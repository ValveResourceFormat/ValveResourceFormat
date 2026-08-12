using System.Runtime.CompilerServices;

namespace ValveResourceFormat.Graphs;

/// <summary>
/// Intersection tests the placement and repair passes measure wires with. A wire is taken as the
/// straight run between its two socket pivots here, whatever curve the caller later draws.
/// </summary>
internal static class GraphSegmentGeometry
{
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
}
