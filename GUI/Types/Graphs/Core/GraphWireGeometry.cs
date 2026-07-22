namespace GUI.Types.Graphs.Core;

/// <summary>
/// The wire curve shared by rendering, hit testing and layout measurement, so all three agree
/// on where a wire actually runs.
/// </summary>
internal static class GraphWireGeometry
{
    /// <summary>Longest a backward wire's handle may grow.</summary>
    private const float BackwardHandleLimit = 250f;

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
    /// Extra horizontal reach for one wire of a fan, so wires sharing a socket peel off at
    /// visibly different distances instead of overlapping. Index is the wire's rank among the
    /// socket's wires sorted by the height of their far end.
    /// </summary>
    public static float FanOffset(GraphLayoutOptions options, int index, int count)
    {
        if (count <= 1 || !options.Has(GraphLayoutFeature.SocketFanSpread))
        {
            return 0f;
        }

        return Math.Min(index * options.SocketFanStep, options.SocketFanLimit);
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

        var fan = geometry.FanReachOf(wire);

        if (straight)
        {
            var stub = 14f + fan;
            into.Add(from);
            into.Add(new Vector2(from.X + stub, from.Y));
            into.Add(new Vector2(to.X - stub, to.Y));
            into.Add(to);
            return;
        }

        var offset = HandleOffset(from, to) + fan;
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
