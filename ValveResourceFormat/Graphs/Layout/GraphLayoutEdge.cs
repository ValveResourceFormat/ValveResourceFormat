namespace ValveResourceFormat.Graphs;

/// <summary>
/// One directed wire of a graph being laid out, expressed against node indices so the layout
/// engine never sees the caller's node type.
/// </summary>
/// <param name="From">Index of the source node.</param>
/// <param name="To">Index of the target node.</param>
/// <param name="FromPivot">
/// Where the wire leaves the source node, as an offset from that node's top-left corner. The
/// layout aligns and scores wires on the straight run between the two pivots, so this is what
/// decides which row of a card a wire appears to dock at.
/// </param>
/// <param name="ToPivot">Where the wire enters the target node, as an offset from its top-left corner.</param>
/// <param name="FromSocket">
/// Identity of the source endpoint. Wires leaving the same socket fan out from one point and so
/// can never cross each other; the crossing passes use this to skip those pairs. Callers without
/// sockets can pass any per-endpoint identifier, or the node index when every node has one output.
/// </param>
/// <param name="ToSocket">Identity of the target endpoint, used the same way.</param>
/// <param name="Dashed">
/// Whether this is a secondary binding rather than primary flow, which pulls more weakly during
/// alignment.
/// </param>
internal readonly record struct GraphLayoutEdge(
    int From,
    int To,
    Vector2 FromPivot,
    Vector2 ToPivot,
    int FromSocket,
    int ToSocket,
    bool Dashed = false)
{
    /// <summary>Whether two wires leave through the same socket or enter through the same socket, and so meet at that end.</summary>
    public bool SharesSocketWith(GraphLayoutEdge other) => FromSocket == other.FromSocket || ToSocket == other.ToSocket;

    /// <summary>Where the wire leaves its source node placed at <paramref name="positions"/>.</summary>
    public Vector2 SourcePoint(Vector2[] positions) => positions[From] + FromPivot;

    /// <summary>Where the wire enters its target node placed at <paramref name="positions"/>.</summary>
    public Vector2 TargetPoint(Vector2[] positions) => positions[To] + ToPivot;
}
