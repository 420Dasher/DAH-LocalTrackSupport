using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlunderNav;

public static class PathSmoothing
{
    // Experimental geometric smoothing.
    //
    // Original navmesh path is always retained as fallback.
    // Each proposed curve is sampled against reachable mesh polygons.
    //
    // Point-on-mesh tests are NOT full swept-volume collision checks.
    // Restrict experimental use to open lobby areas.

    public static List<Vector3> Build(
        List<Vector3> original,
        float radius,
        Func<Vector3, bool> isWalkable,
        out int accepted,
        out int rejected)
    {
        accepted = 0;
        rejected = 0;

        if (original.Count < 3 || original.Count > 600)
            return new List<Vector3>(original);

        radius = Math.Clamp(radius, 0.25f, 1.0f);

        var output = new List<Vector3>(original.Count * 3);
        output.Add(original[0]);

        for (int i = 1; i < original.Count - 1; i++)
        {
            Vector3 previous = original[i - 1];
            Vector3 corner = original[i];
            Vector3 next = original[i + 1];

            Vector3 incoming = corner - previous;
            Vector3 outgoing = next - corner;

            float incomingLength = incoming.Length();
            float outgoingLength = outgoing.Length();

            // Reject very small legs and significant height changes.

            if (incomingLength < 0.55f ||
                outgoingLength < 0.55f ||
                MathF.Abs(incoming.Y) > 0.35f ||
                MathF.Abs(outgoing.Y) > 0.35f)
            {
                AddDistinct(output, corner);
                continue;
            }

            Vector3 directionIn = incoming / incomingLength;
            Vector3 directionOut = outgoing / outgoingLength;

            float dot = Vector3.Dot(directionIn, directionOut);

            // No curve required for nearly straight lines.
            // Avoid smoothing U-turns or severe reversals.

            if (dot > 0.985f || dot < -0.35f)
            {
                AddDistinct(output, corner);
                continue;
            }

            float inset = MathF.Min(
                radius,
                MathF.Min(incomingLength, outgoingLength) * 0.28f);

            if (inset < 0.15f)
            {
                AddDistinct(output, corner);
                continue;
            }

            Vector3 entry = corner - directionIn * inset;
            Vector3 exit = corner + directionOut * inset;

            // Quadratic Bezier control point: original corner.
            // Sample the actual curve at short intervals.

            int steps = Math.Clamp(
                (int)MathF.Ceiling((inset * 2f) / 0.18f),
                5,
                16);

            var candidates = new List<Vector3>(steps + 1);
            candidates.Add(entry);

            bool valid = true;

            try
            {
                if (!isWalkable(entry) || !isWalkable(exit))
                    valid = false;

                if (valid)
                {
                    for (int step = 1; step < steps; step++)
                    {
                        float t = (float)step / steps;
                        float u = 1f - t;

                        Vector3 point =
                            u * u * entry +
                            2f * u * t * corner +
                            t * t * exit;

                        if (!IsFinite(point) ||
                            !isWalkable(point))
                        {
                            valid = false;
                            break;
                        }

                        candidates.Add(point);
                    }
                }
            }
            catch
            {
                // IPC unavailable: use the original path.
                accepted = 0;
                rejected = 0;
                return new List<Vector3>(original);
            }

            if (!valid)
            {
                rejected++;
                AddDistinct(output, corner);
                continue;
            }

            candidates.Add(exit);

            foreach (var point in candidates)
                AddDistinct(output, point);

            accepted++;

            if (output.Count > 1800)
            {
                accepted = 0;
                rejected = 0;
                return new List<Vector3>(original);
            }
        }

        AddDistinct(output, original[^1]);

        // Never change the destination.
        // Fall back if the generated path exceeds the safety limit.

        if (output.Count > 1800 ||
            Vector3.Distance(output[^1], original[^1]) > 0.01f)
        {
            accepted = 0;
            rejected = 0;
            return new List<Vector3>(original);
        }

        return output;
    }

    private static bool IsFinite(Vector3 p)
    {
        return float.IsFinite(p.X) &&
               float.IsFinite(p.Y) &&
               float.IsFinite(p.Z);
    }

    private static void AddDistinct(
        List<Vector3> points,
        Vector3 candidate)
    {
        if (points.Count == 0 ||
            Vector3.Distance(points[^1], candidate) > 0.015f)
        {
            points.Add(candidate);
        }
    }
}