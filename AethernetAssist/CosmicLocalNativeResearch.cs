using System;
using System.Linq;
using System.Text;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace AethernetAssist;

public sealed unsafe partial class Plugin
{
    // DEV4.2 LOCAL RESEARCH2
    // Read-only diagnostics. Never submit callbacks.

    private string BuildCosmicLocalNativeReport()
    {
        var sb = new StringBuilder();

        sb.AppendLine();
        sb.AppendLine("=== COSMIC NATIVE AETHERNET RESEARCH2 ===");
        sb.AppendLine("Territory: " + Client.TerritoryType);

        if (!IsCosmicTerritory(Client.TerritoryType))
        {
            sb.AppendLine("Outside Cosmic Exploration.");
            return sb.ToString();
        }

        sb.AppendLine();
        sb.AppendLine("=== WKS DESTINATION GROUP MAPPING ===");

        try
        {
            var network = Data.GetExcelSheet<WKSAethernet>();
            var aetherytes = Data.GetExcelSheet<WKSAetheryte>();

            var list = Data
                .GetSubrowExcelSheet<WKSAetheryteList>()
                .SelectMany(group => group)
                .ToArray();

            foreach (var net in network)
            {
                if (net.RowId == 0)
                    continue;

                sb.AppendLine(
                    "Network row=" + net.RowId +
                    " | Unknown0=" + net.Unknown0 +
                    " | Unknown1=" + net.Unknown1);

                foreach (var item in list
                    .Where(x => x.RowId == net.RowId)
                    .OrderBy(x => x.SubrowId))
                {
                    uint id = item.Aetheryte.RowId;

                    string name = id == 0
                        ? "<empty>"
                        : aetherytes.GetRow(id)
                            .Name.Value.Name.ToString();

                    sb.AppendLine(
                        "  Subrow=" + item.SubrowId +
                        " | WKS ID=" + id +
                        " | Destination=" + name);
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine(
                "WKS group read error: " + ex.Message);
        }

        sb.AppendLine();
        sb.AppendLine("=== TELEPOTTOWN NATIVE SLOT INSPECTION ===");

        var addon = GetAddon("TelepotTown");

        if (addon == null || addon->AtkValues == null)
        {
            sb.AppendLine(
                "TelepotTown not open. Interact with a shard.");
            return sb.ToString();
        }

        var values = addon->AtkValues;
        int count = addon->AtkValuesCount;

        sb.AppendLine("AtkValuesCount: " + count);

        if (count <= 262)
        {
            sb.AppendLine(
                "Unexpected native layout; no slot inspection.");
            return sb.ToString();
        }

        int nameSlots = Math.Min(32, count - 262);
        int inspected = 0;

        for (int i = 0; i < nameSlots; i++)
        {
            var nameValue = values[262 + i];

            if ((int)nameValue.Type == 0)
                break;

            bool text =
                nameValue.Type == AtkValueType.String ||
                nameValue.Type == AtkValueType.ConstString ||
                nameValue.Type == AtkValueType.ManagedString ||
                nameValue.Type == AtkValueType.WideString;

            if (!text || nameValue.String.Value == null)
            {
                sb.AppendLine(
                    "Slot " + i +
                    ": Invalid name type " + nameValue.Type);
                continue;
            }

            string name;

            try
            {
                name = Clean(
                    MemoryHelper.ReadSeStringNullTerminated(
                        (nint)nameValue.String.Value).TextValue);
            }
            catch (Exception ex)
            {
                sb.AppendLine(
                    "Slot " + i +
                    ": Name decoding failed: " + ex.Message);
                continue;
            }

            int dataStart = 6 + i * 4;

            if (dataStart + 3 >= count)
                break;

            var nativeType = values[dataStart];
            var nativeState = values[dataStart + 1];
            var nativeAux = values[dataStart + 2];
            var nativeCallback = values[dataStart + 3];

            sb.AppendLine(
                "Slot=" + i +
                " | Name=" + name +
                " | Type=" + nativeType.Type +
                ":" + nativeType.UInt +
                " | State=" + nativeState.Type +
                ":" + nativeState.UInt +
                " | Aux=" + nativeAux.Type +
                ":" + nativeAux.UInt +
                " | Callback=" + nativeCallback.Type +
                ":" + nativeCallback.UInt);

            inspected++;
        }

        sb.AppendLine("Named slots inspected: " + inspected);

        sb.AppendLine();
        sb.AppendLine("=== TELEPOTTOWN HEADER VALUES ===");

        for (int i = 0; i < Math.Min(6, count); i++)
        {
            var value = values[i];

            sb.AppendLine(
                "[" + i + "] Type=" + value.Type +
                " | RawUInt=" + value.UInt);
        }

        sb.AppendLine();
        sb.AppendLine("=== NATIVE RESEARCH STATUS ===");
        sb.AppendLine(
            "Read-only. No callbacks or travel submitted.");

        return sb.ToString();
    }
}