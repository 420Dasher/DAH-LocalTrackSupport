using System;

namespace APGambaHax;

// Keep daily limits separate from global per-round payout ceilings.
// Only Barkeep has a verified game-specific daily net cap so far.
internal static class CasinoProfitLimits
{
    public const long BarkeepDailyNetCap = 20_000;
    public const long BarkeepMaximumReturn = 4_000;
    public const long BarkeepEntry = 2_000;
    public const long BarkeepMaximumNetPerShift =
        BarkeepMaximumReturn - BarkeepEntry;
    public const int MaximumFreshPerfectShifts = 10;

    public static long? DailyNetCapFor(string game) =>
        string.Equals(game, "Barkeep", StringComparison.OrdinalIgnoreCase)
            ? BarkeepDailyNetCap
            : null;

    public static bool DailyLimitReached(
        string game,
        long? serverDailyNet,
        bool serverReportedCap)
    {
        var limit = DailyNetCapFor(game);

        return serverReportedCap ||
            (limit.HasValue &&
             serverDailyNet.HasValue &&
             serverDailyNet.Value >= limit.Value);
    }

    public static int BarkeepRemainingPerfectShifts(long? dailyNet)
    {
        if (!dailyNet.HasValue)
            return MaximumFreshPerfectShifts;

        long remaining = Math.Max(0L, BarkeepDailyNetCap - dailyNet.Value);

        long shifts = (remaining + BarkeepMaximumNetPerShift - 1) /
            BarkeepMaximumNetPerShift;

        return (int)Math.Clamp(shifts, 0L, MaximumFreshPerfectShifts);
    }
}
