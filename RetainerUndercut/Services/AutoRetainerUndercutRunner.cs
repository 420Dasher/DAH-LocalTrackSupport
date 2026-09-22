using Dalamud.Plugin.Services;
using RetainerUndercut.Models;

namespace RetainerUndercut.Services;

public sealed class AutoRetainerUndercutRunner : IDisposable
{
    private const int MaxRetriesPerItem = 1;

    private readonly IFramework framework;
    private readonly IGameGui gameGui;
    private readonly RetainerListingScanner scanner;
    private readonly SingleListingMarketCheck marketCheck;
    private readonly Configuration configuration;
    private readonly IPluginLog log;
    private readonly List<ListingSnapshot> queue = [];
    private readonly List<DryRunPreviewResult> previewResults = [];
    private readonly List<RunChangeRecord> runChanges = [];

    private RunnerState state = RunnerState.Idle;
    private int currentIndex;
    private int retriesForCurrentItem;
    private bool stopRequested;
    private long runStartedMs;
    private long runFinishedMs;
    private DateTimeOffset? lastRunFinishedUtc;
    private bool recordHistoryForRun;
    private bool currentItemInFlight;
    private ulong expectedRetainerId;

    public AutoRetainerUndercutRunner(
        IFramework framework,
        IGameGui gameGui,
        RetainerListingScanner scanner,
        SingleListingMarketCheck marketCheck,
        Configuration configuration,
        IPluginLog log)
    {
        this.framework = framework;
        this.gameGui = gameGui;
        this.scanner = scanner;
        this.marketCheck = marketCheck;
        this.configuration = configuration;
        this.log = log;

        framework.Update += OnFrameworkUpdate;
    }

    public bool IsRunning => state is RunnerState.WaitingForUi or RunnerState.RunningItem or RunnerState.WaitingForRetryUi or RunnerState.StopPending;
    public string StateLabel => state.ToString();
    public string Status { get; private set; } = "Idle. Open one retainer's sell list to begin.";
    public string RetainerName { get; private set; } = "None";
    public int TotalItems => queue.Count;
    public int ProcessedItemsCount => Math.Clamp(currentIndex, 0, queue.Count);
    public ulong ExpectedRetainerId => expectedRetainerId;
    public int CurrentNumber => queue.Count == 0 ? 0 : Math.Min(currentIndex + 1, queue.Count);
    public string CurrentItemName => currentIndex >= 0 && currentIndex < queue.Count ? queue[currentIndex].ItemName : "None";
    public int ChangedCount { get; private set; }
    public int AlreadyCheapestCount { get; private set; }
    public int NoCompetitorCount { get; private set; }
    public int MissingListingCount { get; private set; }
    public int FailedSkippedCount { get; private set; }
    public int MarketThrottledCount { get; private set; }
    public int MatchlistProtectedCount { get; private set; }
    public int SafetyFloorCount { get; private set; }
    public int MaxDropBlockedCount { get; private set; }
    public int OutlierBlockedCount { get; private set; }
    public int PriceRecoveredCount { get; private set; }
    public int PriceRecoveryBlockedCount { get; private set; }
    public int IgnoredByRuleCount { get; private set; }
    public int RetryCount { get; private set; }
    public bool DryRunMode { get; private set; }
    public int WouldChangeCount { get; private set; }
    public IReadOnlyList<DryRunPreviewResult> PreviewResults => previewResults;
    public IReadOnlyList<RunChangeRecord> RunChanges => runChanges;
    public bool StopRequested => stopRequested;
    public bool CompletedSuccessfully => state == RunnerState.Completed;
    public bool HardFailed => state == RunnerState.Failed;
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
        currentItemInFlight = false;
        FinishRunTiming();
        state = RunnerState.Stopped;
        Status = "Stopped because the plugin is unloading; no new listing will be started.";
    }

    public bool Start(bool dryRun = false, bool recordHistory = true)
    {
        if (IsRunning || marketCheck.IsBusy)
            return false;

        if (!scanner.SellListVisible || !scanner.MarketContainerLoaded || !scanner.UiOrderMappingReady || scanner.Listings.Count == 0)
        {
            Status = "Cannot start: open a retainer sell list and wait for complete row mapping first.";
            return false;
        }

        expectedRetainerId = scanner.ActiveRetainerId;
        if (expectedRetainerId == 0)
        {
            Status = "Cannot start: active retainer identity is not verified yet.";
            return false;
        }

        queue.Clear();
        queue.AddRange(scanner.Listings.Where(x => x.UiRow >= 0).OrderBy(x => x.UiRow));
        if (queue.Count == 0)
        {
            Status = "Cannot start: no mapped listings were available.";
            return false;
        }

        RetainerName = scanner.ActiveRetainerName;
        DryRunMode = dryRun;
        recordHistoryForRun = recordHistory;
        currentIndex = 0;
        runStartedMs = Environment.TickCount64;
        runFinishedMs = 0;
        lastRunFinishedUtc = null;
        retriesForCurrentItem = 0;
        stopRequested = false;
        currentItemInFlight = false;
        ChangedCount = 0;
        AlreadyCheapestCount = 0;
        NoCompetitorCount = 0;
        MissingListingCount = 0;
        FailedSkippedCount = 0;
        MarketThrottledCount = 0;
        MatchlistProtectedCount = 0;
        SafetyFloorCount = 0;
        MaxDropBlockedCount = 0;
        OutlierBlockedCount = 0;
        PriceRecoveredCount = 0;
        PriceRecoveryBlockedCount = 0;
        IgnoredByRuleCount = 0;
        RetryCount = 0;
        WouldChangeCount = 0;
        previewResults.Clear();
        runChanges.Clear();
        marketCheck.BeginAutomaticRun();

        state = RunnerState.WaitingForUi;
        Status = dryRun
            ? $"Starting DRY RUN preview for {RetainerName}: {queue.Count} listing(s) queued. No prices will be written."
            : $"Starting automatic undercut for {RetainerName}: {queue.Count} listing(s) queued.";
        log.Information(dryRun
            ? $"[AutoUndercut] Starting DRY RUN for {RetainerName} with {queue.Count} mapped listing(s). No price writes or confirms are permitted."
            : $"[AutoUndercut] Starting {RetainerName} with {queue.Count} mapped listing(s).");
        return true;
    }

    public void RequestStop()
    {
        if (!IsRunning)
            return;

        stopRequested = true;
        state = RunnerState.StopPending;
        Status = marketCheck.ConfirmationSent
            ? "STOP REQUESTED. A confirm was already sent for the current item; waiting only for its live-price verification, then the run will stop."
            : "STOP REQUESTED. Cancelling the current pre-confirm operation and stopping before another item starts.";
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

            switch (state)
            {
                case RunnerState.WaitingForUi:
                    TryStartCurrentItem(isRetry: false);
                    return;

                case RunnerState.WaitingForRetryUi:
                    TryStartCurrentItem(isRetry: true);
                    return;

                case RunnerState.RunningItem:
                    HandleCurrentItemCompletion();
                    return;

                case RunnerState.StopPending:
                    HandleStopRequest();
                    return;
            }
        }
        catch (Exception ex)
        {
            if (marketCheck.IsBusy && !marketCheck.ConfirmationSent)
            {
                if (marketCheck.RequiresConfirmation)
                    marketCheck.DiscardPreparedPrice();
                else if (marketCheck.CanCancelBeforeConfirm)
                    marketCheck.CancelBeforeConfirm();
            }

            currentItemInFlight = false;
            state = RunnerState.Failed;
            FinishRunTiming();
            Status = marketCheck.ConfirmationSent
                ? $"AUTOMATIC RUN ABORTED after Confirm was sent: {ex.Message}. No further listings will be started; the market check may finish verification only."
                : $"AUTOMATIC RUN ABORTED: {ex.Message}";
            log.Error(ex, "[AutoUndercut] Runner state-machine error.");
            if (recordHistoryForRun && ProcessedItemsCount > 0)
                PersistRunHistory($"Current retainer — {RetainerName} (hard stop)", ProcessedItemsCount);
        }
    }

    private void TryStartCurrentItem(bool isRetry)
    {
        if (currentIndex >= queue.Count)
        {
            CompleteRun();
            return;
        }

        if (!UiReadyForNextItem())
        {
            Status = $"{ProgressPrefix()} waiting for RetainerSellList to become the only active retainer-market window…";
            return;
        }

        if (scanner.ActiveRetainerId != expectedRetainerId)
        {
            FailHard($"Safety stop: active retainer changed during the run. Expected {expectedRetainerId:X}, observed {scanner.ActiveRetainerId:X}.");
            return;
        }

        var queuedListing = queue[currentIndex];
        if (!TryRefreshQueuedListing(queuedListing, out var listing))
        {
            MissingListingCount++;
            AdvanceToNextItem("listing no longer exists on this retainer; skipped safely");
            return;
        }

        // Always use the freshly scanned snapshot. If an earlier listing sold/disappeared,
        // the visual row of later listings may have shifted even though their raw slot did not.
        queue[currentIndex] = listing;

        if (!marketCheck.Start(listing, autoConfirm: !DryRunMode, dryRun: DryRunMode))
        {
            // Start() can fail synchronously (for example if the sell list vanished in
            // the same frame). Route that failure through the normal retry accounting
            // instead of spinning forever in WaitingForUi.
            if (marketCheck.Outcome != ListingRunOutcome.None)
            {
                currentItemInFlight = true;
                state = RunnerState.RunningItem;
                Status = $"{ProgressPrefix()} start failed safely; processing the result through normal retry rules…";
                return;
            }

            Status = $"{ProgressPrefix()} waiting to start {listing.ItemName}…";
            return;
        }

        currentItemInFlight = true;
        state = RunnerState.RunningItem;
        Status = isRetry
            ? $"{ProgressPrefix()} retrying {listing.ItemName} ({retriesForCurrentItem}/{MaxRetriesPerItem})…"
            : $"{ProgressPrefix()} processing {listing.ItemName}…";
    }

    private bool TryRefreshQueuedListing(ListingSnapshot queued, out ListingSnapshot current)
    {
        // Raw RetainerMarket slots are stable identifiers for the live listing while it exists.
        // Item ID + HQ additionally protects against a slot being repopulated with a different item.
        foreach (var candidate in scanner.Listings)
        {
            if (candidate.MarketSlot != queued.MarketSlot)
                continue;
            if (candidate.ItemId != queued.ItemId)
                continue;
            if (candidate.IsHq != queued.IsHq)
                continue;
            if (candidate.UiRow < 0)
                continue;

            current = candidate;
            return true;
        }

        current = default;
        log.Information($"[AutoUndercut] Row {queued.UiRow + 1} / raw slot {queued.MarketSlot} {queued.ItemName}: listing disappeared before processing; skipping.");
        return false;
    }

    private void HandleCurrentItemCompletion()
    {
        if (marketCheck.IsBusy)
        {
            Status = $"{ProgressPrefix()} {marketCheck.Status}";
            return;
        }

        if (marketCheck.Outcome == ListingRunOutcome.None)
        {
            Status = $"{ProgressPrefix()} waiting for current item result…";
            return;
        }

        switch (marketCheck.Outcome)
        {
            case ListingRunOutcome.DryRunWouldChange:
                WouldChangeCount++;
                AddPreviewResult($"Would undercut using {marketCheck.ActivePricingRuleLabel}{(marketCheck.ActiveItemRuleApplied ? " (per-item rule active)" : string.Empty)}.", wouldChange: true);
                AddRunChange("WOULD UNDERCUT", $"Dry Run using {marketCheck.ActivePricingRuleLabel}.");
                AdvanceToNextItem("DRY RUN: would change; no price was written");
                return;

            case ListingRunOutcome.DryRunMatchlistWouldChange:
                WouldChangeCount++;
                MatchlistProtectedCount++;
                AddPreviewResult($"Would MATCH Matchlisted retainer {marketCheck.MatchlistedRetainerName}.", wouldChange: true);
                AddRunChange("WOULD MATCH", $"Matchlist: {marketCheck.MatchlistedRetainerName}.");
                AdvanceToNextItem("DRY RUN: would match Matchlisted retainer; no price was written");
                return;

            case ListingRunOutcome.DryRunSafetyFloorWouldChange:
                WouldChangeCount++;
                SafetyFloorCount++;
                AddPreviewResult($"Would change with the minimum-price safety floor applied at {marketCheck.ActiveMinimumPriceGil:N0} gil{(marketCheck.ActiveItemRuleApplied ? " (per-item rule may override globals)" : string.Empty)}.", wouldChange: true);
                AddRunChange("WOULD CHANGE — FLOOR", $"Safety floor {marketCheck.ActiveMinimumPriceGil:N0} gil applied.");
                AdvanceToNextItem("DRY RUN: would change at safety floor; no price was written");
                return;

            case ListingRunOutcome.DryRunPriceRecoveryWouldChange:
                WouldChangeCount++;
                PriceRecoveredCount++;
                if (marketCheck.UsedMatchlistRule)
                    MatchlistProtectedCount++;
                AddPreviewResult($"Would raise via Price Recovery to stay just below the next eligible competitor{(marketCheck.UsedMatchlistRule ? $" / match {marketCheck.MatchlistedRetainerName}" : string.Empty)}.", wouldChange: true);
                AddRunChange("WOULD RECOVER", $"Price Recovery: next competitor {marketCheck.PriceRecoveryCompetitorPrice:N0}, gap {marketCheck.PriceRecoveryGapGil:N0} gil ({marketCheck.PriceRecoveryGapPercentObserved:F1}%).");
                AdvanceToNextItem("DRY RUN: would raise via Price Recovery; no price was written");
                return;

            case ListingRunOutcome.ItemRuleIgnored:
                IgnoredByRuleCount++;
                AddPreviewResultIfDryRun($"Ignored by per-item rule: {marketCheck.ActiveItemRuleSummary}.", wouldChange: false);
                AdvanceToNextItem("ignored by per-item rule before any Market Board request");
                return;

            case ListingRunOutcome.PriceRecoveryChanged:
                ChangedCount++;
                PriceRecoveredCount++;
                if (marketCheck.UsedMatchlistRule)
                    MatchlistProtectedCount++;
                AddRunChange("PRICE RECOVERY", $"Raised while remaining cheapest; next competitor {marketCheck.PriceRecoveryCompetitorPrice:N0}, gap {marketCheck.PriceRecoveryGapGil:N0} gil ({marketCheck.PriceRecoveryGapPercentObserved:F1}%). Live-verified.");
                AdvanceToNextItem("raised via Price Recovery and live-verified");
                return;

            case ListingRunOutcome.Changed:
                ChangedCount++;
                AddRunChange("UNDERCUT", $"Live-verified using {marketCheck.ActivePricingRuleLabel}.");
                AdvanceToNextItem("changed and live-verified");
                return;

            case ListingRunOutcome.AlreadyCheapest:
                AlreadyCheapestCount++;
                AddPreviewResultIfDryRun("Already cheapest / no lowering needed.", wouldChange: false);
                AdvanceToNextItem("already cheapest / no lowering needed");
                return;

            case ListingRunOutcome.MatchlistProtectedChanged:
                ChangedCount++;
                MatchlistProtectedCount++;
                AddRunChange("MATCHLIST MATCH", $"Matched {marketCheck.MatchlistedRetainerName} and live-verified.");
                AdvanceToNextItem("matched Matchlisted retainer price and live-verified");
                return;

            case ListingRunOutcome.MatchlistProtectedAlreadyCheapest:
                AlreadyCheapestCount++;
                MatchlistProtectedCount++;
                AddPreviewResultIfDryRun($"Matchlist protected price from {marketCheck.MatchlistedRetainerName} is already satisfied.", wouldChange: false);
                AdvanceToNextItem("Matchlist protected price already satisfied");
                return;

            case ListingRunOutcome.SafetyFloorChanged:
                ChangedCount++;
                SafetyFloorCount++;
                AddRunChange("FLOOR-CLAMPED CHANGE", $"Safety floor {marketCheck.ActiveMinimumPriceGil:N0} gil applied and live-verified.");
                AdvanceToNextItem("minimum-price safety floor applied and live-verified");
                return;

            case ListingRunOutcome.SafetyFloorProtectedNoChange:
                SafetyFloorCount++;
                AddPreviewResultIfDryRun("Minimum-price safety floor prevents further lowering.", wouldChange: false);
                AdvanceToNextItem("minimum-price safety floor prevented further lowering; listing left unchanged");
                return;

            case ListingRunOutcome.SafetyMaxDropBlocked:
                MaxDropBlockedCount++;
                AddPreviewResultIfDryRun("Maximum one-run price-drop protection blocks this reduction.", wouldChange: false);
                AddRunChange("BLOCKED — MAX-DROP", marketCheck.Status);
                AdvanceToNextItem("max-drop protection blocked the calculated reduction; listing left unchanged");
                return;

            case ListingRunOutcome.PriceRecoverySafetyBlocked:
                PriceRecoveryBlockedCount++;
                AddPreviewResultIfDryRun("Price Recovery opportunity was blocked by the maximum one-run raise safety limit.", wouldChange: false);
                AddRunChange("BLOCKED — RECOVERY", marketCheck.Status);
                AdvanceToNextItem("Price Recovery max-raise safety blocked the increase; listing left unchanged");
                return;

            case ListingRunOutcome.SuspiciousOutlierBlocked:
                OutlierBlockedCount++;
                AddPreviewResultIfDryRun("Suspicious isolated cheapest listing was blocked by the outlier guard.", wouldChange: false);
                AddRunChange("BLOCKED — OUTLIER", marketCheck.Status);
                AdvanceToNextItem("suspicious isolated cheapest tier blocked; listing left unchanged");
                return;

            case ListingRunOutcome.NoEligibleCompetitor:
                NoCompetitorCount++;
                AddPreviewResultIfDryRun("No eligible competitor / no current market listings.", wouldChange: false);
                AdvanceToNextItem("no eligible competitor / no current market listings");
                return;

            case ListingRunOutcome.MarketThrottled:
                MarketThrottledCount++;
                AddPreviewResultIfDryRun("Market Board remained throttled after controlled cooldowns.", wouldChange: false);
                AdvanceToNextItem("Market Board remained throttled after controlled cooldowns; skipped without a full-item retry");
                return;

            case ListingRunOutcome.MissingListing:
                MissingListingCount++;
                AddPreviewResultIfDryRun("Listing disappeared during the preview.", wouldChange: false);
                AdvanceToNextItem("listing disappeared before confirmation; skipped safely");
                return;

            case ListingRunOutcome.FailedBeforeConfirm:
                if (retriesForCurrentItem < MaxRetriesPerItem)
                {
                    retriesForCurrentItem++;
                    RetryCount++;
                    currentItemInFlight = false;
                    scanner.ForceRefresh();
                    state = RunnerState.WaitingForRetryUi;
                    Status = $"{ProgressPrefix()} pre-confirm failure; preparing retry {retriesForCurrentItem}/{MaxRetriesPerItem}: {marketCheck.LastError}";
                    log.Warning($"[AutoUndercut] Retrying row {queue[currentIndex].UiRow + 1} after pre-confirm failure: {marketCheck.LastError}");
                    return;
                }

                FailedSkippedCount++;
                AddPreviewResultIfDryRun($"Failed after retry: {marketCheck.LastError}", wouldChange: false);
                AdvanceToNextItem($"skipped after {MaxRetriesPerItem} retry: {marketCheck.LastError}");
                return;

            case ListingRunOutcome.FailedAfterConfirm:
                FailedSkippedCount++;
                AddRunChange("FAILED AFTER CONFIRM", $"Live-price verification failed after Confirm: {marketCheck.LastError}");
                currentItemInFlight = false;
                // This listing was genuinely processed (and Confirm was sent), even though
                // verification failed. Advance the processed count before hard-stopping so
                // partial summaries/history do not pretend it was never checked.
                currentIndex++;
                state = RunnerState.Failed;
                FinishRunTiming();
                Status = $"HARD STOP after confirm verification failure: {marketCheck.LastError}. No further listings will be touched.";
                log.Error($"[AutoUndercut] Hard stop after post-confirm failure: {marketCheck.LastError}");
                if (recordHistoryForRun && ProcessedItemsCount > 0)
                    PersistRunHistory($"Current retainer — {RetainerName} (verification hard stop)", ProcessedItemsCount);
                return;
        }
    }

    private void AddPreviewResultIfDryRun(string reason, bool wouldChange)
    {
        if (!DryRunMode)
            return;

        AddPreviewResult(reason, wouldChange);
    }

    private void AddPreviewResult(string reason, bool wouldChange)
    {
        if (!DryRunMode || currentIndex < 0 || currentIndex >= queue.Count)
            return;

        var listing = queue[currentIndex];
        previewResults.Add(new DryRunPreviewResult(
            RetainerName,
            listing.ItemName,
            listing.ItemId,
            listing.IsHq,
            listing.UnitPrice,
            marketCheck.LowestCompetitorPrice,
            marketCheck.ProposedPrice,
            reason,
            wouldChange,
            marketCheck.UsedMatchlistRule,
            marketCheck.UsedMinimumPriceFloor,
            marketCheck.MaxDropProtectionBlocked,
            marketCheck.SuspiciousOutlierBlocked,
            marketCheck.UsedPriceRecovery,
            marketCheck.PriceRecoverySafetyBlocked));
    }

    private void AddRunChange(string decision, string reason)
    {
        if (currentIndex < 0 || currentIndex >= queue.Count)
            return;

        var listing = queue[currentIndex];
        runChanges.Add(new RunChangeRecord
        {
            RetainerName = RetainerName,
            ItemName = listing.ItemName,
            ItemId = listing.ItemId,
            IsHq = listing.IsHq,
            PreviousPrice = listing.UnitPrice,
            ResultPrice = marketCheck.ProposedPrice,
            Decision = decision,
            Reason = reason,
        });
    }

    private void AdvanceToNextItem(string result)
    {
        var finished = queue[currentIndex];
        log.Information($"[AutoUndercut] Row {finished.UiRow + 1} {finished.ItemName}: {result}.");
        currentItemInFlight = false;
        currentIndex++;
        retriesForCurrentItem = 0;
        scanner.ForceRefresh();

        if (currentIndex >= queue.Count)
        {
            CompleteRun();
            return;
        }

        state = RunnerState.WaitingForUi;
        Status = $"Completed {currentIndex}/{queue.Count}. Waiting for clean sell-list state before the next item.";
    }

    private void HandleStopRequest()
    {
        if (marketCheck.IsBusy)
        {
            if (!marketCheck.ConfirmationSent)
            {
                if (marketCheck.RequiresConfirmation)
                    marketCheck.DiscardPreparedPrice();
                else if (marketCheck.CanCancelBeforeConfirm)
                    marketCheck.CancelBeforeConfirm();
            }

            if (marketCheck.IsBusy)
            {
                Status = marketCheck.ConfirmationSent
                    ? "STOP REQUESTED: Confirm was already sent; waiting only for this item's live-price verification."
                    : "STOP REQUESTED: cancelling the current item before Confirm…";
                return;
            }
        }

        // If a result finished in the same frame as the stop request (especially a
        // post-Confirm verification), account for it exactly once before stopping.
        if (currentItemInFlight && marketCheck.Outcome != ListingRunOutcome.None)
        {
            if (marketCheck.Outcome == ListingRunOutcome.FailedBeforeConfirm)
            {
                FailedSkippedCount++;
                AddPreviewResultIfDryRun($"Stopped after a pre-confirm failure: {marketCheck.LastError}", wouldChange: false);
                currentItemInFlight = false;
                currentIndex++;
                retriesForCurrentItem = 0;
            }
            else
            {
                var requestedStop = stopRequested;
                stopRequested = false;
                HandleCurrentItemCompletion();
                stopRequested = requestedStop;

                if (state is RunnerState.Failed or RunnerState.Completed)
                {
                    stopRequested = false;
                    return;
                }
            }
        }
        else
        {
            currentItemInFlight = false;
        }

        state = RunnerState.Stopped;
        stopRequested = false;
        FinishRunTiming();
        var processed = ProcessedItemsCount;
        Status = DryRunMode
            ? $"DRY RUN STOPPED. Processed {processed}/{queue.Count}. Would change {WouldChangeCount}, already cheapest {AlreadyCheapestCount}, no competitor {NoCompetitorCount}, market throttled {MarketThrottledCount}, matchlist protected {MatchlistProtectedCount}, floor protected {SafetyFloorCount}, max-drop blocked {MaxDropBlockedCount}, outlier blocked {OutlierBlockedCount}, ignored by rule {IgnoredByRuleCount}, missing listing {MissingListingCount}, skipped/failed {FailedSkippedCount}. No prices were written."
            : $"STOPPED. Processed {processed}/{queue.Count}. Changed {ChangedCount}, already cheapest {AlreadyCheapestCount}, no competitor {NoCompetitorCount}, market throttled {MarketThrottledCount}, matchlist protected {MatchlistProtectedCount}, floor protected {SafetyFloorCount}, max-drop blocked {MaxDropBlockedCount}, outlier blocked {OutlierBlockedCount}, ignored by rule {IgnoredByRuleCount}, missing listing {MissingListingCount}, skipped/failed {FailedSkippedCount}.";
        log.Information("[AutoUndercut] User stopped the automatic retainer run.");

        if (recordHistoryForRun && processed > 0)
            PersistRunHistory($"Current retainer — {RetainerName} (stopped)", processed);
    }

    private bool UiReadyForNextItem()
    {
        if (!scanner.SellListVisible || !scanner.MarketContainerLoaded || !scanner.UiOrderMappingReady)
            return false;

        if (IsVisible("RetainerSell") || IsVisible("ItemSearchResult") || IsVisible("ContextMenu"))
            return false;

        return true;
    }

    private bool IsVisible(string addonName)
    {
        var addon = gameGui.GetAddonByName(addonName);
        return !addon.IsNull && addon.IsVisible;
    }

    private string ProgressPrefix() => queue.Count == 0
        ? "[0/0]"
        : $"[{Math.Min(currentIndex + 1, queue.Count)}/{queue.Count}]";

    private void FailHard(string message)
    {
        currentItemInFlight = false;
        state = RunnerState.Failed;
        stopRequested = false;
        FinishRunTiming();
        Status = $"HARD STOP — {message} No further listings will be touched.";
        log.Error($"[AutoUndercut] {Status}");
        if (recordHistoryForRun && ProcessedItemsCount > 0)
            PersistRunHistory($"Current retainer — {RetainerName} (hard stop)", ProcessedItemsCount);
    }

    private void CompleteRun()
    {
        state = RunnerState.Completed;
        stopRequested = false;
        FinishRunTiming();
        Status = DryRunMode
            ? $"DRY RUN COMPLETE — {RetainerName}: {queue.Count}/{queue.Count} checked | would change {WouldChangeCount} | already cheapest {AlreadyCheapestCount} | no competitor {NoCompetitorCount} | market throttled {MarketThrottledCount} | matchlist protected {MatchlistProtectedCount} | floor protected {SafetyFloorCount} | max-drop blocked {MaxDropBlockedCount} | outlier blocked {OutlierBlockedCount} | would recover {PriceRecoveredCount} | recovery blocked {PriceRecoveryBlockedCount} | ignored by rule {IgnoredByRuleCount} | missing listing {MissingListingCount} | skipped/failed {FailedSkippedCount} | retries {RetryCount}. NO PRICES WERE WRITTEN."
            : $"COMPLETE — {RetainerName}: {queue.Count}/{queue.Count} checked | changed {ChangedCount} | already cheapest {AlreadyCheapestCount} | no competitor {NoCompetitorCount} | market throttled {MarketThrottledCount} | matchlist protected {MatchlistProtectedCount} | floor protected {SafetyFloorCount} | max-drop blocked {MaxDropBlockedCount} | outlier blocked {OutlierBlockedCount} | recovered {PriceRecoveredCount} | recovery blocked {PriceRecoveryBlockedCount} | ignored by rule {IgnoredByRuleCount} | missing listing {MissingListingCount} | skipped/failed {FailedSkippedCount} | retries {RetryCount}.";
        log.Information(DryRunMode
            ? $"[AutoUndercut] DRY RUN COMPLETE for {RetainerName}. WouldChange={WouldChangeCount}, Cheapest={AlreadyCheapestCount}, NoCompetitor={NoCompetitorCount}, MarketThrottled={MarketThrottledCount}, MatchlistProtected={MatchlistProtectedCount}, SafetyFloor={SafetyFloorCount}, MaxDropBlocked={MaxDropBlockedCount}, OutlierBlocked={OutlierBlockedCount}, PriceRecovered={PriceRecoveredCount}, PriceRecoveryBlocked={PriceRecoveryBlockedCount}, IgnoredByRule={IgnoredByRuleCount}, MissingListing={MissingListingCount}, FailedSkipped={FailedSkippedCount}, Retries={RetryCount}. No prices were written."
            : $"[AutoUndercut] COMPLETE for {RetainerName}. Changed={ChangedCount}, Cheapest={AlreadyCheapestCount}, NoCompetitor={NoCompetitorCount}, MarketThrottled={MarketThrottledCount}, MatchlistProtected={MatchlistProtectedCount}, SafetyFloor={SafetyFloorCount}, MaxDropBlocked={MaxDropBlockedCount}, OutlierBlocked={OutlierBlockedCount}, PriceRecovered={PriceRecoveredCount}, PriceRecoveryBlocked={PriceRecoveryBlockedCount}, IgnoredByRule={IgnoredByRuleCount}, MissingListing={MissingListingCount}, FailedSkipped={FailedSkippedCount}, Retries={RetryCount}.");

        if (recordHistoryForRun)
            PersistRunHistory($"Current retainer — {RetainerName}", queue.Count);
    }

    private void PersistRunHistory(string scope, int checkedCount)
    {
        configuration.AddRunHistory(new RunHistoryEntry
        {
            FinishedAtUtc = lastRunFinishedUtc ?? DateTimeOffset.UtcNow,
            DryRun = DryRunMode,
            Scope = scope,
            DurationMilliseconds = (long)RunElapsed.TotalMilliseconds,
            Checked = checkedCount,
            Changed = ChangedCount,
            WouldChange = WouldChangeCount,
            OutlierBlocked = OutlierBlockedCount,
            PriceRecovered = PriceRecoveredCount,
            PriceRecoveryBlocked = PriceRecoveryBlockedCount,
            Retainers =
            [
                new RetainerRunHistory
                {
                    RetainerName = RetainerName,
                    Changes = runChanges.Select(CloneRunChange).ToList(),
                },
            ],
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

    private void FinishRunTiming()
    {
        if (runStartedMs <= 0)
            return;

        runFinishedMs = Environment.TickCount64;
        lastRunFinishedUtc = DateTimeOffset.UtcNow;
    }

    private enum RunnerState
    {
        Idle,
        WaitingForUi,
        RunningItem,
        WaitingForRetryUi,
        StopPending,
        Completed,
        Stopped,
        Failed,
    }
}
