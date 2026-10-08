using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace QuickSynthSpam;

// v0.0.1 DEV1 - EARLY PROTOTYPE
public sealed unsafe class Plugin : IDalamudPlugin
{
    [PluginService] private static IDalamudPluginInterface Pi { get; set; } = null!;
    [PluginService] private static ICommandManager Commands { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IGameGui Gui { get; set; } = null!;
    [PluginService] private static IAddonLifecycle Lifecycle { get; set; } = null!;
    [PluginService] private static ICondition Conditions { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;

    private readonly Configuration config;

    private enum Phase
    {
        Idle,
        Dialog,
        Synthesis,
        Return
    }

    private Phase phase;
    private DateTime entered = DateTime.UtcNow;

    private bool open;
    private bool sawSynthesis;
    private int target;
    private int completed;
    private int batch;
    private int batchDone;
    private int batchIndex;

    private string status = "Idle";

    public Plugin()
    {
        config = Pi.GetPluginConfig() as Configuration ?? new Configuration();

        Commands.AddHandler("/qspam", new CommandInfo(Command)
        {
            HelpMessage = "QuickSynth Spam prototype. /qspam 250 sets the total."
        });

        Pi.UiBuilder.Draw += Draw;
        Pi.UiBuilder.OpenMainUi += Open;
        Pi.UiBuilder.OpenConfigUi += Open;

        Framework.Update += Update;

        Lifecycle.RegisterListener(
            AddonEvent.PostSetup, "RecipeNote", RecipeOpened);
        Lifecycle.RegisterListener(
            AddonEvent.PostOpen, "RecipeNote", RecipeOpened);
    }

    public void Dispose()
    {
        phase = Phase.Idle;

        Lifecycle.UnregisterListener(
            AddonEvent.PostSetup, "RecipeNote", RecipeOpened);
        Lifecycle.UnregisterListener(
            AddonEvent.PostOpen, "RecipeNote", RecipeOpened);

        Framework.Update -= Update;
        Pi.UiBuilder.Draw -= Draw;
        Pi.UiBuilder.OpenMainUi -= Open;
        Pi.UiBuilder.OpenConfigUi -= Open;

        Commands.RemoveHandler("/qspam");
    }

    private void Open() => open = true;

    private void RecipeOpened(AddonEvent evt, AddonArgs args)
    {
        if (config.OpenWithCraftingLog)
            open = true;
    }

    private void Command(string command, string args)
    {
        if (int.TryParse(args.Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var value))
        {
            config.TotalCount = Math.Clamp(value, 1, 999999);
            Pi.SavePluginConfig(config);
        }

        open = true;
    }

    private void Draw()
    {
        if (!open)
            return;

        ImGui.SetNextWindowSize(
            new Vector2(410, 0),
            ImGuiCond.FirstUseEver);

        if (!ImGui.Begin(
                "QuickSynth Spam - Early Prototype###QuickSynthSpam",
                ref open,
                ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.End();
            return;
        }

        ImGui.TextDisabled("EARLY PROTOTYPE  |  v0.0.1 DEV1");
        ImGui.Separator();

        var amount = config.TotalCount;

        if (ImGui.InputInt("Total crafts", ref amount, 1, 99))
        {
            config.TotalCount = Math.Clamp(amount, 1, 999999);
            Pi.SavePluginConfig(config);
        }

        var autoOpen = config.OpenWithCraftingLog;

        if (ImGui.Checkbox("Open with Crafting Log", ref autoOpen))
        {
            config.OpenWithCraftingLog = autoOpen;
            Pi.SavePluginConfig(config);
        }

        int full = config.TotalCount / 99;
        int remainder = config.TotalCount % 99;

        string plan = full == 0
            ? $"Plan: {remainder}"
            : remainder == 0
                ? $"Plan: {full} x 99"
                : $"Plan: {full} x 99 + {remainder}";

        ImGui.TextUnformatted(plan);
        ImGui.Separator();

        ImGui.TextUnformatted(
            $"Crafting Log: {(Addon("RecipeNote") != null ? "Open" : "Closed")}");

        ImGui.TextWrapped($"Status: {status}");

        if (phase != Phase.Idle)
        {
            ImGui.TextUnformatted(
                $"Progress: {Math.Min(target, completed + batchDone)} / {target}");
            ImGui.TextUnformatted(
                $"Batch {batchIndex}: {batchDone} / {batch}");
        }

        ImGui.Spacing();

        if (phase == Phase.Idle)
        {
            bool available = Addon("RecipeNote") != null;

            if (!available)
                ImGui.BeginDisabled();

            if (ImGui.Button(
                    "Start selected recipe", new Vector2(190, 0)))
                Start();

            if (!available)
                ImGui.EndDisabled();
        }
        else
        {
            if (ImGui.Button("Stop", new Vector2(190, 0)))
                Stop(true, "Stopped by user");
        }

        ImGui.SameLine();
        ImGui.TextDisabled("/qspam");

        ImGui.End();
    }

    private void Start()
    {
        if (phase != Phase.Idle || Addon("RecipeNote") == null)
            return;

        target = Math.Clamp(config.TotalCount, 1, 999999);
        completed = 0;
        batch = 0;
        batchDone = 0;
        batchIndex = 0;

        NextBatch();
    }

    private void NextBatch()
    {
        if (completed >= target)
        {
            Stop(false, "Finished");
            return;
        }

        var note = Addon("RecipeNote");

        if (note == null)
        {
            Stop(false, "Crafting Log closed");
            return;
        }

        batch = Math.Min(99, target - completed);

        if (TryCraftable(out int available))
        {
            if (available < 1)
            {
                Stop(false, "No materials remaining");
                return;
            }

            batch = Math.Min(batch, available);
        }

        batchDone = 0;
        batchIndex++;
        sawSynthesis = false;

        Callback(note, 9);

        phase = Phase.Dialog;
        entered = DateTime.UtcNow;

        status = $"Opening batch {batchIndex} ({batch})";
    }

    private void Update(IFramework framework)
    {
        if (phase == Phase.Idle)
            return;

        try
        {
            double elapsed = (DateTime.UtcNow - entered).TotalSeconds;

            switch (phase)
            {
                case Phase.Dialog:
                    UpdateDialog(elapsed);
                    break;

                case Phase.Synthesis:
                    UpdateSynthesis(elapsed);
                    break;

                case Phase.Return:
                    UpdateReturn(elapsed);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "QuickSynth Spam automation error");
            Stop(false, "Error: " + ex.Message);
        }
    }

    private void UpdateDialog(double elapsed)
    {
        var dialog = Addon("SynthesisSimpleDialog");

        if (dialog != null)
        {
            var values = stackalloc AtkValue[3];

            values[0] = new AtkValue
            {
                Type = AtkValueType.Int,
                Int = batch
            };

            values[1] = new AtkValue
            {
                Type = AtkValueType.Bool,
                Byte = 1
            };

            values[2] = new AtkValue
            {
                Type = AtkValueType.Bool,
                Byte = 0
            };

            dialog->FireCallback(3, values, true);

            phase = Phase.Synthesis;
            entered = DateTime.UtcNow;
            status = $"Running batch {batchIndex}";
        }
        else if (elapsed > 5)
        {
            Stop(false, "Quick Synth dialog did not open");
        }
    }

    private void UpdateSynthesis(double elapsed)
    {
        var synth = Addon("SynthesisSimple");

        if (synth == null)
        {
            if (sawSynthesis)
            {
                completed += batchDone;
                Stop(false, "Synthesis interrupted");
            }
            else if (elapsed > 8)
            {
                Stop(false, "Synthesis did not start");
            }

            return;
        }

        sawSynthesis = true;

        if (synth->AtkValues == null ||
            synth->AtkValuesCount < 5)
            return;

        int current = Math.Max(0, synth->AtkValues[3].Int);
        int maximum = Math.Max(0, synth->AtkValues[4].Int);

        batchDone = maximum > 0
            ? Math.Min(current, maximum)
            : current;

        status = $"Quick Synth {Math.Min(target, completed + batchDone)}/{target}";

        if (maximum < 1 || current < maximum)
            return;

        if (maximum != batch)
        {
            completed += Math.Min(batch, batchDone);
            Stop(false, "Unexpected batch count - stopped");
            return;
        }

        completed += batch;
        batchDone = 0;

        Callback(synth, -1);

        phase = Phase.Return;
        entered = DateTime.UtcNow;
        status = "Returning to Crafting Log";
    }

    private void UpdateReturn(double elapsed)
    {
        if (Addon("SynthesisSimple") != null)
        {
            if (elapsed > 12)
                Stop(false, "Quick Synth window would not close");

            return;
        }

        if (Addon("RecipeNote") == null)
        {
            if (elapsed > 10)
                Stop(false, "Crafting Log did not return");

            return;
        }

        if (Conditions[ConditionFlag.ExecutingCraftingAction])
            return;

        if (!Conditions[ConditionFlag.PreparingToCraft] &&
            elapsed < 1.5)
            return;

        NextBatch();
    }

    private void Stop(bool cancel, string reason)
    {
        if (cancel)
        {
            var synth = Addon("SynthesisSimple");
            var dialog = Addon("SynthesisSimpleDialog");

            if (phase == Phase.Synthesis && synth != null)
            {
                if (synth->AtkValues != null &&
                    synth->AtkValuesCount >= 5)
                {
                    completed += Math.Min(
                        batch,
                        Math.Max(0, synth->AtkValues[3].Int));
                }

                Callback(synth, -1);
            }
            else if (phase == Phase.Dialog && dialog != null)
            {
                dialog->Close(true);
            }
        }

        phase = Phase.Idle;
        batch = 0;
        batchDone = 0;
        status = reason;
    }

    private static AtkUnitBase* Addon(string name)
    {
        var addon = Gui.GetAddonByName(name);

        if (addon == null || addon.Address == nint.Zero)
            return null;

        var pointer = (AtkUnitBase*)addon.Address;

        return pointer != null && pointer->IsVisible
            ? pointer
            : null;
    }

    private static bool TryCraftable(out int available)
    {
        available = 0;

        var addon = Gui.GetAddonByName("RecipeNote");

        if (addon == null || addon.Address == nint.Zero)
            return false;

        var note = (AddonRecipeNote*)addon.Address;

        var node =
            note->SelectedRecipeQuantityCraftableFromMaterialsInInventory;

        return note->AtkUnitBase.IsVisible
            && node != null
            && int.TryParse(
                node->NodeText.ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out available);
    }

    private static void Callback(AtkUnitBase* addon, int value)
    {
        var v = new AtkValue
        {
            Type = AtkValueType.Int,
            Int = value
        };

        addon->FireCallback(1, &v, true);
    }
}