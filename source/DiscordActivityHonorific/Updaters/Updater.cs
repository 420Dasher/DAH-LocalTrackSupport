using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Discord;
using Discord.WebSocket;
using DiscordActivityHonorific.Configs;
using DiscordActivityHonorific.Interop;
using DiscordActivityHonorific.Utils;
using Newtonsoft.Json;
using Scriban;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordActivityHonorific.Updaters;

public class Updater : IDisposable
{
    private const int UPDATE_THROTTLE_MS = 100;
    private const int SPOTIFY_POLL_INTERVAL_MS = 2000;

    private IChatGui ChatGui { get; init; }
    private Config Config { get; init; }
    private IFramework Framework { get; init; }
    private IDalamudPluginInterface PluginInterface { get; init; }
    private IPluginLog PluginLog { get; init; }

    private ICallGateSubscriber<int, string, object> SetCharacterTitleSubscriber { get; init; }
    private ICallGateSubscriber<int, object> ClearCharacterTitleSubscriber { get; init; }

    // Discord and Spotify-local are kept as independent title sources. The source
    // with the highest configured priority wins. On equal priority, the local-file
    // source wins because it contains the metadata Discord is missing.
    private Action? DiscordUpdateTitle { get; set; }
    private int? DiscordUpdatePriority { get; set; }
    private Action? SpotifyLocalUpdateTitle { get; set; }
    private int? SpotifyLocalUpdatePriority { get; set; }

    private string? UpdatedTitleJson { get; set; }
    private UpdaterContext UpdaterContext { get; init; } = new();
    private bool DisplayedMaxLengthError { get; set; } = false;

    private double DeltaSinceLastUpdateMs { get; set; } = 0;
    private double DeltaSinceLastSpotifyPollMs { get; set; } = 0;
    private bool SpotifyPollInProgress { get; set; } = false;
    private string? CurrentSpotifyLocalTrackKey { get; set; }
    private SpotifyClient? Spotify { get; set; }
    private string? CurrentSpotifyAccessToken { get; set; }
    private PKCECallbackActivator? SpotifyAuthServer { get; set; }

    private DiscordSocketClient DiscordSocketClient { get; init; } = new(new()
    {
        GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildPresences,
        LogLevel = LogSeverity.Verbose,
    });

    public Updater(IChatGui chatGui, Config config, IFramework framwork, IDalamudPluginInterface pluginInterface, IPluginLog pluginLog)
    {
        ChatGui = chatGui;
        Config = config;
        Framework = framwork;
        PluginInterface = pluginInterface;
        PluginLog = pluginLog;
        SetCharacterTitleSubscriber = PluginInterface.GetIpcSubscriber<int, string, object>("Honorific.SetCharacterTitle");
        ClearCharacterTitleSubscriber = PluginInterface.GetIpcSubscriber<int, object>("Honorific.ClearCharacterTitle");

        DiscordSocketClient.Log += Log;
        DiscordSocketClient.PresenceUpdated += PresenceUpdated;

        if (Config.Enabled) Start();
        Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        SpotifyAuthServer?.Dispose();
        SpotifyAuthServer = null;
        Framework.RunOnFrameworkThread(() =>
        {
            ClearCharacterTitleSubscriber.InvokeAction(0);
        });
        DiscordSocketClient.Dispose();
        GC.SuppressFinalize(this);
    }

    public Task Toggle(bool value)
    {
        return value ? Start() : Stop();
    }

    public Task Restart()
    {
        return Stop().ContinueWith(t => Start());
    }

    public Task Start()
    {
        if (!Config.Token.IsNullOrWhitespace() && State() != ConnectionState.Connected)
        {
            var task = DiscordSocketClient.LoginAsync(TokenType.Bot, Config.Token);
            DiscordSocketClient.StartAsync();
            return task;
        }
        else
        {
            return Task.CompletedTask;
        }
    }

    public Task Stop()
    {
        return DiscordSocketClient.LogoutAsync().ContinueWith(t =>
        {
            DiscordSocketClient.StopAsync();
            ClearTitle();
        });
    }

    public ConnectionState State() => DiscordSocketClient.ConnectionState;

    public string GetSpotifyStatus()
    {
        var auth = string.IsNullOrWhiteSpace(Config.SpotifyRefreshToken) ? "not authenticated" : "authenticated";
        var enabled = Config.SpotifyLocalFallbackEnabled ? "enabled" : "disabled";
        var clientId = string.IsNullOrWhiteSpace(Config.SpotifyClientId) ? "missing Client ID" : "Client ID set";
        return $"Spotify local-file fallback: {enabled}, {auth}, {clientId}.";
    }

    public void DisableSpotifyLocalFallback()
    {
        RemoveSpotifyLocalSource();
        Spotify = null;
        CurrentSpotifyAccessToken = null;
    }

    public async Task AuthenticateSpotify(string clientId)
    {
        try
        {
            clientId = clientId.Trim();
            if (string.IsNullOrWhiteSpace(clientId))
            {
                PrintSpotifyMessage("Spotify Client ID is empty.", true);
                return;
            }

            Config.SpotifyClientId = clientId;
            PluginInterface.SavePluginConfig(Config);

            var serverUri = new Uri("http://127.0.0.1:5000/");
            SpotifyAuthServer?.Dispose();
            SpotifyAuthServer = new PKCECallbackActivator(serverUri, "callback");
            await SpotifyAuthServer.Start().ConfigureAwait(false);

            var (verifier, challenge) = PKCEUtil.GenerateCodes();
            var loginRequest = new LoginRequest(SpotifyAuthServer.RedirectUri, clientId, LoginRequest.ResponseType.Code)
            {
                CodeChallenge = challenge,
                CodeChallengeMethod = "S256",
                Scope = new[] { Scopes.UserReadCurrentlyPlaying, Scopes.UserReadPlaybackState }
            };

            PrintSpotifyMessage("Opening Spotify authentication in your browser...");
            BrowserUtil.Open(loginRequest.ToUri());

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var context = await SpotifyAuthServer.ReceiveContext(timeoutCts.Token).ConfigureAwait(false);
            var code = context.Request.QueryString["code"];
            var error = context.Request.QueryString["error"];

            if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code))
            {
                await WriteAuthResponse(context, false).ConfigureAwait(false);
                PrintSpotifyMessage($"Spotify authentication failed: {error ?? "no authorization code received"}.", true);
                return;
            }

            var tokenResponse = await new OAuthClient().RequestToken(
                new PKCETokenRequest(clientId, code, SpotifyAuthServer.RedirectUri, verifier)
            ).ConfigureAwait(false);

            Config.SpotifyRefreshToken = tokenResponse.RefreshToken ?? string.Empty;
            Config.LastSpotifyAuthTime = DateTime.Now;
            Config.SpotifyLocalFallbackEnabled = true;
            PluginInterface.SavePluginConfig(Config);

            CurrentSpotifyAccessToken = tokenResponse.AccessToken;
            Spotify = new SpotifyClient(CurrentSpotifyAccessToken);

            await WriteAuthResponse(context, true).ConfigureAwait(false);
            PrintSpotifyMessage("Spotify authenticated. Local-file fallback is now enabled.");
        }
        catch (TaskCanceledException)
        {
            PrintSpotifyMessage("Spotify authentication timed out. Run spotify-auth again.", true);
        }
        catch (Exception e)
        {
            PluginLog.Error(e, "Spotify authentication failed");
            PrintSpotifyMessage("Spotify authentication failed. Check /xllog for details.", true);
        }
        finally
        {
            SpotifyAuthServer?.Dispose();
            SpotifyAuthServer = null;
        }
    }

    private static async Task WriteAuthResponse(System.Net.HttpListenerContext context, bool success)
    {
        try
        {
            var html = success
                ? "<html><body><h2>DAH-LocalSpotifySupport</h2><p>Spotify authentication succeeded. You can close this tab and return to FFXIV.</p></body></html>"
                : "<html><body><h2>DAH-LocalSpotifySupport</h2><p>Spotify authentication failed. You can close this tab and try again in FFXIV.</p></body></html>";
            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.StatusCode = success ? 200 : 400;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            context.Response.Close();
        }
        catch
        {
            // The browser may close the callback tab before the response is written.
        }
    }

    private Task PresenceUpdated(SocketUser socketUser, SocketPresence oldPresence, SocketPresence newPresence)
    {
        try
        {
            PluginLog.Debug($"PresenceUpdated for user '{socketUser.Username}':\n{JsonConvert.SerializeObject(newPresence, Formatting.Indented)}");
            if (Config.Username.IsNullOrWhitespace() || Config.Username == socketUser.Username)
            {
                DiscordUpdateTitle = null;
                DiscordUpdatePriority = null;

                foreach (var activityConfig in Config.ActivityConfigs.Where(c => c.Enabled).OrderByDescending(c => c.Priority))
                {
                    var resolvedType = activityConfig.ResolveType();
                    if (resolvedType == null) continue;

                    var activity = newPresence.Activities.FirstOrDefault(activity => activity.GetType().IsAssignableTo(resolvedType));
                    if (activity != null)
                    {
                        var matchFilter = true;
                        if (!activityConfig.FilterTemplate.IsNullOrWhitespace())
                        {
                            var filterTemplate = Template.Parse(activityConfig.FilterTemplate);
                            var filter = filterTemplate.Render(new { Activity = activity, Context = UpdaterContext }, member => member.Name);
                            if (bool.TryParse(filter, out var parsedFilter))
                            {
                                matchFilter = parsedFilter;
                            }
                            else
                            {
                                PluginLog.Warning($"Unable to parse filter '{filter}' as boolean, skipping result");
                                matchFilter = false;
                            }
                        }

                        if (matchFilter)
                        {
                            UpdaterContext.SecsElapsed = 0;
                            DiscordUpdatePriority = activityConfig.Priority;
                            DiscordUpdateTitle = CreateTitleUpdateAction(activityConfig, activity, false);
                            return Task.CompletedTask;
                        }
                    }
                }

                if (SpotifyLocalUpdateTitle == null && UpdatedTitleJson != null) ClearRenderedTitle();
            }
            else
            {
                PluginLog.Debug($"Ignored PresenceUpdated for '{socketUser.Username}' since it doesn't match explictely configured username: '{Config.Username}'");
            }
        }
        catch (Exception e)
        {
            PluginLog.Warning(e.ToString());
        }
        return Task.CompletedTask;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var deltaMs = framework.UpdateDelta.TotalMilliseconds;
        DeltaSinceLastUpdateMs += deltaMs;
        UpdaterContext.SecsElapsed += framework.UpdateDelta.TotalSeconds;

        if (Config.Enabled && Config.SpotifyLocalFallbackEnabled &&
            !string.IsNullOrWhiteSpace(Config.SpotifyClientId) &&
            !string.IsNullOrWhiteSpace(Config.SpotifyRefreshToken))
        {
            DeltaSinceLastSpotifyPollMs += deltaMs;
            if (DeltaSinceLastSpotifyPollMs >= SPOTIFY_POLL_INTERVAL_MS && !SpotifyPollInProgress)
            {
                DeltaSinceLastSpotifyPollMs = 0;
                _ = PollSpotifyLocalFallback();
            }
        }
        else if (SpotifyLocalUpdateTitle != null)
        {
            RemoveSpotifyLocalSource();
        }

        var effectiveUpdate = GetEffectiveUpdateTitle();
        if (!Config.Enabled || effectiveUpdate == null) return;

        if (DeltaSinceLastUpdateMs > UPDATE_THROTTLE_MS)
        {
            DeltaSinceLastUpdateMs = 0;
            try
            {
                Task.Run(effectiveUpdate);
            }
            catch (Exception e)
            {
                PluginLog.Warning(e.ToString());
            }
        }
    }

    private Action? GetEffectiveUpdateTitle()
    {
        if (SpotifyLocalUpdateTitle != null &&
            (!DiscordUpdatePriority.HasValue || SpotifyLocalUpdatePriority.GetValueOrDefault() >= DiscordUpdatePriority.Value))
        {
            return SpotifyLocalUpdateTitle;
        }

        return DiscordUpdateTitle;
    }

    private async Task PollSpotifyLocalFallback()
    {
        if (SpotifyPollInProgress) return;
        SpotifyPollInProgress = true;

        try
        {
            var spotify = await GetSpotifyClient().ConfigureAwait(false);
            if (spotify == null)
            {
                RemoveSpotifyLocalSource();
                return;
            }

            var currentlyPlaying = await spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest()).ConfigureAwait(false);
            if (currentlyPlaying != null && currentlyPlaying.IsPlaying && currentlyPlaying.Item is FullTrack track && track.IsLocal)
            {
                ApplySpotifyLocalTrack(track, currentlyPlaying.ProgressMs ?? 0);
            }
            else
            {
                RemoveSpotifyLocalSource();
            }
        }
        catch (APIException e)
        {
            PluginLog.Warning(e, "Spotify local-file fallback API request failed");
            Spotify = null;
            CurrentSpotifyAccessToken = null;
            RemoveSpotifyLocalSource();
        }
        catch (Exception e)
        {
            PluginLog.Warning(e, "Spotify local-file fallback poll failed");
            RemoveSpotifyLocalSource();
        }
        finally
        {
            SpotifyPollInProgress = false;
        }
    }

    private void ApplySpotifyLocalTrack(FullTrack track, int progressMs)
    {
        var activityConfig = Config.ActivityConfigs
            .Where(c => c.Enabled && c.TypeName == nameof(SpotifyGame))
            .OrderByDescending(c => c.Priority)
            .FirstOrDefault();

        if (activityConfig == null)
        {
            if (Config.SpotifyDebugLogging) PluginLog.Debug("Spotify local track found, but no enabled SpotifyGame activity configuration exists.");
            RemoveSpotifyLocalSource();
            return;
        }

        if (activityConfig.TitleDataConfig == null)
        {
            activityConfig.TitleDataConfig = new();
            PluginInterface.SavePluginConfig(Config);
        }

        var key = GetLocalTrackKey(track);
        if (key == CurrentSpotifyLocalTrackKey && SpotifyLocalUpdateTitle != null) return;

        var safeProgressMs = Math.Max(0, progressMs);
        var safeDurationMs = Math.Max(0, track.DurationMs);
        var elapsed = TimeSpan.FromMilliseconds(Math.Min(safeProgressMs, safeDurationMs));
        var duration = TimeSpan.FromMilliseconds(safeDurationMs);
        var remaining = duration > elapsed ? duration - elapsed : TimeSpan.Zero;
        var now = DateTimeOffset.Now;

        var trackUrl = string.Empty;
        if (track.ExternalUrls != null && track.ExternalUrls.TryGetValue("spotify", out var externalUrl))
        {
            trackUrl = externalUrl ?? string.Empty;
        }

        var activity = new SpotifyLocalActivity
        {
            TrackTitle = track.Name ?? string.Empty,
            Artists = track.Artists?.Select(a => a.Name ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray() ?? Array.Empty<string>(),
            AlbumTitle = track.Album?.Name ?? string.Empty,
            AlbumArtUrl = track.Album?.Images?.FirstOrDefault()?.Url ?? string.Empty,
            Duration = duration,
            Elapsed = elapsed,
            Remaining = remaining,
            StartedAt = now - elapsed,
            EndsAt = now + remaining,
            TrackId = track.Id ?? string.Empty,
            TrackUrl = trackUrl,
            IsLocal = true
        };

        var matchFilter = true;
        if (!activityConfig.FilterTemplate.IsNullOrWhitespace())
        {
            var filterTemplate = Template.Parse(activityConfig.FilterTemplate);
            var filter = filterTemplate.Render(new { Activity = activity, Context = UpdaterContext }, member => member.Name);
            if (bool.TryParse(filter, out var parsedFilter))
            {
                matchFilter = parsedFilter;
            }
            else
            {
                PluginLog.Warning($"Unable to parse Spotify local fallback filter '{filter}' as boolean, skipping result");
                matchFilter = false;
            }
        }

        if (!matchFilter)
        {
            RemoveSpotifyLocalSource();
            return;
        }

        CurrentSpotifyLocalTrackKey = key;
        SpotifyLocalUpdatePriority = activityConfig.Priority;
        UpdaterContext.SecsElapsed = 0;
        SpotifyLocalUpdateTitle = CreateTitleUpdateAction(activityConfig, activity, true);

        if (Config.SpotifyDebugLogging)
        {
            PluginLog.Debug($"Spotify local fallback selected: '{activity.TrackTitle}' by '{string.Join(", ", activity.Artists)}'");
        }
    }

    private static string GetLocalTrackKey(FullTrack track)
    {
        // Spotify local files frequently have no regular Spotify track ID. URI is
        // preferred; metadata is the fallback so consecutive local files still
        // trigger an update instead of all looking like the same null ID.
        if (!string.IsNullOrWhiteSpace(track.Uri)) return track.Uri;

        var artists = track.Artists == null ? string.Empty : string.Join("\u001f", track.Artists.Select(a => a.Name ?? string.Empty));
        return $"local\u001f{track.Name}\u001f{artists}\u001f{track.Album?.Name}\u001f{track.DurationMs}";
    }

    private Action CreateTitleUpdateAction(ActivityConfig activityConfig, object activity, bool spotifyLocalSource)
    {
        return () =>
        {
            if (!Config.Enabled || !activityConfig.Enabled || activityConfig.TitleDataConfig == null ||
                (spotifyLocalSource && !Config.SpotifyLocalFallbackEnabled))
            {
                if (spotifyLocalSource) RemoveSpotifyLocalSource();
                else ClearTitle();
                return;
            }

            RenderAndSetTitle(activityConfig, activity);
        };
    }

    private void RenderAndSetTitle(ActivityConfig activityConfig, object activity)
    {
        var titleTemplate = Template.Parse(activityConfig.TitleTemplate);
        var title = titleTemplate.Render(new { Activity = activity, Context = UpdaterContext }, member => member.Name);
        if (title.Length > Constraint.MaxTitleLength)
        {
            if (!DisplayedMaxLengthError)
            {
                var message = $"Title '{title}' is longer than {Constraint.MaxTitleLength} characters, it won't be applied by honorific. Trim whitespaces or truncate variables to reduce the length.";
                PluginLog.Warning(message);
                Framework.RunOnFrameworkThread(() => ChatGui.PrintError(message, "DAH-LocalSpotifySupport"));
                DisplayedMaxLengthError = true;
            }
            return;
        }

        DisplayedMaxLengthError = false;
        var titleData = activityConfig.TitleDataConfig!.ToTitleData(title, Config.IsHonorificSupporter);
        var serializedData = JsonConvert.SerializeObject(titleData, Formatting.Indented);
        if (serializedData == UpdatedTitleJson) return;

        Framework.RunOnFrameworkThread(() =>
        {
            PluginLog.Debug($"Call Honorific SetCharacterTitle IPC with:\n{serializedData}");
            SetCharacterTitleSubscriber.InvokeAction(0, serializedData);
        });
        UpdatedTitleJson = serializedData;
    }

    private async Task<SpotifyClient?> GetSpotifyClient()
    {
        if (string.IsNullOrWhiteSpace(Config.SpotifyRefreshToken) || string.IsNullOrWhiteSpace(Config.SpotifyClientId))
        {
            return null;
        }

        if (Spotify != null && CurrentSpotifyAccessToken != null && Config.LastSpotifyAuthTime.AddMinutes(50) > DateTime.Now)
        {
            return Spotify;
        }

        if (Config.SpotifyDebugLogging) PluginLog.Debug("Refreshing Spotify access token for local-file fallback.");

        try
        {
            var response = await new OAuthClient().RequestToken(
                new PKCETokenRefreshRequest(Config.SpotifyClientId, Config.SpotifyRefreshToken)
            ).ConfigureAwait(false);

            CurrentSpotifyAccessToken = response.AccessToken;
            if (!string.IsNullOrWhiteSpace(response.RefreshToken))
            {
                Config.SpotifyRefreshToken = response.RefreshToken;
            }

            Config.LastSpotifyAuthTime = DateTime.Now;
            PluginInterface.SavePluginConfig(Config);
            Spotify = new SpotifyClient(CurrentSpotifyAccessToken);
            return Spotify;
        }
        catch (Exception e)
        {
            PluginLog.Error(e, "Failed to refresh Spotify token for local-file fallback");
            Spotify = null;
            CurrentSpotifyAccessToken = null;
            return null;
        }
    }

    private void RemoveSpotifyLocalSource()
    {
        var hadLocalSource = SpotifyLocalUpdateTitle != null;
        var localWasEffective = hadLocalSource &&
            (!DiscordUpdatePriority.HasValue || SpotifyLocalUpdatePriority.GetValueOrDefault() >= DiscordUpdatePriority.Value);

        SpotifyLocalUpdateTitle = null;
        SpotifyLocalUpdatePriority = null;
        CurrentSpotifyLocalTrackKey = null;

        if (localWasEffective)
        {
            UpdaterContext.SecsElapsed = 0;
            if (DiscordUpdateTitle == null)
            {
                ClearRenderedTitle();
            }
            else
            {
                // Force the Discord source to be applied on the next update tick.
                UpdatedTitleJson = null;
            }
        }
    }

    private void PrintSpotifyMessage(string message, bool error = false)
    {
        Framework.RunOnFrameworkThread(() =>
        {
            if (error) ChatGui.PrintError(message, "DAH-LocalSpotifySupport");
            else ChatGui.Print(message);
        });
    }

    private Task Log(LogMessage logMessage)
    {
        if (logMessage.Exception != null)
        {
            PluginLog.Warning(logMessage.Exception.ToString());
        }
        else
        {
            PluginLog.Debug(logMessage.Message);
        }

        return Task.CompletedTask;
    }

    private void ClearTitle()
    {
        DiscordUpdateTitle = null;
        DiscordUpdatePriority = null;
        SpotifyLocalUpdateTitle = null;
        SpotifyLocalUpdatePriority = null;
        CurrentSpotifyLocalTrackKey = null;
        ClearRenderedTitle();
    }

    private void ClearRenderedTitle()
    {
        if (UpdatedTitleJson != null)
        {
            PluginLog.Debug("Call Honorific ClearCharacterTitle IPC");
            Framework.RunOnFrameworkThread(() =>
            {
                ClearCharacterTitleSubscriber.InvokeAction(0);
            });
        }

        UpdaterContext.SecsElapsed = 0;
        UpdatedTitleJson = null;
        DisplayedMaxLengthError = false;
    }
}
