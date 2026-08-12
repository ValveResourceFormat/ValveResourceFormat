using System.Linq;

namespace ValveResourceFormat.Graphs;

/// <summary>
/// Graph layout: layered Sugiyama placement per island and 16:9 shelf packing of the island
/// rectangles. Nodes are indices into parallel arrays of size and placement, and wires are
/// <see cref="GraphLayoutEdge"/> values against those indices, so a caller drives it without
/// handing over its own node type.
/// </summary>
public static class GraphLayout
{
    private const float PackingAspect = 16f / 9f;

    /// <summary>Gap between the wrapped sub-columns of one oversized rank.</summary>
    private const float SubColumnSeparation = 64f;

    /// <summary>Layout time every island is given before the rest is shared out by size.</summary>
    private const int SliceFloorMs = 250;

    /// <summary>
    /// Places one connected island and returns the waypoints each long wire should be drawn
    /// through, indexed like <paramref name="edges"/> and null for the wires that draw as a plain
    /// curve. Waypoints only appear when <see cref="GraphLayoutOptions.LongWireDummies"/> is set.
    /// </summary>
    /// <param name="positions">
    /// Receives the top-left corner of each node. Contents on entry are ignored; the array only
    /// has to be as long as <paramref name="sizes"/>.
    /// </param>
    /// <param name="sizes">Measured size of each node.</param>
    /// <param name="edges">The wires between them. A wire from a node to itself is ignored.</param>
    /// <param name="options">Layout tuning.</param>
    public static Vector2[]?[] Layout(Vector2[] positions, Vector2[] sizes, GraphLayoutEdge[] edges, GraphLayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(sizes);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(options);

        var routes = new Vector2[]?[edges.Length];

        if (sizes.Length > 1)
        {
            new LayeredSolver(positions, sizes, edges, options, DeadlineFor(options), routes).Run();
        }

        return routes;
    }

    /// <summary>
    /// Moves placed cards to remove wire crossings and to clear wires that run across them,
    /// judged on the straight run between the real socket pivots.
    /// </summary>
    /// <remarks>
    /// Runs last, on geometry, and is the only pass that sees which socket row a wire docks at.
    /// </remarks>
    /// <param name="positions">Current top-left corner of each node, updated in place.</param>
    /// <param name="sizes">Measured size of each node.</param>
    /// <param name="edges">The wires between them. A wire from a node to itself is ignored.</param>
    /// <param name="options">Layout tuning.</param>
    public static void RepairCrossings(Vector2[] positions, Vector2[] sizes, GraphLayoutEdge[] edges, GraphLayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(sizes);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(options);

        RepairCrossings(positions, sizes, edges, options, DeadlineFor(options));
    }

    private static void RepairCrossings(Vector2[] positions, Vector2[] sizes, GraphLayoutEdge[] edges, GraphLayoutOptions options, LayoutDeadline deadline)
    {
        if (sizes.Length > 1)
        {
            new CrossingRepair(positions, sizes, edges, options, deadline).Run();
        }
    }

    /// <summary>
    /// The connected components of a graph, ignoring wire direction, as lists of node indices in
    /// ascending order. Components come back ordered by their first node.
    /// </summary>
    /// <param name="nodeCount">How many nodes the graph has.</param>
    /// <param name="edges">The wires between them.</param>
    public static List<List<int>> ConnectedComponents(int nodeCount, GraphLayoutEdge[] edges)
    {
        ArgumentNullException.ThrowIfNull(edges);

        var adjacency = new List<int>[nodeCount];

        for (var i = 0; i < nodeCount; i++)
        {
            adjacency[i] = [];
        }

        foreach (var edge in edges)
        {
            if (edge.From == edge.To)
            {
                continue;
            }

            adjacency[edge.From].Add(edge.To);
            adjacency[edge.To].Add(edge.From);
        }

        var components = new List<List<int>>();
        var seen = new bool[nodeCount];
        var pending = new Stack<int>();

        for (var start = 0; start < nodeCount; start++)
        {
            if (seen[start])
            {
                continue;
            }

            var component = new List<int>();
            seen[start] = true;
            pending.Push(start);

            while (pending.Count > 0)
            {
                var node = pending.Pop();
                component.Add(node);

                foreach (var other in adjacency[node])
                {
                    if (!seen[other])
                    {
                        seen[other] = true;
                        pending.Push(other);
                    }
                }
            }

            component.Sort();
            components.Add(component);
        }

        return components;
    }

    /// <summary>The cutoff one island's layout runs under, from its own slice or the whole budget.</summary>
    private static LayoutDeadline DeadlineFor(GraphLayoutOptions options)
        => LayoutDeadline.After(options.LayoutSliceMs ?? options.LayoutBudgetMs);

    /// <summary>
    /// Splits a layout budget across the islands of one layout, so the first island cannot spend
    /// all of it. Islands of fewer than two nodes get nothing, every other island gets a floor plus
    /// a share of what is left in proportion to its node count, and the slices together stay inside
    /// <paramref name="totalMs"/>. A zero total stays zero, which means unlimited; a slice is never
    /// rounded down to zero, which would mean the same thing.
    /// </summary>
    /// <param name="nodeCounts">Node count of each island, in the order they will be laid out.</param>
    /// <param name="totalMs">Milliseconds the whole layout may spend, or zero for unlimited.</param>
    public static int[] SplitBudget(IReadOnlyList<int> nodeCounts, int totalMs)
    {
        ArgumentNullException.ThrowIfNull(nodeCounts);

        var slices = new int[nodeCounts.Count];

        if (totalMs <= 0)
        {
            return slices;
        }

        var islands = 0;
        var totalNodes = 0;

        for (var i = 0; i < nodeCounts.Count; i++)
        {
            if (nodeCounts[i] < 2)
            {
                continue;
            }

            islands++;
            totalNodes += nodeCounts[i];
        }

        if (islands == 0)
        {
            return slices;
        }

        var floor = Math.Min(SliceFloorMs, totalMs / islands);
        var shared = totalMs - (floor * islands);

        for (var i = 0; i < nodeCounts.Count; i++)
        {
            if (nodeCounts[i] >= 2)
            {
                slices[i] = Math.Max(1, floor + (int)((long)shared * nodeCounts[i] / totalNodes));
            }
        }

        return slices;
    }

    /// <summary>
    /// Whether two cards clear each other: they either miss in x entirely, or keep
    /// <paramref name="spacing"/> between them in y. Placement leaves every pair in that state,
    /// so it is the condition every later move has to preserve.
    /// </summary>
    internal static bool CardsClear(Vector2 aMin, Vector2 aMax, Vector2 bMin, Vector2 bMax, float spacing)
        => aMax.X <= bMin.X || bMax.X <= aMin.X
        || bMin.Y - aMax.Y >= spacing || aMin.Y - bMax.Y >= spacing;

    private static float WeightedMedian(List<(float Value, float Weight)> samples)
    {
        samples.Sort(static (a, b) => a.Value.CompareTo(b.Value));

        var total = 0f;

        foreach (var (_, weight) in samples)
        {
            total += weight;
        }

        var running = 0f;

        foreach (var (value, weight) in samples)
        {
            running += weight;

            if (running >= total / 2f)
            {
                return value;
            }
        }

        return samples[^1].Value;
    }

    /// <summary>
    /// Packs island rectangles into shelves aiming for a 16:9 overall shape. Returns the
    /// top-left origin for each rectangle in input order.
    /// </summary>
    /// <param name="sizes">Bounding size of each island.</param>
    /// <param name="padding">Gap left around every island.</param>
    public static Vector2[] PackComponents(Vector2[] sizes, float padding)
    {
        ArgumentNullException.ThrowIfNull(sizes);

        var origins = new Vector2[sizes.Length];

        if (sizes.Length == 0)
        {
            return origins;
        }

        var area = 0f;

        for (var i = 0; i < sizes.Length; i++)
        {
            area += (sizes[i].X + padding) * (sizes[i].Y + padding);
        }

        // Tallest first onto shelves; try a few widths around the aspect-ideal one and keep
        // the packing closest to the target shape.
        var order = Enumerable.Range(0, sizes.Length)
            .OrderByDescending(i => sizes[i].Y)
            .ThenBy(i => i)
            .ToArray();

        var baseWidth = MathF.Sqrt(area * PackingAspect);
        var bestScore = float.PositiveInfinity;

        foreach (var factor in new[] { 0.85f, 1f, 1.2f })
        {
            var maxWidth = Math.Max(baseWidth * factor, sizes.Max(static s => s.X) + padding);
            var candidate = new Vector2[sizes.Length];
            var shelfY = 0f;
            var shelfHeight = 0f;
            var cursorX = 0f;
            var usedWidth = 0f;

            foreach (var i in order)
            {
                var width = sizes[i].X + padding;
                var height = sizes[i].Y + padding;

                if (cursorX > 0f && cursorX + width > maxWidth)
                {
                    shelfY += shelfHeight;
                    shelfHeight = 0f;
                    cursorX = 0f;
                }

                candidate[i] = new Vector2(cursorX, shelfY);
                cursorX += width;
                shelfHeight = Math.Max(shelfHeight, height);
                usedWidth = Math.Max(usedWidth, cursorX);
            }

            var usedHeight = shelfY + shelfHeight;
            var aspect = usedWidth / Math.Max(1f, usedHeight);

            // Zero-width input has no shape to score, and still has to come out placed.
            var score = aspect > 0f ? Math.Max(aspect / PackingAspect, PackingAspect / aspect) : float.MaxValue;

            if (score < bestScore)
            {
                bestScore = score;
                origins = candidate;
            }
        }

        return origins;
    }

    /// <summary>
    /// Sugiyama-style layered placement. Real nodes and the dummies standing in for long wires
    /// share one index space so ordering, spacing and alignment treat them alike.
    /// </summary>
    private sealed class LayeredSolver(
        Vector2[] positions,
        Vector2[] sizes,
        GraphLayoutEdge[] edges,
        GraphLayoutOptions options,
        LayoutDeadline deadline,
        Vector2[]?[] routes)
    {
        /// <summary>
        /// One entry of a node's adjacency. <paramref name="Far"/> and <paramref name="Near"/> are
        /// the two socket heights relative to their own node centers, so the height this node wants
        /// in order to draw the wire level is <c>centerY[Other] + Far - Near</c>.
        /// </summary>
        private readonly record struct Link(int Other, float Far, float Near, float Weight);

        private readonly int realCount = sizes.Length;

        private int count;
        private int[] rankOf = [];
        private float[] height = [];
        private float[] width = [];

        private List<int>[] ranks = [];
        private List<Link>[] up = [];
        private List<Link>[] down = [];
        private int[] positionInRank = [];
        private float[] centerY = [];
        private int[] columnOf = [];
        private float[] columnX = [];

        /// <summary>Dummy chains keyed by the wire they stand in for, source to target order.</summary>
        private readonly Dictionary<int, List<int>> chains = [];
        private readonly HashSet<int> reversed = [];

        public void Run()
        {
            var crossWires = new List<int>(edges.Length);

            for (var i = 0; i < edges.Length; i++)
            {
                if (edges[i].From != edges[i].To)
                {
                    crossWires.Add(i);
                }
            }

            FindBackWires(crossWires);
            AssignRanks(crossWires);
            BuildLayers(crossWires);
            OrderLayers();
            AssignColumns();
            AssignHeights();
            ApplyPositions();

            RepairCrossings(positions, sizes, edges, options, deadline);

            // The repair moves cards, so the lanes the long wires run through are re-derived from
            // where the cards ended up rather than from where they were placed.
            AssignDummyHeights();
            EmitRoutes();
        }

        // DFS back-edge detection; the edges it finds are reversed for ranking so the rest of
        // the pipeline sees an acyclic graph.
        private void FindBackWires(List<int> crossWires)
        {
            var outgoing = new List<int>[realCount];

            for (var i = 0; i < realCount; i++)
            {
                outgoing[i] = [];
            }

            foreach (var wire in crossWires)
            {
                outgoing[edges[wire].From].Add(wire);
            }

            var state = new byte[realCount]; // 0 unvisited, 1 on stack, 2 done
            var stack = new Stack<(int NodeIdx, int EdgeIdx)>();

            for (var start = 0; start < realCount; start++)
            {
                if (state[start] != 0)
                {
                    continue;
                }

                state[start] = 1;
                stack.Push((start, 0));

                while (stack.Count > 0)
                {
                    var (nodeIdx, edgeIdx) = stack.Pop();

                    if (edgeIdx >= outgoing[nodeIdx].Count)
                    {
                        state[nodeIdx] = 2;
                        continue;
                    }

                    stack.Push((nodeIdx, edgeIdx + 1));

                    var target = edges[outgoing[nodeIdx][edgeIdx]].To;

                    if (state[target] == 0)
                    {
                        state[target] = 1;
                        stack.Push((target, 0));
                    }
                    else if (state[target] == 1)
                    {
                        reversed.Add(outgoing[nodeIdx][edgeIdx]);
                    }
                }
            }
        }

        private (int From, int To) Effective(int wire)
            => reversed.Contains(wire)
                ? (edges[wire].To, edges[wire].From)
                : (edges[wire].From, edges[wire].To);

        // Longest-path ranks over the acyclic orientation, then a tightening pass that pulls
        // every node as far right as its successors allow so wires span as few ranks as possible.
        private void AssignRanks(List<int> crossWires)
        {
            rankOf = new int[realCount];

            var incomingCount = new int[realCount];
            var outgoing = new List<int>[realCount];

            for (var i = 0; i < realCount; i++)
            {
                outgoing[i] = [];
            }

            foreach (var wire in crossWires)
            {
                var (from, to) = Effective(wire);
                outgoing[from].Add(to);
                incomingCount[to]++;
            }

            var pending = new int[realCount];
            Array.Copy(incomingCount, pending, realCount);

            var queue = new Queue<int>();

            for (var i = 0; i < realCount; i++)
            {
                if (pending[i] == 0)
                {
                    queue.Enqueue(i);
                }
            }

            var topological = new List<int>(realCount);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                topological.Add(current);

                foreach (var target in outgoing[current])
                {
                    rankOf[target] = Math.Max(rankOf[target], rankOf[current] + 1);

                    if (--pending[target] == 0)
                    {
                        queue.Enqueue(target);
                    }
                }
            }

            // Longest-path ranking maximises span: a sourceless chain lands at the left edge no
            // matter how far right the node it feeds sits. Walking the order backwards and pulling
            // each node up against its nearest successor moves the whole chain beside its consumer,
            // and because the walk is in reverse topological order the whole chain follows.
            //
            // Only spans past the threshold are closed. Slack of a rank or two is what gives the
            // ordering and repair passes room to work, so compressing it costs more crossings than
            // the shorter wires save; the pathological case this targets is a chain that sits at
            // the left edge with one wire reaching the far side of the graph.
            for (var i = topological.Count - 1; i >= 0; i--)
            {
                var node = topological[i];

                if (outgoing[node].Count == 0)
                {
                    continue;
                }

                var tightest = int.MaxValue;

                foreach (var target in outgoing[node])
                {
                    tightest = Math.Min(tightest, rankOf[target]);
                }

                if (tightest != int.MaxValue && tightest - rankOf[node] > options.TightenMinSpan)
                {
                    rankOf[node] = tightest - 1;
                }
            }
        }

        // Builds the per-rank membership lists plus the up/down adjacency the ordering and
        // alignment passes run on, inserting a dummy per intermediate rank for long wires.
        private void BuildLayers(List<int> crossWires)
        {
            var rankCount = rankOf.Max() + 1;
            var wantDummies = options.LongWireDummies;

            var dummyRanks = new List<int>();

            if (wantDummies)
            {
                foreach (var wire in crossWires)
                {
                    var (from, to) = Effective(wire);

                    for (var r = rankOf[from] + 1; r < rankOf[to]; r++)
                    {
                        dummyRanks.Add(r);
                    }
                }
            }

            count = realCount + dummyRanks.Count;
            height = new float[count];
            width = new float[count];
            centerY = new float[count];
            columnOf = new int[count];
            positionInRank = new int[count];

            var fullRanks = new int[count];
            Array.Copy(rankOf, fullRanks, realCount);

            for (var i = 0; i < realCount; i++)
            {
                width[i] = sizes[i].X;
                height[i] = sizes[i].Y;
            }

            for (var d = 0; d < dummyRanks.Count; d++)
            {
                var i = realCount + d;
                fullRanks[i] = dummyRanks[d];
                width[i] = 0f;
                height[i] = options.DummyLaneHeight;
            }

            rankOf = fullRanks;

            ranks = new List<int>[rankCount];

            for (var r = 0; r < rankCount; r++)
            {
                ranks[r] = [];
            }

            up = new List<Link>[count];
            down = new List<Link>[count];

            for (var i = 0; i < count; i++)
            {
                up[i] = [];
                down[i] = [];
            }

            var nextDummy = realCount;

            foreach (var wire in crossWires)
            {
                var (from, to) = Effective(wire);
                var weight = edges[wire].Dashed ? options.DashedWireWeight : options.SolidWireWeight;

                // A reversed wire still docks at its real sockets, so the anchors follow the
                // wire's own direction rather than the ranking direction.
                var sourceAnchor = AnchorOf(edges[wire].FromPivot, edges[wire].From);
                var targetAnchor = AnchorOf(edges[wire].ToPivot, edges[wire].To);
                var fromAnchor = reversed.Contains(wire) ? targetAnchor : sourceAnchor;
                var toAnchor = reversed.Contains(wire) ? sourceAnchor : targetAnchor;

                var span = rankOf[to] - rankOf[from];

                if (!wantDummies || span <= 1)
                {
                    Join(from, to, fromAnchor, toAnchor, weight);
                    continue;
                }

                var chain = new List<int>(span - 1);
                var previous = from;
                var previousAnchor = fromAnchor;

                for (var r = rankOf[from] + 1; r < rankOf[to]; r++)
                {
                    var dummy = nextDummy++;
                    chain.Add(dummy);
                    Join(previous, dummy, previousAnchor, 0f, weight);
                    previous = dummy;
                    previousAnchor = 0f;
                }

                Join(previous, to, previousAnchor, toAnchor, weight);

                if (reversed.Contains(wire))
                {
                    chain.Reverse();
                }

                chains[wire] = chain;
            }

            for (var i = 0; i < count; i++)
            {
                positionInRank[i] = ranks[rankOf[i]].Count;
                ranks[rankOf[i]].Add(i);
            }

            void Join(int from, int to, float fromAnchor, float toAnchor, float weight)
            {
                down[from].Add(new Link(to, toAnchor, fromAnchor, weight));
                up[to].Add(new Link(from, fromAnchor, toAnchor, weight));
            }
        }

        /// <summary>Height of a socket's pivot relative to its node's vertical center.</summary>
        private float AnchorOf(Vector2 pivot, int node) => pivot.Y - sizes[node].Y / 2f;

        /// <summary>
        /// Barycentre sweeps under the layout deadline. A sweep is only kept when the count that
        /// judges it completed, so an unmeasurable graph keeps the order the layers were built in.
        /// </summary>
        private void OrderLayers()
        {
            var best = Snapshot();
            var bestCrossings = CountCrossings();

            if (bestCrossings < 0)
            {
                return;
            }

            for (var sweep = 0; sweep < 12 && !deadline.Expired; sweep++)
            {
                var forward = sweep % 2 == 0;

                for (var step = 0; step < ranks.Length; step++)
                {
                    var r = forward ? step : ranks.Length - 1 - step;
                    var reference = forward ? up : down;

                    if ((forward && r == 0) || (!forward && r == ranks.Length - 1))
                    {
                        continue;
                    }

                    SortRankBy(r, reference);
                }

                Transpose();

                var crossings = CountCrossings();

                if (crossings < 0)
                {
                    break;
                }

                if (crossings < bestCrossings)
                {
                    bestCrossings = crossings;
                    best = Snapshot();
                }
            }

            Restore(best);
        }

        private void SortRankBy(int r, List<Link>[] reference)
        {
            var rank = ranks[r];

            var keyed = rank
                .Select(i => (Index: i, Key: BarycentreOf(i, reference), Fallback: positionInRank[i]))
                .OrderBy(static entry => entry.Key)
                .ThenBy(static entry => entry.Fallback)
                .ToList();

            for (var order = 0; order < keyed.Count; order++)
            {
                var node = keyed[order].Index;
                rank[order] = node;
                positionInRank[node] = order;
            }
        }

        // Positions are normalised into [0,1] within their own rank so a node in a rank of three
        // and a node in a rank of forty produce comparable keys.
        private float BarycentreOf(int node, List<Link>[] reference)
        {
            if (reference[node].Count == 0)
            {
                return NormalisedPosition(node);
            }

            var sum = 0f;
            var weight = 0f;

            foreach (var link in reference[node])
            {
                sum += NormalisedPosition(link.Other) * link.Weight;
                weight += link.Weight;
            }

            return weight > 0f ? sum / weight : NormalisedPosition(node);
        }

        private float NormalisedPosition(int node)
        {
            var size = ranks[rankOf[node]].Count;
            return size <= 1 ? 0.5f : positionInRank[node] / (float)(size - 1);
        }

        // Adjacent-swap refinement: barycentre ordering leaves pairs that trade places for a
        // strict win, and checking them directly is cheap.
        private void Transpose()
        {
            for (var pass = 0; pass < 4; pass++)
            {
                var improved = false;

                foreach (var rank in ranks)
                {
                    for (var i = 0; i + 1 < rank.Count; i++)
                    {
                        var a = rank[i];
                        var b = rank[i + 1];

                        if (LocalCrossings(a, b) <= LocalCrossings(b, a))
                        {
                            continue;
                        }

                        rank[i] = b;
                        rank[i + 1] = a;
                        positionInRank[a] = i + 1;
                        positionInRank[b] = i;
                        improved = true;
                    }
                }

                if (!improved)
                {
                    return;
                }
            }
        }

        // Crossings contributed by the wires of two neighbours when left sits before right.
        // Deliberately counted on node order alone: folding the socket height into the key was
        // measurably worse on the crossbar fixture, because the ordering that minimises it is
        // not the one that minimises the crossings actually drawn.
        private int LocalCrossings(int left, int right)
        {
            return Count(up) + Count(down);

            int Count(List<Link>[] reference)
            {
                var crossings = 0;

                foreach (var a in reference[left])
                {
                    foreach (var b in reference[right])
                    {
                        if (positionInRank[a.Other] > positionInRank[b.Other])
                        {
                            crossings++;
                        }
                    }
                }

                return crossings;
            }
        }

        /// <summary>Projections and boundaries counted between two checks of the layout deadline.</summary>
        private const int CrossingCountCheckInterval = 512;

        /// <summary>
        /// Inversions across every rank boundary, counted with a Fenwick tree so hub-heavy ranks
        /// stay affordable. A wire spanning several ranks is projected onto each boundary it
        /// crosses, so ordering sees the wires that have no dummies standing in for them too.
        /// Heights are normalised per rank, so the two sides of a boundary compare.
        /// </summary>
        /// <returns>The crossing count, or -1 when the layout deadline passed before it was complete.</returns>
        private int CountCrossings()
        {
            var boundaries = new List<(float Upper, float Lower)>[Math.Max(1, ranks.Length)];

            for (var i = 0; i < count; i++)
            {
                if (i % CrossingCountCheckInterval == 0 && deadline.Expired)
                {
                    return -1;
                }

                foreach (var link in down[i])
                {
                    var fromRank = rankOf[i];
                    var toRank = rankOf[link.Other];

                    if (toRank <= fromRank)
                    {
                        continue;
                    }

                    var fromKey = NormalisedPosition(i);
                    var toKey = NormalisedPosition(link.Other);
                    var span = (float)(toRank - fromRank);

                    for (var r = fromRank; r < toRank; r++)
                    {
                        var upper = fromKey + ((toKey - fromKey) * ((r - fromRank) / span));
                        var lower = fromKey + ((toKey - fromKey) * ((r + 1 - fromRank) / span));
                        (boundaries[r] ??= []).Add((upper, lower));
                    }
                }
            }

            var total = 0;

            for (var r = 0; r + 1 < ranks.Length; r++)
            {
                if (r % CrossingCountCheckInterval == 0 && deadline.Expired)
                {
                    return -1;
                }

                var boundary = boundaries[r];

                if (boundary == null || boundary.Count < 2)
                {
                    continue;
                }

                var lowers = new float[boundary.Count];

                for (var i = 0; i < boundary.Count; i++)
                {
                    lowers[i] = boundary[i].Lower;
                }

                Array.Sort(lowers);
                boundary.Sort(static (a, b) => a.Upper != b.Upper ? a.Upper.CompareTo(b.Upper) : a.Lower.CompareTo(b.Lower));

                var size = boundary.Count + 1;
                var tree = new int[size + 1];
                var seen = 0;

                foreach (var (_, lower) in boundary)
                {
                    // Equal heights share a slot, so two wires arriving level do not count.
                    var found = Array.BinarySearch(lowers, lower);
                    var slot = (found < 0 ? ~found : found) + 1;

                    total += seen - Query(slot);
                    Add(slot);
                    seen++;
                }

                int Query(int position)
                {
                    var sum = 0;

                    for (var i = position; i > 0; i -= i & -i)
                    {
                        sum += tree[i];
                    }

                    return sum;
                }

                void Add(int position)
                {
                    for (var i = position; i <= size; i += i & -i)
                    {
                        tree[i]++;
                    }
                }
            }

            return total;
        }

        private int[][] Snapshot() => [.. ranks.Select(static rank => rank.ToArray())];

        private void Restore(int[][] snapshot)
        {
            for (var r = 0; r < ranks.Length; r++)
            {
                ranks[r].Clear();
                ranks[r].AddRange(snapshot[r]);

                for (var i = 0; i < ranks[r].Count; i++)
                {
                    positionInRank[ranks[r][i]] = i;
                }
            }
        }

        // Oversized ranks wrap into several height-capped columns so hub-heavy graphs come out
        // block-shaped instead of as one enormous strip.
        private void AssignColumns()
        {
            var area = 0f;

            for (var i = 0; i < count; i++)
            {
                area += (height[i] + options.NodeSpacing) * (width[i] + options.LayerSpacing);
            }

            var heightCap = Math.Max(1200f, MathF.Sqrt(area / PackingAspect));
            var built = new List<List<int>>();
            var rankOfColumn = new List<int>();

            for (var r = 0; r < ranks.Length; r++)
            {
                // The rank order is the crossing-minimised one, so wrapping chunks it rather than
                // re-sorting it; any reordering here would discard that ordering wholesale.
                var members = ranks[r];

                var column = new List<int>();
                built.Add(column);
                rankOfColumn.Add(r);
                var stack = 0f;

                foreach (var i in members)
                {
                    if (column.Count > 0 && stack + height[i] > heightCap)
                    {
                        column = [];
                        built.Add(column);
                        rankOfColumn.Add(r);
                        stack = 0f;
                    }

                    columnOf[i] = built.Count - 1;
                    column.Add(i);
                    stack += height[i] + options.NodeSpacing;
                }
            }

            // Horizontal: each column wide enough for its widest card. Sub-columns of one rank
            // sit closer together than real rank boundaries so wrapping costs less travel.
            columnX = new float[built.Count];
            var x = 0f;

            for (var c = 0; c < built.Count; c++)
            {
                columnX[c] = x;
                var widest = built[c].Count == 0 ? 0f : built[c].Max(i => width[i]);
                // Sub-columns of one wrapped rank sit closer than a real rank boundary, so wires
                // crossing the wrap travel less than a full layer to do it.
                var sameRank = c + 1 < built.Count && rankOfColumn[c + 1] == rankOfColumn[c];
                x += widest + (sameRank ? SubColumnSeparation : options.LayerSpacing);
            }

            columns = built;
        }

        private List<List<int>> columns = [];

        // Vertical: stack, then align every node on the weighted median of where its wires want
        // it, and re-impose the order with minimum gaps.
        private void AssignHeights()
        {
            foreach (var column in columns)
            {
                var y = 0f;

                foreach (var i in column)
                {
                    centerY[i] = y + height[i] / 2f;
                    y += height[i] + options.NodeSpacing;
                }
            }

            var desired = new List<(float Value, float Weight)>();

            for (var sweep = 0; sweep < 8; sweep++)
            {
                var forward = sweep % 2 == 0;

                for (var c = forward ? 0 : columns.Count - 1; forward ? c < columns.Count : c >= 0; c += forward ? 1 : -1)
                {
                    var column = columns[c];
                    var anchored = 0;
                    var drift = 0f;

                    foreach (var i in column)
                    {
                        desired.Clear();
                        Collect(i, desired);

                        if (desired.Count > 0)
                        {
                            centerY[i] = WeightedMedian(desired);
                        }
                    }

                    for (var pass = 0; pass < 2; pass++)
                    {
                        for (var j = 1; j < column.Count; j++)
                        {
                            var upper = column[j - 1];
                            var lower = column[j];
                            var overlap = centerY[upper] + height[upper] / 2f + options.NodeSpacing - (centerY[lower] - height[lower] / 2f);

                            if (overlap > 0f)
                            {
                                centerY[upper] -= overlap / 2f;
                                centerY[lower] += overlap / 2f;
                            }
                        }
                    }

                    EnforceColumnGaps(column);

                    // Overlap resolution biases downward; re-centering each column on its
                    // neighbors stops the drift from compounding into a staircase.
                    foreach (var i in column)
                    {
                        desired.Clear();
                        Collect(i, desired);

                        if (desired.Count == 0)
                        {
                            continue;
                        }

                        var mean = 0f;

                        foreach (var (value, _) in desired)
                        {
                            mean += value;
                        }

                        // Measured against the same anchored targets the alignment aims at, so
                        // re-centering the column cannot undo the alignment it just applied.
                        drift += centerY[i] - mean / desired.Count;
                        anchored++;
                    }

                    if (anchored > 0)
                    {
                        drift /= anchored;

                        foreach (var i in column)
                        {
                            centerY[i] -= drift;
                        }
                    }
                }
            }

            // Chain staircases climb one row per hop and drag the canvas out; compress
            // everything beyond the dense band, then restore hard separation per column.
            SquashOutliers();
        }

        /// <summary>Where each of a node's wires wants that node to sit, and how hard it pulls.</summary>
        private void Collect(int node, List<(float Value, float Weight)> into)
        {
            foreach (var link in up[node])
            {
                into.Add((centerY[link.Other] + link.Far - link.Near, link.Weight));
            }

            foreach (var link in down[node])
            {
                into.Add((centerY[link.Other] + link.Far - link.Near, link.Weight));
            }
        }

        // Top-down pass guaranteeing minimum vertical gaps within a column, preserving order.
        private void EnforceColumnGaps(List<int> column)
        {
            for (var j = 1; j < column.Count; j++)
            {
                var upper = column[j - 1];
                var lower = column[j];
                var gap = rankOf[upper] == rankOf[lower] && width[upper] == 0f && width[lower] == 0f
                    ? options.DummyLaneHeight
                    : options.NodeSpacing;
                var overlap = centerY[upper] + height[upper] / 2f + gap - (centerY[lower] - height[lower] / 2f);

                if (overlap > 0f)
                {
                    centerY[lower] += overlap;
                }
            }
        }

        /// <summary>
        /// Re-derives the heights of the long-wire dummies from the cards' final positions. The
        /// cards themselves stay where the repair left them; only the lanes between them move.
        /// </summary>
        private void AssignDummyHeights()
        {
            if (count == realCount)
            {
                return;
            }

            for (var i = 0; i < realCount; i++)
            {
                centerY[i] = positions[i].Y + height[i] / 2f;
            }

            var desired = new List<(float Value, float Weight)>();
            var lane = new List<int>();

            for (var sweep = 0; sweep < 8; sweep++)
            {
                var forward = sweep % 2 == 0;

                for (var c = forward ? 0 : columns.Count - 1; forward ? c < columns.Count : c >= 0; c += forward ? 1 : -1)
                {
                    lane.Clear();

                    foreach (var i in columns[c])
                    {
                        if (i < realCount)
                        {
                            continue;
                        }

                        desired.Clear();
                        Collect(i, desired);

                        if (desired.Count > 0)
                        {
                            centerY[i] = WeightedMedian(desired);
                        }

                        lane.Add(i);
                    }

                    SpaceDummies(columns[c], lane);
                }
            }
        }

        /// <summary>Moves the dummies of one column out of the cards in it and off each other.</summary>
        private void SpaceDummies(List<int> column, List<int> lane)
        {
            if (lane.Count == 0)
            {
                return;
            }

            lane.Sort((a, b) => centerY[a].CompareTo(centerY[b]));

            for (var round = 0; round < 2; round++)
            {
                foreach (var dummy in lane)
                {
                    foreach (var i in column)
                    {
                        if (i >= realCount)
                        {
                            continue;
                        }

                        var top = centerY[i] - (height[i] / 2f) - options.DummyLaneHeight;
                        var bottom = centerY[i] + (height[i] / 2f) + options.DummyLaneHeight;

                        if (centerY[dummy] > top && centerY[dummy] < bottom)
                        {
                            centerY[dummy] = centerY[dummy] - top < bottom - centerY[dummy] ? top : bottom;
                        }
                    }
                }

                for (var j = 1; j < lane.Count; j++)
                {
                    var overlap = centerY[lane[j - 1]] + options.DummyLaneHeight - centerY[lane[j]];

                    if (overlap > 0f)
                    {
                        centerY[lane[j]] += overlap;
                    }
                }
            }
        }

        private void SquashOutliers()
        {
            const float Slack = 600f;
            const float Squash = 0.25f;

            var centers = new List<float>();

            foreach (var column in columns)
            {
                foreach (var i in column)
                {
                    centers.Add(centerY[i]);
                }
            }

            if (centers.Count < 8)
            {
                return;
            }

            centers.Sort();
            var top = centers[(int)(centers.Count * 0.08f)] - Slack;
            var bottom = centers[(int)(centers.Count * 0.92f)] + Slack;

            foreach (var column in columns)
            {
                foreach (var i in column)
                {
                    if (centerY[i] < top)
                    {
                        centerY[i] = top - (top - centerY[i]) * Squash;
                    }
                    else if (centerY[i] > bottom)
                    {
                        centerY[i] = bottom + (centerY[i] - bottom) * Squash;
                    }
                }

                EnforceColumnGaps(column);
            }
        }

        private void ApplyPositions()
        {
            for (var i = 0; i < realCount; i++)
            {
                positions[i] = new Vector2(columnX[columnOf[i]], centerY[i] - sizes[i].Y / 2f);
            }
        }

        /// <summary>
        /// Turns each long wire's dummy chain into the waypoints it is drawn through, so the wire
        /// runs between the cards of the ranks it spans rather than over them.
        /// </summary>
        private void EmitRoutes()
        {
            foreach (var (wire, chain) in chains)
            {
                // A reversed wire's chain runs against the direction it is drawn in, so leave it
                // to the plain curve rather than routing it backwards through its own dummies.
                if (reversed.Contains(wire))
                {
                    continue;
                }

                routes[wire] = [.. chain.Select(d => new Vector2(columnX[columnOf[d]], centerY[d]))];
            }
        }
    }
}
