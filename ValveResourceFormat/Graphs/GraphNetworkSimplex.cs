namespace ValveResourceFormat.Graphs;

/// <summary>
/// Network simplex for the integer problem behind layered coordinate assignment: minimise the
/// weighted sum of edge lengths <c>rank[head] - rank[tail]</c> subject to every edge being at
/// least its minimum length.
/// </summary>
/// <remarks>
/// The caller supplies a feasible ranking together with a spanning tree of edges that ranking
/// makes tight. Every pivot keeps the ranking feasible, so a solve cut short by the deadline still
/// leaves a valid, and never worse, result. Trees are numbered by postorder (<c>low</c>..<c>lim</c>
/// covers a node's subtree), which turns every subtree walk into a scan of a contiguous range.
/// </remarks>
internal sealed class NetworkSimplex(int nodeCount)
{
    /// <summary>Negative tree edges compared before the most negative of them leaves the tree.</summary>
    private const int SearchSize = 30;

    private readonly List<(int Tail, int Head, int MinLength, int Weight)> pending = [];

    private int[] tail = [];
    private int[] head = [];
    private int[] minLength = [];
    private int[] weight = [];
    private int[] cutValue = [];

    /// <summary>Slot of each edge in <see cref="treeList"/>, or -1 for an edge outside the tree.</summary>
    private int[] treeIndex = [];
    private int[] treeList = [];
    private int searchStart;

    private int[] incidentStart = [];
    private int[] incident = [];

    /// <summary>Tree edges of each node, in the node's own range of <see cref="incidentStart"/>.</summary>
    private int[] treeAdjacent = [];
    private int[] treeCount = [];
    private int[] tailSlot = [];
    private int[] headSlot = [];

    private int[] low = [];
    private int[] lim = [];
    private int[] parent = [];
    private int[] nodeAtLim = [];
    private int[] stackNode = [];
    private int[] stackCursor = [];

    private int[] rank = [];

    /// <summary>Adds the constraint <c>rank[head] - rank[tail] &gt;= minLength</c> at cost <paramref name="weight"/> per unit of length.</summary>
    /// <returns>The edge's index, as <see cref="Solve"/> expects tree edges to be named.</returns>
    public int AddEdge(int tail, int head, int minLength, int weight)
    {
        pending.Add((tail, head, minLength, weight));
        return pending.Count - 1;
    }

    /// <summary>Length of an edge beyond its minimum under the current ranking.</summary>
    private int Slack(int edge) => rank[head[edge]] - rank[tail[edge]] - minLength[edge];

    /// <summary>
    /// Improves <paramref name="ranks"/> in place until it is optimal or the deadline passes.
    /// </summary>
    /// <param name="ranks">A feasible ranking, one entry per node.</param>
    /// <param name="tree">Edges forming a spanning tree, each one tight under <paramref name="ranks"/>.</param>
    /// <param name="deadline">When to stop pivoting and keep the ranking reached so far.</param>
    /// <returns>Whether the ranking is optimal.</returns>
    public bool Solve(int[] ranks, IReadOnlyList<int> tree, LayoutDeadline deadline)
    {
        ArgumentNullException.ThrowIfNull(ranks);
        ArgumentNullException.ThrowIfNull(tree);

        if (nodeCount < 2)
        {
            return true;
        }

        if (tree.Count != nodeCount - 1)
        {
            throw new ArgumentException("A spanning tree has one edge fewer than the graph has nodes.", nameof(tree));
        }

        rank = ranks;
        Build();

        treeList = new int[tree.Count];

        for (var i = 0; i < tree.Count; i++)
        {
            treeList[i] = tree[i];
            treeIndex[tree[i]] = i;
            Attach(tree[i]);
        }

        if (DfsRange(0, -1, 0) != nodeCount)
        {
            throw new ArgumentException("The tree edges do not reach every node.", nameof(tree));
        }

        return Pivot(deadline);
    }

    /// <summary>Changes the cost of an edge after a solve, for <see cref="Resolve"/> to continue from.</summary>
    public void SetWeight(int edge, int value) => weight[edge] = value;

    /// <summary>Continues from the last solve's tree after edge costs changed.</summary>
    /// <returns>Whether the ranking is optimal.</returns>
    public bool Resolve(LayoutDeadline deadline) => nodeCount < 2 || Pivot(deadline);

    private bool Pivot(LayoutDeadline deadline)
    {
        for (var l = 0; l < nodeCount; l++)
        {
            var node = nodeAtLim[l];

            if (parent[node] >= 0)
            {
                cutValue[parent[node]] = CutValueOf(parent[node]);
            }
        }

        for (var iteration = 0; ; iteration++)
        {
            if ((iteration & 63) == 0 && deadline.Expired)
            {
                return false;
            }

            var leave = LeaveEdge();

            if (leave < 0)
            {
                return true;
            }

            var enter = EnterEdge(leave);

            if (enter < 0)
            {
                return false;
            }

            Update(leave, enter);
        }
    }

    private void Build()
    {
        var edgeCount = pending.Count;
        tail = new int[edgeCount];
        head = new int[edgeCount];
        minLength = new int[edgeCount];
        weight = new int[edgeCount];
        cutValue = new int[edgeCount];
        treeIndex = new int[edgeCount];
        tailSlot = new int[edgeCount];
        headSlot = new int[edgeCount];
        Array.Fill(treeIndex, -1);

        incidentStart = new int[nodeCount + 1];

        for (var e = 0; e < edgeCount; e++)
        {
            (tail[e], head[e], minLength[e], weight[e]) = pending[e];
            incidentStart[tail[e] + 1]++;
            incidentStart[head[e] + 1]++;
        }

        for (var n = 0; n < nodeCount; n++)
        {
            incidentStart[n + 1] += incidentStart[n];
        }

        incident = new int[incidentStart[nodeCount]];
        treeAdjacent = new int[incident.Length];
        treeCount = new int[nodeCount];

        var fill = new int[nodeCount];

        for (var e = 0; e < edgeCount; e++)
        {
            incident[incidentStart[tail[e]] + fill[tail[e]]++] = e;
            incident[incidentStart[head[e]] + fill[head[e]]++] = e;
        }

        low = new int[nodeCount];
        lim = new int[nodeCount];
        parent = new int[nodeCount];
        nodeAtLim = new int[nodeCount];
        stackNode = new int[nodeCount];
        stackCursor = new int[nodeCount];
    }

    private void Attach(int edge)
    {
        tailSlot[edge] = treeCount[tail[edge]];
        treeAdjacent[incidentStart[tail[edge]] + treeCount[tail[edge]]++] = edge;
        headSlot[edge] = treeCount[head[edge]];
        treeAdjacent[incidentStart[head[edge]] + treeCount[head[edge]]++] = edge;
    }

    private void Detach(int edge)
    {
        Remove(tail[edge], tailSlot[edge]);
        Remove(head[edge], headSlot[edge]);

        void Remove(int node, int slot)
        {
            var last = incidentStart[node] + --treeCount[node];
            var moved = treeAdjacent[last];
            treeAdjacent[incidentStart[node] + slot] = moved;

            if (tail[moved] == node)
            {
                tailSlot[moved] = slot;
            }
            else
            {
                headSlot[moved] = slot;
            }
        }
    }

    private bool InSubtree(int root, int node) => low[root] <= lim[node] && lim[node] <= lim[root];

    /// <summary>
    /// Numbers the subtree under <paramref name="root"/> in postorder starting at
    /// <paramref name="start"/>, recording each node's parent edge on the way.
    /// </summary>
    /// <returns>One past the last number given out.</returns>
    private int DfsRange(int root, int parentEdge, int start)
    {
        var next = start;
        var depth = 0;

        parent[root] = parentEdge;
        low[root] = next;
        stackNode[0] = root;
        stackCursor[0] = 0;

        while (depth >= 0)
        {
            var node = stackNode[depth];

            if (stackCursor[depth] < treeCount[node])
            {
                var edge = treeAdjacent[incidentStart[node] + stackCursor[depth]++];

                if (edge == parent[node])
                {
                    continue;
                }

                var child = tail[edge] == node ? head[edge] : tail[edge];
                parent[child] = edge;
                low[child] = next;
                depth++;
                stackNode[depth] = child;
                stackCursor[depth] = 0;
                continue;
            }

            lim[node] = next;
            nodeAtLim[next] = node;
            next++;
            depth--;
        }

        return next;
    }

    /// <summary>
    /// Cut value of a tree edge from the edges around its lower end and the cut values of the tree
    /// edges below it, which must already be current.
    /// </summary>
    private int CutValueOf(int edge)
    {
        var node = parent[tail[edge]] == edge ? tail[edge] : head[edge];
        var forward = node == tail[edge];
        var sum = 0;

        for (var i = incidentStart[node]; i < incidentStart[node + 1]; i++)
        {
            var other = incident[i];
            var far = tail[other] == node ? head[other] : tail[other];
            var outside = !InSubtree(node, far);
            var value = outside ? weight[other] : (treeIndex[other] >= 0 ? cutValue[other] : 0) - weight[other];
            var positive = forward ? head[other] == node : tail[other] == node;

            if (outside)
            {
                positive = !positive;
            }

            sum += positive ? value : -value;
        }

        return sum;
    }

    private int LeaveEdge()
    {
        var best = -1;
        var found = 0;

        for (var scanned = 0; scanned < treeList.Length; scanned++)
        {
            var edge = treeList[searchStart];

            if (cutValue[edge] < 0)
            {
                if (best < 0 || cutValue[edge] < cutValue[best])
                {
                    best = edge;
                }

                if (++found >= SearchSize)
                {
                    return best;
                }
            }

            searchStart = searchStart + 1 == treeList.Length ? 0 : searchStart + 1;
        }

        return best;
    }

    /// <summary>
    /// The non-tree edge of least slack that reconnects the two halves the leaving edge splits the
    /// tree into, pointing the same way across the cut.
    /// </summary>
    private int EnterEdge(int leave)
    {
        var lowerIsTail = lim[tail[leave]] < lim[head[leave]];
        var root = lowerIsTail ? tail[leave] : head[leave];
        var best = -1;
        var bestSlack = int.MaxValue;

        for (var l = low[root]; l <= lim[root]; l++)
        {
            var node = nodeAtLim[l];

            for (var i = incidentStart[node]; i < incidentStart[node + 1]; i++)
            {
                var edge = incident[i];

                if (treeIndex[edge] >= 0)
                {
                    continue;
                }

                // Out of the head's half when the subtree holds the head, into the tail's otherwise.
                var inside = lowerIsTail ? head[edge] == node : tail[edge] == node;
                var far = lowerIsTail ? tail[edge] : head[edge];

                if (!inside || InSubtree(root, far))
                {
                    continue;
                }

                var slack = Slack(edge);

                if (slack < bestSlack)
                {
                    best = edge;
                    bestSlack = slack;

                    if (slack == 0)
                    {
                        return best;
                    }
                }
            }
        }

        return best;
    }

    private void Update(int leave, int enter)
    {
        var delta = Slack(enter);

        if (delta > 0)
        {
            // The two halves only have to move relative to each other, so the smaller one moves.
            var lowerIsTail = lim[tail[leave]] < lim[head[leave]];
            var root = lowerIsTail ? tail[leave] : head[leave];
            var shift = lowerIsTail ? -delta : delta;

            if ((lim[root] - low[root] + 1) * 2 <= nodeCount)
            {
                for (var l = low[root]; l <= lim[root]; l++)
                {
                    rank[nodeAtLim[l]] += shift;
                }
            }
            else
            {
                for (var l = 0; l < low[root]; l++)
                {
                    rank[nodeAtLim[l]] -= shift;
                }

                for (var l = lim[root] + 1; l < nodeCount; l++)
                {
                    rank[nodeAtLim[l]] -= shift;
                }
            }
        }

        var cut = cutValue[leave];
        var ancestor = TreeUpdate(tail[enter], head[enter], cut, true);
        TreeUpdate(head[enter], tail[enter], cut, false);

        cutValue[enter] = -cut;
        cutValue[leave] = 0;

        treeIndex[enter] = treeIndex[leave];
        treeList[treeIndex[enter]] = enter;
        treeIndex[leave] = -1;
        Detach(leave);
        Attach(enter);

        DfsRange(ancestor, parent[ancestor], low[ancestor]);
    }

    /// <summary>
    /// Walks from <paramref name="node"/> up to the first ancestor whose subtree holds
    /// <paramref name="target"/>, adjusting the cut value of every tree edge on the way.
    /// </summary>
    private int TreeUpdate(int node, int target, int cut, bool direction)
    {
        while (!InSubtree(node, target))
        {
            var edge = parent[node];
            var add = node == tail[edge] ? direction : !direction;
            cutValue[edge] += add ? cut : -cut;
            node = lim[tail[edge]] > lim[head[edge]] ? tail[edge] : head[edge];
        }

        return node;
    }
}
