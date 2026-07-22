using System.Runtime.CompilerServices;

namespace GUI.Types.Graphs.Core;

/// <summary>
/// The wire curve shared by rendering, hit testing and layout measurement, so all three agree
/// on where a wire actually runs.
/// </summary>
internal static class GraphWireGeometry
{
    /// <summary>Longest a backward wire's handle may grow.</summary>
    private const float BackwardHandleLimit = 250f;

    /// <summary>Horizontal run a straight wire leaves its socket on before angling away.</summary>
    public const float StraightStub = 14f;

    /// <summary>
    /// Whether two axis-aligned boxes overlap, taken as separate min/max floats so the callers
    /// that keep wire bounds in parallel arrays do not have to build a struct per test. This runs
    /// tens of millions of times inside the crossing repair.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool BoxesOverlap(
        float minXA, float maxXA, float minYA, float maxYA,
        float minXB, float maxXB, float minYB, float maxYB)
        => minXA <= maxXB && minXB <= maxXA && minYA <= maxYB && minYB <= maxYA;

    /// <summary>
    /// Whether a segment enters an axis-aligned box, by ending inside it or cutting an edge.
    /// A bounding-box overlap is not enough: a long diagonal wire can span a card's box while
    /// passing well clear of the card itself.
    /// </summary>
    public static bool SegmentCrossesBox(Vector2 a, Vector2 b, Vector2 min, Vector2 max)
    {
        if ((a.X >= min.X && a.X <= max.X && a.Y >= min.Y && a.Y <= max.Y)
            || (b.X >= min.X && b.X <= max.X && b.Y >= min.Y && b.Y <= max.Y))
        {
            return true;
        }

        var topRight = new Vector2(max.X, min.Y);
        var bottomLeft = new Vector2(min.X, max.Y);

        return SegmentsIntersect(a, b, min, topRight)
            || SegmentsIntersect(a, b, topRight, max)
            || SegmentsIntersect(a, b, max, bottomLeft)
            || SegmentsIntersect(a, b, bottomLeft, min);
    }

    /// <summary>Whether two segments properly cross, by the sign of the four orientation tests.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        var d1 = Cross(p3, p4, p1);
        var d2 = Cross(p3, p4, p2);
        var d3 = Cross(p1, p2, p3);
        var d4 = Cross(p1, p2, p4);

        return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f))
            && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));

        static float Cross(Vector2 a, Vector2 b, Vector2 point)
            => (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);
    }

    /// <summary>
    /// Length of the horizontal bezier handles. It grows with horizontal distance and is damped
    /// when the endpoints are nearly level, so a wire between aligned sockets draws straight.
    /// </summary>
    public static float HandleOffset(Vector2 from, Vector2 to)
    {
        var dx = to.X - from.X;
        var distX = Math.Abs(dx);
        var distY = Math.Abs(to.Y - from.Y);

        if (dx >= 0f)
        {
            var clampFactor = distX <= 0f ? 1f : Math.Min(1f, 3.5f * distY / distX);
            return 0.4f * distX * clampFactor;
        }

        return Math.Min(0.5f * distX + 40f, BackwardHandleLimit);
    }

    /// <summary>
    /// Samples the path a wire draws along into <paramref name="into"/>. Routed wires flatten to
    /// their waypoints; everything else flattens the cubic the renderer builds.
    /// </summary>
    public static void Sample(List<Vector2> into, GraphGeometry geometry, GraphWire wire, bool straight = false, int segments = 16)
    {
        into.Clear();

        var from = geometry.PivotOf(wire.From);
        var to = geometry.PivotOf(wire.To);
        var route = geometry.TryRouteOf(wire);

        if (route?.Waypoints is { Count: > 0 } waypoints)
        {
            into.Add(from);
            into.AddRange(waypoints);
            into.Add(to);
            return;
        }

        if (straight)
        {
            const float stub = StraightStub;
            into.Add(from);
            into.Add(new Vector2(from.X + stub, from.Y));
            into.Add(new Vector2(to.X - stub, to.Y));
            into.Add(to);
            return;
        }

        var offset = HandleOffset(from, to);
        var c1 = new Vector2(from.X + offset, from.Y);
        var c2 = new Vector2(to.X - offset, to.Y);

        for (var i = 0; i <= segments; i++)
        {
            var t = (float)i / segments;
            var inv = 1f - t;

            into.Add(
                inv * inv * inv * from +
                3f * inv * inv * t * c1 +
                3f * inv * t * t * c2 +
                t * t * t * to);
        }
    }
}
