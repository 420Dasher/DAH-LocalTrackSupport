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

    private RunPhase phase = RunPhase.Idle;
    private Task<List<Vector3>>? pending;
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
    }

    private void Save()
    {
        pi.SavePluginConfig(config);
    }

    private bool Active =>
        phase is RunPhase.Planning or
                 RunPhase.Moving or
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
        phase = RunPhase.Idle;
        status = reason;
    }

    private void Pause(string reason)
    {
        CallStop();
        pending = null;
        phase = RunPhase.Paused;
        status = reason;
    }

    private void PlanNext()
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

        var from = player.Position;
        var target = route.Points[nextIndex].Position;

        if (!Valid(from) || !Valid(target))
        {
            Pause("Invalid checkpoint coordinates.");
            return;
        }

        if (Vector3.Distance(from, target) > 25f)
        {
            Pause("Checkpoint is over 25 yalms away. Review route.");
            return;
        }

        try
        {
            pending = findPath.InvokeFunc(from, target, false);

            if (pending == null)
            {
                Pause("vnavmesh returned no path task.");
                return;
            }

            phase = RunPhase.Planning;
            status = $"Finding path to checkpoint {nextIndex + 1}.";
        }
        catch (Exception ex)
        {
            Pause("Path request failed: " + ex.Message);
        }
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

    private void Arrived()
    {
        CallStop();

        var route = CurrentRoute();
        int completed = nextIndex;

        nextIndex++;

        if (nextIndex >= route.Points.Count)
        {
            phase = RunPhase.Finished;
            status = "All recorded checkpoints completed.";
            return;
        }

        if (!config.AutoAdvance ||
            route.Points[completed].Hold)
        {
            phase = RunPhase.Paused;
            status = $"Checkpoint {completed + 1} reached. Continue when safe.";
            return;
        }

        PlanNext();
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
        PlanNext();
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

        PlanNext();
    }

    public void Update(bool inCourse, bool connected,
        bool ready, bool running, bool manualBusy)
    {
        var player = objects.LocalPlayer;

        if (!inCourse || player == null)
        {
            if (Active)
                Stop("Left the Fall Guys course.");

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

        if (config.AutoRecord && !Active &&
            phase != RunPhase.Finished)
        {
            AddPoint(position, true);
        }

        if (!Active)
            return;

        if (!connected || !ready || manualBusy)
        {
            Pause("Navigation became unavailable.");
            return;
        }

        if (phase == RunPhase.Planning)
        {
            if (pending == null || !pending.IsCompleted)
                return;

            var task = pending;
            pending = null;

            try
            {
                var route = CurrentRoute();
                var destination = route.Points[nextIndex].Position;
                var path = task.GetAwaiter().GetResult();

                if (!CheckPath(
                    path, position, destination, out string problem))
                {
                    Pause("Path rejected: " + problem);
                    return;
                }

                movePath.InvokeAction(path, false);

                ownsMovement = true;
                phase = RunPhase.Moving;
                moveStarted = DateTime.UtcNow;
                lastProgress = moveStarted;
                lastPosition = position;

                status = $"Moving to checkpoint {nextIndex + 1}.";
            }
            catch (Exception ex)
            {
                Pause("Navigation failed: " +
                    ex.GetBaseException().Message);
            }

            return;
        }

        if (phase != RunPhase.Moving)
            return;

        var target = CurrentRoute().Points[nextIndex].Position;
        var now = DateTime.UtcNow;

        if (Vector3.Distance(position, target) <= 1.2f)
        {
            Arrived();
            return;
        }

        if (Vector3.Distance(position, lastPosition) > 0.2f)
        {
            lastPosition = position;
            lastProgress = now;
        }

        if (!running && (now - moveStarted).TotalSeconds > 1.0)
        {
            Pause("Movement ended before checkpoint. Resume to retry.");
            return;
        }

        if ((now - lastProgress).TotalSeconds > 3.5)
        {
            Pause("Movement stalled. Resume to retry.");
            return;
        }

        if ((now - moveStarted).TotalSeconds > 18)
        {
            Pause("Checkpoint timed out. Resume to retry.");
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
            "DEV2 - Route Recorder (Lobby + Duty)",
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

        ImGui.TextWrapped(
            "Dynamic obstacles are NOT predicted. " +
            "Use hold checkpoints before timed hazards.");

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