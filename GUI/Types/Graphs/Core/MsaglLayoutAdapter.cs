using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Core.Routing;
using Microsoft.Msagl.Layout.Layered;
using MsaglPoint = Microsoft.Msagl.Core.Geometry.Point;
using MsaglRectangle = Microsoft.Msagl.Core.Geometry.Rectangle;

namespace GUI.Types.Graphs.Core;

/// <summary>Node placement engine; wire routing is bundled either way.</summary>
enum GraphPlacement
{
    /// <summary>MDS stress majorization.</summary>
    Organic,

    /// <summary>Layered Sugiyama flow, left to right.</summary>
    Layered,
}

/// <summary>
/// Runs MSAGL's Sugiyama layout with bundled edge routing over a component and writes the
/// node positions and exact wire curves back into the graph model.
/// </summary>
/// <remarks>
/// MSAGL lays out top-down in a Y-up plane. Instead of using its Transformation option (which
/// rotates the node boxes and breaks the reported geometry), the graph is fed with transposed
/// node dimensions and the resulting coordinates are transposed back, turning the top-down
/// layout into our left-to-right Y-down canvas.
///
/// Edges carry no ports: port pinning destabilizes the bundler on large graphs, and the
/// terminal runs are spliced onto the socket pivots afterwards instead.
/// </remarks>
internal static class MsaglLayoutAdapter
{
    static MsaglLayoutAdapter()
    {
        // The published package was built with its TEST_MSAGL debug paths active; those
        // paths invoke these static visualization callbacks, which are only ever assigned
        // by MSAGL's own debugger UI and crash headless hosts. No-ops neutralize them.
        LayoutAlgorithmSettings.Show = _ => { };
        LayoutAlgorithmSettings.ShowDebugCurves = _ => { };
        LayoutAlgorithmSettings.ShowDebugCurvesEnumeration = _ => { };
        LayoutAlgorithmSettings.ShowDatabase = (_, _) => { };
        LayoutAlgorithmSettings.ShowGraph = _ => { };
    }

    public static void Layout(List<GraphNode> component, List<GraphWire> componentWires, GraphPlacement placement, GraphGeometry geometry)
    {
        if (component.Count == 0)
        {
            return;
        }

        var (geometryGraph, msaglNodes, msaglEdges, selfWires) = BuildGeometry(component, componentWires, useCurrentPositions: false, geometry);

        if (placement == GraphPlacement.Organic)
        {
            try
            {
                CalculateLayout(geometryGraph, new Microsoft.Msagl.Layout.MDS.MdsLayoutSettings
                {
                    RemoveOverlaps = true,
                    NodeSeparation = 36,
                });
                WriteBackAndRoute(msaglNodes, msaglEdges, selfWires, geometry);
                return;
            }
            catch (Exception e)
            {
                // The MDS engine NREs internally (CdtSweeper) on some large islands; those
                // get the layered placement instead while the rest of the graph stays organic.
                GUI.Utils.Log.Debug(nameof(MsaglLayoutAdapter), $"Organic placement failed for an island, falling back to layered: {e.InnerException?.Message ?? e.Message}");
            }
        }

        CalculateLayout(geometryGraph, new SugiyamaLayoutSettings
        {
            LayerSeparation = 220,
            NodeSeparation = 36,
        });
        WriteBackAndRoute(msaglNodes, msaglEdges, selfWires, geometry);
    }

    private static void CalculateLayout(GeometryGraph geometryGraph, LayoutAlgorithmSettings settings)
    {
        settings.EdgeRoutingSettings.EdgeRoutingMode = EdgeRoutingMode.SplineBundling;
        settings.EdgeRoutingSettings.BundlingSettings = CreateBundlingSettings();
        Microsoft.Msagl.Miscellaneous.LayoutHelpers.CalculateLayout(geometryGraph, settings, null);
    }

    private static void WriteBackAndRoute(Dictionary<GraphNode, Node> msaglNodes, List<(Edge Edge, GraphWire Wire)> msaglEdges, List<GraphWire> selfWires, GraphGeometry geometry)
    {
        // Transpose back: MSAGL (x right, y up, flow top-to-bottom) -> ours (x right = flow, y down).
        foreach (var (node, msaglNode) in msaglNodes)
        {
            var center = msaglNode.Center;
            var size = geometry.SizeOf(node);
            node.Position = new Vector2(
                (float)(-center.Y) - size.X / 2f,
                (float)center.X - size.Y / 2f);
        }

        ApplyRoutes(msaglEdges, selfWires, geometry);
    }

    /// <summary>
    /// Re-routes the wires of a component around the CURRENT node positions without moving
    /// any node; used after the user drags nodes so bundles reform around the new layout.
    /// </summary>
    public static void RouteComponent(List<GraphNode> component, List<GraphWire> componentWires, GraphGeometry geometry)
    {
        if (component.Count == 0)
        {
            return;
        }

        var (geometryGraph, _, msaglEdges, selfWires) = BuildGeometry(component, componentWires, useCurrentPositions: true, geometry);

        var router = new Microsoft.Msagl.Routing.SplineRouter(geometryGraph, 12, 20, Math.PI / 6, CreateBundlingSettings());
        router.Run();

        ApplyRoutes(msaglEdges, selfWires, geometry);
    }

    // Zero separation collapses a bundle's parallel metro lines into one visually merged
    // trunk; the raised ink importance makes the router share paths eagerly.
    private static BundlingSettings CreateBundlingSettings() => new()
    {
        EdgeSeparation = 0.0,
        InkImportance = 0.2,
    };

    // Packed island cloud targets a wide-screen shape so first fit-to-view wastes little space.
    private const double PackingAspect = 16.0 / 9.0;

    /// <summary>
    /// Packs component bounding boxes of the given sizes and returns each box's packed
    /// top-left origin. Boxes end up <paramref name="padding"/> apart.
    /// </summary>
    public static Vector2[] PackComponents(IReadOnlyList<Vector2> sizes, float padding)
    {
        var rectangles = new List<RectangleToPack<int>>(sizes.Count);

        for (var i = 0; i < sizes.Count; i++)
        {
            var corner = new MsaglPoint(sizes[i].X + padding, sizes[i].Y + padding);
            rectangles.Add(new RectangleToPack<int>(new MsaglRectangle(new MsaglPoint(0, 0), corner), i));
        }

        new OptimalRectanglePacking<int>(rectangles, PackingAspect).Run();

        var origins = new Vector2[sizes.Count];

        foreach (var rectangle in rectangles)
        {
            origins[rectangle.Data] = new Vector2(
                (float)rectangle.Rectangle.Left + padding / 2f,
                (float)rectangle.Rectangle.Bottom + padding / 2f);
        }

        return origins;
    }

    private static (GeometryGraph Graph, Dictionary<GraphNode, Node> Nodes, List<(Edge Edge, GraphWire Wire)> Edges, List<GraphWire> SelfWires) BuildGeometry(
        List<GraphNode> component, List<GraphWire> componentWires, bool useCurrentPositions, GraphGeometry geometry)
    {
        var geometryGraph = new GeometryGraph();
        var msaglNodes = new Dictionary<GraphNode, Node>(component.Count);

        foreach (var node in component)
        {
            var size = geometry.SizeOf(node);

            // Transposed: our width separates layers (vertical in MSAGL), our height stacks in-layer.
            var msaglNode = new Node(CurveFactory.CreateRectangle(size.Y, size.X, new MsaglPoint()))
            {
                UserData = node,
            };

            if (useCurrentPositions)
            {
                var center = node.Position + size / 2f;
                msaglNode.Center = new MsaglPoint(center.Y, -center.X);
            }

            msaglNodes[node] = msaglNode;
            geometryGraph.Nodes.Add(msaglNode);
        }

        var msaglEdges = new List<(Edge Edge, GraphWire Wire)>();
        var selfWires = new List<GraphWire>();

        foreach (var wire in componentWires)
        {
            if (!msaglNodes.TryGetValue(wire.From.Owner, out var fromNode) ||
                !msaglNodes.TryGetValue(wire.To.Owner, out var toNode))
            {
                continue;
            }

            // Name-group merging can produce self-referencing wires; the router cannot route
            // node-to-itself, so they get a synthetic loop over the node instead.
            if (fromNode == toNode)
            {
                selfWires.Add(wire);
                continue;
            }

            var edge = new Edge(fromNode, toNode);
            geometryGraph.Edges.Add(edge);
            msaglEdges.Add((edge, wire));
        }

        return (geometryGraph, msaglNodes, msaglEdges, selfWires);
    }

    private static void ApplyRoutes(List<(Edge Edge, GraphWire Wire)> msaglEdges, List<GraphWire> selfWires, GraphGeometry geometry)
    {
        foreach (var (edge, wire) in msaglEdges)
        {
            geometry.RouteOf(wire).CurvePath = ExtractPath(edge.Curve);
            SpliceTerminals(wire, geometry);
        }

        foreach (var wire in selfWires)
        {
            SynthesizeSelfLoop(wire, geometry);
        }
    }

    /// <summary>Rebuilds the synthetic loop route of a self-referencing wire from its current pivots.</summary>
    public static void SynthesizeSelfLoop(GraphWire wire, GraphGeometry geometry)
    {
        // Out the right side, over the top of the node, back into the left side.
        var owner = wire.From.Owner;
        var from = geometry.PivotOf(wire.From);
        var to = geometry.PivotOf(wire.To);
        var top = owner.Position.Y - 26f;

        var route = geometry.RouteOf(wire);
        route.CurvePath = null;
        route.Waypoints =
        [
            new Vector2(from.X + 36f, from.Y),
            new Vector2(from.X + 36f, top),
            new Vector2(to.X - 36f, top),
            new Vector2(to.X - 36f, to.Y),
        ];
    }

    // Port-free routes attach anywhere on the node border. Rewrite only the terminal runs:
    // cut the curve where it leaves the inflated node boxes and graft tangent-continuous
    // cubics that dock horizontally into the real socket pivots. The library trunk between
    // the cuts stays verbatim.
    private static void SpliceTerminals(GraphWire wire, GraphGeometry geometry)
    {
        var route = geometry.RouteOf(wire);
        var path = route.CurvePath;

        if (path == null)
        {
            route.Waypoints = null;
            return;
        }

        var fromPivot = geometry.PivotOf(wire.From);
        var toPivot = geometry.PivotOf(wire.To);

        bool Outside(Vector2 p, GraphNode node)
        {
            const float Inflate = 24f;
            var size = geometry.SizeOf(node);
            return p.X < node.Position.X - Inflate || p.Y < node.Position.Y - Inflate ||
                   p.X > node.Position.X + size.X + Inflate || p.Y > node.Position.Y + size.Y + Inflate;
        }

        static Vector2 SafeNormalize(Vector2 v, Vector2 fallback)
        {
            var length = v.Length();
            return length > 0.001f ? v / length : fallback;
        }

        var firstOutside = -1;
        var lastOutside = -1;

        for (var i = 0; i < path.Count; i++)
        {
            if (firstOutside < 0 && Outside(path[i].End, wire.From.Owner))
            {
                firstOutside = i;
            }

            if (Outside(path[i].End, wire.To.Owner))
            {
                lastOutside = i;
            }
        }

        if (firstOutside < 0 || lastOutside < 0 || firstOutside > lastOutside)
        {
            // Nodes are adjacent; a plain socket-to-socket bezier reads better than a splice.
            route.CurvePath = null;
            route.Waypoints = null;
            return;
        }

        var spliced = new List<GraphCurveCommand>(lastOutside - firstOutside + 4);

        // Head: horizontal exit from the output socket into the first point outside the node.
        var headTarget = path[firstOutside].End;
        var leaveDirection = firstOutside + 1 < path.Count
            ? SafeNormalize(OutgoingControl(path[firstOutside + 1]) - headTarget, SafeNormalize(headTarget - fromPivot, Vector2.UnitX))
            : SafeNormalize(headTarget - fromPivot, Vector2.UnitX);
        var headHandle = MathF.Min(60f, Vector2.Distance(fromPivot, headTarget) / 3f);

        spliced.Add(GraphCurveCommand.MoveTo(fromPivot));
        spliced.Add(GraphCurveCommand.CubicTo(
            fromPivot + new Vector2(headHandle, 0f),
            headTarget - leaveDirection * headHandle,
            headTarget));

        for (var i = firstOutside + 1; i <= lastOutside; i++)
        {
            spliced.Add(path[i]);
        }

        // Tail: from the last point outside the node, horizontal arrival into the input socket.
        var tailStart = path[lastOutside].End;
        var arriveDirection = lastOutside >= 1
            ? SafeNormalize(tailStart - IncomingControl(path[lastOutside], path[lastOutside - 1].End), SafeNormalize(toPivot - tailStart, Vector2.UnitX))
            : SafeNormalize(toPivot - tailStart, Vector2.UnitX);
        var tailHandle = MathF.Min(60f, Vector2.Distance(tailStart, toPivot) / 3f);

        spliced.Add(GraphCurveCommand.CubicTo(
            tailStart + arriveDirection * tailHandle,
            toPivot - new Vector2(tailHandle, 0f),
            toPivot));

        route.CurvePath = spliced;
        route.Waypoints = FlattenPath(spliced);
    }

    // Tangent hints for the splice: the control point a command leaves its start toward,
    // and the one it arrives at its end from.
    private static Vector2 OutgoingControl(GraphCurveCommand command) => command.Verb switch
    {
        GraphCurveVerb.CubicTo or GraphCurveVerb.ConicTo => command.A,
        _ => command.End,
    };

    private static Vector2 IncomingControl(GraphCurveCommand command, Vector2 previousEnd) => command.Verb switch
    {
        GraphCurveVerb.CubicTo => command.B,
        GraphCurveVerb.ConicTo => command.A,
        _ => previousEnd,
    };

    private static List<Vector2> FlattenPath(List<GraphCurveCommand> commands)
    {
        var points = new List<Vector2>();
        var current = Vector2.Zero;

        foreach (var command in commands)
        {
            switch (command.Verb)
            {
                case GraphCurveVerb.MoveTo:
                case GraphCurveVerb.LineTo:
                    points.Add(command.End);
                    break;

                case GraphCurveVerb.CubicTo:
                    for (var s = 1; s <= 8; s++)
                    {
                        var t = s / 8f;
                        var mt = 1f - t;
                        points.Add(mt * mt * mt * current + 3f * mt * mt * t * command.A + 3f * mt * t * t * command.B + t * t * t * command.End);
                    }

                    break;

                case GraphCurveVerb.ConicTo:
                    for (var s = 1; s <= 8; s++)
                    {
                        var t = s / 8f;
                        var mt = 1f - t;
                        var denominator = mt * mt + 2f * command.Weight * mt * t + t * t;
                        points.Add((mt * mt * current + 2f * command.Weight * mt * t * command.A + t * t * command.End) / denominator);
                    }

                    break;
            }

            current = command.End;
        }

        return points;
    }

    // The library's routed curve, verbatim: lines, cubics and polylines convert exactly,
    // and elliptical arc pieces become rational conics, which represent them exactly too.
    private static List<GraphCurveCommand>? ExtractPath(ICurve? curve)
    {
        if (curve == null)
        {
            return null;
        }

        var commands = new List<GraphCurveCommand>();

        static Vector2 Transpose(MsaglPoint p) => new((float)-p.Y, (float)p.X);

        void EnsureStart(MsaglPoint start)
        {
            if (commands.Count == 0)
            {
                commands.Add(GraphCurveCommand.MoveTo(Transpose(start)));
            }
        }

        void AddConicArc(ICurve segment, double t0, double t1)
        {
            var s = Transpose(segment[t0]);
            var e = Transpose(segment[t1]);
            var m = Transpose(segment[(t0 + t1) / 2]);
            var tangentS = Transpose(segment.Derivative(t0));
            var tangentE = Transpose(segment.Derivative(t1));

            EnsureStart(segment[t0]);

            // The conic through the arc's midpoint with the arc's end tangents IS the arc.
            if (TryIntersectLines(s, tangentS, e, tangentE, out var control))
            {
                var toControl = control - m;
                var toControlLengthSquared = toControl.LengthSquared();

                if (toControlLengthSquared > 1e-6f)
                {
                    var weight = Vector2.Dot(m - (s + e) / 2f, toControl) / toControlLengthSquared;

                    if (float.IsFinite(weight) && weight > 0.01f && weight <= 4f)
                    {
                        commands.Add(GraphCurveCommand.ConicTo(control, e, weight));
                        return;
                    }
                }
            }

            // Degenerate tangents (near-straight piece): Hermite cubic is visually identical.
            var h = (float)((t1 - t0) / 3.0);
            commands.Add(GraphCurveCommand.CubicTo(s + tangentS * h, e - tangentE * h, e));
        }

        void AddSegment(ICurve segment)
        {
            switch (segment)
            {
                case LineSegment line:
                    EnsureStart(line.Start);
                    commands.Add(GraphCurveCommand.LineTo(Transpose(line.End)));
                    return;

                case CubicBezierSegment bezier:
                    EnsureStart(bezier.B(0));
                    commands.Add(GraphCurveCommand.CubicTo(Transpose(bezier.B(1)), Transpose(bezier.B(2)), Transpose(bezier.B(3))));
                    return;

                case Polyline polyline:
                {
                    MsaglPoint? previous = null;

                    foreach (var p in polyline)
                    {
                        if (previous is { } prev)
                        {
                            EnsureStart(prev);
                            commands.Add(GraphCurveCommand.LineTo(Transpose(p)));
                        }

                        previous = p;
                    }

                    return;
                }

                case Ellipse:
                {
                    // One conic per <=90 degree piece keeps the tangent intersection stable.
                    var span = segment.ParEnd - segment.ParStart;
                    var pieces = Math.Max(1, (int)Math.Ceiling(Math.Abs(span) / (Math.PI / 2)));

                    for (var i = 0; i < pieces; i++)
                    {
                        AddConicArc(segment, segment.ParStart + span * i / pieces, segment.ParStart + span * (i + 1) / pieces);
                    }

                    return;
                }
            }

            // Unknown segment kinds: dense line samples, sub-pixel at any zoom.
            var steps = Math.Clamp((int)(segment.Length / 15.0), 2, 32);
            EnsureStart(segment[segment.ParStart]);

            for (var i = 1; i <= steps; i++)
            {
                commands.Add(GraphCurveCommand.LineTo(Transpose(segment[segment.ParStart + (segment.ParEnd - segment.ParStart) * i / steps])));
            }
        }

        if (curve is Curve composite)
        {
            foreach (var segment in composite.Segments)
            {
                AddSegment(segment);
            }
        }
        else
        {
            AddSegment(curve);
        }

        return commands.Count >= 2 ? commands : null;
    }

    private static bool TryIntersectLines(Vector2 p, Vector2 d1, Vector2 q, Vector2 d2, out Vector2 intersection)
    {
        var denominator = d1.X * d2.Y - d1.Y * d2.X;

        if (Math.Abs(denominator) < 1e-6f)
        {
            intersection = default;
            return false;
        }

        var t = ((q.X - p.X) * d2.Y - (q.Y - p.Y) * d2.X) / denominator;
        intersection = p + d1 * t;
        return true;
    }

}
