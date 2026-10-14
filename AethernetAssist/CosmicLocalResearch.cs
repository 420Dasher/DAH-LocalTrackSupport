using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using Lumina.Excel.Sheets;
using Dalamud.Game.ClientState.Objects.Enums;

namespace AethernetAssist;

public sealed unsafe partial class Plugin
{
    // DEV4.2 LOCAL RESEARCH1
    //
    // Research the actual Cosmic Exploration Aethernet,
    // which uses the WKS-specific Excel sheets.
    //
    // Read-only, generated only on diagnostic export.
    // No callbacks, travel requests or gameplay changes.

    private static bool IsCosmicTerritory(uint territory)
    {
        return territory is 1237 or 1291 or 1310 or 1319;
    }

    private static string DescribeWksRow(object value)
    {
        var sb = new StringBuilder();

        int printed = 0;

        foreach (var property in value.GetType()
            .GetProperties(BindingFlags.Public |
                           BindingFlags.Instance))
        {
            if (printed >= 24)
                break;

            if (property.GetIndexParameters().Length != 0)
                continue;

            try
            {
                var result = property.GetValue(value);

                string display = result?.ToString() ?? "null";

                if (display.Length > 100)
                    display = display[..100] + "...";

                sb.Append(property.Name);
                sb.Append('=');
                sb.Append(display);
                sb.Append(" | ");
                printed++;
            }
            catch
            {
                // Some generated row properties cannot
                // be resolved outside their sheet context.
            }
        }

        return sb.ToString();
    }

    private string BuildCosmicLocalResearchReport()
    {
        var output = new StringBuilder();

        output.AppendLine();
        output.AppendLine(
            "=== COSMIC LOCAL AETHERNET RESEARCH ===");

        output.AppendLine(
            "UTC: " + DateTime.UtcNow.ToString("O"));

        uint territory = Client.TerritoryType;

        output.AppendLine("Territory: " + territory);

        if (!IsCosmicTerritory(territory))
        {
            output.AppendLine(
                "Outside the four Cosmic Exploration zones.");
            return output.ToString();
        }

        var player = Objects.LocalPlayer;

        if (player == null)
        {
            output.AppendLine("Player unavailable.");
            return output.ToString();
        }

        output.AppendLine();
        output.AppendLine(
            "=== NEARBY COSMIC SHARD OBJECTS ===");

        var localObjects = Objects
            .Where(obj =>
                obj != null &&
                obj.ObjectKind == ObjectKind.EventObj &&
                Vector3.Distance(
                    obj.Position,
                    player.Position) <= 18f &&
                (obj.Name.ToString().Contains(
                    "Aethernet",
                    StringComparison.OrdinalIgnoreCase) ||
                 obj.BaseId >= 2015000 &&
                 obj.BaseId <= 2016000))
            .OrderBy(obj =>
                Vector3.DistanceSquared(
                    obj.Position, player.Position))
            .Take(30)
            .ToArray();

        foreach (var obj in localObjects)
        {
            output.AppendLine(
                "BaseID=" + obj.BaseId +
                " | Name=" + obj.Name.ToString() +
                " | Targetable=" + obj.IsTargetable +
                " | Distance=" +
                Vector3.Distance(
                    obj.Position,
                    player.Position).ToString("F2"));
        }

        if (localObjects.Length == 0)
            output.AppendLine("No nearby shard candidates.");

        output.AppendLine();
        output.AppendLine(
            "=== STANDARD AETHERYTE SHEET CHECK ===");

        try
        {
            var standard = Data.GetExcelSheet<Aetheryte>();

            var rows = standard
                .Where(row =>
                    row.Territory.RowId == territory)
                .Take(40)
                .ToArray();

            output.AppendLine(
                "Rows in current Cosmic territory: " +
                rows.Length);

            foreach (var row in rows)
            {
                output.AppendLine(
                    "ID=" + row.RowId +
                    " | Group=" + row.AethernetGroup +
                    " | Main=" + row.IsAetheryte +
                    " | Invisible=" + row.Invisible +
                    " | Name=" +
                    row.AethernetName.Value.Name.ToString());
            }
        }
        catch (Exception ex)
        {
            output.AppendLine(
                "Standard sheet error: " + ex.Message);
        }

        output.AppendLine();
        output.AppendLine(
            "=== WKS AETHERYTE CATALOG ===");

        try
        {
            var sheet = Data.GetExcelSheet<WKSAetheryte>();
            int count = 0;
            int matched = 0;

            foreach (var row in sheet)
            {
                if (row.RowId == 0 || row.Name.RowId == 0)
                    continue;

                count++;

                string name =
                    row.Name.Value.Name.ToString();

                var ids = row.ObjectGroup.Value
                    .Select(member => member.Unknown0)
                    .Where(id => id != 0)
                    .Distinct()
                    .Take(24)
                    .ToArray();

                bool nearbyMatch = ids.Any(id =>
                    localObjects.Any(obj =>
                        obj.BaseId == id &&
                        obj.IsTargetable));

                if (nearbyMatch)
                    matched++;

                output.AppendLine(
                    "WKS ID=" + row.RowId +
                    " | Name=" + name +
                    " | ObjectGroup=" +
                    row.ObjectGroup.RowId +
                    " | EObj IDs=" +
                    string.Join(",", ids) +
                    " | Nearby interactive match=" +
                    nearbyMatch);
            }

            output.AppendLine(
                "Total named WKSAetheryte rows: " + count);

            output.AppendLine(
                "Nearby interactive matches: " + matched);
        }
        catch (Exception ex)
        {
            output.AppendLine(
                "WKSAetheryte read error: " + ex.Message);
        }

        output.AppendLine();
        output.AppendLine(
            "=== WKS AETHERNET NETWORK ROWS ===");

        try
        {
            var sheet = Data.GetExcelSheet<WKSAethernet>();

            foreach (var row in sheet.Take(20))
                output.AppendLine(
                    "Row: " + DescribeWksRow(row));

            output.AppendLine(
                "Total WKSAethernet rows: " +
                sheet.Count());
        }
        catch (Exception ex)
        {
            output.AppendLine(
                "WKSAethernet read error: " + ex.Message);
        }

        output.AppendLine();
        output.AppendLine(
            "=== WKS DESTINATION LIST ROWS ===");

        try
        {
            // WKSAetheryteList is an IExcelSubrow,
            // not an IExcelRow. Use the subrow sheet API.
            var rows = Data
                .GetSubrowExcelSheet<WKSAetheryteList>()
                .SelectMany(group => group)
                .ToArray();

            foreach (var row in rows.Take(60))
                output.AppendLine(
                    "Row: " + DescribeWksRow(row));

            output.AppendLine(
                "Total WKSAetheryteList rows: " +
                rows.Length);
        }
        catch (Exception ex)
        {
            output.AppendLine(
                "WKSAetheryteList read error: " +
                ex.Message);
        }

        output.AppendLine();
        output.AppendLine(
            "=== CURRENT NATIVE SHARD MENU ===");

        foreach (var name in new[]
        {
            "TelepotTown",
            "SelectString",
            "SelectIconString",
            "SelectYesno",
            "WKSAethernet",
            "WKSAetheryte",
            "WKSPlanetSelect"
        })
        {
            var addon = GetAddon(name);

            output.AppendLine(
                name + ": " +
                (addon == null
                    ? "Not visible"
                    : "Visible | AtkValues=" +
                      addon->AtkValuesCount));
        }

        output.AppendLine();
        output.AppendLine(
            "=== LOCAL RESEARCH STATUS ===");

        output.AppendLine(
            "Read-only. No shard callbacks submitted.");

        output.AppendLine(
            "Open a shard manually and copy diagnostics " +
            "while its destination menu is visible.");

        return output.ToString();
    }
}