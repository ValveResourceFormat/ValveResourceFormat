using System.Runtime.CompilerServices;

namespace ValveResourceFormat.Graphs;

/// <summary>
/// Intersection tests the placement and repair passes measure wires with. A wire is taken as the
/// straight run between its two socket pivots here, whatever curve the caller later draws.
/// </summary>
internal static class GraphSegmentGeometry
{
    /// <summary>Inset on every card edge in <see cref="CrossesCard"/>, so a wire grazing a corner does not count.</summary>
    public const float GrazeInset = 2f;

    /// <summary>
    /// Whether a segment is drawn across a card at <paramref name="position"/>, with the card's box
    /// taken <see cref="GrazeInset"/> in from every edge.
    /// </summary>
    public static bool CrossesCard(Vector2 a, Vector2 b, Vector2 position, Vector2 size)
    {
        var min = position + new Vector2(GrazeInset);
        var max = position + size - new Vector2(GrazeInset);

        return BoxesOverlap(Math.Min(a.X, b.X), Math.Max(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y), min.X, max.X, min.Y, max.Y)
            && SegmentCrossesBox(a, b, min, max);
    }

    /// <summary>Whether two axis-aligned boxes, given as their min/max floats, overlap.</summary>
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

    /// <summary>Distance from a point to the closest point of a segment.</summary>
    public static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var along = b - a;
        var lengthSquared = along.LengthSquared();
        var t = lengthSquared > 0f ? MathUtils.Saturate(Vector2.Dot(point - a, along) / lengthSquared) : 0f;
        return Vector2.Distance(point, a + (along * t));
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
            => Vector2.Cross(b - a, point - a);
    }
}
