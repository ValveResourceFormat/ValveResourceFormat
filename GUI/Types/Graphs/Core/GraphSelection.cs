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

        foreach (var socket in node.Inputs)
        {
            foreach (var wire in socket.Wires)
            {
                Direct.Add(wire.From.Owner);
                DirectIn.Add(wire.From.Owner);
            }
        }

        foreach (var socket in node.Outputs)
        {
            foreach (var wire in socket.Wires)
            {
                Direct.Add(wire.To.Owner);
                DirectOut.Add(wire.To.Owner);
            }
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

        // Upstream over inputs, downstream over outputs.
        var queue = new Queue<GraphNode>();
        queue.Enqueue(startNode);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var socket in current.Inputs)
            {
                foreach (var wire in socket.Wires)
                {
                    if (connectedNodes.Add(wire.From.Owner))
                    {
                        queue.Enqueue(wire.From.Owner);
                    }
                }
            }
        }

        queue.Enqueue(startNode);
        var downstreamVisited = new HashSet<GraphNode> { startNode };

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var socket in current.Outputs)
            {
                foreach (var wire in socket.Wires)
                {
                    if (downstreamVisited.Add(wire.To.Owner))
                    {
                        connectedNodes.Add(wire.To.Owner);
                        queue.Enqueue(wire.To.Owner);
                    }
                }
            }
        }
    }
}
