using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using SpotifyTrackHonorific.Honorific;
using System;
using System.Numerics;

namespace SpotifyTrackHonorific.UI;

internal sealed class ConfigWindow : Window
{
    private const string RotatingPreset = "» {cycle:10|vibing to music|{track}|{artist}} «";

    private readonly Plugin plugin;
    private string clientIdDraft;
    private bool showOnboardingSetup;
    private bool confirmResetDisplay;
    private bool confirmForgetSpotify;

    public ConfigWindow(Plugin plugin)
        : base("SpotifyTrackHonorific Settings")
    {
        this.plugin = plugin;
        clientIdDraft = plugin.Config.SpotifyClientId;
        Size = new Vector2(680, 720);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    internal void SyncClientId() => clientIdDraft = plugin.Config.SpotifyClientId;

    public override void Draw()
    {
        DrawHeader();

        var config = plugin.Config;
        if (!config.OnboardingCompleted && !showOnboardingSetup)
        {
            DrawWelcome();
            return;
        }

        if (ImGui.BeginTabBar("##sth-main-tabs"))
        {
            if (ImGui.BeginTabItem("Home"))
            {
                DrawHomeTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Title"))
            {
                DrawTitleTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Appearance"))
            {
                DrawAppearanceTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Advanced"))
            {
                DrawAdvancedTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawHeader()
    {
        ImGui.Text($"SpotifyTrackHonorific v{Plugin.DisplayVersion}");
        ImGui.TextDisabled("Spotify music titles for Honorific");
        ImGui.Spacing();

        ImGui.Text($"Spotify: {plugin.SpotifyFriendlyStatus}");
        ImGui.SameLine();
        ImGui.TextDisabled("   |   ");
        ImGui.SameLine();
        ImGui.Text($"Honorific: {(plugin.HonorificDetected ? "Detected" : "Not detected")}");

        ImGui.TextWrapped($"Now playing: {plugin.NowPlayingText}");
        ImGui.Separator();
        ImGui.Spacing();
    }

    private void DrawWelcome()
    {
        ImGui.Text("Welcome to SpotifyTrackHonorific");
        ImGui.Spacing();
        ImGui.TextWrapped("This plugin shows the music you are listening to on Spotify as an Honorific title in FFXIV.");
        ImGui.Spacing();
        ImGui.Text("Setup takes four steps:");
        ImGui.TextWrapped("1. Create a Spotify Developer app and copy its Client ID.");
        ImGui.TextWrapped("2. Add the callback address shown here to that app.");
        ImGui.TextWrapped("3. Connect Spotify in this window.");
        ImGui.TextWrapped("4. Make sure Honorific is installed and enabled, then play some music.");
        ImGui.Spacing();

        if (ImGui.Button("Get Started"))
            showOnboardingSetup = true;

        ImGui.SameLine();
        ImGui.TextDisabled("Your Spotify password is never requested or stored by this plugin.");
    }

    private void DrawHomeTab()
    {
        var config = plugin.Config;

        ImGui.Text("Status");
        ImGui.Separator();

        var enabled = config.Enabled;
        if (ImGui.Checkbox("Enable Spotify title updates", ref enabled))
        {
            config.Enabled = enabled;
            plugin.SettingsChanged();
        }
        HelpMarker("Turn this off to stop Spotify polling and remove this plugin's Honorific title without removing your saved Spotify connection.");

        ImGui.Text($"Spotify: {plugin.SpotifyFriendlyStatus}");
        ImGui.Text($"Honorific: {(plugin.HonorificDetected ? "Detected and ready" : "Not detected - make sure Honorific is installed and enabled")}");
        ImGui.TextWrapped($"Music: {plugin.NowPlayingText}");

        if (!string.IsNullOrWhiteSpace(plugin.ErrorText))
        {
            ImGui.Spacing();
            if (!plugin.IsAuthenticated)
            {
                ImGui.TextWrapped("Spotify connection needs attention. Your saved authorization could no longer be used.");
            }
            else
            {
                ImGui.TextWrapped("Spotify is temporarily unavailable. Your last valid title is kept while the plugin retries automatically.");
            }
        }

        ImGui.Spacing();
        ImGui.Text("Spotify connection");
        ImGui.Separator();
        ImGui.TextWrapped("Spotify requires a Client ID from your own Spotify Developer app. No client secret is needed.");

        ImGui.Text("Spotify app Client ID");
        HelpMarker("Open your Spotify Developer app, copy its Client ID, and paste it here. This identifies the app; it is not your Spotify password.");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##spotify-client-id", ref clientIdDraft, 128);

        ImGui.Spacing();
        ImGui.Text("Callback address to register in Spotify");
        HelpMarker("Spotify redirects your browser back to this local address after you approve access. Add this exact address as a Redirect URI in the Spotify Developer app.");
        ImGui.TextWrapped(plugin.RedirectUriText);
        if (ImGui.Button("Copy callback address"))
            ImGui.SetClipboardText(plugin.RedirectUriText);

        ImGui.Spacing();
        if (plugin.IsAuthenticating)
        {
            ImGui.TextWrapped("Connecting Spotify... Finish the authorization in the browser window that opened.");
        }
        else
        {
            var connectLabel = plugin.IsAuthenticated ? "Reconnect Spotify" : "Connect Spotify";
            if (ImGui.Button(connectLabel))
                plugin.StartAuthentication(clientIdDraft);
        }

        if (plugin.IsAuthenticated)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("Connected");
        }

        if (config.Enabled && plugin.IsAuthenticated && !string.IsNullOrWhiteSpace(plugin.ErrorText))
        {
            ImGui.Spacing();
            if (ImGui.Button("Retry now"))
                plugin.RetrySpotifyNow();
        }

        ImGui.Spacing();
        ImGui.Text("Honorific");
        ImGui.Separator();
        ImGui.TextWrapped("Honorific must be installed and enabled. The plugin sends only your formatted title and appearance settings to Honorific.");
        if (ImGui.Button("Test Honorific title"))
            plugin.TestHonorificTitle();
        ImGui.SameLine();
        ImGui.TextDisabled("Your Spotify title will return on the next successful update.");
    }

    private void DrawTitleTab()
    {
        var config = plugin.Config;

        ImGui.Text("What should be shown?");
        ImGui.Separator();

        var normalTracks = config.ShowNormalTracks;
        if (ImGui.Checkbox("Show regular Spotify tracks", ref normalTracks))
        {
            config.ShowNormalTracks = normalTracks;
            plugin.SettingsChanged();
        }

        var localTracks = config.ShowLocalTracks;
        if (ImGui.Checkbox("Show Spotify local files", ref localTracks))
        {
            config.ShowLocalTracks = localTracks;
            plugin.SettingsChanged();
        }

        var clearOnPause = config.ClearOnPause;
        if (ImGui.Checkbox("Hide the title when playback is paused or stopped", ref clearOnPause))
        {
            config.ClearOnPause = clearOnPause;
            plugin.SettingsChanged();
        }

        ImGui.Spacing();
        ImGui.Text("Quick formats");
        ImGui.Separator();

        if (ImGui.Button("♪ Artist - Track"))
        {
            config.TitleFormat = Configuration.DefaultTitleFormat;
            plugin.SettingsChanged();
        }
        ImGui.SameLine();
        if (ImGui.Button("Track only"))
        {
            config.TitleFormat = "{track}";
            plugin.SettingsChanged();
        }
        ImGui.SameLine();
        if (ImGui.Button("Rotating"))
        {
            config.TitleFormat = RotatingPreset;
            plugin.SettingsChanged();
        }
        HelpMarker("Rotating alternates between 'vibing to music', the track, and the artist every 10 playback seconds.");

        ImGui.Spacing();
        ImGui.Text("Custom title format");
        HelpMarker("You can type normal text and insert values such as {artist} and {track}. Use Advanced formatting help below for the complete list.");
        var format = config.TitleFormat;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##title-format", ref format, 512))
        {
            config.TitleFormat = format;
            plugin.SettingsChanged();
        }

        ImGui.Spacing();
        ImGui.Text("Title position");
        var prefix = config.IsPrefix;
        if (ImGui.RadioButton("Before character name (prefix)", prefix))
        {
            config.IsPrefix = true;
            plugin.SettingsChanged();
        }
        if (ImGui.RadioButton("After character name (suffix)", !prefix))
        {
            config.IsPrefix = false;
            plugin.SettingsChanged();
        }

        ImGui.Spacing();
        ImGui.Text("Automatic cleanup");
        ImGui.Separator();

        var stripBracketed = config.StripBracketedTrackParts;
        if (ImGui.Checkbox("Remove bracketed extras from track names", ref stripBracketed))
        {
            config.StripBracketedTrackParts = stripBracketed;
            plugin.SettingsChanged();
        }
        HelpMarker("For example, 'Song Name (Remastered 2026)' becomes 'Song Name'. Removes (...), [...], and {...} sections from the track name.");

        var smartFit = config.SmartFitLongTitles;
        if (ImGui.Checkbox("Smart-fit long titles", ref smartFit))
        {
            config.SmartFitLongTitles = smartFit;
            plugin.SettingsChanged();
        }
        HelpMarker("Honorific allows 32 characters. Smart-fit prefers word boundaries, preserves wrappers like » ... «, and avoids dangling separators such as '-...'.");

        ImGui.Spacing();
        ImGui.Text("Live preview");
        ImGui.Separator();
        var original = plugin.PreviewExpandedTitle;
        var displayed = plugin.PreviewTitle;
        if (!string.Equals(original, displayed, StringComparison.Ordinal))
            ImGui.TextWrapped($"Original:  {original}");
        ImGui.TextWrapped($"Displayed: {displayed}");
        ImGui.TextDisabled($"Honorific limit: {displayed.Length} / {HonorificBridge.MaxTitleLength} characters");

        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Advanced formatting help"))
        {
            ImGui.TextDisabled("{artist}    - primary artist");
            ImGui.TextDisabled("{artists}   - all artists, comma-separated");
            ImGui.TextDisabled("{track}     - track title");
            ImGui.TextDisabled("{album}     - album name");
            ImGui.TextDisabled("{duration}  - total track time");
            ImGui.TextDisabled("{elapsed}   - current playback position");
            ImGui.TextDisabled("{remaining} - time remaining");
            ImGui.TextDisabled("{is_local}  - true for Spotify local files");
            ImGui.TextDisabled("{paused}    - true while Spotify reports the track paused");
            ImGui.Spacing();
            ImGui.Text("Cycle format");
            ImGui.TextDisabled("{cycle:10|first|second|third}");
            ImGui.TextWrapped("Each entry is shown for the chosen number of playback seconds, then repeats. Entries may contain normal variables.");
            ImGui.TextDisabled("Example: » {cycle:10|vibing to music|{track}|{artist}} «");
            ImGui.Spacing();
            ImGui.TextWrapped("Timing note: Spotify is normally checked about every 3 seconds while music is playing. Elapsed time, remaining time, and cycle changes therefore may appear up to roughly one polling interval late.");
            ImGui.TextDisabled("Variables are case-insensitive. Unknown variables stay visible so formatting mistakes are easy to spot.");
        }
    }

    private void DrawAppearanceTab()
    {
        var config = plugin.Config;

        ImGui.Text("Standard appearance");
        ImGui.Separator();

        var useColor = config.UseTitleColor;
        if (ImGui.Checkbox("Use a custom title color", ref useColor))
        {
            config.UseTitleColor = useColor;
            if (!useColor)
                config.UseTitleGlow = false;
            plugin.SettingsChanged();
        }

        if (config.UseTitleColor)
        {
            var titleColor = config.TitleColor;
            ImGui.SetNextItemWidth(280);
            if (ImGui.ColorEdit3("Title color", ref titleColor))
            {
                config.TitleColor = titleColor;
                plugin.SettingsChanged();
            }

            if (!config.UseSupporterGradient)
            {
                var useGlow = config.UseTitleGlow;
                if (ImGui.Checkbox("Add a glow", ref useGlow))
                {
                    config.UseTitleGlow = useGlow;
                    plugin.SettingsChanged();
                }

                if (config.UseTitleGlow)
                {
                    var glowColor = config.TitleGlowColor;
                    ImGui.SetNextItemWidth(280);
                    if (ImGui.ColorEdit3("Glow color", ref glowColor))
                    {
                        config.TitleGlowColor = glowColor;
                        plugin.SettingsChanged();
                    }
                }
            }
            else
            {
                ImGui.TextDisabled("Normal glow is replaced while a supporter gradient is active.");
            }
        }

        ImGui.TextWrapped("Honorific's own 'Display Coloured Titles' option must be enabled for colors to be visible.");

        ImGui.Spacing();
        ImGui.Text("Honorific supporter effects");
        ImGui.Separator();
        ImGui.TextWrapped("Honorific marks gradients and animations as supporter features. SpotifyTrackHonorific follows Honorific's trust-based approach and does not verify supporter status.");

        var supporterConfirmed = config.HonorificSupporterConfirmed;
        if (ImGui.Checkbox("I confirm I have access to Honorific supporter features", ref supporterConfirmed))
        {
            config.HonorificSupporterConfirmed = supporterConfirmed;
            if (!supporterConfirmed)
                config.UseSupporterGradient = false;
            plugin.SettingsChanged();
        }

        if (config.HonorificSupporterConfirmed)
            DrawSupporterAppearance();
        else
            ImGui.TextDisabled("Supporter controls stay hidden until you confirm access above.");

        ImGui.Spacing();
        if (ImGui.Button("Reset appearance"))
            ResetAppearance();
    }

    private void DrawSupporterAppearance()
    {
        var config = plugin.Config;

        var useGradient = config.UseSupporterGradient;
        if (ImGui.Checkbox("Use a gradient", ref useGradient))
        {
            config.UseSupporterGradient = useGradient;
            if (useGradient)
                config.UseTitleGlow = false;
            plugin.SettingsChanged();
        }

        if (!config.UseSupporterGradient)
            return;

        var gradientCatalog = HonorificGradientCatalog.GetSnapshot();

        var sourceLabel = config.UseCustomDualGradient ? "Custom three-color gradient" : "Honorific preset";
        ImGui.SetNextItemWidth(320);
        if (ImGui.BeginCombo("Gradient type", sourceLabel))
        {
            if (ImGui.Selectable("Custom three-color gradient", config.UseCustomDualGradient))
            {
                config.UseCustomDualGradient = true;
                plugin.SettingsChanged();
            }

            if (ImGui.Selectable("Honorific preset", !config.UseCustomDualGradient))
            {
                config.UseCustomDualGradient = false;
                plugin.SettingsChanged();
            }
            ImGui.EndCombo();
        }

        if (config.UseCustomDualGradient)
        {
            var colorA = config.GradientColorA;
            ImGui.SetNextItemWidth(280);
            if (ImGui.ColorEdit3("Gradient color A", ref colorA))
            {
                config.GradientColorA = colorA;
                plugin.SettingsChanged();
            }

            var colorB = config.GradientColorB;
            ImGui.SetNextItemWidth(280);
            if (ImGui.ColorEdit3("Gradient color B", ref colorB))
            {
                config.GradientColorB = colorB;
                plugin.SettingsChanged();
            }

            var colorC = config.GradientColorC;
            ImGui.SetNextItemWidth(280);
            if (ImGui.ColorEdit3("Gradient color C", ref colorC))
            {
                config.GradientColorC = colorC;
                plugin.SettingsChanged();
            }
        }
        else if (gradientCatalog.PresetsAvailable)
        {
            var presetLabel = "Choose a preset";
            foreach (var option in gradientCatalog.Presets)
            {
                if (option.Value == config.GradientColourSet)
                {
                    presetLabel = option.Name;
                    break;
                }
            }

            ImGui.SetNextItemWidth(320);
            if (ImGui.BeginCombo("Gradient preset", presetLabel))
            {
                foreach (var option in gradientCatalog.Presets)
                {
                    if (ImGui.Selectable(option.Name, option.Value == config.GradientColourSet))
                    {
                        config.GradientColourSet = option.Value;
                        plugin.SettingsChanged();
                    }
                }
                ImGui.EndCombo();
            }
        }
        else
        {
            ImGui.TextWrapped("Honorific's gradient presets are not available yet. Make sure Honorific is loaded and enabled.");
            if (ImGui.Button("Refresh Honorific options"))
                HonorificGradientCatalog.ForceRefresh();
        }

        var animationLabel = "Choose a style";
        foreach (var option in gradientCatalog.AnimationStyles)
        {
            if (option.Value == config.GradientAnimationStyle)
            {
                animationLabel = option.Name;
                break;
            }
        }

        ImGui.SetNextItemWidth(320);
        if (ImGui.BeginCombo("Animation style", animationLabel))
        {
            foreach (var option in gradientCatalog.AnimationStyles)
            {
                if (ImGui.Selectable(option.Name, option.Value == config.GradientAnimationStyle))
                {
                    config.GradientAnimationStyle = option.Value;
                    config.AnimateGradient = option.Value != 0;
                    plugin.SettingsChanged();
                }
            }
            ImGui.EndCombo();
        }
        HelpMarker("The names come directly from your installed Honorific version. Honorific's 'Allow title animations' option must also be enabled for animated styles to move.");
    }

    private void DrawAdvancedTab()
    {
        var config = plugin.Config;

        ImGui.Text("Connection details");
        ImGui.Separator();
        ImGui.Text($"Spotify state: {plugin.StateText}");
        ImGui.TextWrapped($"Connection health: {plugin.ReliabilityText}");
        if (!string.IsNullOrWhiteSpace(plugin.ErrorText))
        {
            if (ImGui.CollapsingHeader("Technical error details"))
                ImGui.TextWrapped(plugin.ErrorText);
        }
        ImGui.TextDisabled("Playing: approximately 3-second checks | idle/paused: approximately 8-second checks");
        ImGui.TextDisabled("Temporary failures automatically back off up to 120 seconds; Spotify rate-limit retry times are respected.");

        ImGui.Spacing();
        ImGui.Text("Manual tools");
        ImGui.Separator();
        if (ImGui.Button("Retry Spotify now"))
            plugin.RetrySpotifyNow();
        ImGui.SameLine();
        if (ImGui.Button("Test Honorific title"))
            plugin.TestHonorificTitle();
        ImGui.SameLine();
        if (ImGui.Button("Clear this plugin's title"))
            plugin.ClearPluginTitle();

        ImGui.Spacing();
        ImGui.Text("Reset and connection data");
        ImGui.Separator();

        if (!confirmResetDisplay)
        {
            if (ImGui.Button("Reset display settings"))
                confirmResetDisplay = true;
            ImGui.SameLine();
            ImGui.TextDisabled("Keeps Spotify authorization and supporter confirmation.");
        }
        else
        {
            ImGui.TextWrapped("Reset title, playback, and appearance settings to defaults? Your Spotify connection and supporter confirmation will be kept.");
            if (ImGui.Button("Confirm display reset"))
            {
                plugin.ResetDisplaySettings();
                confirmResetDisplay = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel##reset-display"))
                confirmResetDisplay = false;
        }

        ImGui.Spacing();
        if (!confirmForgetSpotify)
        {
            if (ImGui.Button("Forget Spotify connection"))
                confirmForgetSpotify = true;
            ImGui.SameLine();
            ImGui.TextDisabled("Removes the saved authorization token but keeps your Client ID for easy reconnecting.");
        }
        else
        {
            ImGui.TextWrapped("Forget the saved Spotify authorization? You will need to connect Spotify again. Your Client ID will remain saved.");
            if (ImGui.Button("Confirm forget Spotify"))
            {
                plugin.ForgetSpotifyConnection(clearClientId: false);
                confirmForgetSpotify = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel##forget-spotify"))
                confirmForgetSpotify = false;
        }

        if (config.SpotifyAuthorizedAtUtc != DateTime.MinValue)
            ImGui.TextDisabled($"Spotify authorization saved: {config.SpotifyAuthorizedAtUtc:u}");

        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Command-line tools"))
        {
            ImGui.TextDisabled("/sth              - open these settings");
            ImGui.TextDisabled("/sth status       - show connection/reliability status in chat");
            ImGui.TextDisabled("/sth now          - show the currently detected track/title");
            ImGui.TextDisabled("/sth retry        - retry Spotify immediately");
            ImGui.TextDisabled("/sth ipc-test     - send a test title to Honorific");
            ImGui.TextDisabled("/sth clear        - clear this plugin's Honorific title");
            ImGui.TextDisabled("/sth enable       - enable Spotify title updates");
            ImGui.TextDisabled("/sth disable      - disable Spotify title updates");
            ImGui.TextDisabled("/sth auth <id>    - manually start Spotify authorization");
        }
    }

    private void ResetAppearance()
    {
        var config = plugin.Config;
        config.UseTitleColor = false;
        config.TitleColor = Vector3.One;
        config.UseTitleGlow = false;
        config.TitleGlowColor = new Vector3(0.35f, 0.70f, 1.00f);
        config.UseSupporterGradient = false;
        config.UseCustomDualGradient = true;
        config.GradientColourSet = 0;
        config.AnimateGradient = true;
        config.GradientAnimationStyle = 1;
        config.GradientColorA = new Vector3(0.35f, 0.70f, 1.00f);
        config.GradientColorB = new Vector3(1.00f, 0.35f, 0.75f);
        config.GradientColorC = new Vector3(0.35f, 0.70f, 1.00f);
        // Keep supporter confirmation: entitlement is not an appearance setting.
        plugin.SettingsChanged();
    }

    private static void HelpMarker(string text)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }
}
