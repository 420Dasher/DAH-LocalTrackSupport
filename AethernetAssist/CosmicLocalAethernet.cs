using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace AethernetAssist;

public sealed unsafe partial class Plugin
{
    private sealed record CosmicLocalStop(
        uint Id,
        uint Territory,
        uint Network,
        int Index,
        string Name,
        uint[] ObjectIds
    );

    private enum CosmicLocalPhase
    {
        Idle,
        Interact,
        Select,
        WaitForArrival
    }

    private readonly Dictionary<uint, List<CosmicLocalStop>>
        cosmicLocalNetworks = new();

    private CosmicLocalPhase cosmicLocalPhase =
        CosmicLocalPhase.Idle;

    private IGameObject? cosmicLocalNearby;
    private CosmicLocalStop? cosmicLocalSource;
    private CosmicLocalStop? cosmicLocalTarget;

    private nint cosmicLocalSourceAddress;
    private uint cosmicLocalOriginTerritory;
    private Vector3 cosmicLocalOriginPosition;
    private DateTime cosmicLocalPhaseStarted;
    private DateTime cosmicLocalLastAction;
    private bool cosmicLocalSawLoading;
    private int cosmicLocalCallbackAttempts;

    private string cosmicLocalStatus =
        "Approach a Cosmic Aethernet Shard.";

    private string cosmicLocalDetails =
        "No Cosmic subnet destination requested.";

    private void LoadCosmicLocalNetworks()
    {
        try
        {
            var sheet = Data.GetExcelSheet<WKSAetheryte>();

            var destinations = Data
                .GetSubrowExcelSheet<WKSAetheryteList>()
                .SelectMany(group => group)
                .ToArray();

            var planets = new (uint Territory, uint Network)[]
            {
                (1237, 1),
                (1291, 2),
                (1310, 3),
                (1319, 4)
            };

            foreach (var planet in planets)
            {
                var members = destinations
                    .Where(row => row.RowId == planet.Network)
                    .OrderBy(row => row.SubrowId)
                    .ToArray();

                if (members.Length != 5)
                {
                    Log.Warning(
                        "Cosmic Aethernet: network " +
                        planet.Network + " has " +
                        members.Length + " entries, expected 5.");
                    continue;
                }

                var stops = new List<CosmicLocalStop>();

                for (int i = 0; i < members.Length; i++)
                {
                    uint id = members[i].Aetheryte.RowId;

                    if (id == 0 || id !=
                        (planet.Network - 1) * 5 + i + 1)
                        break;

                    var row = sheet.GetRow(id);

                    string name = row.Name.Value.Name
                        .ToString().Trim();

                    var objects = row.ObjectGroup.Value
                        .Select(x => x.Unknown0)
                        .Where(x => x != 0)
                        .Distinct()
                        .ToArray();

                    if (string.IsNullOrWhiteSpace(name) ||
                        objects.Length == 0)
                        break;

                    stops.Add(new CosmicLocalStop(
                        id,
                        planet.Territory,
                        planet.Network,
                        i,
                        name,
                        objects));
                }

                if (stops.Count == 5)
                    cosmicLocalNetworks[planet.Territory] = stops;
                else
                    Log.Warning(
                        "Cosmic Aethernet: invalid WKS records " +
                        "for territory " + planet.Territory);
            }

            Log.Information(
                "Cosmic Aethernet: indexed " +
                cosmicLocalNetworks.Count + " networks.");
        }
        catch (Exception ex)
        {
            Log.Error(
                "Cosmic Aethernet indexing failed: " + ex);
        }
    }

    private void ScanCosmicLocalNearby()
    {
        cosmicLocalNearby = null;
        cosmicLocalSource = null;

        if (!IsCosmicTerritory(Client.TerritoryType) ||
            !cosmicLocalNetworks.TryGetValue(
                Client.TerritoryType, out var stops) ||
            Objects.LocalPlayer == null ||
            IsBusy())
            return;

        var player = Objects.LocalPlayer;

        float closest = config.OpenRange;

        foreach (var obj in Objects)
        {
            if (obj == null ||
                !obj.IsTargetable ||
                obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj)
                continue;

            float distance = Vector3.Distance(
                player.Position, obj.Position);

            if (distance >= closest)
                continue;

            var stop = stops.FirstOrDefault(x =>
                x.ObjectIds.Contains(obj.BaseId));

            if (stop == null)
                continue;

            closest = distance;
            cosmicLocalNearby = obj;
            cosmicLocalSource = stop;
        }
    }

    private static string? CosmicNativeName(
        AtkValue* values, int index, int count)
    {
        if (values == null ||
            index < 0 || index >= count)
            return null;

        var value = values[index];

        if (value.Type != AtkValueType.String &&
            value.Type != AtkValueType.ConstString &&
            value.Type != AtkValueType.ManagedString &&
            value.Type != AtkValueType.WideString)
            return null;

        if (value.String.Value == null)
            return null;

        try
        {
            return Clean(
                MemoryHelper.ReadSeStringNullTerminated(
                    (nint)value.String.Value).TextValue);
        }
        catch
        {
            return null;
        }
    }

    private void SetCosmicLocalPhase(CosmicLocalPhase next)
    {
        cosmicLocalPhase = next;
        cosmicLocalPhaseStarted = DateTime.UtcNow;
    }

    private void StopCosmicLocal(
        string message,
        bool arrived = false)
    {
        cosmicLocalPhase = CosmicLocalPhase.Idle;
        cosmicLocalTarget = null;
        cosmicLocalSourceAddress = nint.Zero;
        cosmicLocalStatus = message;

        cosmicLocalDetails += " | Final: " + message;

        if (arrived)
        {
            cosmicLocalNearby = null;
            cosmicLocalSource = null;
            autoWindowWasInRange = false;
            resetDestinationScroll = true;
        }
        else
        {
            currentPage = NavigationPage.Aethernet;
            windowOpen = true;
            Log.Warning("Cosmic Aethernet: " + message);
        }
    }

    private void StartCosmicLocal(CosmicLocalStop stop)
    {
        if (cosmicLocalPhase != CosmicLocalPhase.Idle ||
            cosmicPhase != CosmicPhase.Idle ||
            phase != TravelPhase.Idle ||
            residentialPhase != ResidentialPhase.Idle ||
            housingPhase != HousingPhase.Idle ||
            IsBusy() ||
            Objects.LocalPlayer == null ||
            cosmicLocalNearby == null ||
            cosmicLocalSource == null)
            return;

        if (cosmicLocalSource.Id == stop.Id ||
            cosmicLocalSource.Network != stop.Network ||
            Client.TerritoryType != stop.Territory)
            return;

        if (GetAddon("TelepotTown") != null ||
            GetAddon("SelectYesno") != null ||
            GetAddon("SelectString") != null)
        {
            cosmicLocalStatus =
                "Close the existing native menu first.";
            return;
        }

        var source = Objects.FirstOrDefault(x =>
            x != null &&
            x.Address == cosmicLocalNearby.Address &&
            x.BaseId == cosmicLocalNearby.BaseId &&
            x.IsTargetable);

        if (source == null ||
            !cosmicLocalSource.ObjectIds.Contains(source.BaseId) ||
            Vector3.Distance(
                Objects.LocalPlayer.Position,
                source.Position) > config.OpenRange + 1f)
            return;

        cosmicLocalTarget = stop;
        cosmicLocalSourceAddress = source.Address;
        cosmicLocalOriginTerritory = Client.TerritoryType;
        cosmicLocalOriginPosition = Objects.LocalPlayer.Position;
        cosmicLocalCallbackAttempts = 0;
        cosmicLocalSawLoading = false;
        cosmicLocalLastAction = DateTime.MinValue;

        cosmicLocalDetails =
            "Requested WKS " + stop.Id +
            " | " + stop.Name +
            " | Network " + stop.Network;

        cosmicLocalStatus =
            "Opening Aethernet for " + stop.Name + "...";

        SetCosmicLocalPhase(CosmicLocalPhase.Interact);
        windowOpen = false;
    }

    private void TickCosmicLocal()
    {
        if (cosmicLocalPhase == CosmicLocalPhase.Idle)
            return;

        if (cosmicLocalTarget == null)
        {
            StopCosmicLocal("No Cosmic destination selected.");
            return;
        }

        var now = DateTime.UtcNow;
        var player = Objects.LocalPlayer;
        var target = cosmicLocalTarget;

        if (cosmicLocalPhase == CosmicLocalPhase.WaitForArrival)
        {
            bool busy = IsBusy();
            bool changedTerritory =
                Client.TerritoryType != cosmicLocalOriginTerritory;

            if (busy || changedTerritory)
                cosmicLocalSawLoading = true;

            float moved = player == null
                ? 0f
                : Vector3.Distance(
                    player.Position,
                    cosmicLocalOriginPosition);

            bool menuOpen = GetAddon("TelepotTown") != null;

            if (player != null &&
                !busy && !menuOpen &&
                (changedTerritory ||
                 moved > 8f ||
                 (cosmicLocalSawLoading && moved > 0.5f)))
            {
                StopCosmicLocal(
                    "Arrived: " + target.Name, true);
                return;
            }

            double elapsed =
                (now - cosmicLocalPhaseStarted).TotalSeconds;

            // TelepotTown may require a second selection
            // to commit the chosen destination.
            // Never send it while loading or after the
            // native menu has closed.
            if (menuOpen && !busy &&
                cosmicLocalCallbackAttempts == 1 &&
                (now - cosmicLocalLastAction)
                    .TotalMilliseconds >= 250)
            {
                cosmicLocalDetails +=
                    " | First selection acknowledged; " +
                    "revalidating before confirmation";

                // This phase validates the planet,
                // all destination names, callback IDs,
                // and selected target again.
                SetCosmicLocalPhase(CosmicLocalPhase.Select);
                return;
            }

            if (menuOpen && !busy && elapsed > 8)
            {
                StopCosmicLocal(
                    "Native Aethernet rejected travel after " +
                    cosmicLocalCallbackAttempts +
                    " callback(s).");
                return;
            }

            if (!menuOpen && !busy && moved <= 8f &&
                !cosmicLocalSawLoading && elapsed > 12)
            {
                StopCosmicLocal(
                    "Menu closed without confirmed movement.");
                return;
            }

            if (elapsed > 40)
                StopCosmicLocal(
                    "Cosmic local arrival timed out.");

            return;
        }

        if (player == null ||
            IsBusy() ||
            Client.TerritoryType != cosmicLocalOriginTerritory)
        {
            StopCosmicLocal(
                "Travel interrupted before selection.");
            return;
        }

        if (HousingMovementKeysPressed())
        {
            StopCosmicLocal(
                "Cancelled by manual movement.");
            return;
        }

        if ((now - cosmicLocalPhaseStarted).TotalSeconds > 12)
        {
            StopCosmicLocal(
                "Cosmic local menu timed out.");
            return;
        }

        if (cosmicLocalPhase == CosmicLocalPhase.Interact)
        {
            if (GetAddon("TelepotTown") != null)
            {
                SetCosmicLocalPhase(CosmicLocalPhase.Select);
                return;
            }

            if ((now - cosmicLocalLastAction)
                .TotalMilliseconds < 850)
                return;

            var source = Objects.FirstOrDefault(x =>
                x != null &&
                x.Address == cosmicLocalSourceAddress &&
                x.IsTargetable);

            if (source == null ||
                cosmicLocalSource == null ||
                !cosmicLocalSource.ObjectIds.Contains(
                    source.BaseId) ||
                Vector3.Distance(
                    player.Position, source.Position) >
                    config.OpenRange + 1f)
            {
                StopCosmicLocal(
                    "Moved out of Cosmic shard range.");
                return;
            }

            var system = TargetSystem.Instance();

            if (system == null)
            {
                StopCosmicLocal(
                    "Native target system unavailable.");
                return;
            }

            system->InteractWithObject(
                (GameObject*)source.Address, false);

            cosmicLocalLastAction = now;
            return;
        }

        if (cosmicLocalPhase != CosmicLocalPhase.Select)
            return;

        var addon = GetAddon("TelepotTown");

        if (addon == null || addon->AtkValues == null)
            return;

        var values = addon->AtkValues;
        int count = addon->AtkValuesCount;

        if (count != 326)
        {
            StopCosmicLocal(
                "Unexpected native Aethernet layout: " + count);
            return;
        }

        int planetIndex = Array.IndexOf(
            CosmicTerritories,
            cosmicLocalOriginTerritory);

        if (planetIndex < 0)
        {
            StopCosmicLocal("Unknown Cosmic territory.");
            return;
        }

        string expectedPlanet = CosmicNames[planetIndex];

        string? header = CosmicNativeName(values, 262, count);

        if (!string.Equals(
            header, expectedPlanet,
            StringComparison.OrdinalIgnoreCase) ||
            values[1].Type != AtkValueType.UInt ||
            values[1].UInt != target.Network ||
            values[2].Type != AtkValueType.UInt ||
            values[2].UInt != 6)
        {
            StopCosmicLocal(
                "Native Cosmic network verification failed.");
            return;
        }

        if (!cosmicLocalNetworks.TryGetValue(
            cosmicLocalOriginTerritory, out var stops) ||
            stops.Count != 5)
        {
            StopCosmicLocal(
                "Cosmic destination catalog unavailable.");
            return;
        }

        // Validate ALL names and native callback indices.
        // Slot 0 is the planet heading, not a destination.
        for (int i = 0; i < stops.Count; i++)
        {
            string? name = CosmicNativeName(
                values, 263 + i, count);

            int dataStart = 6 + (i + 1) * 4;

            if (!string.Equals(
                name, stops[i].Name,
                StringComparison.OrdinalIgnoreCase) ||
                values[dataStart].Type != AtkValueType.UInt ||
                values[dataStart].UInt != 0 ||
                values[dataStart + 3].Type !=
                    AtkValueType.UInt ||
                values[dataStart + 3].UInt != (uint)i)
            {
                cosmicLocalDetails =
                    "Native mismatch at row " + i +
                    " | Name=" + (name ?? "<null>");

                StopCosmicLocal(
                    "Native destination validation failed.");
                return;
            }
        }

        if (target.Index < 0 ||
            target.Index >= stops.Count ||
            stops[target.Index].Id != target.Id)
        {
            StopCosmicLocal(
                "Destination index verification failed.");
            return;
        }

        int targetData = 6 + (target.Index + 1) * 4;

        var nativeState = values[targetData + 1];

        if (nativeState.Type != AtkValueType.UInt ||
            (nativeState.UInt != 0 &&
             nativeState.UInt != 4))
        {
            cosmicLocalDetails =
                "Blocked native state " +
                nativeState.Type + ":" + nativeState.UInt;

            StopCosmicLocal(
                "Selected shard is not available.");
            return;
        }

        uint callback = values[targetData + 3].UInt;

        if (callback != (uint)target.Index ||
            cosmicLocalCallbackAttempts >= 2)
        {
            StopCosmicLocal(
                "Callback guard rejected selection.");
            return;
        }

        // Bound the interval between the first
        // selection and the confirming callback.
        if (cosmicLocalCallbackAttempts == 1 &&
            (now - cosmicLocalLastAction)
                .TotalMilliseconds < 250)
            return;

        AtkValue* args = stackalloc AtkValue[2];

        args[0] = new AtkValue
        {
            Type = AtkValueType.Int,
            Int = 11
        };

        args[1] = new AtkValue
        {
            Type = AtkValueType.UInt,
            UInt = callback
        };

        cosmicLocalDetails =
            "Planet=" + header +
            " | Destination=" + target.Name +
            " | WKS ID=" + target.Id +
            " | Native callback=" + callback +
            " | Native state=" + nativeState.UInt;

        // At most two sends:
        // 1. Select/highlight the destination.
        // 2. Confirm only if TelepotTown remains open.
        //
        // Every send passes the same native validation.
        addon->FireCallback(2, args, true);

        cosmicLocalCallbackAttempts++;
        cosmicLocalLastAction = now;

        cosmicLocalDetails +=
            " | Submitted callback " +
            cosmicLocalCallbackAttempts + "/2";

        cosmicLocalStatus =
            cosmicLocalCallbackAttempts == 1
                ? "Selecting " + target.Name + "..."
                : "Teleporting to " + target.Name + "...";

        SetCosmicLocalPhase(
            CosmicLocalPhase.WaitForArrival);
    }

    private void DrawCosmicLocalAethernetPage()
    {
        if (!cosmicLocalNetworks.TryGetValue(
            Client.TerritoryType, out var stops))
        {
            ImGui.TextDisabled(
                "Cosmic Aethernet data unavailable.");
            return;
        }

        if (cosmicLocalNearby == null ||
            cosmicLocalSource == null)
        {
            ImGui.Spacing();
            ImGui.TextDisabled(
                "No Cosmic Aethernet Shard nearby.");
            ImGui.TextDisabled(
                "Approach an Aethernet crystal.");
            return;
        }

        int planetIndex = Array.IndexOf(
            CosmicTerritories, Client.TerritoryType);

        if (planetIndex < 0)
            return;

        ImGui.TextColored(
            NavAccent, CosmicNames[planetIndex].ToUpperInvariant());

        ImGui.SameLine();

        ImGui.TextDisabled("5 shards");

        ImGui.Spacing();

        float spacing = ImGui.GetStyle().ItemSpacing.Y;
        float rowHeight = 26f + spacing;

        float viewport = Math.Clamp(
            5f * rowHeight +
            ImGui.GetTextLineHeightWithSpacing() + 15f,
            110f, 300f);

        if (ImGui.BeginChild(
            "##CosmicAethernetDestinations",
            new Vector2(0f, viewport),
            false))
        {
            if (resetDestinationScroll)
            {
                ImGui.SetScrollY(0f);
                resetDestinationScroll = false;
            }

            ImGui.TextColored(
                NavMuted, "AETHERNET DESTINATIONS");

            ImGui.Spacing();

            foreach (var stop in stops)
            {
                bool current =
                    cosmicLocalSource.Id == stop.Id;

                bool enabled =
                    !current &&
                    cosmicLocalPhase == CosmicLocalPhase.Idle;

                Vector2 pos = ImGui.GetCursorScreenPos();

                Vector2 size = new(
                    MathF.Max(100f,
                        ImGui.GetContentRegionAvail().X - 4f),
                    26f);

                ImGui.InvisibleButton(
                    "##cosmic-destination-" + stop.Id, size);

                bool hovered = ImGui.IsItemHovered();
                bool clicked = ImGui.IsItemClicked();

                var draw = ImGui.GetWindowDrawList();

                if (hovered && enabled)
                    draw.AddRectFilled(
                        pos, pos + size,
                        ImGui.ColorConvertFloat4ToU32(
                            ThemeColor(6)), 3f);

                string label = current
                    ? stop.Name + "  (current)"
                    : stop.Name;

                draw.AddText(
                    pos + new Vector2(10f, 5f),
                    ImGui.ColorConvertFloat4ToU32(
                        enabled ? NavAccent : NavMuted),
                    "\u25C6");

                draw.AddText(
                    pos + new Vector2(29f, 5f),
                    ImGui.ColorConvertFloat4ToU32(
                        enabled ? ThemeColor(2) :
                                  ThemeColor(3)),
                    label);

                if (hovered && enabled)
                    draw.AddText(
                        pos + new Vector2(size.X - 18f, 5f),
                        ImGui.ColorConvertFloat4ToU32(
                            NavAccent),
                        ">");

                if (clicked && enabled)
                    StartCosmicLocal(stop);
            }
        }

        ImGui.EndChild();

        ImGui.Spacing();
        ImGui.Separator();

        ImGui.TextColored(
            cosmicLocalPhase == CosmicLocalPhase.Idle
                ? NavReady : NavMuted, "\u25CF");

        ImGui.SameLine(0f, 5f);
        ImGui.TextDisabled(cosmicLocalStatus);
    }

    private string BuildCosmicLocalTravelReport()
    {
        return
            "\n=== COSMIC LOCAL AETHERNET TEST1 ===\n" +
            "Phase: " + cosmicLocalPhase + "\n" +
            "Territory: " + Client.TerritoryType + "\n" +
            "Source: " +
                (cosmicLocalSource?.Name ?? "None") + "\n" +
            "Target: " +
                (cosmicLocalTarget?.Name ?? "None") + "\n" +
            "Status: " + cosmicLocalStatus + "\n" +
            "Native callbacks: " +
                cosmicLocalCallbackAttempts + "\n" +
            "Details: " + cosmicLocalDetails + "\n";
    }
}