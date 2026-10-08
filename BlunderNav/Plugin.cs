using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace BlunderNav;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService]
    private static IDalamudPluginInterface Pi { get; set; } = null!;

    [PluginService]
    private static ICommandManager Commands { get; set; } = null!;

    [PluginService]
    private static IFramework Framework { get; set; } = null!;

    [PluginService]
    private static IClientState Client { get; set; } = null!;

    [PluginService]
    private static IObjectTable Objects { get; set; } = null!;

    [PluginService]
    private static IPluginLog Log { get; set; } = null!;

    private readonly ICallGateSubscriber<bool> navReady;
    private readonly ICallGateSubscriber<bool> navRunning;
    private readonly ICallGateSubscriber<
        Vector3, Vector3, bool, Task<List<Vector3>>> navPathfind;
    private readonly ICallGateSubscriber<
        List<Vector3>, bool, object> navMove;
    private readonly ICallGateSubscriber<object> navStop;

    private readonly record struct CastRecord(
        DateTime Time,
        uint Action,
        string Actor,
        Vector3 Position
    );

    private readonly List<CastRecord> recentCasts = new();
    private readonly Dictionary<string, uint> previousCasts = new();

    private Task<List<Vector3>>? pendingPath;
    private List<Vector3>? previewPath;

    private Vector3 previewOrigin;
    private Vector3 previewTarget;
    private Vector3 previousPosition;

    private DateTime previewTime;
    private DateTime movementStart;
    private DateTime lastMovement;
    private DateTime lastPoll;
    private DateTime lastCastPoll;

    private readonly RouteController routes;

    private bool windowOpen;
    private bool navConnected;
    private bool navIsReady;
    private bool pathIsRunning;
    private bool ownMovement;

    private int offsetX;
    private int offsetZ = 2;

    private string status = "Idle.";

    private bool InCourse => Client.TerritoryType == 1165;
    private bool InLobby => Client.TerritoryType == 1197;
    private bool InTestArea => InCourse || InLobby;

    public Plugin()
    {
        navReady = Pi.GetIpcSubscriber<bool>(
            "vnavmesh.Nav.IsReady");

        navRunning = Pi.GetIpcSubscriber<bool>(
            "vnavmesh.Path.IsRunning");

        navPathfind = Pi.GetIpcSubscriber<
            Vector3, Vector3, bool, Task<List<Vector3>>>(
            "vnavmesh.Nav.Pathfind");

        navMove = Pi.GetIpcSubscriber<
            List<Vector3>, bool, object>(
            "vnavmesh.Path.MoveTo");

        navStop = Pi.GetIpcSubscriber<object>(
            "vnavmesh.Path.Stop");

        routes = new RouteController(Pi, Client, Objects, Log);

        Commands.AddHandler("/bnav", new CommandInfo(OnCommand)
        {
            HelpMessage = "BlunderNav DEV1. /bnav stop to stop movement."
        });

        Pi.UiBuilder.Draw += Draw;
        Pi.UiBuilder.OpenMainUi += Open;
        Pi.UiBuilder.OpenConfigUi += Open;

        Framework.Update += Update;

        Log.Information("BlunderNav DEV1 initialized.");
    }

    private void Open()
    {
        windowOpen = true;
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals(
            "stop", StringComparison.OrdinalIgnoreCase))
        {
            Stop("Stopped by command.");
            return;
        }

        windowOpen = true;
    }

    public void Dispose()
    {
        Framework.Update -= Update;
        Pi.UiBuilder.Draw -= Draw;
        Pi.UiBuilder.OpenMainUi -= Open;
        Pi.UiBuilder.OpenConfigUi -= Open;

        Commands.RemoveHandler("/bnav");

        if (ownMovement)
            Stop("Plugin unloading.");
        else
            routes.Stop("Plugin unloading.");
    }

    private string CourseName(Vector3 pos)
    {
        if (InLobby)
            return "Blunderville Lobby";

        if (!InCourse)
            return "Outside Fall Guys";

        if (pos.X >= -40 && pos.X <= 40 &&
            pos.Z >= 100 && pos.Z <= 350)
        {
            return "Manderville Mountain candidate (Round 3)";
        }

        return "Fall Guys course (Round 1/2 unclassified)";
    }

    private void Update(IFramework framework)
    {
        var now = DateTime.UtcNow;
        var player = Objects.LocalPlayer;

        if ((now - lastPoll).TotalMilliseconds >= 400)
        {
            lastPoll = now;

            try
            {
                navIsReady = navReady.InvokeFunc();
                pathIsRunning = navRunning.InvokeFunc();
                navConnected = true;
            }
            catch
            {
                navConnected = false;
                navIsReady = false;
                pathIsRunning = false;

                if (ownMovement)
                {
                    ownMovement = false;
                    status = "vnavmesh disconnected during movement.";
                }
            }
        }

        routes.Update(InTestArea, navConnected, navIsReady, pathIsRunning, ownMovement);

        if (!InTestArea || player == null)
        {
            if (ownMovement)
                Stop("Left the course.");

            pendingPath = null;
            previewPath = null;
            previousCasts.Clear();
            return;
        }

        var pos = player.Position;

        if ((now - lastCastPoll).TotalMilliseconds >= 200)
        {
            lastCastPoll = now;
            ObserveCasts(now, pos);
        }

        // Process completed asynchronous pathfinding.

        if (pendingPath is { IsCompleted: true } task)
        {
            pendingPath = null;

            try
            {
                var result = task.GetAwaiter().GetResult();

                if (Vector3.Distance(pos, previewOrigin) > 1.5f)
                {
                    status = "Preview canceled: player moved.";
                }
                else if (!ValidatePath(result, pos, out var reason))
                {
                    status = "Route rejected: " + reason;
                }
                else
                {
                    previewPath = result;
                    previewTime = now;
                    status = $"Preview ready: {result.Count} waypoints.";
                }
            }
            catch (Exception ex)
            {
                previewPath = null;
                status = "Pathfinding failed: " +
                    ex.GetBaseException().Message;
                Log.Warning(status);
            }
        }

        // Expire previews after 15 seconds or player movement.

        if (previewPath != null &&
            ((now - previewTime).TotalSeconds > 15 ||
             Vector3.Distance(pos, previewOrigin) > 1.5f))
        {
            previewPath = null;
            status = "Preview expired.";
        }

        // Monitor and stop our own navigation requests.

        if (ownMovement)
        {
            if (!navConnected)
            {
                ownMovement = false;
                return;
            }

            if (!pathIsRunning &&
                (now - movementStart).TotalSeconds > 1.0)
            {
                ownMovement = false;
                status = "Navigation finished or stopped.";
                return;
            }

            if (Vector3.Distance(pos, previousPosition) > 0.15f)
            {
                previousPosition = pos;
                lastMovement = now;
            }
            else if ((now - lastMovement).TotalSeconds > 3.0)
            {
                Stop("Movement stalled for 3 seconds.");
                return;
            }

            if ((now - movementStart).TotalSeconds > 12.0)
            {
                Stop("Movement timed out.");
            }
        }
    }

    private void ObserveCasts(DateTime now, Vector3 position)
    {
        var active = new Dictionary<string, uint>();

        foreach (var obj in Objects)
        {
            if (obj is not IBattleChara actor)
                continue;

            if (!actor.IsCasting || actor.CastActionId == 0)
                continue;

            if (Vector3.Distance(position, actor.Position) > 90f)
                continue;

            var key = actor.GameObjectId.ToString();
            var action = actor.CastActionId;

            active[key] = action;

            if (previousCasts.TryGetValue(key, out var old) &&
                old == action)
                continue;

            recentCasts.Insert(0, new CastRecord(
                now, action, actor.Name.ToString(), actor.Position));

            if (recentCasts.Count > 30)
                recentCasts.RemoveAt(recentCasts.Count - 1);
        }

        previousCasts.Clear();

        foreach (var entry in active)
            previousCasts[entry.Key] = entry.Value;
    }

    private bool ValidatePath(
        List<Vector3>? path,
        Vector3 origin,
        out string reason)
    {
        reason = "";

        if (path == null || path.Count == 0)
        {
            reason = "No route returned.";
            return false;
        }

        if (path.Count > 48)
        {
            reason = "Too many waypoints.";
            return false;
        }

        float length = 0;
        var previous = origin;

        foreach (var point in path)
        {
            if (!float.IsFinite(point.X) ||
                !float.IsFinite(point.Y) ||
                !float.IsFinite(point.Z))
            {
                reason = "Invalid coordinates.";
                return false;
            }

            if (Math.Abs(point.Y - origin.Y) > 1.25f)
            {
                reason = "Height change too large.";
                return false;
            }

            if (Vector3.Distance(origin, point) > 7f)
            {
                reason = "Route leaves test area.";
                return false;
            }

            length += Vector3.Distance(previous, point);
            previous = point;
        }

        if (length > 10f)
        {
            reason = "Route exceeds 10 yalms.";
            return false;
        }

        if (Vector3.Distance(previous, previewTarget) > 1.25f)
        {
            reason = "Destination mismatch.";
            return false;
        }

        return true;
    }

    private void Preview()
    {
        var player = Objects.LocalPlayer;

        if (!InTestArea || player == null)
        {
            status = "Enter Fall Guys first.";
            return;
        }

        if (!navConnected || !navIsReady)
        {
            status = "vnavmesh not ready.";
            return;
        }

        if (pathIsRunning || ownMovement || pendingPath != null || routes.IsBusy)
        {
            status = "Navigation already active.";
            return;
        }

        previewPath = null;
        previewOrigin = player.Position;

        previewTarget = previewOrigin +
            new Vector3(offsetX, 0, offsetZ);

        try
        {
            pendingPath = navPathfind.InvokeFunc(
                previewOrigin,
                previewTarget,
                false);

            status = pendingPath == null
                ? "No pathfinding task returned."
                : "Calculating short navigation route...";
        }
        catch (Exception ex)
        {
            pendingPath = null;
            status = "Navigation IPC error: " + ex.Message;
        }
    }

    private void Execute()
    {
        var player = Objects.LocalPlayer;

        if (!InTestArea || player == null ||
            !navConnected || !navIsReady ||
            pathIsRunning || previewPath == null || routes.IsBusy)
        {
            status = "Cannot execute: navigation not ready.";
            return;
        }

        if ((DateTime.UtcNow - previewTime).TotalSeconds > 15 ||
            Vector3.Distance(player.Position, previewOrigin) > 1.5f ||
            !ValidatePath(previewPath, player.Position, out _))
        {
            previewPath = null;
            status = "Preview no longer valid.";
            return;
        }

        try
        {
            navMove.InvokeAction(previewPath, false);

            previewPath = null;
            ownMovement = true;

            movementStart = DateTime.UtcNow;
            lastMovement = movementStart;
            previousPosition = player.Position;

            status = "Executing short test movement.";
        }
        catch (Exception ex)
        {
            status = "Movement failed: " + ex.Message;
        }
    }

    private void Stop(string reason)
    {
        routes.Stop(reason);
        try
        {
            navStop.InvokeAction();
        }
        catch (Exception ex)
        {
            Log.Warning("Stop IPC failed: " + ex.Message);
        }

        ownMovement = false;
        pendingPath = null;
        previewPath = null;
        status = reason;
    }

    private void Draw()
    {
        if (!windowOpen)
            return;

        if (!ImGui.Begin(
            "BlunderNav | DEV3.1 FIX1",
            ref windowOpen,
            ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.End();
            return;
        }

        try
        {
            var player = Objects.LocalPlayer;
            var pos = player?.Position ?? default;

            ImGui.TextUnformatted("BLUNDERNAV");
            ImGui.Separator();

            ImGui.TextUnformatted("Version: 0.0.7 DEV3.1 FIX1");
            ImGui.TextUnformatted(
                "Territory: " + Client.TerritoryType);
            ImGui.TextUnformatted(
                "Course: " + CourseName(pos));

            ImGui.TextUnformatted(
                $"Position: {pos.X:F2}, {pos.Y:F2}, {pos.Z:F2}");

            ImGui.TextUnformatted(
                "vnavmesh: " +
                (!navConnected ? "NOT CONNECTED" :
                 navIsReady ? "READY" : "NO MESH / LOADING"));

            ImGui.TextUnformatted(
                "Navigation: " +
                (pathIsRunning ? "ACTIVE" : "IDLE"));

            ImGui.Spacing();
            ImGui.TextWrapped("Status: " + status);

            ImGui.Separator();

            ImGui.TextUnformatted("NAVIGATION TEST");
            ImGui.TextWrapped(
                "Select a short offset. Preview first, then execute. " +
                "Use only on clear, flat ground. " +
                "Dynamic hazards are not yet considered.");

            if (ImGui.Button("Left 2"))
            {
                offsetX = -2; offsetZ = 0;
                previewPath = null;
            }

            ImGui.SameLine();

            if (ImGui.Button("Right 2"))
            {
                offsetX = 2; offsetZ = 0;
                previewPath = null;
            }

            ImGui.SameLine();

            if (ImGui.Button("Z -2"))
            {
                offsetX = 0; offsetZ = -2;
                previewPath = null;
            }

            ImGui.SameLine();

            if (ImGui.Button("Z +2"))
            {
                offsetX = 0; offsetZ = 2;
                previewPath = null;
            }

            ImGui.TextUnformatted(
                $"Offset: X {offsetX}, Z {offsetZ}");

            bool canPreview =
                InTestArea && navConnected && navIsReady &&
                !pathIsRunning && !ownMovement &&
                pendingPath == null && !routes.IsBusy;

            if (!canPreview)
                ImGui.BeginDisabled();

            if (ImGui.Button("1. Preview route"))
                Preview();

            if (!canPreview)
                ImGui.EndDisabled();

            ImGui.SameLine();

            bool canExecute =
                canPreview && previewPath != null;

            if (!canExecute)
                ImGui.BeginDisabled();

            if (ImGui.Button("2. Execute"))
                Execute();

            if (!canExecute)
                ImGui.EndDisabled();

            ImGui.Spacing();

            if (ImGui.Button("EMERGENCY STOP"))
                Stop("Emergency stop pressed.");

            if (previewPath != null)
            {
                ImGui.TextUnformatted(
                    $"Preview contains {previewPath.Count} waypoints.");

                if (ImGui.TreeNode("Preview coordinates"))
                {
                    foreach (var point in previewPath)
                    {
                        ImGui.TextUnformatted(
                            $"{point.X:F2} / {point.Y:F2} / {point.Z:F2}");
                    }

                    ImGui.TreePop();
                }
            }

            ImGui.Separator();

            routes.Draw(InTestArea, navConnected, navIsReady, pathIsRunning, ownMovement);

            if (ImGui.CollapsingHeader("Cast observations"))
            {
                ImGui.TextWrapped(
                    "Observed nearby casts only. " +
                    "Instant mechanics are not captured.");

                if (ImGui.Button("Clear cast history"))
                    recentCasts.Clear();

                foreach (var cast in recentCasts.Take(20))
                {
                    ImGui.TextUnformatted(
                        $"{cast.Time.ToLocalTime():HH:mm:ss} " +
                        $"ID {cast.Action} | " +
                        $"{cast.Position.X:F1}, {cast.Position.Z:F1} " +
                        $"| {cast.Actor}");
                }
            }

            ImGui.Spacing();
            ImGui.TextDisabled(
                "DEV3.1 FIX1 | API 15 | .NET 10 | /bnav stop");
        }
        finally
        {
            ImGui.End();
        }
    }
}