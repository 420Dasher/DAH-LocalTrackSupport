using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using SpotifyTrackHonorific.Filtering;
using SpotifyTrackHonorific.Honorific;
using SpotifyTrackHonorific.Formatting;
using System;
using System.Linq;
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
    private bool confirmImportSettings;
    private string filterTestDraft = "$uicideboy$";
    private int filterTestFieldIndex;
    private int selectedProfileIndex = -1;
    private string profileNameDraft = string.Empty;
    private string profileStatus = string.Empty;
    private string portableSettingsStatus = string.Empty;
    private string diagnosticsStatus = string.Empty;
    private string customFilterAddDraft = string.Empty;
    private string customFilterSearchDraft = string.Empty;
    private string customFilterStatus = string.Empty;
    private int customFilterScopeIndex;
    private bool confirmClearCustomFilterEntries;
    private int cycleBuilderSeconds = 10;
    private string cycleBuilderEntriesDraft = "vibing to music|{track}|{artist}";
    private string formatBuilderStatus = string.Empty;
    private string honorificCacheStatus = string.Empty;

    public ConfigWindow(Plugin plugin)
        : base("SpotifyTrackHonorific Settings")
    {
        this.plugin = plugin;
        clientIdDraft = plugin.Config.SpotifyClientId;
        Size = new Vector2(760, 780);
        SizeCondition = ImGuiCond.FirstUseEver;
        BgAlpha = 0.90f;
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

            if (ImGui.BeginTabItem("Filter"))
            {
                DrawFilterTab();
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
        ImGui.Text($"SpotifyTrackHonorific  v{Plugin.DisplayVersion}");

        ImGui.TextDisabled($"Spotify: {plugin.SpotifyFriendlyStatus}");
        ImGui.SameLine();
        ImGui.TextDisabled(" | ");
        ImGui.SameLine();
        ImGui.TextDisabled($"Honorific: {(plugin.HonorificDetected ? "Ready" : "Not detected")}");

        ImGui.TextWrapped($"Now playing: {plugin.NowPlayingText}");
        ImGui.Separator();
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

        DrawSectionHeader("Overview");

        var enabled = config.Enabled;
        if (ImGui.Checkbox("Enable Spotify title updates", ref enabled))
        {
            config.Enabled = enabled;
            plugin.SettingsChanged();
        }
        HelpMarker("Turn this off to stop Spotify polling and remove this plugin's Honorific title without removing the saved Spotify connection.");


        if (!string.IsNullOrWhiteSpace(plugin.ErrorText))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(plugin.IsAuthenticated
                ? "Spotify is temporarily unavailable. The last valid title is kept while STH retries."
                : "Spotify connection needs attention. Reconnect Spotify below.");
        }

        DrawQuickProfiles();

        if (!plugin.IsAuthenticated)
        {
            DrawSectionHeader(
                "Spotify setup",
                "Connect STH to your own Spotify Developer app. No client secret is required.");
            DrawSpotifyConnectionSetup();
        }
        else
        {
            DrawSectionHeader(
                "Spotify connection",
                "Connected. Setup details stay out of the way unless you need to reconnect.");

            if (ImGui.CollapsingHeader("Connection settings / reconnect"))
                DrawSpotifyConnectionSetup();

            if (config.Enabled && !string.IsNullOrWhiteSpace(plugin.ErrorText))
            {
                if (ImGui.Button("Retry Spotify now"))
                    plugin.RetrySpotifyNow();
            }
        }

        DrawSectionHeader(
            "Honorific integration",
            plugin.HonorificDetected
                ? "Honorific is detected and ready."
                : "Honorific must be installed and enabled for STH titles to appear.");

        if (ImGui.Button("Test Honorific title"))
            plugin.TestHonorificTitle();

        ImGui.SameLine();
        ImGui.TextDisabled("The Spotify title returns on the next successful update.");

        ImGui.Spacing();

        var patMeSupport = config.EnablePatMeHonorificSupport;
        if (ImGui.Checkbox("PatMeHonorific compatibility", ref patMeSupport))
        {
            config.EnablePatMeHonorificSupport = patMeSupport;
            plugin.SettingsChanged();
        }
        HelpMarker("Lets PatMeHonorific temporarily replace STH with its emote-counter title. STH yields while that temporary title is active and restores the Spotify title after PatMeHonorific clears it.");

        if (config.EnablePatMeHonorificSupport)
        {
            ImGui.TextDisabled(plugin.PatMeHonorificYieldActive
                ? "PatMeHonorific: temporary counter title active - STH is yielding."
                : "PatMeHonorific: compatibility ready.");
        }
    }

    private void DrawSpotifyConnectionSetup()
    {
        ImGui.Text("Spotify app Client ID");
        HelpMarker("Copy the Client ID from your Spotify Developer app. This is not your Spotify password.");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##spotify-client-id", ref clientIdDraft, 128);

        ImGui.Spacing();
        ImGui.Text("Callback address");
        HelpMarker("Add this exact address as a Redirect URI in the Spotify Developer app.");
        ImGui.TextWrapped(plugin.RedirectUriText);

        if (ImGui.Button("Copy callback address"))
            ImGui.SetClipboardText(plugin.RedirectUriText);

        ImGui.SameLine();
        if (plugin.IsAuthenticating)
        {
            ImGui.TextDisabled("Connecting... finish authorization in your browser.");
        }
        else
        {
            var connectLabel = plugin.IsAuthenticated ? "Reconnect Spotify" : "Connect Spotify";
            if (ImGui.Button(connectLabel))
                plugin.StartAuthentication(clientIdDraft);
        }
    }

    private void DrawQuickProfiles()
    {
        var profiles = plugin.SavedTitleProfiles;

        DrawSectionHeader("Quick profiles");

        if (profiles.Count == 0)
        {
            ImGui.TextDisabled("No saved profiles yet. Create one in the Title tab.");
            return;
        }

        ImGui.Text($"Current: {plugin.ActiveProfileName}");
        ImGui.TextDisabled("Changing a captured setting makes the current profile Custom.");

        for (var i = 0; i < profiles.Count; i++)
        {
            if (ImGui.Button($"{profiles[i].Name}##quick-profile-{i}", new Vector2(220, 0)))
                plugin.LoadTitleProfile(i, out profileStatus);

            if (i % 2 == 0 && i + 1 < profiles.Count)
                ImGui.SameLine();
        }

        if (!string.IsNullOrWhiteSpace(profileStatus))
            ImGui.TextDisabled(profileStatus);
    }

    private void DrawTitleTab()
    {
        var config = plugin.Config;

        DrawSectionHeader(
            "Playback and visibility",
            "Choose which Spotify playback states are allowed to produce an Honorific title.");

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
        if (ImGui.Checkbox("Hide title while playback is paused or stopped", ref clearOnPause))
        {
            config.ClearOnPause = clearOnPause;
            plugin.SettingsChanged();
        }

        var hideInCombat = config.AutoHideInCombat;
        if (ImGui.Checkbox("Hide Spotify title during combat", ref hideInCombat))
        {
            config.AutoHideInCombat = hideInCombat;
            plugin.SettingsChanged();
        }
        HelpMarker("Only the Honorific title is hidden. Spotify polling continues and the cached title returns immediately after combat.");

        DrawSavedProfiles();

        DrawSectionHeader(
            "Title format",
            "Start from a preset or edit the format directly.");

        if (ImGui.Button("Artist - Track"))
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
        HelpMarker("Rotating alternates between a short status, track, and artist every 10 playback seconds.");

        ImGui.Spacing();
        ImGui.Text("Custom format");

        var format = config.TitleFormat;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##title-format", ref format, 512))
        {
            config.TitleFormat = format;
            plugin.SettingsChanged();
        }

        var cycleWarning = GetCycleSyntaxWarning(config.TitleFormat);
        if (!string.IsNullOrWhiteSpace(cycleWarning))
            ImGui.TextWrapped($"Format warning: {cycleWarning}");

        DrawLivePreview();

        if (ImGui.CollapsingHeader("Format tools and cycle builder"))
        {
            ImGui.TextDisabled("Insert a supported variable into the current format.");

            var formatVariables = TitleTemplateFormatter.SupportedVariables
                .Where(variable => !variable.StartsWith("{cycle:", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            for (var i = 0; i < formatVariables.Length; i++)
            {
                var variable = formatVariables[i];
                var label = variable switch
                {
                    "{artist}" => "Artist",
                    "{artists}" => "Artists",
                    "{track}" => "Track",
                    "{album}" => "Album",
                    "{duration}" => "Duration",
                    "{elapsed}" => "Elapsed",
                    "{remaining}" => "Remaining",
                    "{is_local}" => "Local",
                    "{paused}" => "Paused",
                    "{honorific}" => "Honorific",
                    _ => variable.Trim('{', '}'),
                };

                if (ImGui.SmallButton($"{label}##format-variable-{i}"))
                    AppendTitleFormatToken(variable);

                if ((i + 1) % 4 != 0 && i + 1 < formatVariables.Length)
                    ImGui.SameLine();
            }

            ImGui.Spacing();
            if (ImGui.Button("Copy format"))
            {
                ImGui.SetClipboardText(config.TitleFormat);
                formatBuilderStatus = "Current title format copied to the clipboard.";
            }

            ImGui.SameLine();
            if (ImGui.Button("Reset format"))
            {
                config.TitleFormat = Configuration.DefaultTitleFormat;
                plugin.SettingsChanged();
                formatBuilderStatus = "Title format reset to the default Artist - Track format.";
            }

            ImGui.Spacing();
            ImGui.Text("Cycle builder");

            var cycleSeconds = cycleBuilderSeconds;
            ImGui.SetNextItemWidth(110);
            if (ImGui.InputInt("Seconds per stage", ref cycleSeconds))
                cycleBuilderSeconds = Math.Max(1, cycleSeconds);

            ImGui.Text("Stages");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##cycle-builder-entries", ref cycleBuilderEntriesDraft, 512);
            ImGui.TextDisabled("Separate stages with |. Example: vibing to music|{track}|{artist}");

            if (TryBuildCycleToken(cycleBuilderSeconds, cycleBuilderEntriesDraft, out var cycleToken, out var cycleError))
            {
                ImGui.TextWrapped($"Generated: {cycleToken}");
                if (ImGui.Button("Append cycle"))
                {
                    AppendTitleFormatToken(cycleToken);
                    formatBuilderStatus = "Cycle token appended to the title format.";
                }
            }
            else
            {
                ImGui.TextWrapped($"Cycle builder: {cycleError}");
            }

            ImGui.TextDisabled("Nested cycle blocks are not supported.");
        }

        if (!string.IsNullOrWhiteSpace(formatBuilderStatus))
            ImGui.TextDisabled(formatBuilderStatus);

        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Original Honorific title"))
        {
            DrawMutedWrapped("The {honorific} variable reuses the title that was active before STH.");

            ImGui.TextWrapped(string.IsNullOrWhiteSpace(plugin.CachedHonorificTitle)
                ? "Cached original: none"
                : $"Cached original: {plugin.CachedHonorificTitle}");

            if (ImGui.Button("Cache current Honorific title"))
                plugin.CacheCurrentHonorificTitle(out honorificCacheStatus);
            HelpMarker("If STH currently owns the visible title, disable STH and set the desired title in Honorific before caching it.");

            ImGui.SameLine();
            if (ImGui.Button("Clear cached title"))
                plugin.ClearCachedHonorificTitle(out honorificCacheStatus);

            if (!string.IsNullOrWhiteSpace(honorificCacheStatus))
                ImGui.TextDisabled(honorificCacheStatus);

            ImGui.TextDisabled("The cached title is local-only and is not exported with profiles or portable settings.");
        }

        DrawSectionHeader("Position and cleanup");

        ImGui.Text("Title position");
        var prefix = config.IsPrefix;

        if (ImGui.RadioButton("Before character name (prefix)", prefix))
        {
            config.IsPrefix = true;
            plugin.SettingsChanged();
        }

        ImGui.SameLine();

        if (ImGui.RadioButton("After character name (suffix)", !prefix))
        {
            config.IsPrefix = false;
            plugin.SettingsChanged();
        }

        ImGui.Spacing();

        var stripBracketed = config.StripBracketedTrackParts;
        if (ImGui.Checkbox("Remove bracketed extras from track names", ref stripBracketed))
        {
            config.StripBracketedTrackParts = stripBracketed;
            plugin.SettingsChanged();
        }
        HelpMarker("Removes bracketed additions such as remaster labels from the track name.");

        var smartFit = config.SmartFitLongTitles;
        if (ImGui.Checkbox("Smart-fit long titles", ref smartFit))
        {
            config.SmartFitLongTitles = smartFit;
            plugin.SettingsChanged();
        }
        HelpMarker("Fits the result to Honorific's 32-character limit while preferring clean word and separator boundaries.");

        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Formatting reference"))
        {
            ImGui.TextDisabled("{artist}    - primary artist");
            ImGui.TextDisabled("{artists}   - all artists");
            ImGui.TextDisabled("{track}     - track title");
            ImGui.TextDisabled("{album}     - album name");
            ImGui.TextDisabled("{duration}  - total track time");
            ImGui.TextDisabled("{elapsed}   - current playback position");
            ImGui.TextDisabled("{remaining} - time remaining");
            ImGui.TextDisabled("{is_local}  - true for Spotify local files");
            ImGui.TextDisabled("{paused}    - true while Spotify reports paused");
            ImGui.TextDisabled("{honorific} - cached pre-STH Honorific title");
            ImGui.Spacing();
            ImGui.Text("Cycle format");
            ImGui.TextDisabled("{cycle:10|first|second|third}");
            ImGui.TextWrapped("Each stage is shown for the chosen number of playback seconds, then repeats. Stages may contain normal variables.");
            ImGui.TextDisabled("Spotify polling remains quota-friendly; cycle and progress values advance locally between polls.");
        }
    }

    private void DrawLivePreview()
    {
        var config = plugin.Config;
        var original = plugin.PreviewExpandedTitle;
        var displayed = plugin.PreviewTitle;

        ImGui.Spacing();
        ImGui.Text("Live preview");
        ImGui.Separator();

        ImGui.TextWrapped($"Honorific receives: {displayed}");

        if (!string.Equals(original, displayed, StringComparison.Ordinal))
            ImGui.TextWrapped($"Before smart-fit: {original}");

        ImGui.TextDisabled(
            $"Source: {(plugin.PreviewUsesCurrentTrack ? "Current Spotify track" : "Built-in example track")} | " +
            $"Position: {(config.IsPrefix ? "Prefix" : "Suffix")} | " +
            $"Characters: {displayed.Length}/{HonorificBridge.MaxTitleLength}");
    }

    private void AppendTitleFormatToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;

        var config = plugin.Config;
        var current = config.TitleFormat ?? string.Empty;

        if (string.IsNullOrWhiteSpace(current))
        {
            config.TitleFormat = token;
        }
        else
        {
            var separator = char.IsWhiteSpace(current[^1]) ? string.Empty : " ";
            config.TitleFormat = current + separator + token;
        }

        plugin.SettingsChanged();
        formatBuilderStatus = $"Appended {token}.";
    }

    private static bool TryBuildCycleToken(
        int secondsPerStage,
        string entriesDraft,
        out string token,
        out string error)
    {
        token = string.Empty;
        error = string.Empty;

        if (secondsPerStage <= 0)
        {
            error = "Seconds per stage must be at least 1.";
            return false;
        }

        var entries = (entriesDraft ?? string.Empty)
            .Split('|', StringSplitOptions.None)
            .Select(entry => entry.Trim())
            .ToArray();

        if (entries.Length == 0 || entries.All(string.IsNullOrWhiteSpace))
        {
            error = "Enter at least one cycle stage.";
            return false;
        }

        if (entries.Any(entry => entry.Contains("{cycle:", StringComparison.OrdinalIgnoreCase)))
        {
            error = "Nested cycle blocks are not supported.";
            return false;
        }

        token = $"{{cycle:{secondsPerStage}|{string.Join("|", entries)}}}";
        return true;
    }

    private static string GetCycleSyntaxWarning(string format)
    {
        if (string.IsNullOrWhiteSpace(format) ||
            !format.Contains("{cycle:", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        const string cyclePrefix = "{cycle:";
        var cursor = 0;

        while (cursor < format.Length)
        {
            var start = format.IndexOf(cyclePrefix, cursor, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                break;

            var depth = 0;
            var end = -1;
            for (var i = start; i < format.Length; i++)
            {
                if (format[i] == '{')
                {
                    depth++;
                    continue;
                }

                if (format[i] != '}')
                    continue;

                depth--;
                if (depth == 0)
                {
                    end = i;
                    break;
                }
            }

            if (end < 0)
                return "A cycle block is missing its closing }.";

            var bodyStart = start + cyclePrefix.Length;
            var body = format.Substring(bodyStart, end - bodyStart);
            var parts = body.Split('|', StringSplitOptions.None);

            if (parts.Length < 2)
                return "A cycle block needs seconds and at least one stage, for example {cycle:10|{track}|{artist}}.";

            if (!int.TryParse(parts[0].Trim(), out var seconds) || seconds <= 0)
                return "Cycle seconds must be a positive whole number.";

            if (parts.Skip(1).All(string.IsNullOrWhiteSpace))
                return "A cycle block needs at least one non-empty stage.";

            if (parts.Skip(1).Any(part => part.Contains("{cycle:", StringComparison.OrdinalIgnoreCase)))
                return "Nested cycle blocks are not supported.";

            cursor = end + 1;
        }

        return string.Empty;
    }
    private void DrawSavedProfiles()
    {
        var profiles = plugin.SavedTitleProfiles;

        if (selectedProfileIndex >= profiles.Count)
            selectedProfileIndex = -1;

        DrawSectionHeader(
            "Saved profiles",
            "Profiles capture title, playback, appearance, and filter settings. Spotify connection data is never stored.");

        var selectedLabel = selectedProfileIndex >= 0 && selectedProfileIndex < profiles.Count
            ? profiles[selectedProfileIndex].Name
            : "Choose a profile";

        ImGui.SetNextItemWidth(280);
        if (ImGui.BeginCombo("##saved-title-profile", selectedLabel))
        {
            for (var i = 0; i < profiles.Count; i++)
            {
                if (ImGui.Selectable(profiles[i].Name, selectedProfileIndex == i))
                {
                    selectedProfileIndex = i;
                    profileNameDraft = profiles[i].Name;
                }
            }

            ImGui.EndCombo();
        }

        if (selectedProfileIndex >= 0 && selectedProfileIndex < profiles.Count)
        {
            ImGui.SameLine();

            if (ImGui.Button("Load"))
                plugin.LoadTitleProfile(selectedProfileIndex, out profileStatus);

            ImGui.SameLine();

            if (ImGui.Button("Update"))
                plugin.UpdateTitleProfile(selectedProfileIndex, out profileStatus);
        }

        ImGui.Spacing();
        ImGui.Text("Profile name");
        ImGui.SetNextItemWidth(280);
        ImGui.InputText("##profile-name", ref profileNameDraft, 64);

        ImGui.SameLine();

        if (ImGui.Button("Save current"))
        {
            if (plugin.SaveTitleProfile(profileNameDraft, out var savedIndex, out profileStatus))
            {
                selectedProfileIndex = savedIndex;

                if (savedIndex >= 0 && savedIndex < plugin.SavedTitleProfiles.Count)
                    profileNameDraft = plugin.SavedTitleProfiles[savedIndex].Name;
            }
        }

        if (selectedProfileIndex >= 0 && selectedProfileIndex < profiles.Count)
        {
            if (ImGui.CollapsingHeader("Manage selected profile"))
            {
                if (ImGui.Button("Rename selected"))
                {
                    if (plugin.RenameTitleProfile(selectedProfileIndex, profileNameDraft, out profileStatus))
                        profileNameDraft = plugin.SavedTitleProfiles[selectedProfileIndex].Name;
                }

                ImGui.SameLine();

                if (ImGui.Button("Duplicate"))
                {
                    if (plugin.DuplicateTitleProfile(selectedProfileIndex, out var duplicateIndex, out profileStatus))
                    {
                        selectedProfileIndex = duplicateIndex;
                        profileNameDraft = plugin.SavedTitleProfiles[duplicateIndex].Name;
                    }
                }

                var canMoveUp = selectedProfileIndex > 0;
                var canMoveDown = selectedProfileIndex + 1 < profiles.Count;

                ImGui.Spacing();

                if (!canMoveUp)
                    ImGui.BeginDisabled();

                if (ImGui.SmallButton("Move up"))
                {
                    if (plugin.MoveTitleProfile(selectedProfileIndex, -1, out var movedIndex, out profileStatus))
                        selectedProfileIndex = movedIndex;
                }

                if (!canMoveUp)
                    ImGui.EndDisabled();

                ImGui.SameLine();

                if (!canMoveDown)
                    ImGui.BeginDisabled();

                if (ImGui.SmallButton("Move down"))
                {
                    if (plugin.MoveTitleProfile(selectedProfileIndex, 1, out var movedIndex, out profileStatus))
                        selectedProfileIndex = movedIndex;
                }

                if (!canMoveDown)
                    ImGui.EndDisabled();

                ImGui.SameLine();

                if (ImGui.SmallButton("Delete profile"))
                {
                    plugin.DeleteTitleProfile(selectedProfileIndex, out profileStatus);
                    selectedProfileIndex = -1;
                    profileNameDraft = string.Empty;
                }

                ImGui.TextDisabled("Profile order is also the Quick profiles order on Home.");
            }
        }

        ImGui.TextDisabled($"{profiles.Count}/{Configuration.MaxTitleProfiles} profiles saved.");

        if (!string.IsNullOrWhiteSpace(profileStatus))
            ImGui.TextDisabled(profileStatus);
    }

    private void DrawFilterTab()
    {
        var config = plugin.Config;

        DrawSectionHeader(
            "Filter status",
            "Censor selected Spotify metadata without breaking the rest of your title format or cycle.");

        var enabled = config.EnableContentFilter;
        if (ImGui.Checkbox("Enable content filter", ref enabled))
        {
            config.EnableContentFilter = enabled;
            plugin.SettingsChanged();
        }

        var smart = config.SmartContentFilterMatching;
        if (ImGui.Checkbox("Smart variation matching", ref smart))
        {
            config.SmartContentFilterMatching = smart;
            plugin.SettingsChanged();
        }
        HelpMarker("Also catches common obfuscation, punctuation and spacing changes, and conservative small typos on longer entries.");

        DrawSectionHeader(
            "Built-in protection",
            "Optional conservative starter list. Custom rules stay separate.");

        var useBuiltIn = config.UseBuiltInContentFilterList;
        if (ImGui.Checkbox("Use built-in triggerword list", ref useBuiltIn))
        {
            config.UseBuiltInContentFilterList = useBuiltIn;
            plugin.SettingsChanged();
        }

        var activeBuiltIns = ContentFilterMatcher.ActiveBuiltInTriggerWordCount(config.DisabledBuiltInContentFilterEntries);
        ImGui.SameLine();
        ImGui.TextDisabled(useBuiltIn
            ? $"{activeBuiltIns}/{ContentFilterMatcher.BuiltInTriggerWords.Count} active"
            : $"{activeBuiltIns}/{ContentFilterMatcher.BuiltInTriggerWords.Count} selected (preset off)");

        if (ImGui.CollapsingHeader("Customize built-in list"))
        {
            string? lastCategory = null;

            foreach (var entry in ContentFilterMatcher.BuiltInTriggerWords)
            {
                if (!string.Equals(lastCategory, entry.Category, StringComparison.Ordinal))
                {
                    if (lastCategory != null)
                        ImGui.Spacing();

                    ImGui.TextDisabled(entry.Category);
                    lastCategory = entry.Category;
                }

                var entryEnabled = ContentFilterMatcher.IsBuiltInEntryEnabled(
                    entry.Id,
                    config.DisabledBuiltInContentFilterEntries);

                if (ImGui.Checkbox($"{entry.Term}##builtin-trigger-{entry.Id}", ref entryEnabled))
                {
                    config.DisabledBuiltInContentFilterEntries = ContentFilterMatcher.SetBuiltInEntryEnabled(
                        entry.Id,
                        entryEnabled,
                        config.DisabledBuiltInContentFilterEntries);
                    plugin.SettingsChanged();
                }
            }

            ImGui.Spacing();

            if (ImGui.Button("Restore built-in defaults"))
            {
                config.DisabledBuiltInContentFilterEntries = string.Empty;
                plugin.SettingsChanged();
            }
        }

        DrawSectionHeader(
            "Custom rules",
            "Quick-add a rule for all metadata, or target artist, track, or album.");

        var customEntries = ParseCustomFilterEntries(config.ContentFilterEntries);
        var scopeLabel = customFilterScopeIndex switch
        {
            1 => "Artist",
            2 => "Track",
            3 => "Album",
            _ => "All fields",
        };

        ImGui.SetNextItemWidth(145);

        if (ImGui.BeginCombo("##custom-filter-scope", scopeLabel))
        {
            if (ImGui.Selectable("All fields", customFilterScopeIndex == 0))
                customFilterScopeIndex = 0;
            if (ImGui.Selectable("Artist", customFilterScopeIndex == 1))
                customFilterScopeIndex = 1;
            if (ImGui.Selectable("Track", customFilterScopeIndex == 2))
                customFilterScopeIndex = 2;
            if (ImGui.Selectable("Album", customFilterScopeIndex == 3))
                customFilterScopeIndex = 3;

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(320);
        ImGui.InputText("##custom-filter-add", ref customFilterAddDraft, 256);

        ImGui.SameLine();

        if (ImGui.Button("Add"))
        {
            var candidate = BuildCustomFilterRule(customFilterScopeIndex, customFilterAddDraft);

            if (string.IsNullOrWhiteSpace(candidate))
            {
                customFilterStatus = "Enter a blacklist term first.";
            }
            else if (ContainsCustomFilterEntry(customEntries, candidate))
            {
                customFilterStatus = $"'{candidate}' is already in the custom blacklist.";
            }
            else
            {
                var addedTerm = customFilterAddDraft.Trim();
                customEntries.Add(candidate);
                config.ContentFilterEntries = SerializeCustomFilterEntries(customEntries);
                plugin.SettingsChanged();
                customFilterAddDraft = string.Empty;

                var builtInOverlap = ContentFilterMatcher.BuiltInTriggerWords
                    .Any(entry => string.Equals(entry.Term, addedTerm, StringComparison.OrdinalIgnoreCase));

                customFilterStatus = builtInOverlap && config.UseBuiltInContentFilterList
                    ? $"Added '{candidate}'. The same term is also active in the all-fields built-in list."
                    : $"Added '{candidate}'.";
            }
        }

        ImGui.Spacing();
        ImGui.Text("Search custom rules");
        ImGui.SetNextItemWidth(320);
        ImGui.InputText("##custom-filter-search", ref customFilterSearchDraft, 256);

        var duplicateCount = CountDuplicateCustomFilterEntries(customEntries);
        ImGui.TextDisabled($"{customEntries.Count} custom entr{(customEntries.Count == 1 ? "y" : "ies")}.");

        if (duplicateCount > 0)
            ImGui.TextWrapped($"{duplicateCount} duplicate entr{(duplicateCount == 1 ? "y" : "ies")} found. Clean + sort will remove them.");

        var removeEntryIndex = -1;
        var visibleEntries = 0;
        var search = customFilterSearchDraft.Trim();

        if (ImGui.BeginTable(
            "##custom-filter-table",
            2,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Rule", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 80);

            for (var i = 0; i < customEntries.Count; i++)
            {
                var entry = customEntries[i];

                if (!string.IsNullOrWhiteSpace(search) &&
                    entry.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                visibleEntries++;
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.TextWrapped(entry);

                ImGui.TableSetColumnIndex(1);

                if (ImGui.SmallButton($"Remove##custom-filter-remove-{i}"))
                    removeEntryIndex = i;
            }

            ImGui.EndTable();
        }

        if (visibleEntries == 0)
        {
            ImGui.TextDisabled(customEntries.Count == 0
                ? "No custom rules saved."
                : "No custom rules match this search.");
        }

        if (removeEntryIndex >= 0)
        {
            var removed = customEntries[removeEntryIndex];
            customEntries.RemoveAt(removeEntryIndex);
            config.ContentFilterEntries = SerializeCustomFilterEntries(customEntries);
            plugin.SettingsChanged();
            customFilterStatus = $"Removed '{removed}'.";
        }

        ImGui.Spacing();

        if (ImGui.SmallButton("Clean + sort"))
        {
            var beforeCount = customEntries.Count;
            var cleaned = CleanSortCustomFilterEntries(customEntries);
            config.ContentFilterEntries = SerializeCustomFilterEntries(cleaned);
            plugin.SettingsChanged();

            var removedCount = beforeCount - cleaned.Count;
            customFilterStatus = removedCount > 0
                ? $"Cleaned, sorted, and removed {removedCount} duplicate entr{(removedCount == 1 ? "y" : "ies")}."
                : $"Cleaned and sorted {cleaned.Count} custom entr{(cleaned.Count == 1 ? "y" : "ies")}.";
        }

        ImGui.SameLine();

        if (!confirmClearCustomFilterEntries)
        {
            if (ImGui.SmallButton("Clear custom rules"))
                confirmClearCustomFilterEntries = true;
        }
        else
        {
            if (ImGui.SmallButton("Confirm clear"))
            {
                config.ContentFilterEntries = string.Empty;
                plugin.SettingsChanged();
                customFilterSearchDraft = string.Empty;
                customFilterStatus = "All custom blacklist entries cleared.";
                confirmClearCustomFilterEntries = false;
            }

            ImGui.SameLine();

            if (ImGui.SmallButton("Cancel##clear-custom-filter"))
                confirmClearCustomFilterEntries = false;
        }

        if (!string.IsNullOrWhiteSpace(customFilterStatus))
            ImGui.TextDisabled(customFilterStatus);

        if (ImGui.CollapsingHeader("Bulk edit / paste raw list"))
        {
            ImGui.TextDisabled("One rule per line. Optional prefixes: artist:, track:, album:");

            var entries = config.ContentFilterEntries;

            if (ImGui.InputTextMultiline("##content-filter-entries", ref entries, 4096, new Vector2(-1, 150)))
            {
                config.ContentFilterEntries = entries;
                plugin.SettingsChanged();
                customFilterStatus = "Raw custom blacklist updated. Use Clean + sort to normalize pasted entries.";
            }
        }

        DrawSectionHeader("Match behavior");

        var actionLabel = config.ContentFilterAction switch
        {
            1 => "Clear Spotify title",
            2 => "Keep previous title",
            _ => "Censor matching fields",
        };

        ImGui.SetNextItemWidth(260);

        if (ImGui.BeginCombo("##content-filter-action", actionLabel))
        {
            if (ImGui.Selectable("Censor matching fields", config.ContentFilterAction == 0))
            {
                config.ContentFilterAction = 0;
                plugin.SettingsChanged();
            }

            if (ImGui.Selectable("Clear Spotify title", config.ContentFilterAction == 1))
            {
                config.ContentFilterAction = 1;
                plugin.SettingsChanged();
            }

            if (ImGui.Selectable("Keep previous title", config.ContentFilterAction == 2))
            {
                config.ContentFilterAction = 2;
                plugin.SettingsChanged();
            }

            ImGui.EndCombo();
        }

        if (config.ContentFilterAction == 0)
        {
            ImGui.TextDisabled("Only the matching metadata field is replaced. Other fields and cycle stages continue normally.");

            ImGui.Text("Replacement text");

            var fallback = config.ContentFilterFallback;
            ImGui.SetNextItemWidth(320);

            if (ImGui.InputText("##content-filter-fallback", ref fallback, 128))
            {
                config.ContentFilterFallback = fallback;
                plugin.SettingsChanged();
            }

            ImGui.SameLine();

            if (ImGui.Button("Default##filter-fallback"))
            {
                config.ContentFilterFallback = Configuration.DefaultContentFilterFallback;
                plugin.SettingsChanged();
            }
        }

        ImGui.Spacing();

        if (ImGui.CollapsingHeader("Matcher test"))
        {
            ImGui.TextDisabled("Test the active rules without changing Spotify playback.");

            var testFieldLabel = filterTestFieldIndex switch
            {
                1 => "Artist only",
                2 => "Track only",
                3 => "Album only",
                _ => "All fields",
            };

            ImGui.SetNextItemWidth(145);

            if (ImGui.BeginCombo("##content-filter-test-field", testFieldLabel))
            {
                if (ImGui.Selectable("All fields", filterTestFieldIndex == 0))
                    filterTestFieldIndex = 0;
                if (ImGui.Selectable("Artist only", filterTestFieldIndex == 1))
                    filterTestFieldIndex = 1;
                if (ImGui.Selectable("Track only", filterTestFieldIndex == 2))
                    filterTestFieldIndex = 2;
                if (ImGui.Selectable("Album only", filterTestFieldIndex == 3))
                    filterTestFieldIndex = 3;

                ImGui.EndCombo();
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(320);
            ImGui.InputText("##content-filter-test", ref filterTestDraft, 256);

            var testResult = plugin.TestContentFilterText(filterTestDraft, filterTestFieldIndex);

            if (testResult.StartsWith("Blocked by", StringComparison.Ordinal))
                ImGui.TextWrapped($"MATCH: {testResult}");
            else
                ImGui.TextDisabled(testResult);

            ImGui.TextDisabled("Built-in terms are all-fields; scoped custom rules only match their selected field.");
        }
    }

    private static System.Collections.Generic.List<string> ParseCustomFilterEntries(string raw)
    {
        var result = new System.Collections.Generic.List<string>();
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        var normalized = raw.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var line in normalized.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                result.Add(trimmed);
        }

        return result;
    }

    private static string SerializeCustomFilterEntries(System.Collections.Generic.List<string> entries) =>
        string.Join(Environment.NewLine, entries);

    private static string BuildCustomFilterRule(int scopeIndex, string text)
    {
        var term = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(term))
            return string.Empty;

        return scopeIndex switch
        {
            1 => $"artist: {term}",
            2 => $"track: {term}",
            3 => $"album: {term}",
            _ => term,
        };
    }

    private static bool ContainsCustomFilterEntry(
        System.Collections.Generic.List<string> entries,
        string candidate)
    {
        foreach (var entry in entries)
        {
            if (string.Equals(entry.Trim(), candidate.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static int CountDuplicateCustomFilterEntries(System.Collections.Generic.List<string> entries)
    {
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = 0;

        foreach (var entry in entries)
        {
            var trimmed = entry.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) && !seen.Add(trimmed))
                duplicates++;
        }

        return duplicates;
    }

    private static System.Collections.Generic.List<string> CleanSortCustomFilterEntries(
        System.Collections.Generic.List<string> entries)
    {
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cleaned = new System.Collections.Generic.List<string>();

        foreach (var entry in entries)
        {
            var trimmed = entry.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) && seen.Add(trimmed))
                cleaned.Add(trimmed);
        }

        cleaned.Sort(StringComparer.OrdinalIgnoreCase);
        return cleaned;
    }

    private void DrawAppearanceTab()
    {
        var config = plugin.Config;

        DrawSectionHeader(
            "Standard appearance",
            "Optional normal Honorific color and glow.");

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
            ImGui.Text("Title color");

            var titleColor = config.TitleColor;
            ImGui.SetNextItemWidth(300);

            if (ImGui.ColorEdit3("##title-color", ref titleColor))
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
                    ImGui.Text("Glow color");

                    var glowColor = config.TitleGlowColor;
                    ImGui.SetNextItemWidth(300);

                    if (ImGui.ColorEdit3("##glow-color", ref glowColor))
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

        ImGui.TextDisabled("Honorific must have Display Coloured Titles enabled for colors to be visible.");

        DrawSectionHeader(
            "Honorific supporter effects",
            "Gradient and animation controls follow Honorific's trust-based supporter access.");

        var supporterConfirmed = config.HonorificSupporterConfirmed;

        if (ImGui.Checkbox("I confirm I have access to Honorific supporter features", ref supporterConfirmed))
        {
            config.HonorificSupporterConfirmed = supporterConfirmed;

            if (!supporterConfirmed)
                config.UseSupporterGradient = false;

            plugin.SettingsChanged();
        }

        if (config.HonorificSupporterConfirmed)
        {
            DrawSupporterAppearance();
        }
        else
        {
            ImGui.TextDisabled("Confirm access above to reveal supporter gradient and animation controls.");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.SmallButton("Reset appearance"))
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
        var sourceLabel = config.UseCustomDualGradient
            ? "Custom three-color gradient"
            : "Honorific preset";

        ImGui.Text("Gradient type");
        ImGui.SetNextItemWidth(320);

        if (ImGui.BeginCombo("##gradient-type", sourceLabel))
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
            ImGui.Text("Gradient color A");
            var colorA = config.GradientColorA;
            ImGui.SetNextItemWidth(300);

            if (ImGui.ColorEdit3("##gradient-color-a", ref colorA))
            {
                config.GradientColorA = colorA;
                plugin.SettingsChanged();
            }

            ImGui.Text("Gradient color B");
            var colorB = config.GradientColorB;
            ImGui.SetNextItemWidth(300);

            if (ImGui.ColorEdit3("##gradient-color-b", ref colorB))
            {
                config.GradientColorB = colorB;
                plugin.SettingsChanged();
            }

            ImGui.Text("Gradient color C");
            var colorC = config.GradientColorC;
            ImGui.SetNextItemWidth(300);

            if (ImGui.ColorEdit3("##gradient-color-c", ref colorC))
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

            ImGui.Text("Gradient preset");
            ImGui.SetNextItemWidth(320);

            if (ImGui.BeginCombo("##gradient-preset", presetLabel))
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

        ImGui.Text("Animation style");
        ImGui.SetNextItemWidth(320);

        if (ImGui.BeginCombo("##gradient-animation-style", animationLabel))
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

        HelpMarker("The names come directly from your installed Honorific version. Honorific's Allow title animations option must also be enabled for animated styles to move.");
    }

    private void DrawAdvancedTab()
    {
        var config = plugin.Config;

        DrawSectionHeader("Connection health");

        ImGui.Text($"Spotify state: {plugin.StateText}");
        ImGui.TextWrapped($"Health: {plugin.ReliabilityText}");
        ImGui.TextDisabled("Playing/paused checks: about 15s | idle checks: about 60s");

        if (!string.IsNullOrWhiteSpace(plugin.ErrorText) &&
            ImGui.CollapsingHeader("Technical error details"))
        {
            ImGui.TextWrapped(plugin.ErrorText);
        }

        DrawSectionHeader("Tools");

        if (ImGui.Button("Retry Spotify"))
            plugin.RetrySpotifyNow();

        ImGui.SameLine();

        if (ImGui.Button("Test Honorific"))
            plugin.TestHonorificTitle();

        ImGui.SameLine();

        if (ImGui.Button("Clear STH title"))
            plugin.ClearPluginTitle();

        ImGui.Spacing();

        if (ImGui.Button("Copy diagnostics"))
        {
            ImGui.SetClipboardText(plugin.BuildDiagnosticsText());
            diagnosticsStatus = "Diagnostics copied. Client ID, OAuth tokens, track names, and artist names are excluded.";
        }

        ImGui.SameLine();
        ImGui.TextDisabled("Safe to paste into a bug report.");

        if (!string.IsNullOrWhiteSpace(diagnosticsStatus))
            ImGui.TextDisabled(diagnosticsStatus);

        DrawPortableSettings();

        ImGui.Spacing();

        if (ImGui.CollapsingHeader("Reset / connection data"))
        {
            if (!confirmResetDisplay)
            {
                if (ImGui.Button("Reset display settings"))
                    confirmResetDisplay = true;

                ImGui.SameLine();
                ImGui.TextDisabled("Keeps Spotify authorization and supporter confirmation.");
            }
            else
            {
                ImGui.TextWrapped("Reset title, playback, and appearance settings to defaults?");

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
                ImGui.TextDisabled("Removes authorization but keeps the Client ID.");
            }
            else
            {
                ImGui.TextWrapped("Forget the saved Spotify authorization? You will need to connect Spotify again.");

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
                ImGui.TextDisabled($"Authorization saved: {config.SpotifyAuthorizedAtUtc:u}");
        }

        ImGui.Spacing();

        if (ImGui.CollapsingHeader("Command-line tools"))
        {
            ImGui.TextDisabled("/sth              - open settings");
            ImGui.TextDisabled("/sth status       - connection/reliability status");
            ImGui.TextDisabled("/sth now          - current detected track/title");
            ImGui.TextDisabled("/sth retry        - retry Spotify");
            ImGui.TextDisabled("/sth ipc-test     - test Honorific");
            ImGui.TextDisabled("/sth clear        - clear STH title");
            ImGui.TextDisabled("/sth enable       - enable title updates");
            ImGui.TextDisabled("/sth disable      - disable title updates");
            ImGui.TextDisabled("/sth auth <id>    - start Spotify authorization");
        }
    }

    private void DrawPortableSettings()
    {
        DrawSectionHeader(
            "Backup / transfer",
            "Portable settings include display, filter, and saved profiles. Spotify credentials and supporter confirmation are never exported.");

        if (ImGui.Button("Copy portable settings"))
        {
            ImGui.SetClipboardText(plugin.ExportPortableSettings());
            portableSettingsStatus = "Portable settings copied to the clipboard.";
            confirmImportSettings = false;
        }

        if (!confirmImportSettings)
        {
            ImGui.SameLine();

            if (ImGui.Button("Import from clipboard"))
                confirmImportSettings = true;
        }
        else
        {
            ImGui.TextWrapped("Import replaces current display/filter settings and saved profiles.");

            if (ImGui.Button("Confirm import"))
            {
                var clipboard = ImGui.GetClipboardText() ?? string.Empty;
                plugin.ImportPortableSettings(clipboard, out portableSettingsStatus);
                confirmImportSettings = false;
                selectedProfileIndex = -1;
                profileNameDraft = string.Empty;
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel##portable-import"))
                confirmImportSettings = false;
        }

        if (!string.IsNullOrWhiteSpace(portableSettingsStatus))
            ImGui.TextDisabled(portableSettingsStatus);
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

    private static void DrawSectionHeader(string title, string? description = null)
    {
        ImGui.Spacing();
        ImGui.Text(title);
        ImGui.Separator();

        if (!string.IsNullOrWhiteSpace(description))
            DrawMutedWrapped(description);
    }

    private static void DrawMutedWrapped(string text)
    {
        ImGui.PushTextWrapPos(0f);
        ImGui.TextDisabled(text);
        ImGui.PopTextWrapPos();
    }

    private static void HelpMarker(string text)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }
}

