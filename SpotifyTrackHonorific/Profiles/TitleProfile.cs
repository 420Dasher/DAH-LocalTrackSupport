using System;
using System.Numerics;

namespace SpotifyTrackHonorific.Profiles;

[Serializable]
public sealed class TitleProfile
{
    public string Name { get; set; } = "Profile";

    public bool ShowNormalTracks { get; set; } = true;
    public bool ShowLocalTracks { get; set; } = true;
    public bool ClearOnPause { get; set; } = true;
    public bool IsPrefix { get; set; } = false;
    public string TitleFormat { get; set; } = Configuration.DefaultTitleFormat;
    public bool StripBracketedTrackParts { get; set; } = false;
    public bool SmartFitLongTitles { get; set; } = true;

    public bool UseTitleColor { get; set; } = false;
    public Vector3 TitleColor { get; set; } = Vector3.One;
    public bool UseTitleGlow { get; set; } = false;
    public Vector3 TitleGlowColor { get; set; } = new(0.35f, 0.70f, 1.00f);

    public bool UseSupporterGradient { get; set; } = false;
    public bool UseCustomDualGradient { get; set; } = true;
    public int GradientColourSet { get; set; } = 0;
    public int GradientAnimationStyle { get; set; } = 1;
    public Vector3 GradientColorA { get; set; } = new(0.35f, 0.70f, 1.00f);
    public Vector3 GradientColorB { get; set; } = new(1.00f, 0.35f, 0.75f);
    public Vector3 GradientColorC { get; set; } = new(0.35f, 0.70f, 1.00f);

    public bool EnableContentFilter { get; set; } = false;
    public bool SmartContentFilterMatching { get; set; } = true;
    public string ContentFilterEntries { get; set; } = string.Empty;
    public int ContentFilterAction { get; set; } = 0;
    public string ContentFilterFallback { get; set; } = Configuration.DefaultContentFilterFallback;
    public bool UseBuiltInContentFilterList { get; set; } = true;
    public string DisabledBuiltInContentFilterEntries { get; set; } = string.Empty;

    public static TitleProfile Capture(Configuration config, string name) => new()
    {
        Name = name,
        ShowNormalTracks = config.ShowNormalTracks,
        ShowLocalTracks = config.ShowLocalTracks,
        ClearOnPause = config.ClearOnPause,
        IsPrefix = config.IsPrefix,
        TitleFormat = config.TitleFormat,
        StripBracketedTrackParts = config.StripBracketedTrackParts,
        SmartFitLongTitles = config.SmartFitLongTitles,
        UseTitleColor = config.UseTitleColor,
        TitleColor = config.TitleColor,
        UseTitleGlow = config.UseTitleGlow,
        TitleGlowColor = config.TitleGlowColor,
        UseSupporterGradient = config.UseSupporterGradient,
        UseCustomDualGradient = config.UseCustomDualGradient,
        GradientColourSet = config.GradientColourSet,
        GradientAnimationStyle = config.GradientAnimationStyle,
        GradientColorA = config.GradientColorA,
        GradientColorB = config.GradientColorB,
        GradientColorC = config.GradientColorC,
        EnableContentFilter = config.EnableContentFilter,
        SmartContentFilterMatching = config.SmartContentFilterMatching,
        ContentFilterEntries = config.ContentFilterEntries,
        ContentFilterAction = config.ContentFilterAction,
        ContentFilterFallback = config.ContentFilterFallback,
        UseBuiltInContentFilterList = config.UseBuiltInContentFilterList,
        DisabledBuiltInContentFilterEntries = config.DisabledBuiltInContentFilterEntries,
    };

    public void ApplyTo(Configuration config)
    {
        config.ShowNormalTracks = ShowNormalTracks;
        config.ShowLocalTracks = ShowLocalTracks;
        config.ClearOnPause = ClearOnPause;
        config.IsPrefix = IsPrefix;
        config.TitleFormat = TitleFormat;
        config.StripBracketedTrackParts = StripBracketedTrackParts;
        config.SmartFitLongTitles = SmartFitLongTitles;

        config.UseTitleColor = UseTitleColor;
        config.TitleColor = TitleColor;
        config.UseTitleGlow = UseTitleGlow;
        config.TitleGlowColor = TitleGlowColor;
        config.UseSupporterGradient = UseSupporterGradient;
        config.UseCustomDualGradient = UseCustomDualGradient;
        config.GradientColourSet = GradientColourSet;
        config.AnimateGradient = GradientAnimationStyle != 0;
        config.GradientAnimationStyle = GradientAnimationStyle;
        config.GradientColorA = GradientColorA;
        config.GradientColorB = GradientColorB;
        config.GradientColorC = GradientColorC;

        config.EnableContentFilter = EnableContentFilter;
        config.SmartContentFilterMatching = SmartContentFilterMatching;
        config.ContentFilterEntries = ContentFilterEntries;
        config.ContentFilterAction = ContentFilterAction;
        config.ContentFilterFallback = ContentFilterFallback;
        config.UseBuiltInContentFilterList = UseBuiltInContentFilterList;
        config.DisabledBuiltInContentFilterEntries = DisabledBuiltInContentFilterEntries;
    }

    public bool EnsureDefaults(int index)
    {
        var changed = false;

        if (string.IsNullOrWhiteSpace(Name))
        {
            Name = $"Profile {Math.Max(1, index + 1)}";
            changed = true;
        }
        else
        {
            var trimmed = Name.Trim();
            if (trimmed.Length > 48)
                trimmed = trimmed[..48];
            if (!string.Equals(trimmed, Name, StringComparison.Ordinal))
            {
                Name = trimmed;
                changed = true;
            }
        }

        if (string.IsNullOrWhiteSpace(TitleFormat))
        {
            TitleFormat = Configuration.DefaultTitleFormat;
            changed = true;
        }

        if (ContentFilterAction < 0 || ContentFilterAction > 2)
        {
            ContentFilterAction = 0;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(ContentFilterFallback))
        {
            ContentFilterFallback = Configuration.DefaultContentFilterFallback;
            changed = true;
        }

        if (GradientColourSet < 0)
        {
            GradientColourSet = 0;
            changed = true;
        }

        if (GradientAnimationStyle < 0)
        {
            GradientAnimationStyle = 0;
            changed = true;
        }

        if (UseTitleGlow && !UseTitleColor)
        {
            UseTitleGlow = false;
            changed = true;
        }

        if (UseSupporterGradient && UseTitleGlow)
        {
            UseTitleGlow = false;
            changed = true;
        }

        TitleFormat ??= Configuration.DefaultTitleFormat;
        ContentFilterEntries ??= string.Empty;
        ContentFilterFallback ??= Configuration.DefaultContentFilterFallback;
        DisabledBuiltInContentFilterEntries ??= string.Empty;
        Name ??= $"Profile {Math.Max(1, index + 1)}";

        return changed;
    }

    public TitleProfile Clone() => (TitleProfile)MemberwiseClone();
}
