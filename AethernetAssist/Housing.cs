using System;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AethernetAssist;

public sealed unsafe partial class Plugin
{
    private readonly record struct HousingDistrict(
        string Name,
        string CityName,
        uint CityTerritory,
        uint ResidentialTerritory
    );

    private static readonly HousingDistrict[] HousingDistricts =
    {
        new("Mist", "Limsa Lominsa", 129, 339),
        new("Lavender Beds", "Gridania", 132, 340),
        new("Goblet", "Ul'dah", 130, 341),
        new("Shirogane", "Kugane", 628, 641),
        new("Empyreum", "Ishgard", 418, 979)
    };

    private enum HousingPhase
    {
        Idle,
        Interact,
        SelectResidential,
        SelectGoToWard,
        SelectWard,
        ConfirmWard,
        ConfirmTravel,
        WaitForArrival
    }

    private HousingPhase housingPhase = HousingPhase.Idle;

    private HousingDistrict housingTarget;
    private int housingTargetWard;
    private nint housingOriginAddress;
    private uint housingOriginTerritory;

    private DateTime housingPhaseStarted;
    private DateTime housingLastAction = DateTime.MinValue;

    private bool housingSawLoading;

    private string housingStatus =
        "Choose a district and ward to begin.";

    private string housingDiagnostic =
        "No housing travel attempted yet.";

    private HousingDistrict SelectedHousingDistrict =>
        HousingDistricts[
            Math.Clamp(
                config.HousingDistrictIndex,
                0,
                HousingDistricts.Length - 1
            )
        ];

    private bool CanStartHousing()
    {
        var player = Objects.LocalPlayer;
        var district = SelectedHousingDistrict;

        return housingPhase == HousingPhase.Idle &&
               phase == TravelPhase.Idle &&
               !IsBusy() &&
               player != null &&
               nearby != null &&
               nearby.IsTargetable &&
               nearby.ObjectKind == ObjectKind.Aetheryte &&
               Client.TerritoryType == district.CityTerritory &&
               Vector3.Distance(
                   player.Position,
                   nearby.Position
               ) <= config.OpenRange + 1f;
    }

    private void SetHousingPhase(HousingPhase next)
    {
        housingPhase = next;
        housingPhaseStarted = DateTime.UtcNow;
    }

    private void StartHousingTravel()
    {
        if (!CanStartHousing())
        {
            housingStatus =
                "Approach the selected city's main Aetheryte.";
            return;
        }

        // Do not take over a manually opened game menu.
        if (GetAddon("SelectString") != null ||
            GetAddon("HousingSelectBlock") != null ||
            GetAddon("SelectYesno") != null)
        {
            housingStatus =
                "Close existing game menus before starting.";
            return;
        }

        housingTarget = SelectedHousingDistrict;
        housingTargetWard = Math.Clamp(config.HousingWard, 1, 30);

        housingOriginAddress = nearby!.Address;
        housingOriginTerritory = Client.TerritoryType;

        housingSawLoading = false;
        housingLastAction = DateTime.MinValue;

        housingDiagnostic =
            $"Requested: {housingTarget.Name}, Ward {housingTargetWard}.";

        housingStatus =
            $"Opening travel to {housingTarget.Name}, Ward {housingTargetWard}...";

        SetHousingPhase(HousingPhase.Interact);
    }

    private void StopHousing(string message)
    {
        housingDiagnostic += " | " + message;
        housingStatus = message;
        housingPhase = HousingPhase.Idle;

        currentPage = NavigationPage.Housing;
        windowOpen = true;

        Log.Warning($"AethernetAssist Housing: {message}");
    }

    private void CompleteHousing()
    {
        housingPhase = HousingPhase.Idle;

        housingStatus =
            $"Arrived: {housingTarget.Name}, Ward {housingTargetWard}.";

        housingDiagnostic += " | Arrival confirmed.";

        currentPage = NavigationPage.Housing;

        Log.Information(
            $"AethernetAssist Housing: {housingStatus}"
        );
    }

    private bool TrySelectHousingMenu(bool residential)
    {
        var addon = (FFXIVClientStructs.FFXIV.Client.UI.AddonSelectString*)
            GetAddon("SelectString");

        if (addon == null)
            return false;

        var menu = addon->PopupMenu.PopupMenu;

        if (menu.EntryNames == null)
            return false;

        int count = Math.Clamp(menu.EntryCount, 0, 32);
        var observed = new System.Collections.Generic.List<string>();

        for (int i = 0; i < count; i++)
        {
            var pointer = menu.EntryNames[i].Value;

            if (pointer == null)
                continue;

            string label = Clean(
                Marshal.PtrToStringUTF8((nint)pointer) ?? ""
            );

            observed.Add(label);

            bool match = residential
                ? label.Contains(
                    "Residential",
                    StringComparison.OrdinalIgnoreCase)
                : label.Contains(
                    "ward",
                    StringComparison.OrdinalIgnoreCase) &&
                  (label.Contains(
                      "Go to",
                      StringComparison.OrdinalIgnoreCase) ||
                   label.Contains(
                      "specified",
                      StringComparison.OrdinalIgnoreCase));

            if (!match)
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

            housingDiagnostic =
                $"Selected native menu entry {i}: {label}.";

            housingLastAction = DateTime.UtcNow;
            return true;
        }

        housingDiagnostic =
            $"Housing menu ({housingPhase}): " +
            string.Join(" | ", observed);

        return false;
    }

    private static string GetHousingYesNoPrompt(AtkUnitBase* addon)
    {
        if (addon == null ||
            addon->UldManager.NodeList == null ||
            addon->UldManager.NodeListCount <= 15)
            return "";

        var node = addon->UldManager.NodeList[15];

        if (node == null)
            return "";

        var textNode = node->GetAsAtkTextNode();

        if (textNode == null)
            return "";

        return Clean(textNode->NodeText.ToString());
    }

    private void TickHousing()
    {
        if (housingPhase == HousingPhase.Idle)
            return;

        var player = Objects.LocalPlayer;

        if (housingPhase == HousingPhase.WaitForArrival)
        {
            if (IsBusy() ||
                Client.TerritoryType != housingOriginTerritory)
            {
                housingSawLoading = true;
            }

            if (player != null &&
                !IsBusy() &&
                Client.TerritoryType ==
                    housingTarget.ResidentialTerritory)
            {
                CompleteHousing();
                return;
            }

            if ((DateTime.UtcNow - housingPhaseStarted)
                .TotalSeconds > 45)
            {
                StopHousing(
                    "Housing travel could not be confirmed.");
            }

            return;
        }

        if ((DateTime.UtcNow - housingPhaseStarted)
            .TotalSeconds > 14)
        {
            StopHousing(
                $"Timed out during {housingPhase}. " +
                housingDiagnostic);
            return;
        }

        if (player == null)
        {
            StopHousing("Player unavailable.");
            return;
        }

        if (IsBusy())
            return;

        switch (housingPhase)
        {
            case HousingPhase.Interact:
            {
                if (GetAddon("SelectString") != null)
                {
                    SetHousingPhase(HousingPhase.SelectResidential);
                    return;
                }

                if ((DateTime.UtcNow - housingLastAction)
                    .TotalMilliseconds < 900)
                    return;

                var obj = Objects.FirstOrDefault(x =>
                    x != null &&
                    x.Address == housingOriginAddress);

                if (obj == null ||
                    !obj.IsTargetable ||
                    Vector3.Distance(
                        player.Position,
                        obj.Position
                    ) > config.OpenRange + 1f)
                {
                    StopHousing(
                        "Main Aetheryte is no longer in range.");
                    return;
                }

                var target = TargetSystem.Instance();

                if (target == null)
                {
                    StopHousing("Target system unavailable.");
                    return;
                }

                target->InteractWithObject(
                    (GameObject*)obj.Address,
                    false
                );

                housingLastAction = DateTime.UtcNow;
                housingStatus = "Opening residential travel menu...";
                return;
            }

            case HousingPhase.SelectResidential:
            {
                if (TrySelectHousingMenu(true))
                {
                    housingStatus =
                        "Opening residential ward options...";
                    SetHousingPhase(HousingPhase.SelectGoToWard);
                }

                return;
            }

            case HousingPhase.SelectGoToWard:
            {
                if (GetAddon("HousingSelectBlock") != null)
                {
                    SetHousingPhase(HousingPhase.SelectWard);
                    return;
                }

                if ((DateTime.UtcNow - housingLastAction)
                    .TotalMilliseconds < 300)
                    return;

                if (TrySelectHousingMenu(false))
                {
                    housingStatus =
                        $"Selecting Ward {housingTargetWard}...";
                    SetHousingPhase(HousingPhase.SelectWard);
                }

                return;
            }

            case HousingPhase.SelectWard:
            {
                var addon = GetAddon("HousingSelectBlock");

                if (addon == null)
                    return;

                if ((DateTime.UtcNow - housingLastAction)
                    .TotalMilliseconds < 300)
                    return;

                if (housingTargetWard > 1)
                {
                    AtkValue* args = stackalloc AtkValue[2];

                    args[0] = new AtkValue
                    {
                        Type = AtkValueType.Int,
                        Int = 1
                    };

                    args[1] = new AtkValue
                    {
                        Type = AtkValueType.Int,
                        Int = housingTargetWard - 1
                    };

                    addon->FireCallback(2, args, true);

                    housingDiagnostic =
                        $"Selected ward index {housingTargetWard - 1}.";
                }
                else
                {
                    housingDiagnostic =
                        "Using default Ward 1 selection.";
                }

                housingLastAction = DateTime.UtcNow;
                SetHousingPhase(HousingPhase.ConfirmWard);
                return;
            }

            case HousingPhase.ConfirmWard:
            {
                if (GetAddon("SelectYesno") != null)
                {
                    SetHousingPhase(HousingPhase.ConfirmTravel);
                    return;
                }

                if ((DateTime.UtcNow - housingLastAction)
                    .TotalMilliseconds < 500)
                    return;

                var addon = GetAddon("HousingSelectBlock");

                if (addon == null)
                    return;

                var button = addon->GetComponentButtonById(34);

                if (button == null || !button->IsEnabled)
                    return;

                // Native HousingSelectBlock confirmation event.
                // Equivalent to the game's ward Select action.
                addon->FireCallbackInt(0);

                housingDiagnostic +=
                    " | Ward selection submitted.";

                housingLastAction = DateTime.UtcNow;
                housingStatus = "Waiting for travel confirmation...";

                SetHousingPhase(HousingPhase.ConfirmTravel);
                return;
            }

            case HousingPhase.ConfirmTravel:
            {
                if (Client.TerritoryType ==
                    housingTarget.ResidentialTerritory)
                {
                    CompleteHousing();
                    return;
                }

                var addon = GetAddon("SelectYesno");

                if (addon == null)
                    return;

                if ((DateTime.UtcNow - housingLastAction)
                    .TotalMilliseconds < 300)
                    return;

                string prompt = GetHousingYesNoPrompt(addon);

                housingDiagnostic +=
                    $" | Confirmation: {prompt}";

                // Never approve an unrelated Yes/No dialog.
                bool expected =
                    prompt.Contains(
                        "ward",
                        StringComparison.OrdinalIgnoreCase) ||
                    (prompt.Contains(
                        "Travel to",
                        StringComparison.OrdinalIgnoreCase) &&
                     prompt.Contains(
                        housingTarget.Name,
                        StringComparison.OrdinalIgnoreCase));

                if (!expected)
                {
                    StopHousing(
                        "Housing confirmation not recognized. " +
                        "Please confirm manually.");
                    return;
                }

                // The native Yes callback.
                addon->FireCallbackInt(0);

                housingLastAction = DateTime.UtcNow;
                housingSawLoading = false;

                housingStatus =
                    $"Traveling to {housingTarget.Name}, " +
                    $"Ward {housingTargetWard}...";

                SetHousingPhase(HousingPhase.WaitForArrival);
                return;
            }
        }
    }

    private void DrawHousingDistrictOption(
        HousingDistrict district,
        int index)
    {
        bool selected = config.HousingDistrictIndex == index;

        Vector2 pos = ImGui.GetCursorScreenPos();
        Vector2 size = new(
            MathF.Max(120f, ImGui.GetContentRegionAvail().X - 4f),
            27f
        );

        ImGui.InvisibleButton(
            $"##housingDistrict{index}",
            size
        );

        bool hovered = ImGui.IsItemHovered();

        var draw = ImGui.GetWindowDrawList();

        if (selected || hovered)
        {
            draw.AddRectFilled(
                pos,
                pos + size,
                ImGui.ColorConvertFloat4ToU32(
                    ThemeColor(6)),
                3f
            );
        }

        if (selected)
        {
            draw.AddRectFilled(
                pos + new Vector2(0f, 3f),
                pos + new Vector2(2f, size.Y - 3f),
                ImGui.ColorConvertFloat4ToU32(NavAccent)
            );
        }

        var color = selected ? NavAccent : ThemeColor(2);

        draw.AddText(
            pos + new Vector2(11f, 6f),
            ImGui.ColorConvertFloat4ToU32(color),
            district.Name
        );

        string location = district.CityName;
        float textWidth = ImGui.CalcTextSize(location).X;

        draw.AddText(
            pos + new Vector2(
                size.X - textWidth - 9f,
                6f),
            ImGui.ColorConvertFloat4ToU32(NavMuted),
            location
        );

        if (ImGui.IsItemClicked() &&
            housingPhase == HousingPhase.Idle)
        {
            config.HousingDistrictIndex = index;
            Save();
        }
    }

    private void DrawHousingPage()
    {
        ImGui.Spacing();
        ImGui.TextColored(NavAccent, "HOUSING");

        ImGui.Spacing();
        ImGui.TextColored(NavMuted, "RESIDENTIAL DISTRICT");

        for (int i = 0; i < HousingDistricts.Length; i++)
            DrawHousingDistrictOption(HousingDistricts[i], i);

        ImGui.Spacing();
        ImGui.TextColored(NavMuted, "WARD");

        int ward = Math.Clamp(config.HousingWard, 1, 30);

        if (ImGui.SliderInt(
            "##housingWard",
            ref ward,
            1,
            30,
            "Ward %d"))
        {
            config.HousingWard = ward;
            Save();
        }

        ImGui.Spacing();

        if (housingPhase != HousingPhase.Idle)
        {
            ImGui.TextColored(
                NavAccent,
                $"Travel: {housingPhase}"
            );

            if (ImGui.Button("Cancel housing travel"))
            {
                StopHousing("Housing travel cancelled.");
            }
        }
        else
        {
            bool canTravel = CanStartHousing();

            if (!canTravel)
                ImGui.BeginDisabled();

            if (ImGui.Button(
                $"Travel to Ward {ward}",
                new Vector2(-1f, 29f)) &&
                canTravel)
            {
                StartHousingTravel();
            }

            if (!canTravel)
                ImGui.EndDisabled();
        }

        ImGui.Spacing();

        if (!CanStartHousing() &&
            housingPhase == HousingPhase.Idle)
        {
            ImGui.TextWrapped(
                $"Requires the main Aetheryte in " +
                $"{SelectedHousingDistrict.CityName}."
            );
        }

        ImGui.TextDisabled(
            "Subdivision transfers: next housing iteration."
        );

        ImGui.Spacing();
        ImGui.Separator();

        ImGui.TextColored(
            housingPhase == HousingPhase.Idle
                ? NavMuted
                : NavAccent,
            housingStatus
        );

        if (config.ShowDebug)
        {
            ImGui.Spacing();
            ImGui.TextWrapped(housingDiagnostic);

            if (ImGui.SmallButton("Copy housing diagnostics"))
                ImGui.SetClipboardText(housingDiagnostic);
        }
    }
}