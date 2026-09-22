using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using RetainerUndercut.Models;

namespace RetainerUndercut.Services;

public sealed class MultiRetainerUndercutRunner : IDisposable
{
    private const int NavigationTimeoutMs = 6000;
    private const int EmptySellListGraceMs = 800;
    private const int TalkClickThrottleMs = 250;
    private const int RetainerListSettleMs = 350;

    private readonly IFramework framework;
    private readonly IGameGui gameGui;
    private readonly RetainerListingScanner scanner;
    private readonly SingleListingMarketCheck marketCheck;
    private readonly AutoRetainerUndercutRunner autoRunner;
    private readonly Configuration configuration;
    private readonly IPluginLog log;

    private readonly List<RetainerTarget> availableRetainers = [];
    private readonly List<RetainerTarget> runQueue = [];
    private readonly List<RetainerRunSummary> summaries = [];
    private readonly List<DryRunPreviewResult> previewResults = [];
    private readonly List<RunChangeRecord> runChanges = [];

    private MultiState state = MultiState.Idle;
    private int currentRetainerIndex;
    private long deadlineMs;
    private long emptyListSinceMs;
    private long lastTalkClickMs;
    private long retainerListStableSinceMs;
    private bool stopRequested;
    private bool childAggregated;
    private long runStartedMs;
    private long runFinishedMs;
    private DateTimeOffset? lastRunFinishedUtc;

    public MultiRetainerUndercutRunner(
        IFramework framework,
        IGameGui gameGui,
        RetainerListingScanner scanner,
        SingleListingMarketCheck marketCheck,
        AutoRetainerUndercutRunner autoRunner,
        Configuration configuration,
        IPluginLog log)
    {
        this.framework = framework;
        this.gameGui = gameGui;
        this.scanner = scanner;
        this.marketCheck = marketCheck;
        this.autoRunner = autoRunner;
        this.configuration = configuration;
        this.log = log;

        framework.Update += OnFrameworkUpdate;
        RefreshRetainers();
    }

    public IReadOnlyList<RetainerTarget> AvailableRetainers => availableRetainers;
    public IReadOnlyList<RetainerRunSummary> Summaries => summaries;
    public IReadOnlyList<DryRunPreviewResult> PreviewResults => previewResults;
    public IReadOnlyList<RunChangeRecord> RunChanges => runChanges;
    public bool IsRunning => state is not MultiState.Idle and not MultiState.Completed and not MultiState.Stopped and not MultiState.Failed;
    public bool CompletedSuccessfully => state == MultiState.Completed;
    public bool HardFailed => state == MultiState.Failed;
    public string StateLabel => state.ToString();
    public string Status { get; private set; } = "Idle. Open the Summoning Bell retainer list to run all retainers.";
    public int TotalRetainers => runQueue.Count;
    public int CurrentRetainerNumber => runQueue.Count == 0 ? 0 : Math.Min(currentRetainerIndex + 1, runQueue.Count);
    public string CurrentRetainerName => CurrentTarget?.Name ?? "None";

    public int RetainersCompleted { get; private set; }
    public int EmptyRetainerCount { get; private set; }
    public int TotalListingsChecked { get; private set; }
    public int TotalChanged { get; private set; }
    public int TotalAlreadyCheapest { get; private set; }
    public int TotalNoCompetitor { get; private set; }
    public int TotalMarketThrottled { get; private set; }
    public int TotalMatchlistProtected { get; private set; }
    public int TotalSafetyFloor { get; private set; }
    public int TotalMaxDropBlocked { get; private set; }
    public int TotalOutlierBlocked { get; private set; }
    public int TotalPriceRecovered { get; private set; }
    public int TotalPriceRecoveryBlocked { get; private set; }
    public int TotalIgnoredByRule { get; private set; }
    public int TotalMissingListings { get; private set; }
    public int TotalSkippedFailed { get; private set; }
    public int TotalRetries { get; private set; }
    public int TotalWouldChange { get; private set; }
    public bool DryRunMode { get; private set; }
    public int EnabledRetainerCount => availableRetainers.Count(x => x.Enabled);
    public int EnabledListingCount => availableRetainers.Where(x => x.Enabled).Sum(x => x.MarketItemCount);
    public bool RetainerListVisible => IsVisible("RetainerList");
    public bool CanStartAll => !IsRunning && !autoRunner.IsRunning && !marketCheck.IsBusy && RetainerListVisible && EnabledRetainerCount > 0;
    public string StartBlockReason
    {
        get
        {
            if (IsRunning)
                return "All-retainer run already active.";
            if (autoRunner.IsRunning)
                return "Current-retainer run is active.";
            if (marketCheck.IsBusy)
                return "A single-item market operation is active.";
            if (!RetainerListVisible)
                return "Open the Summoning Bell retainer list.";
            if (availableRetainers.Count == 0)
                return "Refresh retainers from the bell.";
            if (EnabledRetainerCount == 0)
                return "Enable at least one retainer.";
            return "Ready.";
        }
    }
    public DateTimeOffset? LastRunFinishedUtc => lastRunFinishedUtc;
    public TimeSpan RunElapsed
    {
        get
        {
            if (runStartedMs <= 0)
                return TimeSpan.Zero;

            var end = IsRunning ? Environment.TickCount64 : runFinishedMs;
            if (end <= 0)
                end = Environment.TickCount64;
            return TimeSpan.FromMilliseconds(Math.Max(0, end - runStartedMs));
        }
    }

    private RetainerTarget? CurrentTarget => currentRetainerIndex >= 0 && currentRetainerIndex < runQueue.Count
        ? runQueue[currentRetainerIndex]
        : null;

    public void Dispose()
    {
        ShutdownForDispose();
        framework.Update -= OnFrameworkUpdate;
    }

    public void ShutdownForDispose()
    {
        if (!IsRunning)
            return;

        stopRequested = false;
        FinishRunTiming();

        // If the navigator owns a menu transition, close only that transient menu.
        // Never touch the child listing UI here; SingleListingMarketCheck owns the
        // pre/post-Confirm safety rules for an active pricing operation.
        if (state is not MultiState.RunningRetainer)
        {
            unsafe
            {
                var select = GetVisibleAddon("SelectString");
                if (select is not null)
                    select->Close(true);
            }
        }

        state = MultiState.Stopped;
        Status = "Stopped because the plugin is unloading; no new retainer or listing will be started.";
    }

    public unsafe bool RefreshRetainers()
    {
        if (IsRunning)
            return false;

        var manager = RetainerManager.Instance();
        if (manager is null || !manager->IsReady)
            return false;

        availableRetainers.Clear();
        var count = manager->GetRetainerCount();
        for (uint i = 0; i < count; i++)
        {
            var retainer = manager->GetRetainerBySortedIndex(i);
            if (retainer is null || retainer->RetainerId == 0)
                continue;

            var name = string.IsNullOrWhiteSpace(retainer->NameString)
                ? $"Retainer {i + 1}"
                : retainer->NameString;

            availableRetainers.Add(new RetainerTarget(
                checked((int)i),
                retainer->RetainerId,
                name,
                retainer->MarketItemCount,
                configuration.IsRetainerEnabled(retainer->RetainerId)));
        }

        return availableRetainers.Count > 0;
    }

    public void SetRetainerEnabled(ulong retainerId, bool enabled)
    {
        if (IsRunning)
            return;

        configuration.SetRetainerEnabled(retainerId, enabled);
        RefreshRetainers();
    }

    public void SetAllAvailableRetainersEnabled(bool enabled)
    {
        if (IsRunning || availableRetainers.Count == 0)
            return;

        configuration.SetRetainersEnabled(availableRetainers.Select(x => x.RetainerId), enabled);
        RefreshRetainers();
    }

    public bool Start(bool dryRun = false)
    {
        if (IsRunning || autoRunner.IsRunning || marketCheck.IsBusy)
            return false;

        if (!IsVisible("RetainerList"))
        {
            Status = "Cannot start: open the Summoning Bell retainer list first.";
            return false;
        }

        if (!RefreshRetainers())
        {
            Status = "Cannot start: RetainerManager is not ready or no retainers were detected.";
            return false;
        }

        runQueue.Clear();
        runQueue.AddRange(availableRetainers.Where(x => x.Enabled));
        if (runQueue.Count == 0)
        {
            Status = "Cannot start: no retainers are enabled.";
            return false;
        }

        summaries.Clear();
        previewResults.Clear();
        runChanges.Clear();
        DryRunMode = dryRun;
        currentRetainerIndex = 0;
        runStartedMs = Environment.TickCount64;
        runFinishedMs = 0;
        lastRunFinishedUtc = null;
        stopRequested = false;
        childAggregated = false;
        deadlineMs = 0;
        emptyListSinceMs = 0;
        retainerListStableSinceMs = 0;

        RetainersCompleted = 0;
        EmptyRetainerCount = 0;
        TotalListingsChecked = 0;
        TotalChanged = 0;
        TotalAlreadyCheapest = 0;
        TotalNoCompetitor = 0;
        TotalMarketThrottled = 0;
        TotalMatchlistProtected = 0;
        TotalSafetyFloor = 0;
        TotalMaxDropBlocked = 0;
        TotalOutlierBlocked = 0;
        TotalPriceRecovered = 0;
        TotalPriceRecoveryBlocked = 0;
        TotalIgnoredByRule = 0;
        TotalMissingListings = 0;
        TotalSkippedFailed = 0;
        TotalRetries = 0;
        TotalWouldChange = 0;

        state = MultiState.PreparingRetainer;
        Status = dryRun
            ? $"Starting all-retainer DRY RUN: {runQueue.Count} enabled retainer(s). No prices will be written."
            : $"Starting all-retainer run: {runQueue.Count} enabled retainer(s).";
        log.Information(dryRun
            ? $"[MultiUndercut] Starting multi-retainer DRY RUN with {runQueue.Count} enabled retainer(s). No price writes or confirms are permitted."
            : $"[MultiUndercut] Starting multi-retainer run with {runQueue.Count} enabled retainer(s).");
        return true;
    }

    public void RequestStop()
    {
        if (!IsRunning)
            return;

        stopRequested = true;
        Status = "EMERGENCY STOP REQUESTED. No new retainer or listing will be started.";
        if (autoRunner.IsRunning)
            autoRunner.RequestStop();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!IsRunning)
            return;

        try
        {
            if (stopRequested)
            {
                HandleStopRequest();
                return;
            }

            // Retainers can show a short Talk box while entering/leaving. Click only while
            // this multi-retainer navigator owns the UI, never during the item's market flow.
            if (state is not MultiState.RunningRetainer)
                ClickTalkIfVisible();

            switch (state)
            {
                case MultiState.PreparingRetainer:
                    PrepareCurrentRetainer();
                    break;
                case MultiState.WaitingForRetainerMenu:
                    WaitForRetainerMenu();
                    break;
                case MultiState.WaitingForSellList:
                    WaitForSellList();
                    break;
                case MultiState.RunningRetainer:
                    MonitorCurrentRetainerRun();
                    break;
                case MultiState.ClosingSellList:
                    CloseSellList();
                    break;
                case MultiState.WaitingForRetainerMenuReturn:
                    WaitForRetainerMenuReturn();
                    break;
                case MultiState.WaitingForRetainerList:
                    WaitForRetainerList();
                    break;
            }
        }
        catch (Exception ex)
        {
            FailHard($"Multi-retainer state-machine error: {ex.Message}", ex);
        }
    }

    private void PrepareCurrentRetainer()
    {
        if (currentRetainerIndex >= runQueue.Count)
        {
            CompleteRun();
            return;
        }

        var target = CurrentTarget!.Value;
        childAggregated = false;
        emptyListSinceMs = 0;

        // RetainerManager already exposes the current number of market listings, so there is
        // no reason to enter a retainer that has none.
        if (target.MarketItemCount == 0)
        {
            EmptyRetainerCount++;
            RetainersCompleted++;
            summaries.Add(RetainerRunSummary.Empty(target.Name));
            log.Information($"[MultiUndercut] {target.Name}: 0 market listings, skipped without opening the retainer.");
            currentRetainerIndex++;
            Status = $"Skipped empty retainer {target.Name}. Moving to the next retainer.";
            return;
        }

        // Do not click a RetainerList that is merely visible behind the previous
        // retainer's menu/dialogue. The previous v0.0.6 build could select the next
        // retainer through that background list and then wait forever for a menu
        // that was never actually requested.
        if (IsVisible("SelectString") || IsVisible("Talk"))
        {
            Status = $"[{CurrentRetainerNumber}/{TotalRetainers}] waiting for the previous retainer UI to fully close before selecting {target.Name}…";
            return;
        }

        if (!IsVisible("RetainerList"))
        {
            Status = $"[{CurrentRetainerNumber}/{TotalRetainers}] waiting for RetainerList before selecting {target.Name}…";
            return;
        }

        unsafe
        {
            var list = GetVisibleAddon("RetainerList");
            if (list is null)
                return;

            // Known RetainerList selection callback: command 2 + sorted UI index.
            // Two trailing zeroes mirror the game's/common automation callback shape.
            FireIntCallback(list, 2, target.SortedIndex, 0, 0);
        }

        deadlineMs = Environment.TickCount64 + NavigationTimeoutMs;
        state = MultiState.WaitingForRetainerMenu;
        Status = $"[{CurrentRetainerNumber}/{TotalRetainers}] selected {target.Name}; waiting for its retainer menu…";
        log.Information($"[MultiUndercut] Selecting retainer {target.SortedIndex}: {target.Name} ({target.RetainerId:X}).");
    }

    private void WaitForRetainerMenu()
    {
        var target = CurrentTarget!.Value;

        if (IsVisible("SelectString"))
        {
            // Scanner state can briefly still contain the previous retainer while the
            // next menu is opening. Only treat a mismatch as authoritative once the
            // target menu is actually visible; zero means "not converged yet".
            if (scanner.ActiveRetainerId == 0)
            {
                Status = $"[{CurrentRetainerNumber}/{TotalRetainers}] retainer menu is visible; waiting for active-retainer verification for {target.Name}…";
                return;
            }

            if (scanner.ActiveRetainerId != target.RetainerId)
            {
                FailHard($"Safety stop: visible retainer menu belongs to the wrong retainer. Expected {target.Name} ({target.RetainerId:X}), observed {scanner.ActiveRetainerId:X}.");
                return;
            }

            unsafe
            {
                var select = GetVisibleAddon("SelectString");
                if (select is null)
                    return;

                // Retainer menu entry 2 = "Sell items in your inventory on the market".
                FireIntCallback(select, 2);
            }

            scanner.ForceRefresh();
            deadlineMs = Environment.TickCount64 + NavigationTimeoutMs;
            emptyListSinceMs = 0;
            state = MultiState.WaitingForSellList;
            Status = $"[{CurrentRetainerNumber}/{TotalRetainers}] opening {target.Name}'s market sell list…";
            return;
        }

        if (Environment.TickCount64 >= deadlineMs)
            FailHard($"Timed out waiting for {target.Name}'s retainer menu. No prices were changed for this navigation step.");
    }

    private void WaitForSellList()
    {
        var target = CurrentTarget!.Value;

        if (scanner.SellListVisible)
        {
            if (scanner.ActiveRetainerId == 0)
            {
                Status = $"[{CurrentRetainerNumber}/{TotalRetainers}] sell list is visible; waiting for active-retainer verification for {target.Name}…";
                return;
            }

            if (scanner.ActiveRetainerId != target.RetainerId)
            {
                FailHard($"Safety stop: visible sell list belongs to the wrong retainer. Expected {target.Name} ({target.RetainerId:X}), observed {scanner.ActiveRetainerId:X}.");
                return;
            }
        }

        if (scanner.SellListVisible && scanner.ActiveRetainerId == target.RetainerId && scanner.MarketContainerLoaded)
        {
            if (scanner.Listings.Count == 0)
            {
                if (emptyListSinceMs == 0)
                    emptyListSinceMs = Environment.TickCount64;

                if (Environment.TickCount64 - emptyListSinceMs >= EmptySellListGraceMs)
                {
                    EmptyRetainerCount++;
                    summaries.Add(RetainerRunSummary.Empty(target.Name));
                    Status = $"{target.Name} currently has no live listings; closing it and continuing.";
                    state = MultiState.ClosingSellList;
                    deadlineMs = Environment.TickCount64 + NavigationTimeoutMs;
                }

                return;
            }

            emptyListSinceMs = 0;
            if (!scanner.UiOrderMappingReady)
            {
                Status = $"[{CurrentRetainerNumber}/{TotalRetainers}] {target.Name}: waiting for complete UI-row mapping ({scanner.MappedListings}/{scanner.Listings.Count})…";
                return;
            }

            if (autoRunner.Start(DryRunMode, recordHistory: false))
            {
                state = MultiState.RunningRetainer;
                Status = $"[{CurrentRetainerNumber}/{TotalRetainers}] running proven single-retainer engine for {target.Name} ({scanner.Listings.Count} listing(s)).";
                log.Information($"[MultiUndercut] Started child undercut run for verified retainer {target.Name}.");
                return;
            }
        }

        if (Environment.TickCount64 >= deadlineMs)
            FailHard($"Timed out waiting for a verified, mapped sell list for {target.Name}.");
    }

    private void MonitorCurrentRetainerRun()
    {
        var target = CurrentTarget!.Value;

        if (autoRunner.IsRunning)
        {
            Status = $"[{CurrentRetainerNumber}/{TotalRetainers}] {target.Name}: {autoRunner.Status}";
            return;
        }

        if (autoRunner.HardFailed)
        {
            FailHard($"Hard stop inside {target.Name}: {autoRunner.Status}");
            return;
        }

        if (!autoRunner.CompletedSuccessfully)
        {
            FailHard($"{target.Name}'s item runner stopped unexpectedly: {autoRunner.Status}");
            return;
        }

        AggregateChildResult(target.Name);
        state = MultiState.ClosingSellList;
        deadlineMs = Environment.TickCount64 + NavigationTimeoutMs;
        Status = $"{target.Name} finished. Closing sell list and returning to RetainerList…";
    }

    private void AggregateChildResult(string retainerName, bool partial = false)
    {
        if (childAggregated)
            return;

        childAggregated = true;
        var checkedListings = partial ? autoRunner.ProcessedItemsCount : autoRunner.TotalItems;
        TotalListingsChecked += checkedListings;
        TotalChanged += autoRunner.ChangedCount;
        TotalAlreadyCheapest += autoRunner.AlreadyCheapestCount;
        TotalNoCompetitor += autoRunner.NoCompetitorCount;
        TotalMarketThrottled += autoRunner.MarketThrottledCount;
        TotalMatchlistProtected += autoRunner.MatchlistProtectedCount;
        TotalSafetyFloor += autoRunner.SafetyFloorCount;
        TotalMaxDropBlocked += autoRunner.MaxDropBlockedCount;
        TotalOutlierBlocked += autoRunner.OutlierBlockedCount;
        TotalPriceRecovered += autoRunner.PriceRecoveredCount;
        TotalPriceRecoveryBlocked += autoRunner.PriceRecoveryBlockedCount;
        TotalIgnoredByRule += autoRunner.IgnoredByRuleCount;
        TotalMissingListings += autoRunner.MissingListingCount;
        TotalSkippedFailed += autoRunner.FailedSkippedCount;
        TotalRetries += autoRunner.RetryCount;
        TotalWouldChange += autoRunner.WouldChangeCount;
        if (DryRunMode)
            previewResults.AddRange(autoRunner.PreviewResults);
        runChanges.AddRange(autoRunner.RunChanges.Select(CloneRunChange));

        summaries.Add(new RetainerRunSummary(
            retainerName,
            checkedListings,
            autoRunner.ChangedCount,
            autoRunner.WouldChangeCount,
            autoRunner.AlreadyCheapestCount,
            autoRunner.NoCompetitorCount,
            autoRunner.MarketThrottledCount,
            autoRunner.MatchlistProtectedCount,
            autoRunner.SafetyFloorCount,
            autoRunner.MaxDropBlockedCount,
            autoRunner.OutlierBlockedCount,
            autoRunner.PriceRecoveredCount,
            autoRunner.PriceRecoveryBlockedCount,
            autoRunner.IgnoredByRuleCount,
            autoRunner.MissingListingCount,
            autoRunner.FailedSkippedCount,
            autoRunner.RetryCount,
            false));
    }

    private void CloseSellList()
    {
        unsafe
        {
            var sellList = GetVisibleAddon("RetainerSellList");
            if (sellList is not null)
            {
                sellList->Close(true);
                deadlineMs = Environment.TickCount64 + NavigationTimeoutMs;
            }
        }

        scanner.ForceRefresh();
        state = MultiState.WaitingForRetainerMenuReturn;
        Status = $"[{CurrentRetainerNumber}/{TotalRetainers}] returning from sell list to retainer menu…";
    }

    private void WaitForRetainerMenuReturn()
    {
        // IMPORTANT: RetainerList can already be visible behind SelectString. Always
        // dismiss the active retainer menu first; never treat the background list as
        // proof that we have left the current retainer.
        if (IsVisible("SelectString"))
        {
            unsafe
            {
                var select = GetVisibleAddon("SelectString");
                if (select is not null)
                    select->Close(true);
            }

            retainerListStableSinceMs = 0;
            deadlineMs = Environment.TickCount64 + NavigationTimeoutMs;
            state = MultiState.WaitingForRetainerList;
            Status = $"[{CurrentRetainerNumber}/{TotalRetainers}] closed current retainer menu; waiting for a clean RetainerList…";
            return;
        }

        if (IsVisible("Talk"))
        {
            retainerListStableSinceMs = 0;
            Status = $"[{CurrentRetainerNumber}/{TotalRetainers}] finishing retainer dialogue before returning to RetainerList…";
            return;
        }

        if (TryFinishOnStableRetainerList())
            return;

        if (Environment.TickCount64 >= deadlineMs)
            FailHard("Timed out returning from RetainerSellList to a clean retainer list.");
    }

    private void WaitForRetainerList()
    {
        // A SelectString/Talk can briefly reappear during the leave transition. Clear
        // it and restart the stability timer instead of selecting the next retainer
        // through a background RetainerList.
        if (IsVisible("SelectString"))
        {
            unsafe
            {
                var select = GetVisibleAddon("SelectString");
                if (select is not null)
                    select->Close(true);
            }

            retainerListStableSinceMs = 0;
            deadlineMs = Environment.TickCount64 + NavigationTimeoutMs;
            Status = $"[{CurrentRetainerNumber}/{TotalRetainers}] retainer menu was still present; closed it and am waiting for RetainerList…";
            return;
        }

        if (IsVisible("Talk"))
        {
            retainerListStableSinceMs = 0;
            Status = $"[{CurrentRetainerNumber}/{TotalRetainers}] finishing leave dialogue; waiting for RetainerList…";
            return;
        }

        if (TryFinishOnStableRetainerList())
            return;

        if (Environment.TickCount64 >= deadlineMs)
            FailHard("Timed out returning to a clean RetainerList after closing the current retainer.");
    }

    private bool TryFinishOnStableRetainerList()
    {
        if (!IsVisible("RetainerList") || IsVisible("SelectString") || IsVisible("Talk"))
        {
            retainerListStableSinceMs = 0;
            return false;
        }

        var now = Environment.TickCount64;
        if (retainerListStableSinceMs == 0)
        {
            retainerListStableSinceMs = now;
            Status = $"[{CurrentRetainerNumber}/{TotalRetainers}] RetainerList is visible; waiting {RetainerListSettleMs} ms for it to become stable…";
            return false;
        }

        if (now - retainerListStableSinceMs < RetainerListSettleMs)
            return false;

        retainerListStableSinceMs = 0;
        FinishCurrentRetainerAndAdvance();
        return true;
    }

    private void FinishCurrentRetainerAndAdvance()
    {
        var name = CurrentTarget?.Name ?? "retainer";
        RetainersCompleted++;
        currentRetainerIndex++;
        childAggregated = false;
        retainerListStableSinceMs = 0;
        scanner.ForceRefresh();

        if (currentRetainerIndex >= runQueue.Count)
        {
            CompleteRun();
            return;
        }

        state = MultiState.PreparingRetainer;
        Status = $"Finished {name}. Preparing retainer {CurrentRetainerNumber}/{TotalRetainers}: {CurrentRetainerName}.";
    }

    private void HandleStopRequest()
    {
        if (autoRunner.IsRunning)
        {
            if (!autoRunner.StopRequested)
                autoRunner.RequestStop();

            Status = "EMERGENCY STOP: waiting for the current listing to become safe. If Confirm was already sent, only its verification will finish.";
            return;
        }

        // The child may have completed or safely stopped in the frame before this one.
        // Aggregate its real processed count exactly once so the stop summary/history
        // cannot lose a verified change or claim that unvisited listings were checked.
        if (state == MultiState.RunningRetainer && CurrentTarget is { } target && !childAggregated)
        {
            var childCompleted = autoRunner.CompletedSuccessfully;
            var hasPartialResult = autoRunner.ProcessedItemsCount > 0 || autoRunner.RunChanges.Count > 0;
            if (childCompleted || hasPartialResult)
            {
                AggregateChildResult(target.Name, partial: !childCompleted);
                if (childCompleted)
                    RetainersCompleted++;
            }
        }

        stopRequested = false;
        state = MultiState.Stopped;
        FinishRunTiming();
        Status = DryRunMode
            ? $"DRY RUN STOPPED — retainers completed {RetainersCompleted}/{TotalRetainers}; listings checked {TotalListingsChecked}; would change {TotalWouldChange}. No prices were written."
            : $"STOPPED — retainers completed {RetainersCompleted}/{TotalRetainers}; listings checked {TotalListingsChecked}; changed {TotalChanged}. No new retainer/listing will be touched.";
        log.Information("[MultiUndercut] User stopped the multi-retainer run.");

        if (TotalListingsChecked > 0 || RetainersCompleted > 0)
            PersistRunHistory("All enabled retainers (stopped)");
    }

    private void CompleteRun()
    {
        state = MultiState.Completed;
        stopRequested = false;
        FinishRunTiming();
        Status = DryRunMode
            ? $"DRY RUN COMPLETE — retainers {RetainersCompleted}/{TotalRetainers} | empty {EmptyRetainerCount} | listings checked {TotalListingsChecked} | would change {TotalWouldChange} | already cheapest {TotalAlreadyCheapest} | no competitor {TotalNoCompetitor} | MB throttled {TotalMarketThrottled} | matchlist protected {TotalMatchlistProtected} | floor protected {TotalSafetyFloor} | max-drop blocked {TotalMaxDropBlocked} | outlier blocked {TotalOutlierBlocked} | would recover {TotalPriceRecovered} | recovery blocked {TotalPriceRecoveryBlocked} | ignored by rule {TotalIgnoredByRule} | missing {TotalMissingListings} | skipped/failed {TotalSkippedFailed} | retries {TotalRetries}. NO PRICES WERE WRITTEN."
            : $"COMPLETE — retainers {RetainersCompleted}/{TotalRetainers} | empty {EmptyRetainerCount} | listings checked {TotalListingsChecked} | changed {TotalChanged} | already cheapest {TotalAlreadyCheapest} | no competitor {TotalNoCompetitor} | MB throttled {TotalMarketThrottled} | matchlist protected {TotalMatchlistProtected} | floor protected {TotalSafetyFloor} | max-drop blocked {TotalMaxDropBlocked} | outlier blocked {TotalOutlierBlocked} | recovered {TotalPriceRecovered} | recovery blocked {TotalPriceRecoveryBlocked} | ignored by rule {TotalIgnoredByRule} | missing {TotalMissingListings} | skipped/failed {TotalSkippedFailed} | retries {TotalRetries}.";
        log.Information(DryRunMode
            ? $"[MultiUndercut] DRY RUN COMPLETE. Retainers={RetainersCompleted}/{TotalRetainers}, Empty={EmptyRetainerCount}, Listings={TotalListingsChecked}, WouldChange={TotalWouldChange}, Cheapest={TotalAlreadyCheapest}, NoCompetitor={TotalNoCompetitor}, Throttled={TotalMarketThrottled}, MatchlistProtected={TotalMatchlistProtected}, SafetyFloor={TotalSafetyFloor}, MaxDropBlocked={TotalMaxDropBlocked}, OutlierBlocked={TotalOutlierBlocked}, PriceRecovered={TotalPriceRecovered}, PriceRecoveryBlocked={TotalPriceRecoveryBlocked}, IgnoredByRule={TotalIgnoredByRule}, Missing={TotalMissingListings}, FailedSkipped={TotalSkippedFailed}, Retries={TotalRetries}. No prices were written."
            : $"[MultiUndercut] COMPLETE. Retainers={RetainersCompleted}/{TotalRetainers}, Empty={EmptyRetainerCount}, Listings={TotalListingsChecked}, Changed={TotalChanged}, Cheapest={TotalAlreadyCheapest}, NoCompetitor={TotalNoCompetitor}, Throttled={TotalMarketThrottled}, MatchlistProtected={TotalMatchlistProtected}, SafetyFloor={TotalSafetyFloor}, MaxDropBlocked={TotalMaxDropBlocked}, OutlierBlocked={TotalOutlierBlocked}, PriceRecovered={TotalPriceRecovered}, PriceRecoveryBlocked={TotalPriceRecoveryBlocked}, IgnoredByRule={TotalIgnoredByRule}, Missing={TotalMissingListings}, FailedSkipped={TotalSkippedFailed}, Retries={TotalRetries}.");
        PersistRunHistory("All enabled retainers");
    }

    private void PersistRunHistory(string scope)
    {
        var retainers = summaries
            .Select(summary => new RetainerRunHistory
            {
                RetainerName = summary.Name,
                Changes = runChanges
                    .Where(x => string.Equals(x.RetainerName, summary.Name, StringComparison.Ordinal))
                    .Select(CloneRunChange)
                    .ToList(),
            })
            .ToList();

        configuration.AddRunHistory(new RunHistoryEntry
        {
            FinishedAtUtc = lastRunFinishedUtc ?? DateTimeOffset.UtcNow,
            DryRun = DryRunMode,
            Scope = scope,
            DurationMilliseconds = (long)RunElapsed.TotalMilliseconds,
            Checked = TotalListingsChecked,
            Changed = TotalChanged,
            WouldChange = TotalWouldChange,
            OutlierBlocked = TotalOutlierBlocked,
            PriceRecovered = TotalPriceRecovered,
            PriceRecoveryBlocked = TotalPriceRecoveryBlocked,
            Retainers = retainers,
        });
    }

    private static RunChangeRecord CloneRunChange(RunChangeRecord source) => new()
    {
        RetainerName = source.RetainerName,
        ItemName = source.ItemName,
        ItemId = source.ItemId,
        IsHq = source.IsHq,
        PreviousPrice = source.PreviousPrice,
        ResultPrice = source.ResultPrice,
        Decision = source.Decision,
        Reason = source.Reason,
    };

    private void FailHard(string message, Exception? ex = null)
    {
        if (autoRunner.IsRunning)
        {
            autoRunner.RequestStop();
        }
        else if (state == MultiState.RunningRetainer && CurrentTarget is { } target && !childAggregated)
        {
            var childCompleted = autoRunner.CompletedSuccessfully;
            if (childCompleted || autoRunner.ProcessedItemsCount > 0 || autoRunner.RunChanges.Count > 0)
                AggregateChildResult(target.Name, partial: !childCompleted);
        }

        state = MultiState.Failed;
        stopRequested = false;
        FinishRunTiming();
        Status = $"HARD STOP — {message} No further retainers/listings will be touched.";
        if (ex is null)
            log.Error($"[MultiUndercut] {Status}");
        else
            log.Error(ex, $"[MultiUndercut] {Status}");

        if (TotalListingsChecked > 0 || summaries.Count > 0)
            PersistRunHistory("All enabled retainers (hard stop)");
    }


    private void FinishRunTiming()
    {
        if (runStartedMs <= 0)
            return;

        runFinishedMs = Environment.TickCount64;
        lastRunFinishedUtc = DateTimeOffset.UtcNow;
    }

    private bool IsVisible(string addonName)
    {
        var addon = gameGui.GetAddonByName(addonName);
        return !addon.IsNull && addon.IsVisible;
    }

    private unsafe AtkUnitBase* GetVisibleAddon(string addonName)
    {
        var addon = gameGui.GetAddonByName(addonName);
        return addon.IsNull || !addon.IsVisible ? null : (AtkUnitBase*)addon.Address;
    }

    private unsafe void ClickTalkIfVisible()
    {
        var now = Environment.TickCount64;
        if (now - lastTalkClickMs < TalkClickThrottleMs)
            return;

        var talk = GetVisibleAddon("Talk");
        if (talk is null)
            return;

        var stage = AtkStage.Instance();
        if (stage is null)
            return;

        AtkEvent evt = default;
        evt.Listener = (AtkEventListener*)talk;
        evt.Target = &stage->AtkEventTarget;
        evt.State.StateFlags = (AtkEventStateFlags)132;
        AtkEventData data = default;

        talk->ReceiveEvent(AtkEventType.MouseDown, 0, &evt, &data);
        talk->ReceiveEvent(AtkEventType.MouseClick, 0, &evt, &data);
        talk->ReceiveEvent(AtkEventType.MouseUp, 0, &evt, &data);
        lastTalkClickMs = now;
    }

    private static unsafe void FireIntCallback(AtkUnitBase* addon, params int[] arguments)
    {
        if (addon is null || arguments.Length == 0)
            return;

        var values = stackalloc AtkValue[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            values[i] = default;
            values[i].Type = AtkValueType.Int;
            values[i].Int = arguments[i];
        }

        addon->FireCallback((uint)arguments.Length, values, true);
    }

    private enum MultiState
    {
        Idle,
        PreparingRetainer,
        WaitingForRetainerMenu,
        WaitingForSellList,
        RunningRetainer,
        ClosingSellList,
        WaitingForRetainerMenuReturn,
        WaitingForRetainerList,
        Completed,
        Stopped,
        Failed,
    }
}

public readonly record struct RetainerTarget(
    int SortedIndex,
    ulong RetainerId,
    string Name,
    int MarketItemCount,
    bool Enabled);

public readonly record struct RetainerRunSummary(
    string Name,
    int Listings,
    int Changed,
    int WouldChange,
    int AlreadyCheapest,
    int NoCompetitor,
    int MarketThrottled,
    int MatchlistProtected,
    int SafetyFloor,
    int MaxDropBlocked,
    int OutlierBlocked,
    int PriceRecovered,
    int PriceRecoveryBlocked,
    int IgnoredByRule,
    int MissingListings,
    int SkippedFailed,
    int Retries,
    bool WasEmpty)
{
    public static RetainerRunSummary Empty(string name) => new(name, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, true);
}
