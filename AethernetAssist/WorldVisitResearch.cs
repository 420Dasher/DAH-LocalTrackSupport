using System;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AethernetAssist;

public sealed unsafe partial class Plugin
{
    // DEV4.3 WORLD VISIT RESEARCH1
    //
    // World Visit is restricted to three city Aetherytes.
    // Diagnostic-only. Does not submit callbacks.
    //
    // Cross-DC travel is intentionally unsupported.

    private bool WorldVisitPageAvailable()
    {
        if (Client.TerritoryType is not (129 or 130 or 132))
            return false;

        // This already checks for an actual targetable
        // main Aetheryte within configured proximity.
        // It excludes ordinary Aethernet shards.
        return TryFindHousingAetheryte(
            out _, out var crystal) &&
            crystal != null;
    }

    private void DrawWorldVisitResearchPage()
    {
        ImGui.Spacing();

        ImGui.TextColored(
            NavAccent, "SAME-DC WORLD VISIT");

        ImGui.TextDisabled(
            "DEV4.3 RESEARCH1 - No automatic travel yet");

        ImGui.Spacing();

        ImGui.TextWrapped(
            "Use the main Aetheryte to open " +
            "'Visit Another World'. The plugin will " +
            "inspect the native destination menu.");

        ImGui.Spacing();

        bool menuOpen =
            GetAddon("WorldTravelSelect") != null;

        ImGui.TextColored(
            menuOpen ? NavReady : NavMuted,
            menuOpen
                ? "World selection menu detected"
                : "Waiting for World Visit menu");

        ImGui.Spacing();

        if (ImGui.Button("Copy World Visit diagnostics"))
        {
            ImGui.SetClipboardText(
                BuildWorldVisitResearchReport());
        }

        ImGui.Spacing();

        ImGui.TextDisabled(
            "Only worlds on your current data center.");
    }

    private static void AppendWorldVisitNodes(
        AtkUldManager* manager,
        StringBuilder output,
        ref int captured,
        int depth = 0)
    {
        if (manager == null ||
            manager->NodeList == null ||
            depth > 4 ||
            captured >= 80)
            return;

        for (int i = 0;
             i < manager->NodeListCount &&
             captured < 80;
             i++)
        {
            var node = manager->NodeList[i];

            if (node == null)
                continue;

            if (node->Type == NodeType.Text)
            {
                var text = ((AtkTextNode*)node)
                    ->NodeText.ToString().Trim();

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (text.Length > 180)
                    text = text[..180];

                output.AppendLine(
                    "Text node: " + text);

                captured++;
            }
            else if ((int)node->Type > 1000)
            {
                var component =
                    ((AtkComponentNode*)node)->Component;

                if (component != null)
                {
                    AppendWorldVisitNodes(
                        &component->UldManager,
                        output,
                        ref captured,
                        depth + 1);
                }
            }
        }
    }

    private string BuildWorldVisitResearchReport()
    {
        var output = new StringBuilder();

        output.AppendLine();
        output.AppendLine(
            "=== SAME-DC WORLD VISIT RESEARCH1 ===");

        output.AppendLine(
            "Territory: " + Client.TerritoryType);

        output.AppendLine(
            "Supported location: " +
            (Client.TerritoryType is 129 or 130 or 132));

        output.AppendLine(
            "Main Aetheryte nearby: " +
            WorldVisitPageAvailable());

        output.AppendLine();
        output.AppendLine(
            "=== WORLD VISIT NATIVE ADDONS ===");

        foreach (var name in new[]
        {
            "SelectString",
            "WorldTravelSelect",
            "SelectYesno",
            "WorldTravelFinderReady",
            "WorldTravelFinder"
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
            "=== AETHERYTE SELECTSTRING MENU ===");

        var selector =
            (AddonSelectString*)GetAddon("SelectString");

        if (selector != null)
        {
            var menu = selector->PopupMenu.PopupMenu;

            if (menu.EntryNames != null)
            {
                int count =
                    Math.Clamp(menu.EntryCount, 0, 32);

                for (int i = 0; i < count; i++)
                {
                    var ptr = menu.EntryNames[i].Value;

                    if (ptr == null)
                        continue;

                    string name =
                        Marshal.PtrToStringUTF8(
                            (nint)ptr) ?? "";

                    output.AppendLine(
                        i + ": " + Clean(name));
                }
            }
        }
        else
        {
            output.AppendLine(
                "SelectString is not visible.");
        }

        output.AppendLine();
        output.AppendLine(
            "=== WORLDTRAVELSELECT ATKVALUES ===");

        var travel = GetAddon("WorldTravelSelect");

        if (travel == null ||
            travel->AtkValues == null)
        {
            output.AppendLine(
                "Open Visit Another World manually.");
        }
        else
        {
            int count = travel->AtkValuesCount;

            output.AppendLine(
                "AtkValuesCount: " + count);

            var values = travel->AtkValues;

            for (int i = 0;
                 i < Math.Min(96, count);
                 i++)
            {
                var value = values[i];

                bool isText =
                    value.Type == AtkValueType.String ||
                    value.Type == AtkValueType.ConstString ||
                    value.Type == AtkValueType.ManagedString ||
                    value.Type == AtkValueType.WideString;

                if (isText)
                {
                    if (value.String.Value == null)
                        continue;

                    try
                    {
                        string name =
                            MemoryHelper
                            .ReadSeStringNullTerminated(
                                (nint)value.String.Value)
                            .TextValue;

                        name = Clean(name);

                        if (name.Length > 180)
                            name = name[..180];

                        if (name.Length > 0)
                        {
                            output.AppendLine(
                                "[" + i + "] " +
                                value.Type + " = " + name);
                        }
                    }
                    catch (Exception ex)
                    {
                        output.AppendLine(
                            "[" + i + "] decode error: " +
                            ex.Message);
                    }
                }
                else if (i < 32)
                {
                    output.AppendLine(
                        "[" + i + "] " +
                        value.Type +
                        " | UInt=" + value.UInt);
                }
            }

            output.AppendLine();
            output.AppendLine(
                "=== WORLDTRAVELSELECT VISIBLE TEXT ===");

            int captured = 0;

            AppendWorldVisitNodes(
                &travel->UldManager,
                output,
                ref captured);

            output.AppendLine(
                "Text nodes captured: " + captured);
        }

        output.AppendLine();
        output.AppendLine(
            "=== RESEARCH STATUS ===");
        output.AppendLine(
            "Read-only. No native callbacks submitted.");
        output.AppendLine(
            "No cross-DC travel or character selection.");

        return output.ToString();
    }
}