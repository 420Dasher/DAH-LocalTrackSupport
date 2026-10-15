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

    private readonly Configuration cfg;
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

        Pi.UiBuilder.Draw += Draw;
        Pi.UiBuilder.OpenMainUi += Open;
        Pi.UiBuilder.OpenConfigUi += OpenSettings;
    }

    private void Save() => Pi.SavePluginConfig(cfg);

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
                ImGui.TextDisabled("Local companion - no live Aetherphone connection");
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
        ImGui.TextWrapped(
            "APGambaHax DEV1: modular companion tools for Aetherphone's Gamba casino.");

        ImGui.Spacing();
        ImGui.TextUnformatted("Currency: 1 coin = 1,000 chips");
        ImGui.TextUnformatted("Enabled modules: " +
            cfg.ModulesEnabled.Count(x => x) + " / 6");

        ImGui.Spacing();
        ImGui.TextDisabled(
            "Live bankroll, claims and casino controls require a verified integration.");
    }

    private void DrawRewards()
    {
        Heading("REWARDS");
        ImGui.TextUnformatted("Daily spin: pays wallet coins, not chips.");
        ImGui.TextUnformatted("Timed chips: 5,000 chips per eligible claim.");
        ImGui.TextUnformatted("Check daily streak, welcome and other bonuses.");

        ImGui.Spacing();
        ImGui.TextDisabled("Manual 3-hour reminder (not server status)");

        if (cfg.LastTimedClaimUtc == DateTime.MinValue)
        {
            ImGui.TextUnformatted("No timed claim recorded.");
        }
        else
        {
            TimeSpan remaining =
                cfg.LastTimedClaimUtc.AddHours(3) - DateTime.UtcNow;

            ImGui.TextUnformatted(remaining > TimeSpan.Zero
                ? $"Next check in {Math.Ceiling(remaining.TotalMinutes)} minutes"
                : "Reminder elapsed - check Aetherphone.");
        }

        if (ImGui.Button("Mark timed claim now"))
        {
            cfg.LastTimedClaimUtc = DateTime.UtcNow;
            Save();
        }

        ImGui.SameLine();

        if (ImGui.Button("Clear reminder"))
        {
            cfg.LastTimedClaimUtc = DateTime.MinValue;
            Save();
        }
    }

    private void DrawBarkeep()
    {
        Heading("BARKEEP");
        ImGui.TextUnformatted("Entry cost: 2,000 chips");
        ImGui.TextUnformatted("Maximum payout: 4,000 chips");
        ImGui.TextUnformatted("Daily net-win cap: 20,000 chips");

        ImGui.Spacing();

        ImGui.SliderInt("Accuracy (%)", ref barkeepScore, 0, 100);

        int payout = barkeepScore >= 95 ? 4000 :
                     barkeepScore >= 80 ? 3000 :
                     barkeepScore >= 60 ? 2000 :
                     barkeepScore >= 40 ? 1000 : 0;

        int net = payout - 2000;

        ImGui.TextUnformatted($"Estimated payout: {payout:N0} chips");
        ImGui.TextColored(
            net >= 0 ? Theme.Get(cfg, 5) : Theme.Get(cfg, 3),
            $"Estimated net: {net:+#,0;-#,0;0} chips");

        ImGui.Spacing();
        ImGui.TextWrapped(
            "Local estimate only. Minimum finish time is 60 seconds. Actual results and limits are enforced by Aetherphone's server.");
    }

    private void DrawPlaceholder(string name)
    {
        Heading(name);
        ImGui.TextWrapped(
            "Module foundation ready. Live controls and data integration are not implemented in DEV1.");

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
                ImGui.TextDisabled("APGambaHax " + VersionText + " - DEV1");
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
