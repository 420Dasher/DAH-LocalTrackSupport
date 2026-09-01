using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace DiscordActivityHonorific.Configs;

[Serializable]
public class Config : IPluginConfiguration
{
    public static readonly int CURRENT_VERSION = 1;

    public int Version { get; set; } = CURRENT_VERSION;
    public bool Enabled { get; set; } = true;
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    public bool IsHonorificSupporter { get; set; } = false;

    // Optional Spotify Web API fallback. This is deliberately separate from the
    // normal Discord activity path so existing behaviour stays unchanged.
    public bool SpotifyLocalFallbackEnabled { get; set; } = false;
    public string SpotifyClientId { get; set; } = string.Empty;
    public string SpotifyRefreshToken { get; set; } = string.Empty;
    public DateTime LastSpotifyAuthTime { get; set; } = DateTime.MinValue;
    public bool SpotifyDebugLogging { get; set; } = false;

    public List<ActivityConfig> ActivityConfigs { get; set; } = [];
}
