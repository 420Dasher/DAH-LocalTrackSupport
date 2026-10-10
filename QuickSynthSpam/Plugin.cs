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
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Game;
using RecipeSheet = Lumina.Excel.Sheets.Recipe;
using GearSheet = Lumina.Excel.Sheets.Item;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace QuickSynthSpam;

// v0.1.0 RC1 - RELEASE CANDIDATE
public sealed unsafe class Plugin : IDalamudPlugin
{
    [PluginService] private static IDalamudPluginInterface Pi { get; set; } = null!;
    [PluginService] private static ICommandManager Commands { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IGameGui Gui { get; set; } = null!;
    [PluginService] private static IDataManager Data { get; set; } = null!;
    [PluginService] private static IAddonLifecycle Lifecycle { get; set; } = null!;
    [PluginService] private static ICondition Conditions { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;

    private readonly Configuration config;

    private enum Phase
    {
        Idle,
        Dialog,
        Synthesis,
        Return,
        RepairExit,
        RepairOpen,
        RepairConfirm,
        RepairVerify,
        RepairClose,
        RepairResume
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
    private bool runNqOnly;
    private bool runAutoRepair;
    private int activeRepairThreshold = 50;
    private uint runRecipeId;
    private bool ownsRepairWindow;
    private bool repairCategoryRequested;
    private bool repairCloseSent;

    private uint observedRecipeId;
    private uint filledRecipeId;
    private int displayedCraftable = -1;
    private int pendingCraftable = -1;
    private DateTime pendingSince;

    private DateTime lastGearScan = DateTime.MinValue;
    private DateTime lastGearError = DateTime.MinValue;
    private bool gearSnapshotReady;
    private float lowestGearDurability = 199f;
    private int repairableGearCount;

    private string status = "Idle";

    public Plugin()
    {
        config = Pi.GetPluginConfig() as Configuration ?? new Configuration();

        Commands.AddHandler("/qspam", new CommandInfo(Command)
        {
            HelpMessage = "Open QuickSynthSpam with /qspam. Use /qspam 250 to set the crafting target."
        });

        Pi.UiBuilder.Draw += Draw;
        Pi.UiBuilder.OpenMainUi += Open;
        Pi.UiBuilder.OpenConfigUi += Open;

        Framework.Update += Update;

        Lifecycle.RegisterListener(
            AddonEvent.PostSetup, "RecipeNote", RecipeOpened);
        Lifecycle.RegisterListener(
            AddonEvent.PostOpen, "RecipeNote", RecipeOpened);

        Lifecycle.RegisterListener(
            AddonEvent.PreClose, "RecipeNote", RecipeClosed);
        Lifecycle.RegisterListener(
            AddonEvent.PreFinalize, "RecipeNote", RecipeClosed);
    }

    public void Dispose()
    {
        phase = Phase.Idle;

        Lifecycle.UnregisterListener(
            AddonEvent.PostSetup, "RecipeNote", RecipeOpened);
        Lifecycle.UnregisterListener(
            AddonEvent.PostOpen, "RecipeNote", RecipeOpened);

        Lifecycle.UnregisterListener(
            AddonEvent.PreClose, "RecipeNote", RecipeClosed);
        Lifecycle.UnregisterListener(
            AddonEvent.PreFinalize, "RecipeNote", RecipeClosed);

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

    private void RecipeClosed(AddonEvent evt, AddonArgs args)
    {
        // Do not hide the Stop control while automation is running.
        if (phase == Phase.Idle)
        {
            open = false;
            ResetRecipeTracking();
        }
    }

    private void ResetRecipeTracking()
    {
        observedRecipeId = 0;
        filledRecipeId = 0;
        displayedCraftable = -1;
        pendingCraftable = -1;
    }

    private void UpdateRecipeDefault()
    {
        if (Addon("RecipeNote") == null)
        {
            ResetRecipeTracking();
            return;
        }

        var note = RecipeNote.Instance();

        if (note == null ||
            note->RecipeList == null ||
            note->RecipeList->SelectedRecipe == null)
        {
            displayedCraftable = -1;
            return;
        }

        uint recipeId = note->RecipeList->SelectedRecipe->RecipeId;

        if (recipeId == 0)
        {
            displayedCraftable = -1;
            return;
        }

        if (recipeId != observedRecipeId)
        {
            observedRecipeId = recipeId;
            filledRecipeId = 0;
            pendingCraftable = -1;
        }

        if (!TryCraftable(out int craftable))
        {
            displayedCraftable = -1;
            return;
        }

        craftable = Math.Clamp(craftable, 0, 999999);
        displayedCraftable = craftable;

        if (!config.AutoFillMaxCraftable ||
            filledRecipeId == recipeId)
        {
            return;
        }

        // Wait briefly for the crafting log to refresh
        // its material count after selecting a recipe.
        if (pendingCraftable != craftable)
        {
            pendingCraftable = craftable;
            pendingSince = DateTime.UtcNow;
            return;
        }

        if ((DateTime.UtcNow - pendingSince).TotalMilliseconds < 350)
            return;

        config.TotalCount = craftable;
        filledRecipeId = recipeId;
        Pi.SavePluginConfig(config);
    }
    private void Command(string command, string args)
    {
        if (int.TryParse(args.Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var value))
        {
            config.TotalCount = Math.Clamp(value, 1, 999999);
            config.AutoFillMaxCraftable = false;
            Pi.SavePluginConfig(config);
        }

        open = true;
    }

    private void Draw()
    {
        if (!open)
            return;

        ImGui.SetNextWindowSize(
            new Vector2(450f, 0f),
            ImGuiCond.FirstUseEver);

        if (!ImGui.Begin(
                "QuickSynthSpam###QuickSynthSpam",
                ref open,
                ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.End();
            return;
        }

        var accent = new Vector4(0.40f, 0.88f, 0.68f, 1f);
        var blue = new Vector4(0.40f, 0.75f, 0.98f, 1f);
        var warning = new Vector4(0.98f, 0.72f, 0.36f, 1f);
        var muted = new Vector4(0.65f, 0.67f, 0.72f, 1f);

        bool running = phase != Phase.Idle;
        bool recipeOpen = Addon("RecipeNote") != null;
        bool quickSynthAvailable =
            CanQuickSynthesize(out var quickSynthReason);

        // Header
        ImGui.TextColored(accent, "QUICKSYNTH SPAM");
        ImGui.SameLine();
        ImGui.TextDisabled("v0.1.0 RC1");

        ImGui.TextDisabled(
            "Quick Synthesis batch automation  |  Release Candidate");

        ImGui.Spacing();
        ImGui.Separator();

        // Display the actual automation state rather than
        // reporting the Crafting Log as closed during synthesis.
        string connectionStatus;
        Vector4 connectionColor;

        if (phase == Phase.Synthesis)
        {
            connectionStatus = "QUICK SYNTHESIS RUNNING";
            connectionColor = blue;
        }
        else if (phase == Phase.Dialog)
        {
            connectionStatus = "STARTING QUICK SYNTHESIS";
            connectionColor = blue;
        }
        else if (phase == Phase.Return)
        {
            connectionStatus = "RETURNING TO CRAFTING LOG";
            connectionColor = blue;
        }
        else if (phase >= Phase.RepairExit &&
                 phase <= Phase.RepairResume)
        {
            connectionStatus = "SELF-REPAIR IN PROGRESS";
            connectionColor = blue;
        }
        else if (recipeOpen)
        {
            connectionStatus = "CRAFTING LOG CONNECTED";
            connectionColor = accent;
        }
        else
        {
            connectionStatus = "CRAFTING LOG CLOSED";
            connectionColor = warning;
        }

        ImGui.TextColored(connectionColor, connectionStatus);

        if (recipeOpen && !running && !quickSynthAvailable)
        {
            ImGui.TextWrapped(
                $"Quick Synthesis unavailable: {quickSynthReason}");
        }

        ImGui.Spacing();

        // Setup
        ImGui.TextColored(accent, "SETUP");
        ImGui.TextDisabled("Choose a recipe and set your crafting target.");

        if (running)
            ImGui.BeginDisabled();

        int amount = config.TotalCount;

        ImGui.SetNextItemWidth(160f);

        if (ImGui.InputInt("Total crafts", ref amount, 1, 99))
        {
            config.TotalCount = Math.Clamp(amount, 0, 999999);
            config.AutoFillMaxCraftable = false;
            Pi.SavePluginConfig(config);
        }

        if (displayedCraftable >= 0)
        {
            ImGui.TextDisabled(
                $"Craftable from inventory: {displayedCraftable:N0}");

            if (ImGui.SmallButton("Use max craftable"))
            {
                config.TotalCount = displayedCraftable;
                config.AutoFillMaxCraftable = true;
                filledRecipeId = observedRecipeId;
                Pi.SavePluginConfig(config);
            }
        }
        else
        {
            ImGui.TextDisabled("Craftable amount: unavailable");
        }

        var autoFill = config.AutoFillMaxCraftable;

        if (ImGui.Checkbox(
                "Auto-fill max for selected recipe", ref autoFill))
        {
            config.AutoFillMaxCraftable = autoFill;
            ResetRecipeTracking();
            Pi.SavePluginConfig(config);
        }

        ImGui.TextDisabled(
            config.AutoFillMaxCraftable
                ? "Automatic: follows the selected recipe."
                : "Manual: your quantity is saved until Auto-fill is enabled.");

        var nqOnly = config.CraftNqOnly;

        if (ImGui.Checkbox("Craft NQ items only", ref nqOnly))
        {
            config.CraftNqOnly = nqOnly;
            Pi.SavePluginConfig(config);
        }

        var autoOpen = config.OpenWithCraftingLog;

        if (ImGui.Checkbox("Open with Crafting Log", ref autoOpen))
        {
            config.OpenWithCraftingLog = autoOpen;
            Pi.SavePluginConfig(config);
        }

        if (running)
            ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.Separator();

        // Gear maintenance
        ImGui.TextColored(accent, "GEAR MAINTENANCE");
        ImGui.TextDisabled("Equipped gear  |  Automatic self-repair");

        if (gearSnapshotReady && repairableGearCount > 0)
        {
            ImGui.TextUnformatted(
                $"Lowest durability: {lowestGearDurability:F1}%");

            int limit = Math.Clamp(config.RepairThreshold, 5, 100);

            if (lowestGearDurability < limit)
            {
                ImGui.TextColored(
                    warning, $"Below repair threshold ({limit}%).");
            }
            else
            {
                ImGui.TextDisabled(
                    "Gear condition is above repair threshold.");
            }
        }
        else
        {
            ImGui.TextDisabled(
                gearSnapshotReady
                    ? "No repairable equipped gear detected."
                    : "Equipped gear data unavailable.");
        }

        ImGui.Spacing();

        bool wantRepair = config.AutoRepairEnabled;

        if (ImGui.Checkbox("Automatic self-repair", ref wantRepair))
        {
            config.AutoRepairEnabled = wantRepair;
            Pi.SavePluginConfig(config);
        }

        int threshold = Math.Clamp(config.RepairThreshold, 5, 100);

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Repair below");
        ImGui.SameLine(0f, 12f);

        ImGui.SetNextItemWidth(190f);

        if (ImGui.SliderInt(
                "##RepairThreshold",
                ref threshold,
                5,
                100,
                ""))
        {
            // Snap to increments of five percent.
            config.RepairThreshold = Math.Clamp(
                ((threshold + 2) / 5) * 5, 5, 100);

            Pi.SavePluginConfig(config);
        }

        ImGui.SameLine(0f, 10f);
        ImGui.TextUnformatted($"{config.RepairThreshold}%");

        ImGui.TextDisabled(
            running
                ? (runAutoRepair
                    ? "Threshold changes apply next batch."
                    : "Auto-repair off for this run.")
                : "Checks between batches; uses dark matter.");

        ImGui.Spacing();
        ImGui.Separator();

        // Batch plan
        int planned = running ? target : config.TotalCount;
        int full = planned / 99;
        int remainder = planned % 99;

        string plan = full == 0
            ? $"{remainder}"
            : remainder == 0
                ? $"{full} x 99"
                : $"{full} x 99 + {remainder}";

        int plannedBatches = (planned + 98) / 99;

        ImGui.TextColored(accent, "BATCH PLAN");
        ImGui.TextUnformatted(plan);

        ImGui.TextDisabled(
            $"{plannedBatches} batch(es) planned  |  Maximum 99 per batch");

        ImGui.Spacing();
        ImGui.Separator();

        // Run status
        ImGui.TextColored(accent, "RUN STATUS");

        var statusColor = running
            ? blue
            : status == "Finished"
                ? accent
                : status == "Idle"
                    ? muted
                    : warning;

        ImGui.TextColored(statusColor, running ? "RUNNING" : "NOT RUNNING");

        ImGui.TextWrapped(status);

        if (batchIndex > 0 && target > 0)
        {
            int progress = Math.Clamp(
                completed + batchDone, 0, target);

            ImGui.Spacing();

            ImGui.TextDisabled(
                running ? "Overall progress" : "Last run progress");

            ImGui.ProgressBar(
                (float)progress / target,
                new Vector2(400f, 21f),
                $"{progress:N0} / {target:N0}");

            if (running && batch > 0)
            {
                int currentBatchProgress = Math.Clamp(
                    batchDone, 0, batch);

                ImGui.Spacing();

                ImGui.TextDisabled(
                    $"Batch {batchIndex}  |  {currentBatchProgress} / {batch}");

                ImGui.ProgressBar(
                    (float)currentBatchProgress / batch,
                    new Vector2(400f, 11f),
                    "");
            }
        }
        else
        {
            ImGui.Spacing();
            ImGui.TextDisabled(
                "No crafting run started yet.");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Primary action
        if (!running)
        {
            if (!recipeOpen || config.TotalCount < 1 || displayedCraftable == 0 || !quickSynthAvailable)
                ImGui.BeginDisabled();

            ImGui.PushStyleColor(
                ImGuiCol.Button,
                new Vector4(0.16f, 0.48f, 0.34f, 1f));
            ImGui.PushStyleColor(
                ImGuiCol.ButtonHovered,
                new Vector4(0.20f, 0.60f, 0.42f, 1f));
            ImGui.PushStyleColor(
                ImGuiCol.ButtonActive,
                new Vector4(0.12f, 0.38f, 0.28f, 1f));

            if (ImGui.Button(
                    "Start Quick Synthesis",
                    new Vector2(400f, 34f)))
            {
                Start();
            }

            ImGui.PopStyleColor(3);

            if (!recipeOpen || config.TotalCount < 1 || displayedCraftable == 0 || !quickSynthAvailable)
                ImGui.EndDisabled();
        }
        else
        {
            ImGui.PushStyleColor(
                ImGuiCol.Button,
                new Vector4(0.58f, 0.18f, 0.18f, 1f));
            ImGui.PushStyleColor(
                ImGuiCol.ButtonHovered,
                new Vector4(0.72f, 0.22f, 0.22f, 1f));
            ImGui.PushStyleColor(
                ImGuiCol.ButtonActive,
                new Vector4(0.46f, 0.13f, 0.13f, 1f));

            if (ImGui.Button(
                    "Stop crafting",
                    new Vector2(400f, 34f)))
            {
                Stop(true, "Stopped by user");
            }

            ImGui.PopStyleColor(3);
        }

        ImGui.Spacing();
        ImGui.TextDisabled(
            "/qspam  |  Select a recipe in the Crafting Log");

        ImGui.End();
    }

    private void Start()
    {
        if (phase != Phase.Idle ||
            Addon("RecipeNote") == null ||
            config.TotalCount < 1 ||
            displayedCraftable == 0)
            return;

        if (!CanQuickSynthesize(out var reason))
        {
            status = reason;
            return;
        }

        var recipeNote = RecipeNote.Instance();

        if (recipeNote == null ||
            recipeNote->RecipeList == null ||
            recipeNote->RecipeList->SelectedRecipe == null)
            return;

        runRecipeId = recipeNote->RecipeList->SelectedRecipe->RecipeId;
        runAutoRepair = config.AutoRepairEnabled;
        target = Math.Clamp(config.TotalCount, 1, 999999);
        runNqOnly = config.CraftNqOnly;
        completed = 0;
        batch = 0;
        batchDone = 0;
        batchIndex = 0;

        try
        {
            if (BeginRepairIfNeeded())
                return;

            NextBatch();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Initial repair check failed");
            Stop(false, "Unable to check gear for repair");
        }
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

        if (!CanQuickSynthesize(out var reason))
        {
            Stop(false, reason);
            return;
        }

        batchDone = 0;
        batchIndex++;
        sawSynthesis = false;

        Callback(note, 9);

        phase = Phase.Dialog;
        entered = DateTime.UtcNow;

        status = $"Opening batch {batchIndex} ({batch})";
    }

    private void UpdateGearSnapshot()
    {
        var now = DateTime.UtcNow;

        if ((now - lastGearScan).TotalSeconds < 1)
            return;

        lastGearScan = now;
        gearSnapshotReady = false;

        var manager = InventoryManager.Instance();

        if (manager == null)
            return;

        var equipped = manager->GetInventoryContainer(
            InventoryType.EquippedItems);

        if (equipped == null || !equipped->IsLoaded)
            return;

        var sheet = Data.GetExcelSheet<GearSheet>();

        if (sheet == null)
            return;

        float lowest = 199f;
        int repairable = 0;


        for (int i = 0; i < equipped->Size; i++)
        {
            // Waist and Soul Crystal are not repairable gear.
            if (i == 5 || i == 13)
                continue;

            var item = equipped->GetInventorySlot(i);

            if (item == null || item->ItemId == 0)
                continue;

            if (!sheet.TryGetRow(item->ItemId, out var row))
                continue;

            if (row.ClassJobRepair.RowId == 0)
                continue;

            repairable++;

            // Condition uses 300 points per 1% durability.
            // Over-repair can raise displayed durability to 199%.
            float durability = Math.Clamp(
                item->Condition / 300f, 0f, 199f);

            lowest = Math.Min(lowest, durability);


        }

        lowestGearDurability = lowest;
        repairableGearCount = repairable;

        gearSnapshotReady = true;
    }
    // Called only before the first batch or after a completed
    // batch. Never interrupts a running Quick Synthesis batch.
    private bool BeginRepairIfNeeded()
    {
        if (!runAutoRepair)
            return false;

        lastGearScan = DateTime.MinValue;
        UpdateGearSnapshot();

        if (!gearSnapshotReady)
        {
            Stop(false, "Cannot read equipped gear for repair");
            return true;
        }

        int threshold = Math.Clamp(config.RepairThreshold, 5, 100);

        if (repairableGearCount == 0 || lowestGearDurability >= threshold)
            return false;

        activeRepairThreshold = threshold;

        if (Addon("SelectYesno") != null || Addon("Repair") != null)
        {
            Stop(false, "Close existing repair or confirmation windows first");
            return true;
        }

        var recipeWindow = Addon("RecipeNote");

        if (recipeWindow == null || runRecipeId == 0)
        {
            Stop(false, "Cannot preserve selected crafting recipe");
            return true;
        }

        ownsRepairWindow = false;
        repairCategoryRequested = false;
        repairCloseSent = false;

        // Exit the crafting stance before invoking general actions.
        Callback(recipeWindow, -1);

        phase = Phase.RepairExit;
        entered = DateTime.UtcNow;
        status = $"Repair due ({lowestGearDurability:F1}%): exiting crafting stance";
        return true;
    }

    private void UpdateRepairExit(double elapsed)
    {
        if (Addon("RecipeNote") != null ||
            Conditions[ConditionFlag.PreparingToCraft] ||
            Conditions[ConditionFlag.Crafting])
        {
            if (elapsed > 12)
                Stop(false, "Timed out leaving crafting stance for repair");
            return;
        }

        if (elapsed < 0.5)
            return;

        var actions = ActionManager.Instance();

        if (actions == null ||
            !actions->UseAction(ActionType.GeneralAction, 6))
        {
            Stop(false, "Could not open self-repair (general action unavailable)");
            return;
        }

        ownsRepairWindow = true;
        phase = Phase.RepairOpen;
        entered = DateTime.UtcNow;
        status = "Opening the self-repair window";
    }

    private void UpdateRepairOpen(double elapsed)
    {
        var repair = (AddonRepair*)Addon("Repair");

        if (repair == null)
        {
            if (elapsed > 8)
                Stop(false, "Self-repair window did not open");
            return;
        }

        if (elapsed < 0.5)
            return;

        if (repair->Dropdown == null)
        {
            if (elapsed > 8)
                Stop(false, "Repair category selector unavailable");
            return;
        }

        // Only repair the equipped-items category, never an
        // unrelated inventory or Armoury Chest category.
        if (repair->Dropdown->GetSelectedItemIndex() != 0)
        {
            if (!repairCategoryRequested)
            {
                repair->Dropdown->SelectItem(0);
                repairCategoryRequested = true;
                entered = DateTime.UtcNow;
            }
            else if (elapsed > 2)
            {
                Stop(false, "Could not select Equipped Items repair category");
            }
            return;
        }

        if (Addon("SelectYesno") != null)
        {
            Stop(false, "Unexpected confirmation dialog during repair");
            return;
        }

        if (repair->RepairAllButton == null ||
            !repair->RepairAllButton->IsEnabled)
        {
            if (elapsed > 6)
            {
                Stop(false,
                    "Self-repair unavailable: check dark matter and repair-job level");
            }
            return;
        }

        // The game's native Repair All callback.
        Callback((AtkUnitBase*)repair, 0);

        phase = Phase.RepairConfirm;
        entered = DateTime.UtcNow;
        status = "Waiting for repair confirmation";
    }

    private void UpdateRepairConfirm(double elapsed)
    {
        if (Addon("Repair") == null)
        {
            Stop(false, "Repair window closed unexpectedly");
            return;
        }

        var confirmation = (AddonSelectYesno*)Addon("SelectYesno");

        if (confirmation == null)
        {
            if (elapsed > 6)
                Stop(false, "Repair confirmation did not open");
            return;
        }

        if (elapsed < 0.35)
            return;

        if (confirmation->YesButton == null ||
            !confirmation->YesButton->IsEnabled)
        {
            if (elapsed > 6)
                Stop(false, "Repair confirmation unavailable");
            return;
        }

        Callback((AtkUnitBase*)confirmation, 0);

        phase = Phase.RepairVerify;
        entered = DateTime.UtcNow;
        status = "Repairing equipped gear";
    }

    private void UpdateRepairVerify(double elapsed)
    {
        if (elapsed > 15)
        {
            Stop(false, $"Repair timed out or did not restore gear above {activeRepairThreshold}%");
            return;
        }

        if (elapsed < 0.8 || Conditions[ConditionFlag.Occupied39])
            return;

        lastGearScan = DateTime.MinValue;
        UpdateGearSnapshot();

        if (!gearSnapshotReady)
        {
            Stop(false, "Unable to verify repaired equipment");
            return;
        }

        if (lowestGearDurability < activeRepairThreshold)
            return;

        phase = Phase.RepairClose;
        entered = DateTime.UtcNow;
        status = $"Repair verified ({lowestGearDurability:F1}% minimum)";
    }

    private void UpdateRepairClose(double elapsed)
    {
        if (!repairCloseSent)
        {
            var repair = Addon("Repair");

            if (repair != null)
                repair->Close(true);

            repairCloseSent = true;
            entered = DateTime.UtcNow;
            return;
        }

        if (Addon("Repair") != null)
        {
            if (elapsed > 8)
                Stop(false, "Repair window would not close");
            return;
        }

        if (elapsed < 0.5)
            return;

        if (runRecipeId == 0)
        {
            Stop(false, "Saved recipe was lost");
            return;
        }

        var agent = AgentRecipeNote.Instance();

        if (agent == null)
        {
            Stop(false, "Crafting Log agent unavailable");
            return;
        }

        agent->OpenRecipeByRecipeId(runRecipeId);

        ownsRepairWindow = false;
        phase = Phase.RepairResume;
        entered = DateTime.UtcNow;
        status = "Restoring the selected recipe after repair";
    }

    private void UpdateRepairResume(double elapsed)
    {
        var note = RecipeNote.Instance();

        if (Addon("RecipeNote") == null ||
            note == null ||
            note->RecipeList == null ||
            note->RecipeList->SelectedRecipe == null ||
            note->RecipeList->SelectedRecipe->RecipeId != runRecipeId)
        {
            if (elapsed > 10)
                Stop(false, "Unable to restore the original crafting recipe");
            return;
        }

        if (elapsed < 1.0 ||
            Conditions[ConditionFlag.ExecutingCraftingAction])
            return;

        if (!CanQuickSynthesize(out var reason))
        {
            if (elapsed > 10)
                Stop(false, "Cannot resume after repair: " + reason);
            return;
        }

        lastGearScan = DateTime.MinValue;
        UpdateGearSnapshot();

        if (!gearSnapshotReady || lowestGearDurability < activeRepairThreshold)
        {
            Stop(false, "Equipped gear still needs repair");
            return;
        }

        // Keeps the same total target and completed count.
        NextBatch();
    }
    private void Update(IFramework framework)
    {
        if (open)
        {
            try
            {
                UpdateGearSnapshot();
            }
            catch (Exception ex)
            {
                gearSnapshotReady = false;

                if ((DateTime.UtcNow - lastGearError).TotalSeconds >= 30)
                {
                    lastGearError = DateTime.UtcNow;
                    Log.Error(ex, "Gear durability monitoring failed");
                }
            }
        }

        if (phase == Phase.Idle)
        {
            try
            {
                UpdateRecipeDefault();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Recipe default update failed");
            }

            return;
        }

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
                case Phase.RepairExit:
                    UpdateRepairExit(elapsed);
                    break;
                case Phase.RepairOpen:
                    UpdateRepairOpen(elapsed);
                    break;
                case Phase.RepairConfirm:
                    UpdateRepairConfirm(elapsed);
                    break;
                case Phase.RepairVerify:
                    UpdateRepairVerify(elapsed);
                    break;
                case Phase.RepairClose:
                    UpdateRepairClose(elapsed);
                    break;
                case Phase.RepairResume:
                    UpdateRepairResume(elapsed);
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
                Byte = runNqOnly ? (byte)1 : (byte)0
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

        if (completed >= target)
        {
            NextBatch();
            return;
        }

        if (!CanQuickSynthesize(out var reason))
        {
            if (elapsed > 10)
                Stop(false, reason);

            return;
        }

        if (BeginRepairIfNeeded())
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

        // Close only the Repair window opened by this plugin.
        // Never auto-confirm a dialog during cancellation.
        if (ownsRepairWindow)
        {
            var repair = Addon("Repair");
            if (repair != null && Addon("SelectYesno") == null)
                repair->Close(true);
        }

        ownsRepairWindow = false;
        runAutoRepair = false;
        runRecipeId = 0;
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

    private static bool CanQuickSynthesize(out string reason)
    {
        reason = "";

        var addon = (AddonRecipeNote*)Addon("RecipeNote");

        if (addon == null)
        {
            reason = "Crafting Log is closed";
            return false;
        }

        var recipeNote = RecipeNote.Instance();

        if (recipeNote == null ||
            recipeNote->RecipeList == null ||
            recipeNote->RecipeList->SelectedRecipe == null)
        {
            reason = "Select a crafting recipe";
            return false;
        }

        uint recipeId =
            recipeNote->RecipeList->SelectedRecipe->RecipeId;

        if (recipeId == 0)
        {
            reason = "No recipe selected";
            return false;
        }

        var recipe = Data.GetExcelSheet<RecipeSheet>()
            .GetRowOrDefault(recipeId);

        if (recipe == null)
        {
            reason = "Selected recipe data is unavailable";
            return false;
        }

        if (!recipe.Value.CanQuickSynth)
        {
            reason = "This recipe does not support Quick Synthesis";
            return false;
        }

        // Standard recipes need to be crafted successfully once.
        // Master-book recipes are handled separately by the game.
        if (recipe.Value.SecretRecipeBook.RowId == 0 &&
            !QuestManager.IsRecipeComplete(recipeId))
        {
            reason = "Craft this recipe once to unlock Quick Synthesis";
            return false;
        }

        var button = addon->QuickSynthesisButton;

        if (button == null ||
            button->AtkComponentBase.OwnerNode == null ||
            !button->IsEnabled)
        {
            reason = "Quick Synthesis is currently unavailable";
            return false;
        }

        return true;
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