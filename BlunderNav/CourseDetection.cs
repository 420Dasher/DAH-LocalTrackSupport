using System;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Game.ClientState.Objects.Enums;

namespace BlunderNav;

public sealed partial class Plugin
{
    // Verified through live Fall Guys diagnostic captures.
    //
    // 878 Gentlebean's Fever
    // 879 Manderville-can Parade
    // 880 The Gold Swiveller
    // 881 Saucery Siege
    // 882 Manderville Mountain
    // 883 Pre-round waiting room
    //
    // Territory 1197 is the Blunderville hub.
    //
    // Map detection never depends on recorded routes.

    private DateTime lastCourseScan;
    private uint lastDetectionTerritory;
    private uint lastDetectionMapId;

    private int detectedMapSlot = -1;

    private string courseDetectionStatus =
        "Waiting for Fall Guys map data.";

    private string diagnosticReport = "";

    private bool IsWaitingRoom =>
        Client.TerritoryType == 1165 &&
        Client.MapId == 883;

    private int IdentifyCourseMapId()
    {
        if (Client.TerritoryType == 1197)
            return 0;

        if (Client.TerritoryType != 1165)
            return -1;

        return Client.MapId switch
        {
            878 => 1,
            879 => 2,
            880 => 4,
            881 => 3,
            882 => 5,
            883 => -2,
            _ => -1
        };
    }

    private string DetectedMapName =>
        IdentifyCourseMapId() switch
        {
            0 => "Blunderville Hub",
            -2 => "Pre-round Waiting Room",
            1 => Names[1],
            2 => Names[2],
            3 => Names[3],
            4 => Names[4],
            5 => Names[5],
            _ => "Unknown Fall Guys Map"
        };

    private void UpdateCourseSelection(
        Vector3 position,
        DateTime now)
    {
        if ((now - lastCourseScan).TotalMilliseconds < 350)
            return;

        lastCourseScan = now;

        uint territory = Client.TerritoryType;
        uint mapId = Client.MapId;

        bool previousKnown = lastDetectionTerritory != 0;

        bool changed =
            previousKnown &&
            (lastDetectionTerritory != territory ||
             lastDetectionMapId != mapId);

        lastDetectionTerritory = territory;
        lastDetectionMapId = mapId;

        if (changed)
        {
            // Territory 1165 can contain several distinct
            // maps. Reset stage-specific state on Map ID
            // changes even if the territory stays the same.

            if (guideRunning)
            {
                StopGuide(
                    "Map changed. Visual guidance stopped.");
            }

            if (config.AutoRecord)
            {
                config.AutoRecord = false;
                Save();
            }

            hazardEnabled = false;
            clearArmed = false;

            casts.Clear();
            previousCasts.Clear();

            ResetSuggestion();
            splatoon.Clear();
        }

        int result = IdentifyCourseMapId();

        detectedMapSlot = result;

        if (result == -2)
        {
            courseDetectionStatus =
                "Waiting room detected. No course route loaded.";
            return;
        }

        if (result == -1)
        {
            courseDetectionStatus =
                $"Unrecognized map ID {mapId}. " +
                "No automatic route change.";
            return;
        }

        if (result == 0)
        {
            courseDetectionStatus =
                "Blunderville hub detected.";
            return;
        }

        courseDetectionStatus =
            $"Confirmed Map ID {mapId}: {Names[result]}.";

        // Identifying the current map is unconditional.
        // Selecting its saved route is optional.

        if (!config.AutoSelectCourse ||
            config.AutoRecord ||
            guideRunning ||
            config.SelectedSlot == result)
            return;

        config.SelectedSlot = result;
        Save();

        ResetSuggestion();
        splatoon.Clear();

        status =
            $"Detected {Names[result]}. " +
            "Matching route profile selected.";
    }

    private string CaptureCourseDiagnostics(Vector3 position)
    {
        var report = new StringBuilder();

        report.AppendLine(
            "=== BLUNDERNAV DEV5 FIX2 DIAGNOSTICS ===");

        report.AppendLine($"UTC: {DateTime.UtcNow:O}");
        report.AppendLine($"Territory: {Client.TerritoryType}");
        report.AppendLine($"Map ID: {Client.MapId}");
        report.AppendLine($"Detected map: {DetectedMapName}");
        report.AppendLine($"Detection: {courseDetectionStatus}");

        report.AppendLine(
            $"Player XYZ: " +
            $"{position.X:F2}, {position.Y:F2}, {position.Z:F2}");

        report.AppendLine(
            $"Route profile: {Names[config.SelectedSlot]}");

        report.AppendLine(
            $"Auto-load route: {config.AutoSelectCourse}");

        report.AppendLine();
        report.AppendLine("=== NEARBY NON-PLAYER OBJECTS ===");

        var nearby = Objects
            .Where(o =>
                o != null &&
                o.ObjectKind != ObjectKind.Pc)
            .Select(o => new
            {
                Object = o,
                Distance = Vector3.Distance(
                    position, o.Position)
            })
            .Where(x => x.Distance <= 120f)
            .OrderBy(x => x.Distance)
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
        report.AppendLine("=== CURRENT MAP CAST HISTORY ===");

        foreach (var cast in casts.Take(30))
            report.AppendLine(cast);

        report.AppendLine();
        report.AppendLine(
            "No automatic AoE geometry is inferred.");

        return report.ToString();
    }
}