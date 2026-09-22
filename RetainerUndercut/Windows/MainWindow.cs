using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using RetainerUndercut.Models;
using RetainerUndercut.Services;

namespace RetainerUndercut.Windows;

public sealed class MainWindow : Window
{
    private readonly RetainerListingScanner scanner;
    private readonly SingleListingMarketCheck marketCheck;
    private readonly AutoRetainerUndercutRunner autoRunner;
    private readonly MultiRetainerUndercutRunner multiRunner;
    private readonly Configuration configuration;

    private string matchlistInput = string.Empty;
    private string matchlistFeedback = string.Empty;

    public MainWindow(
        RetainerListingScanner scanner,
        SingleListingMarketCheck marketCheck,
        AutoRetainerUndercutRunner autoRunner,
        MultiRetainerUndercutRunner multiRunner,
        Configuration configuration)
        : base("Retainer Undercut — v0.1.0###RetainerUndercutMain")
    {
        this.scanner = scanner;
        this.marketCheck = marketCheck;
        this.autoRunner = autoRunner;
        this.multiRunner = multiRunner;
        this.configuration = configuration;

        Size = new Vector2(1120, 790);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 6f));

        DrawHeader();
        ImGui.Spacing();

        if (ImGui.BeginTabBar("##RetainerUndercutMainTabs"))
        {
            if (ImGui.BeginTabItem("Dashboard"))
            {
                DrawRunAndPreviewTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem($"Item Rules ({configuration.ItemRules.Count})###ItemRulesTab"))
            {
                DrawItemRulesTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Pricing & Safety"))
            {
                DrawPricingAndMatchlistTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Developer"))
            {
                DrawDiagnosticsTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.PopStyleVar(3);
    }

    private void DrawHeader()
    {
        var busy = multiRunner.IsRunning || autoRunner.IsRunning || marketCheck.IsBusy;
        var state = multiRunner.IsRunning
            ? (multiRunner.DryRunMode ? "PREVIEWING ALL RETAINERS" : "UPDATING ALL RETAINERS")
            : autoRunner.IsRunning
                ? (autoRunner.DryRunMode ? "PREVIEWING CURRENT RETAINER" : "UPDATING CURRENT RETAINER")
                : marketCheck.IsBusy
                    ? "CHECKING ONE ITEM"
                    : "READY";

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.40f, 0.88f, 0.68f, 1f));
        ImGui.TextUnformatted("Retainer Undercut");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.TextDisabled("v0.1.0");
        ImGui.TextDisabled("Preview first, then apply. Advanced details stay out of the way until you need them.");
        ImGui.Separator();

        if (ImGui.BeginTable("##HeaderStatus", 4, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableNextRow();
            DrawStatusCell("STATUS", state, busy ? "Automation is controlling the retainer UI" : "Waiting for you");
            DrawStatusCell("RETAINERS", $"{multiRunner.EnabledRetainerCount}/{multiRunner.AvailableRetainers.Count} enabled", $"{multiRunner.EnabledListingCount} listing(s) in the next all-retainer run");
            DrawStatusCell("PRICING", BuildHumanPricingSummary(), configuration.PriceRecoveryEnabled ? "Price Recovery is enabled" : "Only lowers prices unless Recovery is enabled");
            DrawStatusCell("SAFETY", BuildHumanSafetySummary(), $"{configuration.ItemRules.Count} item rule(s) • {configuration.MatchlistedRetainerNames.Count} protected seller(s)");
            ImGui.EndTable();
        }
    }

    private static void DrawStatusCell(string label, string value, string detail)
    {
        ImGui.TableNextColumn();
        ImGui.TextDisabled(label);
        ImGui.TextUnformatted(value);
        ImGui.TextDisabled(detail);
    }

    private void DrawRunAndPreviewTab()
    {
        DrawSectionHeader("Run prices", "Preview is read-only. Apply changes only after the preview looks right.");
        DrawRunControlBar();
        ImGui.Spacing();

        DrawRetainerOverview();
        ImGui.Spacing();

        DrawRunProgressAndCounters();
        ImGui.Spacing();

        DrawLatestSuggestions();
        ImGui.Spacing();
        DrawLatestRunChanges();
    }

    private void DrawRunControlBar()
    {
        var allCanStart = multiRunner.CanStartAll;
        var currentCanStart = !multiRunner.IsRunning && !autoRunner.IsRunning && !marketCheck.IsBusy &&
                              scanner.SellListVisible && scanner.MarketContainerLoaded && scanner.UiOrderMappingReady && scanner.Listings.Count > 0;

        if (!multiRunner.IsRunning && !autoRunner.IsRunning)
        {
            if (ImGui.Button("Refresh retainer list"))
                multiRunner.RefreshRetainers();
            ImGui.SameLine();
            ImGui.TextDisabled("Use this after opening the Summoning Bell retainer list.");

            ImGui.Spacing();
            ImGui.TextUnformatted("Preview prices — safe, no prices are written");
            if (!allCanStart) ImGui.BeginDisabled();
            if (ImGui.Button("Preview all enabled retainers##preview_all"))
                multiRunner.Start(dryRun: true);
            if (!allCanStart) ImGui.EndDisabled();
            ImGui.SameLine();
            if (!currentCanStart) ImGui.BeginDisabled();
            if (ImGui.Button("Preview current retainer##preview_current"))
                autoRunner.Start(dryRun: true);
            if (!currentCanStart) ImGui.EndDisabled();

            ImGui.Spacing();
            ImGui.TextUnformatted("Apply prices — actually changes listings");
            if (!allCanStart) ImGui.BeginDisabled();
            DrawLiveButton("Apply to all enabled retainers##live_all", () => multiRunner.Start(dryRun: false));
            if (!allCanStart) ImGui.EndDisabled();
            ImGui.SameLine();
            if (!currentCanStart) ImGui.BeginDisabled();
            DrawLiveButton("Apply to current retainer##live_current", () => autoRunner.Start(dryRun: false));
            if (!currentCanStart) ImGui.EndDisabled();

            if (!allCanStart)
                ImGui.TextDisabled($"All retainers unavailable: {multiRunner.StartBlockReason}");
            if (!currentCanStart)
                ImGui.TextDisabled("Current retainer unavailable: open its sell list and wait until the rows are detected.");
            else
                ImGui.TextDisabled($"Current retainer ready: {scanner.ActiveRetainerName} • {scanner.Listings.Count} listing(s)");
        }
        else
        {
            if (multiRunner.IsRunning)
            {
                DrawStopButton(multiRunner.DryRunMode ? "Stop preview##stop_all" : "EMERGENCY STOP##stop_all", multiRunner.RequestStop);
                ImGui.SameLine();
                ImGui.TextUnformatted($"{multiRunner.CurrentRetainerNumber}/{multiRunner.TotalRetainers} • {multiRunner.CurrentRetainerName} • {FormatDuration(multiRunner.RunElapsed)}");
            }
            else if (autoRunner.IsRunning)
            {
                DrawStopButton(autoRunner.DryRunMode ? "Stop preview##stop_current" : "EMERGENCY STOP##stop_current", autoRunner.RequestStop);
                ImGui.SameLine();
                ImGui.TextUnformatted($"{autoRunner.CurrentNumber}/{autoRunner.TotalItems} • {autoRunner.CurrentItemName} • {FormatDuration(autoRunner.RunElapsed)}");
            }
        }
    }

    private void DrawRetainerOverview()
    {
        DrawSectionHeader("Retainers", "Enable/disable retainers here. The selection persists between reloads.");

        if (multiRunner.AvailableRetainers.Count == 0)
        {
            ImGui.TextDisabled("No retainers loaded yet. Open the Summoning Bell list and press Refresh retainers from bell.");
            return;
        }

        if (!multiRunner.IsRunning)
        {
            if (ImGui.SmallButton("Enable all"))
                multiRunner.SetAllAvailableRetainersEnabled(true);
            ImGui.SameLine();
            if (ImGui.SmallButton("Disable all"))
                multiRunner.SetAllAvailableRetainersEnabled(false);
            ImGui.SameLine();
            ImGui.TextDisabled($"{multiRunner.EnabledRetainerCount} enabled • {multiRunner.EnabledListingCount} listings in next run");
        }

        if (ImGui.BeginTable("##RetainerOverview", 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Run", ImGuiTableColumnFlags.WidthFixed, 52);
            ImGui.TableSetupColumn("Retainer", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Listings", ImGuiTableColumnFlags.WidthFixed, 85);
            ImGui.TableSetupColumn("Last result", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            ulong changedRetainerId = 0;
            var changedEnabled = false;

            foreach (var retainer in multiRunner.AvailableRetainers)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                var enabled = retainer.Enabled;
                if (multiRunner.IsRunning)
                    ImGui.BeginDisabled();
                if (ImGui.Checkbox($"##retainer_enabled_{retainer.RetainerId:X}", ref enabled) && enabled != retainer.Enabled)
                {
                    changedRetainerId = retainer.RetainerId;
                    changedEnabled = enabled;
                }
                if (multiRunner.IsRunning)
                    ImGui.EndDisabled();

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(retainer.Name);
                if (string.Equals(multiRunner.CurrentRetainerName, retainer.Name, StringComparison.Ordinal) && multiRunner.IsRunning)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled("← active");
                }

                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{retainer.MarketItemCount}/20");

                ImGui.TableNextColumn();
                var summary = multiRunner.Summaries.LastOrDefault(x => string.Equals(x.Name, retainer.Name, StringComparison.Ordinal));
                if (summary.Name is null)
                {
                    ImGui.TextDisabled("—");
                }
                else if (summary.WasEmpty)
                {
                    ImGui.TextDisabled("EMPTY / skipped");
                }
                else
                {
                    ImGui.TextWrapped(BuildRetainerSummary(summary, multiRunner.DryRunMode));
                }
            }

            if (changedRetainerId != 0)
                multiRunner.SetRetainerEnabled(changedRetainerId, changedEnabled);

            ImGui.EndTable();
        }
    }

    private void DrawRunProgressAndCounters()
    {
        DrawSectionHeader("Latest run", "The important results stay visible; technical counters are tucked underneath.");

        var preferMulti = PreferMultiRunAsLatest();
        if (preferMulti && (multiRunner.TotalRetainers > 0 || multiRunner.Summaries.Count > 0))
        {
            var actionLabel = multiRunner.DryRunMode ? "Would change" : "Changed";
            var actionValue = multiRunner.DryRunMode ? multiRunner.TotalWouldChange : multiRunner.TotalChanged;
            var protectionCount = multiRunner.TotalMatchlistProtected + multiRunner.TotalSafetyFloor + multiRunner.TotalMaxDropBlocked + multiRunner.TotalOutlierBlocked + multiRunner.TotalPriceRecoveryBlocked + multiRunner.TotalIgnoredByRule;
            var problems = multiRunner.TotalMarketThrottled + multiRunner.TotalMissingListings + multiRunner.TotalSkippedFailed;

            var recoveryLabel = multiRunner.DryRunMode ? "Would recover" : "Price recovered";
            DrawCounterTable(new[]
            {
                ("Checked", multiRunner.TotalListingsChecked),
                (actionLabel, actionValue),
                ("Already cheapest", multiRunner.TotalAlreadyCheapest),
                (recoveryLabel, multiRunner.TotalPriceRecovered),
                ("Protected / skipped", protectionCount),
                ("Problems", problems),
            });

            if (ImGui.CollapsingHeader("Detailed counters##multi_details"))
            {
                DrawCounterTable(new[]
                {
                    ("Matchlist", multiRunner.TotalMatchlistProtected),
                    ("Ignored by rule", multiRunner.TotalIgnoredByRule),
                    ("No competitor", multiRunner.TotalNoCompetitor),
                    ("Minimum price", multiRunner.TotalSafetyFloor),
                    ("Large drop blocked", multiRunner.TotalMaxDropBlocked),
                    ("Suspicious price", multiRunner.TotalOutlierBlocked),
                    ("Large raise blocked", multiRunner.TotalPriceRecoveryBlocked),
                    ("Market throttled", multiRunner.TotalMarketThrottled),
                    ("Missing listing", multiRunner.TotalMissingListings),
                    ("Failed", multiRunner.TotalSkippedFailed),
                    ("Retries", multiRunner.TotalRetries),
                });
            }

            ImGui.TextDisabled(multiRunner.Status);
            if (!multiRunner.IsRunning && multiRunner.LastRunFinishedUtc is { } finished)
                ImGui.TextDisabled($"Finished {finished.ToLocalTime():HH:mm:ss} • {FormatDuration(multiRunner.RunElapsed)}");
            return;
        }

        if (autoRunner.TotalItems > 0 || autoRunner.PreviewResults.Count > 0)
        {
            var actionLabel = autoRunner.DryRunMode ? "Would change" : "Changed";
            var actionValue = autoRunner.DryRunMode ? autoRunner.WouldChangeCount : autoRunner.ChangedCount;
            var protectionCount = autoRunner.MatchlistProtectedCount + autoRunner.SafetyFloorCount + autoRunner.MaxDropBlockedCount + autoRunner.OutlierBlockedCount + autoRunner.PriceRecoveryBlockedCount + autoRunner.IgnoredByRuleCount;
            var problems = autoRunner.MarketThrottledCount + autoRunner.MissingListingCount + autoRunner.FailedSkippedCount;

            var recoveryLabel = autoRunner.DryRunMode ? "Would recover" : "Price recovered";
            DrawCounterTable(new[]
            {
                ("Checked", autoRunner.ProcessedItemsCount),
                (actionLabel, actionValue),
                ("Already cheapest", autoRunner.AlreadyCheapestCount),
                (recoveryLabel, autoRunner.PriceRecoveredCount),
                ("Protected / skipped", protectionCount),
                ("Problems", problems),
            });

            if (ImGui.CollapsingHeader("Detailed counters##single_details"))
            {
                DrawCounterTable(new[]
                {
                    ("Matchlist", autoRunner.MatchlistProtectedCount),
                    ("Ignored by rule", autoRunner.IgnoredByRuleCount),
                    ("No competitor", autoRunner.NoCompetitorCount),
                    ("Minimum price", autoRunner.SafetyFloorCount),
                    ("Large drop blocked", autoRunner.MaxDropBlockedCount),
                    ("Suspicious price", autoRunner.OutlierBlockedCount),
                    ("Large raise blocked", autoRunner.PriceRecoveryBlockedCount),
                    ("Market throttled", autoRunner.MarketThrottledCount),
                    ("Missing listing", autoRunner.MissingListingCount),
                    ("Failed", autoRunner.FailedSkippedCount),
                    ("Retries", autoRunner.RetryCount),
                });
            }

            ImGui.TextDisabled(autoRunner.Status);
            return;
        }

        ImGui.TextDisabled("No run yet. Start with Preview to see exactly what the plugin wants to do.");
    }

    private static void DrawCounterTable(IEnumerable<(string Label, int Value)> counters)
    {
        var data = counters.ToArray();
        const int columns = 6;
        if (!ImGui.BeginTable("##RunCounters", columns, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchSame))
            return;

        for (var i = 0; i < data.Length; i++)
        {
            if (i % columns == 0)
                ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextDisabled(data[i].Label);
            ImGui.TextUnformatted(data[i].Value.ToString("N0"));
        }

        ImGui.EndTable();
    }

    private void DrawLatestSuggestions()
    {
        var results = PreferMultiRunAsLatest()
            ? multiRunner.PreviewResults
            : autoRunner.PreviewResults;

        DrawSectionHeader("Preview suggestions", "After a Preview, every item is grouped by retainer with its current price, market reference, and proposed action.");

        if (results.Count == 0)
        {
            ImGui.TextDisabled("No Preview results yet. Use one of the Preview buttons above to populate this section.");
            return;
        }

        foreach (var group in results.GroupBy(x => x.RetainerName))
        {
            var wouldChange = group.Count(x => x.WouldChange);
            var protectedCount = group.Count(x => x.MatchlistProtected || x.SafetyFloorProtected || x.MaxDropBlocked || x.OutlierBlocked || x.PriceRecoveryBlocked);
            var label = $"{group.Key} — {group.Count()} checked • {wouldChange} would change • {protectedCount} protected##preview_{group.Key}";
            if (!ImGui.CollapsingHeader(label, ImGuiTreeNodeFlags.DefaultOpen))
                continue;

            DrawSuggestionTable(group.ToArray(), group.Key);
            ImGui.Spacing();
        }
    }

    private static string BuildRetainerSummary(RetainerRunSummary summary, bool dryRun)
    {
        var parts = new List<string> { $"{summary.Listings} checked" };
        if (dryRun)
        {
            if (summary.WouldChange > 0) parts.Add($"{summary.WouldChange} would change");
        }
        else if (summary.Changed > 0) parts.Add($"{summary.Changed} changed");
        if (summary.AlreadyCheapest > 0) parts.Add($"{summary.AlreadyCheapest} cheapest");
        if (summary.NoCompetitor > 0) parts.Add($"{summary.NoCompetitor} no competitor");
        if (summary.MatchlistProtected > 0) parts.Add($"{summary.MatchlistProtected} matchlist");
        if (summary.SafetyFloor > 0) parts.Add($"{summary.SafetyFloor} floor");
        if (summary.MaxDropBlocked > 0) parts.Add($"{summary.MaxDropBlocked} max-drop");
        if (summary.OutlierBlocked > 0) parts.Add($"{summary.OutlierBlocked} outlier");
        if (summary.PriceRecovered > 0) parts.Add(dryRun ? $"{summary.PriceRecovered} would recover" : $"{summary.PriceRecovered} recovered");
        if (summary.PriceRecoveryBlocked > 0) parts.Add($"{summary.PriceRecoveryBlocked} recovery blocked");
        if (summary.IgnoredByRule > 0) parts.Add($"{summary.IgnoredByRule} ignored");
        if (summary.MarketThrottled > 0) parts.Add($"{summary.MarketThrottled} throttled");
        if (summary.MissingListings > 0) parts.Add($"{summary.MissingListings} missing");
        if (summary.SkippedFailed > 0) parts.Add($"{summary.SkippedFailed} failed");
        if (summary.Retries > 0) parts.Add($"{summary.Retries} retries");
        return string.Join(" • ", parts);
    }

    private void DrawLatestRunChanges()
    {
        if (!ImGui.CollapsingHeader("Latest run changes"))
            return;

        ImGui.TextDisabled("Last 5 completed runs, grouped by retainer. Open this when you want the exact before → after changes and why they happened.");
        if (configuration.RunHistory.Count == 0)
        {
            ImGui.TextDisabled("No completed run history yet.");
            return;
        }

        for (var runIndex = 0; runIndex < configuration.RunHistory.Count; runIndex++)
        {
            var run = configuration.RunHistory[runIndex];
            var finished = run.FinishedAtUtc.ToLocalTime();
            var mode = run.DryRun ? "DRY" : "LIVE";
            var actionCount = run.DryRun ? run.WouldChange : run.Changed;
            var runLabel = $"{finished:yyyy-MM-dd HH:mm:ss} • {mode} • {run.Scope} • {run.Checked} checked • {actionCount} {(run.DryRun ? "would change" : "changed")}##history_run_{runIndex}_{run.FinishedAtUtc.UtcDateTime.Ticks}";
            var flags = runIndex == 0 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
            if (!ImGui.CollapsingHeader(runLabel, flags))
                continue;

            ImGui.TextDisabled($"Duration {FormatDuration(TimeSpan.FromMilliseconds(run.DurationMilliseconds))} • {(run.DryRun ? "would recover" : "recovered")} {run.PriceRecovered} • recovery blocked {run.PriceRecoveryBlocked} • outlier blocked {run.OutlierBlocked}");
            foreach (var retainer in run.Retainers)
            {
                var retainerLabel = $"{retainer.RetainerName} — {retainer.Changes.Count} recorded change(s)/decision(s)##history_retainer_{runIndex}_{retainer.RetainerName}";
                if (!ImGui.CollapsingHeader(retainerLabel, runIndex == 0 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None))
                    continue;

                if (retainer.Changes.Count == 0)
                {
                    ImGui.TextDisabled("No changed or safety-blocked listings on this retainer.");
                    continue;
                }

                DrawRunHistoryChangeTable(retainer.Changes, $"{runIndex}_{retainer.RetainerName}");
            }
        }
    }

    private static void DrawRunHistoryChangeTable(IReadOnlyList<RunChangeRecord> changes, string id)
    {
        if (!ImGui.BeginTable(
                $"##RunHistoryChanges_{id}",
                6,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 2.0f);
        ImGui.TableSetupColumn("Before", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("After / proposed", ImGuiTableColumnFlags.WidthFixed, 105);
        ImGui.TableSetupColumn("Difference", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("What happens", ImGuiTableColumnFlags.WidthStretch, 1.3f);
        ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.WidthStretch, 2.3f);
        ImGui.TableHeadersRow();

        foreach (var change in changes)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.TextUnformatted($"{change.ItemName}{(change.IsHq ? " HQ" : string.Empty)}");
            ImGui.TableNextColumn(); ImGui.TextUnformatted($"{change.PreviousPrice:N0}");
            ImGui.TableNextColumn(); ImGui.TextUnformatted(change.ResultPrice is { } result ? $"{result:N0}" : "—");
            ImGui.TableNextColumn();
            if (change.ResultPrice is { } resultPrice)
            {
                var difference = (long)resultPrice - (long)change.PreviousPrice;
                ImGui.TextUnformatted($"{difference:+#,0;-#,0;0}");
            }
            else
            {
                ImGui.TextDisabled("—");
            }
            ImGui.TableNextColumn(); ImGui.TextUnformatted(change.Decision);
            ImGui.TableNextColumn(); ImGui.TextWrapped(change.Reason);
        }

        ImGui.EndTable();
    }

    private void DrawSuggestionTable(IReadOnlyList<DryRunPreviewResult> results, string id)
    {
        if (!ImGui.BeginTable(
                $"##SuggestionTable_{id}",
                7,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp,
                new Vector2(0, Math.Min(330f, 46f + (results.Count * 26f)))))
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 2.0f);
        ImGui.TableSetupColumn("Current", ImGuiTableColumnFlags.WidthFixed, 88);
        ImGui.TableSetupColumn("Cheapest market", ImGuiTableColumnFlags.WidthFixed, 105);
        ImGui.TableSetupColumn("Suggested price", ImGuiTableColumnFlags.WidthFixed, 105);
        ImGui.TableSetupColumn("What happens", ImGuiTableColumnFlags.WidthStretch, 1.3f);
        ImGui.TableSetupColumn("Pricing rule", ImGuiTableColumnFlags.WidthStretch, 1.5f);
        ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.WidthStretch, 2.0f);
        ImGui.TableHeadersRow();

        foreach (var result in results)
        {
            var resolved = configuration.ResolveItemPricing(result.ItemId, result.IsHq);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{result.ItemName}{(result.IsHq ? " HQ" : string.Empty)}");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{result.CurrentPrice:N0}");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(result.LowestCompetitorPrice is { } competitor ? $"{competitor:N0}" : "—");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(result.ProposedPrice is { } proposed ? $"{proposed:N0}" : "—");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(BuildDecisionLabel(result));

            ImGui.TableNextColumn();
            ImGui.TextWrapped(resolved.Ignore ? "IGNORE" : resolved.HasItemRule ? resolved.RuleSummary : resolved.PricingModeLabel);

            ImGui.TableNextColumn();
            ImGui.TextWrapped(result.Reason);
        }

        ImGui.EndTable();
    }

    private static string BuildDecisionLabel(DryRunPreviewResult result)
    {
        if (result.PriceRecoveryBlocked)
            return "Keep price — raise too large";
        if (result.PriceRecovery && result.WouldChange)
            return "Raise price safely";
        if (result.OutlierBlocked)
            return "Keep price — suspicious market";
        if (result.MaxDropBlocked)
            return "Keep price — drop too large";
        if (result.MatchlistProtected && result.WouldChange)
            return "Match protected seller";
        if (result.MatchlistProtected)
            return "Already matches protected seller";
        if (result.SafetyFloorProtected && result.WouldChange)
            return "Lower to minimum price";
        if (result.SafetyFloorProtected)
            return "Minimum price protected";
        if (result.WouldChange)
            return "Lower price";
        return "Keep current price";
    }

    private void DrawCurrentListingsCompact()
    {
        if (!ImGui.BeginTable("##CurrentListingsCompact", 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable,
                new Vector2(0, Math.Min(260f, 46f + (scanner.Listings.Count * 25f)))))
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Current", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("Rule", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableHeadersRow();

        foreach (var listing in scanner.Listings.OrderBy(x => x.UiRow))
        {
            var resolved = configuration.ResolveItemPricing(listing.ItemId, listing.IsHq);
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.TextUnformatted($"{listing.ItemName}{(listing.IsHq ? " HQ" : string.Empty)}");
            ImGui.TableNextColumn(); ImGui.TextUnformatted($"{listing.UnitPrice:N0}");
            ImGui.TableNextColumn(); ImGui.TextWrapped(resolved.Ignore ? "IGNORE" : resolved.HasItemRule ? resolved.RuleSummary : resolved.PricingModeLabel);
            ImGui.TableNextColumn(); ImGui.TextDisabled(resolved.HasItemRule ? "custom" : "global");
        }

        ImGui.EndTable();
    }

    private void DrawItemRulesTab()
    {
        DrawSectionHeader("Items on the open retainer", "Add special behavior only where you need it. HQ and NQ can have separate rules.");
        DrawCurrentRetainerRuleTable();
        ImGui.Spacing();
        DrawSectionHeader("Custom item rules", "Everything without a custom rule simply uses the global Pricing & Safety settings.");
        DrawConfiguredRulesEditor();
    }

    private void DrawCurrentRetainerRuleTable()
    {
        var editingLocked = multiRunner.IsRunning || autoRunner.IsRunning || marketCheck.IsBusy;
        var currentItems = scanner.Listings
            .Where(x => x.ItemId != 0)
            .GroupBy(x => (x.ItemId, x.IsHq))
            .Select(x => x.First())
            .OrderBy(x => x.ItemName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.IsHq)
            .ToArray();

        if (currentItems.Length == 0)
        {
            ImGui.TextDisabled("Open a retainer sell list to see its items here.");
            return;
        }

        ImGui.TextDisabled($"{scanner.ActiveRetainerName} • {currentItems.Length} unique item/quality entries");

        if (ImGui.BeginTable("##CurrentRuleItems", 5,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable,
                new Vector2(0, Math.Min(310f, 46f + (currentItems.Length * 28f)))))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 2f);
            ImGui.TableSetupColumn("Current", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("Pricing behavior", ImGuiTableColumnFlags.WidthStretch, 2f);
            ImGui.TableSetupColumn("Uses", ImGuiTableColumnFlags.WidthFixed, 95);
            ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 105);
            ImGui.TableHeadersRow();

            foreach (var listing in currentItems)
            {
                var existing = configuration.FindItemRule(listing.ItemId, listing.IsHq);
                var resolved = configuration.ResolveItemPricing(listing.ItemId, listing.IsHq);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{listing.ItemName}{(listing.IsHq ? " HQ" : string.Empty)}");
                ImGui.TextDisabled($"Item #{listing.ItemId}");

                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{listing.UnitPrice:N0}");

                ImGui.TableNextColumn();
                ImGui.TextWrapped(resolved.Ignore ? "IGNORE" : resolved.HasItemRule ? resolved.RuleSummary : resolved.PricingModeLabel);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(existing is null ? "Global" : existing.Ignore ? "Do not touch" : "Custom");

                ImGui.TableNextColumn();
                if (editingLocked)
                    ImGui.BeginDisabled();
                if (existing is null)
                {
                    if (ImGui.SmallButton($"Customize##add_rule_{listing.ItemId}_{listing.IsHq}"))
                        configuration.GetOrCreateItemRule(listing.ItemId, listing.IsHq, listing.ItemName);
                }
                else
                {
                    ImGui.TextDisabled("configured below");
                }
                if (editingLocked)
                    ImGui.EndDisabled();
            }

            ImGui.EndTable();
        }

        if (editingLocked)
            ImGui.TextDisabled("Rule editing is locked while a pricing operation is active.");
    }

    private void DrawConfiguredRulesEditor()
    {
        var editingLocked = multiRunner.IsRunning || autoRunner.IsRunning || marketCheck.IsBusy;

        if (configuration.ItemRules.Count == 0)
        {
            ImGui.TextDisabled("No custom item rules yet.");
            return;
        }

        if (editingLocked)
            ImGui.BeginDisabled();

        ItemPricingRule? removeRule = null;
        foreach (var rule in configuration.ItemRules.ToArray())
        {
            var resolved = configuration.ResolveItemPricing(rule.ItemId, rule.IsHq);
            var state = rule.Ignore ? "Do not touch" : resolved.RuleSummary;
            var treeLabel = $"{rule.ItemName}{(rule.IsHq ? " HQ" : string.Empty)} — {state}##item_rule_{rule.ItemId}_{rule.IsHq}";
            if (!ImGui.CollapsingHeader(treeLabel))
                continue;

            ImGui.TextDisabled("Only enabled overrides below replace the global setting for this exact item + quality.");

            var ignore = rule.Ignore;
            if (ImGui.Checkbox($"Do not touch this item##ignore_{rule.ItemId}_{rule.IsHq}", ref ignore))
            {
                rule.Ignore = ignore;
                configuration.Save();
            }
            ImGui.TextDisabled("When enabled, the item is skipped before Compare Prices is opened.");

            ImGui.Spacing();
            var overridePricing = rule.OverridePricing;
            if (ImGui.Checkbox($"Use a different undercut for this item##pricing_override_{rule.ItemId}_{rule.IsHq}", ref overridePricing))
            {
                rule.OverridePricing = overridePricing;
                configuration.Save();
            }
            if (rule.OverridePricing)
            {
                if (ImGui.Button($"Fixed gil##rule_fixed_{rule.ItemId}_{rule.IsHq}"))
                {
                    rule.PricingMode = UndercutPricingMode.FixedAmount;
                    configuration.Save();
                }
                ImGui.SameLine();
                if (ImGui.Button($"Percentage##rule_percent_{rule.ItemId}_{rule.IsHq}"))
                {
                    rule.PricingMode = UndercutPricingMode.Percentage;
                    configuration.Save();
                }
                ImGui.SameLine();
                if (rule.PricingMode == UndercutPricingMode.FixedAmount)
                {
                    var amount = rule.FixedUndercutAmount;
                    ImGui.SetNextItemWidth(110f);
                    if (ImGui.InputInt($"gil below##rule_amount_{rule.ItemId}_{rule.IsHq}", ref amount, 1, 10))
                    {
                        rule.FixedUndercutAmount = Math.Clamp(amount, 1, 999_999_999);
                        configuration.Save();
                    }
                }
                else
                {
                    var percentage = rule.PercentageUndercut;
                    ImGui.SetNextItemWidth(90f);
                    if (ImGui.InputInt($"% below##rule_pct_{rule.ItemId}_{rule.IsHq}", ref percentage, 1, 5))
                    {
                        rule.PercentageUndercut = Math.Clamp(percentage, 1, 99);
                        configuration.Save();
                    }
                }
                ImGui.TextDisabled("This replaces the global undercut amount for this item only.");
            }

            ImGui.Spacing();
            var overrideFloor = rule.OverrideMinimumPrice;
            if (ImGui.Checkbox($"Use a different minimum price##floor_override_{rule.ItemId}_{rule.IsHq}", ref overrideFloor))
            {
                rule.OverrideMinimumPrice = overrideFloor;
                configuration.Save();
            }
            if (rule.OverrideMinimumPrice)
            {
                ImGui.SameLine();
                var floor = rule.MinimumPriceGil;
                ImGui.SetNextItemWidth(130f);
                if (ImGui.InputInt($"gil minimum##rule_floor_{rule.ItemId}_{rule.IsHq}", ref floor, 100, 1000))
                {
                    rule.MinimumPriceGil = Math.Clamp(floor, 1, 999_999_999);
                    configuration.Save();
                }
                ImGui.TextDisabled("The plugin will not lower this item below that price.");
            }

            ImGui.Spacing();
            var overrideMaxDrop = rule.OverrideMaxDrop;
            if (ImGui.Checkbox($"Use a different maximum price drop##maxdrop_override_{rule.ItemId}_{rule.IsHq}", ref overrideMaxDrop))
            {
                rule.OverrideMaxDrop = overrideMaxDrop;
                configuration.Save();
            }
            if (rule.OverrideMaxDrop)
            {
                ImGui.SameLine();
                var maxDrop = rule.MaxDropPercent;
                ImGui.SetNextItemWidth(90f);
                if (ImGui.InputInt($"% maximum##rule_maxdrop_{rule.ItemId}_{rule.IsHq}", ref maxDrop, 1, 5))
                {
                    rule.MaxDropPercent = Math.Clamp(maxDrop, 1, 99);
                    configuration.Save();
                }
                ImGui.TextDisabled("A larger one-run drop is skipped instead of being applied.");
            }

            ImGui.Spacing();
            var effective = configuration.ResolveItemPricing(rule.ItemId, rule.IsHq);
            ImGui.TextUnformatted("Effective behavior");
            ImGui.TextDisabled(BuildHumanItemRuleSummary(effective));

            if (ImGui.SmallButton($"Remove custom rule##remove_rule_{rule.ItemId}_{rule.IsHq}"))
                removeRule = rule;
            ImGui.Spacing();
        }

        if (removeRule is not null)
            configuration.RemoveItemRule(removeRule.ItemId, removeRule.IsHq);

        if (editingLocked)
        {
            ImGui.EndDisabled();
            ImGui.TextDisabled("Item rules are locked while a pricing operation is active.");
        }
    }

    private void DrawPricingAndMatchlistTab()
    {
        DrawSectionHeader("Pricing & Safety", "Global defaults for normal items. Every option below explains what it changes in plain language.");
        DrawPricingPanel();
        ImGui.Spacing();
        DrawSectionHeader("Friend / FC price protection", "Retainers on this list are matched at the cheapest tier instead of being undercut.");
        DrawMatchlistPanel();
    }

    private void DrawPricingPanel()
    {
        var editingLocked = multiRunner.IsRunning || autoRunner.IsRunning || marketCheck.IsBusy;
        if (editingLocked)
            ImGui.BeginDisabled();

        if (ImGui.CollapsingHeader("How should I undercut?", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextUnformatted("Undercut style");
            ImGui.TextDisabled("Fixed gil subtracts a set amount. Percentage subtracts a percentage of the competitor price.");
            if (ImGui.Button("Fixed gil##pricing_fixed"))
            {
                configuration.PricingMode = UndercutPricingMode.FixedAmount;
                configuration.Save();
            }
            ImGui.SameLine();
            if (ImGui.Button("Percentage##pricing_percent"))
            {
                configuration.PricingMode = UndercutPricingMode.Percentage;
                configuration.Save();
            }

            ImGui.Spacing();
            if (configuration.PricingMode == UndercutPricingMode.FixedAmount)
            {
                ImGui.TextUnformatted("How many gil below the competitor?");
                var amount = configuration.FixedUndercutAmount;
                ImGui.SetNextItemWidth(150f);
                if (ImGui.InputInt("gil##global_undercut", ref amount, 1, 10))
                {
                    configuration.FixedUndercutAmount = Math.Clamp(amount, 1, 999_999_999);
                    configuration.Save();
                }
                var example = Math.Max(1, 20_000 - configuration.FixedUndercutAmount);
                ImGui.TextDisabled($"Example: competitor 20,000 → your target {example:N0} gil.");
            }
            else
            {
                ImGui.TextUnformatted("How many percent below the competitor?");
                var percent = configuration.PercentageUndercut;
                ImGui.SetNextItemWidth(120f);
                if (ImGui.InputInt("%##global_percent", ref percent, 1, 5))
                {
                    configuration.PercentageUndercut = Math.Clamp(percent, 1, 99);
                    configuration.Save();
                }
                var example = 20_000 * (100 - configuration.PercentageUndercut) / 100;
                ImGui.TextDisabled($"Example: competitor 20,000 → your target {example:N0} gil.");
            }
        }

        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Safety limits", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var floorEnabled = configuration.MinimumPriceEnabled;
            if (ImGui.Checkbox("Never go below a minimum price##global_floor", ref floorEnabled))
            {
                configuration.MinimumPriceEnabled = floorEnabled;
                configuration.Save();
            }
            if (configuration.MinimumPriceEnabled)
            {
                ImGui.SameLine();
                var floor = configuration.MinimumPriceGil;
                ImGui.SetNextItemWidth(150f);
                if (ImGui.InputInt("gil minimum##global_floor_value", ref floor, 100, 1000))
                {
                    configuration.MinimumPriceGil = Math.Clamp(floor, 1, 999_999_999);
                    configuration.Save();
                }
            }
            ImGui.TextDisabled(configuration.MinimumPriceEnabled
                ? $"A suggested price below {configuration.MinimumPriceGil:N0} is stopped at the minimum instead. This never raises an already-lower listing."
                : "Off: normal pricing may go as low as the market requires.");

            ImGui.Spacing();
            var maxDropEnabled = configuration.MaxDropProtectionEnabled;
            if (ImGui.Checkbox("Block very large price drops##global_max_drop", ref maxDropEnabled))
            {
                configuration.MaxDropProtectionEnabled = maxDropEnabled;
                configuration.Save();
            }
            if (configuration.MaxDropProtectionEnabled)
            {
                ImGui.SameLine();
                var maxDrop = configuration.MaxDropPercent;
                ImGui.SetNextItemWidth(100f);
                if (ImGui.InputInt("% maximum drop##global_max_drop_value", ref maxDrop, 1, 5))
                {
                    configuration.MaxDropPercent = Math.Clamp(maxDrop, 1, 99);
                    configuration.Save();
                }
            }
            ImGui.TextDisabled(configuration.MaxDropProtectionEnabled
                ? $"If one run would lower your listing by more than {configuration.MaxDropPercent}%, the plugin leaves it unchanged."
                : "Off: there is no percentage limit on a downward change.");

            ImGui.Spacing();
            var outlierEnabled = configuration.OutlierProtectionEnabled;
            if (ImGui.Checkbox("Protect against suspiciously cheap listings##global_outlier", ref outlierEnabled))
            {
                configuration.OutlierProtectionEnabled = outlierEnabled;
                configuration.Save();
            }
            if (configuration.OutlierProtectionEnabled)
            {
                ImGui.SameLine();
                var gap = configuration.OutlierGapPercent;
                ImGui.SetNextItemWidth(100f);
                if (ImGui.InputInt("% gap##global_outlier_gap", ref gap, 5, 10))
                {
                    configuration.OutlierGapPercent = Math.Clamp(gap, 10, 95);
                    configuration.Save();
                }
            }
            ImGui.TextDisabled(configuration.OutlierProtectionEnabled
                ? $"If one seller is alone at the cheapest price and is {configuration.OutlierGapPercent}%+ below the next price tier, the plugin treats it as suspicious and does not chase it."
                : "Off: the cheapest eligible listing is trusted normally.");
        }

        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Raise prices when there is room", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var recoveryEnabled = configuration.PriceRecoveryEnabled;
            if (ImGui.Checkbox("Raise my price when I am safely the cheapest##global_price_recovery", ref recoveryEnabled))
            {
                configuration.PriceRecoveryEnabled = recoveryEnabled;
                configuration.Save();
            }
            ImGui.TextDisabled("Example: you are 10,000 and the next seller is 15,000 → the plugin can move you toward 14,999 while keeping you cheapest.");

            if (configuration.PriceRecoveryEnabled)
            {
                ImGui.Spacing();
                ImGui.TextUnformatted("How big must the gap be before raising?");
                var gapMode = configuration.RecoveryGapMode;
                if (ImGui.Button("At least X gil##recovery_gap_mode_gil"))
                {
                    configuration.RecoveryGapMode = PriceRecoveryGapMode.FixedGil;
                    configuration.Save();
                }
                ImGui.SameLine();
                if (ImGui.Button("At least X percent##recovery_gap_mode_percent"))
                {
                    configuration.RecoveryGapMode = PriceRecoveryGapMode.Percentage;
                    configuration.Save();
                }
                ImGui.SameLine();
                if (gapMode == PriceRecoveryGapMode.FixedGil)
                {
                    var recoveryGap = configuration.PriceRecoveryMinimumGapGil;
                    ImGui.SetNextItemWidth(130f);
                    if (ImGui.InputInt("gil gap##recovery_gap_gil", ref recoveryGap, 100, 1000))
                    {
                        configuration.PriceRecoveryMinimumGapGil = Math.Clamp(recoveryGap, 1, 999_999_999);
                        configuration.Save();
                    }
                    ImGui.TextDisabled($"Recovery only starts when the next seller is at least {configuration.PriceRecoveryMinimumGapGil:N0} gil higher than you.");
                }
                else
                {
                    var recoveryGapPercent = configuration.PriceRecoveryMinimumGapPercent;
                    ImGui.SetNextItemWidth(100f);
                    if (ImGui.InputInt("% gap##recovery_gap_percent", ref recoveryGapPercent, 1, 5))
                    {
                        configuration.PriceRecoveryMinimumGapPercent = Math.Clamp(recoveryGapPercent, 1, 99);
                        configuration.Save();
                    }
                    ImGui.TextDisabled($"Recovery only starts when the next seller is at least {configuration.PriceRecoveryMinimumGapPercent}% higher than you.");
                }

                ImGui.Spacing();
                var maxRaiseEnabled = configuration.PriceRecoveryMaxRaiseEnabled;
                if (ImGui.Checkbox("Limit how much one run may raise a price##global_recovery_max_raise", ref maxRaiseEnabled))
                {
                    configuration.PriceRecoveryMaxRaiseEnabled = maxRaiseEnabled;
                    configuration.Save();
                }
                if (configuration.PriceRecoveryMaxRaiseEnabled)
                {
                    ImGui.SameLine();
                    var maxRaise = configuration.PriceRecoveryMaxRaisePercent;
                    ImGui.SetNextItemWidth(100f);
                    if (ImGui.InputInt("% maximum raise##global_recovery_max_raise_value", ref maxRaise, 5, 10))
                    {
                        configuration.PriceRecoveryMaxRaisePercent = Math.Clamp(maxRaise, 1, 999);
                        configuration.Save();
                    }
                    ImGui.TextDisabled($"A recovery larger than {configuration.PriceRecoveryMaxRaisePercent}% in one run is blocked instead of applied.");
                }
                else
                {
                    ImGui.TextDisabled("Off: Price Recovery has no percentage cap on upward changes.");
                }
            }
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Tip: after changing any pricing option, run Preview first. Preview uses the exact same pricing logic without writing a price.");

        if (editingLocked)
        {
            ImGui.EndDisabled();
            ImGui.TextDisabled("Pricing settings are locked while a pricing operation is active.");
        }
    }

    private void DrawMatchlistPanel()
    {
        ImGui.TextDisabled("Use this for friends / FC members you do not want to undercut. If one of them is at the cheapest eligible price, the plugin matches that price instead.");
        ImGui.TextDisabled($"{configuration.MatchlistedRetainerNames.Count} protected retainer name(s). Names are matched case-insensitively but otherwise exactly.");

        var editingLocked = multiRunner.IsRunning || autoRunner.IsRunning || marketCheck.IsBusy;
        if (editingLocked)
            ImGui.BeginDisabled();

        ImGui.SetNextItemWidth(280f);
        var pressedEnter = ImGui.InputText("Retainer name##matchlistInput", ref matchlistInput, 64, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        var addPressed = ImGui.Button("Protect this seller");
        if (pressedEnter || addPressed)
            TryAddMatchlistEntry();

        if (configuration.MatchlistedRetainerNames.Count == 0)
        {
            ImGui.TextDisabled("No protected seller names yet.");
        }
        else if (ImGui.BeginTable("##MatchlistTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Protected retainer", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableHeadersRow();

            string? removeName = null;
            foreach (var name in configuration.MatchlistedRetainerNames)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextUnformatted(name);
                ImGui.TableNextColumn();
                if (ImGui.SmallButton($"Remove##matchlist_{name}"))
                    removeName = name;
            }

            ImGui.EndTable();

            if (removeName is not null && configuration.RemoveMatchlistedRetainer(removeName))
                matchlistFeedback = $"Removed {removeName} from price protection.";
        }

        if (editingLocked)
        {
            ImGui.EndDisabled();
            ImGui.TextDisabled("Seller protection is locked while a pricing operation is active.");
        }

        if (!string.IsNullOrWhiteSpace(matchlistFeedback))
            ImGui.TextDisabled(matchlistFeedback);
    }

    private void TryAddMatchlistEntry()
    {
        var trimmed = matchlistInput.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            matchlistFeedback = "Enter a retainer name first.";
            return;
        }

        if (configuration.IsRetainerMatchlisted(trimmed))
        {
            matchlistFeedback = $"{trimmed} is already on the Matchlist.";
            return;
        }

        if (configuration.AddMatchlistedRetainer(trimmed))
        {
            matchlistFeedback = $"Added {trimmed} to the Matchlist.";
            matchlistInput = string.Empty;
        }
    }

    private void DrawDiagnosticsTab()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.72f, 0.28f, 1f));
        ImGui.TextUnformatted("Developer diagnostics");
        ImGui.PopStyleColor();
        ImGui.TextDisabled("Nothing on this tab is required for normal use. Keep it collapsed unless we are debugging a problem.");
        ImGui.Separator();

        if (ImGui.CollapsingHeader("Current item pipeline"))
        {
            ImGui.TextDisabled("Detailed state for the currently processed listing.");
            DrawCurrentItemPanel();
        }

        if (ImGui.CollapsingHeader("Retainer scanner"))
        {
            ImGui.TextDisabled("Sell-list visibility, row mapping and market-container diagnostics.");
            DrawScannerDiagnostics();
        }

        if (ImGui.CollapsingHeader("Raw row / slot mapping"))
        {
            ImGui.TextDisabled("Raw UI row ↔ RetainerMarket slot mapping used by the pricing engine.");
            DrawListingsTable();
        }
    }

    private void DrawCurrentItemPanel()
    {
        ImGui.TextUnformatted($"State: {marketCheck.StateLabel}");
        ImGui.TextWrapped(marketCheck.Status);

        if (marketCheck.DryRunMode)
            ImGui.TextDisabled("DRY RUN SAFETY ACTIVE — no Asking Price write and no Confirm.");

        if (marketCheck.SelectedListing is { } selected)
        {
            ImGui.TextUnformatted($"Selected: row {selected.UiRow + 1} / raw {selected.MarketSlot} • {selected.ItemName}{(selected.IsHq ? " HQ" : string.Empty)}");
            ImGui.TextUnformatted($"Original: {selected.UnitPrice:N0} gil");
        }

        if (marketCheck.SelectedListing.HasValue)
        {
            ImGui.TextUnformatted(marketCheck.ActiveItemRuleApplied
                ? $"Item rule: {marketCheck.ActiveItemRuleSummary}"
                : "Item rule: global defaults");
        }

        if (marketCheck.LowestCompetitorPrice is { } lowest)
            ImGui.TextUnformatted($"Lowest eligible: {lowest:N0}{(string.IsNullOrWhiteSpace(marketCheck.LowestCompetitorRetainerName) ? string.Empty : $" • {marketCheck.LowestCompetitorRetainerName}")}");
        if (marketCheck.ProposedPrice is { } proposed)
            ImGui.TextUnformatted($"Target: {proposed:N0} • {marketCheck.PricingActionLabel}");
        if (marketCheck.UsedMatchlistRule)
            ImGui.TextUnformatted($"Matchlist protected: {marketCheck.MatchlistedRetainerName}");
        if (marketCheck.UsedMinimumPriceFloor)
            ImGui.TextUnformatted($"Minimum-price floor applied: {marketCheck.ActiveMinimumPriceGil:N0}");
        if (marketCheck.CalculatedDropPercent is { } dropPercent)
            ImGui.TextUnformatted($"One-run drop: {dropPercent:F1}% • limit {marketCheck.ActiveMaxDropPercent}%{(marketCheck.MaxDropProtectionBlocked ? " • BLOCKED" : string.Empty)}");
        if (marketCheck.OutlierNextTierPrice is { } nextTier)
            ImGui.TextUnformatted($"Outlier guard: cheapest tier → next tier {nextTier:N0} • gap {(marketCheck.OutlierGapPercentObserved ?? 0):F1}%{(marketCheck.SuspiciousOutlierBlocked ? " • BLOCKED" : string.Empty)}");
        if (marketCheck.PriceRecoveryCompetitorPrice is { } recoveryCompetitor)
            ImGui.TextUnformatted($"Price Recovery: current → next competitor {recoveryCompetitor:N0} • gap {(marketCheck.PriceRecoveryGapGil ?? 0):N0} gil / {(marketCheck.PriceRecoveryGapPercentObserved ?? 0):F1}%{(marketCheck.UsedPriceRecovery ? " • ACTIVE" : marketCheck.PriceRecoverySafetyBlocked ? " • BLOCKED" : string.Empty)}");
        if (marketCheck.CalculatedRaisePercent is { } raisePercent)
            ImGui.TextUnformatted($"One-run raise: {raisePercent:F1}% • limit {(configuration.PriceRecoveryMaxRaiseEnabled ? $"{configuration.PriceRecoveryMaxRaisePercent}%" : "off")}");
        if (marketCheck.PriceRecoveryOwnListingBlockPrice is { } ownBlock)
            ImGui.TextUnformatted($"Price Recovery own-retainer guard: blocked by your own listing at {ownBlock:N0}");
        if (marketCheck.ObservedPriceFieldValue is { } observed)
            ImGui.TextUnformatted($"Asking Price field: {observed:N0}");
        if (marketCheck.PostConfirmObservedPrice is { } liveObserved)
            ImGui.TextUnformatted($"Post-confirm raw price: {liveObserved:N0}");
        if (marketCheck.ListingsObserved > 0 || marketCheck.OfferingsPacketsObserved > 0)
        {
            ImGui.TextUnformatted($"Market data: {marketCheck.OfferingsPacketsObserved} packet(s) • {marketCheck.ListingsObserved} listing(s) observed • {marketCheck.EligibleListingsObserved} eligible");
            ImGui.TextUnformatted($"Quality split: {marketCheck.NqListingsObserved} NQ • {marketCheck.HqListingsObserved} HQ • native cross-check {(marketCheck.NativeQualityValidationAvailable ? $"{marketCheck.NativeQualityRowsMatched} matched / {marketCheck.NativeQualityCorrections} quality corrected / {marketCheck.NativePriceCorrections} price corrected" : "unavailable")}");
            if (marketCheck.ExpectedMarketRequestId >= 0 || marketCheck.IgnoredStaleOfferingsPackets > 0)
                ImGui.TextUnformatted($"Request sync: native request {marketCheck.ExpectedMarketRequestId} • stale packet(s) ignored {marketCheck.IgnoredStaleOfferingsPackets}");
        }
        if (marketCheck.ExpectedRetainerId != 0)
            ImGui.TextUnformatted($"Expected retainer ID: {marketCheck.ExpectedRetainerId:X} • scanner active: {scanner.ActiveRetainerId:X}");
        if (marketCheck.UsedCachedQuote)
            ImGui.TextUnformatted("Quote: CACHE HIT");
        if (marketCheck.ThrottleBackoffs > 0)
            ImGui.TextUnformatted($"MB throttle backoffs: {marketCheck.ThrottleBackoffs}");
        if (!string.IsNullOrWhiteSpace(marketCheck.LastError))
            ImGui.TextWrapped($"Last error: {marketCheck.LastError}");
    }

    private void DrawScannerDiagnostics()
    {
        ImGui.TextUnformatted($"Scanner: {scanner.Status}");
        ImGui.TextUnformatted($"Retainer: {scanner.ActiveRetainerName}");
        ImGui.TextUnformatted($"Sell list: {(scanner.SellListVisible ? "VISIBLE" : "not visible")} • Market container: {(scanner.MarketContainerLoaded ? "LOADED" : "waiting")}");
        ImGui.TextUnformatted($"Listings: {scanner.Listings.Count} • row mapping {scanner.MappedListings}/{scanner.Listings.Count} {(scanner.UiOrderMappingReady ? "READY" : "incomplete")}");

        if (scanner.LastSuccessfulScanUtc is { } lastScan)
            ImGui.TextUnformatted($"Last scan: {lastScan.ToLocalTime():HH:mm:ss.fff}");

        if (ImGui.Button("Force scanner refresh"))
            scanner.ForceRefresh();
    }

    private void DrawListingsTable()
    {
        if (scanner.Listings.Count == 0)
        {
            ImGui.TextDisabled("No active listings are currently readable.");
            return;
        }

        if (!ImGui.BeginTable(
                "##RetainerListings",
                8,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable,
                new Vector2(0, 360)))
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Row", ImGuiTableColumnFlags.WidthFixed, 45);
        ImGui.TableSetupColumn("Raw", ImGuiTableColumnFlags.WidthFixed, 55);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Item ID", ImGuiTableColumnFlags.WidthFixed, 75);
        ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("HQ", ImGuiTableColumnFlags.WidthFixed, 38);
        ImGui.TableSetupColumn("Unit", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 105);
        ImGui.TableHeadersRow();

        foreach (var listing in scanner.Listings)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.TextUnformatted(listing.UiRowLabel);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(listing.MarketSlot.ToString());
            ImGui.TableNextColumn(); ImGui.TextUnformatted(listing.ItemName);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(listing.ItemId.ToString());
            ImGui.TableNextColumn(); ImGui.TextUnformatted(listing.Quantity.ToString("N0"));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(listing.IsHq ? "Yes" : "No");
            ImGui.TableNextColumn(); ImGui.TextUnformatted($"{listing.UnitPrice:N0}");
            ImGui.TableNextColumn(); ImGui.TextUnformatted($"{listing.TotalPrice:N0}");
        }

        ImGui.EndTable();
    }

    private bool PreferMultiRunAsLatest()
    {
        if (multiRunner.IsRunning)
            return true;
        if (autoRunner.IsRunning)
            return false;

        if (multiRunner.LastRunFinishedUtc is { } multiFinished && autoRunner.LastRunFinishedUtc is { } autoFinished)
            return multiFinished >= autoFinished;
        if (multiRunner.LastRunFinishedUtc.HasValue)
            return true;
        if (autoRunner.LastRunFinishedUtc.HasValue)
            return false;

        return multiRunner.TotalRetainers > 0 || multiRunner.Summaries.Count > 0;
    }

    private static void DrawSectionHeader(string title, string subtitle)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.40f, 0.88f, 0.68f, 1f));
        ImGui.TextUnformatted(title);
        ImGui.PopStyleColor();
        ImGui.TextDisabled(subtitle);
        ImGui.Separator();
    }

    private string BuildHumanPricingSummary()
    {
        return configuration.PricingMode == UndercutPricingMode.FixedAmount
            ? $"Undercut by {configuration.FixedUndercutAmount:N0} gil"
            : $"Undercut by {configuration.PercentageUndercut}%";
    }

    private string BuildHumanSafetySummary()
    {
        var parts = new List<string>();
        if (configuration.MinimumPriceEnabled) parts.Add($"minimum {configuration.MinimumPriceGil:N0}");
        if (configuration.MaxDropProtectionEnabled) parts.Add($"drop limit {configuration.MaxDropPercent}%");
        if (configuration.OutlierProtectionEnabled) parts.Add("cheap-listing guard on");
        if (configuration.PriceRecoveryEnabled) parts.Add("safe raises on");
        return parts.Count == 0 ? "Standard pricing" : string.Join(" • ", parts);
    }

    private static string BuildHumanItemRuleSummary(ResolvedItemPricingSettings settings)
    {
        if (settings.Ignore)
            return "Do not touch this item.";

        var pricing = settings.PricingMode == UndercutPricingMode.FixedAmount
            ? $"undercut by {settings.FixedUndercutAmount:N0} gil"
            : $"undercut by {settings.PercentageUndercut}%";
        var floor = settings.MinimumPriceEnabled ? $"minimum {settings.MinimumPriceGil:N0} gil" : "no minimum price";
        var drop = settings.MaxDropProtectionEnabled ? $"maximum {settings.MaxDropPercent}% one-run drop" : "no one-run drop limit";
        return $"{pricing} • {floor} • {drop}";
    }

    private static void DrawLiveButton(string label, Action onClick)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.16f, 0.48f, 0.34f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.20f, 0.60f, 0.42f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.12f, 0.38f, 0.28f, 1f));
        if (ImGui.Button(label))
            onClick();
        ImGui.PopStyleColor(3);
    }

    private static void DrawStopButton(string label, Action onClick)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.58f, 0.18f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.72f, 0.22f, 0.22f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.46f, 0.13f, 0.13f, 1f));
        if (ImGui.Button(label))
            onClick();
        ImGui.PopStyleColor(3);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
        if (duration.TotalMinutes >= 1)
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        return $"{duration.TotalSeconds:F1}s";
    }
}
