using System;
using System.Text;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AethernetAssist;

public sealed unsafe partial class Plugin
{
    // DEV4.2 RESEARCH3
    // On-demand, read-only native UI inspection.
    // No callbacks, clicks or memory modification.

    private string BuildCosmicPlanetResearchReport()
    {
        var report = new StringBuilder();

        report.AppendLine();
        report.AppendLine(
            "=== WKSPLANETSELECT DIRECT INSPECTION ===");

        report.AppendLine(
            "UTC: " + DateTime.UtcNow.ToString("O"));

        var addon = GetAddon("WKSPlanetSelect");

        if (addon == null)
        {
            report.AppendLine(
                "WKSPlanetSelect is not currently visible.");

            return report.ToString();
        }

        report.AppendLine(
            "Addon: WKSPlanetSelect");

        report.AppendLine(
            "Ready: " + addon->IsReady);

        report.AppendLine(
            "Visible: " + addon->IsVisible);

        report.AppendLine(
            "AtkValuesCount: " + addon->AtkValuesCount);

        report.AppendLine();
        report.AppendLine("=== ATKVALUES ===");

        if (addon->AtkValues == null)
        {
            report.AppendLine("AtkValues pointer is null.");
        }
        else
        {
            int count = Math.Min(
                (int)addon->AtkValuesCount, 64);

            for (int i = 0; i < count; i++)
            {
                try
                {
                    var value = addon->AtkValues[i];

                    string content = value.GetValueAsString();

                    if (content.Length > 240)
                        content = content[..240] + "...";

                    content = content
                        .Replace("\r", " ")
                        .Replace("\n", " ");

                    report.AppendLine(
                        "[" + i + "] " +
                        "Type=" + value.Type +
                        " | Value=" + content);
                }
                catch (Exception ex)
                {
                    report.AppendLine(
                        "[" + i + "] Read failed: " +
                        ex.Message);
                }
            }
        }

        report.AppendLine();
        report.AppendLine("=== NATIVE UI NODES ===");

        var uld = &addon->UldManager;

        if (uld->NodeList == null)
        {
            report.AppendLine("NodeList unavailable.");
        }
        else
        {
            int count = Math.Min(
                (int)uld->NodeListCount, 160);

            report.AppendLine(
                "Total nodes: " + uld->NodeListCount);

            report.AppendLine(
                "Inspecting up to: " + count);

            for (int i = 0; i < count; i++)
            {
                var node = uld->NodeList[i];

                if (node == null)
                    continue;

                string line =
                    "[" + i + "]" +
                    " NodeID=" + node->NodeId +
                    " Type=" + node->Type +
                    " Size=" + node->Width +
                    "x" + node->Height;

                if (node->Type == NodeType.Text)
                {
                    try
                    {
                        var text =
                            ((AtkTextNode*)node)
                                ->NodeText.ToString();

                        if (text.Length > 180)
                            text = text[..180] + "...";

                        text = text
                            .Replace("\r", " ")
                            .Replace("\n", " ");

                        line += " | Text=" + text;
                    }
                    catch (Exception ex)
                    {
                        line +=
                            " | Text read error=" + ex.Message;
                    }
                }

                report.AppendLine(line);
            }
        }

        report.AppendLine();
        report.AppendLine("=== INSPECTION STATUS ===");

        report.AppendLine(
            "Read-only; no native callbacks submitted.");

        report.AppendLine(
            "Change the selected planet manually and " +
            "capture again.");

        return report.ToString();
    }
}