using System;
using System.Reflection;

namespace APGambaHax;

// One scoring engine for practice and wager shifts.
// Server actions go through Aetherphone's existing cabinet/store lifecycle.
internal sealed class BarkeepAutomation
{
    private const BindingFlags Flags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private const long Entry = 2000;
    private const int Perfect = 100;

    private object? cabinet;
    private object? trackedShift;
    private object? practiceShift;
    private string roundId = "";
    private string settledRoundId = "";
    private bool managingWager;
    private bool startPending;
    private bool faulted;
    private bool batchArmed;
    private int batchStartWagers;
    private long batchStartNet;
    private int batchStartPerfect;
    private int batchEndWagers;
    private long batchEndNet;
    private int batchEndPerfect;
    private bool hasBatch;
    private bool includePendingSettlement;
    private bool paused;
    private DateTime requestedAt;
    private DateTime nextStartAt = DateTime.MaxValue;
    private DateTime observedDailyNetUtcDate = DateTime.MinValue;
    private int batchEffectiveLimit = 1;
    private long afterEntry;
    private bool afterEntryKnown;

    public string Status { get; private set; } = "Idle";
    public string LastResult { get; private set; } = "No wager settled yet.";
    public int Wagers { get; private set; }
    public int PerfectWagers { get; private set; }
    public int PracticeRuns { get; private set; }
    public long SessionNet { get; private set; }
    public long? NetWinToday { get; private set; }
    public bool Capped { get; private set; }

    private long historyPositiveNet;
    private bool historyLoaded;
    private bool observedLiveUnsettled;

    public string FreshSettlementRoundId { get; private set; } = "";

    public bool HistoryLoaded => historyLoaded;
    public long HistoryPositiveNet => historyPositiveNet;
    public bool DailyNetConfirmed => NetWinToday.HasValue;

    public bool DailyCapReached =>
        CasinoProfitLimits.DailyLimitReached(
            "Barkeep", NetWinToday, Capped) ||
        (historyLoaded &&
         historyPositiveNet >= CasinoProfitLimits.BarkeepDailyNetCap);

    public bool CanStartBatch =>
        DailyNetConfirmed && !DailyCapReached;

    public int DailyShiftsRemaining =>
        !CanStartBatch ? 0 :
        CasinoProfitLimits.BarkeepRemainingPerfectShifts(
            Math.Max(NetWinToday!.Value, historyPositiveNet));

    public int MaximumNewBatchShifts => DailyShiftsRemaining;

    public int BatchEffectiveLimit => batchEffectiveLimit;

    public int EffectiveBatchLimit(int configured)
    {
        if (!CanStartBatch)
            return 0;

        int completed = batchArmed
            ? Wagers - batchStartWagers
            : 0;

        int dailyBound = completed + DailyShiftsRemaining;

        return Math.Min(
            Math.Clamp(configured, 1, 9999),
            Math.Clamp(
                dailyBound,
                0,
                CasinoProfitLimits.MaximumFreshPerfectShifts));
    }

    public void RestoreDailySnapshot(long? net, bool capped)
    {
        if (batchArmed)
        {
            StopBatch();
            DisableWagersRequested = true;
        }

        NetWinToday = net;
        Capped = capped && net.HasValue;
        historyPositiveNet = 0;
        historyLoaded = false;
        observedLiveUnsettled = false;
        FreshSettlementRoundId = "";
        observedDailyNetUtcDate = net.HasValue
            ? DateTime.UtcNow.Date
            : DateTime.MinValue;
    }

    public void SetHistoryObservation(bool loaded, long positiveNet)
    {
        historyLoaded = loaded;
        historyPositiveNet = loaded
            ? Math.Clamp(
                positiveNet,
                0L,
                CasinoProfitLimits.BarkeepDailyNetCap)
            : 0L;

        if (batchArmed && DailyCapReached)
            Fail("Daily Barkeep cap detected from history");
    }

    private void ExpirePreviousDaySnapshot()
    {
        // Provisional UTC rollover: the actual server reset
        // boundary is not exposed by Barkeep's settlement DTO.
        if (observedDailyNetUtcDate == DateTime.MinValue ||
            observedDailyNetUtcDate == DateTime.UtcNow.Date)
            return;

        observedDailyNetUtcDate = DateTime.MinValue;
        NetWinToday = null;
        Capped = false;
    }
    public bool BatchArmed => batchArmed;
    public bool HasError => faulted;
    public bool HasBatch => hasBatch;
    public int BatchWagers => hasBatch ? batchEndWagers - batchStartWagers : 0;
    public int BatchPerfect => hasBatch ? batchEndPerfect - batchStartPerfect : 0;
    public long BatchNet => hasBatch ? batchEndNet - batchStartNet : 0;
    public string LastStopReason { get; private set; } = "";

    public int NextShiftInSeconds =>
        batchArmed && !paused && nextStartAt != DateTime.MaxValue
            ? Math.Max(0, (int)Math.Ceiling(
                (nextStartAt - DateTime.UtcNow).TotalSeconds))
            : -1;
    public bool Paused => paused;
    public bool ManagingWager => managingWager || startPending;
    public bool DisableWagersRequested { get; private set; }

    private static object? Get(object? target, string name)
    {
        if (target is null) return null;
        var type = target.GetType();

        return type.GetProperty(name, Flags)?.GetValue(target)
            ?? type.GetField(name, Flags)?.GetValue(target);
    }

    private static MethodInfo Method(object target, string name, params Type[] args)
    {
        return target.GetType().GetMethod(name, Flags, null, args, null)
            ?? throw new MissingMethodException(target.GetType().FullName, name);
    }

    private static void Call(object target, string name)
    {
        Method(target, name).Invoke(target, null);
    }

    private static int Int(object target, string name)
    {
        return Get(target, name) is int value
            ? value
            : throw new MissingMemberException(target.GetType().FullName, name);
    }

    private static long? Long(object? target, string name)
    {
        return Get(target, name) is long value ? value : null;
    }

    private static bool Bool(object? target, string name)
    {
        return Get(target, name) is true;
    }

    private static object? Sitting(object candidate)
    {
        return Get(Get(Get(candidate, "store"), "State"), "Sitting");
    }

    private static long? Stack(object candidate)
    {
        return Long(Sitting(candidate), "Stack");
    }

    public void ResetFault()
    {
        if (ManagingWager || batchArmed)
        {
            Status = "Cannot reset during an active batch or wager";
            return;
        }

        faulted = false;
        paused = false;
        Status = LastStopReason.Length > 0 ? LastStopReason : "Ready";
    }

    public void StartBatch(int requestedLimit)
    {
        ExpirePreviousDaySnapshot();

        if (faulted)
        {
            Status = "Reset compatibility error first";
            return;
        }

        if (batchArmed || ManagingWager)
        {
            Status = "Already running";
            return;
        }

        if (DailyCapReached)
        {
            Status = "Daily Barkeep cap reached - batch blocked";
            return;
        }

        if (!DailyNetConfirmed)
        {
            Status = "Daily net unconfirmed - batch blocked";
            return;
        }

        batchEffectiveLimit = EffectiveBatchLimit(requestedLimit);

        if (batchEffectiveLimit <= 0)
        {
            Status = "No profitable Barkeep shifts remaining";
            return;
        }

        hasBatch = true;
        includePendingSettlement = false;
        batchStartWagers = Wagers;
        batchStartNet = SessionNet;
        batchStartPerfect = PerfectWagers;
        batchEndWagers = Wagers;
        batchEndNet = SessionNet;
        batchEndPerfect = PerfectWagers;
        LastStopReason = "";
        batchArmed = true;
        paused = false;
        DisableWagersRequested = false;
        nextStartAt = DateTime.UtcNow;
        Status = "Batch start queued";
    }

    public void Pause()
    {
        paused = true;
        Status = "Batch paused - current wager can finish";
    }

    public void Resume()
    {
        if (!batchArmed || faulted) return;

        if (!CanStartBatch)
        {
            Fail(DailyCapReached
                ? "Daily Barkeep cap reached"
                : "Daily net unconfirmed");
            return;
        }

        paused = false;

        if (!ManagingWager)
            nextStartAt = DateTime.UtcNow;

        Status = "Batch resumed";
    }

    public void StopBatch()
    {
        // No active batch means there was nothing to stop.
        if (!batchArmed)
            return;

        includePendingSettlement = ManagingWager;
        batchArmed = false;
        paused = false;
        nextStartAt = DateTime.MaxValue;
        LastStopReason = ManagingWager
            ? "Stopped - completing current wager"
            : "Stopped manually";
        Status = LastStopReason;
    }

    public void AcknowledgeDisable()
    {
        DisableWagersRequested = false;
    }

    private void Fail(string reason)
    {
        batchArmed = false;
        paused = true;
        nextStartAt = DateTime.MaxValue;
        DisableWagersRequested = true;
        LastStopReason = "Paused - " + reason;
        Status = LastStopReason;
    }

    private void TryStart(object candidate, bool resetSettlement)
    {
        if (!batchArmed || paused || faulted || startPending || managingWager)
            return;

        if (DailyCapReached || !DailyNetConfirmed)
        {
            Fail(DailyCapReached
                ? "Daily Barkeep cap reached"
                : "Daily net unconfirmed");
            return;
        }

        var store = Get(candidate, "store");
        var state = Get(store, "State");
        var play = Get(candidate, "play");
        var shift = Get(candidate, "shift");

        if (state is null || Sitting(candidate) is null)
        {
            Fail("No active casino sitting");
            return;
        }

        if (Bool(state, "StakesPaused") || Bool(state, "Draining"))
        {
            Fail("Casino wagers are paused or draining");
            return;
        }

        if (Stack(candidate) is not long chips || chips < Entry)
        {
            Fail("Insufficient chips");
            return;
        }

        if (Bool(play, "RoundInFlight") || Get(play, "PendingRound") is not null)
        {
            Fail("Aetherphone has another pending round");
            return;
        }

        if (shift is not null)
        {
            if (!resetSettlement || Get(candidate, "settle") is null)
            {
                Fail("Another Barkeep shift is active");
                return;
            }

            Call(candidate, "Reset");

            if (Get(candidate, "shift") is not null)
            {
                Fail("Could not clear previous settlement");
                return;
            }
        }

        Call(candidate, "StartWager");

        if (!Bool(candidate, "startRequested"))
        {
            Fail("Aetherphone declined the wager start");
            return;
        }

        startPending = true;
        requestedAt = DateTime.UtcNow;
        nextStartAt = DateTime.MaxValue;
        Status = "Starting wager through Aetherphone";
    }

    private void PerfectOrder(object candidate, object shift, string mode)
    {
        int patrons = Int(shift, "PatronCount");
        int completed = Int(shift, "CompletedOrders");

        if (patrons < 8 || patrons > 14 ||
            completed < 0 || completed > patrons)
            throw new InvalidOperationException("Invalid Barkeep patron state");

        if (completed == patrons)
        {
            Status = mode + " - all patrons served";
            return;
        }

        double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        double elapsed = (double)(
            Method(shift, "ElapsedSeconds", typeof(double))
                .Invoke(shift, new object[] { now }) ?? 0d
        );

        int allowed = Math.Min(
            patrons, (int)Math.Floor(Math.Max(0d, elapsed) / 4d));

        if (completed >= allowed)
        {
            Status = $"{mode} - {completed}/{patrons} patrons";
            return;
        }

        bool arrived = (bool)(
            Method(shift, "CurrentPatronArrived", typeof(double))
                .Invoke(shift, new object[] { elapsed }) ?? false
        );

        if (!arrived)
        {
            Status = $"{mode} - waiting for patron";
            return;
        }

        int stepCount = Int(shift, "CurrentStepCount");
        int stepIndex = Int(shift, "CurrentStepIndex");

        if (stepCount < 2 || stepCount > 4 ||
            stepIndex < 0 || stepIndex >= stepCount)
            throw new InvalidOperationException("Invalid Barkeep step state");

        var commit = Method(shift, "CommitStepGrade", typeof(int));

        for (int step = stepIndex; step < stepCount; step++)
            commit.Invoke(shift, new object[] { Perfect });

        var verb = Get(candidate, "verbStage");

        if (verb is not null)
            Call(verb, "Cancel");

        Status = $"{mode} - {Int(shift, "CompletedOrders")}/{patrons} patrons";
    }

    private void HandleSettlement(
        object candidate,
        object settlement,
        int shiftLimit,
        int profitLimit,
        int lossLimit,
        bool autoContinue,
        int delay)
    {
        if (roundId == settledRoundId)
            return;

        var resultRoundId = Get(settlement, "RoundId") as string ?? "";

        if (!string.Equals(roundId, resultRoundId, StringComparison.Ordinal))
        {
            Fail("Settlement round ID mismatch");
            return;
        }

        settledRoundId = roundId;
        managingWager = false;
        startPending = false;

        bool granted = Bool(settlement, "Granted");
        string reason = Get(settlement, "Reason") as string ?? "";

        if (!granted)
        {
            LastResult = "Rejected: " + reason;
            Fail("Settlement rejected: " + reason);
            return;
        }

        long payout = Long(settlement, "Payout") ?? 0;
        long stack = Long(settlement, "Stack") ?? 0;

        // Settlement Payout is the server-authoritative return.
        // The returned Stack is retained for reporting.
        long returned = payout;
        long net = payout - Entry;

        Wagers++;
        SessionNet += net;

        if (observedLiveUnsettled)
            FreshSettlementRoundId = roundId;

        if (trackedShift is not null &&
            Int(trackedShift, "Score") == Int(trackedShift, "MaxScore"))
            PerfectWagers++;

        if (batchArmed || includePendingSettlement)
        {
            batchEndWagers = Wagers;
            batchEndNet = SessionNet;
            batchEndPerfect = PerfectWagers;
            includePendingSettlement = false;
        }

        NetWinToday = Long(settlement, "NetWinToday");
        Capped = Bool(settlement, "Capped");
        observedDailyNetUtcDate = DateTime.UtcNow.Date;

        // Tighten the current batch ceiling using the latest
        // server-reported daily net winnings.
        if (batchArmed)
            batchEffectiveLimit = Math.Min(
                batchEffectiveLimit,
                EffectiveBatchLimit(shiftLimit));

        LastResult = $"Server return {returned:N0}, net {net:+#,0;-#,0;0} chips" +
            $", settled stack {stack:N0}";

        string? stop = null;

        if (DailyCapReached)
            stop = "Daily Barkeep cap reached";
        else if (batchArmed &&
                 Wagers - batchStartWagers >= batchEffectiveLimit)
            stop = batchEffectiveLimit < shiftLimit
                ? "Daily batch maximum reached"
                : "Shift limit reached";
        else if (batchArmed && profitLimit > 0 && SessionNet - batchStartNet >= profitLimit)
            stop = "Profit target reached";
        else if (batchArmed && lossLimit > 0 && SessionNet - batchStartNet <= -lossLimit)
            stop = "Loss limit reached";

        if (stop is not null)
        {
            batchArmed = false;
            nextStartAt = DateTime.MaxValue;
            DisableWagersRequested = true;
            LastStopReason = stop == "Shift limit reached"
                ? $"Shift limit reached ({BatchWagers}/{batchEffectiveLimit})"
                : stop;
            Status = LastStopReason;
            return;
        }

        if (batchArmed && paused)
        {
            // Preserve the current batch and its counters.
            // Do not schedule another wager until Resume is pressed.
            nextStartAt = DateTime.MaxValue;
            Status = "Batch paused - wager settled; ready to resume";
        }
        else if (batchArmed && autoContinue)
        {
            nextStartAt = DateTime.UtcNow.AddSeconds(Math.Clamp(delay, 1, 60));
            Status = "Wager settled - next batch shift queued";
        }
        else
        {
            batchArmed = false;
            nextStartAt = DateTime.MaxValue;
            Status = "Wager settled";
        }
    }

    public void Tick(
        object? candidate,
        bool practiceEnabled,
        bool wagerEnabled,
        bool autoContinue,
        int shiftLimit,
        int profitLimit,
        int lossLimit,
        int delay)
    {
        ExpirePreviousDaySnapshot();

        if (faulted)
            return;

        // Disabling the wager switch must never launch another batch shift.
        if (!wagerEnabled && batchArmed)
            StopBatch();

        if (candidate is null)
        {
            if (ManagingWager)
                Fail("Aetherphone connection lost");
            else
                Status = "Waiting for Aetherphone";

            return;
        }

        try
        {
            if (cabinet is not null &&
                !ReferenceEquals(cabinet, candidate) && ManagingWager)
            {
                Fail("Aetherphone instance changed during wager");
                return;
            }

            cabinet = candidate;

            // Aetherphone ordinarily consumes results during its Draw().
            // Its own Tick() also handles finish/retry logic.
            if (startPending || managingWager)
                Call(candidate, "Tick");

            string mode = Get(candidate, "mode")?.ToString() ?? "";
            object? shift = Get(candidate, "shift");
            object? settle = Get(candidate, "settle");

            if (shift is not null && mode == "Practice" &&
                Get(shift, "RoundId") is string practiceId &&
                practiceId.Length == 0)
            {
                if (!ReferenceEquals(practiceShift, shift))
                    practiceShift = shift;

                if (Bool(candidate, "practiceSettled"))
                {
                    if (ReferenceEquals(practiceShift, shift) &&
                        !ReferenceEquals(trackedShift, shift))
                    {
                        PracticeRuns++;
                        trackedShift = shift;
                    }

                    Status = "Practice complete";
                }
                else if (practiceEnabled)
                {
                    PerfectOrder(candidate, shift, "Practice");
                }
                else
                {
                    Status = "Practice automation disabled";
                }

                return;
            }

            if (shift is not null && mode == "Wager" &&
                Get(shift, "RoundId") is string activeId &&
                activeId.Length > 0 && (wagerEnabled || managingWager))
            {
                if (!ReferenceEquals(trackedShift, shift) ||
                    !string.Equals(roundId, activeId, StringComparison.Ordinal))
                {
                    trackedShift = shift;
                    roundId = activeId;
                    managingWager = true;
                    startPending = false;
                    observedLiveUnsettled = false;
                    afterEntryKnown = Stack(candidate) is long;
                    afterEntry = Stack(candidate) ?? 0;
                }

                if (settle is not null)
                {
                    // An already-settled round visible after reload
                    // is not evidence of a new daily settlement.
                    if (!observedLiveUnsettled)
                    {
                        managingWager = false;
                        startPending = false;
                        Status = "Previous wager result";

                        if (batchArmed && !paused &&
                            DateTime.UtcNow >= nextStartAt)
                            TryStart(candidate, true);

                        return;
                    }

                    HandleSettlement(candidate, settle, shiftLimit,
                        profitLimit, lossLimit, autoContinue, delay);

                    if (batchArmed && !paused &&
                        nextStartAt != DateTime.MaxValue &&
                        DateTime.UtcNow >= nextStartAt)
                    {
                        TryStart(candidate, true);
                    }

                    return;
                }

                observedLiveUnsettled = true;
                PerfectOrder(candidate, shift, "Wager");
                return;
            }

            if (startPending)
            {
                var reason = Get(candidate, "inlineReason") as string ?? "";

                if (reason.Length > 0)
                {
                    startPending = false;
                    Fail("Wager start rejected: " + reason);
                    return;
                }

                if ((DateTime.UtcNow - requestedAt).TotalSeconds > 20)
                {
                    startPending = false;
                    Fail("Wager start timed out");
                    return;
                }

                Status = "Waiting for wager start";
                return;
            }

            if (managingWager)
            {
                managingWager = false;
                Fail("Managed wager state disappeared");
                return;
            }

            if (batchArmed && !paused &&
                DateTime.UtcNow >= nextStartAt)
            {
                TryStart(candidate, false);
                return;
            }

            if (batchArmed && paused)
                Status = "Batch paused";
            else if (batchArmed)
                Status = "Waiting for next batch shift";
            else if (!practiceEnabled && !wagerEnabled)
                Status = LastStopReason.Length > 0
                    ? LastStopReason
                    : "Automation disabled";
        }
        catch (Exception exception)
        {
            faulted = true;
            Fail("Compatibility error: " +
                exception.GetBaseException().GetType().Name);
        }
    }
}
