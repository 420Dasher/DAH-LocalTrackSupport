using Dalamud.Configuration;

namespace APGambaHax;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;
    // Off by default; only free-play practice shifts can be assisted.
    public bool PracticeAutomationEnabled { get; set; } = false;

    public bool UseDalamudColors { get; set; }
    public uint[] MatchedStyleColors { get; set; } = Array.Empty<uint>();

    // Rewards, Barkeep, Missions, Originals, Blackjack, Challenges.
    public bool[] ModulesEnabled { get; set; } =
        { true, true, true, true, true, true };

    // Manual local reminder only, not server claim state.
    public DateTime LastTimedClaimUtc { get; set; } = DateTime.MinValue;
}
