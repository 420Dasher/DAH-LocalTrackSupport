using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace SpotifyTrackHonorific.Spotify;

internal sealed class SpotifyTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
}

internal sealed class SpotifyPlaybackResponse
{
    [JsonPropertyName("is_playing")]
    public bool IsPlaying { get; set; }

    [JsonPropertyName("progress_ms")]
    public int? ProgressMs { get; set; }

    [JsonPropertyName("currently_playing_type")]
    public string? CurrentlyPlayingType { get; set; }

    [JsonPropertyName("item")]
    public SpotifyTrack? Item { get; set; }
}

internal sealed class SpotifyTrack
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("uri")]
    public string? Uri { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("duration_ms")]
    public int DurationMs { get; set; }

    [JsonPropertyName("is_local")]
    public bool IsLocal { get; set; }

    [JsonPropertyName("artists")]
    public List<SpotifyArtist>? Artists { get; set; }

    [JsonPropertyName("album")]
    public SpotifyAlbum? Album { get; set; }
}

internal sealed class SpotifyArtist
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class SpotifyAlbum
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed record SpotifyTrackInfo(
    string Name,
    IReadOnlyList<string> Artists,
    string Album,
    int DurationMs,
    int ProgressMs,
    bool IsLocal,
    string Fingerprint)
{
    public string ArtistText => Artists.Count == 0 ? "Unknown Artist" : string.Join(", ", Artists);

    public static SpotifyTrackInfo FromApi(SpotifyTrack track, int progressMs)
    {
        var name = string.IsNullOrWhiteSpace(track.Name) ? "Unknown Track" : track.Name.Trim();
        var artists = track.Artists?
            .Select(x => x.Name?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToArray() ?? Array.Empty<string>();
        var album = track.Album?.Name?.Trim() ?? string.Empty;

        // Local tracks can have a null/empty normal Spotify ID. Prefer URI where
        // possible; otherwise fingerprint the metadata so local->local changes
        // are still detected reliably.
        string fingerprint;
        if (!string.IsNullOrWhiteSpace(track.Uri))
        {
            fingerprint = $"{(track.IsLocal ? "local" : "spotify")}|uri|{track.Uri}";
        }
        else if (!string.IsNullOrWhiteSpace(track.Id))
        {
            fingerprint = $"{(track.IsLocal ? "local" : "spotify")}|id|{track.Id}";
        }
        else
        {
            fingerprint = $"{(track.IsLocal ? "local" : "spotify")}|meta|{name}|{string.Join("\u001f", artists)}|{album}|{track.DurationMs}";
        }

        return new SpotifyTrackInfo(
            name,
            artists,
            album,
            Math.Max(0, track.DurationMs),
            Math.Max(0, progressMs),
            track.IsLocal,
            fingerprint);
    }
}

internal enum SpotifyPollState
{
    PlayingTrack,
    PausedTrack,
    NotPlaying,
    NotAuthenticated,
    RateLimited,
    TransientError,
    Error
}

internal sealed record SpotifyPollResult(
    SpotifyPollState State,
    SpotifyTrackInfo? Track = null,
    int RetryAfterSeconds = 0,
    string? Error = null);
