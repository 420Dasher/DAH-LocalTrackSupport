using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace BlunderNav;

public sealed class RouteController
{
    private static readonly string[] Names =
    {
        "Unclassified / Test Route",
        "Gentlebean's Fever",
        "Manderville-can Parade",
        "Saucery Siege",
        "The Gold Swiveller",
        "Manderville Mountain"
    };

    private enum RunPhase
    {
        Idle,
        Planning,
        Moving,
        Recovering,
        Paused,
        Finished
    }

    private readonly IDalamudPluginInterface pi;
    private readonly IClientState client;
    private readonly IObjectTable objects;
    private readonly IPluginLog log;
    private readonly RouteConfiguration config;

    private readonly ICallGateSubscriber<
        Vector3, Vector3, bool, Task<List<Vector3>>> findPath;

    private readonly ICallGateSubscriber<
        List<Vector3>, bool, object> movePath;

    private readonly ICallGateSubscriber<object> stopPath;

    private readonly ICallGateSubscriber<
        Vector3, float, bool, bool> isPointOnMesh;

    private RunPhase phase = RunPhase.Idle;
    private Task<List<Vector3>>? pending;
    private readonly List<Vector3> stitchedPath = new();

    private int lastSmoothedCorners;
    private int lastRejectedCorners;
    private int lastMovementWaypoints;
    private bool smoothingFallback;

    private Vector3 planningOrigin;
    private Vector3 legFrom;
    private Vector3 legTarget;

    private int planIndex;
    private int groupEndIndex;
    private int recoveryAttempts;

    private DateTime legRequested;
    private DateTime recoverAt;

    private bool ownsMovement;
    private bool clearArmed;

    private uint runTerritory;
    private int nextIndex = 1;
    private DateTime moveStarted;
    private DateTime lastProgress;
    private Vector3 lastPosition;
    private string status = "No route running.";

    public RouteController(
        IDalamudPluginInterface pluginInterface,
        IClientState clientState,
        IObjectTable objectTable,
        IPluginLog pluginLog)
    {
        pi = pluginInterface;
        client = clientState;
        objects = objectTable;
        log = pluginLog;

        config = pi.GetPluginConfig() as RouteConfiguration
            ?? new RouteConfiguration();

        config.Routes ??= new List<RecordedRoute>();
        config.SelectedSlot = Math.Clamp(
            config.SelectedSlot, 0, Names.Length - 1);

        findPath = pi.GetIpcSubscriber<
            Vector3, Vector3, bool, Task<List<Vector3>>>(
            "vnavmesh.Nav.Pathfind");

        movePath = pi.GetIpcSubscriber<
            List<Vector3>, bool, object>(
            "vnavmesh.Path.MoveTo");

        stopPath = pi.GetIpcSubscriber<object>(
            "vnavmesh.Path.Stop");

        isPointOnMesh = pi.GetIpcSubscriber<
            Vector3, float, bool, bool>(
            "vnavmesh.Query.Mesh.IsPointOnMesh");
    }

    private void Save()
    {
        pi.SavePluginConfig(config);
    }

    private bool Active =>
        phase is RunPhase.Planning or
                 RunPhase.Moving or
                 RunPhase.Recovering or
                 RunPhase.Paused;

    public bool IsBusy => Active;

    private RecordedRoute CurrentRoute()
    {
        string name = Names[config.SelectedSlot];
        ushort territory = client.TerritoryType == 1197
            ? (ushort)1197 : (ushort)1165;

        foreach (var existing in config.Routes)
        {
            if (existing.Name == name && existing.Territory == territory)
            {
                existing.Points ??= new List<RoutePoint>();
                return existing;
            }
        }

        var created = new RecordedRoute
        {
            Name = name,
            Territory = territory
        };

        config.Routes.Add(created);
        Save();

        return created;
    }

    private static bool Valid(Vector3 p)
    {
        return float.IsFinite(p.X) &&
               float.IsFinite(p.Y) &&
               float.IsFinite(p.Z);
    }

    private void AddPoint(Vector3 position, bool automatic)
    {
        var route = CurrentRoute();

        if (!Valid(position))
            return;

        if (route.Points.Count >= 250)
        {
            config.AutoRecord = false;
            Save();
            status = "Route limit reached: 250 checkpoints.";
            return;
        }

        if (route.Points.Count > 0)
        {
            float distance = Vector3.Distance(
                route.Points[^1].Position, position);

            if (distance < (automatic ? config.RecordSpacing : 0.5f))
                return;

            if (distance > 20f)
            {
                if (automatic)
                {
                    config.AutoRecord = false;
                    Save();
                }

                status = "Large displacement detected. Recording paused.";
                return;
            }
        }

        route.Points.Add(new RoutePoint(position));
        Save();

        status = $"Recorded checkpoint {route.Points.Count}.";
    }

    private void CallStop()
    {
        if (!ownsMovement)
            return;

        try
        {
            stopPath.InvokeAction();
        }
        catch (Exception ex)
        {
            log.Warning("BlunderNav stop IPC: " + ex.Message);
        }

        ownsMovement = false;
    }

    public void Stop(string reason)
    {
        CallStop();
        pending = null;
        stitchedPath.Clear();
        recoveryAttempts = 0;
        phase = RunPhase.Idle;
        status = reason;
    }

    private void Pause(string reason)
    {
        CallStop();
        pending = null;
        stitchedPath.Clear();
        phase = RunPhase.Paused;
        status = reason;
    }

    private void Recover(string reason)
    {
        CallStop();
        pending = null;
        stitchedPath.Clear();

        if (recoveryAttempts >= 2)
        {
            Pause(reason + " Recovery limit reached (2 attempts).");
            return;
        }

        recoveryAttempts++;
        recoverAt = DateTime.UtcNow.AddMilliseconds(650);
        phase = RunPhase.Recovering;

        status = $"Repath attempt {recoveryAttempts}/2: {reason}";
    }

    private void RequestLeg()
    {
        var route = CurrentRoute();

        if (planIndex < 1 ||
            planIndex > groupEndIndex ||
            planIndex >= route.Points.Count)
        {
            Pause("Invalid route leg.");
            return;
        }

        legTarget = route.Points[planIndex].Position;

        if (!Valid(legFrom) || !Valid(legTarget))
        {
            Pause("Invalid route coordinates.");
            return;
        }

        if (Vector3.Distance(legFrom, legTarget) > 25f)
        {
            Pause("Route leg exceeds 25 yalms. Add checkpoints.");
            return;
        }

        try
        {
            pending = findPath.InvokeFunc(legFrom, legTarget, false);

            if (pending == null)
            {
                Pause("vnavmesh returned no pathfinding task.");
                return;
            }

            legRequested = DateTime.UtcNow;
            phase = RunPhase.Planning;

            status =
                $"Planning {planIndex + 1}/{route.Points.Count} " +
                $"(group ends at {groupEndIndex + 1}).";
        }
        catch (Exception ex)
        {
            Pause("Pathfinding request failed: " + ex.Message);
        }
    }

    private void BeginGroup()
    {
        var player = objects.LocalPlayer;
        var route = CurrentRoute();

        if (player == null ||
            nextIndex < 1 ||
            nextIndex >= route.Points.Count)
        {
            Pause("Invalid checkpoint or player unavailable.");
            return;
        }

        groupEndIndex = nextIndex;

        // When Auto-advance is enabled, combine consecutive
        // ordinary checkpoints into one continuous vnavmesh path.
        //
        // An explicit HOLD terminates the current group.
        // Limit each group to 24 route legs for bounded planning.

        if (config.AutoAdvance)
        {
            while (
                groupEndIndex < route.Points.Count - 1 &&
                !route.Points[groupEndIndex].Hold &&
                groupEndIndex - nextIndex + 1 < 64)
            {
                groupEndIndex++;
            }
        }

        planningOrigin = player.Position;
        legFrom = planningOrigin;
        planIndex = nextIndex;

        pending = null;
        stitchedPath.Clear();

        RequestLeg();
    }

    private bool CheckPath(
        List<Vector3> path,
        Vector3 origin,
        Vector3 destination,
        out string error)
    {
        error = "";

        if (path.Count == 0 || path.Count > 96)
        {
            error = "Invalid waypoint count.";
            return false;
        }

        float length = 0;
        Vector3 previous = origin;

        foreach (var point in path)
        {
            if (!Valid(point))
            {
                error = "Non-finite path coordinates.";
                return false;
            }

            if (MathF.Abs(point.Y - origin.Y) > 8)
            {
                error = "Unexpected height difference.";
                return false;
            }

            length += Vector3.Distance(previous, point);
            previous = point;
        }

        if (length > 40f)
        {
            error = "Path exceeds 40 yalms.";
            return false;
        }

        if (Vector3.Distance(previous, destination) > 2f)
        {
            error = "Path destination mismatch.";
            return false;
        }

        return true;
    }

    private void GroupArrived()
    {
        CallStop();

        var route = CurrentRoute();
        int completed = groupEndIndex;

        nextIndex = completed + 1;
        recoveryAttempts = 0;
        stitchedPath.Clear();

        if (nextIndex >= route.Points.Count)
        {
            phase = RunPhase.Finished;
            status = "Smooth route completed successfully.";
            return;
        }

        if (!config.AutoAdvance ||
            route.Points[completed].Hold)
        {
            phase = RunPhase.Paused;

            status =
                $"Checkpoint {completed + 1} reached. " +
                "Continue when ready.";

            return;
        }

        // A route exceeding the group-size limit resumes
        // automatically with a new bounded group.

        BeginGroup();
    }

    private void Start(bool inCourse, bool connected,
        bool ready, bool running, bool manualBusy)
    {
        if (!inCourse || !connected || !ready ||
            running || manualBusy)
        {
            status = "Navigation unavailable or already active.";
            return;
        }

        var route = CurrentRoute();
        var player = objects.LocalPlayer;

        if (player == null || route.Points.Count < 2)
        {
            status = "Record at least two checkpoints first.";
            return;
        }

        if (Vector3.Distance(
            player.Position, route.Points[0].Position) > 3f)
        {
            status = "Stand within 3 yalms of the first checkpoint.";
            return;
        }

        config.AutoRecord = false;
        Save();

        runTerritory = client.TerritoryType;
        nextIndex = 1;
        recoveryAttempts = 0;
        BeginGroup();
    }

    private void Continue(bool inCourse,
        bool connected, bool ready,
        bool running, bool manualBusy)
    {
        if (phase != RunPhase.Paused)
            return;

        if (!inCourse || !connected || !ready ||
            running || manualBusy)
        {
            status = "Cannot continue while navigation is unavailable.";
            return;
        }

        recoveryAttempts = 0;
        BeginGroup();
    }

    public void Update(bool inCourse, bool connected,
        bool ready, bool running, bool manualBusy)
    {
        var player = objects.LocalPlayer;

        if (!inCourse || player == null)
        {
            if (Active)
                Stop("Left Blunderville. Route stopped.");

            if (config.AutoRecord)
            {
                config.AutoRecord = false;
                Save();
            }

            return;
        }

        if (Active && client.TerritoryType != runTerritory)
        {
            Stop("Territory changed. Route stopped.");
            return;
        }

        var position = player.Position;
        var now = DateTime.UtcNow;

        // Recording can be restarted even after Finished.

        if (config.AutoRecord && !Active)
            AddPoint(position, true);

        if (!Active)
            return;

        if (!connected || !ready || manualBusy)
        {
            Pause("Navigation unavailable.");
            return;
        }

        // ------------------------------------------------
        // BOUNDED AUTOMATIC RECOVERY
        // ------------------------------------------------

        if (phase == RunPhase.Recovering)
        {
            if (now >= recoverAt)
                BeginGroup();

            return;
        }

        // ------------------------------------------------
        // BUILD CONTINUOUS PATH
        // ------------------------------------------------

        if (phase == RunPhase.Planning)
        {
            if ((now - legRequested).TotalSeconds > 10)
            {
                Pause("Pathfinding timed out.");
                return;
            }

            if (pending == null || !pending.IsCompleted)
                return;

            var task = pending;
            pending = null;

            try
            {
                if (Vector3.Distance(
                    position, planningOrigin) > 2f)
                {
                    Pause("Player moved during path planning.");
                    return;
                }

                var path = task.GetAwaiter().GetResult();
                string problem = "No route.";

                if (path == null ||
                    !CheckPath(
                        path, legFrom, legTarget,
                        out problem))
                {
                    Pause("Route leg rejected: " +
                        (path == null ? "No route." : problem));
                    return;
                }

                // Stitch the legs while removing overlapping
                // consecutive waypoints.

                foreach (var waypoint in path)
                {
                    if (stitchedPath.Count > 0 &&
                        Vector3.Distance(
                            stitchedPath[^1], waypoint) < 0.10f)
                    {
                        continue;
                    }

                    stitchedPath.Add(waypoint);

                    if (stitchedPath.Count > 1024)
                    {
                        Pause(
                            "Combined path too large. " +
                            "Split the route with a HOLD checkpoint.");
                        return;
                    }
                }

                legFrom = legTarget;

                if (planIndex < groupEndIndex)
                {
                    planIndex++;
                    RequestLeg();
                    return;
                }

                if (stitchedPath.Count == 0)
                {
                    Pause("Combined route was empty.");
                    return;
                }

                // Build an optional, mesh-sampled curved path.
                // The original route remains the fallback.

                List<Vector3> movementPath = stitchedPath;

                lastSmoothedCorners = 0;
                lastRejectedCorners = 0;
                smoothingFallback = false;

                if (config.SmoothCorners &&
                    client.TerritoryType == 1197)
                {
                    try
                    {
                        movementPath = PathSmoothing.Build(
                            stitchedPath,
                            config.CornerRadius,
                            p => isPointOnMesh.InvokeFunc(
                                p, 0.75f, false),
                            out lastSmoothedCorners,
                            out lastRejectedCorners);

                        smoothingFallback =
                            lastSmoothedCorners == 0;
                    }
                    catch (Exception ex)
                    {
                        movementPath = stitchedPath;
                        smoothingFallback = true;

                        log.Warning(
                            "BlunderNav smoothing fallback: " +
                            ex.Message);
                    }
                }

                lastMovementWaypoints = movementPath.Count;

                // Single continuous vnavmesh movement request.

                movePath.InvokeAction(movementPath, false);

                ownsMovement = true;
                phase = RunPhase.Moving;

                moveStarted = now;
                lastProgress = now;
                lastPosition = position;

                status =
                    $"Smooth movement through checkpoints " +
                    $"{nextIndex + 1}-{groupEndIndex + 1}.";
            }
            catch (Exception ex)
            {
                Pause("Route planning failed: " +
                    ex.GetBaseException().Message);
            }

            return;
        }

        if (phase != RunPhase.Moving)
            return;

        var route = CurrentRoute();

        if (groupEndIndex >= route.Points.Count)
        {
            Pause("Route changed while moving.");
            return;
        }

        // Track intermediate checkpoint progress.
        // These DO NOT stop vnavmesh movement.

        while (nextIndex < groupEndIndex &&
            Vector3.Distance(
                position,
                route.Points[nextIndex].Position) <= 2.0f)
        {
            nextIndex++;
        }

        var destination = route.Points[groupEndIndex].Position;

        if (Vector3.Distance(position, destination) <= 1.2f)
        {
            GroupArrived();
            return;
        }

        var frameMovement =
            Vector3.Distance(position, lastPosition);

        // Large unexpected displacement triggers repathing.

        if (frameMovement > 9f)
        {
            Recover("Unexpected displacement.");
            return;
        }

        if (frameMovement > 0.15f)
        {
            lastPosition = position;
            lastProgress = now;
        }

        if (!running && (now - moveStarted).TotalSeconds > 1.2)
        {
            Recover("Navigation ended before reaching destination.");
            return;
        }

        if ((now - lastProgress).TotalSeconds > 3.5)
        {
            Recover("Movement stalled.");
            return;
        }

        // Cap any individual uninterrupted movement group.

        if ((now - moveStarted).TotalSeconds > 90)
        {
            Recover("Movement group timed out.");
        }
    }

    private string DetectedLayout(Vector3 position, bool inCourse)
    {
        if (!inCourse)
            return "Outside Blunderville";

        if (client.TerritoryType == 1197)
            return "Blunderville Lobby - navigation testing";

        if (position.X >= -40 &&
            position.X <= 40 &&
            position.Z >= 100 &&
            position.Z <= 350)
        {
            return "Manderville Mountain candidate";
        }

        if (position.X >= -40 &&
            position.X <= 40 &&
            position.Z >= -400 &&
            position.Z <= -100)
        {
            return "Round 1 layout candidate";
        }

        return "Unclassified - choose route profile";
    }

    public void Draw(bool inCourse, bool connected,
        bool ready, bool running, bool manualBusy)
    {
        if (!ImGui.CollapsingHeader(
            "DEV3.1 - Smooth Route Recorder",
            ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var player = objects.LocalPlayer;
        var position = player?.Position ?? default;

        ImGui.TextWrapped(
            "Detected: " + DetectedLayout(position, inCourse));

        bool editingLocked = Active;

        if (editingLocked)
            ImGui.BeginDisabled();

        int slot = config.SelectedSlot;

        if (ImGui.Combo(
            "Route profile", ref slot, Names, Names.Length))
        {
            config.SelectedSlot = slot;
            clearArmed = false;
            Save();
        }

        var route = CurrentRoute();

        ImGui.TextUnformatted(
            route.Territory == 1197
                ? "Route scope: LOBBY (1197)"
                : "Route scope: DUTY (1165)");

        if (route.Territory == 1197)
            ImGui.TextWrapped(
                "Lobby routes are saved separately from duty routes.");

        ImGui.TextUnformatted(
            $"Recorded checkpoints: {route.Points.Count}");

        if (!inCourse)
            ImGui.BeginDisabled();

        if (ImGui.Button("Record current position") &&
            player != null)
        {
            AddPoint(position, false);
        }

        if (!inCourse)
            ImGui.EndDisabled();

        ImGui.SameLine();

        if (ImGui.Button("Remove last") && route.Points.Count > 0)
        {
            route.Points.RemoveAt(route.Points.Count - 1);
            Save();
            status = "Last checkpoint removed.";
        }

        bool recording = config.AutoRecord;

        if (ImGui.Checkbox("Automatic recording", ref recording))
        {
            config.AutoRecord = recording && inCourse;
            Save();
        }

        float spacing = config.RecordSpacing;

        if (ImGui.SliderFloat(
            "Recording spacing", ref spacing, 2, 10))
        {
            config.RecordSpacing = spacing;
            Save();
        }

        if (route.Points.Count > 0)
        {
            int lastIndex = route.Points.Count - 1;
            bool hold = route.Points[lastIndex].Hold;

            if (ImGui.Checkbox("Hold after last checkpoint", ref hold))
            {
                route.Points[lastIndex].Hold = hold;
                Save();
            }
        }

        if (!clearArmed)
        {
            if (ImGui.Button("Clear route..."))
                clearArmed = true;
        }
        else
        {
            if (ImGui.Button("CONFIRM CLEAR"))
            {
                route.Points.Clear();
                config.AutoRecord = false;
                clearArmed = false;
                Save();
                status = "Recorded route cleared.";
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel clear"))
                clearArmed = false;
        }

        if (editingLocked)
            ImGui.EndDisabled();

        ImGui.Separator();
        ImGui.TextUnformatted("CHECKPOINT PLAYBACK");

        bool advance = config.AutoAdvance;

        if (ImGui.Checkbox("Auto-advance checkpoints", ref advance))
        {
            config.AutoAdvance = advance;
            Save();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("EXPERIMENTAL TURN SMOOTHING");

        bool allowSmoothingEdit =
            !Active && client.TerritoryType == 1197;

        if (!allowSmoothingEdit)
            ImGui.BeginDisabled();

        bool smooth = config.SmoothCorners;

        if (ImGui.Checkbox("Smooth corners (lobby only)", ref smooth))
        {
            config.SmoothCorners = smooth;
            Save();
        }

        float radius = config.CornerRadius;

        if (ImGui.SliderFloat(
            "Corner radius (yalms)", ref radius, 0.25f, 1.0f))
        {
            config.CornerRadius = radius;
            Save();
        }

        if (!allowSmoothingEdit)
            ImGui.EndDisabled();

        ImGui.TextWrapped(
            "Experimental: curves are sampled on reachable " +
            "navmesh polygons, but full collision clearance " +
            "is not guaranteed. Test in open lobby areas.");

        ImGui.TextUnformatted(
            $"Last path: {lastMovementWaypoints} waypoints");

        ImGui.TextUnformatted(
            $"Smoothed bends: {lastSmoothedCorners} | " +
            $"Rejected: {lastRejectedCorners}");

        if (smoothingFallback)
        {
            ImGui.TextUnformatted(
                "Smoothing fallback: original route used.");
        }

        ImGui.Separator();

        ImGui.TextWrapped(
            "Auto-advance ON: smooth combined path. " +
            "Auto-advance OFF: pause at every checkpoint. " +
            "HOLD checkpoints always pause. " +
            "Dynamic hazards are not detected.");

        bool canStart =
            !Active && inCourse && connected && ready &&
            !running && !manualBusy &&
            route.Points.Count >= 2;

        if (!canStart)
            ImGui.BeginDisabled();

        if (ImGui.Button("Start recorded route"))
        {
            Start(inCourse, connected, ready, running, manualBusy);
        }

        if (!canStart)
            ImGui.EndDisabled();

        if (phase == RunPhase.Paused)
        {
            if (ImGui.Button("Resume / Retry"))
                Continue(inCourse, connected, ready, running, manualBusy);
        }

        if (phase is RunPhase.Moving or RunPhase.Planning)
        {
            if (ImGui.Button("Pause route"))
                Pause("Paused manually.");
        }

        if (Active)
        {
            if (ImGui.Button("STOP ROUTE"))
                Stop("Route stopped.");
        }

        ImGui.TextUnformatted($"Playback: {phase}");

        if (Active)
        {
            ImGui.TextUnformatted(
                $"Recovery attempts: {recoveryAttempts}/2");
        }
        ImGui.TextWrapped("Route status: " + status);

        if (Active)
        {
            ImGui.TextUnformatted(
                $"Next checkpoint: {nextIndex + 1}/{route.Points.Count}");
        }

        if (ImGui.TreeNode("Recorded checkpoint list"))
        {
            for (int i = 0; i < route.Points.Count; i++)
            {
                var p = route.Points[i];

                ImGui.TextUnformatted(
                    $"{i + 1:000}: {p.X:F2}, {p.Y:F2}, {p.Z:F2}" +
                    (p.Hold ? " [HOLD]" : ""));
            }

            ImGui.TreePop();
        }

        ImGui.Separator();
    }
}