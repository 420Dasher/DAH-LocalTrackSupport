using Dalamud.Game.Network.Structures;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using RetainerUndercut.Models;
using RetainerUndercut;

namespace RetainerUndercut.Services;

public sealed class SingleListingMarketCheck : IDisposable
{
    private const int UiTimeoutMs = 3500;
    private const int MarketTimeoutMs = 7000;
    private const int PostConfirmTimeoutMs = 8000;
    private const int ListingsPerBatch = 10;
    private const int OfferingsPacketSettleMs = 750;
    private const ulong MaxMarketPriceGil = 999_999_999UL;
    private const int MarketThrottleBaseDelayMs = 2200;
    private const int MarketThrottleStepMs = 500;
    private const int MarketThrottleMaxDelayMs = 4500;
    private const int MaxThrottleBackoffsPerItem = 2;
    private const int EmptyResultUiGraceMs = 1200;
    private const int NativeEmptyStableMs = 500;
    private const int MarketQuoteCacheTtlMs = 30000;
    private const int NoCompetitorCacheTtlMs = 8000;

    private readonly IFramework framework;
    private readonly IGameGui gameGui;
    private readonly IMarketBoard marketBoard;
    private readonly Configuration configuration;
    private readonly IPluginLog log;
    private readonly HashSet<ulong> ownRetainerIds = [];
    private readonly Dictionary<MarketCacheKey, CachedMarketQuote> marketQuoteCache = [];
    private readonly List<ulong> ownListingPricesForQuote = [];
    private readonly object offeringsLock = new();
    private readonly List<MarketOfferSnapshot> pendingOfferings = [];
    private readonly HashSet<ulong> pendingListingIds = [];
    private ResolvedItemPricingSettings activePricing;

    private CheckState state = CheckState.Idle;
    private long deadlineMs;
    private int pendingOfferingsRequestId = -1;
    private int pendingOfferingsPacketCount;
    private long pendingOfferingsLastPacketAtMs;
    private string pendingOfferingsError = string.Empty;
    private ulong expectedRetainerId;
    private long searchResultVisibleAtMs;
    private long marketCooldownUntilMs;
    private long retryMarketAtMs;
    private int throttleBackoffsForItem;
    private long nativeEmptyConfirmedSinceMs;
    private int expectedMarketRequestId = -1;

    public SingleListingMarketCheck(
        IFramework framework,
        IGameGui gameGui,
        IMarketBoard marketBoard,
        Configuration configuration,
        IPluginLog log)
    {
        this.framework = framework;
        this.gameGui = gameGui;
        this.marketBoard = marketBoard;
        this.configuration = configuration;
        this.log = log;
        activePricing = configuration.ResolveItemPricing(0, false);

        framework.Update += OnFrameworkUpdate;
        marketBoard.OfferingsReceived += OnOfferingsReceived;
    }

    public ListingSnapshot? SelectedListing { get; private set; }
    public string StateLabel => state.ToString();
    public string Status { get; private set; } = "Idle. Choose a listing or start the automatic retainer loop.";
    public ListingRunOutcome Outcome { get; private set; } = ListingRunOutcome.None;
    public bool AutoConfirmMode { get; private set; }
    public bool DryRunMode { get; private set; }
    public string LastError { get; private set; } = string.Empty;
    public ulong? LowestCompetitorPrice { get; private set; }
    public ulong? ProposedPrice { get; private set; }
    public int LastRequestId { get; private set; } = -1;
    public int ListingsObserved { get; private set; }
    public int EligibleListingsObserved { get; private set; }
    public int? OriginalPriceFieldValue { get; private set; }
    public int? ObservedPriceFieldValue { get; private set; }
    public ulong? PostConfirmObservedPrice { get; private set; }
    public bool PriceFieldWritten { get; private set; }
    public bool ConfirmationSent { get; private set; }
    public bool LivePriceVerified { get; private set; }
    public bool UsedCachedQuote { get; private set; }
    public int ThrottleBackoffs { get; private set; }
    public int OfferingsPacketsObserved { get; private set; }
    public int HqListingsObserved { get; private set; }
    public int NqListingsObserved { get; private set; }
    public int NativeQualityRowsMatched { get; private set; }
    public int NativeQualityCorrections { get; private set; }
    public int NativePriceCorrections { get; private set; }
    public int IgnoredStaleOfferingsPackets { get; private set; }
    public int ExpectedMarketRequestId => expectedMarketRequestId;
    public bool NativeQualityValidationAvailable { get; private set; }
    public ulong ExpectedRetainerId => expectedRetainerId;
    public bool UsedMatchlistRule { get; private set; }
    public bool UsedMinimumPriceFloor { get; private set; }
    public bool MaxDropProtectionBlocked { get; private set; }
    public bool SuspiciousOutlierBlocked { get; private set; }
    public bool UsedPriceRecovery { get; private set; }
    public bool PriceRecoverySafetyBlocked { get; private set; }
    public ulong? OutlierNextTierPrice { get; private set; }
    public double? OutlierGapPercentObserved { get; private set; }
    public double? CalculatedDropPercent { get; private set; }
    public ulong? PriceRecoveryCompetitorPrice { get; private set; }
    public ulong? PriceRecoveryGapGil { get; private set; }
    public double? PriceRecoveryGapPercentObserved { get; private set; }
    public double? CalculatedRaisePercent { get; private set; }
    public ulong? PriceRecoveryOwnListingBlockPrice { get; private set; }
    public string MatchlistedRetainerName { get; private set; } = string.Empty;
    public string LowestCompetitorRetainerName { get; private set; } = string.Empty;
    public bool ActiveItemRuleApplied => activePricing.HasItemRule;
    public bool ActiveItemIgnored => activePricing.Ignore;
    public string ActiveItemRuleSummary => activePricing.RuleSummary;
    public string ActivePricingRuleLabel => activePricing.PricingModeLabel;
    public bool ActiveMinimumPriceEnabled => activePricing.MinimumPriceEnabled;
    public int ActiveMinimumPriceGil => activePricing.MinimumPriceGil;
    public bool ActiveMaxDropProtectionEnabled => activePricing.MaxDropProtectionEnabled;
    public int ActiveMaxDropPercent => activePricing.MaxDropPercent;
    public string PricingActionLabel
    {
        get
        {
            if (UsedPriceRecovery && UsedMatchlistRule)
                return "PRICE RECOVERY + MATCH";
            if (UsedPriceRecovery)
                return "PRICE RECOVERY";
            if (UsedMinimumPriceFloor && UsedMatchlistRule)
                return "MATCH + SAFETY FLOOR";
            if (UsedMinimumPriceFloor)
                return "UNDERCUT + SAFETY FLOOR";
            if (UsedMatchlistRule)
                return "MATCH";
            return activePricing.PricingMode == UndercutPricingMode.FixedAmount
                ? $"UNDERCUT -{activePricing.FixedUndercutAmount:N0} GIL"
                : $"UNDERCUT -{activePricing.PercentageUndercut}%";
        }
    }

    public bool IsBusy => state is not CheckState.Idle and not CheckState.Done and not CheckState.Failed;
    public bool HasResult => state == CheckState.Done;
    public bool RequiresConfirmation => state == CheckState.AwaitingConfirmation && PriceFieldWritten && !ConfirmationSent;
    public bool CanDiscardPreparedPrice => RequiresConfirmation;
    public bool CanCancelBeforeConfirm => IsBusy && !ConfirmationSent && state != CheckState.AwaitingConfirmation;

    public void Dispose()
    {
        ShutdownForDispose();
        framework.Update -= OnFrameworkUpdate;
        marketBoard.OfferingsReceived -= OnOfferingsReceived;
    }

    public void ShutdownForDispose()
    {
        if (!IsBusy)
        {
            ResetPendingOfferings();
            return;
        }

        if (ConfirmationSent)
        {
            // Never send another callback after Confirm. At unload time we can no longer
            // complete the normal server verification, so leave the game UI alone and log
            // the exact state rather than risking a second mutation.
            log.Warning("[AutoUndercut] Plugin unloaded after Confirm was sent; no further UI action will be taken and live-price verification is being abandoned.");
            ResetPendingOfferings();
            PriceFieldWritten = false;
            state = CheckState.Failed;
            return;
        }

        try
        {
            CleanupWindows(cancelRetainerSell: true);
            log.Information("[AutoUndercut] Plugin unload cancelled the in-progress pricing operation before Confirm.");
        }
        finally
        {
            ResetPendingOfferings();
            PriceFieldWritten = false;
            state = CheckState.Idle;
        }
    }

    public void BeginAutomaticRun()
    {
        marketQuoteCache.Clear();
        log.Information("[AutoUndercut] Cleared per-run market quote cache.");
    }

    public bool Start(ListingSnapshot listing, bool autoConfirm = false, bool dryRun = false)
    {
        if (IsBusy)
            return false;

        ResetResult();
        AutoConfirmMode = autoConfirm;
        DryRunMode = dryRun;
        SelectedListing = listing;
        activePricing = configuration.ResolveItemPricing(listing.ItemId, listing.IsHq);
        expectedRetainerId = GetActiveRetainerId();

        if (activePricing.Ignore)
        {
            Outcome = ListingRunOutcome.ItemRuleIgnored;
            Status = $"ITEM RULE: {listing.ItemName}{(listing.IsHq ? " HQ" : string.Empty)} is ignored. No Market Board request, price write, or Confirm was performed.";
            LastError = string.Empty;
            state = CheckState.Done;
            log.Information($"[AutoUndercut] Item rule ignored {listing.ItemName} ({listing.ItemId}, {(listing.IsHq ? "HQ" : "NQ")}); skipped before opening the item context menu.");
            return true;
        }

        if (listing.UiRow < 0)
        {
            Fail("This listing has no valid visual row mapping.", cleanup: false);
            return false;
        }

        if (listing.UnitPrice is < 1 or > MaxMarketPriceGil)
        {
            Fail($"Safety stop: snapshot price {listing.UnitPrice:N0} is outside FFXIV's supported market range (1-{MaxMarketPriceGil:N0}).", cleanup: false);
            return false;
        }

        if (expectedRetainerId == 0)
        {
            Fail("Safety stop: the active retainer ID could not be verified before starting this listing.", cleanup: false);
            return false;
        }

        if (!RefreshOwnRetainers() || !ownRetainerIds.Contains(expectedRetainerId))
        {
            Fail("Safety stop: the player's retainer list could not be verified before pricing. Refusing to risk treating an own listing as a competitor.", cleanup: false);
            return false;
        }

        unsafe
        {
            var sellList = GetVisibleAddon("RetainerSellList");
            if (sellList is null)
            {
                Fail("RetainerSellList is not visible.", cleanup: false);
                return false;
            }

            // UI row, not raw RetainerMarket slot. Re-verify the current sell-list mapping
            // immediately before clicking so a redraw/reorder cannot redirect us to another row.
            if (!SellListRowTargetsSelectedSlot(sellList, listing, out var rowReason))
            {
                Fail($"Safety stop before opening the listing menu: {rowReason}", cleanup: false);
                return false;
            }

            FireIntCallback(sellList, 0, listing.UiRow, 1);
        }

        SetState(CheckState.WaitingForContextMenu,
            $"Row {listing.UiRow + 1}: waiting for item context menu…", UiTimeoutMs);
        log.Information($"[RealRepriceTest] Started for row {listing.UiRow + 1}, raw slot {listing.MarketSlot}, item {listing.ItemId} ({listing.ItemName}).");
        return true;
    }

    public void CancelBeforeConfirm()
    {
        if (!CanCancelBeforeConfirm)
            return;

        Status = "Real-reprice preparation cancelled before confirmation.";
        LastError = string.Empty;
        CleanupWindows(cancelRetainerSell: true);
        PriceFieldWritten = false;
        state = CheckState.Idle;
    }

    public void DiscardPreparedPrice()
    {
        if (!CanDiscardPreparedPrice)
            return;

        CleanupWindows(cancelRetainerSell: true);
        PriceFieldWritten = false;
        Status = "Prepared test price discarded with Cancel; the live listing was not changed.";
        LastError = string.Empty;
        state = CheckState.Idle;
        log.Information("[RealRepriceTest] Discarded prepared value with Cancel callback; no confirm sent.");
    }

    public bool ConfirmPreparedPrice()
    {
        if (DryRunMode)
        {
            LastError = "Dry Run safety blocked a Confirm attempt.";
            log.Warning("[AutoUndercut] Dry Run safety blocked ConfirmPreparedPrice().");
            return false;
        }

        if (!RequiresConfirmation || ProposedPrice is not { } proposed || SelectedListing is not { } selected)
            return false;

        if (!ExpectedRetainerStillActive(requireNonZero: true, out var observedRetainerId))
        {
            Fail($"Safety stop before Confirm: active retainer changed. Expected {expectedRetainerId:X}, observed {observedRetainerId:X}.");
            return false;
        }

        if (!SelectedListingStillExists())
        {
            CompleteMissingListing("The retainer listing disappeared or changed before confirmation; skipping this item without sending Confirm.");
            return false;
        }

        if (proposed is < 1 or > MaxMarketPriceGil || proposed > int.MaxValue)
        {
            Fail($"Proposed price {proposed:N0} is outside FFXIV's supported market range (1-{MaxMarketPriceGil:N0}).");
            return false;
        }

        unsafe
        {
            var retainerSell = GetVisibleRetainerSell();
            if (retainerSell is null || retainerSell->AskingPrice is null)
            {
                Fail("Adjust Price window disappeared before confirmation.");
                return false;
            }

            if (!RetainerSellDialogMatchesSelectedListing(retainerSell, requireOriginalPrice: false, out var mismatchReason))
            {
                Fail($"Safety stop before Confirm: Adjust Price no longer matches the selected listing ({mismatchReason}).");
                return false;
            }

            var observed = retainerSell->AskingPrice->Value;
            ObservedPriceFieldValue = observed;
            if (observed != (int)proposed)
            {
                Fail($"Refusing to confirm: Asking Price currently shows {observed:N0}, expected {proposed:N0}.");
                return false;
            }

            if (proposed == selected.UnitPrice)
            {
                Fail($"Refusing to confirm: proposed price {proposed:N0} equals the original listing price; there is nothing to change.");
                return false;
            }

            if (proposed > selected.UnitPrice && !UsedPriceRecovery)
            {
                Fail($"Refusing to confirm: proposed price {proposed:N0} is above the original listing price {selected.UnitPrice:N0} without an active Price Recovery decision.");
                return false;
            }

            if (proposed < selected.UnitPrice && UsedPriceRecovery)
            {
                Fail($"Refusing to confirm: Price Recovery expected an increase, but proposed price {proposed:N0} is below the original {selected.UnitPrice:N0}.");
                return false;
            }

            // Callback 0 = Confirm. This is the one intentional live-listing mutation in v0.0.4.
            FireIntCallback(&retainerSell->AtkUnitBase, 0);
        }

        ConfirmationSent = true;
        PriceFieldWritten = false;
        SetState(CheckState.WaitingForPostConfirmVerification,
            $"Confirm sent for {proposed:N0}. Waiting for RetainerMarket raw slot {selected.MarketSlot} to report the new live price…",
            PostConfirmTimeoutMs);
        log.Information($"[RealRepriceTest] Confirm callback sent. Expected live price: {proposed:N0} on raw slot {selected.MarketSlot}.");
        return true;
    }

    private void ResetResult()
    {
        LowestCompetitorPrice = null;
        ProposedPrice = null;
        LastRequestId = -1;
        ListingsObserved = 0;
        EligibleListingsObserved = 0;
        OriginalPriceFieldValue = null;
        ObservedPriceFieldValue = null;
        PostConfirmObservedPrice = null;
        PriceFieldWritten = false;
        ConfirmationSent = false;
        LivePriceVerified = false;
        UsedCachedQuote = false;
        ThrottleBackoffs = 0;
        OfferingsPacketsObserved = 0;
        HqListingsObserved = 0;
        NqListingsObserved = 0;
        NativeQualityRowsMatched = 0;
        NativeQualityCorrections = 0;
        NativePriceCorrections = 0;
        IgnoredStaleOfferingsPackets = 0;
        NativeQualityValidationAvailable = false;
        UsedMatchlistRule = false;
        UsedMinimumPriceFloor = false;
        MaxDropProtectionBlocked = false;
        SuspiciousOutlierBlocked = false;
        UsedPriceRecovery = false;
        PriceRecoverySafetyBlocked = false;
        OutlierNextTierPrice = null;
        OutlierGapPercentObserved = null;
        CalculatedDropPercent = null;
        PriceRecoveryCompetitorPrice = null;
        PriceRecoveryGapGil = null;
        PriceRecoveryGapPercentObserved = null;
        CalculatedRaisePercent = null;
        PriceRecoveryOwnListingBlockPrice = null;
        ownListingPricesForQuote.Clear();
        ResetPendingOfferings();
        expectedRetainerId = 0;
        MatchlistedRetainerName = string.Empty;
        LowestCompetitorRetainerName = string.Empty;
        throttleBackoffsForItem = 0;
        searchResultVisibleAtMs = 0;
        retryMarketAtMs = 0;
        nativeEmptyConfirmedSinceMs = 0;
        expectedMarketRequestId = -1;
        Outcome = ListingRunOutcome.None;
        LastError = string.Empty;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!IsBusy)
            return;

        try
        {
            if (Environment.TickCount64 > deadlineMs)
            {
                if (ConfirmationSent)
                    FailAfterConfirm($"Timed out while {DescribeStateForTimeout()}.");
                else
                    Fail($"Timed out while {DescribeStateForTimeout()}.");
                return;
            }

            unsafe
            {
                switch (state)
                {
                    case CheckState.WaitingForContextMenu:
                    {
                        var contextMenu = GetVisibleAddon("ContextMenu");
                        if (contextMenu is null)
                            return;

                        if (!ExpectedRetainerStillActive(requireNonZero: true, out var observedRetainerId))
                        {
                            Fail($"Safety stop before using the item context menu: active retainer changed. Expected {expectedRetainerId:X}, observed {observedRetainerId:X}.");
                            return;
                        }

                        if (!SelectedListingStillExists())
                        {
                            CompleteMissingListing("The selected listing disappeared or changed before its context menu could be used; skipping safely.");
                            return;
                        }

                        var liveSellList = GetVisibleAddon("RetainerSellList");
                        var rowReason = "the sell-list row mapping is unavailable";
                        if (liveSellList is null ||
                            SelectedListing is not { } menuListing ||
                            !SellListRowTargetsSelectedSlot(liveSellList, menuListing, out rowReason))
                        {
                            Fail($"Safety stop before selecting Adjust Price: {(string.IsNullOrWhiteSpace(rowReason) ? "the sell-list row mapping is unavailable" : rowReason)}.");
                            return;
                        }

                        if (!ContextMenuLooksLikeAdjustPrice(contextMenu, out var contextReason))
                        {
                            Fail($"Safety stop: an unexpected context menu opened instead of the retainer Adjust Price menu ({contextReason}).");
                            return;
                        }

                        FireIntCallback(contextMenu, 0, 0, 0, 0, 0); // Adjust Price
                        SetState(CheckState.WaitingForRetainerSell,
                            "Adjust Price selected; waiting for RetainerSell…", UiTimeoutMs);
                        return;
                    }

                    case CheckState.WaitingForRetainerSell:
                    {
                        var retainerSell = GetVisibleRetainerSell();
                        if (retainerSell is null)
                            return;

                        if (!RetainerSellDialogMatchesSelectedListing(retainerSell, requireOriginalPrice: true, out var mismatchReason))
                        {
                            Fail($"Safety stop: Adjust Price opened for an unexpected listing ({mismatchReason}).");
                            return;
                        }

                        // Duplicate listings of the same item/quality can reuse the fresh quote from
                        // earlier in this automatic run. This avoids hammering the Market Board and
                        // triggering the game's "Please wait and try your search again" throttle.
                        if ((AutoConfirmMode || DryRunMode) && TryApplyCachedMarketQuote())
                            return;

                        var now = Environment.TickCount64;
                        if (now < marketCooldownUntilMs)
                        {
                            retryMarketAtMs = marketCooldownUntilMs;
                            state = CheckState.WaitingForThrottleCooldown;
                            deadlineMs = retryMarketAtMs + UiTimeoutMs;
                            Status = $"Market Board cooldown active; waiting {Math.Max(0, retryMarketAtMs - now)} ms before requesting prices…";
                            return;
                        }

                        RequestComparePrices(&retainerSell->AtkUnitBase);
                        return;
                    }

                    case CheckState.WaitingForSearchResult:
                    {
                        var result = GetVisibleItemSearchResult();
                        if (result is null)
                            return;

                        CaptureExpectedMarketRequestId();
                        DropPendingOfferingsFromWrongRequest();
                        searchResultVisibleAtMs = Environment.TickCount64;
                        SetState(CheckState.WaitingForOfferings,
                            "Market result window is ready; waiting for live offerings…", MarketTimeoutMs);
                        return;
                    }

                    case CheckState.WaitingForOfferings:
                    {
                        var result = GetVisibleItemSearchResult();
                        if (result is null)
                            return;

                        if (SearchResultIsThrottled(result))
                        {
                            HandleMarketThrottle();
                            return;
                        }

                        if (TryTakePendingOfferingsError(out var marketDataError))
                        {
                            Fail($"Market data error: {marketDataError}");
                            return;
                        }

                        var now = Environment.TickCount64;
                        if (TryFinalizeAccumulatedOfferings(now))
                            return;

                        // The ItemSearchResult list can exist briefly with zero rows before the
                        // native listing cache has finished receiving the search. Never treat that
                        // transient UI state as an authoritative "no competitor" result. Require the
                        // native InfoProxy to be on this exact item and to expose a completed
                        // zero-entry/zero-listing state stably before taking the empty-search shortcut.
                        // Do NOT gate on WaitingForListings here: at a retainer bell that flag can stay
                        // true for the lifetime of the sell-list and is not a per-request completion bit.
                        if (OfferingsPacketsObserved == 0
                            && searchResultVisibleAtMs > 0
                            && now - searchResultVisibleAtMs >= EmptyResultUiGraceMs
                            && SearchResultLooksEmpty(result))
                        {
                            if (SelectedListing is { } selected
                                && TryGetNativeSearchState(selected.ItemId, out var nativeListingCount, out var nativeEntryCount)
                                && nativeListingCount == 0
                                && nativeEntryCount == 0)
                            {
                                if (nativeEmptyConfirmedSinceMs == 0)
                                {
                                    nativeEmptyConfirmedSinceMs = now;
                                    Status = "Market UI and the exact native item cache both report 0 listings; confirming the empty result is stable…";
                                    return;
                                }

                                if (now - nativeEmptyConfirmedSinceMs >= NativeEmptyStableMs)
                                {
                                    CacheNoCompetitorQuote();
                                    CompleteWithoutWrite(
                                        "Market check complete: native search stably confirmed zero current listings.",
                                        -1,
                                        ListingRunOutcome.NoEligibleCompetitor);
                                    return;
                                }
                            }
                            else
                            {
                                nativeEmptyConfirmedSinceMs = 0;
                                Status = "Market result window is empty, but the exact native item cache does not yet confirm a completed zero-result state…";
                            }
                        }
                        else
                        {
                            nativeEmptyConfirmedSinceMs = 0;
                        }
                        return;
                    }

                    case CheckState.WaitingForThrottleCooldown:
                    {
                        var now = Environment.TickCount64;
                        if (now < retryMarketAtMs)
                            return;

                        var retainerSell = GetVisibleRetainerSell();
                        if (retainerSell is null)
                            return;

                        if (!RetainerSellDialogMatchesSelectedListing(retainerSell, requireOriginalPrice: true, out var mismatchReason))
                        {
                            Fail($"Safety stop before retrying Compare Prices: Adjust Price no longer matches the selected listing ({mismatchReason}).");
                            return;
                        }

                        RequestComparePrices(&retainerSell->AtkUnitBase, "Market Board cooldown finished; retrying Compare Prices…");
                        return;
                    }

                    case CheckState.WaitingForPriceField:
                    {
                        if (!ExpectedRetainerStillActive(requireNonZero: true, out var observedRetainerId))
                        {
                            Fail($"Safety stop before price write: active retainer changed. Expected {expectedRetainerId:X}, observed {observedRetainerId:X}.");
                            return;
                        }

                        if (!SelectedListingStillExists())
                        {
                            CompleteMissingListing(DryRunMode
                                ? "The retainer listing disappeared or changed before the Dry Run could finalize this suggestion; skipping the stale preview."
                                : "The retainer listing disappeared or changed before the price field was written; skipping this item.");
                            return;
                        }

                        if (DryRunMode)
                        {
                            CompleteDryRunWouldChange(-1, cacheHit: UsedCachedQuote);
                            return;
                        }

                        if (ProposedPrice is not { } proposed)
                        {
                            Fail("No proposed price was available for the field-write stage.");
                            return;
                        }

                        if (proposed is < 1 or > MaxMarketPriceGil || proposed > int.MaxValue)
                        {
                            Fail($"Proposed price {proposed:N0} is outside FFXIV's supported market range (1-{MaxMarketPriceGil:N0}).");
                            return;
                        }

                        var retainerSell = GetVisibleRetainerSell();
                        if (retainerSell is null || retainerSell->AskingPrice is null)
                            return;

                        if (!RetainerSellDialogMatchesSelectedListing(retainerSell, requireOriginalPrice: true, out var mismatchReason))
                        {
                            Fail($"Safety stop before price write: Adjust Price no longer matches the selected listing ({mismatchReason}).");
                            return;
                        }

                        OriginalPriceFieldValue ??= retainerSell->AskingPrice->Value;
                        retainerSell->AskingPrice->SetValue((int)proposed);

                        SetState(CheckState.VerifyingPriceField,
                            $"Wrote {proposed:N0} into Asking Price; verifying before enabling Confirm…", UiTimeoutMs);
                        return;
                    }

                    case CheckState.VerifyingPriceField:
                    {
                        if (!ExpectedRetainerStillActive(requireNonZero: true, out var observedRetainerId))
                        {
                            Fail($"Safety stop while verifying the price field: active retainer changed. Expected {expectedRetainerId:X}, observed {observedRetainerId:X}.");
                            return;
                        }

                        if (ProposedPrice is not { } proposed)
                        {
                            Fail("Proposed price disappeared before field verification.");
                            return;
                        }

                        var retainerSell = GetVisibleRetainerSell();
                        if (retainerSell is null || retainerSell->AskingPrice is null)
                            return;

                        var observed = retainerSell->AskingPrice->Value;
                        ObservedPriceFieldValue = observed;
                        if (observed != (int)proposed)
                            return;

                        PriceFieldWritten = true;
                        state = CheckState.AwaitingConfirmation;
                        deadlineMs = long.MaxValue;
                        LastError = string.Empty;

                        if (AutoConfirmMode)
                        {
                            Status = $"PRICE FIELD VERIFIED at {observed:N0}. Automatic mode: sending the single confirm for this item…";
                            log.Information($"[AutoUndercut] Price field verified at {observed:N0}; auto-confirming this item.");
                            ConfirmPreparedPrice();
                            return;
                        }

                        Status = $"PRICE FIELD VERIFIED at {observed:N0}. Review it now. The live listing has NOT changed yet; use CONFIRM ONE REAL PRICE CHANGE to test the real confirmation path.";
                        log.Information($"[RealRepriceTest] Price field verified at {observed:N0}; awaiting explicit confirmation button.");
                        return;
                    }

                    case CheckState.AwaitingConfirmation:
                        return;

                    case CheckState.WaitingForPostConfirmVerification:
                    {
                        if (!ExpectedRetainerStillActive(requireNonZero: true, out var observedRetainerId))
                        {
                            FailAfterConfirm($"Active retainer changed after Confirm. Expected {expectedRetainerId:X}, observed {observedRetainerId:X}; live-price verification cannot safely continue.");
                            return;
                        }

                        if (SelectedListing is not { } selected || ProposedPrice is not { } proposed)
                        {
                            FailAfterConfirm("Lost selected-listing data after confirmation.");
                            return;
                        }

                        var manager = InventoryManager.Instance();
                        if (manager is null)
                            return;

                        var marketContainer = manager->GetInventoryContainer(InventoryType.RetainerMarket);
                        if (marketContainer is null || !marketContainer->IsLoaded)
                            return;

                        var rawSlot = marketContainer->GetInventorySlot(selected.MarketSlot);
                        if (rawSlot is null || rawSlot->ItemId == 0)
                        {
                            FailAfterConfirm("The listing disappeared after Confirm before its new live price could be verified.");
                            return;
                        }

                        var rawIsHq = (rawSlot->Flags & InventoryItem.ItemFlags.HighQuality) != 0;
                        if (rawSlot->ItemId != selected.ItemId || rawIsHq != selected.IsHq || rawSlot->Quantity != selected.Quantity)
                        {
                            FailAfterConfirm($"Raw slot {selected.MarketSlot} changed to a different listing after Confirm; refusing to verify against the wrong item.");
                            return;
                        }

                        var observedLivePrice = manager->GetRetainerMarketPrice(selected.MarketSlot);
                        PostConfirmObservedPrice = observedLivePrice;

                        if (observedLivePrice == proposed)
                        {
                            LivePriceVerified = true;
                            UpdateCachedOwnPriceAfterVerifiedChange(selected, proposed);
                            Outcome = UsedPriceRecovery
                                ? ListingRunOutcome.PriceRecoveryChanged
                                : UsedMinimumPriceFloor
                                    ? ListingRunOutcome.SafetyFloorChanged
                                    : UsedMatchlistRule
                                        ? ListingRunOutcome.MatchlistProtectedChanged
                                        : ListingRunOutcome.Changed;
                            state = CheckState.Done;
                            LastError = string.Empty;
                            Status = $"SUCCESS: live listing verified at {observedLivePrice:N0} on raw slot {selected.MarketSlot}. Single-item confirmation pipeline passed.";
                            log.Information($"[RealRepriceTest] LIVE PRICE VERIFIED: raw slot {selected.MarketSlot} now reports {observedLivePrice:N0}.");
                            return;
                        }

                        // If the server-reported price moved away from the original but to the wrong value,
                        // fail immediately rather than pretending the requested price succeeded.
                        if (observedLivePrice != 0 && observedLivePrice != selected.UnitPrice)
                        {
                            FailAfterConfirm($"Post-confirm verification mismatch: raw slot {selected.MarketSlot} reports {observedLivePrice:N0}, expected {proposed:N0}.");
                            return;
                        }

                        Status = $"Confirm sent. Waiting for live price update… currently {observedLivePrice:N0}; expected {proposed:N0}.";
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "[RealRepriceTest] State machine error.");
            if (ConfirmationSent)
                FailAfterConfirm($"State-machine error after confirmation: {ex.Message}");
            else
                Fail($"State-machine error: {ex.Message}");
        }
    }

    private void OnOfferingsReceived(IMarketBoardCurrentOfferings offerings)
    {
        // Market Board results arrive in packets of up to 10 listings. Do not make a
        // pricing decision from the first packet: a later packet can contain a lower
        // seller, a Matchlisted seller tied at the cheapest tier, or the next price tier
        // needed by the outlier guard. Copy the packet into plugin-owned data here and
        // finalize it from the framework update loop.
        if (state is not (CheckState.WaitingForSearchResult or CheckState.WaitingForOfferings) || SelectedListing is not { } selected)
            return;

        try
        {
            // Pin every offerings page to the request the native item-search proxy says is
            // currently active. This is especially important when the SAME item is searched
            // again: a late page from the previous request has the same ItemId and would pass
            // an item-only filter, causing an old market price to be reused as if it were fresh.
            var nativeRequestId = TryGetNativeCurrentRequestId(selected.ItemId);
            if (nativeRequestId >= 0)
            {
                expectedMarketRequestId = nativeRequestId;
                if ((byte)offerings.RequestId != (byte)nativeRequestId)
                {
                    IgnoredStaleOfferingsPackets++;
                    log.Debug($"[AutoUndercut] Ignoring stale Market Board request {offerings.RequestId}; native active request is {nativeRequestId} for item {selected.ItemId}.");
                    return;
                }
            }

            if (offerings.ItemListings.Count > 0 && offerings.ItemListings[0].ItemId != selected.ItemId)
            {
                log.Debug($"[AutoUndercut] Ignoring offerings packet for item {offerings.ItemListings[0].ItemId}; waiting for {selected.ItemId}.");
                return;
            }

            var snapshots = offerings.ItemListings
                .Select(offer => new MarketOfferSnapshot(
                    offer.ListingId,
                    offer.ItemId,
                    offer.IsHq,
                    offer.PricePerUnit,
                    offer.ItemQuantity,
                    offer.RetainerId,
                    offer.RetainerName ?? string.Empty))
                .ToArray();

            lock (offeringsLock)
            {
                // Re-check inside the lock because a timeout/throttle/stop can move the
                // state between the initial event check and this packet copy.
                if (state is not (CheckState.WaitingForSearchResult or CheckState.WaitingForOfferings))
                    return;

                if (pendingOfferingsRequestId >= 0 && offerings.RequestId != pendingOfferingsRequestId)
                {
                    // If the old accumulator somehow belongs to a stale same-item request,
                    // prefer the page that matches the native active request instead of
                    // discarding the fresh page.
                    if (expectedMarketRequestId >= 0 && (byte)offerings.RequestId == (byte)expectedMarketRequestId)
                    {
                        log.Debug($"[AutoUndercut] Replacing stale pending Market Board request {pendingOfferingsRequestId} with active request {offerings.RequestId}.");
                        pendingOfferings.Clear();
                        pendingListingIds.Clear();
                        pendingOfferingsRequestId = -1;
                        pendingOfferingsPacketCount = 0;
                        pendingOfferingsLastPacketAtMs = 0;
                    }
                    else
                    {
                        IgnoredStaleOfferingsPackets++;
                        log.Debug($"[AutoUndercut] Ignoring Market Board request {offerings.RequestId}; current request is {pendingOfferingsRequestId}.");
                        return;
                    }
                }

                if (pendingOfferingsRequestId < 0)
                    pendingOfferingsRequestId = offerings.RequestId;

                pendingOfferingsPacketCount++;
                pendingOfferingsLastPacketAtMs = Environment.TickCount64;
                nativeEmptyConfirmedSinceMs = 0;

                foreach (var snapshot in snapshots)
                {
                    // ListingId is the best packet-retransmit identity. If it is unavailable,
                    // keep the row rather than risk collapsing two genuinely identical listings.
                    if (snapshot.ListingId != 0 && !pendingListingIds.Add(snapshot.ListingId))
                        continue;

                    pendingOfferings.Add(snapshot);
                }

                LastRequestId = pendingOfferingsRequestId;
                ListingsObserved = pendingOfferings.Count;
                OfferingsPacketsObserved = pendingOfferingsPacketCount;
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "[AutoUndercut] Failed while copying a Market Board offerings packet.");
            lock (offeringsLock)
                pendingOfferingsError = ex.Message;
        }
    }

    private bool TryFinalizeAccumulatedOfferings(long nowMs)
    {
        PendingMarketOfferBatch batch;

        CaptureExpectedMarketRequestId();
        DropPendingOfferingsFromWrongRequest();

        uint nativeListingCount = 0;
        var nativeStateAvailable = false;
        if (SelectedListing is { } selected)
            nativeStateAvailable = TryGetNativeSearchState(selected.ItemId, out nativeListingCount, out _);

        lock (offeringsLock)
        {
            if (pendingOfferingsPacketCount == 0)
                return false;

            // Cross-check packet completion against the game's own InfoProxy cache when it is
            // already on the exact selected item. This prevents a long inter-packet gap from
            // letting the 750 ms quiet timer finalize an incomplete result set.
            if (nativeStateAvailable && nativeListingCount > pendingOfferings.Count)
                return false;

            // Always wait for a quiet period after the most recent packet. A short packet is
            // normally the final page, but treating it as immediate completion can race a late
            // page and leave the pricing decision based on an incomplete mixed HQ/NQ result set.
            if (nowMs - pendingOfferingsLastPacketAtMs < OfferingsPacketSettleMs)
                return false;

            batch = new PendingMarketOfferBatch(
                pendingOfferingsRequestId,
                pendingOfferings.ToArray(),
                pendingOfferingsPacketCount);

            pendingOfferings.Clear();
            pendingListingIds.Clear();
            pendingOfferingsRequestId = -1;
            pendingOfferingsPacketCount = 0;
            pendingOfferingsLastPacketAtMs = 0;
            pendingOfferingsError = string.Empty;
        }

        EvaluateMarketOfferings(batch);
        return true;
    }

    private void EvaluateMarketOfferings(PendingMarketOfferBatch batch)
    {
        if (state != CheckState.WaitingForOfferings || SelectedListing is not { } selected)
            return;

        if (!ExpectedRetainerStillActive(requireNonZero: true, out var observedRetainerId))
        {
            Fail($"Safety stop: active retainer changed while reading Market Board results. Expected {expectedRetainerId:X}, observed {observedRetainerId:X}.");
            return;
        }

        // Re-check the raw retainer slot at the exact point the market result is consumed.
        // This makes the selected listing's HQ/NQ state authoritative even if the UI or an
        // earlier snapshot changed while the Market Board request was in flight.
        if (!SelectedListingStillExists())
        {
            CompleteMissingListing("The selected retainer listing changed while Market Board data was loading; skipping the stale result safely.");
            return;
        }

        LastRequestId = batch.RequestId;
        ListingsObserved = batch.Offers.Length;
        OfferingsPacketsObserved = batch.PacketCount;
        EligibleListingsObserved = 0;
        HqListingsObserved = 0;
        NqListingsObserved = 0;
        NativeQualityRowsMatched = 0;
        NativeQualityCorrections = 0;
        NativePriceCorrections = 0;

        var nativeOffers = TryReadNativeMarketOfferings(selected.ItemId);
        NativeQualityValidationAvailable = nativeOffers is not null;

        if (batch.Offers.Length == 0)
        {
            CacheNoCompetitorQuote();
            CompleteWithoutWrite(
                "Market check complete: no current market listings exist; skipping this item.",
                batch.RequestId,
                ListingRunOutcome.NoEligibleCompetitor);
            return;
        }

        ulong? lowest = null;
        string lowestRetainerName = string.Empty;
        bool lowestHasMatchlistedRetainer = false;
        string lowestMatchlistedRetainerName = string.Empty;
        var eligiblePrices = new List<ulong>();
        var ownListingPrices = new List<ulong>();

        foreach (var offer in batch.Offers)
        {
            if (offer.ItemId != selected.ItemId)
                continue;

            // The Dalamud offerings event and the game's native InfoProxy cache both expose
            // HQ/NQ. Correlate by exact ListingId + item/price/quantity and use the native cache
            // as a second source when available. This prevents a transient/stale quality bit in
            // one event row from making an NQ listing follow an HQ price (or vice versa).
            var effectiveIsHq = offer.IsHq;
            var effectivePricePerUnit = offer.PricePerUnit;
            if (nativeOffers is not null
                && offer.ListingId != 0
                && nativeOffers.TryGetValue(offer.ListingId, out var nativeOffer)
                && nativeOffer.ItemId == offer.ItemId)
            {
                NativeQualityRowsMatched++;
                if (nativeOffer.IsHq != offer.IsHq)
                {
                    NativeQualityCorrections++;
                    log.Warning($"[AutoUndercut] HQ/NQ disagreement for listing {offer.ListingId:X}: network={(offer.IsHq ? "HQ" : "NQ")}, native={(nativeOffer.IsHq ? "HQ" : "NQ")}; native listing cache wins.");
                }

                if (nativeOffer.PricePerUnit != offer.PricePerUnit)
                {
                    NativePriceCorrections++;
                    log.Warning($"[AutoUndercut] Price disagreement for listing {offer.ListingId:X}: packet={offer.PricePerUnit:N0}, native={nativeOffer.PricePerUnit:N0}; native listing cache wins for the current request.");
                }

                effectiveIsHq = nativeOffer.IsHq;
                effectivePricePerUnit = nativeOffer.PricePerUnit;
            }

            if (effectiveIsHq)
                HqListingsObserved++;
            else
                NqListingsObserved++;

            if (effectiveIsHq != selected.IsHq)
                continue;

            var price = (ulong)effectivePricePerUnit;
            if (price == 0)
                continue;

            // An explicitly Matchlisted retainer is treated as a protected market seller
            // even if it happens to be one of the player's own retainers (useful for tests
            // and intentional same-account matching). Ordinary own retainers stay excluded.
            var isMatchlisted = configuration.IsRetainerMatchlisted(offer.RetainerName);
            var isOwnRetainer = ownRetainerIds.Contains(offer.RetainerId);
            if (isOwnRetainer)
                ownListingPrices.Add(price);
            if (isOwnRetainer && !isMatchlisted)
                continue;

            EligibleListingsObserved++;
            eligiblePrices.Add(price);

            if (!lowest.HasValue || price < lowest.Value)
            {
                lowest = price;
                lowestRetainerName = offer.RetainerName;
                lowestHasMatchlistedRetainer = isMatchlisted;
                lowestMatchlistedRetainerName = isMatchlisted ? offer.RetainerName : string.Empty;
                continue;
            }

            // If several sellers share the cheapest tier, any Matchlisted seller on that
            // tier protects it, even if that seller arrived in a later 10-row packet.
            if (price == lowest.Value && isMatchlisted)
            {
                lowestHasMatchlistedRetainer = true;
                lowestMatchlistedRetainerName = offer.RetainerName;
            }
        }

        ownListingPricesForQuote.Clear();
        ownListingPricesForQuote.AddRange(ownListingPrices);

        if (lowest.HasValue)
        {
            LowestCompetitorPrice = lowest.Value;
            LowestCompetitorRetainerName = lowestRetainerName;
            UsedMatchlistRule = lowestHasMatchlistedRetainer;
            MatchlistedRetainerName = lowestMatchlistedRetainerName;
            ProposedPrice = UsedMatchlistRule
                ? lowest.Value
                : activePricing.CalculateNormalTarget(lowest.Value);

            var cheapestTierCount = eligiblePrices.Count(x => x == lowest.Value);
            var nextTier = eligiblePrices
                .Where(x => x > lowest.Value)
                .OrderBy(x => x)
                .Select(x => (ulong?)x)
                .FirstOrDefault();

            // Cache market facts only. Per-listing decisions (outlier, floor, max-drop,
            // recovery) are deliberately rerun for every duplicate item because they also
            // depend on that specific listing's current price.
            CacheLowestCompetitorQuote(
                lowest.Value,
                UsedMatchlistRule,
                MatchlistedRetainerName,
                LowestCompetitorRetainerName,
                nextTier,
                cheapestTierCount);

            if (TryBlockSuspiciousOutlier(selected, batch.RequestId, cacheHit: false, nextTier, cheapestTierCount))
                return;

            if (ApplySafetyAndNoRaiseRules(selected, batch.RequestId, cacheHit: false))
                return;

            if (DryRunMode)
            {
                CompleteDryRunWouldChange(batch.RequestId, cacheHit: false);
                return;
            }

            CloseSearchResultOnly();
            var actionText = UsedPriceRecovery
                ? $"PRICE RECOVERY: current {selected.UnitPrice:N0}, next eligible competitor {lowest.Value:N0}; raising to {ProposedPrice!.Value:N0}"
                : UsedMatchlistRule
                    ? $"MATCHLIST: {MatchlistedRetainerName} is at the cheapest price {lowest.Value:N0}; target {ProposedPrice!.Value:N0}"
                    : $"Lowest eligible competitor: {lowest.Value:N0}; configured target: {ProposedPrice!.Value:N0} ({activePricing.PricingModeLabel})";
            if (UsedMinimumPriceFloor)
                actionText += $"; safety floor applied at {activePricing.MinimumPriceGil:N0}";
            SetState(CheckState.WaitingForPriceField,
                $"{actionText}. Waiting for Asking Price field…",
                UiTimeoutMs);
            return;
        }

        CacheNoCompetitorQuote();
        CompleteWithoutWrite(
            "Market check complete: no eligible competitor listing was found for the same quality.",
            batch.RequestId,
            ListingRunOutcome.NoEligibleCompetitor);
    }

    private void CaptureExpectedMarketRequestId()
    {
        if (SelectedListing is not { } selected)
            return;

        var requestId = TryGetNativeCurrentRequestId(selected.ItemId);
        if (requestId < 0)
            return;

        expectedMarketRequestId = requestId;
    }

    private void DropPendingOfferingsFromWrongRequest()
    {
        if (expectedMarketRequestId < 0)
            return;

        lock (offeringsLock)
        {
            if (pendingOfferingsRequestId < 0 || (byte)pendingOfferingsRequestId == (byte)expectedMarketRequestId)
                return;

            IgnoredStaleOfferingsPackets += Math.Max(1, pendingOfferingsPacketCount);
            log.Debug($"[AutoUndercut] Dropping pending stale Market Board request {pendingOfferingsRequestId}; native active request is {expectedMarketRequestId}.");
            pendingOfferings.Clear();
            pendingListingIds.Clear();
            pendingOfferingsRequestId = -1;
            pendingOfferingsPacketCount = 0;
            pendingOfferingsLastPacketAtMs = 0;
            pendingOfferingsError = string.Empty;
        }
    }

    private static unsafe int TryGetNativeCurrentRequestId(uint itemId)
    {
        var infoProxy = InfoProxyItemSearch.Instance();
        if (infoProxy is null || infoProxy->SearchItemId != itemId)
            return -1;

        return infoProxy->InfoProxyPageInterface.CurrentRequestId;
    }

    private static bool TryGetNativeSearchState(uint itemId, out uint listingCount, out uint entryCount)
    {
        listingCount = 0;
        entryCount = 0;

        unsafe
        {
            var infoProxy = InfoProxyItemSearch.Instance();
            if (infoProxy is null || infoProxy->SearchItemId != itemId)
                return false;

            listingCount = infoProxy->ListingCount;
            entryCount = infoProxy->InfoProxyPageInterface.InfoProxyInterface.EntryCount;
            return true;
        }
    }

    private static unsafe Dictionary<ulong, NativeMarketOfferSnapshot>? TryReadNativeMarketOfferings(uint itemId)
    {
        var infoProxy = InfoProxyItemSearch.Instance();
        if (infoProxy is null || infoProxy->SearchItemId != itemId)
            return null;

        var reportedCount = (int)infoProxy->ListingCount;
        var count = Math.Min(reportedCount, infoProxy->Listings.Length);
        if (count < 0)
            return null;

        var result = new Dictionary<ulong, NativeMarketOfferSnapshot>();
        foreach (var listing in infoProxy->Listings[..count])
        {
            if (listing.ListingId == 0 || listing.ItemId == 0 || listing.UnitPrice == 0 || listing.Quantity == 0)
                continue;

            result[listing.ListingId] = new NativeMarketOfferSnapshot(
                listing.ItemId,
                listing.IsHqItem,
                listing.UnitPrice,
                listing.Quantity);
        }

        return result;
    }

    private void ResetPendingOfferings()
    {
        lock (offeringsLock)
        {
            pendingOfferings.Clear();
            pendingListingIds.Clear();
            pendingOfferingsRequestId = -1;
            pendingOfferingsPacketCount = 0;
            pendingOfferingsLastPacketAtMs = 0;
            pendingOfferingsError = string.Empty;
        }
    }

    private bool TryTakePendingOfferingsError(out string error)
    {
        lock (offeringsLock)
        {
            error = pendingOfferingsError;
            pendingOfferingsError = string.Empty;
            return !string.IsNullOrWhiteSpace(error);
        }
    }

    private void CompleteWithoutWrite(string status, int requestId, ListingRunOutcome outcome)
    {
        if (requestId >= 0)
            LastRequestId = requestId;
        Outcome = outcome;
        Status = status;
        LastError = string.Empty;
        ResetPendingOfferings();
        CleanupWindows(cancelRetainerSell: true);
        state = CheckState.Done;
        log.Information($"[RealRepriceTest] {status}");
    }

    private void Fail(string error, bool cleanup = true)
    {
        LastError = error;
        Outcome = ListingRunOutcome.FailedBeforeConfirm;
        Status = $"FAILED BEFORE CONFIRM: {error}";
        ResetPendingOfferings();
        if (cleanup)
            CleanupWindows(cancelRetainerSell: true);
        PriceFieldWritten = false;
        state = CheckState.Failed;
        log.Warning($"[RealRepriceTest] {error}");
    }

    private void FailAfterConfirm(string error)
    {
        LastError = error;
        Outcome = ListingRunOutcome.FailedAfterConfirm;
        Status = $"CONFIRM WAS SENT, BUT VERIFICATION FAILED: {error}";
        ResetPendingOfferings();
        PriceFieldWritten = false;
        state = CheckState.Failed;
        log.Warning($"[RealRepriceTest] Post-confirm verification failure: {error}");
    }

    private void SetState(CheckState newState, string status, int timeoutMs)
    {
        state = newState;
        Status = status;
        deadlineMs = Environment.TickCount64 + timeoutMs;
    }

    private string DescribeStateForTimeout() => state switch
    {
        CheckState.WaitingForContextMenu => "waiting for the item context menu",
        CheckState.WaitingForRetainerSell => "waiting for the Adjust Price window",
        CheckState.WaitingForSearchResult => "waiting for the Compare Prices result window",
        CheckState.WaitingForOfferings => "waiting for market-board offerings",
        CheckState.WaitingForThrottleCooldown => "waiting for the Market Board throttle cooldown",
        CheckState.WaitingForPriceField => "waiting for the Asking Price field after closing market results",
        CheckState.VerifyingPriceField => "verifying the written Asking Price field",
        CheckState.WaitingForPostConfirmVerification => "waiting for the live RetainerMarket price to update",
        _ => "waiting for the game UI",
    };

    private unsafe void RequestComparePrices(AtkUnitBase* retainerSell, string? status = null)
    {
        ResetPendingOfferings();
        nativeEmptyConfirmedSinceMs = 0;
        expectedMarketRequestId = -1;
        ListingsObserved = 0;
        EligibleListingsObserved = 0;
        OfferingsPacketsObserved = 0;

        // Arm the receiving state BEFORE firing Compare Prices. The game/network callback is
        // normally asynchronous, but setting state first makes an unusually fast offerings
        // packet impossible to lose between FireCallback() and SetState().
        SetState(CheckState.WaitingForSearchResult,
            status ?? "Compare Prices requested; waiting for ItemSearchResult…", MarketTimeoutMs);
        FireIntCallback(retainerSell, 4);
        CaptureExpectedMarketRequestId();
    }

    private bool TryApplyCachedMarketQuote()
    {
        if (SelectedListing is not { } selected)
            return false;

        var key = new MarketCacheKey(selected.ItemId, selected.IsHq);
        if (!marketQuoteCache.TryGetValue(key, out var cached))
            return false;

        var now = Environment.TickCount64;
        var ttl = cached.HasEligibleCompetitor
            ? MarketQuoteCacheTtlMs
            : NoCompetitorCacheTtlMs;

        if (now - cached.CachedAtMs > ttl)
        {
            marketQuoteCache.Remove(key);
            return false;
        }

        UsedCachedQuote = true;

        if (!ExpectedRetainerStillActive(requireNonZero: true, out var observedRetainerId))
        {
            Fail($"Safety stop on cache hit: active retainer changed. Expected {expectedRetainerId:X}, observed {observedRetainerId:X}.");
            return true;
        }

        if (!cached.HasEligibleCompetitor)
        {
            CompleteWithoutWrite(
                "CACHE HIT: no eligible external listing was found for this item/quality earlier in this run; skipping another Market Board request.",
                -1,
                ListingRunOutcome.NoEligibleCompetitor);
            return true;
        }

        if (cached.LowestCompetitorPrice is not { } lowest)
            return false;

        LowestCompetitorPrice = lowest;
        UsedMatchlistRule = cached.UseMatchlistRule;
        MatchlistedRetainerName = cached.MatchlistedRetainerName ?? string.Empty;
        LowestCompetitorRetainerName = cached.LowestCompetitorRetainerName ?? string.Empty;
        ownListingPricesForQuote.Clear();
        if (cached.OwnListingPrices is { Length: > 0 })
            ownListingPricesForQuote.AddRange(cached.OwnListingPrices);
        ProposedPrice = UsedMatchlistRule ? lowest : activePricing.CalculateNormalTarget(lowest);

        // Outlier classification is intentionally NOT cached. Whether an outlier should
        // block depends on this duplicate listing's current price, so re-run it for every
        // cached quote instead of inheriting the first duplicate's outcome.
        if (TryBlockSuspiciousOutlier(
                selected,
                -1,
                cacheHit: true,
                cached.NextTierPrice,
                cached.CheapestTierCount))
            return true;

        if (ApplySafetyAndNoRaiseRules(selected, -1, cacheHit: true))
            return true;

        if (DryRunMode)
        {
            CompleteDryRunWouldChange(-1, cacheHit: true);
            return true;
        }

        var cacheAction = UsedPriceRecovery
            ? $"PRICE RECOVERY cache hit: current {selected.UnitPrice:N0}, next competitor {lowest:N0}, raising to {ProposedPrice.Value:N0}"
            : UsedMatchlistRule
                ? $"MATCHLIST cache hit: matching {MatchlistedRetainerName} at {ProposedPrice.Value:N0}"
                : $"lowest eligible competitor {lowest:N0}; configured target {ProposedPrice.Value:N0} ({activePricing.PricingModeLabel})";
        if (UsedMinimumPriceFloor)
            cacheAction += $"; safety floor {activePricing.MinimumPriceGil:N0}";
        SetState(CheckState.WaitingForPriceField,
            $"CACHE HIT: {cacheAction}. Skipping duplicate Market Board request and writing Asking Price…",
            UiTimeoutMs);
        log.Information($"[AutoUndercut] Market quote cache hit for item {selected.ItemId} ({(selected.IsHq ? "HQ" : "NQ")}): lowest {lowest:N0}, action={PricingActionLabel}.");
        return true;
    }

    private bool TryBlockSuspiciousOutlier(ListingSnapshot selected, int requestId, bool cacheHit, ulong? nextTierPrice, int cheapestTierCount)
    {
        if (!configuration.OutlierProtectionEnabled || UsedMatchlistRule || LowestCompetitorPrice is not { } lowest || nextTierPrice is not { } next || next <= lowest)
            return false;

        // Outlier protection is only for downward repricing. If our own listing is already
        // below the cheapest eligible competitor, Price Recovery / already-cheapest logic owns it.
        if (selected.UnitPrice < lowest)
            return false;

        if (ProposedPrice is { } rawTarget && rawTarget >= selected.UnitPrice)
            return false;

        // Conservative rule: only a single listing at the cheapest tier can be treated as bait/outlier.
        // If two or more sellers share that tier, assume it is a real market price.
        if (cheapestTierCount != 1)
            return false;

        var gapPercent = (double)(next - lowest) * 100.0 / next;
        OutlierNextTierPrice = next;
        OutlierGapPercentObserved = gapPercent;
        if (gapPercent < configuration.OutlierGapPercent)
            return false;

        SuspiciousOutlierBlocked = true;
        var prefix = cacheHit ? "CACHE HIT: " : string.Empty;
        CompleteWithoutWrite(
            $"{prefix}OUTLIER BLOCK: the single cheapest listing is {lowest:N0}, {gapPercent:F1}% below the next eligible price tier at {next:N0}. Threshold is {configuration.OutlierGapPercent}%. {selected.ItemName} was left unchanged.",
            requestId,
            ListingRunOutcome.SuspiciousOutlierBlocked);
        return true;
    }

    private bool ApplySafetyAndNoRaiseRules(ListingSnapshot selected, int requestId, bool cacheHit)
    {
        if (ProposedPrice is not { } target)
            return false;

        var prefix = cacheHit ? "CACHE HIT: " : string.Empty;
        var lowest = LowestCompetitorPrice;

        // If our own listing is strictly below the cheapest eligible competitor, it is already
        // cheapest. Never push it downward just to satisfy a large percentage-undercut setting.
        // Optional Price Recovery may instead raise it toward the next real competitor tier.
        if (lowest is { } competitor && selected.UnitPrice < competitor)
        {
            PriceRecoveryCompetitorPrice = competitor;
            var gapGil = competitor - selected.UnitPrice;
            // Recovery's percentage option is expressed from OUR current price:
            // 10,000 -> 15,000 is a 50% higher next seller, matching the UI wording.
            var gapPercent = selected.UnitPrice > 0 ? (double)gapGil * 100.0 / selected.UnitPrice : 100.0;
            PriceRecoveryGapGil = gapGil;
            PriceRecoveryGapPercentObserved = gapPercent;

            var recoveryTarget = UsedMatchlistRule
                ? competitor
                : activePricing.CalculateNormalTarget(competitor);

            // Remove one occurrence of this listing's own current price, then treat any
            // remaining own listing STRICTLY BELOW the recovery target as a sibling that
            // would make the raise pointless. An own listing exactly AT the target is safe:
            // both listings can tie there without leapfrogging each other.
            var siblingOwnPrices = ownListingPricesForQuote.ToList();
            var selfPriceIndex = siblingOwnPrices.FindIndex(x => x == selected.UnitPrice);
            if (selfPriceIndex >= 0)
                siblingOwnPrices.RemoveAt(selfPriceIndex);

            var blockingOwnPrice = siblingOwnPrices
                .Where(x => x < recoveryTarget)
                .OrderBy(x => x)
                .Select(x => (ulong?)x)
                .FirstOrDefault();
            if (blockingOwnPrice is { } ownBlock)
            {
                PriceRecoveryOwnListingBlockPrice = ownBlock;
                ProposedPrice = recoveryTarget;
                CompleteWithoutWrite(
                    $"{prefix}Already cheapest: Price Recovery was not applied because another one of your own listings is at {ownBlock:N0}, below the calculated recovery target {recoveryTarget:N0}. Raising this listing would leave your sibling listing cheaper anyway.",
                    requestId,
                    UsedMatchlistRule
                        ? ListingRunOutcome.MatchlistProtectedAlreadyCheapest
                        : ListingRunOutcome.AlreadyCheapest);
                return true;
            }

            var gapPasses = configuration.RecoveryGapMode == PriceRecoveryGapMode.FixedGil
                ? gapGil >= (ulong)Math.Max(1, configuration.PriceRecoveryMinimumGapGil)
                : gapPercent >= configuration.PriceRecoveryMinimumGapPercent;

            if (configuration.PriceRecoveryEnabled && gapPasses && recoveryTarget > selected.UnitPrice)
            {
                var raise = recoveryTarget - selected.UnitPrice;
                var raisePercent = selected.UnitPrice > 0
                    ? (double)raise * 100.0 / selected.UnitPrice
                    : 100.0;
                CalculatedRaisePercent = raisePercent;

                if (configuration.PriceRecoveryMaxRaiseEnabled && raisePercent > configuration.PriceRecoveryMaxRaisePercent)
                {
                    PriceRecoverySafetyBlocked = true;
                    ProposedPrice = recoveryTarget;
                    CompleteWithoutWrite(
                        $"{prefix}PRICE RECOVERY BLOCKED: current {selected.UnitPrice:N0}, next eligible competitor {competitor:N0}, recovery target {recoveryTarget:N0} (+{raisePercent:F1}%). Configured maximum one-run raise is {configuration.PriceRecoveryMaxRaisePercent}%. Listing left unchanged.",
                        requestId,
                        ListingRunOutcome.PriceRecoverySafetyBlocked);
                    return true;
                }

                UsedPriceRecovery = true;
                ProposedPrice = recoveryTarget;
                return false;
            }

            ProposedPrice = recoveryTarget;
            var gapRequirement = configuration.RecoveryGapMode == PriceRecoveryGapMode.FixedGil
                ? $"{configuration.PriceRecoveryMinimumGapGil:N0} gil"
                : $"{configuration.PriceRecoveryMinimumGapPercent}%";
            var reason = UsedMatchlistRule
                ? $"{prefix}Matchlist protected {MatchlistedRetainerName} at {competitor:N0}; current price {selected.UnitPrice:N0} is already cheaper. Price Recovery {(configuration.PriceRecoveryEnabled ? $"did not meet the configured minimum gap ({gapRequirement})" : "is disabled")}."
                : $"{prefix}Already cheapest: current price {selected.UnitPrice:N0}, next eligible competitor {competitor:N0} (gap {gapGil:N0} gil / {gapPercent:F1}%). Price Recovery {(configuration.PriceRecoveryEnabled ? $"did not meet the configured minimum gap ({gapRequirement}) or target would not raise the listing" : "is disabled")}.";
            CompleteWithoutWrite(
                reason,
                requestId,
                UsedMatchlistRule
                    ? ListingRunOutcome.MatchlistProtectedAlreadyCheapest
                    : ListingRunOutcome.AlreadyCheapest);
            return true;
        }

        // Minimum-price floors only constrain downward moves. They must never become an excuse
        // to raise an already-cheapest listing; Price Recovery is the only path allowed to raise.
        if (activePricing.MinimumPriceEnabled)
        {
            var floor = (ulong)Math.Max(1, activePricing.MinimumPriceGil);
            if (target < floor)
            {
                target = floor;
                ProposedPrice = floor;
                UsedMinimumPriceFloor = true;
            }
        }

        if (target >= selected.UnitPrice)
        {
            if (UsedMinimumPriceFloor)
            {
                CompleteWithoutWrite(
                    $"{prefix}Safety floor {activePricing.MinimumPriceGil:N0} prevents the calculated target from going lower; current price {selected.UnitPrice:N0} will not be raised or changed.",
                    requestId,
                    ListingRunOutcome.SafetyFloorProtectedNoChange);
                return true;
            }

            var reason = UsedMatchlistRule
                ? $"{prefix}Matchlist protected {MatchlistedRetainerName} at {LowestCompetitorPrice:N0}; current price {selected.UnitPrice:N0} already satisfies that tier."
                : $"{prefix}No repricing needed: current price {selected.UnitPrice:N0} is already at the configured target {target:N0}.";
            CompleteWithoutWrite(
                reason,
                requestId,
                UsedMatchlistRule
                    ? ListingRunOutcome.MatchlistProtectedAlreadyCheapest
                    : ListingRunOutcome.AlreadyCheapest);
            return true;
        }

        if (activePricing.MaxDropProtectionEnabled && selected.UnitPrice > 0)
        {
            var drop = selected.UnitPrice - target;
            var dropPercent = (double)drop * 100.0 / selected.UnitPrice;
            CalculatedDropPercent = dropPercent;

            if (dropPercent > activePricing.MaxDropPercent)
            {
                MaxDropProtectionBlocked = true;
                CompleteWithoutWrite(
                    $"{prefix}SAFETY BLOCK: calculated drop from {selected.UnitPrice:N0} to {target:N0} is {dropPercent:F1}%, above the configured {activePricing.MaxDropPercent}% maximum. Listing left unchanged.",
                    requestId,
                    ListingRunOutcome.SafetyMaxDropBlocked);
                return true;
            }
        }

        return false;
    }

    private void CompleteDryRunWouldChange(int requestId, bool cacheHit)
    {
        if (SelectedListing is not { } selected || ProposedPrice is not { } proposed)
        {
            Fail("Dry-run preview lost the selected listing or proposed price before completion.");
            return;
        }

        var prefix = cacheHit ? "CACHE HIT: " : string.Empty;
        var reason = UsedPriceRecovery
            ? UsedMatchlistRule
                ? $"PRICE RECOVERY: raise to MATCH {MatchlistedRetainerName} at {proposed:N0}"
                : $"PRICE RECOVERY: raise to {proposed:N0}, remaining cheapest using {activePricing.PricingModeLabel}"
            : UsedMatchlistRule
                ? $"MATCH {MatchlistedRetainerName} at {proposed:N0}"
                : $"UNDERCUT to {proposed:N0} using {activePricing.PricingModeLabel}";

        if (UsedMinimumPriceFloor)
            reason += $" with safety floor {activePricing.MinimumPriceGil:N0}";

        var outcome = UsedPriceRecovery
            ? ListingRunOutcome.DryRunPriceRecoveryWouldChange
            : UsedMinimumPriceFloor
                ? ListingRunOutcome.DryRunSafetyFloorWouldChange
                : UsedMatchlistRule
                    ? ListingRunOutcome.DryRunMatchlistWouldChange
                    : ListingRunOutcome.DryRunWouldChange;

        CompleteWithoutWrite(
            $"{prefix}DRY RUN: {selected.ItemName} would change from {selected.UnitPrice:N0} to {proposed:N0} ({reason}). No price field was written and no Confirm was sent.",
            requestId,
            outcome);
    }

    private void CacheLowestCompetitorQuote(
        ulong lowest,
        bool useMatchlistRule,
        string matchlistedRetainerName,
        string lowestCompetitorRetainerName,
        ulong? nextTierPrice,
        int cheapestTierCount)
    {
        if ((!AutoConfirmMode && !DryRunMode) || SelectedListing is not { } selected)
            return;

        marketQuoteCache[new MarketCacheKey(selected.ItemId, selected.IsHq)] =
            new CachedMarketQuote(
                true,
                lowest,
                useMatchlistRule,
                matchlistedRetainerName,
                lowestCompetitorRetainerName,
                nextTierPrice,
                cheapestTierCount,
                ownListingPricesForQuote.ToArray(),
                Environment.TickCount64);
    }

    private void UpdateCachedOwnPriceAfterVerifiedChange(ListingSnapshot selected, ulong newPrice)
    {
        var key = new MarketCacheKey(selected.ItemId, selected.IsHq);
        if (!marketQuoteCache.TryGetValue(key, out var cached) || !cached.HasEligibleCompetitor)
            return;

        var ownPrices = cached.OwnListingPrices?.ToList() ?? [];
        var index = ownPrices.FindIndex(x => x == selected.UnitPrice);
        if (index < 0)
            return;

        ownPrices[index] = newPrice;
        marketQuoteCache[key] = new CachedMarketQuote(
            cached.HasEligibleCompetitor,
            cached.LowestCompetitorPrice,
            cached.UseMatchlistRule,
            cached.MatchlistedRetainerName,
            cached.LowestCompetitorRetainerName,
            cached.NextTierPrice,
            cached.CheapestTierCount,
            ownPrices.ToArray(),
            cached.CachedAtMs);
    }

    private void CacheNoCompetitorQuote()
    {
        if ((!AutoConfirmMode && !DryRunMode) || SelectedListing is not { } selected)
            return;

        marketQuoteCache[new MarketCacheKey(selected.ItemId, selected.IsHq)] =
            new CachedMarketQuote(
                false,
                null,
                false,
                string.Empty,
                string.Empty,
                null,
                0,
                [],
                Environment.TickCount64);
    }

    private unsafe AddonItemSearchResult* GetVisibleItemSearchResult()
    {
        var addonRef = gameGui.GetAddonByName("ItemSearchResult");
        if (addonRef.IsNull || !addonRef.IsVisible)
            return null;

        return (AddonItemSearchResult*)addonRef.Address;
    }

    private static unsafe bool SearchResultIsThrottled(AddonItemSearchResult* addon)
    {
        if (addon is null || addon->ErrorMessage is null)
            return false;

        var errorVisible = addon->ErrorMessage->AtkResNode.IsVisible();
        var hitsVisible = addon->HitsMessage is not null && addon->HitsMessage->AtkResNode.IsVisible();
        return errorVisible && !hitsVisible;
    }

    private static unsafe bool SearchResultLooksEmpty(AddonItemSearchResult* addon)
    {
        if (addon is null || addon->Results is null || addon->HitsMessage is null)
            return false;

        var errorVisible = addon->ErrorMessage is not null && addon->ErrorMessage->AtkResNode.IsVisible();
        if (errorVisible)
            return false;

        return addon->HitsMessage->AtkResNode.IsVisible() && addon->Results->ListLength == 0;
    }

    private void HandleMarketThrottle()
    {
        if (SelectedListing is not { } selected)
        {
            Fail("Market Board throttle detected, but the selected listing context was lost.");
            return;
        }

        throttleBackoffsForItem++;
        ThrottleBackoffs++;

        if (throttleBackoffsForItem > MaxThrottleBackoffsPerItem)
        {
            CompleteWithoutWrite(
                $"Market Board remained throttled after {MaxThrottleBackoffsPerItem} controlled backoff(s); skipped {selected.ItemName} without retrying the whole item workflow.",
                -1,
                ListingRunOutcome.MarketThrottled);
            return;
        }

        var delay = Math.Min(
            MarketThrottleBaseDelayMs + ((throttleBackoffsForItem - 1) * MarketThrottleStepMs),
            MarketThrottleMaxDelayMs);

        var now = Environment.TickCount64;
        marketCooldownUntilMs = now + delay;
        retryMarketAtMs = marketCooldownUntilMs;
        ResetPendingOfferings();
        CloseSearchResultOnly();
        state = CheckState.WaitingForThrottleCooldown;
        deadlineMs = retryMarketAtMs + UiTimeoutMs;
        Status = $"Market Board throttle detected. Controlled cooldown {delay} ms ({throttleBackoffsForItem}/{MaxThrottleBackoffsPerItem}); no full-item retry will be counted.";
        log.Warning($"[AutoUndercut] Market Board throttle for item {selected.ItemId}; backing off {delay} ms before retrying Compare Prices.");
    }

    private unsafe void CloseSearchResultOnly()
    {
        var searchResult = GetVisibleAddon("ItemSearchResult");
        if (searchResult is not null)
            searchResult->Close(true);
    }

    private unsafe void CleanupWindows(bool cancelRetainerSell)
    {
        try
        {
            var searchResult = GetVisibleAddon("ItemSearchResult");
            if (searchResult is not null)
                searchResult->Close(true);

            var contextMenu = GetVisibleAddon("ContextMenu");
            if (contextMenu is not null)
                contextMenu->Close(true);

            var retainerSell = GetVisibleRetainerSell();
            if (retainerSell is not null)
            {
                if (cancelRetainerSell)
                    FireIntCallback(&retainerSell->AtkUnitBase, 1); // Cancel

                ((AtkUnitBase*)retainerSell)->Close(true);
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[RealRepriceTest] Cleanup encountered an error.");
        }
    }

    private static unsafe bool ContextMenuLooksLikeAdjustPrice(AtkUnitBase* contextMenu, out string reason)
    {
        reason = string.Empty;
        if (contextMenu is null || contextMenu->AtkValues is null || contextMenu->AtkValuesCount <= 8)
        {
            reason = "context-menu values are unavailable";
            return false;
        }

        // Current ContextMenu layout: AtkValues[0] is entry count and entries start at 8.
        // For a retainer market listing, the first action is the game's "Adjust Price" action.
        // Verifying this prevents an unrelated user/plugin context menu from receiving our index-0 click.
        var countValue = contextMenu->AtkValues[0];
        var count = countValue.Type switch
        {
            AtkValueType.UInt => (int)countValue.UInt,
            AtkValueType.Int => Math.Max(0, countValue.Int),
            _ => 0,
        };
        if (count <= 0)
        {
            reason = "context menu contains no selectable entries";
            return false;
        }

        var firstEntry = contextMenu->AtkValues[8].GetValueAsString();
        if (string.IsNullOrWhiteSpace(firstEntry) ||
            !firstEntry.Contains("Adjust Price", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"first action is '{firstEntry}', not 'Adjust Price'";
            return false;
        }

        return true;
    }

    private unsafe bool RetainerSellDialogMatchesSelectedListing(
        AddonRetainerSell* addon,
        bool requireOriginalPrice,
        out string mismatchReason)
    {
        mismatchReason = string.Empty;
        if (addon is null || SelectedListing is not { } selected)
        {
            mismatchReason = "dialog or selected-listing context is unavailable";
            return false;
        }

        // Live testing showed why we should not infer identity from generated child-control
        // pointers or hard-coded node indices: those are not reliable enough across the live
        // Adjust Price layout. The stable identity information exposed by this addon is in
        // its AtkValues, which is also what established retainer-pricing plugins use:
        //   [1] item display name (including the HQ icon)
        //   [5] original asking price
        //
        // Quantity is already pinned independently by SelectedListingStillExists(), and the
        // exact UI row -> raw RetainerMarket slot mapping is rechecked immediately before
        // opening/selecting Adjust Price. That gives us multiple independent identity checks
        // without trusting an unstable UI node number.
        var unit = &addon->AtkUnitBase;
        if (unit->AtkValues is null || unit->AtkValuesCount <= 5)
        {
            mismatchReason = "Adjust Price data is not ready";
            return false;
        }

        var displayedName = unit->AtkValues[1].GetValueAsString();
        if (string.IsNullOrWhiteSpace(displayedName) ||
            !displayedName.Contains(selected.ItemName, StringComparison.Ordinal))
        {
            mismatchReason = $"item name is '{displayedName}', expected '{selected.ItemName}'";
            return false;
        }

        var dialogIsHq = displayedName.Contains('\uE03C');
        if (dialogIsHq != selected.IsHq)
        {
            mismatchReason = $"quality is {(dialogIsHq ? "HQ" : "NQ")}, expected {(selected.IsHq ? "HQ" : "NQ")}";
            return false;
        }

        if (requireOriginalPrice)
        {
            var priceValue = unit->AtkValues[5];
            var originalPrice = priceValue.Type switch
            {
                AtkValueType.Int => (long)priceValue.Int,
                AtkValueType.UInt => (long)priceValue.UInt,
                AtkValueType.Int64 => priceValue.Int64,
                AtkValueType.UInt64 when priceValue.UInt64 <= long.MaxValue => (long)priceValue.UInt64,
                _ => -1L,
            };

            if (originalPrice != (long)selected.UnitPrice)
            {
                mismatchReason = $"dialog original price is {originalPrice:N0}, expected snapshot {selected.UnitPrice:N0}";
                return false;
            }
        }

        return true;
    }

    private static unsafe bool SellListRowTargetsSelectedSlot(
        AtkUnitBase* sellList,
        ListingSnapshot selected,
        out string reason)
    {
        reason = string.Empty;
        if (sellList is null || sellList->AtkValues is null)
        {
            reason = "the sell-list row mapping is unavailable";
            return false;
        }

        if (selected.UiRow is < 0 or >= 20 || selected.MarketSlot is < 0 or >= 20)
        {
            reason = $"invalid selected row/slot ({selected.UiRow}/{selected.MarketSlot})";
            return false;
        }

        const int firstSlotValueIndex = 15;
        const int valuesPerRow = 13;
        var atkIndex = firstSlotValueIndex + (selected.UiRow * valuesPerRow);
        if (atkIndex < 0 || atkIndex >= sellList->AtkValuesCount)
        {
            reason = $"row {selected.UiRow + 1} has no current slot mapping";
            return false;
        }

        var value = sellList->AtkValues[atkIndex];
        if (value.Type is not (AtkValueType.Int or AtkValueType.UInt))
        {
            reason = $"row {selected.UiRow + 1} mapping is not ready";
            return false;
        }

        var mappedSlot = value.Int;
        if (mappedSlot != selected.MarketSlot)
        {
            reason = $"row {selected.UiRow + 1} now points to raw slot {mappedSlot}, expected {selected.MarketSlot}";
            return false;
        }

        return true;
    }

    private unsafe bool SelectedListingStillExists()
    {
        if (SelectedListing is not { } selected)
            return false;

        if (!ExpectedRetainerStillActive(requireNonZero: true, out _))
            return false;

        var manager = InventoryManager.Instance();
        if (manager is null)
            return false;

        var container = manager->GetInventoryContainer(InventoryType.RetainerMarket);
        if (container is null || !container->IsLoaded)
            return false;

        var slot = container->GetInventorySlot(selected.MarketSlot);
        if (slot is null || slot->ItemId == 0)
            return false;

        var isHq = (slot->Flags & InventoryItem.ItemFlags.HighQuality) != 0;
        if (slot->ItemId != selected.ItemId || isHq != selected.IsHq || slot->Quantity != selected.Quantity)
            return false;

        // Protect against another plugin/user action changing this exact listing after our
        // snapshot but before our write/Confirm. The raw server-side market price must still
        // match the snapshot we based the decision on.
        var livePrice = manager->GetRetainerMarketPrice(selected.MarketSlot);
        return livePrice == selected.UnitPrice;
    }

    private bool ExpectedRetainerStillActive(bool requireNonZero, out ulong observedRetainerId)
    {
        observedRetainerId = GetActiveRetainerId();
        if (expectedRetainerId == 0)
            return !requireNonZero;
        if (observedRetainerId == 0)
            return !requireNonZero;
        return observedRetainerId == expectedRetainerId;
    }

    private static unsafe ulong GetActiveRetainerId()
    {
        var gameFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance();
        if (gameFramework is null || gameFramework->UIModule is null)
            return 0;

        var itemOrderModule = gameFramework->UIModule->GetItemOrderModule();
        return itemOrderModule is null ? 0 : itemOrderModule->ActiveRetainerId;
    }

    private void CompleteMissingListing(string status)
    {
        Outcome = ListingRunOutcome.MissingListing;
        Status = status;
        LastError = string.Empty;
        ResetPendingOfferings();
        CleanupWindows(cancelRetainerSell: true);
        PriceFieldWritten = false;
        state = CheckState.Done;
        log.Information($"[AutoUndercut] {status}");
    }

    private bool RefreshOwnRetainers()
    {
        ownRetainerIds.Clear();

        unsafe
        {
            var manager = RetainerManager.Instance();
            if (manager is null || !manager->IsReady)
                return false;

            var count = manager->GetRetainerCount();
            for (uint i = 0; i < count; i++)
            {
                var retainer = manager->GetRetainerBySortedIndex(i);
                if (retainer is not null && retainer->RetainerId != 0)
                    ownRetainerIds.Add(retainer->RetainerId);
            }
        }

        return ownRetainerIds.Count > 0;
    }

    private unsafe AtkUnitBase* GetVisibleAddon(string name)
    {
        var addonRef = gameGui.GetAddonByName(name);
        if (addonRef.IsNull || !addonRef.IsVisible)
            return null;

        return (AtkUnitBase*)addonRef.Address;
    }

    private unsafe AddonRetainerSell* GetVisibleRetainerSell()
    {
        var addonRef = gameGui.GetAddonByName("RetainerSell");
        if (addonRef.IsNull || !addonRef.IsVisible)
            return null;

        return (AddonRetainerSell*)addonRef.Address;
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

    private enum CheckState
    {
        Idle,
        WaitingForContextMenu,
        WaitingForRetainerSell,
        WaitingForSearchResult,
        WaitingForOfferings,
        WaitingForThrottleCooldown,
        WaitingForPriceField,
        VerifyingPriceField,
        AwaitingConfirmation,
        WaitingForPostConfirmVerification,
        Done,
        Failed,
    }

    private readonly record struct MarketCacheKey(uint ItemId, bool IsHq);
    private readonly record struct MarketOfferSnapshot(
        ulong ListingId,
        uint ItemId,
        bool IsHq,
        uint PricePerUnit,
        uint Quantity,
        ulong RetainerId,
        string RetainerName);
    private readonly record struct NativeMarketOfferSnapshot(
        uint ItemId,
        bool IsHq,
        uint PricePerUnit,
        uint Quantity);
    private readonly record struct PendingMarketOfferBatch(int RequestId, MarketOfferSnapshot[] Offers, int PacketCount);
    private readonly record struct CachedMarketQuote(
        bool HasEligibleCompetitor,
        ulong? LowestCompetitorPrice,
        bool UseMatchlistRule,
        string MatchlistedRetainerName,
        string LowestCompetitorRetainerName,
        ulong? NextTierPrice,
        int CheapestTierCount,
        ulong[] OwnListingPrices,
        long CachedAtMs);
}

public enum ListingRunOutcome
{
    None,
    Changed,
    AlreadyCheapest,
    MatchlistProtectedChanged,
    MatchlistProtectedAlreadyCheapest,
    DryRunWouldChange,
    DryRunMatchlistWouldChange,
    DryRunSafetyFloorWouldChange,
    DryRunPriceRecoveryWouldChange,
    ItemRuleIgnored,
    SafetyFloorChanged,
    SafetyFloorProtectedNoChange,
    SafetyMaxDropBlocked,
    PriceRecoveryChanged,
    PriceRecoverySafetyBlocked,
    SuspiciousOutlierBlocked,
    NoEligibleCompetitor,
    MarketThrottled,
    MissingListing,
    FailedBeforeConfirm,
    FailedAfterConfirm,
}
