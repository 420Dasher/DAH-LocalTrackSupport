using System;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace AethernetAssist;

public sealed unsafe partial class Plugin
{
    // DEV4.1 RESEARCH ONLY.
    // This module reads game data and native menus.
    // It intentionally does not fire travel callbacks.

    private string ResidentialResearchSummary()
    {
        if (!TryGetResidentialDistrict(out var district))
            return "Outside the five supported residential districts.";

        try
        {
            var rows = Data.GetExcelSheet<HousingAethernet>()
                .Where(x =>
                    x.TerritoryType.RowId ==
                    Client.TerritoryType)
                .ToArray();

            var manager = HousingManager.Instance();

            string division = manager == null
                ? "Unavailable"
                : manager->GetCurrentDivision() switch
                {
                    1 => "Main Division",
                    2 => "Subdivision",
                    var value => "Unknown (" + value + ")"
                };

            return district.Name +
                " | Ward " + GetCurrentHousingWard() +
                " | " + division +
                " | Sheet rows: " + rows.Length;
        }
        catch (Exception ex)
        {
            return "Residential research unavailable: " +
                ex.Message;
        }
    }

    private string BuildResidentialResearchReport()
    {
        var result = new StringBuilder();

        result.AppendLine(
            "=== AETHERNETASSIST DEV4.1 RESIDENTIAL RESEARCH ===");

        result.AppendLine("UTC: " +
            DateTime.UtcNow.ToString("O"));

        result.AppendLine("Territory: " +
            Client.TerritoryType);

        var player = Objects.LocalPlayer;

        if (player != null)
        {
            result.AppendLine(
                "Player XYZ: " +
                player.Position.X.ToString("F2") + ", " +
                player.Position.Y.ToString("F2") + ", " +
                player.Position.Z.ToString("F2"));
        }
        else
        {
            result.AppendLine("Player: Not available");
        }

        if (!TryGetResidentialDistrict(out var district))
        {
            result.AppendLine(
                "Not currently in a supported residential ward.");

            return result.ToString();
        }

        result.AppendLine("District: " + district.Name);
        result.AppendLine(
            "Current ward: " + GetCurrentHousingWard());

        var manager = HousingManager.Instance();

        if (manager != null)
        {
            byte division = manager->GetCurrentDivision();

            result.AppendLine(
                "Division raw: " + division);

            result.AppendLine(
                "Division: " + (division switch
                {
                    1 => "Main",
                    2 => "Subdivision",
                    _ => "Unknown"
                }));
        }
        else
        {
            result.AppendLine(
                "HousingManager unavailable.");
        }

        result.AppendLine();
        result.AppendLine("=== HOUSINGAETHERNET SHEET ===");

        try
        {
            var rows = Data.GetExcelSheet<HousingAethernet>()
                .Where(x =>
                    x.TerritoryType.RowId ==
                    Client.TerritoryType)
                .OrderBy(x => x.Order)
                .ThenBy(x => x.RowId)
                .ToArray();

            result.AppendLine("Matching rows: " +
                rows.Length);

            // A subdivision split halfway through the
            // ordered rows is a research hypothesis,
            // not yet validated navigation behavior.
            int candidateSplit = rows.Length / 2;

            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];

                string name = row.PlaceName.Value.Name
                    .ToString()
                    .Trim();

                result.AppendLine(
                    "[" + i + "] " +
                    "Row=" + row.RowId +
                    " Order=" + row.Order +
                    " PlaceNameID=" +
                    row.PlaceName.RowId +
                    " GroupGuess=" +
                    (i < candidateSplit
                        ? "Main"
                        : "Subdivision") +
                    " Name=" + name);
            }
        }
        catch (Exception ex)
        {
            result.AppendLine(
                "Sheet read error: " + ex);
        }

        result.AppendLine();
        result.AppendLine("=== NEARBY AETHERNET OBJECTS ===");

        if (player != null)
        {
            int count = 0;

            foreach (var obj in Objects
                .Where(x => x != null)
                .OrderBy(x => Vector3.Distance(
                    player.Position, x.Position)))
            {
                if (obj == null)
                    continue;

                float distance = Vector3.Distance(
                    player.Position, obj.Position);

                if (distance > 40f)
                    continue;

                bool likelyAethernet =
                    obj.ObjectKind == ObjectKind.Aetheryte ||
                    obj.BaseId is
                        2003395 or
                        2000151 or
                        2014665;

                if (!likelyAethernet)
                    continue;

                result.AppendLine(
                    "BaseID=" + obj.BaseId +
                    " Kind=" + obj.ObjectKind +
                    " Targetable=" + obj.IsTargetable +
                    " Distance=" +
                    distance.ToString("F2") +
                    " XYZ=(" +
                    obj.Position.X.ToString("F2") + ", " +
                    obj.Position.Y.ToString("F2") + ", " +
                    obj.Position.Z.ToString("F2") + ")" +
                    " Name=" + obj.Name.ToString());

                count++;
            }

            result.AppendLine(
                "Nearby matching objects: " + count);
        }

        result.AppendLine();
        result.AppendLine("=== NATIVE SELECTSTRING ===");

        try
        {
            var addon =
                (AddonSelectString*)GetAddon("SelectString");

            if (addon == null)
            {
                result.AppendLine(
                    "SelectString not currently open.");
            }
            else
            {
                var menu = addon->PopupMenu.PopupMenu;

                if (menu.EntryNames == null)
                {
                    result.AppendLine("No entry names.");
                }
                else
                {
                    int count = Math.Clamp(
                        menu.EntryCount, 0, 32);

                    for (int i = 0; i < count; i++)
                    {
                        var pointer =
                            menu.EntryNames[i].Value;

                        if (pointer == null)
                            continue;

                        string label = Clean(
                            Marshal.PtrToStringUTF8(
                                (nint)pointer) ?? "");

                        result.AppendLine(
                            "[" + i + "] " + label);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            result.AppendLine(
                "SelectString capture error: " +
                ex.Message);
        }

        result.AppendLine();
        result.AppendLine("=== NATIVE TELEPOTTOWN ===");

        try
        {
            var addon = GetAddon("TelepotTown");

            if (addon == null)
            {
                result.AppendLine(
                    "TelepotTown not currently open.");
            }
            else if (addon->AtkValues == null)
            {
                result.AppendLine(
                    "TelepotTown has no AtkValues.");
            }
            else
            {
                var values = addon->AtkValues;
                int valueCount = addon->AtkValuesCount;

                result.AppendLine(
                    "AtkValuesCount: " + valueCount);

                if (valueCount <= 262)
                {
                    result.AppendLine(
                        "No city-style destination name slots.");
                }
                else
                {
                    int slots = Math.Min(
                        20, valueCount - 262);

                    for (int i = 0; i < slots; i++)
                    {
                        var value = values[262 + i];

                        if ((int)value.Type == 0)
                            break;

                        bool textType =
                            value.Type == AtkValueType.String ||
                            value.Type ==
                                AtkValueType.ConstString ||
                            value.Type ==
                                AtkValueType.WideString ||
                            value.Type ==
                                AtkValueType.ManagedString;

                        if (!textType)
                        {
                            result.AppendLine(
                                "[" + i + "] Type=" +
                                value.Type);
                            continue;
                        }

                        var pointer = value.String.Value;

                        if (pointer == null)
                        {
                            result.AppendLine(
                                "[" + i + "] Null text");
                            continue;
                        }

                        try
                        {
                            string name = Clean(
                                MemoryHelper
                                    .ReadSeStringNullTerminated(
                                        (nint)pointer)
                                    .TextValue);

                            result.AppendLine(
                                "[" + i + "] " + name);
                        }
                        catch (Exception ex)
                        {
                            result.AppendLine(
                                "[" + i +
                                "] Read error: " +
                                ex.Message);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            result.AppendLine(
                "TelepotTown capture error: " +
                ex.Message);
        }

        result.AppendLine();
        AppendResidentialObjectResearch(result);

        result.AppendLine("=== RESEARCH NOTES ===");
        result.AppendLine(
            "All native menus were READ ONLY.");
        result.AppendLine(
            "No residential teleport callback was submitted.");
        result.AppendLine(
            "GroupGuess is not verified against native menus.");

        return result.ToString();
    }
}