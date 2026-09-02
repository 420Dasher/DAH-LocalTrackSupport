using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SpotifyTrackHonorific.Spotify;

internal sealed class SpotifyApiService : IDisposable
{
    private static readonly Uri RedirectUri = new("http://127.0.0.1:5000/callback");
    private const string AuthorizeEndpoint = "https://accounts.spotify.com/authorize";
    private const string TokenEndpoint = "https://accounts.spotify.com/api/token";
    private const string CurrentlyPlayingEndpoint = "https://api.spotify.com/v1/me/player/currently-playing?additional_types=track";
    private const string RequiredScope = "user-read-currently-playing";

    private readonly HttpClient http = new();
    private readonly Configuration config;
    private readonly Action saveConfig;

    private string? accessToken;
    private DateTimeOffset accessTokenExpiresAt = DateTimeOffset.MinValue;

    public SpotifyApiService(Configuration config, Action saveConfig)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        http.Timeout = TimeSpan.FromSeconds(15);
    }

    public bool HasRefreshToken => !string.IsNullOrWhiteSpace(config.SpotifyClientId) && !string.IsNullOrWhiteSpace(config.SpotifyRefreshToken);
    public string RedirectUriText => RedirectUri.ToString();

    public async Task AuthenticateAsync(string clientId, Action<Uri> openBrowser, CancellationToken cancellationToken)
    {
        clientId = clientId.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("Spotify Client ID is empty.", nameof(clientId));

        var verifier = CreatePkceVerifier();
        var challenge = CreatePkceChallenge(verifier);
        var state = CreateState();

        using var callback = new SpotifyAuthCallbackServer(RedirectUri);
        callback.Start();

        var authorizeUri = BuildAuthorizeUri(clientId, challenge, state);
        openBrowser(authorizeUri);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));

        var context = await callback.WaitForCallbackAsync(timeout.Token).ConfigureAwait(false);
        var error = context.Request.QueryString["error"];
        var code = context.Request.QueryString["code"];
        var returnedState = context.Request.QueryString["state"];

        if (!string.Equals(returnedState, state, StringComparison.Ordinal))
        {
            await SpotifyAuthCallbackServer.ReplyAsync(context, false, "Authentication failed: state mismatch.").ConfigureAwait(false);
            throw new InvalidOperationException("Spotify authentication state mismatch.");
        }

        if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code))
        {
            var reason = error ?? "no authorization code was returned";
            await SpotifyAuthCallbackServer.ReplyAsync(context, false, $"Authentication failed: {reason}.").ConfigureAwait(false);
            throw new InvalidOperationException($"Spotify authentication failed: {reason}.");
        }

        try
        {
            var token = await RequestTokenAsync(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri.ToString(),
                ["code_verifier"] = verifier
            }, timeout.Token).ConfigureAwait(false);

            config.SpotifyClientId = clientId;
            ApplyToken(token, isNewAuthorization: true);
            await SpotifyAuthCallbackServer.ReplyAsync(context, true, "Spotify authentication succeeded.").ConfigureAwait(false);
        }
        catch
        {
            await SpotifyAuthCallbackServer.ReplyAsync(context, false, "Spotify authentication failed while exchanging the authorization code.").ConfigureAwait(false);
            throw;
        }
    }

    public async Task<SpotifyPollResult> PollCurrentlyPlayingAsync(CancellationToken cancellationToken)
    {
        if (!HasRefreshToken)
            return new SpotifyPollResult(SpotifyPollState.NotAuthenticated);

        try
        {
            if (!await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false))
                return new SpotifyPollResult(SpotifyPollState.NotAuthenticated, Error: "Spotify authorization expired or was revoked. Re-authenticate.");

            var response = await RequestCurrentlyPlayingAsync(cancellationToken).ConfigureAwait(false);

            // The access token may be revoked between our expiry check and the API
            // request. Force exactly one refresh + retry. If Spotify still rejects a
            // freshly issued access token, stop looping and require a clean re-auth.
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                ClearCachedAccessToken();

                if (!await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false))
                    return new SpotifyPollResult(SpotifyPollState.NotAuthenticated, Error: "Spotify authorization expired or was revoked. Re-authenticate.");

                response = await RequestCurrentlyPlayingAsync(cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    response.Dispose();
                    ClearStoredAuthorization();
                    return new SpotifyPollResult(SpotifyPollState.NotAuthenticated, Error: "Spotify rejected a freshly refreshed access token. Please authenticate again.");
                }
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NoContent)
                    return new SpotifyPollResult(SpotifyPollState.NotPlaying);

                if ((int)response.StatusCode == 429)
                {
                    var retryAfter = GetRetryAfterSeconds(response, 5);
                    return new SpotifyPollResult(SpotifyPollState.RateLimited, RetryAfterSeconds: retryAfter, Error: "Spotify rate limit reached.");
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var error = BuildHttpError("Spotify playback request", response.StatusCode, body);
                    if (IsTransientStatus(response.StatusCode))
                    {
                        return new SpotifyPollResult(
                            SpotifyPollState.TransientError,
                            RetryAfterSeconds: GetRetryAfterSeconds(response, 0),
                            Error: error);
                    }

                    return new SpotifyPollResult(SpotifyPollState.Error, Error: error);
                }

                if (string.IsNullOrWhiteSpace(body))
                    return new SpotifyPollResult(SpotifyPollState.NotPlaying);

                var playback = JsonSerializer.Deserialize<SpotifyPlaybackResponse>(body);
                if (playback == null || playback.Item == null ||
                    !string.Equals(playback.CurrentlyPlayingType, "track", StringComparison.OrdinalIgnoreCase))
                    return new SpotifyPollResult(SpotifyPollState.NotPlaying);

                var track = SpotifyTrackInfo.FromApi(playback.Item, playback.ProgressMs ?? 0);
                return playback.IsPlaying
                    ? new SpotifyPollResult(SpotifyPollState.PlayingTrack, Track: track)
                    : new SpotifyPollResult(SpotifyPollState.PausedTrack, Track: track);
            }
        }
        catch (SpotifyTokenException ex) when (ex.IsRateLimited)
        {
            return new SpotifyPollResult(
                SpotifyPollState.RateLimited,
                RetryAfterSeconds: Math.Max(1, ex.RetryAfterSeconds),
                Error: ex.Message);
        }
        catch (SpotifyTokenException ex) when (ex.IsTransient)
        {
            return new SpotifyPollResult(
                SpotifyPollState.TransientError,
                RetryAfterSeconds: ex.RetryAfterSeconds,
                Error: ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return new SpotifyPollResult(SpotifyPollState.TransientError, Error: $"Spotify network error: {ex.Message}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new SpotifyPollResult(SpotifyPollState.TransientError, Error: "Spotify request timed out.");
        }
        catch (JsonException ex)
        {
            return new SpotifyPollResult(SpotifyPollState.TransientError, Error: $"Spotify returned an unreadable response: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new SpotifyPollResult(SpotifyPollState.Error, Error: ex.Message);
        }
    }

    private async Task<bool> EnsureAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(accessToken) && DateTimeOffset.UtcNow < accessTokenExpiresAt.Subtract(TimeSpan.FromMinutes(2)))
            return true;

        if (!HasRefreshToken)
            return false;

        try
        {
            var token = await RequestTokenAsync(new Dictionary<string, string>
            {
                ["client_id"] = config.SpotifyClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = config.SpotifyRefreshToken
            }, cancellationToken).ConfigureAwait(false);

            ApplyToken(token, isNewAuthorization: false);
            return true;
        }
        catch (SpotifyTokenException ex) when (string.Equals(ex.ErrorCode, "invalid_grant", StringComparison.OrdinalIgnoreCase))
        {
            // Spotify refresh tokens can be revoked. Preserve the Client ID for a
            // one-click re-auth, but stop retrying the dead refresh token forever.
            ClearStoredAuthorization();
            return false;
        }
    }

    private async Task<HttpResponseMessage> RequestCurrentlyPlayingAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, CurrentlyPlayingEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SpotifyTokenResponse> RequestTokenAsync(Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(fields);
        using var response = await http.PostAsync(TokenEndpoint, content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string? errorCode = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var error))
                    errorCode = error.GetString();
            }
            catch
            {
                // Keep the summarized raw response in the exception below.
            }

            throw new SpotifyTokenException(
                errorCode,
                response.StatusCode,
                GetRetryAfterSeconds(response, 0),
                BuildHttpError("Spotify token request", response.StatusCode, body));
        }

        var token = JsonSerializer.Deserialize<SpotifyTokenResponse>(body)
            ?? throw new InvalidOperationException("Spotify returned an empty token response.");

        if (string.IsNullOrWhiteSpace(token.AccessToken))
            throw new InvalidOperationException("Spotify token response did not contain an access token.");

        return token;
    }

    private void ApplyToken(SpotifyTokenResponse token, bool isNewAuthorization)
    {
        accessToken = token.AccessToken;
        var expires = token.ExpiresIn > 0 ? token.ExpiresIn : 3600;
        accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expires);

        if (!string.IsNullOrWhiteSpace(token.RefreshToken))
            config.SpotifyRefreshToken = token.RefreshToken;

        if (isNewAuthorization)
            config.SpotifyAuthorizedAtUtc = DateTime.UtcNow;

        saveConfig();
    }

    private void ClearCachedAccessToken()
    {
        accessToken = null;
        accessTokenExpiresAt = DateTimeOffset.MinValue;
    }

    public void ForgetAuthorization(bool clearClientId = false)
    {
        config.SpotifyRefreshToken = string.Empty;
        config.SpotifyAuthorizedAtUtc = DateTime.MinValue;
        if (clearClientId)
            config.SpotifyClientId = string.Empty;

        ClearCachedAccessToken();
        saveConfig();
    }

    private void ClearStoredAuthorization()
    {
        ForgetAuthorization(clearClientId: false);
    }

    private static bool IsTransientStatus(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code == 408 || code == 425 || code == 429 || code >= 500;
    }

    private static int GetRetryAfterSeconds(HttpResponseMessage response, int fallback)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return ClampRetryAfter((int)Math.Ceiling(delta.TotalSeconds), fallback);

        if (retryAfter?.Date is { } date)
        {
            var seconds = (int)Math.Ceiling((date - DateTimeOffset.UtcNow).TotalSeconds);
            return ClampRetryAfter(seconds, fallback);
        }

        return Math.Max(0, fallback);
    }

    private static int ClampRetryAfter(int seconds, int fallback)
    {
        if (seconds <= 0)
            seconds = Math.Max(1, fallback);
        return Math.Clamp(seconds, 1, 3600);
    }

    private static string BuildHttpError(string operation, HttpStatusCode statusCode, string body)
    {
        var summary = SummarizeBody(body);
        return string.IsNullOrWhiteSpace(summary)
            ? $"{operation} returned HTTP {(int)statusCode} ({statusCode})."
            : $"{operation} returned HTTP {(int)statusCode} ({statusCode}): {summary}";
    }

    private static string SummarizeBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        var oneLine = body.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= 300 ? oneLine : oneLine[..300] + "...";
    }

    private static Uri BuildAuthorizeUri(string clientId, string challenge, string state)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = RedirectUri.ToString(),
            ["scope"] = RequiredScope,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = challenge,
            ["state"] = state
        };

        var builder = new StringBuilder(AuthorizeEndpoint).Append('?');
        var first = true;
        foreach (var (key, value) in query)
        {
            if (!first) builder.Append('&');
            first = false;
            builder.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
        }
        return new Uri(builder.ToString());
    }

    private static string CreatePkceVerifier() => Base64Url(RandomNumberGenerator.GetBytes(64));

    private static string CreatePkceChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64Url(hash);
    }

    private static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void Dispose() => http.Dispose();

    private sealed class SpotifyTokenException : Exception
    {
        public string? ErrorCode { get; }
        public HttpStatusCode StatusCode { get; }
        public int RetryAfterSeconds { get; }
        public bool IsRateLimited => (int)StatusCode == 429;
        public bool IsTransient => IsTransientStatus(StatusCode);

        public SpotifyTokenException(string? errorCode, HttpStatusCode statusCode, int retryAfterSeconds, string message)
            : base(message)
        {
            ErrorCode = errorCode;
            StatusCode = statusCode;
            RetryAfterSeconds = retryAfterSeconds;
        }
    }
}
