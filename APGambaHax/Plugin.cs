using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace APGambaHax;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService]
    private static IDalamudPluginInterface Pi { get; set; } = null!;

    [PluginService]
    private static ICommandManager Commands { get; set; } = null!;

    private enum Page
    {
        Overview,
        Rewards,
        Barkeep,
        Missions,
        Originals,
        Blackjack,
        Challenges
    }

    private static readonly string[] Pages =
    {
        "Overview", "Rewards", "Barkeep", "Missions",
        "Originals", "Blackjack", "Challenges"
    };

    [PluginService]
    private static IFramework Framework { get; set; } = null!;

    private readonly Configuration cfg;
    private readonly AetherphoneBridge bridge = new();
    private bool mainOpen;
    private bool settingsOpen;
    private Page page = Page.Overview;
    private int barkeepScore = 95;
    private uint[] sampledTheme = Array.Empty<uint>();

    private static string VersionText =>
        "v" + (typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "?");

    public Plugin()
    {
        cfg = Pi.GetPluginConfig() as Configuration ?? new Configuration();

        if (cfg.ModulesEnabled is not { Length: 6 })
        {
            cfg.ModulesEnabled = new[]
            {
                true, true, true, true, true, true
            };
            Save();
        }

        Commands.AddHandler("/apgamba", new CommandInfo(OnCommand)
        {
            HelpMessage = "Open APGambaHax. Subcommands: rewards, barkeep, missions, originals, blackjack, challenges, settings."
        });

        Framework.Update += OnFrameworkUpdate;
        Pi.UiBuilder.Draw += Draw;
        Pi.UiBuilder.OpenMainUi += Open;
        Pi.UiBuilder.OpenConfigUi += OpenSettings;
    }

    private void Save() => Pi.SavePluginConfig(cfg);

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (cfg.ModulesEnabled[0] || cfg.ModulesEnabled[1])
            bridge.Tick();
        else
            bridge.Clear();
    }

    private static string TimeUntil(long unix)
    {
        if (unix <= 0)
            return "unknown";

        var remaining = DateTimeOffset.FromUnixTimeSeconds(unix)
            - DateTimeOffset.UtcNow;

        if (remaining <= TimeSpan.Zero)
            return "available now";

        if (remaining.TotalHours >= 1)
            return $"{Math.Ceiling(remaining.TotalHours)}h";

        return $"{Math.Ceiling(remaining.TotalMinutes)}m";
    }

    private void Open() => mainOpen = true;
    private void OpenSettings() => settingsOpen = true;

    private void OnCommand(string command, string arguments)
    {
        string input = arguments.Trim().ToLowerInvariant();

        if (input == "settings")
        {
            settingsOpen = true;
            return;
        }

        if (Enum.TryParse<Page>(input, true, out var requested) &&
            Enum.IsDefined(requested))
        {
            int index = (int)requested;
            if (index == 0 || cfg.ModulesEnabled[index - 1])
                page = requested;
        }

        mainOpen = true;
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        Pi.UiBuilder.Draw -= Draw;
        Pi.UiBuilder.OpenMainUi -= Open;
        Pi.UiBuilder.OpenConfigUi -= OpenSettings;
        Commands.RemoveHandler("/apgamba");
    }

    private void Draw()
    {
        // Capture the real client colors before overriding them.
        if (settingsOpen)
            sampledTheme = Theme.Capture();

        DrawSettings();
        DrawMain();
    }

    private void DrawMain()
    {
        if (!mainOpen)
            return;

        Theme.Push(cfg);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(7f, 4f));

        try
        {
            ImGui.SetNextWindowSize(
                new Vector2(480f, 340f), ImGuiCond.FirstUseEver);

            bool open = mainOpen;

            if (ImGui.Begin(
                "APGambaHax###APGambaHaxMain",
                ref open,
                ImGuiWindowFlags.NoCollapse))
            {
                ImGui.TextDisabled(VersionText);
                ImGui.SameLine(ImGui.GetWindowWidth() - 91f);

                if (ImGui.SmallButton("Settings"))
                    settingsOpen = true;

                ImGui.Spacing();

                float gap = 6f;
                float buttonWidth = MathF.Max(
                    90f, (ImGui.GetContentRegionAvail().X - 2f * gap) / 3f);

                for (int i = 0; i < Pages.Length; i++)
                {
                    if (i % 3 != 0)
                        ImGui.SameLine(0f, gap);

                    bool enabled = i == 0 || cfg.ModulesEnabled[i - 1];

                    if (!enabled)
                        ImGui.BeginDisabled();

                    bool selected = (int)page == i;

                    if (selected)
                        ImGui.PushStyleColor(
                            ImGuiCol.Button, Theme.Get(cfg, 14));

                    if (ImGui.Button(Pages[i] + "##page" + i,
                        new Vector2(buttonWidth, 25f)))
                    {
                        page = (Page)i;
                    }

                    if (selected)
                        ImGui.PopStyleColor();

                    if (!enabled)
                        ImGui.EndDisabled();
                }

                ImGui.Spacing();
                ImGui.Separator();

                switch (page)
                {
                    case Page.Overview: DrawOverview(); break;
                    case Page.Rewards: DrawRewards(); break;
                    case Page.Barkeep: DrawBarkeep(); break;
                    case Page.Missions: DrawPlaceholder("MISSIONS"); break;
                    case Page.Originals: DrawPlaceholder("ORIGINALS"); break;
                    case Page.Blackjack: DrawPlaceholder("BLACKJACK"); break;
                    case Page.Challenges: DrawPlaceholder("CHALLENGES"); break;
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.TextDisabled("Aetherphone: " + bridge.Status + " | read-only bridge");
            }

            ImGui.End();
            mainOpen = open;
        }
        finally
        {
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(Theme.PushCount);
        }
    }

    private void Heading(string text) =>
        ImGui.TextColored(Theme.Get(cfg, 4), text);

    private void DrawOverview()
    {
        Heading("OVERVIEW");

        ImGui.TextUnformatted("Aetherphone: " + bridge.Status);

        if (bridge.HasCasinoState)
        {
            if (bridge.Chips.HasValue)
                ImGui.TextUnformatted($"Bankroll: {bridge.Chips.Value:N0} chips");
            else
                ImGui.TextDisabled("Bankroll: no active sitting");

            if (bridge.Level.HasValue)
                ImGui.TextUnformatted("Casino level: " + bridge.Level.Value);

            if (bridge.StakesPaused == true)
                ImGui.TextDisabled("Wagers are currently paused by Aetherphone.");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Currency: 1 coin = 1,000 chips");
        ImGui.TextUnformatted(
            $"Enabled modules: {cfg.ModulesEnabled.Count(x => x)} / 6");

        ImGui.Spacing();
        ImGui.TextDisabled("DEV2 TEST1 - live read-only bridge.");
        ImGui.TextDisabled("No automated wagers or reward claims.");
    }

    private void DrawRewards()
    {
        Heading("REWARDS");

        if (bridge.HasCasinoState)
        {
            if (bridge.DailySpinClaimed.HasValue)
            {
                ImGui.TextUnformatted(
                    "Daily spin: " +
                    (bridge.DailySpinClaimed.Value ? "claimed" : "unclaimed"));

                if (bridge.DailySpinNextAtUnix > 0)
                    ImGui.TextDisabled(
                        "Next spin: " +
                        TimeUntil(bridge.DailySpinNextAtUnix.Value));
            }
            else
            {
                ImGui.TextDisabled("Daily spin status not loaded yet.");
            }

            ImGui.Spacing();

            if (bridge.Bonuses.Count == 0)
                ImGui.TextDisabled("No bonus data in Aetherphone's cache.");

            foreach (var bonus in bridge.Bonuses)
            {
                var name = bonus.Kind.Length == 0 ? "Bonus" : bonus.Kind;
                var ready = bonus.Ready && bonus.Eligible;

                var status = ready ? "READY"
                    : bonus.NextAtUnix > 0
                        ? TimeUntil(bonus.NextAtUnix)
                        : "not ready";

                ImGui.TextWrapped(
                    $"{name}: {bonus.Amount:N0} chips - {status}");
            }

            ImGui.Spacing();
            ImGui.TextDisabled("Statuses come from Aetherphone's cached state.");
        }
        else
        {
            ImGui.TextDisabled("Open Gamba in Aetherphone to load live rewards.");
            ImGui.TextUnformatted("Timed claim: 5,000 chips per 3h.");
        }

        ImGui.Separator();
        ImGui.TextDisabled("Manual fallback reminder");

        if (cfg.LastTimedClaimUtc == DateTime.MinValue)
        {
            ImGui.TextUnformatted("No timed claim recorded.");
        }
        else
        {
            var remaining =
                cfg.LastTimedClaimUtc.AddHours(3) - DateTime.UtcNow;

            ImGui.TextUnformatted(remaining > TimeSpan.Zero
                ? $"Next check in {Math.Ceiling(remaining.TotalMinutes)} minutes"
                : "Reminder elapsed - check Aetherphone.");
        }

        if (ImGui.SmallButton("Mark claim now"))
        {
            cfg.LastTimedClaimUtc = DateTime.UtcNow;
            Save();
        }

        ImGui.SameLine();

        if (ImGui.SmallButton("Clear reminder"))
        {
            cfg.LastTimedClaimUtc = DateTime.MinValue;
            Save();
        }
    }

    private void DrawBarkeep()
    {
        Heading("BARKEEP");

        ImGui.TextUnformatted("Aetherphone: " + bridge.BarkeepStatus);

        if (bridge.BarkeepPatrons.HasValue &&
            bridge.BarkeepCompleted.HasValue)
        {
            ImGui.TextUnformatted(
                $"Patrons: {bridge.BarkeepCompleted.Value}/{bridge.BarkeepPatrons.Value}");

            if (bridge.BarkeepScore.HasValue)
                ImGui.TextUnformatted(
                    $"Current score: {bridge.BarkeepScore.Value:N0}");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Entry: 2,000 | Top payout: 4,000 chips");
        ImGui.TextUnformatted("Daily net-win cap: 20,000 chips");

        ImGui.SliderInt("Accuracy (%)", ref barkeepScore, 0, 100);

        int payout = barkeepScore >= 95 ? 4000 :
                     barkeepScore >= 80 ? 3000 :
                     barkeepScore >= 60 ? 2000 :
                     barkeepScore >= 40 ? 1000 : 0;

        int net = payout - 2000;

        ImGui.TextUnformatted(
            $"Estimated payout: {payout:N0} chips");

        ImGui.TextColored(
            net >= 0 ? Theme.Get(cfg, 5) : Theme.Get(cfg, 3),
            $"Estimated net: {net:+#,0;-#,0;0} chips");

        ImGui.Spacing();
        ImGui.TextDisabled(
            "Read-only shift tracking. No grade injection or automatic wagers.");
    }

    private void DrawPlaceholder(string name)
    {
        Heading(name);
        ImGui.TextWrapped(
            "Module foundation ready. Additional game automation is not implemented in DEV2 TEST1.");

        if (name == "ORIGINALS")
            ImGui.TextDisabled(
                "Aetherphone already includes Auto Bet for supported games.");
        else if (name == "BLACKJACK")
            ImGui.TextDisabled(
                "Strategy and hand guidance are planned.");
        else
            ImGui.TextDisabled(
                "Live state and progress tracking are planned.");
    }

    private void DrawSettings()
    {
        if (!settingsOpen)
            return;

        Theme.Push(cfg);

        try
        {
            ImGui.SetNextWindowSize(
                new Vector2(390f, 370f), ImGuiCond.FirstUseEver);

            bool open = settingsOpen;

            if (ImGui.Begin(
                "APGambaHax - Settings###APGambaHaxSettings",
                ref open, ImGuiWindowFlags.NoCollapse))
            {
                Heading("MODULES");
                ImGui.TextDisabled("Disabled modules have no active features.");

                for (int i = 0; i < cfg.ModulesEnabled.Length; i++)
                {
                    bool enabled = cfg.ModulesEnabled[i];

                    if (ImGui.Checkbox("Enable " + Pages[i + 1],
                        ref enabled))
                    {
                        cfg.ModulesEnabled[i] = enabled;

                        if (!enabled && (int)page == i + 1)
                            page = Page.Overview;

                        Save();
                    }
                }

                ImGui.Spacing();
                ImGui.Separator();
                Heading("APPEARANCE");

                ImGui.TextDisabled(cfg.UseDalamudColors
                    ? "Using matched Dalamud colors"
                    : "Using default dark palette");

                if (ImGui.Button("Match Dalamud colors") &&
                    sampledTheme.Length == 17)
                {
                    cfg.MatchedStyleColors = (uint[])sampledTheme.Clone();
                    cfg.UseDalamudColors = true;
                    Save();
                }

                ImGui.SameLine();

                if (ImGui.Button("Default dark"))
                {
                    cfg.UseDalamudColors = false;
                    Save();
                }

                ImGui.Spacing();
                ImGui.TextDisabled("APGambaHax " + VersionText + " - DEV2 TEST1");
                ImGui.TextDisabled("Bridge: " + bridge.Status);
                ImGui.TextDisabled("Aetherphone assembly: " + bridge.Version);
            }

            ImGui.End();
            settingsOpen = open;
        }
        finally
        {
            ImGui.PopStyleColor(Theme.PushCount);
        }
    }
}
