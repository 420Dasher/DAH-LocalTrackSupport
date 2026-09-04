using System;
using System.Collections.Generic;

namespace DiscordActivityHonorific.Updaters;

/// <summary>
/// Small compatibility model that exposes the SpotifyGame-style fields used by
/// DiscordActivityHonorific's existing Scriban templates, but is populated from
/// Spotify Web API metadata for a local file.
/// </summary>
public sealed class SpotifyLocalActivity
{
    public string Name { get; init; } = "Spotify";
    public string TrackTitle { get; init; } = string.Empty;
    public IReadOnlyList<string> Artists { get; init; } = Array.Empty<string>();
    public string AlbumTitle { get; init; } = string.Empty;
    public string AlbumArtUrl { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public TimeSpan Elapsed { get; init; }
    public TimeSpan Remaining { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndsAt { get; init; }
    public string TrackId { get; init; } = string.Empty;
    public string TrackUrl { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public bool IsLocal { get; init; } = true;
    public string Details => TrackTitle;
}
