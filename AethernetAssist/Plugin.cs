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
        Pi.UiBuilder.OpenConfigUi += OpenConfig;

        Log.Information("AethernetAssist v0.0.9 DEV3 FIX1 initialized.");
    }

    public void Dispose()
    {
        Framework.Update -= Update;

        Pi.UiBuilder.Draw -= Draw;
        Pi.UiBuilder.OpenMainUi -= Open;
        Pi.UiBuilder.OpenConfigUi -= OpenConfig;

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
        bool locked = !DestinationAvailable(shard);

        if (locked && !config.ShowUnlearned)
            return;

        Vector2 pos = ImGui.GetCursorScreenPos();
        Vector2 size = new(
            MathF.Max(100f, ImGui.GetContentRegionAvail().X - 4f),
            26f);

        ImGui.InvisibleButton($"##destination-{shard.Id}", size);

        bool hovered = ImGui.IsItemHovered();
        bool clicked = ImGui.IsItemClicked();

        var draw = ImGui.GetWindowDrawList();

        if (hovered && !locked)
        {
            draw.AddRectFilled(
                pos, pos + size,
                ImGui.ColorConvertFloat4ToU32(
                    new Vector4(0.14f, 0.20f, 0.26f, 0.95f)),
                3f);
        }

        Vector4 iconColor = locked ? NavMuted : NavAccent;
        Vector4 textColor = locked
            ? new Vector4(0.46f, 0.51f, 0.55f, 1f)
            : new Vector4(0.86f, 0.89f, 0.93f, 1f);

        string label = locked
            ? $"{shard.Name}  (locked)"
            : shard.Name;

        ImGui.PushClipRect(pos, pos + size, true);

        draw.AddText(
            pos + new Vector2(10f, 5f),
            ImGui.ColorConvertFloat4ToU32(iconColor),
            "\u25C6");

        draw.AddText(
            pos + new Vector2(29f, 5f),
            ImGui.ColorConvertFloat4ToU32(textColor),
            label);

        if (hovered && !locked)
        {
            draw.AddText(
                pos + new Vector2(size.X - 18f, 5f),
                ImGui.ColorConvertFloat4ToU32(NavAccent),
                ">");
        }

        ImGui.PopClipRect();

        if (clicked && !locked)
            StartTravel(shard);
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
                settingsWindowOpen = true;
                break;

            case "settings":
            case "config":
                settingsWindowOpen = true;
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

        if (changed)
        {
            status = "Ready";
            resetDestinationScroll = true;
        }

        if (changed && config.AutoOpen)
        {
            currentPage = NavigationPage.Aethernet;
            windowOpen = true;
        }
    }

    private void ClearNearby()
    {
        if (nearby != null && currentPage == NavigationPage.Aethernet)
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

    private static readonly Vector4 NavAccent =
        new(0.51f, 0.76f, 0.92f, 1f);

    private static readonly Vector4 NavMuted =
        new(0.51f, 0.58f, 0.65f, 1f);

    private static readonly Vector4 NavReady =
        new(0.49f, 0.80f, 0.66f, 1f);

    private enum NavigationPage
    {
        Aethernet,
        Housing,
        DcTravel
    }

    private NavigationPage currentPage = NavigationPage.Aethernet;
    private bool settingsWindowOpen;
    private bool resetDestinationScroll = true;

    private void OpenConfig()
    {
        settingsWindowOpen = true;
    }

    private float GetDestinationContentHeight()
    {
        if (nearby == null ||
            !networks.TryGetValue(activeGroup, out var entries))
            return 0f;

        // Size the viewport using ONLY normal city shards.
        // Additional destinations remain below the fold.
        var city = entries.Where(x =>
            !x.Invisible &&
            (config.ShowUnlearned || DestinationAvailable(x)))
            .ToList();

        int groups = city.GroupBy(x => x.Territory).Count();

        if (city.Count == 0)
            return 72f;

        float spacing = ImGui.GetStyle().ItemSpacing.Y;
        float rowHeight = 26f + spacing;
        float groupHeight = ImGui.GetTextLineHeightWithSpacing() + 8f;

        float height = city.Count * rowHeight +
                       groups * groupHeight + 10f;

        return Math.Clamp(height, 72f, 590f);
    }

    private void DrawPageTab(
        string label,
        NavigationPage target,
        float width)
    {
        Vector2 pos = ImGui.GetCursorScreenPos();
        Vector2 size = new(width, 30f);

        ImGui.InvisibleButton(
            $"##navigation-{(int)target}", size);

        bool hovered = ImGui.IsItemHovered();
        bool selected = currentPage == target;

        var draw = ImGui.GetWindowDrawList();

        if (hovered && !selected)
        {
            draw.AddRectFilled(
                pos, pos + size,
                ImGui.ColorConvertFloat4ToU32(
                    new Vector4(0.13f, 0.18f, 0.23f, 0.75f)),
                3f);
        }

        Vector4 color = selected ? NavAccent : NavMuted;
        float textWidth = ImGui.CalcTextSize(label).X;

        draw.AddText(
            pos + new Vector2((width - textWidth) * 0.5f, 6f),
            ImGui.ColorConvertFloat4ToU32(color),
            label);

        if (selected)
        {
            draw.AddRectFilled(
                pos + new Vector2(14f, 28f),
                pos + new Vector2(width - 14f, 30f),
                ImGui.ColorConvertFloat4ToU32(NavAccent),
                1f);
        }

        if (ImGui.IsItemClicked() && !selected)
        {
            currentPage = target;

            if (target == NavigationPage.Aethernet)
                resetDestinationScroll = true;
        }
    }

    private void DrawAethernetPage()
    {
        if (nearby == null ||
            !networks.TryGetValue(activeGroup, out var entries))
        {
            ImGui.Spacing();
            ImGui.TextDisabled("No Aethernet shard nearby.");
            ImGui.TextDisabled("Approach an Aethernet crystal.");
            return;
        }

        var physical = entries.Where(x => !x.Invisible).ToList();
        var main = entries.FirstOrDefault(x => x.IsMain);

        string name = main?.Name ?? "Local Aethernet";
        int unlocked = physical.Count(x => IsUnlocked(x.Id));

        string attuned = $"{unlocked}/{physical.Count} attuned";

        ImGui.TextUnformatted(name);
        ImGui.SameLine(
            MathF.Max(210f,
                ImGui.GetWindowWidth() -
                ImGui.CalcTextSize(attuned).X - 16f));
        ImGui.TextColored(NavReady, attuned);

        ImGui.Spacing();

        float viewport = GetDestinationContentHeight();

        if (ImGui.BeginChild(
            "##AethernetDestinations",
            new Vector2(0f, viewport),
            false))
        {
            if (resetDestinationScroll)
            {
                ImGui.SetScrollY(0f);
                resetDestinationScroll = false;
            }

            foreach (var area in physical.GroupBy(x => x.Territory))
            {
                var visible = area.Where(x =>
                    config.ShowUnlearned || DestinationAvailable(x))
                    .ToList();

                if (visible.Count == 0)
                    continue;

                ImGui.TextColored(
                    NavMuted,
                    GetAreaName(area.Key).ToUpperInvariant());
                ImGui.Spacing();

                foreach (var shard in visible)
                    DrawDestination(shard);

                ImGui.Spacing();
            }

            var extras = entries.Where(x =>
                x.Invisible &&
                (config.ShowUnlearned || DestinationAvailable(x)))
                .ToList();

            if (extras.Count > 0)
            {
                // This gap starts AFTER the physical shards.
                // The Other heading is initially below the
                // visible scroll viewport, not in the main list.
                ImGui.Dummy(new Vector2(0f, 38f));

                ImGui.TextColored(NavMuted, "OTHER");
                ImGui.Spacing();

                foreach (var shard in extras)
                    DrawDestination(shard);
            }
        }

        ImGui.EndChild();

        ImGui.Spacing();
        ImGui.Separator();

        bool ready = phase == TravelPhase.Idle &&
            (status == "Ready" || status.StartsWith("Arrived:"));

        ImGui.TextColored(
            ready ? NavReady : NavMuted,
            "\u25CF");

        ImGui.SameLine(0f, 5f);
        ImGui.TextDisabled(status);

        if (entries.Any(x => x.Invisible))
        {
            string hint = "Scroll for more";
            float hintWidth = ImGui.CalcTextSize(hint).X;

            // Only show the hint when the footer has space.
            if (ImGui.GetWindowWidth() >
                ImGui.CalcTextSize(status).X + hintWidth + 65f)
            {
                ImGui.SameLine(
                    ImGui.GetWindowWidth() - hintWidth - 16f);
                ImGui.TextColored(NavMuted, hint);
            }
        }
    }

    private void DrawSettings()
    {
        if (!settingsWindowOpen)
            return;

        ImGui.SetNextWindowSize(
            new Vector2(355f, 235f),
            ImGuiCond.FirstUseEver);

        bool open = settingsWindowOpen;

        if (!ImGui.Begin(
            "Aethernet Assist - Settings###AethernetAssistSettings",
            ref open,
            ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            settingsWindowOpen = open;
            return;
        }

        ImGui.TextUnformatted("SETTINGS");
        ImGui.SameLine();
        ImGui.TextDisabled("DEV3");
        ImGui.Separator();

        bool autoOpen = config.AutoOpen;

        if (ImGui.Checkbox(
            "Auto-open near Aethernet",
            ref autoOpen))
        {
            config.AutoOpen = autoOpen;
            Save();
        }

        bool showUnlearned = config.ShowUnlearned;

        if (ImGui.Checkbox(
            "Show unlearned destinations",
            ref showUnlearned))
        {
            config.ShowUnlearned = showUnlearned;
            Save();
        }

        ImGui.Spacing();

        float range = config.OpenRange;

        if (ImGui.SliderFloat(
            "Proximity range",
            ref range,
            8f,
            11.1f,
            "%.1f yalms"))
        {
            config.OpenRange = Math.Clamp(
                MathF.Round(range, 1),
                8f,
                11.1f);
            Save();
        }

        ImGui.Spacing();

        bool diagnostics = config.ShowDebug;

        if (ImGui.Checkbox(
            "Enable diagnostics",
            ref diagnostics))
        {
            config.ShowDebug = diagnostics;
            Save();
        }

        if (config.ShowDebug &&
            ImGui.CollapsingHeader("Diagnostics"))
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
                $"Callbacks: {callbackAttempts}/2");

            ImGui.TextUnformatted(
                $"Native menu: {GetAddon("TelepotTown") != null}");

            ImGui.Separator();
            ImGui.TextWrapped(lastMenuReadout);

            if (ImGui.Button("Copy diagnostics"))
                ImGui.SetClipboardText(lastMenuReadout);
        }

        ImGui.End();
        settingsWindowOpen = open;
    }

    private void Draw()
    {
        // Settings are fully independent of navigation.
        DrawSettings();

        if (!windowOpen)
            return;

        const float width = 386f;

        ImGui.PushStyleColor(
            ImGuiCol.WindowBg,
            new Vector4(0.065f, 0.080f, 0.105f, 0.97f));

        ImGui.PushStyleColor(
            ImGuiCol.ChildBg,
            new Vector4(0.077f, 0.095f, 0.120f, 0.95f));

        ImGui.PushStyleColor(
            ImGuiCol.ScrollbarBg,
            new Vector4(0.075f, 0.090f, 0.115f, 1f));

        ImGui.PushStyleColor(
            ImGuiCol.ScrollbarGrab,
            new Vector4(0.26f, 0.34f, 0.42f, 0.9f));

        ImGui.PushStyleColor(
            ImGuiCol.ScrollbarGrabHovered,
            new Vector4(0.40f, 0.55f, 0.67f, 1f));

        ImGui.PushStyleVar(
            ImGuiStyleVar.ItemSpacing,
            new Vector2(7f, 3f));

        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 7f);

        try
        {
            ImGui.SetNextWindowSize(
                new Vector2(width, 195f),
                ImGuiCond.FirstUseEver);

            bool open = windowOpen;

            if (!ImGui.Begin(
                "Aethernet Assist###AethernetAssist",
                ref open,
                ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoScrollbar))
            {
                ImGui.End();
                windowOpen = open;
                return;
            }

            ImGui.TextDisabled("v0.0.9");

            ImGui.SameLine(ImGui.GetWindowWidth() - 87f);

            ImGui.PushStyleColor(
                ImGuiCol.Button, new Vector4(0f, 0f, 0f, 0f));
            ImGui.PushStyleColor(
                ImGuiCol.ButtonHovered,
                new Vector4(0.16f, 0.23f, 0.30f, 0.9f));
            ImGui.PushStyleColor(
                ImGuiCol.ButtonActive,
                new Vector4(0.20f, 0.31f, 0.40f, 1f));

            if (ImGui.SmallButton("Settings"))
                settingsWindowOpen = true;

            ImGui.PopStyleColor(3);
            ImGui.Spacing();

            float available = ImGui.GetContentRegionAvail().X;
            float tabWidth = MathF.Max(85f, (available - 14f) / 3f);

            DrawPageTab(
                "Aethernet", NavigationPage.Aethernet, tabWidth);

            ImGui.SameLine(0f, 7f);

            DrawPageTab(
                "Housing", NavigationPage.Housing, tabWidth);

            ImGui.SameLine(0f, 7f);

            DrawPageTab(
                "DC Travel", NavigationPage.DcTravel, tabWidth);

            ImGui.Spacing();

            switch (currentPage)
            {
                case NavigationPage.Aethernet:
                    DrawAethernetPage();
                    break;

                case NavigationPage.Housing:
                    ImGui.Spacing();
                    ImGui.TextColored(NavAccent, "HOUSING");
                    ImGui.TextDisabled(
                        "Residential travel is planned.");
                    ImGui.Spacing();
                    break;

                case NavigationPage.DcTravel:
                    ImGui.Spacing();
                    ImGui.TextColored(NavAccent, "DC TRAVEL");
                    ImGui.TextDisabled(
                        "Same-DC world visiting is planned.");
                    ImGui.Spacing();
                    break;
            }

            // Match the actual content immediately, on the
            // current frame. No delayed resize animation.
            float naturalHeight =
                ImGui.GetCursorPosY() +
                ImGui.GetStyle().WindowPadding.Y + 3f;

            ImGui.SetWindowSize(
                new Vector2(width, MathF.Max(132f, naturalHeight)),
                ImGuiCond.Always);

            ImGui.End();
            windowOpen = open;
        }
        finally
        {
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(5);
        }
    }
}
