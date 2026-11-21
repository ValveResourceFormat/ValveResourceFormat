namespace ValveResourceFormat.Graphs;

/// <summary>
/// What a wire sweeps when its near end travels vertically through a range of heights while its far
/// end stays put: a triangle fanning out from the far end, grown by a margin. A segment that stays
/// clear of it, of the far end and of the path of the near end crosses the wire at every height in
/// the range or at none.
/// </summary>
internal readonly struct WireSweep
{
    private readonly Vector2 far;
    private readonly Vector2 low;
    private readonly Vector2 high;
    private readonly float margin;
    private readonly Vector2 boundsMin;
    private readonly Vector2 boundsMax;

    /// <summary>Inward normals of the triangle's edges, and the offsets a point inside clears them by.</summary>
    private readonly Vector2 normal0;
    private readonly Vector2 normal1;
    private readonly Vector2 normal2;
    private readonly float offset0;
    private readonly float offset1;
    private readonly float offset2;
    private readonly bool flat;

    public WireSweep(Vector2 far, Vector2 near, float lowest, float highest, float margin)
    {
        this.far = far;
        low = near with { Y = near.Y + lowest };
        high = near with { Y = near.Y + highest };
        this.margin = margin;
        boundsMin = Vector2.Min(far, low) - new Vector2(margin);
        boundsMax = Vector2.Max(far, high) + new Vector2(margin);

        var orientation = Math.Sign(Vector2.Cross(low - far, high - far));
        flat = orientation == 0;
        (normal0, offset0) = Edge(far, low);
        (normal1, offset1) = Edge(low, high);
        (normal2, offset2) = Edge(high, far);

        (Vector2, float) Edge(Vector2 from, Vector2 to)
        {
            var along = to - from;
            var normal = new Vector2(-along.Y, along.X) * orientation;
            return (normal, Vector2.Dot(normal, from) - (margin * along.Length()));
        }
    }

    /// <summary>Whether a box overlaps the bounds of the sweep grown by the margin.</summary>
    public bool Overlaps(float minX, float maxX, float minY, float maxY)
        => GraphSegmentGeometry.BoxesOverlap(minX, maxX, minY, maxY, boundsMin.X, boundsMax.X, boundsMin.Y, boundsMax.Y);

    /// <summary>Whether segment <paramref name="a"/>-<paramref name="b"/> comes within the margin of the sweep.</summary>
    public bool Reaches(Vector2 a, Vector2 b)
    {
        if (!Overlaps(Math.Min(a.X, b.X), Math.Max(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y)))
        {
            return false;
        }

        return Inside(a) || Inside(b)
            || GraphSegmentGeometry.DistanceToSegment(far, a, b) <= margin
            || GraphSegmentGeometry.SegmentCrossesBox(a, b, new Vector2(low.X - margin, low.Y - margin), new Vector2(low.X + margin, high.Y + margin));
    }

    private bool Inside(Vector2 point)
    {
        if (flat)
        {
            return GraphSegmentGeometry.DistanceToSegment(point, far, low) <= margin
                || GraphSegmentGeometry.DistanceToSegment(point, low, high) <= margin
                || GraphSegmentGeometry.DistanceToSegment(point, high, far) <= margin;
        }

        return Vector2.Dot(normal0, point) >= offset0
            && Vector2.Dot(normal1, point) >= offset1
            && Vector2.Dot(normal2, point) >= offset2;
    }
}
