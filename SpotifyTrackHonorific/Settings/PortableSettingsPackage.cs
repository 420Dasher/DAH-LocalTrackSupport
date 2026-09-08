using SpotifyTrackHonorific.Profiles;
using System.Collections.Generic;

namespace SpotifyTrackHonorific.Settings;

internal sealed class PortableSettingsPackage
{
    internal const int CurrentFormatVersion = 2;
    internal const string ExpectedSchema = "SpotifyTrackHonorific.PortableSettings";

    public string Schema { get; set; } = ExpectedSchema;
    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string ExportedFromVersion { get; set; } = string.Empty;

    // Combat visibility is global and portable, but intentionally is not part of
    // saved title profiles.
    public bool AutoHideInCombat { get; set; } = false;

    public TitleProfile? CurrentSettings { get; set; }
    public List<TitleProfile> Profiles { get; set; } = new();
}
