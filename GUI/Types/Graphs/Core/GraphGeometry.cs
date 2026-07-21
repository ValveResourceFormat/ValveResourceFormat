namespace GUI.Types.Graphs.Core;

/// <summary>Measured size and layout freshness of one node.</summary>
sealed class NodeGeometry
{
    public Vector2 Size;

    /// <summary>The <see cref="GraphNode.ContentVersion"/> the size and offsets were computed for.</summary>
    public int ComputedVersion = -1;

    /// <summary>
    /// Presentation rows: consecutive socket rows collapse into shared input|output lines;
    /// text rows pass through. Parallel to <see cref="RowCenters"/>.
    /// </summary>
    public List<GraphRow> LayoutRows = [];

    /// <summary>Row baselines as offsets from the node top, parallel to <see cref="LayoutRows"/>.</summary>
    public float[] RowCenters = [];
}

/// <summary>Routed geometry of one wire; both stay null for a plain socket-to-socket curve.</summary>
sealed class WireRoute
{
    /// <summary>Corner points of the orthogonal route computed by the layout.</summary>
    public List<Vector2>? Waypoints;

    /// <summary>
    /// Exact routed curve as a path-command stream. When set, rendering prefers it over
    /// <see cref="Waypoints"/>, which then only serves culling and hit-testing.
    /// </summary>
    public List<GraphCurveCommand>? CurvePath;
}

/// <summary>
/// All geometry derived from the model by the active view: measured node sizes, socket
/// pivots, row baselines and routed wire paths. The model itself stays pure content so a
/// different renderer can present it from scratch.
/// </summary>
internal sealed class GraphGeometry
{
    private readonly Dictionary<GraphNode, NodeGeometry> nodes = [];
    private readonly Dictionary<GraphSocket, Vector2> pivotOffsets = [];
    private readonly Dictionary<GraphWire, WireRoute> routes = [];

    public NodeGeometry NodeOf(GraphNode node)
    {
        if (!nodes.TryGetValue(node, out var geometry))
        {
            geometry = new NodeGeometry();
            nodes[node] = geometry;
        }

        return geometry;
    }

    public Vector2 SizeOf(GraphNode node) => nodes.TryGetValue(node, out var geometry) ? geometry.Size : Vector2.Zero;

    public void SetPivotOffset(GraphSocket socket, Vector2 offset) => pivotOffsets[socket] = offset;

    public Vector2 PivotOffsetOf(GraphSocket socket) => pivotOffsets.GetValueOrDefault(socket);

    /// <summary>Absolute canvas position of a socket.</summary>
    public Vector2 PivotOf(GraphSocket socket) => socket.Owner.Position + PivotOffsetOf(socket);

    public WireRoute RouteOf(GraphWire wire)
    {
        if (!routes.TryGetValue(wire, out var route))
        {
            route = new WireRoute();
            routes[wire] = route;
        }

        return route;
    }

    public WireRoute? TryRouteOf(GraphWire wire) => routes.GetValueOrDefault(wire);

    public void ClearAllRoutes()
    {
        foreach (var route in routes.Values)
        {
            route.Waypoints = null;
            route.CurvePath = null;
        }
    }
}
