using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SpotifyTrackHonorific.Honorific;
using SpotifyTrackHonorific.Formatting;
using SpotifyTrackHonorific.Filtering;
using SpotifyTrackHonorific.Profiles;
using SpotifyTrackHonorific.Settings;
using SpotifyTrackHonorific.Spotify;
using SpotifyTrackHonorific.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SpotifyTrackHonorific;

public sealed class Plugin : IDalamudPlugin
{
    internal const string DisplayVersion = "1.0.5";
    private const string ShortCommand = "/sth";
    private const string LongCommand = "/spotifytrackhonorific";
    private static readonly TimeSpan NormalPollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LocalRenderRefreshInterval = TimeSpan.FromSeconds(1);
    private const int FailureBackoffBaseSeconds = 5;
    private const int FailureBackoffMaxSeconds = 120;
    private const int RateLimitFallbackSeconds = 30;
    private const int QuotaFallbackBaseSeconds = 3600;
    private const int QuotaFallbackMaxSeconds = 43200;
    private static readonly JsonSerializerOptions PortableSettingsJsonOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
    };

    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] private static IChatGui ChatGui { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;

    private readonly Configuration config;
    private readonly SpotifyApiService spotify;
    private readonly HonorificBridge honorific;
    private readonly CancellationTokenSource lifetimeCts = new();
    private readonly WindowSystem windowSystem = new("SpotifyTrackHonorific");
    private readonly ConfigWindow configWindow;

    private long nextPollUtcTicks = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
    private int pollInProgress;
    private int authenticationInProgress;
    private string? appliedFingerprint;
    private bool hasAppliedTitle;
    private SpotifyTrackInfo? lastTrack;
    private bool lastTrackPaused;
    private long lastTrackObservedUtcTicks;
    private long nextLocalRenderUtcTicks;
    private string lastState = "Starting";
    private string? lastError;
    private bool honorificErrorShown;
    private int consecutivePollFailures;
    private long lastSuccessfulPollUtcTicks;
    private long spotifyCooldownUntilUtcTicks;
    private string? lastLoggedSpotifyError;

    internal Configuration Config => config;
    internal bool IsAuthenticated => spotify.HasRefreshToken;
    internal bool IsAuthenticating => Volatile.Read(ref authenticationInProgress) != 0;
    internal string StateText => lastState;
    internal string? ErrorText => lastError;
    internal string ReliabilityText => BuildReliabilityText();
    internal string PreviewTitle => BuildPreviewTitle();
    internal string PreviewExpandedTitle => BuildPreviewExpandedTitle();
    internal bool PreviewUsesCurrentTrack => lastTrack != null;
    internal IReadOnlyList<TitleProfile> SavedTitleProfiles => config.TitleProfiles;
    internal string RedirectUriText => spotify.RedirectUriText;
    internal string NowPlayingText => lastTrack == null
        ? (IsAuthenticated ? "Waiting for music" : "Spotify not connected")
        : $"{lastTrack.ArtistText} - {lastTrack.Name}";
    internal bool HonorificDetected => HonorificGradientCatalog.GetSnapshot().HonorificTypesFound;
    internal string SpotifyFriendlyStatus => BuildSpotifyFriendlyStatus();

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (config.EnsureDefaults())
            SaveConfig();

        spotify = new SpotifyApiService(config, SaveConfig);
        honorific = new HonorificBridge(PluginInterface);

        configWindow = new ConfigWindow(this);
        windowSystem.AddWindow(configWindow);

        var commandInfo = new CommandInfo(OnCommand)
        {
            HelpMessage = "Open SpotifyTrackHonorific settings. Use '/sth help' for commands."
        };
        CommandManager.AddHandler(ShortCommand, commandInfo);
        CommandManager.AddHandler(LongCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = commandInfo.HelpMessage
        });

        Framework.Update += OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += OpenConfigUi;

        ChatGui.Print($"SpotifyTrackHonorific v{DisplayVersion} loaded. Use /sth to open settings.");
    }

    public void Dispose()
    {
        lifetimeCts.Cancel();

        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= OpenConfigUi;

        CommandManager.RemoveHandler(ShortCommand);
        CommandManager.RemoveHandler(LongCommand);
        windowSystem.RemoveAllWindows();

        TryClearHonorific();
        spotify.Dispose();
        lifetimeCts.Dispose();
    }

    private void OpenConfigUi() => configWindow.IsOpen = true;

    internal void SettingsChanged()
    {
        config.EnsureDefaults();
        SaveConfig();
        SchedulePollNow();
        appliedFingerprint = null;

        if (!config.Enabled)
        {
            TryClearHonorific();
            return;
        }

        var renderTrack = GetCurrentRenderTrack();
        if (renderTrack != null)
        {
            if (lastTrackPaused && config.ClearOnPause)
            {
                TryClearHonorific();
            }
            else if (IsTrackAllowed(renderTrack))
            {
                var filterMatch = GetContentFilterMatch(renderTrack);
                if (filterMatch != null && config.ContentFilterAction == 1)
                {
                    lastState = $"Triggerword censored ({filterMatch.Field})";
                    TryClearHonorific();
                }
                else if (filterMatch != null && config.ContentFilterAction == 2)
                {
                    lastState = $"Triggerword censored ({filterMatch.Field}) - previous title kept";
                }
                else
                {
                    if (filterMatch != null)
                        lastState = $"Triggerword censored ({filterMatch.Field})";
                    ApplyHonorificTitle(BuildConfiguredTitle(renderTrack, lastTrackPaused), BuildRenderFingerprint(renderTrack, lastTrackPaused));
                }
            }
            else
            {
                TryClearHonorific();
            }
            return;
        }

        if (config.ClearOnPause && hasAppliedTitle && lastState == "Nothing playing / paused")
            TryClearHonorific();
    }

    internal void StartAuthentication(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            ChatGui.PrintError("Enter your Spotify Client ID first.", "SpotifyTrackHonorific");
            return;
        }

        // Atomic gate prevents accidental double-clicks from starting two callback
        // listeners/token exchanges at once.
        if (Interlocked.CompareExchange(ref authenticationInProgress, 1, 0) != 0)
            return;

        _ = AuthenticateAsync(clientId);
    }

    internal void RetrySpotifyNow()
    {
        if (TryGetSpotifyCooldownRemaining(out var remaining))
        {
            lastState = $"Spotify cooldown active - retry in {FormatRetryDelay(remaining)}";
            ChatGui.Print($"Spotify asked us to wait. Retry is available in {FormatRetryDelay(remaining)}.");
            return;
        }

        Interlocked.Exchange(ref consecutivePollFailures, 0);
        lastLoggedSpotifyError = null;
        lastState = "Manual Spotify retry requested";
        SchedulePollNow();
    }

    internal void TestHonorificTitle()
    {
        ApplyHonorificTitle("♪ Honorific test", "manual-ui-test");
        SchedulePollNow();
    }

    internal void ClearPluginTitle() => TryClearHonorific();

    internal string TestContentFilterText(string text)
    {
        var match = ContentFilterMatcher.TestText(
            config.ContentFilterEntries,
            config.UseBuiltInContentFilterList,
            config.DisabledBuiltInContentFilterEntries,
            config.SmartContentFilterMatching,
            text);

        if (match == null)
            return "No blacklist match.";

        var variation = match.UsedFuzzyMatch ? " - smart variation match" : string.Empty;
        var source = match.IsBuiltIn ? "built-in: " : string.Empty;
        return $"Blocked by {source}{match.Entry} ({match.Field}){variation}";
    }

    internal bool SaveTitleProfile(string name, out int profileIndex, out string message)
    {
        profileIndex = -1;
        name = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            message = "Enter a profile name first.";
            return false;
        }

        if (name.Length > 48)
            name = name[..48];

        for (var i = 0; i < config.TitleProfiles.Count; i++)
        {
            if (!string.Equals(config.TitleProfiles[i].Name, name, StringComparison.OrdinalIgnoreCase))
                continue;

            config.TitleProfiles[i] = TitleProfile.Capture(config, name);
            SaveConfig();
            profileIndex = i;
            message = $"Updated profile '{name}'.";
            return true;
        }

        if (config.TitleProfiles.Count >= Configuration.MaxTitleProfiles)
        {
            message = $"You can save up to {Configuration.MaxTitleProfiles} profiles. Delete one first.";
            return false;
        }

        config.TitleProfiles.Add(TitleProfile.Capture(config, name));
        SaveConfig();
        profileIndex = config.TitleProfiles.Count - 1;
        message = $"Saved profile '{name}'.";
        return true;
    }

    internal bool LoadTitleProfile(int profileIndex, out string message)
    {
        if (profileIndex < 0 || profileIndex >= config.TitleProfiles.Count)
        {
            message = "Choose a saved profile first.";
            return false;
        }

        var profile = config.TitleProfiles[profileIndex];
        var requestedSupporterGradient = profile.UseSupporterGradient;
        profile.ApplyTo(config);
        SettingsChanged();

        message = requestedSupporterGradient && !config.HonorificSupporterConfirmed
            ? $"Loaded '{profile.Name}'. Supporter gradient stayed disabled until supporter access is confirmed."
            : $"Loaded profile '{profile.Name}'.";
        return true;
    }

    internal bool DeleteTitleProfile(int profileIndex, out string message)
    {
        if (profileIndex < 0 || profileIndex >= config.TitleProfiles.Count)
        {
            message = "Choose a saved profile first.";
            return false;
        }

        var name = config.TitleProfiles[profileIndex].Name;
        config.TitleProfiles.RemoveAt(profileIndex);
        SaveConfig();
        message = $"Deleted profile '{name}'.";
        return true;
    }

    internal string ExportPortableSettings()
    {
        var package = new PortableSettingsPackage
        {
            ExportedFromVersion = DisplayVersion,
            CurrentSettings = TitleProfile.Capture(config, "Current settings"),
            Profiles = new List<TitleProfile>(),
        };

        foreach (var profile in config.TitleProfiles)
            package.Profiles.Add(profile.Clone());

        return JsonSerializer.Serialize(package, PortableSettingsJsonOptions);
    }

    internal bool ImportPortableSettings(string json, out string message)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            message = "Clipboard does not contain portable SpotifyTrackHonorific settings.";
            return false;
        }

        try
        {
            var package = JsonSerializer.Deserialize<PortableSettingsPackage>(json, PortableSettingsJsonOptions);
            if (package == null ||
                !string.Equals(package.Schema, PortableSettingsPackage.ExpectedSchema, StringComparison.Ordinal) ||
                package.FormatVersion <= 0 ||
                package.FormatVersion > PortableSettingsPackage.CurrentFormatVersion ||
                package.CurrentSettings == null)
            {
                message = "Clipboard data is not a supported SpotifyTrackHonorific settings export.";
                return false;
            }

            var importedCurrent = package.CurrentSettings.Clone();
            importedCurrent.EnsureDefaults(0);
            importedCurrent.ApplyTo(config);

            config.TitleProfiles.Clear();
            if (package.Profiles != null)
            {
                foreach (var importedProfile in package.Profiles)
                {
                    if (importedProfile == null || config.TitleProfiles.Count >= Configuration.MaxTitleProfiles)
                        continue;

                    var profile = importedProfile.Clone();
                    profile.EnsureDefaults(config.TitleProfiles.Count);
                    config.TitleProfiles.Add(profile);
                }
            }

            // SettingsChanged validates/saves v10 data and refreshes the currently
            // applied title. Spotify credentials, onboarding, global enable state and
            // supporter entitlement confirmation were never part of the export.
            SettingsChanged();
            message = $"Imported display settings and {config.TitleProfiles.Count} saved profile(s). Spotify connection data was unchanged.";
            return true;
        }
        catch (JsonException ex)
        {
            message = $"Could not import settings: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Portable settings import failed");
            message = $"Could not import settings: {ex.Message}";
            return false;
        }
    }

    internal void ForgetSpotifyConnection(bool clearClientId = false)
    {
        spotify.ForgetAuthorization(clearClientId);
        ResetFailureCounter();
        ClearSpotifyCooldown();
        lastTrack = null;
        lastTrackPaused = false;
        lastTrackObservedUtcTicks = 0;
        Interlocked.Exchange(ref nextLocalRenderUtcTicks, 0);
        lastError = null;
        lastState = "Spotify connection removed";
        appliedFingerprint = null;
        TryClearHonorific();
        ScheduleNextPoll(IdlePollInterval);
        configWindow.SyncClientId();
    }

    internal void ResetDisplaySettings()
    {
        var defaults = new Configuration();

        config.Enabled = defaults.Enabled;
        config.ShowNormalTracks = defaults.ShowNormalTracks;
        config.ShowLocalTracks = defaults.ShowLocalTracks;
        config.ClearOnPause = defaults.ClearOnPause;
        config.IsPrefix = defaults.IsPrefix;
        config.TitleFormat = defaults.TitleFormat;
        config.StripBracketedTrackParts = defaults.StripBracketedTrackParts;
        config.SmartFitLongTitles = defaults.SmartFitLongTitles;

        config.UseTitleColor = defaults.UseTitleColor;
        config.TitleColor = defaults.TitleColor;
        config.UseTitleGlow = defaults.UseTitleGlow;
        config.TitleGlowColor = defaults.TitleGlowColor;
        config.UseSupporterGradient = defaults.UseSupporterGradient;
        config.UseCustomDualGradient = defaults.UseCustomDualGradient;
        config.GradientColourSet = defaults.GradientColourSet;
        config.AnimateGradient = defaults.AnimateGradient;
        config.GradientAnimationStyle = defaults.GradientAnimationStyle;
        config.GradientColorA = defaults.GradientColorA;
        config.GradientColorB = defaults.GradientColorB;
        config.GradientColorC = defaults.GradientColorC;

        // Spotify credentials, first-run completion, and the user's explicit
        // supporter-entitlement confirmation are intentionally preserved.
        SettingsChanged();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!config.Enabled)
        {
            if (hasAppliedTitle)
                TryClearHonorific();
            return;
        }

        RefreshCachedProgressTitle();

        if (DateTimeOffset.UtcNow.UtcDateTime.Ticks < Interlocked.Read(ref nextPollUtcTicks))
            return;

        // Framework ticks are frequent. The atomic gate guarantees a slow request
        // can never overlap with another poll even if completion happens off-thread.
        if (Interlocked.CompareExchange(ref pollInProgress, 1, 0) != 0)
            return;

        _ = PollSpotifyAsync();
    }

    private async Task PollSpotifyAsync()
    {
        try
        {
            var result = await spotify.PollCurrentlyPlayingAsync(lifetimeCts.Token).ConfigureAwait(false);

            switch (result.State)
            {
                case SpotifyPollState.PlayingTrack when result.Track != null:
                    MarkSpotifyPollHealthy();
                    lastTrack = result.Track;
                    lastTrackPaused = false;
                    lastTrackObservedUtcTicks = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
                    Interlocked.Exchange(ref nextLocalRenderUtcTicks, lastTrackObservedUtcTicks);
                    lastError = null;
                    ScheduleNextPoll(NormalPollInterval);

                    if (!IsTrackAllowed(result.Track))
                    {
                        lastState = result.Track.IsLocal
                            ? "Local track hidden by settings"
                            : "Normal Spotify track hidden by settings";

                        if (hasAppliedTitle)
                            await Framework.RunOnFrameworkThread(TryClearHonorific).ConfigureAwait(false);
                        appliedFingerprint = null;
                        break;
                    }

                    var playingFilterMatch = GetContentFilterMatch(result.Track);
                    if (playingFilterMatch != null)
                    {
                        lastState = $"Triggerword censored ({playingFilterMatch.Field})";

                        if (config.ContentFilterAction == 1)
                        {
                            if (hasAppliedTitle)
                                await Framework.RunOnFrameworkThread(TryClearHonorific).ConfigureAwait(false);
                            appliedFingerprint = null;
                            break;
                        }

                        if (config.ContentFilterAction == 2)
                        {
                            lastState += " - previous title kept";
                            break;
                        }
                    }
                    else
                    {
                        lastState = result.Track.IsLocal ? "Playing local track" : "Playing Spotify track";
                    }

                    var renderFingerprint = BuildRenderFingerprint(result.Track, paused: false);
                    if (!string.Equals(appliedFingerprint, renderFingerprint, StringComparison.Ordinal))
                    {
                        var title = BuildConfiguredTitle(result.Track, paused: false);
                        await Framework.RunOnFrameworkThread(() => ApplyHonorificTitle(title, renderFingerprint)).ConfigureAwait(false);
                    }
                    break;

                case SpotifyPollState.PausedTrack when result.Track != null:
                    MarkSpotifyPollHealthy();
                    lastTrack = result.Track;
                    lastTrackPaused = true;
                    lastTrackObservedUtcTicks = 0;
                    Interlocked.Exchange(ref nextLocalRenderUtcTicks, 0);
                    lastState = "Playback paused";
                    lastError = null;

                    // Spotify does not push a "playback resumed" event to the plugin.
                    // Keep checking paused playback at the normal 15-second cadence so
                    // resuming is detected promptly. Truly idle/not-playing stays at
                    // the 60-second cadence to preserve the low-quota behavior.
                    ScheduleNextPoll(NormalPollInterval);

                    if (!IsTrackAllowed(result.Track))
                    {
                        if (hasAppliedTitle)
                            await Framework.RunOnFrameworkThread(TryClearHonorific).ConfigureAwait(false);
                        appliedFingerprint = null;
                        break;
                    }

                    if (config.ClearOnPause)
                    {
                        if (hasAppliedTitle)
                            await Framework.RunOnFrameworkThread(TryClearHonorific).ConfigureAwait(false);
                        break;
                    }

                    var pausedFilterMatch = GetContentFilterMatch(result.Track);
                    if (pausedFilterMatch != null)
                    {
                        lastState = $"Triggerword censored ({pausedFilterMatch.Field})";

                        if (config.ContentFilterAction == 1)
                        {
                            if (hasAppliedTitle)
                                await Framework.RunOnFrameworkThread(TryClearHonorific).ConfigureAwait(false);
                            appliedFingerprint = null;
                            break;
                        }

                        if (config.ContentFilterAction == 2)
                        {
                            lastState += " - previous title kept";
                            break;
                        }
                    }

                    var pausedFingerprint = BuildRenderFingerprint(result.Track, paused: true);
                    if (!string.Equals(appliedFingerprint, pausedFingerprint, StringComparison.Ordinal))
                    {
                        var pausedTitle = BuildConfiguredTitle(result.Track, paused: true);
                        await Framework.RunOnFrameworkThread(() => ApplyHonorificTitle(pausedTitle, pausedFingerprint)).ConfigureAwait(false);
                    }
                    break;

                case SpotifyPollState.NotPlaying:
                    MarkSpotifyPollHealthy();
                    lastTrack = null;
                    lastTrackPaused = false;
                    lastTrackObservedUtcTicks = 0;
                    Interlocked.Exchange(ref nextLocalRenderUtcTicks, 0);
                    lastState = "Nothing playing / paused";
                    lastError = null;
                    ScheduleNextPoll(IdlePollInterval);
                    if (config.ClearOnPause && hasAppliedTitle)
                        await Framework.RunOnFrameworkThread(TryClearHonorific).ConfigureAwait(false);
                    break;

                case SpotifyPollState.NotAuthenticated:
                    ResetFailureCounter();
                    ClearSpotifyCooldown();
                    lastTrack = null;
                    lastTrackPaused = false;
                    lastTrackObservedUtcTicks = 0;
                    Interlocked.Exchange(ref nextLocalRenderUtcTicks, 0);
                    lastState = string.IsNullOrWhiteSpace(result.Error)
                        ? "Not authenticated"
                        : "Spotify re-authentication required";
                    lastError = result.Error;
                    ScheduleNextPoll(IdlePollInterval);
                    if (hasAppliedTitle)
                        await Framework.RunOnFrameworkThread(TryClearHonorific).ConfigureAwait(false);
                    break;

                case SpotifyPollState.RateLimited:
                {
                    var delay = ScheduleSpotifyFailure(
                        result.Error,
                        result.RetryAfterSeconds,
                        useExponentialBackoff: false,
                        isQuotaExceeded: false);
                    lastState = $"Spotify rate limited - retrying in {FormatRetryDelay(TimeSpan.FromSeconds(delay))}";
                    break;
                }

                case SpotifyPollState.QuotaExceeded:
                {
                    var delay = ScheduleSpotifyFailure(
                        result.Error,
                        result.RetryAfterSeconds,
                        useExponentialBackoff: false,
                        isQuotaExceeded: true);
                    lastState = $"Spotify Development Mode quota exceeded - retrying in {FormatRetryDelay(TimeSpan.FromSeconds(delay))}";
                    break;
                }

                case SpotifyPollState.TransientError:
                {
                    var delay = ScheduleSpotifyFailure(result.Error, result.RetryAfterSeconds, useExponentialBackoff: true);
                    lastState = $"Temporary Spotify error - retrying in {delay}s";
                    break;
                }

                case SpotifyPollState.Error:
                {
                    // Unknown/non-transient HTTP failures still back off so a broken
                    // endpoint or scope cannot flood Spotify or the Dalamud log. The
                    // last known-good Honorific title is deliberately retained.
                    var delay = ScheduleSpotifyFailure(result.Error, 0, useExponentialBackoff: true);
                    lastState = $"Spotify API error - retrying in {delay}s";
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
        {
            // Plugin is unloading.
        }
        catch (Exception ex)
        {
            var delay = ScheduleSpotifyFailure(ex.Message, 0, useExponentialBackoff: true);
            lastState = $"Unexpected Spotify error - retrying in {delay}s";
            Log.Error(ex, "Unexpected Spotify polling error");
        }
        finally
        {
            Volatile.Write(ref pollInProgress, 0);
        }
    }

    private void MarkSpotifyPollHealthy()
    {
        var recoveredFailures = Interlocked.Exchange(ref consecutivePollFailures, 0);
        Interlocked.Exchange(ref lastSuccessfulPollUtcTicks, DateTimeOffset.UtcNow.UtcDateTime.Ticks);
        ClearSpotifyCooldown();
        lastLoggedSpotifyError = null;

        if (recoveredFailures > 0)
            Log.Information($"Spotify polling recovered after {recoveredFailures} consecutive failure(s).");
    }

    private void ResetFailureCounter()
    {
        Interlocked.Exchange(ref consecutivePollFailures, 0);
        lastLoggedSpotifyError = null;
    }

    private int ScheduleSpotifyFailure(
        string? error,
        int minimumDelaySeconds,
        bool useExponentialBackoff,
        bool isQuotaExceeded = false)
    {
        var failureCount = Interlocked.Increment(ref consecutivePollFailures);
        var delay = Math.Max(0, minimumDelaySeconds);

        if (isQuotaExceeded)
        {
            // Spotify's Development Mode quota is separate from the rolling
            // rate-limit window. If Spotify provides Retry-After, never retry
            // earlier than requested. If it does not, back off aggressively:
            // 1h, 2h, 4h, 8h, 12h, 12h...
            var shift = Math.Min(Math.Max(0, failureCount - 1), 4);
            var quotaFallback = Math.Min(
                QuotaFallbackMaxSeconds,
                QuotaFallbackBaseSeconds * (1 << shift));
            delay = Math.Max(delay, quotaFallback);
        }
        else if (useExponentialBackoff)
        {
            // 5, 10, 20, 40, 80, 120, 120... seconds.
            var shift = Math.Min(Math.Max(0, failureCount - 1), 5);
            var exponential = Math.Min(FailureBackoffMaxSeconds, FailureBackoffBaseSeconds * (1 << shift));
            delay = Math.Max(delay, exponential);
        }
        else
        {
            // Ordinary Spotify rate limits are based on a rolling 30-second
            // request window. Retry-After is authoritative when supplied.
            delay = Math.Max(delay, RateLimitFallbackSeconds);
        }

        delay = Math.Max(1, delay);
        lastError = string.IsNullOrWhiteSpace(error) ? "Unknown Spotify error." : error;
        var retryDelay = TimeSpan.FromSeconds(delay);
        ScheduleNextPoll(retryDelay);

        if (isQuotaExceeded || !useExponentialBackoff)
            SetSpotifyCooldown(retryDelay);

        // Avoid one warning every retry forever. Log the first failure, any changed
        // error, and periodic reminders during a long outage.
        if (failureCount == 1 || failureCount % 5 == 0 || !string.Equals(lastLoggedSpotifyError, lastError, StringComparison.Ordinal))
        {
            Log.Warning($"Spotify poll failure #{failureCount}; retrying in {FormatRetryDelay(retryDelay)}: {lastError}");
            lastLoggedSpotifyError = lastError;
        }

        return delay;
    }

    private void ScheduleNextPoll(TimeSpan delay)
    {
        var when = DateTimeOffset.UtcNow.Add(delay).UtcDateTime.Ticks;
        Interlocked.Exchange(ref nextPollUtcTicks, when);
    }

    private void SchedulePollNow()
    {
        var nowTicks = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
        var cooldownTicks = Interlocked.Read(ref spotifyCooldownUntilUtcTicks);
        Interlocked.Exchange(ref nextPollUtcTicks, Math.Max(nowTicks, cooldownTicks));
    }

    private void SetSpotifyCooldown(TimeSpan delay)
    {
        var untilTicks = DateTimeOffset.UtcNow.Add(delay).UtcDateTime.Ticks;
        Interlocked.Exchange(ref spotifyCooldownUntilUtcTicks, untilTicks);
    }

    private void ClearSpotifyCooldown() =>
        Interlocked.Exchange(ref spotifyCooldownUntilUtcTicks, 0);

    private bool TryGetSpotifyCooldownRemaining(out TimeSpan remaining)
    {
        var cooldownTicks = Interlocked.Read(ref spotifyCooldownUntilUtcTicks);
        if (cooldownTicks <= 0)
        {
            remaining = TimeSpan.Zero;
            return false;
        }

        var until = new DateTimeOffset(new DateTime(cooldownTicks, DateTimeKind.Utc));
        remaining = until - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            ClearSpotifyCooldown();
            remaining = TimeSpan.Zero;
            return false;
        }

        return true;
    }

    private string BuildSpotifyFriendlyStatus()
    {
        if (!config.Enabled)
            return "Disabled";
        if (IsAuthenticating)
            return "Connecting...";
        if (!IsAuthenticated)
            return "Needs connection";
        if (Volatile.Read(ref consecutivePollFailures) > 0)
            return "Temporarily unavailable";
        if (lastState.Contains("re-authentication required", StringComparison.OrdinalIgnoreCase))
            return "Needs attention";
        return "Connected";
    }

    private string BuildReliabilityText()
    {
        if (!config.Enabled)
            return "Polling disabled";

        var now = DateTimeOffset.UtcNow;
        var failures = Volatile.Read(ref consecutivePollFailures);
        var lastSuccessTicks = Interlocked.Read(ref lastSuccessfulPollUtcTicks);
        var nextTicks = Interlocked.Read(ref nextPollUtcTicks);

        string successText;
        if (lastSuccessTicks <= 0)
        {
            successText = "no successful poll yet";
        }
        else
        {
            var successAt = new DateTimeOffset(new DateTime(lastSuccessTicks, DateTimeKind.Utc));
            successText = $"last success {FormatAge(now - successAt)} ago";
        }

        var retryDelay = new DateTimeOffset(new DateTime(nextTicks, DateTimeKind.Utc)) - now;
        if (retryDelay < TimeSpan.Zero)
            retryDelay = TimeSpan.Zero;

        if (failures > 0)
            return $"Recovering: {failures} consecutive failure(s), retry in {FormatRetryDelay(retryDelay)}, {successText}";

        if (Volatile.Read(ref pollInProgress) != 0)
            return $"Healthy: polling now, {successText}";

        return $"Healthy: {successText}";
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;
        if (age.TotalSeconds < 60)
            return $"{Math.Max(0, (int)age.TotalSeconds)}s";
        if (age.TotalMinutes < 60)
            return $"{(int)age.TotalMinutes}m";
        if (age.TotalHours < 24)
            return $"{(int)age.TotalHours}h";
        return $"{(int)age.TotalDays}d";
    }

    private static string FormatRetryDelay(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;

        if (delay.TotalDays >= 1)
            return $"{(int)delay.TotalDays}d {delay.Hours}h";
        if (delay.TotalHours >= 1)
            return $"{(int)delay.TotalHours}h {delay.Minutes}m";
        if (delay.TotalMinutes >= 1)
            return $"{(int)delay.TotalMinutes}m {delay.Seconds}s";
        return $"{Math.Max(0, (int)Math.Ceiling(delay.TotalSeconds))}s";
    }

    private SpotifyTrackInfo? GetCurrentRenderTrack()
    {
        var track = lastTrack;
        if (track == null || lastTrackPaused || lastTrackObservedUtcTicks <= 0)
            return track;

        var observedAt = new DateTimeOffset(new DateTime(lastTrackObservedUtcTicks, DateTimeKind.Utc));
        var elapsedMs = Math.Max(0, (long)(DateTimeOffset.UtcNow - observedAt).TotalMilliseconds);
        var maxProgress = track.DurationMs > 0 ? track.DurationMs : int.MaxValue;
        var progress = Math.Clamp((long)track.ProgressMs + elapsedMs, 0L, (long)maxProgress);

        return track with { ProgressMs = (int)progress };
    }

    private void RefreshCachedProgressTitle()
    {
        var track = lastTrack;
        if (track == null ||
            lastTrackPaused ||
            !TitleTemplateFormatter.UsesProgressVariable(config.TitleFormat) ||
            !IsTrackAllowed(track))
            return;

        var nowTicks = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
        if (nowTicks < Interlocked.Read(ref nextLocalRenderUtcTicks))
            return;

        Interlocked.Exchange(
            ref nextLocalRenderUtcTicks,
            DateTimeOffset.UtcNow.Add(LocalRenderRefreshInterval).UtcDateTime.Ticks);

        if (GetContentFilterMatch(track) != null && config.ContentFilterAction != 0)
            return;

        var renderTrack = GetCurrentRenderTrack();
        if (renderTrack == null)
            return;

        var fingerprint = BuildRenderFingerprint(renderTrack, paused: false);
        if (!string.Equals(appliedFingerprint, fingerprint, StringComparison.Ordinal))
            ApplyHonorificTitle(BuildConfiguredTitle(renderTrack, paused: false), fingerprint);
    }

    private bool IsTrackAllowed(SpotifyTrackInfo track) =>
        track.IsLocal ? config.ShowLocalTracks : config.ShowNormalTracks;

    private ContentFilterMatch? GetContentFilterMatch(SpotifyTrackInfo track)
    {
        if (!config.EnableContentFilter)
            return null;

        return ContentFilterMatcher.MatchTrack(
            track,
            config.ContentFilterEntries,
            config.UseBuiltInContentFilterList,
            config.DisabledBuiltInContentFilterEntries,
            config.SmartContentFilterMatching);
    }

    private string BuildConfiguredTitle(SpotifyTrackInfo track, bool paused)
    {
        var renderTrack = GetContentFilteredTrack(track);
        var formatted = TitleTemplateFormatter.Expand(config.TitleFormat, renderTrack, paused, config.StripBracketedTrackParts);
        return HonorificBridge.FitTitle(formatted, config.SmartFitLongTitles);
    }

    private SpotifyTrackInfo GetContentFilteredTrack(SpotifyTrackInfo track)
    {
        if (!config.EnableContentFilter || config.ContentFilterAction != 0)
            return track;

        var replacement = string.IsNullOrWhiteSpace(config.ContentFilterFallback)
            ? Configuration.DefaultContentFilterFallback
            : config.ContentFilterFallback.Trim();

        return ContentFilterMatcher.CensorTrack(
            track,
            config.ContentFilterEntries,
            config.UseBuiltInContentFilterList,
            config.DisabledBuiltInContentFilterEntries,
            config.SmartContentFilterMatching,
            replacement).Track;
    }

    private SpotifyTrackInfo BuildPreviewTrack() => GetCurrentRenderTrack() ?? new SpotifyTrackInfo(
        "A Very Long Example Track Title (Remastered 2026)",
        new[] { "Artist", "Featured Artist" },
        "Album",
        243000,
        83000,
        false,
        "preview");

    private string BuildPreviewExpandedTitle()
    {
        var track = GetContentFilteredTrack(BuildPreviewTrack());
        return TitleTemplateFormatter.Expand(
            config.TitleFormat,
            track,
            lastTrack != null && lastTrackPaused,
            config.StripBracketedTrackParts);
    }

    private string BuildPreviewTitle() =>
        HonorificBridge.FitTitle(BuildPreviewExpandedTitle(), config.SmartFitLongTitles);

    private string BuildRenderFingerprint(SpotifyTrackInfo track, bool paused)
    {
        var progressPart = TitleTemplateFormatter.UsesProgressVariable(config.TitleFormat)
            ? $"|progress:{track.ProgressMs / 1000}"
            : string.Empty;

        var contentFilterPart =
            $"|contentFilter:{config.EnableContentFilter}" +
            $"|contentFilterSmart:{config.SmartContentFilterMatching}" +
            $"|contentFilterAction:{config.ContentFilterAction}" +
            $"|contentFilterReplacement:{config.ContentFilterFallback}" +
            $"|contentFilterBuiltIn:{config.UseBuiltInContentFilterList}" +
            $"|contentFilterBuiltInDisabled:{config.DisabledBuiltInContentFilterEntries}" +
            $"|contentFilterEntries:{config.ContentFilterEntries}";

        var stylePart = config.UseTitleColor
            ? $"|color:{config.TitleColor.X:F4},{config.TitleColor.Y:F4},{config.TitleColor.Z:F4}|glowEnabled:{config.UseTitleGlow}|glow:{config.TitleGlowColor.X:F4},{config.TitleGlowColor.Y:F4},{config.TitleGlowColor.Z:F4}"
            : "|color:default|glowEnabled:false";

        var supporterStylePart =
            $"|supporterConfirmed:{config.HonorificSupporterConfirmed}" +
            $"|supporterGradient:{config.UseSupporterGradient}" +
            $"|customGradient:{config.UseCustomDualGradient}" +
            $"|gradientSet:{config.GradientColourSet}" +
            $"|gradientAnimationStyle:{config.GradientAnimationStyle}" +
            $"|gradientA:{config.GradientColorA.X:F4},{config.GradientColorA.Y:F4},{config.GradientColorA.Z:F4}" +
            $"|gradientB:{config.GradientColorB.X:F4},{config.GradientColorB.Y:F4},{config.GradientColorB.Z:F4}" +
            $"|gradientC:{config.GradientColorC.X:F4},{config.GradientColorC.Y:F4},{config.GradientColorC.Z:F4}";

        return $"{track.Fingerprint}|prefix:{config.IsPrefix}|paused:{paused}|strip:{config.StripBracketedTrackParts}|smartfit:{config.SmartFitLongTitles}|format:{config.TitleFormat}{stylePart}{supporterStylePart}{contentFilterPart}{progressPart}";
    }

    private void ApplyHonorificTitle(string title, string fingerprint)
    {
        try
        {
            var supporterGradient = config.HonorificSupporterConfirmed && config.UseSupporterGradient;
            var color = config.UseTitleColor ? config.TitleColor : (System.Numerics.Vector3?)null;
            System.Numerics.Vector3? glow = null;
            System.Numerics.Vector3? color3 = null;
            int? gradientColourSet = null;
            int? gradientAnimationStyle = null;

            if (supporterGradient)
            {
                // Stored numerically so we can pass Honorific's enum through IPC
                // without taking a compile-time Honorific assembly dependency.
                gradientAnimationStyle = Math.Max(0, config.GradientAnimationStyle);

                if (config.UseCustomDualGradient)
                {
                    // Honorific's custom GradientColourSet = -1 form uses THREE
                    // colours: Color + Glow + Color3. v0.0.10 only populated the
                    // latter two, which could fall back to black/white in static mode.
                    gradientColourSet = -1;
                    color = config.GradientColorA;
                    glow = config.GradientColorB;
                    color3 = config.GradientColorC;
                }
                else
                {
                    gradientColourSet = Math.Max(0, config.GradientColourSet);
                }
            }
            else if (config.UseTitleColor && config.UseTitleGlow)
            {
                glow = config.TitleGlowColor;
            }

            honorific.Set(
                title,
                config.IsPrefix,
                color,
                glow,
                gradientColourSet,
                gradientAnimationStyle,
                color3);
            appliedFingerprint = fingerprint;
            hasAppliedTitle = true;
            honorificErrorShown = false;
            Log.Information($"Applied Spotify title: {title}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Honorific.SetCharacterTitle IPC failed");
            appliedFingerprint = null;
            hasAppliedTitle = false;
            if (!honorificErrorShown)
            {
                ChatGui.PrintError("SpotifyTrackHonorific could not reach Honorific. Make sure Honorific is installed and enabled.", "SpotifyTrackHonorific");
                honorificErrorShown = true;
            }
        }
    }

    private void TryClearHonorific()
    {
        if (!hasAppliedTitle)
            return;

        try
        {
            honorific.Clear();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Honorific.ClearCharacterTitle IPC failed");
        }
        finally
        {
            hasAppliedTitle = false;
            appliedFingerprint = null;
        }
    }

    private void OnCommand(string command, string args)
    {
        var split = args.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var sub = split.Length == 0 ? "config" : split[0].ToLowerInvariant();
        var argument = split.Length > 1 ? split[1].Trim() : string.Empty;

        switch (sub)
        {
            case "config":
                OpenConfigUi();
                break;

            case "auth":
                if (string.IsNullOrWhiteSpace(argument))
                {
                    ChatGui.PrintError("Usage: /sth auth YOUR_SPOTIFY_CLIENT_ID", "SpotifyTrackHonorific");
                    return;
                }
                StartAuthentication(argument);
                break;

            case "status":
                PrintStatus();
                break;

            case "now":
                PrintNow();
                break;

            case "retry":
                RetrySpotifyNow();
                ChatGui.Print("Spotify retry scheduled immediately.");
                break;

            case "enable":
                config.Enabled = true;
                SettingsChanged();
                ChatGui.Print("SpotifyTrackHonorific enabled.");
                break;

            case "disable":
                config.Enabled = false;
                SettingsChanged();
                ChatGui.Print("SpotifyTrackHonorific disabled.");
                break;

            case "clear":
                TryClearHonorific();
                ChatGui.Print("SpotifyTrackHonorific title cleared.");
                break;

            case "ipc-test":
                ApplyHonorificTitle("♪ Spotify IPC test", "manual-ipc-test");
                ChatGui.Print("Sent an Honorific IPC test title. Use /sth clear afterward.");
                break;

            case "help":
            default:
                PrintHelp();
                break;
        }
    }

    private async Task AuthenticateAsync(string clientId)
    {
        try
        {
            ChatGui.Print($"Spotify authentication starting. Redirect URI must be registered as: {spotify.RedirectUriText}");
            ChatGui.Print("Opening Spotify in your browser...");

            await spotify.AuthenticateAsync(
                clientId,
                OpenBrowser,
                lifetimeCts.Token).ConfigureAwait(false);

            config.Enabled = true;
            config.OnboardingCompleted = true;
            SaveConfig();
            ResetFailureCounter();
            ClearSpotifyCooldown();
            lastError = null;
            lastState = "Authenticated - waiting for Spotify";
            SchedulePollNow();
            await Framework.RunOnFrameworkThread(() =>
            {
                configWindow.SyncClientId();
                ChatGui.Print("Spotify authentication succeeded. SpotifyTrackHonorific is enabled.");
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!lifetimeCts.IsCancellationRequested)
        {
            await Framework.RunOnFrameworkThread(() => ChatGui.PrintError("Spotify authentication timed out. Authenticate again from /sth or the settings window.", "SpotifyTrackHonorific")).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Spotify authentication failed");
            await Framework.RunOnFrameworkThread(() => ChatGui.PrintError($"Spotify authentication failed: {ex.Message}", "SpotifyTrackHonorific")).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref authenticationInProgress, 0);
        }
    }

    private static void OpenBrowser(Uri uri)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        });
    }

    private void PrintStatus()
    {
        var auth = spotify.HasRefreshToken ? "authenticated" : "not authenticated";
        var enabled = config.Enabled ? "enabled" : "disabled";
        ChatGui.Print($"SpotifyTrackHonorific v{DisplayVersion}: {enabled}, {auth}. State: {lastState}.");
        ChatGui.Print($"Reliability: {BuildReliabilityText()}.");
        if (!string.IsNullOrWhiteSpace(lastError))
            ChatGui.PrintError($"Last error: {lastError}", "SpotifyTrackHonorific");
    }

    private void PrintNow()
    {
        var renderTrack = GetCurrentRenderTrack();
        if (renderTrack == null)
        {
            ChatGui.Print($"No active track cached. State: {lastState}.");
            return;
        }

        ChatGui.Print($"Spotify: {renderTrack.ArtistText} - {renderTrack.Name} | Album: {renderTrack.Album} | Local: {renderTrack.IsLocal}");
        ChatGui.Print($"Honorific title: {BuildConfiguredTitle(renderTrack, lastTrackPaused)} | {(config.IsPrefix ? "Prefix" : "Suffix")}");
    }

    private static void PrintHelp()
    {
        ChatGui.Print("SpotifyTrackHonorific commands:");
        ChatGui.Print("/sth                    - open settings");
        ChatGui.Print("/sth auth <client-id>   - authenticate with Spotify using PKCE");
        ChatGui.Print("/sth status             - show auth/poll state");
        ChatGui.Print("/sth now                - show the detected current track/title");
        ChatGui.Print("/sth retry              - reset backoff and retry Spotify now");
        ChatGui.Print("/sth ipc-test           - test Honorific without Spotify");
        ChatGui.Print("/sth clear              - clear this plugin's title");
        ChatGui.Print("/sth enable|disable     - toggle Spotify polling");
    }

    private void SaveConfig() => PluginInterface.SavePluginConfig(config);
}
