using Dalamud.Configuration;
using System;
using System.Numerics;

namespace SpotifyTrackHonorific;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public const string DefaultTitleFormat = "♪ {artist} - {track}";

    public int Version { get; set; } = 7;

    public bool Enabled { get; set; } = true;
    public bool ShowNormalTracks { get; set; } = true;
    public bool ShowLocalTracks { get; set; } = true;
    public bool ClearOnPause { get; set; } = true;
    public bool IsPrefix { get; set; } = false;
    public string TitleFormat { get; set; } = DefaultTitleFormat;

    // v3 title-cleanup/presentation options.
    public bool StripBracketedTrackParts { get; set; } = false;
    public bool SmartFitLongTitles { get; set; } = true;

    // v4 Honorific appearance options.
    public bool UseTitleColor { get; set; } = false;
    public Vector3 TitleColor { get; set; } = Vector3.One;
    public bool UseTitleGlow { get; set; } = false;
    public Vector3 TitleGlowColor { get; set; } = new Vector3(0.35f, 0.70f, 1.00f);

    // v5/v6 Honorific supporter gradient/animation options. Honorific itself uses a
    // trust-based supporter toggle; this confirmation deliberately does the same.
    public bool HonorificSupporterConfirmed { get; set; } = false;
    public bool UseSupporterGradient { get; set; } = false;

    // Kept under its old serialized name so v0.0.10 configs migrate cleanly. In v6
    // this selects Honorific's custom three-colour gradient payload (-1) vs a preset.
    public bool UseCustomDualGradient { get; set; } = true;
    public int GradientColourSet { get; set; } = 0;

    // v5 compatibility value. v6 uses GradientAnimationStyle so Honorific's enum can
    // be represented directly in a dropdown instead of as a boolean.
    public bool AnimateGradient { get; set; } = true;
    public int GradientAnimationStyle { get; set; } = 1;

    public Vector3 GradientColorA { get; set; } = new Vector3(0.35f, 0.70f, 1.00f);
    public Vector3 GradientColorB { get; set; } = new Vector3(1.00f, 0.35f, 0.75f);
    public Vector3 GradientColorC { get; set; } = new Vector3(0.35f, 0.70f, 1.00f);

    public string SpotifyClientId { get; set; } = string.Empty;
    public string SpotifyRefreshToken { get; set; } = string.Empty;
    public DateTime SpotifyAuthorizedAtUtc { get; set; } = DateTime.MinValue;

    // v7 release-UI onboarding state. Existing authenticated users migrate as completed.
    public bool OnboardingCompleted { get; set; } = false;

    public bool EnsureDefaults()
    {
        var changed = false;

        if (Version < 3)
        {
            StripBracketedTrackParts = false;
            SmartFitLongTitles = true;
            Version = 3;
            changed = true;
        }

        if (Version < 4)
        {
            UseTitleColor = false;
            TitleColor = Vector3.One;
            UseTitleGlow = false;
            TitleGlowColor = new Vector3(0.35f, 0.70f, 1.00f);
            Version = 4;
            changed = true;
        }

        if (Version < 5)
        {
            HonorificSupporterConfirmed = false;
            UseSupporterGradient = false;
            UseCustomDualGradient = true;
            GradientColourSet = 0;
            AnimateGradient = true;
            GradientColorA = new Vector3(0.35f, 0.70f, 1.00f);
            GradientColorB = new Vector3(1.00f, 0.35f, 0.75f);
            Version = 5;
            changed = true;
        }

        if (Version < 6)
        {
            // Carry the v0.0.10 animation checkbox into the enum-backed selector.
            GradientAnimationStyle = AnimateGradient ? 1 : 0;

            // v0.0.10 only supplied two of Honorific's three custom-gradient colour
            // slots. Loop the first colour back into C for a pleasant A -> B -> A
            // default while preserving both colours the user already chose.
            GradientColorC = GradientColorA;
            Version = 6;
            changed = true;
        }

        if (Version < 7)
        {
            // Do not show first-run onboarding to users who already completed Spotify
            // setup in an earlier build. New installs stay false until auth succeeds.
            OnboardingCompleted =
                !string.IsNullOrWhiteSpace(SpotifyClientId) &&
                !string.IsNullOrWhiteSpace(SpotifyRefreshToken);
            Version = 7;
            changed = true;
        }

        // Honorific only applies ordinary glow when a main title colour is present.
        if (UseTitleGlow && !UseTitleColor)
        {
            UseTitleGlow = false;
            changed = true;
        }

        // Never emit supporter-only gradient fields unless the user has explicitly
        // confirmed their entitlement. Turning confirmation off is an immediate gate.
        if (!HonorificSupporterConfirmed && UseSupporterGradient)
        {
            UseSupporterGradient = false;
            changed = true;
        }

        // Gradient styling owns Honorific's Glow field while active, so ordinary glow
        // is disabled to keep the payload unambiguous.
        if (UseSupporterGradient && UseTitleGlow)
        {
            UseTitleGlow = false;
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

        if (string.IsNullOrWhiteSpace(TitleFormat))
        {
            TitleFormat = DefaultTitleFormat;
            changed = true;
        }

        SpotifyClientId ??= string.Empty;
        SpotifyRefreshToken ??= string.Empty;
        TitleFormat ??= DefaultTitleFormat;

        return changed;
    }
}
