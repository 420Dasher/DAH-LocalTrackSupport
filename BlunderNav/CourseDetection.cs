using System;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;

namespace BlunderNav;

public sealed partial class Plugin
{
    // Conservative learning-based course selection.
    //
    // Territory 1165 contains several stages.
    // Without known game-object signatures, we can only
    // identify a route by proximity to saved recordings.
    //
    // When route geometries overlap or no recording exists,
    // leave the current profile unchanged.

    private DateTime lastCourseScan;
    private int candidateCourse = -1;
    private int candidateSamples;

    private string courseDetectionStatus =
        "Waiting for a recorded route near your position.";

    private string diagnosticReport = "";

    private const float MaximumDetectionDistance = 8f;
    private const float MinimumCandidateSeparation = 4f;

    private static float DistanceToRecordedRoute(
        RecordedRoute route,
        Vector3 position)
    {
        if (route.Points == null || route.Points.Count < 2)
            return float.MaxValue;

        FindNearestUpcomingCheckpoint(
            route,
            position,
            out float distance);

        return distance;
    }

    private void ResetCourseCandidate()
    {
        candidateCourse = -1;
        candidateSamples = 0;
    }

    private void UpdateCourseSelection(
        Vector3 position,
        DateTime now)
    {
        if ((now - lastCourseScan).TotalMilliseconds < 1000)
            return;

        lastCourseScan = now;

        if (!config.AutoSelectCourse)
        {
            courseDetectionStatus = "Manual profile override active.";
            ResetCourseCandidate();
            return;
        }

        if (config.AutoRecord)
        {
            courseDetectionStatus =
                "Course selection paused during recording.";
            ResetCourseCandidate();
            return;
        }

        ushort territory = (ushort)Client.TerritoryType;

        int bestSlot = -1;
        float bestDistance = float.MaxValue;
        float secondDistance = float.MaxValue;

        for (int slot = 0; slot < Names.Length; slot++)
        {
            string routeName = Names[slot];

            foreach (var route in config.Routes)
            {
                if (route == null ||
                    route.Territory != territory ||
                    route.Name != routeName)
                    continue;

                float distance = DistanceToRecordedRoute(
                    route, position);

                if (distance < bestDistance)
                {
                    secondDistance = bestDistance;
                    bestDistance = distance;
                    bestSlot = slot;
                }
                else if (distance < secondDistance)
                {
                    secondDistance = distance;
                }
            }
        }

        if (bestSlot < 0)
        {
            courseDetectionStatus =
                "Unknown: no recorded route for this territory.";
            ResetCourseCandidate();
            return;
        }

        if (bestDistance > MaximumDetectionDistance)
        {
            courseDetectionStatus =
                $"No confident match. Closest route: " +
                $"{Names[bestSlot]} ({bestDistance:F1}y away).";
            ResetCourseCandidate();
            return;
        }

        if (secondDistance < float.MaxValue &&
            secondDistance - bestDistance <
                MinimumCandidateSeparation)
        {
            courseDetectionStatus =
                $"Ambiguous routes near {Names[bestSlot]}. " +
                "Manual selection available.";

            ResetCourseCandidate();
            return;
        }

        if (config.SelectedSlot == bestSlot)
        {
            courseDetectionStatus =
                $"Matched: {Names[bestSlot]} " +
                $"({bestDistance:F1}y from recorded route).";

            ResetCourseCandidate();
            return;
        }

        if (candidateCourse != bestSlot)
        {
            candidateCourse = bestSlot;
            candidateSamples = 1;
        }
        else
        {
            candidateSamples++;
        }

        courseDetectionStatus =
            $"Candidate: {Names[bestSlot]} " +
            $"({candidateSamples}/3 checks, " +
            $"{bestDistance:F1}y away).";

        // Require a stable match across three separate scans
        // before changing any profile.

        if (candidateSamples < 3)
            return;

        if (guideRunning)
        {
            StopGuide(
                "Course profile changed. Restart visual guidance.");
        }

        config.SelectedSlot = bestSlot;
        Save();

        ResetSuggestion();
        splatoon.Clear();
        ResetCourseCandidate();

        courseDetectionStatus =
            $"Auto-selected: {Names[bestSlot]}.";

        status =
            $"Course detected: {Names[bestSlot]}. " +
            "Visual guidance can now be started.";
    }

    private string CaptureCourseDiagnostics(Vector3 position)
    {
        var report = new StringBuilder();

        report.AppendLine("=== BLUNDERNAV DEV5 DIAGNOSTICS ===");
        report.AppendLine($"UTC: {DateTime.UtcNow:O}");
        report.AppendLine(
            $"Territory: {Client.TerritoryType}");

        report.AppendLine(
            $"Player XYZ: " +
            $"{position.X:F3}, " +
            $"{position.Y:F3}, " +
            $"{position.Z:F3}");

        report.AppendLine(
            $"Selected profile: {Names[config.SelectedSlot]}");

        report.AppendLine(
            $"Automatic selection: {config.AutoSelectCourse}");

        report.AppendLine(
            $"Course status: {courseDetectionStatus}");

        report.AppendLine();
        report.AppendLine("=== RECORDED PROFILES ===");

        foreach (var route in config.Routes)
        {
            if (route == null || route.Points == null)
                continue;

            report.AppendLine(
                $"{route.Name} | " +
                $"Territory={route.Territory} | " +
                $"Points={route.Points.Count}");

            if (route.Points.Count > 0)
            {
                Vector3 first = route.Points[0].Position;

                report.AppendLine(
                    $"  Start: {first.X:F2}, " +
                    $"{first.Y:F2}, {first.Z:F2}");
            }
        }

        report.AppendLine();
        report.AppendLine(
            "=== NEARBY GAME OBJECTS (UP TO 60, 80Y) ===");

        var nearby = Objects
            .Where(obj => obj != null)
            .Select(obj => new
            {
                Object = obj,
                Distance = Vector3.Distance(
                    position, obj.Position)
            })
            .Where(entry => entry.Distance <= 80f)
            .OrderBy(entry => entry.Distance)
            .Take(60)
            .ToList();

        foreach (var entry in nearby)
        {
            var obj = entry.Object;
            var p = obj.Position;

            report.AppendLine(
                $"Distance={entry.Distance:F1}y | " +
                $"Kind={obj.ObjectKind} | " +
                $"DataID={obj.DataId} | " +
                $"ObjectID=0x{obj.GameObjectId:X} | " +
                $"Name={obj.Name} | " +
                $"XYZ={p.X:F2},{p.Y:F2},{p.Z:F2}");
        }

        report.AppendLine();
        report.AppendLine(
            "=== OBSERVED CAST ACTIONS (UP TO 30) ===");

        foreach (var cast in casts.Take(30))
            report.AppendLine(cast);

        report.AppendLine();
        report.AppendLine(
            "Observations are not verified hazard definitions.");

        return report.ToString();
    }

    private void DrawCourseDetection(Vector3 position)
    {
        ImGui.Separator();
        ImGui.TextUnformatted("AUTOMATIC COURSE SELECTION");

        bool autoSelect = config.AutoSelectCourse;

        if (ImGui.Checkbox(
            "Select course automatically",
            ref autoSelect))
        {
            config.AutoSelectCourse = autoSelect;
            ResetCourseCandidate();
            Save();
        }

        ImGui.TextWrapped(courseDetectionStatus);

        ImGui.TextWrapped(
            "Uses territory and recorded-route locations. " +
            "Ambiguous or unrecorded courses are not guessed.");

        if (!config.AutoSelectCourse)
        {
            ImGui.TextWrapped(
                "Manual override enabled. Check the option " +
                "above to resume automatic selection.");
        }

        if (ImGui.CollapsingHeader(
            "DEV5 - Object and mechanic diagnostics"))
        {
            ImGui.TextWrapped(
                "Capture nearby game objects and observed " +
                "casts to help identify Fall Guys stages " +
                "and obstacle IDs. No hazard shapes are " +
                "automatically inferred yet.");

            if (ImGui.Button("Capture nearby objects"))
            {
                diagnosticReport = CaptureCourseDiagnostics(
                    position);

                status = "Diagnostic snapshot captured.";
            }

            if (diagnosticReport.Length > 0)
            {
                if (ImGui.Button("Copy diagnostic report"))
                {
                    ImGui.SetClipboardText(diagnosticReport);
                    status = "Diagnostics copied to clipboard.";
                }

                ImGui.TextUnformatted(
                    $"Report length: {diagnosticReport.Length} characters");

                if (ImGui.TreeNode("Diagnostic preview"))
                {
                    foreach (var line in diagnosticReport
                        .Split('\n').Take(20))
                    {
                        ImGui.TextUnformatted(line.TrimEnd('\r'));
                    }

                    ImGui.TreePop();
                }
            }
        }
    }
}