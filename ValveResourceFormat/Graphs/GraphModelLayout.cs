namespace ValveResourceFormat.Graphs;

/// <summary>
/// Drives <see cref="ValveResourceFormat.Graphs.GraphLayout"/> from the view's node model: node
/// sizes and socket pivots go out as parallel arrays, placements and routed waypoints come back.
/// Self-referencing wires never reach the engine and get a synthetic orbit route here instead.
/// </summary>
internal static class GraphModelLayout
{
    public static void Layout(
        List<GraphNode> component,
        List<GraphWire> componentWires,
        GraphGeometry geometry,
        GraphLayoutOptions options,
        int budgetMs)
    {
        var (sizes, edges) = Describe(component, componentWires, geometry);
        var positions = new Vector2[component.Count];

        var routes = GraphLayout.Layout(positions, sizes, edges, options, budgetMs);

        for (var i = 0; i < component.Count; i++)
        {
            component[i].Position = positions[i];
        }

        for (var i = 0; i < componentWires.Count; i++)
        {
            if (routes[i] is { } waypoints)
            {
                geometry.RouteOf(componentWires[i]).SetRoute(waypoints);
            }
        }

        foreach (var wire in componentWires)
        {
            if (wire.From.Owner == wire.To.Owner)
            {
                SynthesizeSelfLoop(wire, geometry);
            }
        }
    }

    /// <summary>
    /// Moves placed cards to remove wire crossings and to clear wires that run across them,
    /// judged on the straight run between the real socket pivots.
    /// </summary>
    public static void RepairCrossings(
        List<GraphNode> component,
        List<GraphWire> componentWires,
        GraphGeometry geometry,
        GraphLayoutOptions options,
        int budgetMs)
    {
        var (sizes, edges) = Describe(component, componentWires, geometry);
        var positions = new Vector2[component.Count];

        for (var i = 0; i < component.Count; i++)
        {
            positions[i] = component[i].Position;
        }

        GraphLayout.RepairCrossings(positions, sizes, edges, options, budgetMs);

        for (var i = 0; i < component.Count; i++)
        {
            component[i].Position = positions[i];
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
        route.SetRoute(
        [
            new Vector2(from.X + 36f, from.Y),
            new Vector2(from.X + 36f, top),
            new Vector2(to.X - 36f, top),
            new Vector2(to.X - 36f, to.Y),
        ]);
    }

    /// <summary>
    /// The island as the engine sees it. Socket identity becomes a dense integer per socket, so
    /// the crossing passes can still tell which wires fan out from one point.
    /// </summary>
    private static (Vector2[] Sizes, GraphLayoutEdge[] Edges) Describe(
        List<GraphNode> component,
        List<GraphWire> componentWires,
        GraphGeometry geometry)
    {
        var index = new Dictionary<GraphNode, int>(component.Count);
        var sizes = new Vector2[component.Count];

        for (var i = 0; i < component.Count; i++)
        {
            index[component[i]] = i;
            sizes[i] = geometry.SizeOf(component[i]);
        }

        var socketIds = new Dictionary<GraphSocket, int>();
        var edges = new GraphLayoutEdge[componentWires.Count];

        for (var i = 0; i < componentWires.Count; i++)
        {
            var wire = componentWires[i];

            edges[i] = new GraphLayoutEdge(
                index[wire.From.Owner],
                index[wire.To.Owner],
                geometry.PivotOffsetOf(wire.From),
                geometry.PivotOffsetOf(wire.To),
                SocketId(wire.From),
                SocketId(wire.To),
                wire.Dashed);
        }

        return (sizes, edges);

        int SocketId(GraphSocket socket)
        {
            if (!socketIds.TryGetValue(socket, out var id))
            {
                socketIds[socket] = id = socketIds.Count;
            }

            return id;
        }
    }
}
