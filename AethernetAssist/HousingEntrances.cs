using System;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace AethernetAssist;

public sealed unsafe partial class Plugin
{
    private readonly record struct WardEntrance(
        uint Territory,
        uint NpcBaseId,
        Vector3 First,
        Vector3 Second
    );

    // Known ward transition points. These are used only for
    // proximity detection; the game handles the actual travel.
    private static readonly WardEntrance[] WardEntrances =
    {
        new(
            339, 0,
            new Vector3(-10.03f, 48.73f, -162.87f),
            new Vector3(-539.83f, 48.58f, -714.26f)),
        new(
            340, 1005655,
            new Vector3(10.73f, 2.61f, 208.09f),
            new Vector3(-912.11f, 2.61f, -693.26f)),
        new(
            341, 0,
            new Vector3(-6.57f, -11.08f, -194.34f),
            new Vector3(-509.08f, -11.08f, -710.30f)),
        new(
            641, 1019108,
            new Vector3(-121.17f, 2.03f, 154.89f),
            new Vector3(-858.95f, 2.03f, -825.19f)),
        new(
            979, 1035622,
            new Vector3(13.72f, -15.20f, 182.94f),
            new Vector3(-887.14f, -15.20f, -690.30f))
    };

    private bool housingFromWard;
    private bool housingLeavingDistrict;
    private bool housingSawLoading;
    private bool housingGateSource;

    private bool TryGetResidentialDistrict(
        out HousingDistrict district)
    {
        district = default;

        foreach (var candidate in HousingDistricts)
        {
            if (candidate.ResidentialTerritory !=
                Client.TerritoryType)
                continue;

            district = candidate;
            return true;
        }

        return false;
    }

    private bool TryFindWardEntrance(
        out HousingDistrict district,
        out IGameObject? npc,
        out bool isGate)
    {
        district = default;
        npc = null;
        isGate = false;

        var player = Objects.LocalPlayer;

        if (player == null ||
            !TryGetResidentialDistrict(out district))
            return false;

        foreach (var entrance in WardEntrances)
        {
            if (entrance.Territory != Client.TerritoryType)
                continue;

            if (entrance.NpcBaseId == 0)
            {
                isGate = true;

                // Walk-through gates: identify the nearby
                // exit, without automating character movement.
                return
                    Vector3.Distance(
                        player.Position,
                        entrance.First) <= config.OpenRange ||
                    Vector3.Distance(
                        player.Position,
                        entrance.Second) <= config.OpenRange;
            }

            float best = config.OpenRange;

            foreach (var obj in Objects)
            {
                if (obj == null ||
                    !obj.IsTargetable ||
                    obj.BaseId != entrance.NpcBaseId)
                    continue;

                float distance = Vector3.Distance(
                    player.Position,
                    obj.Position);

                if (distance >= best)
                    continue;

                // Avoid another actor with the same base ID
                // in an unexpected part of the ward.
                bool positionMatches =
                    Vector3.Distance(
                        obj.Position, entrance.First) < 15f ||
                    Vector3.Distance(
                        obj.Position, entrance.Second) < 15f;

                if (!positionMatches)
                    continue;

                best = distance;
                npc = obj;
            }

            return npc != null;
        }

        return false;
    }

    private void StartHousingExit()
    {
        if (!CanStartHousing() ||
            !TryFindWardEntrance(
                out var district,
                out var npc,
                out bool gate))
            return;

        housingTarget = district;
        housingTargetWard = 0;
        housingOriginAddress = npc?.Address ?? nint.Zero;

        housingFromWard = true;
        housingLeavingDistrict = true;
        housingGateSource = gate;
        housingSawLoading = false;
        housingLastAction = DateTime.MinValue;
        PrepareHousingGateWalk();

        housingDiagnostic =
            "Leave district requested: " + district.Name;

        housingStatus = gate
            ? "Walk through the exit to open its travel menu."
            : "Opening district exit menu...";

        SetHousingPhase(gate
            ? HousingPhase.SelectExit
            : HousingPhase.Interact);
    }

    private bool TrySelectLeaveDistrict()
    {
        var addon = (FFXIVClientStructs.FFXIV.Client.UI.AddonSelectString*)
            GetAddon("SelectString");

        if (addon == null)
            return false;

        var menu = addon->PopupMenu.PopupMenu;

        if (menu.EntryNames == null)
            return false;

        int count = Math.Clamp(menu.EntryCount, 0, 32);
        var options = new System.Collections.Generic.List<string>();

        for (int i = 0; i < count; i++)
        {
            var ptr = menu.EntryNames[i].Value;
            if (ptr == null)
                continue;

            string label = Clean(
                Marshal.PtrToStringUTF8((nint)ptr) ?? "");

            options.Add(label);

            // Only the game's explicit residential exit.
            bool exit =
                label.Contains(
                    "Leave residential district",
                    StringComparison.OrdinalIgnoreCase) ||
                label.Contains(
                    "Exit residential district",
                    StringComparison.OrdinalIgnoreCase);

            if (!exit)
                continue;

            AtkValue value = new()
            {
                Type = AtkValueType.Int,
                Int = i
            };

            addon->AtkUnitBase.FireCallback(
                1,
                &value,
                true);

            housingDiagnostic =
                "Selected native exit: " + label;

            housingLastAction = DateTime.UtcNow;
            return true;
        }

        housingDiagnostic =
            "Exit menu options: " +
            string.Join(" | ", options);

        return false;
    }

    private string DescribeWardEntrance()
    {
        bool found = TryFindWardEntrance(
            out var district,
            out var npc,
            out bool gate);

        if (!found)
        {
            return TryGetResidentialDistrict(out district)
                ? district.Name + ": Not near ward entrance"
                : "Not in residential district";
        }

        return district.Name +
            (gate
                ? ": Walk-through gate"
                : ": NPC " + npc!.BaseId);
    }

    private void CompleteHousingExit()
    {
        StopHousingGateWalk();
        housingPhase = HousingPhase.Idle;
        housingStatus = "Left residential district.";
        housingDiagnostic += " | Exit confirmed.";

        currentPage = NavigationPage.Housing;
        windowOpen = true;
    }
}