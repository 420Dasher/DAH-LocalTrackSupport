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

public sealed partial class Plugin : IDalamudPlugin
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

    private static readonly string[] Names =
    {
        "Unclassified / Test Route",
        "Gentlebean's Fever",
        "Manderville-can Parade",
        "Saucery Siege",
        "The Gold Swiveller",
        "Manderville Mountain"
    };

    // Read-only navigation APIs.
    // There are deliberately NO movement IPC subscribers.

    private readonly ICallGateSubscriber<bool> navReady;

    private readonly ICallGateSubscriber<
        Vector3, Vector3, bool, Task<List<Vector3>>> findPath;

    private readonly ICallGateSubscriber<
        Vector3, Vector3, bool, Vector3, float,
        Task<List<Vector3>>> findAvoid;

    private readonly SplatoonBridge splatoon;
    private readonly RouteConfiguration config;

    private Task<List<Vector3>>? pending;
    private List<Vector3>? suggestedPath;

    private Vector3 requestedOrigin;
    private Vector3 requestedTarget;

    private DateTime requestStarted;
    private DateTime lastPathRequest;
    private DateTime lastOverlayUpdate;
    private DateTime lastNavPoll;
    private DateTime lastCastPoll;

    private uint lastTerritory;
    private uint hazardTerritory;

    private bool windowOpen;
    private bool showOverlay = true;
    private bool showEntireRoute;
    private bool guideRunning;
    private bool hazardEnabled;
    private bool navAvailable;
    private bool clearArmed;

    private int nextIndex = 1;

    private float hazardRadius = 3.0f;
    private Vector3 hazardCenter;

    private string status =
        "Visual-only navigation. Character movement is manual.";

    private readonly List<string> casts = new();
    private readonly Dictionary<ulong, uint> previousCasts = new();

    private bool InArea =>
        Client.TerritoryType is 1197 or 1165;

    public Plugin()
    {
        config = Pi.GetPluginConfig() as RouteConfiguration
            ?? new RouteConfiguration();

        config.Routes ??= new List<RecordedRoute>();

        config.SelectedSlot = Math.Clamp(
            config.SelectedSlot, 0, Names.Length - 1);

        navReady = Pi.GetIpcSubscriber<bool>(
            "vnavmesh.Nav.IsReady");

        findPath = Pi.GetIpcSubscriber<
            Vector3, Vector3, bool, Task<List<Vector3>>>(
            "vnavmesh.Nav.Pathfind");

        findAvoid = Pi.GetIpcSubscriber<
            Vector3, Vector3, bool, Vector3, float,
            Task<List<Vector3>>>(
            "vnavmesh.Nav.PathfindAvoid");

        splatoon = new SplatoonBridge(Pi, Log);

        lastTerritory = Client.TerritoryType;

        Commands.AddHandler("/bnav", new CommandInfo(OnCommand)
        {
            HelpMessage =
                "Open BlunderNav visual guidance. " +
                "/bnav stop hides active guidance."
        });

        Framework.Update += Update;

        Pi.UiBuilder.Draw += Draw;
        Pi.UiBuilder.OpenMainUi += Open;
        Pi.UiBuilder.OpenConfigUi += Open;

        Log.Information(
            "BlunderNav DEV5 FIX2: direct map IDs, visual guidance.");
    }

    private void Save()
    {
        Pi.SavePluginConfig(config);
    }

    private void Open()
    {
        windowOpen = true;
    }

    private void OnCommand(string command, string arguments)
    {
        if (arguments.Trim().Equals(
            "stop", StringComparison.OrdinalIgnoreCase))
        {
            StopGuide("Visual guidance stopped.");
            showOverlay = false;
            splatoon.Clear();
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

        // Only visual elements are removed.
        // No movement APIs are called.

        splatoon.Dispose();
    }

    private RecordedRoute CurrentRoute()
    {
        ushort territory =
            Client.TerritoryType == 1197
                ? (ushort)1197
                : (ushort)1165;

        string name = Names[config.SelectedSlot];

        foreach (var route in config.Routes)
        {
            if (route.Territory == territory &&
                route.Name == name)
            {
                route.Points ??= new List<RoutePoint>();
                return route;
            }
        }

        var created = new RecordedRoute
        {
            Territory = territory,
            Name = name
        };

        config.Routes.Add(created);
        Save();

        return created;
    }

    private static bool Valid(Vector3 point)
    {
        return float.IsFinite(point.X) &&
               float.IsFinite(point.Y) &&
               float.IsFinite(point.Z);
    }

    private void Record(Vector3 position, bool automatic)
    {
        if (!InArea || !Valid(position))
            return;

        var route = CurrentRoute();

        if (route.Points.Count >= 250)
        {
            config.AutoRecord = false;
            Save();
            status = "Checkpoint limit reached.";
            return;
        }

        if (route.Points.Count > 0)
        {
            float distance = Vector3.Distance(
                route.Points[^1].Position, position);

            float spacing = automatic
                ? config.RecordSpacing
                : 0.5f;

            if (distance < spacing)
                return;

            if (distance > 20f)
            {
                if (automatic)
                {
                    config.AutoRecord = false;
                    Save();
                }

                status = "Large displacement. Recording paused.";
                return;
            }
        }

        route.Points.Add(new RoutePoint(position));
        Save();

        status = $"Recorded checkpoint {route.Points.Count}.";
    }

    private void StopGuide(string message)
    {
        guideRunning = false;
        pending = null;
        suggestedPath = null;
        status = message;
    }

    // Find the nearest recorded line segment, rather than
    // requiring the player to stand at checkpoint one.
    //
    // Projection onto a 3D segment also distinguishes
    // overlapping paths at different heights.
    //
    // When several segments are equally close, prefer
    // the earlier recorded segment for predictable behavior.

    private static int FindNearestUpcomingCheckpoint(
        RecordedRoute route,
        Vector3 position,
        out float distance)
    {
        distance = float.MaxValue;

        if (route.Points.Count < 2)
            return 1;

        int bestSegment = 0;

        for (int i = 0; i < route.Points.Count - 1; i++)
        {
            Vector3 a = route.Points[i].Position;
            Vector3 b = route.Points[i + 1].Position;
            Vector3 segment = b - a;

            float squaredLength = segment.LengthSquared();

            float t = squaredLength > 0.0001f
                ? Math.Clamp(
                    Vector3.Dot(position - a, segment) /
                    squaredLength,
                    0f, 1f)
                : 0f;

            Vector3 closest = a + segment * t;

            float candidateDistance =
                Vector3.Distance(position, closest);

            if (candidateDistance < distance - 0.01f)
            {
                distance = candidateDistance;
                bestSegment = i;
            }
        }

        return bestSegment + 1;
    }

    private void ResetSuggestion()
    {
        pending = null;
        suggestedPath = null;
        lastPathRequest = DateTime.MinValue;
    }

    private void RejoinGuide(Vector3 position)
    {
        var route = CurrentRoute();

        if (route.Points.Count < 2)
        {
            StopGuide("Route has too few checkpoints.");
            return;
        }

        nextIndex = FindNearestUpcomingCheckpoint(
            route, position, out float distance);

        ResetSuggestion();

        status =
            $"Rejoined near segment {nextIndex}/" +
            $"{route.Points.Count - 1}. " +
            $"Distance from recorded trail: {distance:F1} yalms.";
    }

    private void StartGuide()
    {
        if (!InArea || Objects.LocalPlayer == null)
        {
            status = "Enter Blunderville first.";
            return;
        }

        var route = CurrentRoute();

        if (route.Points.Count < 2)
        {
            status = "Record at least two checkpoints.";
            return;
        }

        Vector3 position = Objects.LocalPlayer.Position;

        int selected = FindNearestUpcomingCheckpoint(
            route, position, out float distance);

        config.AutoRecord = false;
        Save();

        guideRunning = true;
        showOverlay = true;
        nextIndex = selected;

        ResetSuggestion();

        status =
            $"Visual guidance started near segment " +
            $"{selected}/{route.Points.Count - 1}. " +
            $"Distance from trail: {distance:F1} yalms. " +
            "Movement remains manual.";
    }

    private void ObserveCasts()
    {
        var active = new Dictionary<ulong, uint>();

        foreach (var obj in Objects)
        {
            if (obj is not IBattleChara actor)
                continue;

            if (!actor.IsCasting || actor.CastActionId == 0)
                continue;

            ulong id = actor.GameObjectId;
            uint action = actor.CastActionId;

            active[id] = action;

            if (previousCasts.TryGetValue(id, out uint old) &&
                old == action)
                continue;

            casts.Insert(0,
                $"{DateTime.Now:HH:mm:ss} | " +
                $"Action {action} | " +
                $"{actor.Name} | " +
                $"X {actor.Position.X:F1} Z {actor.Position.Z:F1}");

            if (casts.Count > 30)
                casts.RemoveAt(casts.Count - 1);
        }

        previousCasts.Clear();

        foreach (var pair in active)
            previousCasts[pair.Key] = pair.Value;
    }

    private static float DistanceXZ(
        Vector3 point,
        Vector3 a,
        Vector3 b)
    {
        var ab = new Vector2(b.X - a.X, b.Z - a.Z);
        var ap = new Vector2(point.X - a.X, point.Z - a.Z);

        float square = ab.LengthSquared();

        if (square < 0.0001f)
            return ap.Length();

        float t = Math.Clamp(
            Vector2.Dot(ap, ab) / square, 0f, 1f);

        return Vector2.Distance(
            new Vector2(point.X, point.Z),
            new Vector2(a.X, a.Z) + ab * t);
    }

    private bool ValidateSuggestion(
        List<Vector3>? path,
        out string reason)
    {
        reason = "";

        if (path == null || path.Count == 0 ||
            path.Count > 256)
        {
            reason = "Invalid path result.";
            return false;
        }

        Vector3 previous = requestedOrigin;
        float length = 0f;

        foreach (var point in path)
        {
            if (!Valid(point))
            {
                reason = "Invalid path coordinates.";
                return false;
            }

            float segment = Vector3.Distance(previous, point);
            length += segment;

            if (length > 90f)
            {
                reason = "Suggested path too long.";
                return false;
            }

            if (hazardEnabled &&
                hazardTerritory == Client.TerritoryType &&
                DistanceXZ(hazardCenter, previous, point) <
                    hazardRadius)
            {
                reason = "Path intersects marked hazard.";
                return false;
            }

            previous = point;
        }

        if (Vector3.Distance(
            path[^1], requestedTarget) > 2.5f)
        {
            reason = "Path endpoint mismatch.";
            return false;
        }

        return true;
    }

    private void RequestSuggestion(Vector3 origin)
    {
        if (!guideRunning || !navAvailable ||
            pending != null || !InArea)
            return;

        var route = CurrentRoute();

        if (nextIndex >= route.Points.Count)
            return;

        var destination = route.Points[nextIndex].Position;

        if (Vector3.Distance(origin, destination) > 25f)
        {
            suggestedPath = null;
            status = "Next checkpoint too distant for suggestion.";
            return;
        }

        requestedOrigin = origin;
        requestedTarget = destination;

        try
        {
            if (hazardEnabled &&
                hazardTerritory == Client.TerritoryType)
            {
                pending = findAvoid.InvokeFunc(
                    origin,
                    destination,
                    false,
                    hazardCenter,
                    hazardRadius + 0.5f);
            }
            else
            {
                pending = findPath.InvokeFunc(
                    origin,
                    destination,
                    false);
            }

            requestStarted = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            pending = null;
            suggestedPath = null;

            status = "Path suggestion unavailable: " +
                ex.GetBaseException().Message;
        }
    }

    private void Update(IFramework framework)
    {
        var now = DateTime.UtcNow;
        uint territory = Client.TerritoryType;
        var player = Objects.LocalPlayer;

        if (territory != lastTerritory)
        {
            lastTerritory = territory;

            StopGuide("Territory changed. Guidance stopped.");

            config.AutoRecord = false;
            Save();

            hazardEnabled = false;
            clearArmed = false;
            previousCasts.Clear();
            splatoon.Clear();
        }

        if (!InArea || player == null)
        {
            if ((now - lastOverlayUpdate).TotalSeconds > 1)
            {
                lastOverlayUpdate = now;
                splatoon.Clear();
            }

            return;
        }

        var position = player.Position;

        UpdateCourseSelection(position, now);

        if ((now - lastNavPoll).TotalMilliseconds >= 1000)
        {
            lastNavPoll = now;

            try
            {
                navAvailable = navReady.InvokeFunc();
            }
            catch
            {
                navAvailable = false;
            }
        }

        if ((now - lastCastPoll).TotalMilliseconds >= 300)
        {
            lastCastPoll = now;
            ObserveCasts();
        }

        if (config.AutoRecord && !guideRunning)
            Record(position, true);

        if (guideRunning)
        {
            var route = CurrentRoute();

            while (nextIndex < route.Points.Count &&
                Vector3.Distance(
                    position,
                    route.Points[nextIndex].Position) <= 1.6f)
            {
                nextIndex++;

                pending = null;
                suggestedPath = null;
                lastPathRequest = DateTime.MinValue;
            }

            if (nextIndex >= route.Points.Count)
            {
                StopGuide("Recorded route completed manually.");
            }
            else
            {
                if (pending != null && pending.IsCompleted)
                {
                    var task = pending;
                    pending = null;

                    try
                    {
                        var result = task.GetAwaiter().GetResult();

                        string problem = "Player moved during pathfinding.";

                        if (Vector3.Distance(
                            position, requestedOrigin) <= 5f &&
                            ValidateSuggestion(result, out problem))
                        {
                            suggestedPath = result;
                        }
                        else
                        {
                            suggestedPath = null;
                            status = "Suggestion rejected: " + problem;
                        }
                    }
                    catch (Exception ex)
                    {
                        suggestedPath = null;
                        status = "Pathfinding failed: " +
                            ex.GetBaseException().Message;
                    }
                }

                if (pending != null &&
                    (now - requestStarted).TotalSeconds > 7)
                {
                    pending = null;
                    suggestedPath = null;
                    status = "Pathfinding timed out.";
                }

                if ((now - lastPathRequest).TotalSeconds >= 1.5)
                {
                    lastPathRequest = now;

                    if (pending == null)
                        RequestSuggestion(position);
                }
            }
        }

        if ((now - lastOverlayUpdate).TotalMilliseconds >= 650)
        {
            lastOverlayUpdate = now;
            RefreshOverlay();
        }
    }

    private void RefreshOverlay()
    {
        int liveCourse = IdentifyCourseMapId();

        bool suppressDutyOverlay =
            Client.TerritoryType == 1165 &&
            (liveCourse < 1 ||
             (config.AutoSelectCourse &&
              config.SelectedSlot != liveCourse));

        if (!showOverlay || !InArea || suppressDutyOverlay)
        {
            splatoon.Clear();
            return;
        }

        var route = CurrentRoute();

        var lines = new List<VisualLine>();

        // Muted cyan: recorded route reference.
        // Bright green: current calculated suggestion.

        const uint recordedColor = 0xA0FFCC55;
        const uint suggestionColor = 0xEE55FF44;
        const uint targetColor = 0xE040FF88;
        const uint dangerColor = 0xDD3030FF;

        int begin = 0;
        int end = route.Points.Count - 1;

        if (guideRunning && !showEntireRoute)
        {
            begin = Math.Max(0, nextIndex - 1);
            // Keep only the next six recorded segments visible.
            end = Math.Min(end, begin + 6);
        }

        for (int i = begin; i < end; i++)
        {
            lines.Add(new VisualLine(
                route.Points[i].Position,
                route.Points[i + 1].Position,
                recordedColor,
                2.5f));
        }

        if (guideRunning && suggestedPath != null)
        {
            var player = Objects.LocalPlayer;

            if (player != null && suggestedPath.Count > 0)
            {
                Vector3 previous = player.Position;

                foreach (var point in suggestedPath)
                {
                    if (Vector3.Distance(previous, point) > 0.05f)
                    {
                        lines.Add(new VisualLine(
                            previous, point,
                            suggestionColor, 5f));
                    }

                    previous = point;
                }
            }
        }

        VisualCircle? target = null;

        if (guideRunning && nextIndex < route.Points.Count)
        {
            target = new VisualCircle(
                route.Points[nextIndex].Position,
                0.8f,
                targetColor,
                4f);
        }

        VisualCircle? danger = null;

        if (hazardEnabled &&
            hazardTerritory == Client.TerritoryType)
        {
            danger = new VisualCircle(
                hazardCenter,
                hazardRadius,
                dangerColor,
                4f);
        }

        splatoon.Publish(lines, target, danger);
    }

    private void Draw()
    {
        DrawCompactUi();
    }
}