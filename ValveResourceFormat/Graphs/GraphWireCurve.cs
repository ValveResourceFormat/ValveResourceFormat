using System.Linq;

namespace ValveResourceFormat.Graphs;

/// <summary>
/// The curve a wire is drawn along: a bezier with horizontal handles between the two sockets, and
/// for a routed wire one such bezier between each pair of consecutive points of its route, so the
/// wire leaves and enters every waypoint level. Drawing and layout share it, so the layout judges a
/// wire on the line that ends up on screen.
/// </summary>
internal static class GraphWireCurve
{
    /// <summary>Longest a backward wire's handle may grow.</summary>
    private const float BackwardHandleLimit = 250f;

    private const int CurveSamples = 24;
    private const int RouteSamplesPerSpan = 10;

    /// <summary>
    /// Length of the horizontal bezier handles. It grows with horizontal distance and is damped
    /// when the endpoints are nearly level, so a wire between aligned sockets draws straight.
    /// </summary>
    private static float HandleOffset(Vector2 from, Vector2 to)
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

    /// <summary>The cubic bezier spans the wire is drawn as, in drawing order.</summary>
    /// <param name="from">Pivot of the source socket.</param>
    /// <param name="waypoints">The route the wire is drawn through, or null for a plain curve.</param>
    /// <param name="to">Pivot of the target socket.</param>
    public static IEnumerable<(Vector2 Start, Vector2 Control0, Vector2 Control1, Vector2 End)> Spans(Vector2 from, IReadOnlyList<Vector2>? waypoints, Vector2 to)
    {
        var previous = from;

        foreach (var point in (waypoints ?? []).Append(to))
        {
            var offset = HandleOffset(previous, point);
            yield return (previous, previous + new Vector2(offset, 0f), point - new Vector2(offset, 0f), point);
            previous = point;
        }
    }

    /// <summary>Replaces the contents of <paramref name="into"/> with points along the drawn wire.</summary>
    /// <param name="from">Pivot of the source socket.</param>
    /// <param name="waypoints">The route the wire is drawn through, or null for a plain curve.</param>
    /// <param name="to">Pivot of the target socket.</param>
    /// <param name="into">Receives the polyline, starting at <paramref name="from"/> and ending at <paramref name="to"/>.</param>
    public static void Sample(Vector2 from, IReadOnlyList<Vector2>? waypoints, Vector2 to, List<Vector2> into)
    {
        into.Clear();

        var samples = waypoints is { Count: > 0 } ? RouteSamplesPerSpan : CurveSamples;

        foreach (var (start, control0, control1, end) in Spans(from, waypoints, to))
        {
            for (var s = into.Count == 0 ? 0 : 1; s <= samples; s++)
            {
                var t = s / (float)samples;
                var u = 1f - t;
                into.Add(u * u * u * start + 3f * u * u * t * control0 + 3f * u * t * t * control1 + t * t * t * end);
            }
        }
    }
}
