using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace AethernetAssist;

public sealed unsafe partial class Plugin
{
    private sealed record OverworldStop(
        uint Id,
        uint Territory,
        string Name
    );

    private readonly Dictionary<uint, List<OverworldStop>> overworldStops = new();
    private readonly List<string> overworldMenuSnapshots = new();

    private IGameObject? overworldNearby;
    private string overworldPreviousMenu = "";

    private void LoadOverworldAetherytes()
    {
        foreach (var row in Data.GetExcelSheet<Aetheryte>())
        {
            if (!row.IsAetheryte || row.AethernetGroup != 0)
                continue;

            uint territory = row.Territory.Value.RowId;

            if (territory == 0)
                continue;

            string name = row.PlaceName.Value.Name.ToString().Trim();

            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (!overworldStops.TryGetValue(territory, out var list))
            {
                list = new List<OverworldStop>();
                overworldStops[territory] = list;
            }

            if (list.All(x => x.Id != row.RowId))
                list.Add(new OverworldStop(row.RowId, territory, name));
        }

        foreach (var list in overworldStops.Values)
        {
            list.Sort((a, b) =>
                string.Compare(
                    a.Name,
                    b.Name,
                    StringComparison.OrdinalIgnoreCase
                ));
        }

        Log.Information(
            $"Overworld catalogue: {overworldStops.Count} territories; " +
            $"{overworldStops.Values.Sum(x => x.Count)} main crystals."
        );
    }

    private void ScanOverworldNearby()
    {
        overworldNearby = null;

        var player = Objects.LocalPlayer;

        if (player == null ||
            IsBusy() ||
            nearby != null ||
            cosmicLocalNearby != null ||
            !overworldStops.TryGetValue(
                Client.TerritoryType, out var stops) ||
            stops.Count < 2)
            return;

        float closest = config.OpenRange;

        foreach (var obj in Objects)
        {
            if (obj == null ||
                !obj.IsTargetable ||
                obj.ObjectKind != ObjectKind.Aetheryte)
                continue;

            bool matches = stops.Any(x =>
                x.Id == obj.BaseId ||
                string.Equals(
                    x.Name,
                    obj.Name.ToString(),
                    StringComparison.OrdinalIgnoreCase
                ));

            if (!matches)
                continue;

            float distance = Vector3.Distance(
                player.Position,
                obj.Position
            );

            if (distance >= closest)
                continue;

            closest = distance;
            overworldNearby = obj;
        }
    }

    // Read-only capture. No callbacks, interactions or teleports.

    private void CaptureOverworldNativeMenu()
    {
        if (overworldNearby == null)
        {
            overworldPreviousMenu = "";
            return;
        }

        var menus = new List<string>();

        var select = (AddonSelectString*)GetAddon("SelectString");

        if (select != null)
        {
            var popup = select->PopupMenu.PopupMenu;

            if (popup.EntryNames != null)
            {
                int count = Math.Clamp(popup.EntryCount, 0, 32);
                var labels = new List<string>();

                for (int i = 0; i < count; i++)
                {
                    var pointer = popup.EntryNames[i].Value;

                    if (pointer == null)
                        continue;

                    try
                    {
                        string label = Clean(
                            Marshal.PtrToStringUTF8(
                                (nint)pointer
                            ) ?? ""
                        );

                        if (label.Length > 100)
                            label = label.Substring(0, 100);

                        labels.Add($"{i}: {label}");
                    }
                    catch
                    {
                        labels.Add($"{i}: unreadable label");
                    }
                }

                menus.Add(
                    "SelectString entries=" + count +
                    " [" + string.Join(" | ", labels) + "]"
                );
            }
        }

        foreach (string addonName in new[]
        {
            "SelectIconString",
            "TelepotTown",
            "SelectYesno"
        })
        {
            var addon = GetAddon(addonName);

            if (addon == null)
                continue;

            if (addon->AtkValues == null ||
                addon->AtkValuesCount <= 0)
            {
                menus.Add(addonName + " visible (no AtkValues)");
                continue;
            }

            int count = Math.Min((int)addon->AtkValuesCount, 350);
            var labels = new List<string>();

            for (int i = 0; i < count && labels.Count < 26; i++)
            {
                var value = addon->AtkValues[i];

                if (value.Type != AtkValueType.String &&
                    value.Type != AtkValueType.ConstString &&
                    value.Type != AtkValueType.ManagedString)
                    continue;

                var ptr = value.String.Value;

                if (ptr == null)
                    continue;

                try
                {
                    string label = Clean(
                        MemoryHelper.ReadSeStringNullTerminated(
                            (nint)ptr
                        ).TextValue
                    );

                    if (string.IsNullOrWhiteSpace(label))
                        continue;

                    if (label.Length > 100)
                        label = label.Substring(0, 100);

                    labels.Add($"{i}: {label}");
                }
                catch
                {
                    labels.Add($"{i}: unreadable string");
                }
            }

            menus.Add(
                addonName + " values=" +
                addon->AtkValuesCount +
                " [" + string.Join(" | ", labels) + "]"
            );
        }

        if (menus.Count == 0)
        {
            overworldPreviousMenu = "";
            return;
        }

        string detail = string.Join("\n", menus);

        if (detail == overworldPreviousMenu)
            return;

        overworldPreviousMenu = detail;

        string entry =
            $"[{DateTime.Now:HH:mm:ss}] " +
            $"Territory {Client.TerritoryType}; " +
            $"crystal {overworldNearby.Name} " +
            $"(BaseId={overworldNearby.BaseId})\n" +
            detail;

        overworldMenuSnapshots.Add(entry);

        if (overworldMenuSnapshots.Count > 20)
            overworldMenuSnapshots.RemoveAt(0);
    }

    private string BuildOverworldNativeMenuReport()
    {
        var builder = new StringBuilder();

        builder.AppendLine(
            "=== OVERWORLD NATIVE MENU - DEV7 TEST2 READ ONLY ==="
        );

        builder.AppendLine(
            "Current territory: " + Client.TerritoryType
        );

        builder.AppendLine(
            "Nearby source: " +
            (overworldNearby?.Name.ToString() ?? "None")
        );

        builder.AppendLine(
            "Snapshots: " + overworldMenuSnapshots.Count
        );

        foreach (string snapshot in overworldMenuSnapshots)
        {
            builder.AppendLine("---");
            builder.AppendLine(snapshot);
        }

        if (overworldMenuSnapshots.Count == 0)
        {
            builder.AppendLine(
                "No native menu observed. Interact with an overworld crystal."
            );
        }

        return builder.ToString();
    }

    private void DrawOverworldPage()
    {
        if (overworldNearby == null ||
            !overworldStops.TryGetValue(
                Client.TerritoryType, out var stops))
            return;

        ImGui.TextUnformatted(GetAreaName(Client.TerritoryType));
        ImGui.SameLine();
        ImGui.TextDisabled("OVERWORLD - TEST2");

        ImGui.Spacing();

        ImGui.TextColored(
            NavMuted,
            "Read-only research: no gil or travel actions."
        );

        ImGui.TextWrapped(
            "Interact with this crystal normally. " +
            "AethernetAssist records the native menu options."
        );

        ImGui.Spacing();

        var unlocked = Aetherytes.Where(x =>
            x.TerritoryId == Client.TerritoryType &&
            x.SubIndex == 0 &&
            !x.IsSharedHouse &&
            !x.IsApartment
        ).Select(x => x.AetheryteId).ToHashSet();

        var visible = stops.Where(x =>
            config.ShowUnlearned || unlocked.Contains(x.Id)
        ).ToList();

        float height = Math.Clamp(
            visible.Count * 26f + 8f,
            60f,
            440f
        );

        if (ImGui.BeginChild(
            "##OverworldResearch",
            new Vector2(0f, height),
            false))
        {
            foreach (var stop in visible)
            {
                bool current = stop.Id == overworldNearby.BaseId;

                string state = current
                    ? " [CURRENT]"
                    : unlocked.Contains(stop.Id)
                        ? " [ATTUNED]"
                        : " [LOCKED]";

                ImGui.TextUnformatted(stop.Name + state);
            }
        }

        ImGui.EndChild();

        ImGui.Separator();

        ImGui.TextDisabled(
            $"Captured menu states: {overworldMenuSnapshots.Count}"
        );

        if (ImGui.Button("Copy overworld diagnostics"))
        {
            ImGui.SetClipboardText(
                BuildOverworldNativeMenuReport()
            );
        }

        ImGui.SameLine();

        if (ImGui.SmallButton("Clear##overworld-capture"))
        {
            overworldMenuSnapshots.Clear();
            overworldPreviousMenu = "";
        }
    }
}