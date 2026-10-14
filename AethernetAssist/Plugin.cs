using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Memory;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using AetheryteSheet = Lumina.Excel.Sheets.Aetheryte;
using TerritorySheet = Lumina.Excel.Sheets.TerritoryType;
using NativeQuestManager = FFXIVClientStructs.FFXIV.Client.Game.QuestManager;

namespace AethernetAssist;

public sealed unsafe class Plugin : IDalamudPlugin
{
    [PluginService]
    private static IDalamudPluginInterface Pi { get; set; } = null!;

    [PluginService]
    private static ICommandManager Commands { get; set; } = null!;

    [PluginService]
    private static IFramework Framework { get; set; } = null!;

    [PluginService]
    private static IClientState Client { get; set; } = null!;

    [PluginService]
    private static IObjectTable Objects { get; set; } = null!;

    [PluginService]
    private static IDataManager Data { get; set; } = null!;

    [PluginService]
    private static IGameGui Gui { get; set; } = null!;

    [PluginService]
    private static ICondition Conditions { get; set; } = null!;

    [PluginService]
    private static IPluginLog Log { get; set; } = null!;

    private sealed record Shard(
        uint Id,
        uint Territory,
        uint Group,
        string Name,
        bool IsMain,
        bool Invisible,
        uint RequiredQuest
    );

    private readonly Configuration config;

    private readonly Dictionary<uint, List<Shard>> networks = new();
    private readonly Dictionary<uint, HashSet<uint>> territories = new();

    private static readonly uint[] GridaniaOrder =
    {
        2, 25, 26, 27, 28, 29, 30, 31, 32, 54, 94
    };

    private static readonly uint[] LimsaOrder =
    {
        8, 43, 44, 49, 41, 42, 48, 45, 46, 93
    };

    private static readonly uint[] UldahOrder =
    {
        9, 33, 34, 35, 36, 47, 50, 125,
        37, 51, 38, 39, 40, 95
    };

    private static readonly uint[] EulmoreOrder =
    {
        134, 135, 158, 159, 157, 160
    };

    private static readonly uint[] RadzOrder =
    {
        183, 191, 192, 193, 194, 195,
        196, 198, 199, 197
    };

    private enum TravelPhase
    {
        Idle,
        Interact,
        SelectAethernet,
        SelectDestination,
        WaitForArrival
    }

    private TravelPhase phase = TravelPhase.Idle;

    private Shard? destination;
    private nint originAddress;
    private uint originTerritory;
    private Vector3 originPosition;

    private DateTime phaseStarted;
    private DateTime lastInteraction = DateTime.MinValue;
    private DateTime lastScan = DateTime.MinValue;

    private int callbackAttempts;
    private bool sawLoading;

    private IGameObject? nearby;
    private nint nearbyAddress;
    private uint activeGroup;
    private uint activeTerritory;

    private bool windowOpen;
    private string status = "Approach an Aethernet shard.";
    private string lastMenuReadout = "No destination inspected yet.";

    public Plugin()
    {
        config = Pi.GetPluginConfig() as Configuration
            ?? new Configuration();

        // Migrate older settings to the new proximity default.
        if (config.Version < 2)
        {
            config.Version = 2;
            config.OpenRange = 11.1f;
            Save();
        }

        config.OpenRange = Math.Clamp(
            config.OpenRange, 8f, 11.1f);

        LoadNetworks();

        Commands.AddHandler("/anet", new CommandInfo(OnCommand)
        {
            HelpMessage =
                "Open AethernetAssist. /anet debug toggles diagnostics. " +
                "/anet cancel stops automation."
        });

        Framework.Update += Update;

        Pi.UiBuilder.Draw += Draw;
        Pi.UiBuilder.OpenMainUi += Open;
        Pi.UiBuilder.OpenConfigUi += Open;

        Log.Information("AethernetAssist v0.0.7 DEV2 FIX1 initialized.");
    }

    public void Dispose()
    {
        Framework.Update -= Update;

        Pi.UiBuilder.Draw -= Draw;
        Pi.UiBuilder.OpenMainUi -= Open;
        Pi.UiBuilder.OpenConfigUi -= Open;

        Commands.RemoveHandler("/anet");

        phase = TravelPhase.Idle;
    }

    private void LoadNetworks()
    {
        foreach (var row in Data.GetExcelSheet<AetheryteSheet>())
        {
            if (row.AethernetGroup == 0)
                continue;

            var name = row.AethernetName.Value.Name
                .ToString()
                .Trim();

            if (string.IsNullOrWhiteSpace(name))
                continue;

            var shard = new Shard(
                row.RowId,
                row.Territory.Value.RowId,
                (uint)row.AethernetGroup,
                name,
                row.IsAetheryte,
                row.Invisible,
                row.RequiredQuest.RowId
            );

            if (!networks.TryGetValue(shard.Group, out var entries))
            {
                entries = new List<Shard>();
                networks[shard.Group] = entries;
            }

            entries.Add(shard);

            if (!shard.Invisible)
            {
                if (!territories.TryGetValue(shard.Territory, out var groups))
                {
                    groups = new HashSet<uint>();
                    territories[shard.Territory] = groups;
                }

                groups.Add(shard.Group);
            }
        }

        foreach (var entries in networks.Values)
        {
            var root = entries.FirstOrDefault(x => x.IsMain);

            uint[] order = root?.Id switch
            {
                2 => GridaniaOrder,
                8 => LimsaOrder,
                9 => UldahOrder,
                134 => EulmoreOrder,
                183 => RadzOrder,
                _ => Array.Empty<uint>()
            };

            entries.Sort((a, b) =>
                SortKey(a, order).CompareTo(SortKey(b, order)));
        }

        Log.Information(
            $"Indexed {networks.Count} Aethernet networks, " +
            $"{networks.Values.Sum(x => x.Count)} destinations.");
    }

    private static int SortKey(Shard shard, uint[] order)
    {
        var index = Array.IndexOf(order, shard.Id);

        if (index >= 0)
            return index;

        return shard.IsMain
            ? 0
            : 1000 + (int)shard.Id;
    }

    private static bool IsBusy() =>
        Conditions[ConditionFlag.BetweenAreas] ||
        Conditions[ConditionFlag.BetweenAreas51] ||
        Conditions[ConditionFlag.WaitingToVisitOtherWorld] ||
        Conditions[ConditionFlag.ReadyingVisitOtherWorld];

    private static bool IsUnlocked(uint id)
    {
        var state = UIState.Instance();

        return state != null &&
               state->IsAetheryteUnlocked(id);
    }

    private bool DestinationAvailable(Shard shard)
    {
        if (!shard.Invisible)
            return IsUnlocked(shard.Id);

        if (!networks.TryGetValue(shard.Group, out var entries))
            return false;

        // Standard additional destinations require the local
        // physical Aethernet network to be fully attuned.
        foreach (var entry in entries)
        {
            if (!entry.Invisible && !IsUnlocked(entry.Id))
                return false;
        }

        // Some exits additionally require quest completion.
        if (shard.RequiredQuest != 0 &&
            !NativeQuestManager.IsQuestComplete(shard.RequiredQuest))
            return false;

        return true;
    }

    private string GetAreaName(uint territory)
    {
        try
        {
            var name = Data.GetExcelSheet<TerritorySheet>()
                .GetRow(territory).PlaceName.Value.Name.ToString();

            return string.IsNullOrWhiteSpace(name)
                ? $"Area {territory}"
                : name;
        }
        catch
        {
            return $"Area {territory}";
        }
    }

    private void DrawDestination(Shard shard)
    {
        bool available = DestinationAvailable(shard);

        if (!available && !config.ShowUnlearned)
            return;

        if (!available)
            ImGui.BeginDisabled();

        string label = available
            ? shard.Name
            : $"{shard.Name} (Locked)";

        if (ImGui.Button(
                $"{label}##{shard.Id}",
                new Vector2(-1f, 29f)) && available)
            StartTravel(shard);

        if (!available)
            ImGui.EndDisabled();
    }


    private static bool IsShardObject(IGameObject obj)
    {
        if (obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Aetheryte)
            return true;

        return obj.BaseId is 2000151 or 2014665;
    }

    private void Save() => Pi.SavePluginConfig(config);

    private void Open()
    {
        windowOpen = true;
    }

    private void OnCommand(string command, string arguments)
    {
        switch (arguments.Trim().ToLowerInvariant())
        {
            case "debug":
                config.ShowDebug = !config.ShowDebug;
                Save();
                windowOpen = true;
                break;

            case "cancel":
                Cancel("Cancelled by user.");
                break;

            default:
                windowOpen = true;
                break;
        }
    }

    private void Update(IFramework framework)
    {
        try
        {
            if (phase != TravelPhase.Idle)
            {
                TickTravel();
                return;
            }

            if ((DateTime.UtcNow - lastScan).TotalMilliseconds < 200)
                return;

            lastScan = DateTime.UtcNow;
            ScanNearby();
        }
        catch (Exception ex)
        {
            Log.Error($"AethernetAssist: {ex}");
            Cancel("Stopped after an internal error.");
        }
    }

    private void ScanNearby()
    {
        var player = Objects.LocalPlayer;

        if (player == null || IsBusy() ||
            !territories.TryGetValue(Client.TerritoryType, out var groups))
        {
            ClearNearby();
            return;
        }

        IGameObject? nearest = null;

        var maxRange = config.OpenRange +
                       (nearby != null ? 0.8f : 0f);

        var closest = maxRange;

        foreach (var obj in Objects)
        {
            if (obj == null ||
                !obj.IsTargetable ||
                !IsShardObject(obj))
                continue;

            var distance = Vector3.Distance(
                player.Position,
                obj.Position
            );

            if (distance >= closest)
                continue;

            nearest = obj;
            closest = distance;
        }

        if (nearest == null)
        {
            ClearNearby();
            return;
        }

        uint resolvedGroup = 0;

        if (groups.Count == 1)
        {
            resolvedGroup = groups.First();
        }
        else
        {
            // Never silently select the wrong network.
            var matching = groups.Where(group =>
                networks[group].Any(shard =>
                    string.Equals(
                        shard.Name,
                        nearest.Name.ToString(),
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            ).ToArray();

            if (matching.Length == 1)
                resolvedGroup = matching[0];
        }

        if (resolvedGroup == 0)
        {
            ClearNearby();
            status = "Could not identify the local network.";
            return;
        }

        bool changed =
            nearbyAddress != nearest.Address ||
            activeGroup != resolvedGroup ||
            activeTerritory != Client.TerritoryType;

        nearby = nearest;
        nearbyAddress = nearest.Address;
        activeGroup = resolvedGroup;
        activeTerritory = Client.TerritoryType;

        if (changed) status = "Ready";

        if (changed && config.AutoOpen)
            windowOpen = true;
    }

    private void ClearNearby()
    {
        if (nearby != null)
            windowOpen = false;

        nearby = null;
        nearbyAddress = 0;
        activeGroup = 0;
        activeTerritory = 0;
    }

    private void StartTravel(Shard chosen)
    {
        if (phase != TravelPhase.Idle ||
            nearby == null ||
            Objects.LocalPlayer == null ||
            IsBusy() ||
            !DestinationAvailable(chosen))
            return;

        if (!nearby.IsTargetable ||
            Vector3.Distance(
                Objects.LocalPlayer.Position,
                nearby.Position
            ) > config.OpenRange + 1f)
            return;

        destination = chosen;
        originAddress = nearby.Address;
        originTerritory = Client.TerritoryType;
        originPosition = Objects.LocalPlayer.Position;

        callbackAttempts = 0;
        sawLoading = false;

        lastInteraction = DateTime.MinValue;

        lastMenuReadout = $"Requested destination: {chosen.Name}";
        status = $"Opening Aethernet for {chosen.Name}...";

        GoTo(TravelPhase.Interact);

        windowOpen = false;
    }

    private void GoTo(TravelPhase next)
    {
        phase = next;
        phaseStarted = DateTime.UtcNow;
    }

    private void TickTravel()
    {
        if (destination == null)
        {
            Cancel("No destination selected.");
            return;
        }

        if (phase == TravelPhase.WaitForArrival)
        {
            var player = Objects.LocalPlayer;
            bool busy = IsBusy();
            bool menuOpen = GetAddon("TelepotTown") != null;
            bool territoryChanged =
                Client.TerritoryType != originTerritory;

            float moved = player == null
                ? 0f
                : Vector3.Distance(player.Position, originPosition);

            if (busy || territoryChanged)
                sawLoading = true;

            // Confirm that the game has actually accepted the
            // teleport instead of assuming the callback succeeded.
            if (player != null && !busy && !menuOpen &&
                (sawLoading || territoryChanged || moved > 12f))
            {
                status = $"Arrived: {destination.Name}";
                phase = TravelPhase.Idle;
                destination = null;
                ClearNearby();
                return;
            }

            double elapsed =
                (DateTime.UtcNow - phaseStarted).TotalSeconds;

            // A visible native menu after two callbacks means
            // that destination selection was not acknowledged.
            if (menuOpen && !busy && !territoryChanged &&
                elapsed > 5)
            {
                lastMenuReadout +=
                    $" | Unacknowledged: menu open, callbacks={callbackAttempts}," +
                    $" territory={Client.TerritoryType}, moved={moved:0.0}y";

                Cancel("Destination not accepted: native Aethernet menu stayed open.");
                return;
            }

            if (!menuOpen && !sawLoading && moved <= 12f &&
                elapsed > 9)
            {
                Cancel("Native menu closed, but no teleport or movement was detected.");
                return;
            }

            if (elapsed > 35)
                Cancel("Teleport arrival could not be confirmed.");

            return;
        }

        if (callbackAttempts > 0 &&
            (IsBusy() || GetAddon("TelepotTown") == null))
        {
            GoTo(TravelPhase.WaitForArrival);
            return;
        }

        if (Objects.LocalPlayer == null || IsBusy())
        {
            Cancel("Travel unavailable in the current game state.");
            return;
        }

        if ((DateTime.UtcNow - phaseStarted).TotalSeconds > 10)
        {
            Cancel($"Timed out during {phase}.");
            return;
        }

        if (phase == TravelPhase.Interact)
        {
            if (GetAddon("TelepotTown") != null)
            {
                GoTo(TravelPhase.SelectDestination);
                return;
            }

            if (GetAddon("SelectString") != null)
            {
                GoTo(TravelPhase.SelectAethernet);
                return;
            }

            if ((DateTime.UtcNow - lastInteraction).TotalMilliseconds < 900)
                return;

            // Re-resolve the object instead of trusting a stale pointer.
            var obj = Objects.FirstOrDefault(x =>
                x != null && x.Address == originAddress);

            if (obj == null ||
                !obj.IsTargetable ||
                Objects.LocalPlayer == null ||
                Vector3.Distance(
                    Objects.LocalPlayer.Position,
                    obj.Position
                ) > config.OpenRange + 1f)
            {
                Cancel("Moved out of Aethernet range.");
                return;
            }

            var target = TargetSystem.Instance();

            if (target == null)
            {
                Cancel("Target system unavailable.");
                return;
            }

            target->InteractWithObject(
                (GameObject*)obj.Address,
                false
            );

            lastInteraction = DateTime.UtcNow;
            return;
        }

        if (phase == TravelPhase.SelectAethernet)
        {
            if (GetAddon("TelepotTown") != null)
            {
                GoTo(TravelPhase.SelectDestination);
                return;
            }

            var addon = (AddonSelectString*)GetAddon("SelectString");

            if (addon == null)
                return;

            var menu = addon->PopupMenu.PopupMenu;

            if (menu.EntryNames == null)
                return;

            var count = Math.Clamp(menu.EntryCount, 0, 32);

            for (int i = 0; i < count; i++)
            {
                var namePtr = menu.EntryNames[i].Value;

                if (namePtr == null)
                    continue;

                var name = Marshal.PtrToStringUTF8(
                    (nint)namePtr) ?? "";

                // DEV1 recognizes the English client menu.
                if (!name.Contains(
                    "Aethernet",
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = new AtkValue
                {
                    Type = AtkValueType.Int,
                    Int = i
                };

                addon->AtkUnitBase.FireCallback(
                    1,
                    &value,
                    true
                );

                status = "Opening Aethernet destination list...";
                GoTo(TravelPhase.SelectDestination);
                return;
            }

            return;
        }

        if (phase == TravelPhase.SelectDestination)
        {
            var addon = GetAddon("TelepotTown");

            if (addon == null)
            {
                lastMenuReadout = "Waiting for TelepotTown addon.";
                return;
            }

            if (addon->AtkValues == null)
            {
                lastMenuReadout = "TelepotTown has no AtkValues.";
                return;
            }

            var values = addon->AtkValues;
            int valueCount = addon->AtkValuesCount;

            if (valueCount <= 262)
            {
                lastMenuReadout =
                    $"TelepotTown AtkValuesCount={valueCount}; expected at least 263.";
                return;
            }

            // Read populated native slots directly. Do not trust
            // AtkValue[0] as the number of destination-name rows.
            int slots = Math.Min(20, valueCount - 262);
            var observed = new List<string>();

            for (int i = 0; i < slots; i++)
            {
                var nameValue = values[262 + i];

                if ((int)nameValue.Type == 0)
                    break;

                if (nameValue.Type != AtkValueType.String &&
                    nameValue.Type != AtkValueType.String8 &&
                    nameValue.Type != AtkValueType.WideString &&
                    nameValue.Type != AtkValueType.ManagedString)
                {
                    observed.Add($"{i}: invalid name type {nameValue.Type}");
                    continue;
                }

                var namePtr = nameValue.String.Value;

                if (namePtr == null)
                {
                    observed.Add($"{i}: null name pointer");
                    continue;
                }

                string name;

                try
                {
                    name = MemoryHelper.ReadSeStringNullTerminated(
                        (nint)namePtr).TextValue;
                }
                catch (Exception ex)
                {
                    observed.Add($"{i}: decode error {ex.Message}");
                    continue;
                }

                name = Clean(name);
                observed.Add($"{i}: {name}");

                if (!string.Equals(
                    name,
                    Clean(destination.Name),
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!DestinationAvailable(destination))
                {
                    Cancel("Destination is not attuned.");
                    return;
                }

                int callbackIndex = 6 + i * 4 + 3;

                if (callbackIndex >= valueCount)
                {
                    Cancel($"Callback data missing for row {i}.");
                    return;
                }

                var callbackValue = values[callbackIndex];

                if (callbackValue.Type != AtkValueType.UInt)
                {
                    lastMenuReadout =
                        $"Matched {name}, but callback type is {callbackValue.Type}.";
                    Cancel("Unexpected Aethernet callback data.");
                    return;
                }

                uint callbackData = callbackValue.UInt;

                int dataStart = 6 + i * 4;
                var nativeType = values[dataStart];
                var nativeState = values[dataStart + 1];

                lastMenuReadout =
                    $"Matched row {i}: {name}; callback={callbackData}; " +
                    $"Type={nativeType.UInt} ({nativeType.Type}); " +
                    $"State={nativeState.UInt} ({nativeState.Type}); " +
                    $"AtkValuesCount={valueCount}.";

                if (callbackAttempts == 0 ||
                    (callbackAttempts == 1 &&
                     (DateTime.UtcNow - lastInteraction).TotalMilliseconds > 120))
                {
                    AtkValue* args = stackalloc AtkValue[2];

                    args[0] = new AtkValue
                    {
                        Type = AtkValueType.Int,
                        Int = 11
                    };

                    args[1] = new AtkValue
                    {
                        Type = AtkValueType.UInt,
                        UInt = callbackData
                    };

                    addon->FireCallback(2, args, true);

                    lastInteraction = DateTime.UtcNow;
                    callbackAttempts++;

                    status = $"Teleporting to {destination.Name}...";

                    if (config.ShowDebug)
                        Log.Information(lastMenuReadout);

                    if (callbackAttempts >= 2)
                        GoTo(TravelPhase.WaitForArrival);
                }

                return;
            }

            lastMenuReadout =
                $"AtkValuesCount={valueCount}; " +
                $"entry hint={values[0].UInt}; " +
                $"scanned slots={slots}; " +
                $"names=[{string.Join(" | ", observed)}]";

            if ((DateTime.UtcNow - phaseStarted).TotalSeconds > 5)
            {
                Log.Warning(
                    $"Could not match destination {destination.Name}. " +
                    lastMenuReadout);

                Cancel(
                    $"Could not find {destination.Name} in native menu.");
            }
        }
    }

    private static string Clean(string text)
    {
        return new string(
            text.Where(c => !char.IsControl(c)).ToArray()
        ).Trim();
    }

    private void Cancel(string message)
    {
        phase = TravelPhase.Idle;
        destination = null;
        originAddress = 0;
        callbackAttempts = 0;

        status = message;
        windowOpen = true;

        Log.Warning(message);
    }

    private static AtkUnitBase* GetAddon(string name)
    {
        var handle = Gui.GetAddonByName(name);

        if (handle == null || handle.Address == nint.Zero)
            return null;

        var addon = (AtkUnitBase*)handle.Address;

        return addon != null &&
               addon->IsReady &&
               addon->IsVisible
            ? addon
            : null;
    }

    private void Draw()
    {
        if (!windowOpen)
            return;

        ImGui.SetNextWindowSize(
            new Vector2(365f, 0f),
            ImGuiCond.FirstUseEver
        );

        bool open = windowOpen;

        if (!ImGui.Begin(
                "Aethernet Assist###AethernetAssist",
                ref open,
                ImGuiWindowFlags.AlwaysAutoResize |
                ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            windowOpen = open;
            return;
        }

        var accent = new Vector4(
            0.53f, 0.78f, 0.97f, 1f);

        ImGui.TextColored(accent, "AETHERNET ASSIST");
        ImGui.SameLine();
        ImGui.TextDisabled("v0.0.7 DEV2 FIX1");

        ImGui.Separator();

        if (nearby == null ||
            !networks.TryGetValue(activeGroup, out var entries))
        {
            ImGui.TextDisabled("No Aethernet shard in range.");
            ImGui.TextWrapped(
                "Approach a city Aetheryte or Aethernet shard.");
        }
        else
        {
            var main = entries.FirstOrDefault(x => x.IsMain);

            ImGui.TextUnformatted(
                main?.Name ?? "Local Aethernet");

            var physical = entries.Where(x => !x.Invisible).ToList();
            int unlocked = physical.Count(x => IsUnlocked(x.Id));

            ImGui.TextDisabled(
                $"{unlocked}/{physical.Count} shards attuned");

            ImGui.Spacing();

            // Vanilla-style city sections, based on the
            // territory associated with each real shard.
            foreach (var area in physical.GroupBy(x => x.Territory))
            {
                var areaItems = area.Where(x =>
                    config.ShowUnlearned || DestinationAvailable(x));

                var visibleItems = areaItems.ToList();
                if (visibleItems.Count == 0)
                    continue;

                ImGui.TextDisabled(GetAreaName(area.Key));
                ImGui.Separator();

                foreach (var shard in visibleItems)
                    DrawDestination(shard);

                ImGui.Spacing();
            }

            // The game marks exit gates, airship landings,
            // and certain quest destinations as Invisible.
            // They are real Aetheryte rows, not physical shards.
            var extras = entries.Where(x =>
                x.Invisible &&
                (config.ShowUnlearned || DestinationAvailable(x)))
                .ToList();

            if (extras.Count > 0)
            {
                ImGui.TextDisabled("Other");
                ImGui.Separator();

                foreach (var shard in extras)
                    DrawDestination(shard);
            }
        }

        ImGui.Spacing();
        ImGui.Separator();

        ImGui.TextDisabled(status);

        if (ImGui.CollapsingHeader("Settings"))
        {
            bool autoOpen = config.AutoOpen;

            if (ImGui.Checkbox(
                    "Auto-open near Aethernet",
                    ref autoOpen))
            {
                config.AutoOpen = autoOpen;
                Save();
            }

            bool showLocked = config.ShowUnlearned;

            if (ImGui.Checkbox(
                    "Show unlearned shards",
                    ref showLocked))
            {
                config.ShowUnlearned = showLocked;
                Save();
            }

            float range = config.OpenRange;

            if (ImGui.SliderFloat(
                    "Proximity range",
                    ref range,
                    8f,
                    11.1f,
                    "%.1f yalms"))
            {
                config.OpenRange = Math.Clamp(MathF.Round(range, 1), 8f, 11.1f);
                Save();
            }
        }

        if (config.ShowDebug &&
            ImGui.CollapsingHeader(
                "Diagnostics",
                ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextUnformatted(
                $"Territory: {Client.TerritoryType}");

            ImGui.TextUnformatted(
                $"Network group: {activeGroup}");

            ImGui.TextUnformatted(
                $"Nearby object: {nearby?.BaseId ?? 0}");

            ImGui.TextUnformatted(
                $"Travel phase: {phase}");

            ImGui.TextUnformatted(
                $"Callbacks sent: {callbackAttempts}/2");

            ImGui.TextUnformatted(
                $"Native menu open: {GetAddon("TelepotTown") != null}");

            ImGui.TextUnformatted(
                $"Origin territory: {originTerritory}");

            ImGui.Separator();
            ImGui.TextWrapped(lastMenuReadout);

            if (ImGui.Button("Copy menu diagnostics"))
            {
                ImGui.SetClipboardText(lastMenuReadout);
            }
        }

        ImGui.End();

        windowOpen = open;
    }
}