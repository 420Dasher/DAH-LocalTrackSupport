using System;
using System.Linq;
using System.Numerics;
using System.Text;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AethernetAssist;

public sealed unsafe partial class Plugin
{
    // Read-only diagnostics. No automation or callbacks.
    private string residentialPreMenuObjects =
        "No pre-menu object snapshot yet.";

    private DateTime residentialSnapshotTime;
    private uint residentialSnapshotTerritory;

    private void TickResidentialObjectResearch()
    {
        if (!TryGetResidentialDistrict(out _))
        {
            residentialSnapshotTerritory = 0;
            residentialPreMenuObjects = "Outside residential district.";
            return;
        }

        if (residentialSnapshotTerritory != Client.TerritoryType)
        {
            residentialSnapshotTerritory = Client.TerritoryType;
            residentialPreMenuObjects = "No snapshot in this zone yet.";
        }

        // Preserve the last approach snapshot once a native
        // menu appears. This catches transient source objects.
        if (GetAddon("TelepotTown") != null ||
            GetAddon("SelectString") != null)
            return;

        residentialPreMenuObjects = ReadResidentialObjects();
        residentialSnapshotTime = DateTime.UtcNow;
    }

    private string ReadResidentialObjects()
    {
        var player = Objects.LocalPlayer;

        if (player == null)
            return "Local player unavailable.";

        var output = new StringBuilder();

        int total = 0;
        int printed = 0;

        // No shard-ID or object-kind filter here.
        // Player characters are excluded for privacy.
        var nearbyObjects = Objects
            .Where(obj =>
                obj != null &&
                !obj.ObjectKind.ToString().Contains(
                    "Player", StringComparison.OrdinalIgnoreCase) &&
                Vector3.Distance(
                    player.Position, obj.Position) <= 35f)
            .OrderBy(obj =>
                Vector3.Distance(
                    player.Position, obj.Position))
            .ToArray();

        foreach (var obj in nearbyObjects)
        {
            total++;

            if (printed >= 40)
                continue;

            var distance = Vector3.Distance(
                player.Position, obj.Position);

            output.AppendLine(
                "[" + printed + "]" +
                " BaseID=" + obj.BaseId +
                " Kind=" + obj.ObjectKind +
                " Targetable=" + obj.IsTargetable +
                " Distance=" + distance.ToString("F2") +
                " XYZ=(" +
                    obj.Position.X.ToString("F2") + ", " +
                    obj.Position.Y.ToString("F2") + ", " +
                    obj.Position.Z.ToString("F2") + ")" +
                " Name=" + obj.Name.ToString());

            printed++;
        }

        output.Insert(
            0,
            "Non-player objects within 35y: " + total +
            " | Displayed: " + printed + "\n");

        if (printed == 0)
            output.AppendLine("No matching objects in object table.");

        return output.ToString();
    }

    private static string ResidentialNativeValue(AtkValue value)
    {
        return value.Type switch
        {
            AtkValueType.UInt =>
                "UInt=" + value.UInt,

            AtkValueType.Int =>
                "Int=" + value.Int,

            _ => "Type=" + value.Type
        };
    }

    private void AppendResidentialObjectResearch(StringBuilder output)
    {
        output.AppendLine();
        output.AppendLine("=== PRE-MENU OBJECT SNAPSHOT ===");

        output.AppendLine("Snapshot UTC: " +
            (residentialSnapshotTime == default
                ? "Unavailable"
                : residentialSnapshotTime.ToString("O")));

        output.AppendLine("Snapshot territory: " +
            residentialSnapshotTerritory);

        output.AppendLine(residentialPreMenuObjects);

        output.AppendLine();
        output.AppendLine("=== LIVE OBJECTS (UNFILTERED) ===");
        output.AppendLine(ReadResidentialObjects());

        output.AppendLine();
        output.AppendLine("=== TELEPOTTOWN NATIVE ROW DATA ===");

        try
        {
            var addon = GetAddon("TelepotTown");

            if (addon == null || addon->AtkValues == null)
            {
                output.AppendLine("Native menu not available.");
                return;
            }

            var values = addon->AtkValues;
            int count = addon->AtkValuesCount;

            output.AppendLine("AtkValuesCount: " + count);

            if (count <= 262)
            {
                output.AppendLine(
                    "City-style name slots are unavailable.");
                return;
            }

            int maxRows = Math.Min(20, count - 262);

            for (int row = 0; row < maxRows; row++)
            {
                // Name slots have already been verified
                // in Research 1 for the Lavender Beds.
                if ((int)values[262 + row].Type == 0)
                    break;

                int data = 6 + row * 4;

                if (data + 3 >= count)
                    break;

                output.AppendLine(
                    "Row " + row +
                    " | Type: " +
                    ResidentialNativeValue(values[data]) +
                    " | State: " +
                    ResidentialNativeValue(values[data + 1]) +
                    " | Extra: " +
                    ResidentialNativeValue(values[data + 2]) +
                    " | Callback: " +
                    ResidentialNativeValue(values[data + 3]));
            }
        }
        catch (Exception ex)
        {
            output.AppendLine(
                "Native metadata read error: " + ex.Message);
        }

        output.AppendLine();
        output.AppendLine(
            "Read-only capture: no native callback submitted.");
    }
}