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

internal readonly record struct LiveMission(
    string Id,
    int Slot,
    string Description,
    string Metric,
    string GameKind,
    long Progress,
    long Target,
    long Reward,
    bool Complete,
    bool Claimed);

internal sealed class AetherphoneBridge
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private const BindingFlags StaticFlags =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private DateTime nextReadUtc = DateTime.MinValue;
    private readonly List<LiveBonus> bonuses = new();
    private object? casinoStore;
    private object? missionStore;
    private readonly List<LiveMission> missions = new();

    public string Status { get; private set; } = "Waiting for Aetherphone";
    public string Version { get; private set; } = "--";
    public bool HasCasinoState { get; private set; }

    public string AccountKey { get; private set; } = "";
    public bool BarkeepHistoryLoaded { get; private set; }
    public long BarkeepHistoryPositiveNet { get; private set; }
    public long BarkeepHistorySignedNet { get; private set; }
    public bool BarkeepHistoryHasMore { get; private set; }
    public int BarkeepHistoryRounds { get; private set; }
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
    public IReadOnlyList<LiveMission> Missions => missions;

    public bool MissionsLoaded { get; private set; }
    public int MissionsDayIndex { get; private set; }
    public long MissionsResetsAtUnix { get; private set; }

    public string ClaimingMission =>
        Member(missionStore, "ClaimingMission") as string ?? "";

    public bool TryClaimMission(
        string missionId,
        out string message)
    {
        message = "";

        if (missionStore is null ||
            !MissionsLoaded ||
            AccountKey.Length == 0)
        {
            message = "Mission store is unavailable";
            return false;
        }

        var target = missions.Find(m =>
            string.Equals(m.Id, missionId,
                StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(target.Id) ||
            !target.Complete || target.Claimed)
        {
            message = "Mission is not claimable";
            return false;
        }

        if (MissionsResetsAtUnix > 0 &&
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() >=
                MissionsResetsAtUnix)
        {
            message = "Mission reset pending";
            return false;
        }

        try
        {
            // Verify the authenticated account is still the one
            // from which the mission snapshot was retrieved.
            string currentId =
                Member(missionStore, "AccountId") as string ?? "";

            if (currentId.Length == 0 ||
                Convert.ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(currentId))) !=
                    AccountKey)
            {
                message = "Aetherphone account changed";
                return false;
            }

            if (ClaimingMission.Length > 0)
            {
                message = "Another mission claim is processing";
                return false;
            }

            var method = missionStore.GetType().GetMethod(
                "ClaimMission",
                InstanceFlags,
                null,
                new[] { typeof(string) },
                null);

            if (method is null)
            {
                message = "Aetherphone mission API unavailable";
                return false;
            }

            method.Invoke(
                missionStore,
                new object[] { missionId });

            message = "Request handed to Aetherphone";
            return true;
        }
        catch (Exception exception)
        {
            message = "Compatibility error: " +
                exception.GetBaseException().GetType().Name;
            return false;
        }
    }

    public string ClaimingBonus =>
        Member(casinoStore, "ClaimingBonus") as string ?? "";

    public bool TryClaimBonus(string kind, out string message)
    {
        message = "";

        if (casinoStore is null ||
            !HasCasinoState ||
            AccountKey.Length == 0)
        {
            message = "No authenticated casino state";
            return false;
        }

        if (RewardsAutomation.IndexOf(kind) < 0)
        {
            message = "Unknown bonus type";
            return false;
        }

        var liveBonus = bonuses.Find(b =>
            string.Equals(b.Kind, kind, StringComparison.Ordinal));

        if (!liveBonus.Ready || !liveBonus.Eligible)
        {
            message = "Bonus is not ready or eligible";
            return false;
        }

        try
        {
            // Recheck account identity at the moment of the request.
            var session = Member(casinoStore, "session");
            var userId = Member(
                Member(session, "CurrentUser"), "Id") as string;

            if (Member(session, "IsSignedIn") is not true ||
                string.IsNullOrEmpty(userId) ||
                Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(userId)))
                    != AccountKey)
            {
                message = "Account changed - waiting for refresh";
                return false;
            }

            if (ClaimingBonus.Length > 0)
            {
                message = "Another claim is processing";
                return false;
            }

            var method = casinoStore.GetType().GetMethod(
                "ClaimBonus",
                InstanceFlags,
                null,
                new[] { typeof(string) },
                null);

            if (method is null)
            {
                message = "Aetherphone claim API unavailable";
                return false;
            }

            // The original Aetherphone store owns networking,
            // retries and server-side result handling.
            method.Invoke(casinoStore, new object[] { kind });

            message = "Request handed to Aetherphone";
            return true;
        }
        catch (Exception exception)
        {
            message = "Compatibility error: " +
                exception.GetBaseException().GetType().Name;
            return false;
        }
    }

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
        casinoStore = null;
        missionStore = null;
        missions.Clear();
        MissionsLoaded = false;
        MissionsDayIndex = 0;
        MissionsResetsAtUnix = 0;
        BarkeepCabinet = null;
        AccountKey = "";
        BarkeepHistoryLoaded = false;
        BarkeepHistoryPositiveNet = 0;
        BarkeepHistorySignedNet = 0;
        BarkeepHistoryHasMore = false;
        BarkeepHistoryRounds = 0;
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

    public void Tick(bool includeMissions = false)
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

            // Only an authenticated account may issue bonus claims.
            casinoStore = AccountKey.Length > 0 ? store : null;

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

                        BarkeepHistoryHasMore =
                            Member(history, "HasMore") is true;

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

                                BarkeepHistorySignedNet += payout - stake;
                                BarkeepHistoryRounds++;
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
                BarkeepHistorySignedNet = 0;
                BarkeepHistoryHasMore = false;
                BarkeepHistoryRounds = 0;
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

            // Missions use Aetherphone's existing cached store.
            // Its own EnsureFresh() handles refresh throttling.
            if (includeMissions && AccountKey.Length > 0)
            {
                try
                {
                    var floor = Member(app, "floor");

                    if (floor is not null)
                    {
                        floor.GetType().GetMethod(
                            "EnsureFresh",
                            InstanceFlags)?.Invoke(floor, null);

                        var response = Member(floor, "Missions");

                        if (response is not null)
                        {
                            missionStore = floor;
                            MissionsLoaded = true;
                            MissionsDayIndex = (int)(
                                Number(Member(response, "DayIndex")) ?? 0);

                            MissionsResetsAtUnix =
                                Number(Member(response, "ResetsAtUnix")) ?? 0;

                            var textType = app.GetType().Assembly.GetType(
                                "Aetherphone.Apps.Casino.Strip.MissionText",
                                false);

                            var compose = textType?.GetMethod(
                                "Compose", StaticFlags);

                            if (Member(response, "Missions") is IEnumerable items)
                            {
                                foreach (var mission in items)
                                {
                                    if (mission is null)
                                        continue;

                                    var id = Member(mission, "Id") as string ?? "";
                                    if (id.Length == 0)
                                        continue;

                                    var metric = Member(mission, "Metric")
                                        as string ?? "";

                                    var game = Member(mission, "GameKind")
                                        as string ?? "";

                                    string description = "";

                                    if (compose is not null)
                                    {
                                        try
                                        {
                                            description = compose.Invoke(
                                                null,
                                                new object[] { mission })
                                                as string ?? "";
                                        }
                                        catch
                                        {
                                            // Fall back to raw mission data.
                                        }
                                    }

                                    if (description.Length == 0)
                                    {
                                        description = metric.Length > 0
                                            ? metric + (game.Length > 0
                                                ? " - " + game : "")
                                            : id;
                                    }

                                    missions.Add(new LiveMission(
                                        id,
                                        (int)(Number(Member(mission, "Slot")) ?? 0),
                                        description,
                                        metric,
                                        game,
                                        Number(Member(mission, "Progress")) ?? 0,
                                        Number(Member(mission, "Target")) ?? 0,
                                        Number(Member(mission, "Reward")) ?? 0,
                                        Flag(Member(mission, "Complete")) ?? false,
                                        Flag(Member(mission, "Claimed")) ?? false));
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Keep the Rewards and Barkeep bridges alive.
                    missionStore = null;
                    missions.Clear();
                    MissionsLoaded = false;
                }
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
