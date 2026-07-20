using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Core.Routing;
using Microsoft.Msagl.Layout.Layered;
using MsaglPoint = Microsoft.Msagl.Core.Geometry.Point;

namespace GUI.Types.Graphs.Core;

/// <summary>
/// Runs MSAGL's Sugiyama layout with rectilinear edge routing over a component and writes the
/// node positions and orthogonal wire routes back into the graph model.
/// </summary>
/// <remarks>
/// MSAGL lays out top-down in a Y-up plane. Instead of using its Transformation option (which
/// rotates the node boxes and breaks the reported geometry), the graph is fed with transposed
/// node dimensions and the resulting coordinates are transposed back, turning the top-down
/// layout into our left-to-right Y-down canvas.
/// </remarks>
internal static class MsaglLayoutAdapter
{
    public static void Layout(List<GraphNode> component, List<GraphWire> componentWires)
    {
        if (component.Count == 0)
        {
            return;
        }

        var (geometryGraph, msaglNodes, msaglEdges, selfWires) = BuildGeometry(component, componentWires, useCurrentPositions: false);

        var settings = new SugiyamaLayoutSettings
        {
            LayerSeparation = 220,
            NodeSeparation = 36,
        };
        settings.EdgeRoutingSettings.EdgeRoutingMode = EdgeRoutingMode.Rectilinear;

        Microsoft.Msagl.Miscellaneous.LayoutHelpers.CalculateLayout(geometryGraph, settings, null);

        // Transpose back: MSAGL (x right, y up, flow top-to-bottom) -> ours (x right = flow, y down).
        foreach (var (node, msaglNode) in msaglNodes)
        {
            var center = msaglNode.Center;
            node.Position = new Vector2(
                (float)(-center.Y) - node.Size.X / 2f,
                (float)center.X - node.Size.Y / 2f);
        }

        RouteAndApply(geometryGraph, msaglEdges, selfWires);
    }

    /// <summary>
    /// Re-routes the wires of a component around the CURRENT node positions without moving any
    /// node; used after the user drags nodes so routes stay orthogonal and sensible.
    /// </summary>
    public static void RouteComponent(List<GraphNode> component, List<GraphWire> componentWires)
    {
        if (component.Count == 0)
        {
            return;
        }

        var (geometryGraph, _, msaglEdges, selfWires) = BuildGeometry(component, componentWires, useCurrentPositions: true);
        RouteAndApply(geometryGraph, msaglEdges, selfWires);
    }

    private static (GeometryGraph Graph, Dictionary<GraphNode, Node> Nodes, List<(Edge Edge, GraphWire Wire)> Edges, List<GraphWire> SelfWires) BuildGeometry(
        List<GraphNode> component, List<GraphWire> componentWires, bool useCurrentPositions)
    {
        var geometryGraph = new GeometryGraph();
        var msaglNodes = new Dictionary<GraphNode, Node>(component.Count);

        foreach (var node in component)
        {
            // Transposed: our width separates layers (vertical in MSAGL), our height stacks in-layer.
            var msaglNode = new Node(CurveFactory.CreateRectangle(node.Size.Y, node.Size.X, new MsaglPoint()))
            {
                UserData = node,
            };

            if (useCurrentPositions)
            {
                var center = node.Position + node.Size / 2f;
                msaglNode.Center = new MsaglPoint(center.Y, -center.X);
            }

            msaglNodes[node] = msaglNode;
            geometryGraph.Nodes.Add(msaglNode);
        }

        // One MSAGL edge per wire, pinned to the actual socket pivots via relative ports so
        // routes connect where our sockets are (inputs left edge, outputs right edge).
        var msaglEdges = new List<(Edge Edge, GraphWire Wire)>();

        static MsaglPoint SocketOffset(GraphSocket socket)
        {
            // Our socket offset is node-top-left-relative in a Y-down plane; MSAGL wants a
            // center-relative offset in its transposed Y-up plane.
            var relative = socket.PivotOffset - socket.Owner.Size / 2f;
            return new MsaglPoint(relative.Y, -relative.X);
        }

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
            var fromOffset = SocketOffset(wire.From);
            var toOffset = SocketOffset(wire.To);
            edge.EdgeGeometry.SourcePort = new RelativeFloatingPort(() => fromNode.BoundaryCurve, () => fromNode.Center, fromOffset);
            edge.EdgeGeometry.TargetPort = new RelativeFloatingPort(() => toNode.BoundaryCurve, () => toNode.Center, toOffset);
            geometryGraph.Edges.Add(edge);
            msaglEdges.Add((edge, wire));
        }

        return (geometryGraph, msaglNodes, msaglEdges, selfWires);
    }

    private static void RouteAndApply(GeometryGraph geometryGraph, List<(Edge Edge, GraphWire Wire)> msaglEdges, List<GraphWire> selfWires)
    {
        // Sugiyama leaves spline curves regardless of the routing mode; the rectilinear
        // router has to run as its own pass. Corner radius 0 keeps pure right angles.
        var router = new Microsoft.Msagl.Routing.Rectilinear.RectilinearEdgeRouter(geometryGraph, 12, 0, useSparseVisibilityGraph: true);
        router.Run();

        foreach (var (edge, wire) in msaglEdges)
        {
            var points = ExtractRoute(edge);

            if (points == null)
            {
                continue;
            }

            wire.Waypoints = SnapRouteToSockets(points, wire.From.Pivot, wire.To.Pivot);
        }

        foreach (var wire in selfWires)
        {
            // Out the right side, over the top of the node, back into the left side.
            var owner = wire.From.Owner;
            var from = wire.From.Pivot;
            var to = wire.To.Pivot;
            var top = owner.Position.Y - 26f;

            wire.Waypoints =
            [
                new Vector2(from.X + 36f, from.Y),
                new Vector2(from.X + 36f, top),
                new Vector2(to.X - 36f, top),
                new Vector2(to.X - 36f, to.Y),
            ];
        }
    }

    // The router treats ports as suggestions and may slide the attachment along the node edge;
    // rebuild the route's head and tail as horizontal runs at the exact socket rows.
    internal static List<Vector2> SnapRouteToSockets(List<Vector2> points, Vector2 fromPivot, Vector2 toPivot)
    {
        var firstVertical = -1;
        var lastVertical = -1;

        for (var i = 0; i + 1 < points.Count; i++)
        {
            if (MathF.Abs(points[i].X - points[i + 1].X) < 0.5f && MathF.Abs(points[i].Y - points[i + 1].Y) > 0.5f)
            {
                if (firstVertical < 0)
                {
                    firstVertical = i;
                }

                lastVertical = i;
            }
        }

        if (firstVertical < 0)
        {
            // Purely horizontal route: synthesize one elbow between the socket rows.
            var midX = (fromPivot.X + toPivot.X) / 2f;
            return [new Vector2(midX, fromPivot.Y), new Vector2(midX, toPivot.Y)];
        }

        var result = new List<Vector2>(lastVertical - firstVertical + 2)
        {
            new(points[firstVertical].X, fromPivot.Y),
        };

        for (var i = firstVertical + 1; i <= lastVertical; i++)
        {
            result.Add(points[i]);
        }

        result.Add(new Vector2(points[lastVertical].X, toPivot.Y));
        return result;
    }

    private static List<Vector2>? ExtractRoute(Edge edge)
    {
        if (edge.Curve == null)
        {
            return null;
        }

        var points = new List<Vector2>();

        void AddPoint(MsaglPoint point)
        {
            var converted = new Vector2((float)-point.Y, (float)point.X);

            if (points.Count == 0 || Vector2.DistanceSquared(points[^1], converted) > 1f)
            {
                points.Add(converted);
            }
        }

        switch (edge.Curve)
        {
            case LineSegment line:
                AddPoint(line.Start);
                AddPoint(line.End);
                break;

            case Curve curve:
                foreach (var segment in curve.Segments)
                {
                    AddPoint(segment.Start);
                }

                AddPoint(curve.End);
                break;

            case Microsoft.Msagl.Core.Geometry.Curves.Polyline polyline:
                foreach (var point in polyline)
                {
                    AddPoint(point);
                }

                break;

            default:
                return null;
        }

        return points.Count >= 2 ? points : null;
    }
}
