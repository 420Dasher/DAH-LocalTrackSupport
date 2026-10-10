using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace BlunderNav;

public sealed partial class Plugin
{
    private void DrawCompactUi()
    {
        if (!windowOpen)
            return;

        ImGui.SetNextWindowSize(
            new Vector2(505f, 435f),
            ImGuiCond.FirstUseEver);

        if (!ImGui.Begin(
            "BlunderNav | DEV5",
            ref windowOpen,
            ImGuiWindowFlags.None))
        {
            ImGui.End();
            return;
        }

        try
        {
            ImGui.TextUnformatted("BLUNDERNAV");
            ImGui.SameLine();
            ImGui.TextDisabled("DEV5 FIX2  |  0.0.12");

            ImGui.TextUnformatted(DetectedMapName);

            ImGui.TextDisabled(
                $"Map {Client.MapId}  |  " +
                $"Splatoon: {splatoon.Status}  |  " +
                $"vnavmesh: {(navAvailable ? "Ready" : "Offline")}");

            ImGui.Separator();

            if (!InArea || Objects.LocalPlayer == null)
            {
                ImGui.TextWrapped(
                    "Enter Blunderville to use visual guidance.");
                return;
            }

            Vector3 position = Objects.LocalPlayer.Position;

            if (ImGui.BeginTabBar("##bnav_tabs"))
            {
                if (ImGui.BeginTabItem("Guide"))
                {
                    DrawGuideTab(position);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Routes"))
                {
                    DrawRoutesTab(position);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Diagnostics"))
                {
                    DrawDiagnosticsTab(position);
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            ImGui.Separator();
            ImGui.TextWrapped(status);

            ImGui.TextDisabled(
                "Visual guidance only  |  /bnav");
        }
        finally
        {
            ImGui.End();
        }
    }

    private void DrawGuideTab(Vector3 position)
    {
        ImGui.Spacing();

        ImGui.TextDisabled("CURRENT LOCATION");
        ImGui.TextWrapped(courseDetectionStatus);

        bool autoLoad = config.AutoSelectCourse;

        if (ImGui.Checkbox(
            "Auto-select route for detected map",
            ref autoLoad))
        {
            config.AutoSelectCourse = autoLoad;
            Save();
        }

        if (IsWaitingRoom)
        {
            ImGui.Separator();
            ImGui.TextUnformatted("Waiting for the next round.");
            ImGui.TextDisabled(
                "No course route is displayed here.");
            return;
        }

        if (Client.TerritoryType == 1165 &&
            detectedMapSlot < 1)
        {
            ImGui.TextWrapped(
                "Unknown map: route guidance is paused.");
            return;
        }

        var route = CurrentRoute();

        ImGui.Separator();
        ImGui.TextDisabled("ACTIVE ROUTE");

        ImGui.TextUnformatted(
            Names[config.SelectedSlot]);

        ImGui.TextDisabled(
            $"{route.Points.Count} recorded checkpoints");

        if (detectedMapSlot >= 1 &&
            config.SelectedSlot != detectedMapSlot)
        {
            ImGui.TextWrapped(
                "Selected route differs from detected map. " +
                "Enable auto-selection or choose the correct " +
                "profile under Routes.");
        }

        ImGui.Spacing();

        if (!guideRunning)
        {
            if (ImGui.Button("Start guidance"))
                StartGuide();
        }
        else
        {
            ImGui.TextUnformatted(
                $"Next checkpoint: " +
                $"{nextIndex + 1}/{route.Points.Count}");

            if (ImGui.Button("Rejoin"))
                RejoinGuide(position);

            ImGui.SameLine();

            if (ImGui.Button("Stop guidance"))
                StopGuide("Guidance stopped manually.");
        }

        ImGui.Spacing();

        bool visible = showOverlay;

        if (ImGui.Checkbox("Show overlay", ref visible))
        {
            showOverlay = visible;

            if (!visible)
                splatoon.Clear();
        }

        ImGui.TextDisabled(
            "Cyan: recording  |  Green: suggested path");

        if (!navAvailable)
        {
            ImGui.TextWrapped(
                "vnavmesh is unavailable. Recorded-route " +
                "visualization still works.");
        }
    }

    private void DrawRoutesTab(Vector3 position)
    {
        if (IsWaitingRoom)
        {
            ImGui.TextUnformatted(
                "Route editing is paused in the waiting room.");
            return;
        }

        ImGui.Spacing();

        int slot = config.SelectedSlot;

        if (guideRunning)
            ImGui.BeginDisabled();

        if (ImGui.Combo(
            "Route profile",
            ref slot,
            Names,
            Names.Length))
        {
            config.SelectedSlot = slot;
            config.AutoSelectCourse = false;
            config.AutoRecord = false;

            clearArmed = false;

            ResetSuggestion();
            splatoon.Clear();
            Save();

            status =
                "Manual route selected. Automatic route " +
                "selection disabled.";
        }

        if (guideRunning)
            ImGui.EndDisabled();

        var route = CurrentRoute();

        ImGui.TextDisabled(
            $"Checkpoints: {route.Points.Count}/250");

        ImGui.Separator();
        ImGui.TextDisabled("RECORDING");

        if (guideRunning)
            ImGui.BeginDisabled();

        if (ImGui.Button("Add checkpoint"))
            Record(position, false);

        ImGui.SameLine();

        if (ImGui.Button("Undo last") &&
            route.Points.Count > 0)
        {
            route.Points.RemoveAt(route.Points.Count - 1);
            Save();
        }

        bool recording = config.AutoRecord;

        if (ImGui.Checkbox("Auto record", ref recording))
        {
            config.AutoRecord = recording;
            Save();
        }

        float spacing = config.RecordSpacing;

        if (ImGui.SliderFloat(
            "Spacing (yalms)",
            ref spacing,
            2f,
            10f))
        {
            config.RecordSpacing = spacing;
            Save();
        }

        if (route.Points.Count > 0)
        {
            bool hold = route.Points[^1].Hold;

            if (ImGui.Checkbox(
                "Hold at last checkpoint",
                ref hold))
            {
                route.Points[^1].Hold = hold;
                Save();
            }
        }

        ImGui.Spacing();

        if (!clearArmed)
        {
            if (ImGui.Button("Clear route..."))
                clearArmed = true;
        }
        else
        {
            if (ImGui.Button("Confirm clear"))
            {
                route.Points.Clear();
                config.AutoRecord = false;
                clearArmed = false;

                Save();
                splatoon.Clear();

                status = "Route cleared.";
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel"))
                clearArmed = false;
        }

        if (guideRunning)
            ImGui.EndDisabled();

        ImGui.Separator();
        ImGui.TextDisabled("VISUAL SETTINGS");

        ImGui.Checkbox(
            "Show entire recorded route",
            ref showEntireRoute);

        ImGui.Spacing();

        if (ImGui.TreeNode("Checkpoint list"))
        {
            for (int i = 0; i < route.Points.Count; i++)
            {
                var p = route.Points[i];

                ImGui.TextUnformatted(
                    $"{i + 1:000}  " +
                    $"{p.X:F2}, {p.Y:F2}, {p.Z:F2}" +
                    (p.Hold ? "  [HOLD]" : ""));
            }

            ImGui.TreePop();
        }
    }

    private void DrawDiagnosticsTab(Vector3 position)
    {
        ImGui.Spacing();

        ImGui.TextDisabled("MAP DETECTION");

        ImGui.TextUnformatted(
            $"Territory: {Client.TerritoryType}");

        ImGui.TextUnformatted(
            $"Map ID: {Client.MapId}");

        ImGui.TextUnformatted(DetectedMapName);

        ImGui.TextWrapped(courseDetectionStatus);

        ImGui.TextDisabled(
            $"XYZ: {position.X:F1}, " +
            $"{position.Y:F1}, {position.Z:F1}");

        ImGui.Separator();
        ImGui.TextDisabled("MECHANIC RESEARCH");

        ImGui.TextWrapped(
            "Captures live object IDs and cast actions. " +
            "AoE geometry is not automatically detected yet.");

        if (ImGui.Button("Capture + copy report"))
        {
            diagnosticReport =
                CaptureCourseDiagnostics(position);

            ImGui.SetClipboardText(diagnosticReport);

            status = "Diagnostic report copied.";
        }

        ImGui.SameLine();

        if (ImGui.Button("Clear cast history"))
        {
            casts.Clear();
            previousCasts.Clear();

            status = "Cast history cleared.";
        }

        if (diagnosticReport.Length > 0 &&
            ImGui.TreeNode("Report preview"))
        {
            foreach (var line in diagnosticReport
                .Split('\n').Take(22))
            {
                ImGui.TextUnformatted(
                    line.TrimEnd('\r'));
            }

            ImGui.TreePop();
        }

        if (ImGui.TreeNode(
            $"Recent casts ({casts.Count})"))
        {
            foreach (var cast in casts.Take(15))
                ImGui.TextUnformatted(cast);

            ImGui.TreePop();
        }

        ImGui.Separator();

        if (ImGui.CollapsingHeader(
            "Manual test hazard (experimental)"))
        {
            ImGui.TextWrapped(
                "Manual test circle only. " +
                "Not a detected Fall Guys AoE.");

            ImGui.SliderFloat(
                "Radius",
                ref hazardRadius,
                1f,
                8f);

            if (ImGui.Button("Place test circle"))
            {
                hazardCenter = position;
                hazardTerritory = Client.TerritoryType;
                hazardEnabled = true;

                ResetSuggestion();

                status = "Manual test circle placed.";
            }

            if (hazardEnabled)
            {
                ImGui.SameLine();

                if (ImGui.Button("Remove test circle"))
                {
                    hazardEnabled = false;
                    ResetSuggestion();

                    status = "Test circle removed.";
                }
            }

            ImGui.TextDisabled(
                "Uses experimental read-only " +
                "vnavmesh avoidance.");
        }
    }
}