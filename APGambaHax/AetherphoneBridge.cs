using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace APGambaHax;

internal readonly record struct LiveBonus(
    string Kind,
    bool Ready,
    bool Eligible,
    long Amount,
    long NextAtUnix,
    int StreakDay);

internal sealed class AetherphoneBridge
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private const BindingFlags StaticFlags =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private DateTime nextReadUtc = DateTime.MinValue;
    private readonly List<LiveBonus> bonuses = new();

    public string Status { get; private set; } = "Waiting for Aetherphone";
    public string Version { get; private set; } = "--";
    public bool HasCasinoState { get; private set; }

    public string AccountKey { get; private set; } = "";
    public bool BarkeepHistoryLoaded { get; private set; }
    public long BarkeepHistoryPositiveNet { get; private set; }
    public object? BarkeepCabinet { get; private set; }
    public long? Chips { get; private set; }
    public int? Level { get; private set; }
    public long? MaxWinPerBet { get; private set; }
    public bool? StakesPaused { get; private set; }
    public bool? DailySpinClaimed { get; private set; }
    public long? DailySpinNextAtUnix { get; private set; }
    public string BarkeepStatus { get; private set; } = "No active shift";
    public int? BarkeepCompleted { get; private set; }
    public int? BarkeepPatrons { get; private set; }
    public int? BarkeepScore { get; private set; }

    public IReadOnlyList<LiveBonus> Bonuses => bonuses;

    private static object? Member(object? target, string name)
    {
        if (target is null)
            return null;

        var type = target.GetType();

        return type.GetProperty(name, InstanceFlags)?.GetValue(target)
            ?? type.GetField(name, InstanceFlags)?.GetValue(target);
    }

    private static long? Number(object? value)
    {
        return value switch
        {
            long number => number,
            int number => number,
            short number => number,
            _ => null
        };
    }

    private static bool? Flag(object? value)
    {
        return value is bool flag ? flag : null;
    }

    private void ResetSnapshot()
    {
        HasCasinoState = false;
        BarkeepCabinet = null;
        AccountKey = "";
        BarkeepHistoryLoaded = false;
        BarkeepHistoryPositiveNet = 0;
        Chips = null;
        Level = null;
        MaxWinPerBet = null;
        StakesPaused = null;
        DailySpinClaimed = null;
        DailySpinNextAtUnix = null;
        BarkeepStatus = "No active shift";
        BarkeepCompleted = null;
        BarkeepPatrons = null;
        BarkeepScore = null;
        bonuses.Clear();
    }

    public void Clear()
    {
        ResetSnapshot();
        Status = "Modules disabled";
        Version = "--";
        nextReadUtc = DateTime.MinValue;
    }

    private static object? FindCasinoApp(out string version)
    {
        version = "--";
        object? bestApp = null;
        var bestScore = -1;

        // DEV13's object path, with candidate selection to avoid
        // preferring an old Aetherphone instance after a reload.
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var pluginType = assembly.GetType("Aetherphone.Plugin", false);
            if (pluginType is null)
                continue;

            var instance = pluginType.GetProperty("Instance", StaticFlags)
                ?.GetValue(null);

            var shell = Member(instance, "shell");
            var apps = Member(shell, "apps") as IEnumerable;

            if (apps is null)
                continue;

            foreach (var app in apps)
            {
                if (app?.GetType().FullName !=
                    "Aetherphone.Apps.Casino.CasinoApp")
                    continue;

                var store = Member(app, "casino");
                var state = Member(store, "State");
                var sitting = Member(state, "Sitting");

                var score = state is null ? 0 : 100;
                if (sitting is not null)
                    score += 100;

                var cabinet = Member(app, "barkeep");
                if (Member(cabinet, "shift") is not null)
                    score += 300;

                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestApp = app;
                version = assembly.GetName().Version?.ToString() ?? "unknown";
            }
        }

        return bestApp;
    }

    public void Tick()
    {
        if (DateTime.UtcNow < nextReadUtc)
            return;

        nextReadUtc = DateTime.UtcNow.AddSeconds(1.5);

        ResetSnapshot();

        try
        {
            var app = FindCasinoApp(out var version);
            Version = version;

            if (app is null)
            {
                Status = "Waiting for Aetherphone";
                return;
            }

            var store = Member(app, "casino");
            var state = Member(store, "State");

            if (state is null)
            {
                Status = "Connected - casino state not loaded";
                return;
            }

            HasCasinoState = true;
            Status = "Connected";

            // Bind cached daily observations to the authenticated
            // Aetherphone account, not to a shared plugin config.
            var session = Member(app, "session");

            var userId = Member(
                Member(session, "CurrentUser"), "Id") as string;

            if (Member(session, "IsSignedIn") is true &&
                !string.IsNullOrWhiteSpace(userId))
            {
                AccountKey = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(userId)));
            }

            // Aetherphone owns the history requests and caching.
            // This is a read of its existing history store.
            try
            {
                if (AccountKey.Length > 0)
                {
                    var history = Member(app, "history");

                    if (history is not null)
                    {
                        history.GetType().GetMethod(
                            "EnsureFresh", InstanceFlags)?.Invoke(
                                history, null);

                        BarkeepHistoryLoaded =
                            Member(history, "Loaded") is true;

                        if (BarkeepHistoryLoaded &&
                            Member(history, "Rounds") is IEnumerable rounds)
                        {
                            long dayStart =
                                new DateTimeOffset(
                                    DateTime.UtcNow.Date,
                                    TimeSpan.Zero).ToUnixTimeSeconds();

                            long dayEnd = dayStart + 86400L;

                            foreach (var round in rounds)
                            {
                                if (round is null ||
                                    Member(round, "GameKind") as string
                                        != "casino.bartender" ||
                                    Member(round, "State") is not int stateCode ||
                                    stateCode != 1)
                                    continue;

                                long created = Number(
                                    Member(round, "CreatedAtUnix")) ?? 0;

                                long settled = Number(
                                    Member(round, "SettledAtUnix")) ?? created;

                                if (settled < dayStart || settled >= dayEnd)
                                    continue;

                                long stake = Number(
                                    Member(round, "Stake")) ?? 0;

                                long payout = Number(
                                    Member(round, "Payout")) ?? 0;

                                long positiveNet = Math.Max(
                                    0L, payout - stake);

                                BarkeepHistoryPositiveNet = Math.Min(
                                    20_000L,
                                    BarkeepHistoryPositiveNet + positiveNet);
                            }
                        }
                    }
                }
            }
            catch
            {
                // History recovery is optional; failure must not
                // break the otherwise working casino bridge.
                BarkeepHistoryLoaded = false;
                BarkeepHistoryPositiveNet = 0;
            }

            var sitting = Member(state, "Sitting");
            Chips = Number(Member(sitting, "Stack"));

            Level = (int?)Number(
                Member(Member(state, "Progress"), "Level"));

            StakesPaused = Flag(Member(state, "StakesPaused"));

            var serverMaxWin = Number(Member(state, "MaxWinPerBet"));
            var ceilingMaxWin = Number(
                Member(Member(state, "Ceiling"), "MaxWinCap"));

            MaxWinPerBet = serverMaxWin is > 0
                ? serverMaxWin
                : ceilingMaxWin is > 0 ? ceilingMaxWin : null;

            // These values are Aetherphone's existing cached
            // server responses. No network requests are made here.
            if (Member(state, "Bonuses") is IEnumerable bonusItems)
            {
                foreach (var item in bonusItems)
                {
                    if (item is null)
                        continue;

                    bonuses.Add(new LiveBonus(
                        Member(item, "Kind") as string ?? "",
                        Flag(Member(item, "Ready")) ?? false,
                        Flag(Member(item, "Eligible")) ?? false,
                        Number(Member(item, "Amount")) ?? 0,
                        Number(Member(item, "NextAtUnix")) ?? 0,
                        (int)(Number(Member(item, "StreakDay")) ?? 0)));
                }
            }

            var spinStore = Member(app, "casinoSpin");
            var spin = Member(spinStore, "Answer");

            if (spin is not null)
            {
                DailySpinClaimed = Flag(Member(spin, "Claimed"));
                DailySpinNextAtUnix = Number(Member(spin, "NextSpinAtUnix"));
            }

            var barkeep = Member(app, "barkeep");
            BarkeepCabinet = barkeep;
            var shift = Member(barkeep, "shift");
            var settlement = Member(barkeep, "settle");

            if (shift is not null)
            {
                BarkeepCompleted = (int?)Number(
                    Member(shift, "CompletedOrders"));

                BarkeepPatrons = (int?)Number(
                    Member(shift, "PatronCount"));

                BarkeepScore = (int?)Number(
                    Member(shift, "Score"));

                BarkeepStatus = settlement is not null
                    ? "Wager settled"
                    : Member(barkeep, "practiceSettled") is true
                        ? "Practice complete"
                        : "Shift active";
            }
        }
        catch (Exception exception)
        {
            ResetSnapshot();
            Status = "Bridge error: " + exception.GetType().Name;
        }
    }
}
