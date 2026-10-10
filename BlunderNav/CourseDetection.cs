using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;

namespace BlunderNav;

public sealed partial class Plugin
{
    // Map recognition never reads recorded route geometry.
    //
    // Sources:
    // - Client territory
    // - Client MapId
    // - Current player position
    // - Stage-specific object signatures
    //
    // Map IDs may be shared. They are NOT treated as
    // definitive without supporting observations.

    private DateTime lastCourseScan;

    private uint lastDetectionTerritory;
    private uint lastDetectionMapId;

    private int detectedMapSlot = -1;
    private int candidateCourse = -1;
    private int candidateSamples;
    private int teachingSlot = 1;

    private string courseDetectionStatus =
        "Waiting for map identification.";

    private string diagnosticReport = "";

    private string DetectedMapName =>
        Client.TerritoryType == 1197
            ? "Blunderville Lobby"
            : detectedMapSlot >= 1 &&
              detectedMapSlot < Names.Length
                ? Names[detectedMapSlot]
                : "Unknown Fall Guys course";

    private void ResetCourseCandidate()
    {
        candidateCourse = -1;
        candidateSamples = 0;
    }

    private HashSet<string> CollectMapObjects(
        Vector3 position)
    {
        var keys = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var obj in Objects)
        {
            if (obj == null ||
                obj.ObjectKind == ObjectKind.Pc ||
                obj.DataId == 0)
                continue;

            if (Vector3.DistanceSquared(
                position, obj.Position) > 120f * 120f)
                continue;

            // Stable across sessions; excludes actor instance IDs.
            keys.Add(
                $"{(int)obj.ObjectKind}:{obj.DataId}");
        }

        return keys;
    }

    private int IdentifyCourse(
        Vector3 position,
        out string evidence)
    {
        evidence = "";

        if (Client.TerritoryType == 1197)
        {
            evidence = "Blunderville lobby territory.";
            return 0;
        }

        if (Client.TerritoryType != 1165)
        {
            evidence = "Outside Fall Guys event duty.";
            return -1;
        }

        // Established Stage 3 location from vFallguy.
        // Other stages require observed game signatures.
        if (position.X >= -40f &&
            position.X <= 40f &&
            position.Z >= 100f &&
            position.Z <= 350f)
        {
            evidence = "Manderville Mountain arena location.";
            return 5;
        }

        var live = CollectMapObjects(position);

        var samples = config.CourseSignatures
            .Where(s =>
                s != null &&
                s.Slot >= 1 &&
                s.Slot < Names.Length &&
                s.ObjectKeys != null &&
                (s.MapId == 0 ||
                 Client.MapId == 0 ||
                 s.MapId == Client.MapId))
            .ToList();

        if (samples.Count == 0)
        {
            evidence =
                $"Map ID {Client.MapId}; " +
                "no learned signatures for this area.";
            return -1;
        }

        int bestSlot = -1;
        float bestScore = 0f;
        float secondScore = 0f;
        int bestHits = 0;

        foreach (var sample in samples)
        {
            // Ignore object IDs also associated with
            // another known course. They are not useful
            // for distinguishing the stages.

            var competingKeys = new HashSet<string>(
                config.CourseSignatures
                    .Where(s =>
                        s != null &&
                        s.Slot != sample.Slot &&
                        s.ObjectKeys != null)
                    .SelectMany(s => s.ObjectKeys),
                StringComparer.Ordinal);

            var uniqueKeys = sample.ObjectKeys
                .Distinct(StringComparer.Ordinal)
                .Where(k => !competingKeys.Contains(k))
                .ToArray();

            if (uniqueKeys.Length < 3)
                continue;

            int hits = uniqueKeys.Count(live.Contains);

            float coverage =
                (float)hits / uniqueKeys.Length;

            // Reject weak or incidental matches.

            if (hits < 3 || coverage < 0.20f)
                continue;

            float score = hits + coverage * 2f;

            if (score > bestScore)
            {
                if (bestSlot >= 0 &&
                    bestSlot != sample.Slot)
                {
                    secondScore = bestScore;
                }

                bestSlot = sample.Slot;
                bestScore = score;
                bestHits = hits;
            }
            else if (sample.Slot != bestSlot &&
                     score > secondScore)
            {
                secondScore = score;
            }
        }

        if (bestSlot < 0)
        {
            evidence =
                $"Map ID {Client.MapId}; " +
                "no sufficiently strong object match.";
            return -1;
        }

        if (secondScore > 0 &&
            bestScore - secondScore < 2.5f)
        {
            evidence =
                "Ambiguous object signatures.";
            return -1;
        }

        evidence =
            $"{bestHits} distinctive object IDs matched; " +
            $"Map ID {Client.MapId}.";

        return bestSlot;
    }

    private void ApplyDetectedMap(int slot)
    {
        if (!config.AutoSelectCourse ||
            config.AutoRecord ||
            guideRunning ||
            slot < 0 ||
            slot >= Names.Length ||
            config.SelectedSlot == slot)
        {
            return;
        }

        config.SelectedSlot = slot;
        Save();

        ResetSuggestion();
        splatoon.Clear();

        status =
            $"Map identified: {DetectedMapName}. " +
            "Matching route profile selected.";
    }

    private void UpdateCourseSelection(
        Vector3 position,
        DateTime now)
    {
        if ((now - lastCourseScan).TotalMilliseconds < 1000)
            return;

        lastCourseScan = now;

        uint territory = Client.TerritoryType;
        uint mapId = Client.MapId;

        if (lastDetectionTerritory != territory ||
            lastDetectionMapId != mapId)
        {
            lastDetectionTerritory = territory;
            lastDetectionMapId = mapId;

            detectedMapSlot = -1;
            ResetCourseCandidate();
        }

        int result = IdentifyCourse(
            position, out string evidence);

        if (result == -1)
        {
            detectedMapSlot = -1;
            ResetCourseCandidate();

            courseDetectionStatus =
                "Unidentified: " + evidence;

            return;
        }

        if (detectedMapSlot == result)
        {
            courseDetectionStatus =
                $"Detected: {DetectedMapName}. {evidence}";

            ApplyDetectedMap(result);
            return;
        }

        if (candidateCourse != result)
        {
            candidateCourse = result;
            candidateSamples = 1;
        }
        else
        {
            candidateSamples++;
        }

        courseDetectionStatus =
            $"Checking: {(result == 0 ? "Lobby" : Names[result])} " +
            $"({candidateSamples}/3). {evidence}";

        if (candidateSamples < 3)
            return;

        detectedMapSlot = result;
        ResetCourseCandidate();

        courseDetectionStatus =
            $"Detected: {DetectedMapName}. {evidence}";

        ApplyDetectedMap(result);
    }

    private void TeachCurrentMap(Vector3 position)
    {
        if (Client.TerritoryType != 1165)
        {
            status = "Enter a Fall Guys event course first.";
            return;
        }

        if (teachingSlot < 1 ||
            teachingSlot >= Names.Length)
        {
            status = "Select the actual course name first.";
            return;
        }

        var keys = CollectMapObjects(position)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        if (keys.Count < 3)
        {
            status =
                "Too few identifiable objects. " +
                "Move further into the active course.";
            return;
        }

        config.CourseSignatures ??= new();

        config.CourseSignatures.Add(
            new CourseSignature
            {
                Slot = teachingSlot,
                MapId = Client.MapId,
                ObjectKeys = keys
            });

        // Keep up to 6 observations per map.
        var same = config.CourseSignatures
            .Where(s => s.Slot == teachingSlot)
            .ToList();

        while (same.Count > 6)
        {
            config.CourseSignatures.Remove(same[0]);
            same.RemoveAt(0);
        }

        Save();
        ResetCourseCandidate();

        status =
            $"Learned {keys.Count} object IDs for " +
            $"{Names[teachingSlot]} (Map ID {Client.MapId}).";

        courseDetectionStatus =
            "Learning saved. Automatic recognition will " +
            "be evaluated from live evidence.";
    }

    private string CaptureCourseDiagnostics(Vector3 position)
    {
        var report = new StringBuilder();

        report.AppendLine("=== BLUNDERNAV DEV5 FIX1 ===");
        report.AppendLine($"UTC: {DateTime.UtcNow:O}");
        report.AppendLine($"Territory: {Client.TerritoryType}");
        report.AppendLine($"Map ID: {Client.MapId}");
        report.AppendLine($"Detected map: {DetectedMapName}");
        report.AppendLine(
            $"Detection: {courseDetectionStatus}");

        report.AppendLine(
            $"Player XYZ: {position.X:F2}, " +
            $"{position.Y:F2}, {position.Z:F2}");

        report.AppendLine(
            $"Route profile: {Names[config.SelectedSlot]}");

        report.AppendLine(
            $"Auto-load route: {config.AutoSelectCourse}");

        report.AppendLine(
            $"Saved map signatures: " +
            $"{config.CourseSignatures.Count}");

        report.AppendLine();
        report.AppendLine("=== OBJECT SIGNATURE ===");

        foreach (var key in CollectMapObjects(position)
            .OrderBy(k => k, StringComparer.Ordinal))
        {
            report.AppendLine(key);
        }

        report.AppendLine();
        report.AppendLine("=== NEARBY NON-PLAYER OBJECTS ===");

        var nearby = Objects
            .Where(obj =>
                obj != null &&
                obj.ObjectKind != ObjectKind.Pc)
            .Select(obj => new
            {
                Object = obj,
                Distance = Vector3.Distance(
                    position, obj.Position)
            })
            .Where(entry => entry.Distance <= 120f)
            .OrderBy(entry => entry.Distance)
            .Take(80);

        foreach (var entry in nearby)
        {
            var obj = entry.Object;
            var p = obj.Position;

            report.AppendLine(
                $"{entry.Distance:F1}y | " +
                $"Kind={obj.ObjectKind} | " +
                $"DataID={obj.DataId} | " +
                $"Name={obj.Name} | " +
                $"XYZ={p.X:F2},{p.Y:F2},{p.Z:F2}");
        }

        report.AppendLine();
        report.AppendLine("=== OBSERVED CASTS ===");

        foreach (var cast in casts.Take(30))
            report.AppendLine(cast);

        report.AppendLine();
        report.AppendLine(
            "No hazard geometry has been inferred.");

        return report.ToString();
    }

    private void DrawCourseDetection(Vector3 position)
    {
        ImGui.Separator();
        ImGui.TextUnformatted("FALL GUYS MAP DETECTION");

        ImGui.TextUnformatted(
            $"Detected map: {DetectedMapName}");

        ImGui.TextUnformatted(
            $"Territory: {Client.TerritoryType} | " +
            $"Map ID: {Client.MapId}");

        ImGui.TextWrapped(courseDetectionStatus);

        bool autoLoad = config.AutoSelectCourse;

        if (ImGui.Checkbox(
            "Auto-load route for detected map",
            ref autoLoad))
        {
            config.AutoSelectCourse = autoLoad;
            Save();
        }

        ImGui.TextWrapped(
            "Map identification runs independently of " +
            "recorded routes. Only confirmed map detections " +
            "can automatically select a route profile.");

        if (ImGui.CollapsingHeader(
            "Map identification calibration"))
        {
            ImGui.TextWrapped(
                "For unidentified maps, choose the map " +
                "you are actually playing and capture its " +
                "object signature. Repeat in different areas " +
                "to improve recognition.");

            ImGui.Combo(
                "This map is...",
                ref teachingSlot,
                Names,
                Names.Length);

            if (ImGui.Button("Teach current map"))
                TeachCurrentMap(position);

            ImGui.TextUnformatted(
                $"Saved observations: " +
                $"{config.CourseSignatures.Count}");

            if (ImGui.Button("Capture diagnostic snapshot"))
            {
                diagnosticReport =
                    CaptureCourseDiagnostics(position);

                status = "Diagnostic snapshot captured.";
            }

            if (diagnosticReport.Length > 0)
            {
                if (ImGui.Button("Copy diagnostic report"))
                {
                    ImGui.SetClipboardText(diagnosticReport);
                    status = "Report copied to clipboard.";
                }

                if (ImGui.TreeNode("Diagnostic preview"))
                {
                    foreach (var line in diagnosticReport
                        .Split('\n').Take(25))
                    {
                        ImGui.TextUnformatted(
                            line.TrimEnd('\r'));
                    }

                    ImGui.TreePop();
                }
            }
        }
    }
}