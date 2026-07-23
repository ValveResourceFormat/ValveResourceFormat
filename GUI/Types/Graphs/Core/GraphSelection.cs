namespace GUI.Types.Graphs.Core;

/// <summary>
/// Selection state of a graph: a primary node with its transitive chain and one-hop wire
/// neighbors, or a single selected wire. The render tiers derive from this.
/// </summary>
internal sealed class GraphSelection
{
    public GraphNode? PrimaryNode { get; private set; }
    public GraphWire? Wire { get; private set; }

    /// <summary>Transitive upstream and downstream chain of the primary node, including it.</summary>
    public HashSet<GraphNode> Connected { get; } = [];

    /// <summary>One-hop wire neighbors of the primary node.</summary>
    public HashSet<GraphNode> Direct { get; } = [];

    /// <summary>One-hop upstream neighbors (over the primary node's inputs).</summary>
    public HashSet<GraphNode> DirectIn { get; } = [];

    /// <summary>One-hop downstream neighbors (over the primary node's outputs).</summary>
    public HashSet<GraphNode> DirectOut { get; } = [];

    public bool IsEmpty => PrimaryNode == null && Wire == null;

    // Clicking a wire focuses just its two endpoint nodes; clicking it again deselects.
    public void SelectWire(GraphWire wire)
    {
        PrimaryNode = null;
        Connected.Clear();
        ClearDirect();
        Wire = Wire == wire ? null : wire;
    }

    public void SetPrimary(GraphNode node)
    {
        PrimaryNode = node;
        Wire = null;
        TraverseConnected(node, Connected);
        CollectDirectNeighbors(node);
    }

    public void Clear()
    {
        PrimaryNode = null;
        Wire = null;
        Connected.Clear();
        ClearDirect();
    }

    private void ClearDirect()
    {
        Direct.Clear();
        DirectIn.Clear();
        DirectOut.Clear();
    }

    // Direct wire neighbors of the primary selection; everything else renders dimmed.
    // Upstream and downstream are tracked separately for directional highlighting.
    private void CollectDirectNeighbors(GraphNode node)
    {
        ClearDirect();

        foreach (var neighbor in node.Neighbors(upstream: true))
        {
            Direct.Add(neighbor);
            DirectIn.Add(neighbor);
        }

        foreach (var neighbor in node.Neighbors(upstream: false))
        {
            Direct.Add(neighbor);
            DirectOut.Add(neighbor);
        }

        Direct.Remove(node);
        DirectIn.Remove(node);
        DirectOut.Remove(node);
    }

    /// <summary>Clears the set and fills it with the transitive upstream and downstream chain of <paramref name="startNode"/>, including it.</summary>
    public static void TraverseConnected(GraphNode startNode, HashSet<GraphNode> connectedNodes)
    {
        connectedNodes.Clear();
        connectedNodes.Add(startNode);

        TraverseDirection(startNode, connectedNodes, upstream: true);
        TraverseDirection(startNode, connectedNodes, upstream: false);
    }

    /// <summary>
    /// Adds every node reachable from <paramref name="startNode"/> in one direction to
    /// <paramref name="reached"/>. Each direction tracks its own visited set, so a node already
    /// reached the other way is still walked through rather than cutting the cone short.
    /// </summary>
    public static void TraverseDirection(GraphNode startNode, HashSet<GraphNode> reached, bool upstream)
    {
        var visited = new HashSet<GraphNode> { startNode };
        var queue = new Queue<GraphNode>();
        queue.Enqueue(startNode);

        while (queue.Count > 0)
        {
            foreach (var next in queue.Dequeue().Neighbors(upstream))
            {
                if (visited.Add(next))
                {
                    reached.Add(next);
                    queue.Enqueue(next);
                }
            }
        }
    }
}
