namespace ValveResourceFormat.Graphs;

/// <summary>Measured size and layout freshness of one node.</summary>
public sealed class NodeGeometry
{
    /// <summary>Measured card size.</summary>
    public Vector2 Size { get; set; }

    /// <summary>The <see cref="GraphNode.ContentVersion"/> the size and offsets were computed for.</summary>
    public int ComputedVersion { get; set; } = -1;

    /// <summary>
    /// Presentation rows: consecutive socket rows collapse into shared input-output lines;
    /// text rows pass through. Parallel to <see cref="RowCenters"/>.
    /// </summary>
    public List<GraphRow> LayoutRows { get; } = [];

    /// <summary>Row baselines as offsets from the node top, parallel to <see cref="LayoutRows"/>.</summary>
    public float[] RowCenters { get; set; } = [];
}

/// <summary>Routed geometry of one wire; empty for a plain socket-to-socket curve.</summary>
public sealed class WireRoute
{
    /// <summary>Corner points of the orthogonal route computed by the layout.</summary>
    public List<Vector2> Waypoints { get; } = [];

    /// <summary>Whether the layout routed this wire rather than leaving it a plain curve.</summary>
    public bool IsRouted => Waypoints.Count > 0;

    /// <summary>Replaces the route with the given corner points.</summary>
    /// <param name="waypoints">The corners to draw the wire through.</param>
    public void SetRoute(IEnumerable<Vector2> waypoints)
    {
        Waypoints.Clear();
        Waypoints.AddRange(waypoints);
    }

    /// <summary>Drops the route, leaving the wire to draw as a plain curve.</summary>
    public void ClearRoute() => Waypoints.Clear();
}

/// <summary>
/// All geometry derived from the model: measured node sizes, socket pivots, row baselines and
/// routed wire paths. The model itself stays pure content, so a renderer can present it from
/// scratch without the model knowing how it is drawn.
/// </summary>
public sealed class GraphGeometry
{
    private readonly Dictionary<GraphNode, NodeGeometry> nodes = [];
    private readonly Dictionary<GraphSocket, Vector2> pivotOffsets = [];
    private readonly Dictionary<GraphWire, WireRoute> routes = [];

    /// <summary>The geometry of a node, created empty on first use.</summary>
    /// <param name="node">The node to look up.</param>
    public NodeGeometry NodeOf(GraphNode node)
    {
        if (!nodes.TryGetValue(node, out var geometry))
        {
            geometry = new NodeGeometry();
            nodes[node] = geometry;
        }

        return geometry;
    }

    /// <summary>Measured size of a node, or zero if it has never been measured.</summary>
    /// <param name="node">The node to size.</param>
    public Vector2 SizeOf(GraphNode node) => nodes.TryGetValue(node, out var geometry) ? geometry.Size : Vector2.Zero;

    /// <summary>Records where a socket sits relative to its node's top-left corner.</summary>
    /// <param name="socket">The socket to place.</param>
    /// <param name="offset">Its offset from the node origin.</param>
    public void SetPivotOffset(GraphSocket socket, Vector2 offset) => pivotOffsets[socket] = offset;

    /// <summary>Where a socket sits relative to its node's top-left corner.</summary>
    /// <param name="socket">The socket to locate.</param>
    public Vector2 PivotOffsetOf(GraphSocket socket) => pivotOffsets.GetValueOrDefault(socket);

    /// <summary>Absolute canvas position of a socket.</summary>
    /// <param name="socket">The socket to locate.</param>
    public Vector2 PivotOf(GraphSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        return socket.Owner.Position + PivotOffsetOf(socket);
    }

    /// <summary>The route of a wire, created empty on first use.</summary>
    /// <param name="wire">The wire to look up.</param>
    public WireRoute RouteOf(GraphWire wire)
    {
        if (!routes.TryGetValue(wire, out var route))
        {
            route = new WireRoute();
            routes[wire] = route;
        }

        return route;
    }

    /// <summary>The route of a wire, or null when it has none.</summary>
    /// <param name="wire">The wire to look up.</param>
    public WireRoute? TryRouteOf(GraphWire wire) => routes.GetValueOrDefault(wire);

    /// <summary>Drops the route of a wire that no longer exists.</summary>
    /// <param name="wire">The wire that was removed.</param>
    public void RemoveWire(GraphWire wire) => routes.Remove(wire);

    /// <summary>Drops the pivot of a socket that no longer exists.</summary>
    /// <param name="socket">The socket that was removed.</param>
    public void RemoveSocket(GraphSocket socket) => pivotOffsets.Remove(socket);

    /// <summary>Drops every routed wire path, leaving the wires to draw as plain curves.</summary>
    public void ClearAllRoutes()
    {
        foreach (var route in routes.Values)
        {
            route.ClearRoute();
        }
    }

    /// <summary>Drops all measured node, socket and wire geometry.</summary>
    public void Clear()
    {
        nodes.Clear();
        pivotOffsets.Clear();
        routes.Clear();
    }
}
