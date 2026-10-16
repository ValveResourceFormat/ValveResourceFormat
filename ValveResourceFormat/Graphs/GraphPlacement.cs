using System.Linq;

namespace ValveResourceFormat.Graphs;

/// <summary>
/// Places the nodes of a decompiled graph document with <see cref="GraphLayout"/>, on cards of one
/// fixed size since the editors size their own. Islands are laid out separately and then packed
/// against each other, the way the graph viewers arrange a graph on opening.
/// </summary>
internal static class GraphPlacement
{
    /// <summary>Card size every node is laid out at.</summary>
    public static readonly Vector2 NodeSize = new(200f, 80f);

    /// <summary>
    /// One wire between two cards. It leaves the right edge of its source and enters the left edge
    /// of its target, both at mid height, which is where an editor draws a connection on a card of
    /// this shape.
    /// </summary>
    public static GraphLayoutEdge MakeEdge(int from, int to) => new(
        from,
        to,
        new Vector2(NodeSize.X, NodeSize.Y / 2f),
        new Vector2(0f, NodeSize.Y / 2f),
        from * 2,
        (to * 2) + 1);

    /// <summary>
    /// Places every node of one canvas and returns their top-left corners: each island laid out on
    /// its own under a share of the layout budget, then the islands packed toward a screen shape.
    /// </summary>
    public static Vector2[] Layout(int nodeCount, GraphLayoutEdge[] edges)
    {
        var options = new GraphLayoutOptions { LongWireDummies = true };
        var positions = new Vector2[nodeCount];
        var islands = GraphLayout.ConnectedComponents(nodeCount, edges);
        var mins = new Vector2[islands.Count];
        var maxs = new Vector2[islands.Count];

        GraphLayout.LayoutIslands([.. islands.Select(island => island.Count)], options.LayoutBudgetMs,
            (i, sliceMs) => LayoutIsland(islands[i], edges, positions, options, sliceMs, out mins[i], out maxs[i]));

        var offsets = GraphLayout.PackIslands(mins, maxs);

        for (var i = 0; i < islands.Count; i++)
        {
            foreach (var node in islands[i])
            {
                positions[node] += offsets[i];
            }
        }

        return positions;
    }

    /// <summary>
    /// Places one island in its own coordinate space and reports the bounding box it occupies, so
    /// the caller can pack the islands against each other afterwards.
    /// </summary>
    private static void LayoutIsland(
        List<int> island,
        GraphLayoutEdge[] edges,
        Vector2[] positions,
        GraphLayoutOptions options,
        int sliceMs,
        out Vector2 min,
        out Vector2 max)
    {
        var local = new Dictionary<int, int>(island.Count);

        for (var i = 0; i < island.Count; i++)
        {
            local[island[i]] = i;
        }

        var islandEdges = edges
            .Where(edge => local.ContainsKey(edge.From) && local.ContainsKey(edge.To))
            .Select(edge => MakeEdge(local[edge.From], local[edge.To]))
            .ToArray();

        var sizes = new Vector2[island.Count];
        Array.Fill(sizes, NodeSize);

        var islandPositions = new Vector2[island.Count];

        GraphLayout.Layout(islandPositions, sizes, islandEdges, options, sliceMs);

        min = new Vector2(float.MaxValue);
        max = new Vector2(float.MinValue);

        for (var i = 0; i < island.Count; i++)
        {
            positions[island[i]] = islandPositions[i];
            min = Vector2.Min(min, islandPositions[i]);
            max = Vector2.Max(max, islandPositions[i] + NodeSize);
        }
    }
}
