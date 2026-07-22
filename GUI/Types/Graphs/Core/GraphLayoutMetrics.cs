using System.Globalization;
using System.Linq;

namespace GUI.Types.Graphs.Core;

/// <summary>Measured readability of one laid-out graph. Lower is better for every count.</summary>
readonly record struct GraphLayoutMetrics(
    int Nodes,
    int Wires,
    int WireCrossings,
    int WiresOverNodes,
    int BackwardWires,
    int StraightWires,
    float TotalWireLength,
    float MeanDockOffset,
    Vector2 Extent)
{
    /// <summary>Wall time to parse the asset and build the node model, including its first layout.</summary>
    public double BuildMilliseconds { get; init; }

    /// <summary>Wall time the placement itself took.</summary>
    public double LayoutMilliseconds { get; init; }

    /// <summary>Wall time to draw the whole graph to a raster canvas once.</summary>
    public double RenderMilliseconds { get; init; }

    /// <summary>Canvas area in megapixels; lower is a more compact drawing.</summary>
    public float Area => Extent.X * Extent.Y / 1_000_000f;

    public static string CsvHeader
        => "variant,nodes,wires,crossings,wires_over_cards,backward,straight,total_length,mean_dock_offset,"
            + "width,height,area_mpx,build_ms,layout_ms,render_ms";

    public string ToCsvRow(string stage) => string.Join(',',
    [
        stage,
        Nodes.ToString(CultureInfo.InvariantCulture),
        Wires.ToString(CultureInfo.InvariantCulture),
        WireCrossings.ToString(CultureInfo.InvariantCulture),
        WiresOverNodes.ToString(CultureInfo.InvariantCulture),
        BackwardWires.ToString(CultureInfo.InvariantCulture),
        StraightWires.ToString(CultureInfo.InvariantCulture),
        TotalWireLength.ToString("F0", CultureInfo.InvariantCulture),
        MeanDockOffset.ToString("F1", CultureInfo.InvariantCulture),
        Extent.X.ToString("F0", CultureInfo.InvariantCulture),
        Extent.Y.ToString("F0", CultureInfo.InvariantCulture),
        Area.ToString("F2", CultureInfo.InvariantCulture),
        BuildMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
        LayoutMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
        RenderMilliseconds.ToString("F1", CultureInfo.InvariantCulture),
    ]);
}

/// <summary>
/// Scores a laid-out graph so a layout change can be compared against the one before it rather
/// than judged by eye.
/// </summary>
internal static class GraphLayoutScorer
{
    /// <summary>A wire counts as straight when its two ends sit within this many pixels.</summary>
    private const float StraightTolerance = 1.5f;

    /// <summary>
    /// Every crossing wire pair, named, with where both ends sit. This is the only way to tell
    /// whether a given crossing is a socket-order problem, a node-order problem or unavoidable.
    /// </summary>
    public static List<string> DescribeCrossings(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphWire> wires, GraphGeometry geometry, bool straight = false)
    {
        var visibleWires = wires.Where(static w => !w.From.Owner.Hidden && !w.To.Owner.Hidden).ToList();
        var paths = new List<Vector2>[visibleWires.Count];
        var bounds = new (Vector2 Min, Vector2 Max)[visibleWires.Count];
        var buffer = new List<Vector2>();
        var described = new List<string>();

        for (var i = 0; i < visibleWires.Count; i++)
        {
            GraphWireGeometry.Sample(buffer, geometry, visibleWires[i], straight);
            paths[i] = [.. buffer];
            bounds[i] = BoundsOf(paths[i]);
        }

        for (var i = 0; i < visibleWires.Count; i++)
        {
            for (var j = i + 1; j < visibleWires.Count; j++)
            {
                if (visibleWires[i].From == visibleWires[j].From || visibleWires[i].To == visibleWires[j].To
                    || visibleWires[i].From == visibleWires[j].To || visibleWires[i].To == visibleWires[j].From)
                {
                    continue;
                }

                if (Overlaps(bounds[i], bounds[j]) && PathsIntersect(paths[i], paths[j]))
                {
                    described.Add($"{Describe(visibleWires[i], geometry)}\n      X {Describe(visibleWires[j], geometry)}");
                }
            }
        }

        return described;

        static string Describe(GraphWire wire, GraphGeometry geometry)
        {
            var from = geometry.PivotOf(wire.From);
            var to = geometry.PivotOf(wire.To);

            return $"{Trim(wire.From.Owner.Title)}.{Trim(wire.From.Name)} @({from.X:F0},{from.Y:F0})"
                + $" -> {Trim(wire.To.Owner.Title)}.{Trim(wire.To.Name)} @({to.X:F0},{to.Y:F0})";
        }

        static string Trim(string text) => text.Length > 34 ? text[..34] : text;
    }

    /// <summary>
    /// Where one card sits and where each of its wires docks, with the height that would make
    /// each wire run level. Answers "why is this node here and not lower" with numbers.
    /// </summary>
    public static List<string> DescribeNodes(IReadOnlyList<GraphNode> nodes, GraphGeometry geometry, string filter)
    {
        var lines = new List<string>();

        foreach (var node in nodes.Where(n => n.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            var size = geometry.SizeOf(node);
            lines.Add($"{node.Title} at ({node.Position.X:F0},{node.Position.Y:F0}) size {size.X:F0}x{size.Y:F0}");

            foreach (var socket in node.Inputs.Concat(node.Outputs))
            {
                foreach (var wire in socket.Wires)
                {
                    var mine = geometry.PivotOf(socket);
                    var other = wire.From == socket ? wire.To : wire.From;
                    var theirs = geometry.PivotOf(other);

                    lines.Add($"    {(socket.IsInput ? "in " : "out")} {Pad(socket.Name)} @y{mine.Y:F0}"
                        + $" <-> {Pad(other.Owner.Title)}.{Pad(other.Name)} @y{theirs.Y:F0}"
                        + $"   level would need dy {theirs.Y - mine.Y:+0;-0;0}");
                }
            }
        }

        return lines;

        static string Pad(string text) => (text.Length > 26 ? text[..26] : text).PadRight(26);
    }

    /// <summary>
    /// For each card matching the filter, every wire into it in dock order alongside the source's
    /// position and how many cards feed that source exclusively. Shows whether two crossing wires
    /// into one card could be uncrossed by exchanging the branches behind them.
    /// </summary>
    public static List<string> DescribeInputOrder(IReadOnlyList<GraphNode> nodes, GraphGeometry geometry, string filter)
    {
        var lines = new List<string>();

        foreach (var node in nodes.Where(n => n.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            lines.Add($"{node.Title} at ({node.Position.X:F0},{node.Position.Y:F0})");

            foreach (var socket in node.Inputs)
            {
                foreach (var wire in socket.Wires)
                {
                    var source = wire.From.Owner;
                    var branch = new HashSet<GraphNode>();
                    Upstream(source, node, branch);

                    lines.Add($"    dock y{geometry.PivotOf(socket).Y:F0} '{socket.Name}'"
                        + $" <- {source.Title} out y{geometry.PivotOf(wire.From).Y:F0}"
                        + $" at x{source.Position.X:F0}, exclusive upstream {branch.Count}");
                }
            }

            // Wires drawn across this card, with the move that would clear each one and whatever
            // in the same column stands in the way of taking it.
            var box = (Min: node.Position, Max: node.Position + geometry.SizeOf(node));

            foreach (var other in nodes)
            {
                foreach (var wire in other.Outputs.SelectMany(static s => s.Wires))
                {
                    if (wire.From.Owner == node || wire.To.Owner == node)
                    {
                        continue;
                    }

                    var a = geometry.PivotOf(wire.From);
                    var b = geometry.PivotOf(wire.To);

                    if (!GraphWireGeometry.SegmentCrossesBox(a, b, box.Min, box.Max))
                    {
                        continue;
                    }

                    var span = Math.Abs(b.X - a.X);
                    var t = span > 0.01f ? Math.Clamp(((box.Min.X + box.Max.X) / 2f - Math.Min(a.X, b.X)) / span, 0f, 1f) : 0f;
                    var left = a.X <= b.X ? a : b;
                    var right = a.X <= b.X ? b : a;
                    var height = left.Y + ((right.Y - left.Y) * t);

                    var blockers = nodes.Where(n => n != node
                        && Math.Abs(n.Position.X - node.Position.X) < 8f
                        && Math.Abs((n.Position.Y + geometry.SizeOf(n).Y / 2f) - (box.Min.Y + box.Max.Y) / 2f) < 400f)
                        .Select(n => $"{n.Title}@y{n.Position.Y:F0}");

                    lines.Add($"    CROSSED BY {wire.From.Owner.Title} -> {wire.To.Owner.Title}"
                        + $" at y{height:F0}; card y{box.Min.Y:F0}..{box.Max.Y:F0};"
                        + $" down {height + 32f - box.Min.Y:+0;-0;0} up {height - 32f - box.Max.Y:+0;-0;0};"
                        + $" column neighbours: {string.Join(", ", blockers)}");
                }
            }

            // Pairwise, on both the chord the repair scores and the curve the renderer draws, so
            // a disagreement between the two shows up here rather than as a mismatch with the eye.
            var incoming = node.Inputs.SelectMany(static s => s.Wires).ToList();
            var buffer = new List<Vector2>();
            var sampled = new List<List<Vector2>>();

            foreach (var wire in incoming)
            {
                GraphWireGeometry.Sample(buffer, geometry, wire);
                sampled.Add([.. buffer]);
            }

            for (var i = 0; i < incoming.Count; i++)
            {
                for (var j = i + 1; j < incoming.Count; j++)
                {
                    var a1 = geometry.PivotOf(incoming[i].From);
                    var a2 = geometry.PivotOf(incoming[i].To);
                    var b1 = geometry.PivotOf(incoming[j].From);
                    var b2 = geometry.PivotOf(incoming[j].To);

                    lines.Add($"    pair {incoming[i].From.Owner.Title} x {incoming[j].From.Owner.Title}:"
                        + $" chord {GraphWireGeometry.SegmentsIntersect(a1, a2, b1, b2)}"
                        + $", curve {PathsIntersect(sampled[i], sampled[j])}"
                        + $"  [({a1.X:F0},{a1.Y:F0})->({a2.X:F0},{a2.Y:F0})]"
                        + $" [({b1.X:F0},{b1.Y:F0})->({b2.X:F0},{b2.Y:F0})]");
                }
            }
        }

        return lines;

        // Everything reachable backwards from source without passing through the consumer.
        static void Upstream(GraphNode node, GraphNode stop, HashSet<GraphNode> seen)
        {
            if (node == stop || !seen.Add(node))
            {
                return;
            }

            foreach (var socket in node.Inputs)
            {
                foreach (var wire in socket.Wires)
                {
                    Upstream(wire.From.Owner, stop, seen);
                }
            }
        }
    }

    public static GraphLayoutMetrics Measure(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphWire> wires, GraphGeometry geometry, bool straight = false)
    {
        var visibleNodes = nodes.Where(static n => !n.Hidden).ToList();
        var visibleWires = wires.Where(static w => !w.From.Owner.Hidden && !w.To.Owner.Hidden).ToList();

        var paths = new List<Vector2>[visibleWires.Count];
        var bounds = new (Vector2 Min, Vector2 Max)[visibleWires.Count];
        var buffer = new List<Vector2>();

        var totalLength = 0f;
        var dockOffsetSum = 0f;
        var backward = 0;
        var levelWires = 0;
        var dockCounted = 0;

        for (var i = 0; i < visibleWires.Count; i++)
        {
            var wire = visibleWires[i];
            GraphWireGeometry.Sample(buffer, geometry, wire, straight);
            paths[i] = [.. buffer];
            bounds[i] = BoundsOf(paths[i]);

            for (var p = 1; p < paths[i].Count; p++)
            {
                totalLength += Vector2.Distance(paths[i][p - 1], paths[i][p]);
            }

            if (wire.From.Owner == wire.To.Owner)
            {
                continue;
            }

            var from = geometry.PivotOf(wire.From);
            var to = geometry.PivotOf(wire.To);
            var dockOffset = Math.Abs(to.Y - from.Y);

            dockOffsetSum += dockOffset;
            dockCounted++;

            if (to.X < from.X)
            {
                backward++;
            }
            else if (dockOffset <= StraightTolerance)
            {
                levelWires++;
            }
        }

        return new GraphLayoutMetrics(
            visibleNodes.Count,
            visibleWires.Count,
            CountCrossings(visibleWires, paths, bounds),
            CountWiresOverNodes(visibleNodes, visibleWires, paths, bounds, geometry),
            backward,
            levelWires,
            totalLength,
            dockCounted > 0 ? dockOffsetSum / dockCounted : 0f,
            ExtentOf(visibleNodes, geometry));
    }

    // Pairwise over wire polylines with an AABB reject. Wires meeting at a shared socket are
    // touching by construction, not crossing, so those pairs are skipped.
    private static int CountCrossings(List<GraphWire> wires, List<Vector2>[] paths, (Vector2 Min, Vector2 Max)[] bounds)
    {
        var crossings = 0;

        for (var i = 0; i < wires.Count; i++)
        {
            for (var j = i + 1; j < wires.Count; j++)
            {
                if (wires[i].From == wires[j].From || wires[i].To == wires[j].To
                    || wires[i].From == wires[j].To || wires[i].To == wires[j].From)
                {
                    continue;
                }

                if (!Overlaps(bounds[i], bounds[j]))
                {
                    continue;
                }

                if (PathsIntersect(paths[i], paths[j]))
                {
                    crossings++;
                }
            }
        }

        return crossings;
    }

    // A wire running across a card it is not attached to is the most damaging overlap there is,
    // so it is counted separately from wire-on-wire crossings.
    private static int CountWiresOverNodes(
        List<GraphNode> nodes,
        List<GraphWire> wires,
        List<Vector2>[] paths,
        (Vector2 Min, Vector2 Max)[] bounds,
        GraphGeometry geometry)
    {
        var offences = 0;

        for (var i = 0; i < wires.Count; i++)
        {
            foreach (var node in nodes)
            {
                if (node == wires[i].From.Owner || node == wires[i].To.Owner)
                {
                    continue;
                }

                var min = node.Position;
                var max = node.Position + geometry.SizeOf(node);

                if (!Overlaps(bounds[i], (min, max)))
                {
                    continue;
                }

                if (PathEntersRect(paths[i], min, max))
                {
                    offences++;
                }
            }
        }

        return offences;
    }

    private static Vector2 ExtentOf(List<GraphNode> nodes, GraphGeometry geometry)
    {
        if (nodes.Count == 0)
        {
            return Vector2.Zero;
        }

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);

        foreach (var node in nodes)
        {
            min = Vector2.Min(min, node.Position);
            max = Vector2.Max(max, node.Position + geometry.SizeOf(node));
        }

        return max - min;
    }

    private static (Vector2 Min, Vector2 Max) BoundsOf(List<Vector2> path)
    {
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);

        foreach (var point in path)
        {
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        return (min, max);
    }

    private static bool Overlaps((Vector2 Min, Vector2 Max) a, (Vector2 Min, Vector2 Max) b)
        => GraphWireGeometry.BoxesOverlap(a.Min.X, a.Max.X, a.Min.Y, a.Max.Y, b.Min.X, b.Max.X, b.Min.Y, b.Max.Y);

    private static bool PathsIntersect(List<Vector2> a, List<Vector2> b)
    {
        for (var i = 1; i < a.Count; i++)
        {
            for (var j = 1; j < b.Count; j++)
            {
                if (SegmentsIntersect(a[i - 1], a[i], b[j - 1], b[j]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool PathEntersRect(List<Vector2> path, Vector2 min, Vector2 max)
    {
        foreach (var point in path)
        {
            if (point.X >= min.X && point.X <= max.X && point.Y >= min.Y && point.Y <= max.Y)
            {
                return true;
            }
        }

        var corners = new[]
        {
            min,
            new Vector2(max.X, min.Y),
            max,
            new Vector2(min.X, max.Y),
        };

        for (var i = 1; i < path.Count; i++)
        {
            for (var c = 0; c < 4; c++)
            {
                if (SegmentsIntersect(path[i - 1], path[i], corners[c], corners[(c + 1) % 4]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
        => GraphWireGeometry.SegmentsIntersect(p1, p2, p3, p4);
}
