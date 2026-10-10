using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.IoC;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;

namespace BlunderNav;

public sealed partial class Plugin
{
    // DEV6.1 is an observation-only research stage.
    // Objects are NOT assumed to be finish lines or goals.
    // No observed position is automatically navigated to.

    [PluginService]
    private static Dalamud.Plugin.Services.ITargetManager Targets
        { get; set; } = null!;

    private sealed class ObservedWorldObject
    {
        public ulong InstanceId;
        public uint BaseId;
        public string Kind = "";
        public string Name = "";
        public Vector3 FirstPosition;
        public Vector3 LastPosition;
        public float MaximumTravel;
        public float HitboxRadius;
        public bool Targetable;
        public int Sightings;
        public bool WasSelected;
    }

    private readonly Dictionary<ulong, ObservedWorldObject>
        objectiveObjects = new();

    private readonly List<string> objectiveTargetEvents = new();

    private uint objectiveMapId;
    private DateTime objectiveStartedUtc = DateTime.MinValue;
    private DateTime lastObjectiveSampleUtc = DateTime.MinValue;

    private int objectiveSamples;
    private Vector3 firstPlayerPosition;
    private Vector3 minimumPlayerPosition;
    private Vector3 maximumPlayerPosition;
    private float maximumPlayerDistance;
    private ulong lastSelectedTargetId;

    private string ObjectiveSurveyStatus
    {
        get
        {
            if (IdentifyCourseMapId() < 1)
                return "Waiting for an active course.";

            if (objectiveSamples == 0)
                return "Awaiting first survey sample.";

            return
                $"{objectiveSamples} samples, " +
                $"{objectiveObjects.Count} observed objects, " +
                $"{maximumPlayerDistance:F0}y explored from start.";
        }
    }

    private static string FormatPosition(Vector3 value)
    {
        return FormattableString.Invariant(
            $"{value.X:F2}; {value.Y:F2}; {value.Z:F2}");
    }

    private void ResetObjectiveDiscovery()
    {
        objectiveMapId = 0;
        objectiveStartedUtc = DateTime.MinValue;
        lastObjectiveSampleUtc = DateTime.MinValue;

        objectiveSamples = 0;
        maximumPlayerDistance = 0f;
        lastSelectedTargetId = 0;

        objectiveObjects.Clear();
        objectiveTargetEvents.Clear();
    }

    private void UpdateObjectiveDiscovery(
        Vector3 position,
        DateTime now)
    {
        // No tracking in the hub, waiting room or
        // outside the five identified Fall Guys courses.

        if (Client.TerritoryType != 1165 ||
            IdentifyCourseMapId() < 1)
            return;

        uint map = Client.MapId;

        if (objectiveMapId != map)
        {
            ResetObjectiveDiscovery();

            objectiveMapId = map;
            objectiveStartedUtc = now;

            firstPlayerPosition = position;
            minimumPlayerPosition = position;
            maximumPlayerPosition = position;
        }

        if ((now - lastObjectiveSampleUtc).TotalMilliseconds < 750)
            return;

        lastObjectiveSampleUtc = now;
        objectiveSamples++;

        minimumPlayerPosition = Vector3.Min(
            minimumPlayerPosition, position);

        maximumPlayerPosition = Vector3.Max(
            maximumPlayerPosition, position);

        maximumPlayerDistance = MathF.Max(
            maximumPlayerDistance,
            Vector3.Distance(position, firstPlayerPosition));

        // Sample nearby non-player objects. Keep the
        // data bounded so long sessions cannot grow
        // indefinitely.

        foreach (var obj in Objects)
        {
            if (obj == null ||
                obj.ObjectKind == ObjectKind.Pc ||
                obj.GameObjectId == 0)
                continue;

            if (Vector3.DistanceSquared(
                position, obj.Position) > 150f * 150f)
                continue;

            ulong id = obj.GameObjectId;

            if (!objectiveObjects.TryGetValue(
                id, out var observed))
            {
                if (objectiveObjects.Count >= 300)
                    continue;

                observed = new ObservedWorldObject
                {
                    InstanceId = id,
                    BaseId = obj.BaseId,
                    Kind = obj.ObjectKind.ToString(),
                    Name = obj.Name.ToString(),
                    FirstPosition = obj.Position,
                    LastPosition = obj.Position,
                    Targetable = obj.IsTargetable,
                    HitboxRadius = obj.HitboxRadius
                };

                objectiveObjects[id] = observed;
            }

            observed.Sightings++;
            observed.LastPosition = obj.Position;
            observed.Targetable |= obj.IsTargetable;

            observed.MaximumTravel = MathF.Max(
                observed.MaximumTravel,
                Vector3.Distance(
                    observed.FirstPosition, obj.Position));
        }

        // Inspect the player's target read-only.
        // This can reveal interactables that do not
        // have a useful visible name.

        IGameObject? target = Targets.Target;

        if (target == null ||
            target.ObjectKind == ObjectKind.Pc ||
            target.GameObjectId == 0)
        {
            lastSelectedTargetId = 0;
            return;
        }

        ulong targetId = target.GameObjectId;

        if (objectiveObjects.TryGetValue(
            targetId, out var trackedTarget))
        {
            trackedTarget.WasSelected = true;
        }

        if (targetId == lastSelectedTargetId)
            return;

        lastSelectedTargetId = targetId;

        objectiveTargetEvents.Insert(
            0,
            $"{DateTime.UtcNow:HH:mm:ss} UTC | " +
            $"Selected {target.ObjectKind} | " +
            $"BaseID={target.BaseId} | " +
            $"Name={target.Name} | " +
            $"XYZ={FormatPosition(target.Position)}");

        if (objectiveTargetEvents.Count > 25)
        {
            objectiveTargetEvents.RemoveAt(
                objectiveTargetEvents.Count - 1);
        }
    }

    private void AddObjectiveSurveyPoint(
        Vector3 position,
        string label)
    {
        if (Client.TerritoryType != 1165 ||
            IdentifyCourseMapId() < 1)
        {
            status = "Enter an active course first.";
            return;
        }

        config.ObjectiveSurveyPoints ??=
            new List<ObjectiveSurveyPoint>();

        var point = new ObjectiveSurveyPoint
        {
            MapId = Client.MapId,
            Label = label,
            X = position.X,
            Y = position.Y,
            Z = position.Z
        };

        config.ObjectiveSurveyPoints.Add(point);

        // Limit research points to 24 per map.

        var sameMap = config.ObjectiveSurveyPoints
            .Where(p => p.MapId == Client.MapId)
            .ToList();

        if (sameMap.Count > 24)
        {
            config.ObjectiveSurveyPoints.Remove(sameMap[0]);
        }

        Save();

        status =
            $"Research point saved: {label}. " +
            "This is not yet an automatic route destination.";
    }

    private string CaptureObjectiveResearchReport(
        Vector3 position)
    {
        var report = new StringBuilder();

        report.AppendLine(
            "=== BLUNDERNAV DEV6.1 OBJECTIVE RESEARCH ===");

        report.AppendLine($"UTC: {DateTime.UtcNow:O}");
        report.AppendLine($"Territory: {Client.TerritoryType}");
        report.AppendLine($"Map ID: {Client.MapId}");
        report.AppendLine($"Course: {DetectedMapName}");

        report.AppendLine(
            $"Current XYZ: {FormatPosition(position)}");

        report.AppendLine(
            $"Initial XYZ: {FormatPosition(firstPlayerPosition)}");

        report.AppendLine(
            $"Observed minimum XYZ: " +
            FormatPosition(minimumPlayerPosition));

        report.AppendLine(
            $"Observed maximum XYZ: " +
            FormatPosition(maximumPlayerPosition));

        report.AppendLine(
            $"Furthest from entry: {maximumPlayerDistance:F1}y");

        report.AppendLine(
            $"Survey samples: {objectiveSamples}");

        report.AppendLine(
            $"Tracked object instances: {objectiveObjects.Count}");

        report.AppendLine();
        report.AppendLine(
            "=== OBJECT TYPES OBSERVED THIS COURSE ===");

        // Group repeated objects with the same Base ID.
        // One course may contain dozens of identical NPCs.

        foreach (var group in objectiveObjects.Values
            .GroupBy(o => o.Kind + "/" + o.BaseId)
            .OrderByDescending(g => g.Any(o => o.WasSelected))
            .ThenByDescending(g => g.Any(o => o.Targetable))
            .ThenByDescending(g => g.Count())
            .Take(45))
        {
            report.AppendLine(
                $"{group.Key} | Instances={group.Count()} | " +
                $"Targetable={group.Count(o => o.Targetable)} | " +
                $"Selected={group.Count(o => o.WasSelected)} | " +
                $"Moving={group.Count(o => o.MaximumTravel > 1f)}");
        }

        report.AppendLine();
        report.AppendLine(
            "=== POSSIBLE INTERACTION / LANDMARK OBJECTS ===");

        var candidates = objectiveObjects.Values
            .Where(o =>
                o.Targetable ||
                o.WasSelected ||
                !string.IsNullOrWhiteSpace(o.Name) ||
                o.MaximumTravel > 1f)
            .OrderByDescending(o => o.WasSelected)
            .ThenByDescending(o => o.Targetable)
            .ThenByDescending(o =>
                !string.IsNullOrWhiteSpace(o.Name))
            .ThenByDescending(o => o.Sightings)
            .Take(35);

        foreach (var obj in candidates)
        {
            report.AppendLine(
                $"Kind={obj.Kind} | " +
                $"BaseID={obj.BaseId} | " +
                $"Instance=0x{obj.InstanceId:X} | " +
                $"Name={obj.Name} | " +
                $"Targetable={obj.Targetable} | " +
                $"Selected={obj.WasSelected} | " +
                $"Seen={obj.Sightings} | " +
                $"Travel={obj.MaximumTravel:F1}y | " +
                $"Radius={obj.HitboxRadius:F1} | " +
                $"First={FormatPosition(obj.FirstPosition)} | " +
                $"Last={FormatPosition(obj.LastPosition)}");
        }

        report.AppendLine();
        report.AppendLine("=== TARGET CHANGES ===");

        foreach (var entry in objectiveTargetEvents)
            report.AppendLine(entry);

        report.AppendLine();
        report.AppendLine("=== OPTIONAL RESEARCH POINTS ===");

        if (config.ObjectiveSurveyPoints != null)
        {
            foreach (var sample in config.ObjectiveSurveyPoints
                .Where(p => p.MapId == Client.MapId))
            {
                report.AppendLine(
                    $"{sample.Label} | " +
                    FormatPosition(new Vector3(
                        sample.X, sample.Y, sample.Z)));
            }
        }

        report.AppendLine();
        report.AppendLine("=== CURRENT MAP CASTS ===");

        foreach (var cast in casts.Take(30))
            report.AppendLine(cast);

        report.AppendLine();
        report.AppendLine(
            "Object candidates and research points are " +
            "NOT verified finish lines or hazard geometry.");

        report.AppendLine(
            "No automatic route generation is enabled.");

        return report.ToString();
    }

    private void DrawObjectiveResearch(Vector3 position)
    {
        ImGui.TextDisabled("OBJECTIVE DISCOVERY - DEV6.1");

        ImGui.TextWrapped(ObjectiveSurveyStatus);

        if (ImGui.CollapsingHeader(
            "Objective research tools"))
        {
            ImGui.TextWrapped(
                "Tracks game objects, target changes and " +
                "your position across this course. " +
                "Research only: no guessed destinations.");

            if (ImGui.Button("Capture + copy objective report"))
            {
                diagnosticReport =
                    CaptureObjectiveResearchReport(position);

                ImGui.SetClipboardText(diagnosticReport);

                status = "Objective research report copied.";
            }

            ImGui.Spacing();
            ImGui.TextDisabled("OPTIONAL GOAL SAMPLES");

            ImGui.TextWrapped(
                "You may tag an objective once. " +
                "No route recording is needed. " +
                "These samples are not used for guidance yet.");

            if (ImGui.Button("Mark finish / goal here"))
            {
                AddObjectiveSurveyPoint(
                    position, "Observed finish / goal");
            }

            IGameObject? target = Targets.Target;

            bool targetAvailable =
                target != null &&
                target.ObjectKind != ObjectKind.Pc;

            if (!targetAvailable)
                ImGui.BeginDisabled();

            if (ImGui.Button("Mark selected target"))
            {
                if (target != null)
                {
                    AddObjectiveSurveyPoint(
                        target.Position,
                        $"Selected target BaseID={target.BaseId}");
                }
            }

            if (!targetAvailable)
                ImGui.EndDisabled();

            ImGui.Spacing();

            if (ImGui.TreeNode("Observed target changes"))
            {
                foreach (var entry in objectiveTargetEvents
                    .Take(12))
                {
                    ImGui.TextWrapped(entry);
                }

                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Candidate landmarks"))
            {
                foreach (var item in objectiveObjects.Values
                    .Where(o =>
                        o.Targetable ||
                        o.WasSelected ||
                        !string.IsNullOrWhiteSpace(o.Name))
                    .OrderByDescending(o => o.WasSelected)
                    .ThenByDescending(o => o.Targetable)
                    .Take(15))
                {
                    ImGui.TextUnformatted(
                        $"{item.Kind} / {item.BaseId} " +
                        $"{item.Name} " +
                        $"({item.LastPosition.X:F1}, " +
                        $"{item.LastPosition.Z:F1})");
                }

                ImGui.TreePop();
            }

            if (diagnosticReport.Length > 0)
            {
                ImGui.TextDisabled(
                    $"Last report: {diagnosticReport.Length} chars");
            }
        }
    }
}