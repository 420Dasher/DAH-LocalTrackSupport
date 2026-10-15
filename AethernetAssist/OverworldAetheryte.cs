using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace AethernetAssist;

public sealed unsafe partial class Plugin
{
    // Overworld travel is isolated from city Aethernet, residential,
    // Cosmic and housing travel. All destinations come from game data.
    private sealed record OverworldStop(uint Id, uint Territory, string Name);

    private enum OverworldPhase { Idle, WaitingForArrival }

    private readonly Dictionary<uint, List<OverworldStop>> overworldStops = new();
    private IGameObject? overworldNearby;
    private OverworldPhase overworldPhase = OverworldPhase.Idle;
    private DateTime overworldRequestedAt;
    private Vector3 overworldOriginPosition;
    private uint overworldOriginTerritory;
    private bool overworldSawLoading;
    private string overworldDestinationName = "";
    private string overworldStatus = "Ready";

    private void LoadOverworldAetherytes()
    {
        foreach (var row in Data.GetExcelSheet<Aetheryte>())
        {
            // Regular teleport destinations, not city Aethernet shards.
            if (!row.IsAetheryte || row.AethernetGroup != 0)
                continue;

            uint territory = row.Territory.Value.RowId;
            if (territory == 0)
                continue;

            string name = row.PlaceName.Value.Name.ToString().Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (!overworldStops.TryGetValue(territory, out var list))
                overworldStops[territory] = list = new List<OverworldStop>();

            list.Add(new OverworldStop(row.RowId, territory, name));
        }

        foreach (var list in overworldStops.Values)
            list.Sort((a, b) => string.Compare(
                a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

        Log.Information($"Overworld index: {overworldStops.Count} territories, " +
                        $"{overworldStops.Values.Sum(x => x.Count)} main Aetherytes.");
    }

    private bool HasOverworldStopsHere() =>
        overworldStops.TryGetValue(Client.TerritoryType, out var list) &&
        list.Count > 1;

    private void ScanOverworldNearby()
    {
        overworldNearby = null;

        var player = Objects.LocalPlayer;
        if (player == null || IsBusy() || !HasOverworldStopsHere() ||
            nearby != null || cosmicLocalNearby != null)
            return;

        float closest = config.OpenRange;
        foreach (var obj in Objects)
        {
            if (obj == null || !obj.IsTargetable ||
                obj.ObjectKind != ObjectKind.Aetheryte)
                continue;

            float dist = Vector3.Distance(player.Position, obj.Position);
            if (dist >= closest)
                continue;

            overworldNearby = obj;
            closest = dist;
        }
    }

    private void DrawOverworldPage()
    {
        if (overworldNearby == null ||
            !overworldStops.TryGetValue(Client.TerritoryType, out var stops))
            return;

        // In contrast to Aethernet destination callbacks, overworld
        // costs/unlocks are read directly from the live Teleport list.
        var live = Aetherytes.Where(x =>
            x.TerritoryId == Client.TerritoryType &&
            x.SubIndex == 0 && !x.IsSharedHouse && !x.IsApartment)
            .GroupBy(x => x.AetheryteId)
            .ToDictionary(x => x.Key, x => x.First());

        ImGui.TextUnformatted(GetAreaName(Client.TerritoryType));
        ImGui.SameLine();
        ImGui.TextDisabled("OVERWORLD");
        ImGui.Spacing();

        var sourceName = overworldNearby.Name.ToString();
        var visible = stops.Where(x => config.ShowUnlearned || live.ContainsKey(x.Id))
            .ToList();

        float height = Math.Clamp(visible.Count * 32f + 6f, 70f, 480f);
        if (ImGui.BeginChild("##OverworldDestinations", new Vector2(0f, height), false))
        {
            foreach (var stop in visible)
            {
                bool unlocked = live.TryGetValue(stop.Id, out var entry);
                bool current = string.Equals(sourceName, stop.Name,
                    StringComparison.OrdinalIgnoreCase);
                bool canTeleport = unlocked && !current &&
                    overworldPhase == OverworldPhase.Idle;

                string suffix = current ? "Current" : unlocked
                    ? $"{entry!.GilCost:N0} gil" : "Not attuned";
                string label = $"{stop.Name}  -  {suffix}##overworld-{stop.Id}";
                ImGui.BeginDisabled(!canTeleport);
                if (ImGui.Button(label, new Vector2(-1f, 26f)) && canTeleport)
                    StartOverworldTeleport(stop);
                ImGui.EndDisabled();
            }
        }
        ImGui.EndChild();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(overworldPhase == OverworldPhase.Idle ? NavReady : NavMuted,
            "\u25CF");
        ImGui.SameLine(0f, 5f);
        ImGui.TextDisabled(overworldStatus);
    }

    private void StartOverworldTeleport(OverworldStop stop)
    {
        var player = Objects.LocalPlayer;
        if (player == null || overworldNearby == null || !overworldNearby.IsTargetable ||
            Vector3.Distance(player.Position, overworldNearby.Position) > config.OpenRange + 0.5f ||
            IsBusy() || overworldPhase != OverworldPhase.Idle ||
            phase != TravelPhase.Idle || housingPhase != HousingPhase.Idle ||
            residentialPhase != ResidentialPhase.Idle ||
            cosmicLocalPhase != CosmicLocalPhase.Idle ||
            cosmicPhase != CosmicPhase.Idle ||
            worldVisitPhase != WorldVisitPhase.Idle ||
            stop.Territory != Client.TerritoryType)
            return;

        // Revalidate against the live list at click time: no cached costs,
        // no unsupported estate sub-index, no inferred unlock state.
        var entry = Aetherytes.FirstOrDefault(x =>
            x.AetheryteId == stop.Id && x.TerritoryId == stop.Territory &&
            x.SubIndex == 0 && !x.IsSharedHouse && !x.IsApartment);
        if (entry == null)
        {
            overworldStatus = "Destination is not available for Teleport.";
            return;
        }

        if (string.Equals(overworldNearby.Name.ToString(), stop.Name,
            StringComparison.OrdinalIgnoreCase))
        {
            overworldStatus = "Already at this Aetheryte.";
            return;
        }

        var telepo = Telepo.Instance();
        if (telepo == null)
        {
            overworldStatus = "The game's teleport service is unavailable.";
            return;
        }

        bool accepted = telepo->Teleport(entry.AetheryteId, entry.SubIndex);
        if (!accepted)
        {
            overworldStatus = "The game rejected the teleport request.";
            return;
        }

        RememberNavigationPage(NavigationPage.Aethernet);
        overworldOriginTerritory = Client.TerritoryType;
        overworldOriginPosition = player.Position;
        overworldRequestedAt = DateTime.UtcNow;
        overworldDestinationName = stop.Name;
        overworldSawLoading = false;
        overworldPhase = OverworldPhase.WaitingForArrival;
        overworldStatus = $"Teleporting to {stop.Name}...";
        windowOpen = false;
        Log.Information("Overworld teleport requested: " +
                        stop.Name + " (#" + stop.Id + ", " + entry.GilCost + " gil).");
    }

    private void TickOverworldTeleport()
    {
        if (overworldPhase != OverworldPhase.WaitingForArrival)
            return;

        if (IsBusy() || Objects.LocalPlayer == null)
        {
            overworldSawLoading = true;
            return;
        }

        if (Client.TerritoryType != overworldOriginTerritory)
        {
            StopOverworldTeleport("Territory changed unexpectedly. No retry.", false);
            return;
        }

        // Same-territory teleports do not necessarily trigger a territory
        // change. Confirm a significant actual position change instead.
        if (Vector3.Distance(Objects.LocalPlayer.Position, overworldOriginPosition) > 20f)
        {
            StopOverworldTeleport("Arrived: " + overworldDestinationName, true);
            return;
        }

        if ((DateTime.UtcNow - overworldRequestedAt).TotalSeconds > 50)
            StopOverworldTeleport("Teleport arrival unconfirmed. No retry.", false);
    }

    private void StopOverworldTeleport(string message, bool arrived)
    {
        overworldPhase = OverworldPhase.Idle;
        overworldStatus = message;
        if (arrived)
        {
            overworldNearby = null;
            autoWindowWasInRange = false;
        }
        else
        {
            windowOpen = true;
            Log.Warning(message);
        }
    }
}