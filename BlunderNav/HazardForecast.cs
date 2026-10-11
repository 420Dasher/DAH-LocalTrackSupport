using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;

namespace BlunderNav;

public sealed partial class Plugin
{
    // DEV6.3 - RESEARCH ONLY
    //
    // Initial 34812 square geometry is informed by
    // vFallguy's public Manderville Mountain research.
    //
    // This is an independent implementation using
    // BlunderNav's existing cast observer and Splatoon
    // visual line primitives.
    //
    // Cast-start observation has polling latency.
    // Actual hit timing has NOT been calibrated.
    //
    // Red is an experimental predicted-overlap warning,
    // NOT a verified hit and NOT an instruction to move.
    //
    // No route avoidance or movement is performed.

    private sealed class MountainCastAnchor
    {
        public ulong CasterId;
        public float X;
        public DateTime ObservedUtc;
    }

    private readonly Dictionary<ulong, MountainCastAnchor>
        mountainCastAnchors = new();

    private DateTime mountainLastMotionUtc =
        DateTime.MinValue;

    private Vector3 mountainLastPlayerPosition;
    private Vector3 mountainVelocity;

    private int mountainVisibleSquares;
    private int mountainProjectedOverlaps;
    private int mountainObservedCount;

    // Approximate five-step square geometry.
    // Positions are XYZ, not XZY.
    //
    // These are model coordinates for action 34812.
    // They are not discovered dynamically by the game.

    private static readonly Vector3[] MountainSquareSteps =
    {
        new(0f, 25.59f, 190.40f),
        new(0f, 23.37f, 196.40f),
        new(0f, 21.15f, 202.40f),
        new(0f, 18.94f, 208.40f),
        new(0f, 16.73f, 214.40f)
    };

    private static readonly float[] MountainLanes =
    {
        -12f, -6f, 0f, 6f, 12f
    };

    private void ResetMountainHazards()
    {
        mountainCastAnchors.Clear();

        mountainLastMotionUtc = DateTime.MinValue;
        mountainVelocity = Vector3.Zero;

        mountainVisibleSquares = 0;
        mountainProjectedOverlaps = 0;
        mountainObservedCount = 0;
    }

    private void ObserveMountainCast(
        IBattleChara actor,
        DateTime now)
    {
        if (!config.HazardForecastEnabled ||
            Client.TerritoryType != 1165 ||
            Client.MapId != 882 ||
            actor.CastActionId != 34812)
        {
            return;
        }

        Vector3 p = actor.Position;

        if (!Valid(p) ||
            MathF.Abs(p.Z - 190.4f) > 2.0f ||
            MathF.Abs(p.Y - 25.59f) > 2.0f)
        {
            return;
        }

        float lane = 0f;
        float nearest = float.MaxValue;

        foreach (float candidate in MountainLanes)
        {
            float distance = MathF.Abs(
                candidate - p.X);

            if (distance < nearest)
            {
                nearest = distance;
                lane = candidate;
            }
        }

        // Only recognize the mapped lane emitters.
        // Other 34812 casts must not be assigned one
        // of these hazard shapes by accident.

        if (nearest > 1.0f)
            return;

        mountainCastAnchors[actor.GameObjectId] =
            new MountainCastAnchor
            {
                CasterId = actor.GameObjectId,
                X = lane,
                ObservedUtc = now
            };

        mountainObservedCount++;
    }

    private void UpdateMountainMotion(
        Vector3 position,
        DateTime now)
    {
        if (Client.TerritoryType != 1165 ||
            Client.MapId != 882 ||
            !config.HazardForecastEnabled)
        {
            if (mountainCastAnchors.Count > 0)
                ResetMountainHazards();

            return;
        }

        if (mountainLastMotionUtc != DateTime.MinValue)
        {
            float dt = (float)(
                now - mountainLastMotionUtc).TotalSeconds;

            if (dt >= 0.03f && dt <= 0.5f)
            {
                Vector3 velocity =
                    (position - mountainLastPlayerPosition)
                    / dt;

                // Reject teleports, large corrections,
                // and unreliable instantaneous speeds.

                if (Valid(velocity) &&
                    velocity.Length() < 12f)
                {
                    mountainVelocity = velocity;
                }
                else
                {
                    mountainVelocity = Vector3.Zero;
                }
            }
        }

        mountainLastPlayerPosition = position;
        mountainLastMotionUtc = now;

        // Cast-based forecasts are short-lived.
        // Do not keep displaying them after the
        // observed activation window has passed.

        foreach (var pair in mountainCastAnchors.ToArray())
        {
            double age = (
                now - pair.Value.ObservedUtc
            ).TotalSeconds;

            if (age < 0 || age > 4.5)
                mountainCastAnchors.Remove(pair.Key);
        }
    }

    private static bool WithinMountainSquare(
        Vector3 point,
        Vector3 center,
        float halfWidth)
    {
        return MathF.Abs(point.X - center.X) <= halfWidth &&
               MathF.Abs(point.Z - center.Z) <= halfWidth &&
               MathF.Abs(point.Y - center.Y) <= 5f;
    }

    private static void AppendMountainSquare(
        List<VisualLine> lines,
        Vector3 center,
        float halfWidth,
        uint color)
    {
        Vector3 a = center +
            new Vector3(-halfWidth, 0f, -halfWidth);

        Vector3 b = center +
            new Vector3(-halfWidth, 0f, +halfWidth);

        Vector3 c = center +
            new Vector3(+halfWidth, 0f, +halfWidth);

        Vector3 d = center +
            new Vector3(+halfWidth, 0f, -halfWidth);

        lines.Add(new VisualLine(a, b, color, 3.5f));
        lines.Add(new VisualLine(b, c, color, 3.5f));
        lines.Add(new VisualLine(c, d, color, 3.5f));
        lines.Add(new VisualLine(d, a, color, 3.5f));
    }

    private void AppendMountainHazards(
        List<VisualLine> lines,
        Vector3 playerPosition)
    {
        mountainVisibleSquares = 0;
        mountainProjectedOverlaps = 0;

        if (!config.HazardForecastEnabled ||
            Client.TerritoryType != 1165 ||
            Client.MapId != 882 ||
            !showOverlay)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;

        // Color values use Splatoon's ABGR ordering.
        //
        // Blue: early forecast.
        // Yellow: near predicted activation.
        // Red: predicted player overlap near activation.
        //
        // The absolute impact time is approximate;
        // cast polling can introduce additional delay.

        const uint earlyColor = 0xDDFFAA33;
        const uint soonColor = 0xDD00DDFF;
        const uint overlapColor = 0xEE3030FF;

        const float halfWidth = 3.0f;

        foreach (var anchor in mountainCastAnchors.Values)
        {
            double elapsed = (
                now - anchor.ObservedUtc
            ).TotalSeconds;

            for (int step = 0;
                 step < MountainSquareSteps.Length;
                 step++)
            {
                // First impact estimated roughly one
                // second after the observed cast start.
                //
                // Each following square advances by
                // about half a second.
                //
                // The true timing must be tested.

                float until = 1.0f +
                    step * 0.5f -
                    (float)elapsed;

                if (until > 2.4f || until < -0.25f)
                    continue;

                Vector3 template =
                    MountainSquareSteps[step];

                Vector3 center = new(
                    anchor.X,
                    template.Y,
                    template.Z);

                // Keep the display near the character
                // rather than filling the entire course.

                if (Vector3.Distance(
                    playerPosition, center) > 85f)
                {
                    continue;
                }

                float predictionTime =
                    Math.Clamp(until, 0f, 2.4f);

                Vector3 predictedPosition =
                    playerPosition +
                    mountainVelocity * predictionTime;

                bool predictedOverlap =
                    WithinMountainSquare(
                        predictedPosition,
                        center,
                        halfWidth + 0.5f);

                bool urgentOverlap =
                    until <= 0.65f &&
                    until >= -0.15f &&
                    predictedOverlap;

                uint color = urgentOverlap
                    ? overlapColor
                    : until <= 1.25f
                        ? soonColor
                        : earlyColor;

                AppendMountainSquare(
                    lines,
                    center,
                    halfWidth,
                    color);

                mountainVisibleSquares++;

                if (urgentOverlap)
                    mountainProjectedOverlaps++;

                // Bound the number of live elements.
                if (mountainVisibleSquares >= 28)
                    return;
            }
        }
    }

    private void DrawMountainHazardUi()
    {
        if (Client.MapId != 882 ||
            Client.TerritoryType != 1165)
        {
            return;
        }

        ImGui.TextDisabled("LIVE HAZARD FORECAST - DEV6.3");

        bool enabled = config.HazardForecastEnabled;

        if (ImGui.Checkbox(
            "Show experimental 34812 hazard forecasts",
            ref enabled))
        {
            config.HazardForecastEnabled = enabled;

            ResetMountainHazards();
            Save();
            splatoon.Clear();
        }

        if (!enabled)
        {
            ImGui.TextDisabled(
                "Live hazard forecast disabled.");
            return;
        }

        ImGui.TextWrapped(
            "Blue: early forecast | Yellow: approaching | " +
            "Red: predicted player overlap.");

        ImGui.TextDisabled(
            $"Cast anchors: {mountainCastAnchors.Count} | " +
            $"Visible squares: {mountainVisibleSquares} | " +
            $"Overlap warnings: {mountainProjectedOverlaps}");

        ImGui.TextWrapped(
            "Research preview only. Cast timing and " +
            "actual hit windows are not yet verified. " +
            "No hazard avoidance is applied to routes.");
    }

    private string BuildMountainHazardReport()
    {
        var report = new StringBuilder();

        report.AppendLine(
            "=== BLUNDERNAV DEV6.3 HAZARD RESEARCH ===");

        report.AppendLine($"UTC: {DateTime.UtcNow:O}");
        report.AppendLine($"Territory: {Client.TerritoryType}");
        report.AppendLine($"Map ID: {Client.MapId}");

        report.AppendLine(
            $"Enabled: {config.HazardForecastEnabled}");

        report.AppendLine(
            $"Observed cast starts: {mountainObservedCount}");

        report.AppendLine(
            $"Active anchors: {mountainCastAnchors.Count}");

        report.AppendLine(
            $"Visible forecast squares: {mountainVisibleSquares}");

        report.AppendLine(
            $"Projected overlap warnings: " +
            $"{mountainProjectedOverlaps}");

        report.AppendLine(
            $"Player velocity XYZ: " +
            $"{mountainVelocity.X:F2}; " +
            $"{mountainVelocity.Y:F2}; " +
            $"{mountainVelocity.Z:F2}");

        foreach (var cast in mountainCastAnchors.Values
            .OrderByDescending(e => e.ObservedUtc))
        {
            report.AppendLine(
                $"34812 | Caster 0x{cast.CasterId:X} | " +
                $"Lane X={cast.X:F1} | " +
                $"Observed {cast.ObservedUtc:O}");
        }

        report.AppendLine(
            "Square model: half-width 3 yalms.");

        report.AppendLine(
            "Timing estimate: first square +1.0s, " +
            "subsequent squares +0.5s each.");

        report.AppendLine(
            "Uncertainty: cast polling, action effect " +
            "delay, phase and actual hit duration.");

        report.AppendLine(
            "Red means predicted overlap, not a " +
            "verified incoming hit.");

        report.AppendLine(
            "No automatic movement or route avoidance.");

        return report.ToString();
    }

    private void DrawMountainHazardDiagnostics()
    {
        if (Client.MapId != 882 ||
            Client.TerritoryType != 1165)
        {
            return;
        }

        ImGui.Separator();
        ImGui.TextDisabled("LIVE 34812 RESEARCH");

        ImGui.TextWrapped(
            $"Observed {mountainObservedCount} cast starts. " +
            $"{mountainCastAnchors.Count} active anchors.");

        ImGui.TextDisabled(
            $"Current forecast squares: " +
            $"{mountainVisibleSquares}");

        if (ImGui.Button("Copy hazard timing report"))
        {
            ImGui.SetClipboardText(
                BuildMountainHazardReport());

            status = "Hazard timing report copied.";
        }

        ImGui.SameLine();

        if (ImGui.Button("Reset hazard observations"))
        {
            ResetMountainHazards();
            splatoon.Clear();

            status = "Hazard observations reset.";
        }
    }
}