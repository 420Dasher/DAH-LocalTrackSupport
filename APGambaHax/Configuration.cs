using Dalamud.Configuration;

namespace APGambaHax;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;
    // Off by default; only free-play practice shifts can be assisted.
    public bool PracticeAutomationEnabled { get; set; } = false;
    public bool WagerAutomationEnabled { get; set; } = false;
    public bool AutoContinueWagers { get; set; } = false;
    public int WagerStopAfter { get; set; } = 1;
    public int WagerStopAtProfit { get; set; } = 0;
    public int WagerStopAtLoss { get; set; } = 0;
    public int WagerDelaySeconds { get; set; } = 3;

    // Keyed by SHA256 of the authenticated Aetherphone account ID.
    // Never stores the account ID itself.
    public Dictionary<string, BarkeepDailyRecord> BarkeepDailyRecords
        { get; set; } = new();

    public bool UseDalamudColors { get; set; }
    public uint[] MatchedStyleColors { get; set; } = Array.Empty<uint>();

    // Rewards, Barkeep, Missions, Originals, Blackjack, Challenges.
    public bool[] ModulesEnabled { get; set; } =
        { true, true, true, true, true, true };

    // Manual local reminder only, not server claim state.
    public DateTime LastTimedClaimUtc { get; set; } = DateTime.MinValue;
}

public sealed class BarkeepDailyRecord
{
    public long NetWinToday { get; set; }
    public bool Capped { get; set; }
    public long ObservedAtUnix { get; set; }
}
