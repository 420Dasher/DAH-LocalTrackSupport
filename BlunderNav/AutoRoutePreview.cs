using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;

namespace BlunderNav;

public sealed partial class Plugin
{
    // DEV6.2 EXPERIMENTAL
    //
    // Only two race finish crossings have been confirmed.
    // Saucery Siege uses manually selected test objectives.
    //
    // No player movement is performed.
    // A navmesh path is NOT proof of hazard safety.

    private Task<List<Vector3>>? autoPreviewPending;
    private List<Vector3>? autoPreviewPath;

    private Vector3 autoPreviewOrigin;
    private Vector3 autoPreviewTarget;

    private uint autoPreviewMapId;
    private DateTime autoPreviewRequestedUtc = DateTime.MinValue;
    private DateTime autoPreviewLastRequestUtc = DateTime.MinValue;

    private float autoPreviewLength;
    private int sauceryTestTarget;

    private string autoPreviewStatus =
        "Waiting for a supported objective.";

    private bool AutoPreviewShown =>
        config.AutoPreviewEnabled &&
        !guideRunning &&
        showOverlay &&
        Client.TerritoryType == 1165 &&
        Client.MapId == autoPreviewMapId &&
        autoPreviewPath != null &&
        autoPreviewPath.Count > 0;

    private void ResetAutoPreview()
    {
        // Discard the previous result. The underlying IPC
        // task may finish later, but cannot be reused.

        autoPreviewPending = null;
        autoPreviewPath = null;
        autoPreviewMapId = 0;
        autoPreviewLength = 0f;

        autoPreviewLastRequestUtc = DateTime.MinValue;
        autoPreviewRequestedUtc = DateTime.MinValue;

        autoPreviewStatus = "Waiting for a route calculation.";
    }

    private void ResetAutoPreviewForMapChange()
    {
        ResetAutoPreview();
        sauceryTestTarget = 0;
    }

    private bool TryGetAutoPreviewGoal(
        out Vector3 goal,
        out string name)
    {
        goal = default;
        name = "";

        if (Client.TerritoryType != 1165)
            return false;

        switch (Client.MapId)
        {
            case 878:
                goal = new Vector3(
                    -198.32f, 13.59f, -364.44f);
                name = "Gentlebean's Fever finish";
                return true;

            case 879:
                goal = new Vector3(
                    -3.38f, 18.94f, -379.94f);
                name = "Manderville-can Parade finish";
                return true;

            case 881:
                switch (sauceryTestTarget)
                {
                    case 1:
                        goal = new Vector3(
                            -200.26f, 6.00f, 204.02f);
                        name = "Blue delivery (manual test)";
                        return true;

                    case 2:
                        goal = new Vector3(
                            -241.04f, 6.00f, 274.59f);
                        name = "Red delivery (manual test)";
                        return true;

                    case 3:
                        goal = new Vector3(
                            -158.90f, 6.00f, 273.09f);
                        name = "Yellow delivery (manual test)";
                        return true;

                    default:
                        goal = new Vector3(
                            -200.30f, 5.46f, 252.90f);
                        name = "Central crystal pickup";
                        return true;
                }
        }

        return false;
    }

    private static bool ValidateLongPath(
        List<Vector3>? points,
        Vector3 origin,
        Vector3 target,
        out float length,
        out string error)
    {
        length = 0f;
        error = "";

        if (points == null ||
            points.Count == 0 ||
            points.Count > 1024)
        {
            error = "Missing or oversized path result.";
            return false;
        }

        if (!Valid(points[0]) ||
            Vector3.Distance(origin, points[0]) > 35f)
        {
            error = "Path does not begin near player.";
            return false;
        }

        Vector3 previous = origin;

        foreach (var point in points)
        {
            if (!Valid(point))
            {
                error = "Invalid path coordinates.";
                return false;
            }

            float segment = Vector3.Distance(
                previous, point);

            if (segment > 140f)
            {
                error = "Unexpected large path segment.";
                return false;
            }

            length += segment;

            if (length > 1000f)
            {
                error = "Path exceeds safety length limit.";
                return false;
            }

            previous = point;
        }

        if (Vector3.Distance(
            points[^1], target) > 4.5f)
        {
            error = "Path does not reach the objective.";
            return false;
        }

        return true;
    }

    private void UpdateAutoPreview(
        Vector3 position,
        DateTime now)
    {
        if (!config.AutoPreviewEnabled ||
            !showOverlay ||
            guideRunning ||
            Client.TerritoryType != 1165 ||
            IdentifyCourseMapId() < 1)
        {
            if (autoPreviewPending != null ||
                autoPreviewPath != null)
            {
                ResetAutoPreview();
            }

            return;
        }

        if (!TryGetAutoPreviewGoal(
            out Vector3 goal,
            out string name))
        {
            if (autoPreviewPending != null ||
                autoPreviewPath != null)
            {
                ResetAutoPreview();
            }

            autoPreviewStatus =
                "Research only: no verified destination.";
            return;
        }

        if (autoPreviewMapId != Client.MapId ||
            Vector3.Distance(
                goal, autoPreviewTarget) > 0.1f)
        {
            ResetAutoPreview();

            autoPreviewMapId = Client.MapId;
            autoPreviewTarget = goal;
        }

        if (Vector3.Distance(position, goal) < 3f)
        {
            autoPreviewPending = null;
            autoPreviewPath = null;

            autoPreviewStatus =
                "At objective area. Crossing/qualification " +
                "must be confirmed in-game.";

            return;
        }

        if (!navAvailable)
        {
            autoPreviewPending = null;
            autoPreviewPath = null;
            autoPreviewStatus = "vnavmesh is unavailable.";
            return;
        }

        if (autoPreviewPending != null)
        {
            if (autoPreviewPending.IsCompleted)
            {
                var finished = autoPreviewPending;
                autoPreviewPending = null;

                try
                {
                    var result =
                        finished.GetAwaiter().GetResult();

                    if (Vector3.Distance(
                        position, autoPreviewOrigin) > 18f)
                    {
                        autoPreviewPath = null;
                        autoPreviewStatus =
                            "Player moved. Recalculating.";
                    }
                    else if (!ValidateLongPath(
                        result,
                        autoPreviewOrigin,
                        autoPreviewTarget,
                        out float length,
                        out string reason))
                    {
                        autoPreviewPath = null;
                        autoPreviewStatus =
                            "No usable path: " + reason;
                    }
                    else
                    {
                        autoPreviewPath = result;
                        autoPreviewLength = length;

                        autoPreviewStatus =
                            $"Calculated {length:F0}y path " +
                            $"({result.Count} nodes). " +
                            "Hazards NOT evaluated.";
                    }
                }
                catch (Exception ex)
                {
                    autoPreviewPath = null;

                    autoPreviewStatus =
                        "Pathfinder failed: " +
                        ex.GetBaseException().Message;
                }
            }
            else if ((now - autoPreviewRequestedUtc)
                .TotalSeconds > 8)
            {
                autoPreviewPending = null;
                autoPreviewPath = null;
                autoPreviewLastRequestUtc = now;

                autoPreviewStatus =
                    "Path calculation timed out.";
            }
        }

        if (autoPreviewPending != null)
            return;

        // Limit IPC requests. A validated path is reused
        // briefly while the player advances. Failed queries
        // back off for longer to avoid repeated expensive work.

        double cooldown = autoPreviewPath == null
            ? 9.0
            : 5.0;

        if ((now - autoPreviewLastRequestUtc)
            .TotalSeconds < cooldown)
            return;

        if (autoPreviewPath != null &&
            Vector3.Distance(
                position, autoPreviewOrigin) < 6f &&
            (now - autoPreviewLastRequestUtc)
                .TotalSeconds < 15)
        {
            return;
        }

        if (Vector3.Distance(position, goal) > 800f)
        {
            autoPreviewPath = null;
            autoPreviewStatus =
                "Destination outside preview distance limit.";
            return;
        }

        autoPreviewOrigin = position;
        autoPreviewRequestedUtc = now;
        autoPreviewLastRequestUtc = now;

        try
        {
            // Read-only path request. No movement IPC.
            //
            // The existing guide uses separate path state,
            // so neither workflow can consume the other's
            // asynchronous result.

            autoPreviewPending = findPath.InvokeFunc(
                position,
                goal,
                false);

            autoPreviewStatus =
                $"Calculating path to {name}...";
        }
        catch (Exception ex)
        {
            autoPreviewPending = null;
            autoPreviewPath = null;

            autoPreviewStatus =
                "Path IPC unavailable: " +
                ex.GetBaseException().Message;
        }
    }

    private void AppendAutoPreviewLines(
        List<VisualLine> lines,
        Vector3 playerPosition)
    {
        if (!AutoPreviewShown ||
            autoPreviewPath == null)
            return;

        if (Vector3.Distance(
            playerPosition, autoPreviewOrigin) > 22f)
            return;

        // Draw only the first 130 yalms of the returned
        // path to keep Splatoon overlays readable.

        const uint color = 0xEE55FF44;
        const float maxVisibleLength = 130f;

        float visibleLength = 0f;
        Vector3 previous = playerPosition;
        int count = 0;

        foreach (var next in autoPreviewPath)
        {
            if (count >= 90 ||
                visibleLength >= maxVisibleLength)
                break;

            float segment = Vector3.Distance(
                previous, next);

            if (segment <= 0.05f)
            {
                previous = next;
                continue;
            }

            float remaining =
                maxVisibleLength - visibleLength;

            Vector3 endpoint = segment > remaining
                ? previous +
                    (next - previous) *
                    (remaining / segment)
                : next;

            lines.Add(new VisualLine(
                previous,
                endpoint,
                color,
                4.0f));

            visibleLength += Vector3.Distance(
                previous, endpoint);

            previous = endpoint;
            count++;
        }
    }

    private void DrawAutoPreviewUi(Vector3 position)
    {
        ImGui.TextDisabled("LIVE CALCULATED ROUTE");

        bool enabled = config.AutoPreviewEnabled;

        if (ImGui.Checkbox(
            "Automatic path preview (experimental)",
            ref enabled))
        {
            config.AutoPreviewEnabled = enabled;

            ResetAutoPreview();
            Save();
            splatoon.Clear();
        }

        if (!enabled)
        {
            ImGui.TextDisabled("Path preview disabled.");
            return;
        }

        if (Client.MapId == 881)
        {
            ImGui.TextWrapped(
                "Crystal color detection is pending. " +
                "Choose a destination manually for " +
                "this pathfinding test.");

            string[] targets =
            {
                "Central pickup",
                "Blue delivery",
                "Red delivery",
                "Yellow delivery"
            };

            int choice = sauceryTestTarget;

            if (ImGui.Combo(
                "Test destination",
                ref choice,
                targets,
                targets.Length))
            {
                sauceryTestTarget = choice;

                ResetAutoPreview();
                splatoon.Clear();
            }
        }

        if (TryGetAutoPreviewGoal(
            out Vector3 goal,
            out string goalName))
        {
            ImGui.TextWrapped(
                "Destination: " + goalName);

            ImGui.TextDisabled(
                $"Goal XYZ: {goal.X:F1}, " +
                $"{goal.Y:F1}, {goal.Z:F1}");

            ImGui.TextWrapped(autoPreviewStatus);

            if (autoPreviewPath != null)
            {
                ImGui.TextDisabled(
                    $"Returned path: " +
                    $"{autoPreviewLength:F0} yalms");
            }

            if (ImGui.Button("Recalculate now"))
            {
                ResetAutoPreview();
                splatoon.Clear();
            }
        }
        else
        {
            ImGui.TextWrapped(
                "No calculated route yet. This course " +
                "still needs verified objective mechanics.");
        }

        ImGui.TextDisabled(
            "Read-only vnavmesh | No hazard guarantees");
    }
}