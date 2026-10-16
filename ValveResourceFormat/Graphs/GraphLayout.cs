using System.Linq;

namespace ValveResourceFormat.Graphs;

/// <summary>
/// Graph layout: layered Sugiyama placement per island and 16:9 shelf packing of the island
/// rectangles. Nodes are indices into parallel arrays of size and placement, and wires are
/// <see cref="GraphLayoutEdge"/> values against those indices, so a caller drives it without
/// handing over its own node type.
/// </summary>
internal static class GraphLayout
{
    private const float PackingAspect = 16f / 9f;

    /// <summary>Gap between the wrapped sub-columns of one oversized rank.</summary>
    private const float SubColumnSeparation = 96f;

    /// <summary>Part of an island's budget, as a divisor, held back for routing wires around cards.</summary>
    private const int RoutingShare = 4;

    /// <summary>Layout time every island is given before the rest is shared out by size.</summary>
    private const int SliceFloorMs = 250;

    /// <summary>Most barycentre sweeps the ordering runs, alternating direction.</summary>
    private const int OrderingSweeps = 24;

    /// <summary>Most adjacent-swap passes run after each ordering sweep.</summary>
    private const int TransposePasses = 32;

    /// <summary>Snapping distance for left edges that count as one column.</summary>
    private const float ColumnQuantum = 8f;

    /// <summary>Slack allowed on the separation test, so gaps laid out at exactly the spacing pass.</summary>
    private const float SeparationTolerance = 0.5f;

    /// <summary>Largest height difference between two sockets that still draws a wire level.</summary>
    internal const float LevelTolerance = 0.5f;

    /// <summary>Crossings the snap that follows the crossing repair may add for every wire it levels.</summary>
    private const float SnapCrossingsPerLevelWire = 2f;

    /// <summary>Gap left around each island when the laid-out islands are packed together.</summary>
    internal const float IslandPadding = 150f;

    /// <summary>Horizontal gap between layers.</summary>
    private const float LayerSpacing = 320f;

    /// <summary>Vertical gap between cards in one column.</summary>
    internal const float NodeSpacing = 60f;

    /// <summary>The spacing <see cref="CardsClear"/> is given by the passes that move placed cards.</summary>
    internal const float MinimumGap = NodeSpacing - SeparationTolerance;

    /// <summary>Vertical room reserved for one long-wire dummy slot inside a gutter.</summary>
    internal const float DummyLaneHeight = 14f;

    /// <summary>Weight a solid wire pulls with during alignment.</summary>
    private const float SolidWireWeight = 1f;

    /// <summary>Weight a dashed wire pulls with during alignment, so a secondary binding does not outvote the primary flow.</summary>
    private const float DashedWireWeight = 0.25f;

    /// <summary>
    /// Places one connected island and returns the waypoints each long wire should be drawn
    /// through, indexed like <paramref name="edges"/> and null for the wires that draw as a plain
    /// curve. Long wires get waypoints through their dummies when
    /// <see cref="GraphLayoutOptions.LongWireDummies"/> is set, and any forward wire that would be
    /// drawn across a card can be routed through the gaps between cards.
    /// </summary>
    /// <param name="positions">
    /// Receives the top-left corner of each node. Contents on entry are ignored; the array only
    /// has to be as long as <paramref name="sizes"/>.
    /// </param>
    /// <param name="sizes">Measured size of each node.</param>
    /// <param name="edges">The wires between them. A wire from a node to itself is ignored.</param>
    /// <param name="options">Layout tuning.</param>
    /// <param name="budgetMs">Milliseconds this island may spend refining, or zero for unlimited.</param>
    public static Vector2[]?[] Layout(Vector2[] positions, Vector2[] sizes, GraphLayoutEdge[] edges, GraphLayoutOptions options, int budgetMs)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(sizes);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(options);

        var routes = new Vector2[]?[edges.Length];

        if (sizes.Length > 1)
        {
            new LayeredSolver(positions, sizes, edges, options, budgetMs, routes).Run();
        }

        return routes;
    }

    /// <summary>
    /// Runs the passes that follow placement on an island where it stands: the crossing repair,
    /// the snap onto heights that draw wires level, and the rerouting of wires drawn across cards.
    /// Wires whose cards moved and that now run level lose their route first.
    /// </summary>
    /// <param name="positions">Current top-left corner of each node, updated in place.</param>
    /// <param name="sizes">Measured size of each node.</param>
    /// <param name="edges">The wires between them. A wire from a node to itself is ignored.</param>
    /// <param name="routes">Current route of each wire, indexed like <paramref name="edges"/>; updated in place.</param>
    /// <param name="budgetMs">Milliseconds each pass may spend, or zero for unlimited.</param>
    public static void Refine(Vector2[] positions, Vector2[] sizes, GraphLayoutEdge[] edges, Vector2[]?[] routes, int budgetMs)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(sizes);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(routes);

        if (sizes.Length < 2)
        {
            return;
        }

        var placed = positions[..];
        var deadline = LayoutDeadline.After(budgetMs);

        Refine(positions, sizes, edges, routes, deadline, deadline, deadline, static () => null, () =>
        {
            for (var i = 0; i < edges.Length; i++)
            {
                var moved = positions[edges[i].From] != placed[edges[i].From] || positions[edges[i].To] != placed[edges[i].To];

                if (moved && RunsLevel(edges[i].SourcePoint(positions), edges[i].TargetPoint(positions)))
                {
                    routes[i] = null;
                }
            }
        });
    }

    /// <summary>
    /// The passes that follow placement: the crossing repair, the snap towards targets or onto
    /// heights that draw wires level, and the rerouting of forward wires drawn across cards.
    /// </summary>
    /// <param name="positions">Current top-left corner of each node, updated in place.</param>
    /// <param name="sizes">Measured size of each node.</param>
    /// <param name="edges">The wires between them.</param>
    /// <param name="routes">Route of each wire, indexed like <paramref name="edges"/>; updated in place.</param>
    /// <param name="repairDeadline">When the crossing repair stops.</param>
    /// <param name="snapDeadline">When the snap stops.</param>
    /// <param name="routingDeadline">When the rerouting stops.</param>
    /// <param name="targets">Called after the repair for the top edge each card is snapped towards, or null to only level wires.</param>
    /// <param name="emitRoutes">Called once the cards are final, to set the routes the rerouting starts from.</param>
    private static void Refine(
        Vector2[] positions,
        Vector2[] sizes,
        GraphLayoutEdge[] edges,
        Vector2[]?[] routes,
        LayoutDeadline repairDeadline,
        LayoutDeadline snapDeadline,
        LayoutDeadline routingDeadline,
        Func<float[]?> targets,
        Action emitRoutes)
    {
        new CrossingRepair(positions, sizes, edges, repairDeadline).Run();

        if (!snapDeadline.Expired)
        {
            new CardSnap(positions, sizes, edges, snapDeadline, SnapCrossingsPerLevelWire, rounds: 4).Run(targets());
        }

        emitRoutes();
        new GraphWireRouter(positions, sizes, edges, routes, routingDeadline).Run();
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

        var joined = new DisjointSets(nodeCount);

        foreach (var edge in edges)
        {
            joined.Union(edge.From, edge.To);
        }

        var components = new List<List<int>>();
        var componentOf = new Dictionary<int, List<int>>();

        for (var node = 0; node < nodeCount; node++)
        {
            var root = joined.Find(node);

            if (!componentOf.TryGetValue(root, out var component))
            {
                componentOf[root] = component = [];
                components.Add(component);
            }

            component.Add(node);
        }

        return components;
    }

    /// <summary>Per node, the indices of the wires touching it, skipping wires from a node to itself.</summary>
    internal static List<int>[] IncidentWires(int nodeCount, GraphLayoutEdge[] edges)
    {
        var incident = new List<int>[nodeCount];

        for (var i = 0; i < nodeCount; i++)
        {
            incident[i] = [];
        }

        for (var i = 0; i < edges.Length; i++)
        {
            if (edges[i].From != edges[i].To)
            {
                incident[edges[i].From].Add(i);
                incident[edges[i].To].Add(i);
            }
        }

        return incident;
    }

    /// <summary>The placed cards grouped by <see cref="ColumnKey"/>, groups in order of their first card.</summary>
    internal static List<List<int>> GroupByColumn(Vector2[] positions, int nodeCount)
    {
        var columns = new List<List<int>>();
        var columnOf = new Dictionary<int, List<int>>();

        for (var node = 0; node < nodeCount; node++)
        {
            var key = ColumnKey(positions[node].X);

            if (!columnOf.TryGetValue(key, out var column))
            {
                columnOf[key] = column = [];
                columns.Add(column);
            }

            column.Add(node);
        }

        return columns;
    }

    /// <summary>
    /// Lays out islands one after another under one shared budget, smallest first, so the time a
    /// quick island leaves unused passes on to the biggest ones.
    /// </summary>
    /// <param name="nodeCounts">Node count of each island.</param>
    /// <param name="budgetMs">Milliseconds all islands together may spend, or zero for unlimited.</param>
    /// <param name="layoutIsland">Lays out the island at an index within the given milliseconds, zero meaning unlimited.</param>
    internal static void LayoutIslands(IReadOnlyList<int> nodeCounts, int budgetMs, Action<int, int> layoutIsland)
    {
        var smallestFirst = Enumerable.Range(0, nodeCounts.Count).OrderBy(i => nodeCounts[i]).ToArray();
        var budget = new IslandBudget([.. smallestFirst.Select(i => nodeCounts[i])], budgetMs);

        for (var turn = 0; turn < smallestFirst.Length; turn++)
        {
            var island = smallestFirst[turn];
            layoutIsland(island, nodeCounts[island] < 2 ? 0 : budget.SliceFor(turn));
        }
    }

    /// <summary>
    /// Packs laid-out islands toward a screen shape and returns, per island, the offset that moves
    /// it from where it was laid out to where it is packed.
    /// </summary>
    /// <param name="mins">Top-left corner of each island's bounding box.</param>
    /// <param name="maxs">Bottom-right corner of each island's bounding box.</param>
    internal static Vector2[] PackIslands(Vector2[] mins, Vector2[] maxs)
    {
        var sizes = new Vector2[mins.Length];

        for (var i = 0; i < mins.Length; i++)
        {
            sizes[i] = maxs[i] - mins[i];
        }

        var offsets = PackComponents(sizes, IslandPadding);

        for (var i = 0; i < mins.Length; i++)
        {
            offsets[i] -= mins[i];
        }

        return offsets;
    }

    /// <summary>
    /// Splits a layout budget across islands so the first cannot spend all of it. Islands of
    /// fewer than two nodes get nothing; others get a floor plus a share of the rest by node
    /// count, with the slices together staying within <paramref name="totalMs"/>. A zero total
    /// stays zero (unlimited); a slice never rounds down to zero, which would also mean unlimited.
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

    /// <summary>Key shared by the cards whose left edges sit in one column.</summary>
    internal static int ColumnKey(float left) => (int)MathF.Round(left / ColumnQuantum);

    /// <summary>Whether a wire runs left to right with its two ends within <see cref="LevelTolerance"/> of level.</summary>
    internal static bool RunsLevel(Vector2 from, Vector2 to)
        => to.X > from.X && MathF.Abs(to.Y - from.Y) <= LevelTolerance;

    /// <summary>
    /// Packs island rectangles into shelves aiming for a 16:9 overall shape. Returns the
    /// top-left origin for each rectangle in input order.
    /// </summary>
    /// <param name="sizes">Bounding size of each island.</param>
    /// <param name="padding">Gap left around every island.</param>
    private static Vector2[] PackComponents(Vector2[] sizes, float padding)
    {
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
        int budgetMs,
        Vector2[]?[] routes)
    {
        /// <summary>Milliseconds the placement passes may spend, leaving the last part of the budget to routing.</summary>
        private static int PlacementMs(int budgetMs) => budgetMs <= 0 ? 0 : Math.Max(1, budgetMs - (budgetMs / RoutingShare));

        /// <summary>Milliseconds the crossing repair may spend, leaving the rest of the placement share to the snap.</summary>
        private static int RepairMs(int budgetMs) => budgetMs <= 0 ? 0 : Math.Max(1, PlacementMs(budgetMs) * 4 / 5);

        private readonly LayoutDeadline deadline = LayoutDeadline.After(PlacementMs(budgetMs));
        private readonly LayoutDeadline repairDeadline = LayoutDeadline.After(RepairMs(budgetMs));
        private readonly LayoutDeadline routingDeadline = LayoutDeadline.After(budgetMs);

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
        private List<List<int>> columns = [];

        /// <summary>
        /// Dummy chains keyed by the wire they stand in for, in source to target order. Reversed wires
        /// have none here and draw as a plain curve.
        /// </summary>
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
            SolveHeights(columns, columnOf, deadline);
            ApplyPositions();

            Refine(positions, sizes, edges, routes, repairDeadline, deadline, routingDeadline, SnapTargets, () =>
            {
                PlaceDummies();
                EmitRoutes();
            });
        }

        // DFS back-edge detection. Reversing those edges for ranking leaves an acyclic graph.
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

            // Walking in reverse topological order, a node pulled up against its nearest successor
            // carries the chain feeding it along.
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

                if (tightest != int.MaxValue && tightest - rankOf[node] > 1)
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
                height[i] = DummyLaneHeight;
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
                var weight = edges[wire].Dashed ? DashedWireWeight : SolidWireWeight;

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

                if (!reversed.Contains(wire))
                {
                    chains[wire] = chain;
                }
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
        /// Sweeping stops early once a pair of sweeps returns the order it started from, since every
        /// later pair would repeat it.
        /// </summary>
        private void OrderLayers()
        {
            var best = Snapshot();
            var bestCrossings = CountCrossings();

            if (bestCrossings < 0)
            {
                return;
            }

            var pairStart = best;

            for (var sweep = 0; sweep < OrderingSweeps && !deadline.Expired; sweep++)
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

                if (!forward)
                {
                    if (SameOrder(pairStart))
                    {
                        break;
                    }

                    pairStart = Snapshot();
                }
            }

            Restore(best);
        }

        private bool SameOrder(int[][] snapshot)
        {
            for (var r = 0; r < ranks.Length; r++)
            {
                if (!ranks[r].SequenceEqual(snapshot[r]))
                {
                    return false;
                }
            }

            return true;
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

        // Positions are normalised into [0,1] within their own rank.
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
                sum += SocketPosition(link.Other, positionInRank[link.Other], link.Far) * link.Weight;
                weight += link.Weight;
            }

            return weight > 0f ? sum / weight : NormalisedPosition(node);
        }

        private float NormalisedPosition(int node) => NormalisedPosition(node, positionInRank[node]);

        private float NormalisedPosition(int node, int slot)
        {
            var size = ranks[rankOf[node]].Count;
            return size <= 1 ? 0.5f : slot / (float)(size - 1);
        }

        /// <summary>
        /// Where a socket sits within its rank, normalised into [0,1]. Each node of the rank owns an
        /// equal share of that range and its sockets fall inside the share at their height on the
        /// card, so wires into one card at different rows have a defined order.
        /// </summary>
        /// <param name="node">The node the socket is on.</param>
        /// <param name="slot">The position of that node within its rank.</param>
        /// <param name="anchor">Height of the socket relative to the node's vertical center.</param>
        private float SocketPosition(int node, int slot, float anchor)
        {
            var row = Math.Clamp(anchor / height[node] + 0.5f, 0f, 1f);
            return (slot + row) / ranks[rankOf[node]].Count;
        }

        // Swaps neighbours of a rank while that strictly removes crossings.
        private void Transpose()
        {
            for (var pass = 0; pass < TransposePasses; pass++)
            {
                var improved = false;

                foreach (var rank in ranks)
                {
                    for (var i = 0; i + 1 < rank.Count; i++)
                    {
                        var a = rank[i];
                        var b = rank[i + 1];

                        var (aFirst, bFirst) = LocalCrossings(a, b, i);

                        if (aFirst <= bFirst)
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

        /// <summary>
        /// Crossings contributed by the wires of two neighbours of one rank, with <paramref name="a"/>
        /// at <paramref name="slot"/> and <paramref name="b"/> directly after it, and with the two
        /// the other way round. A pair crosses when the two wires have traded places by the last
        /// rank boundary they both reach, with each wire running straight from its node towards the
        /// far node, so wires spanning several ranks are judged as <see cref="CountCrossings"/>
        /// projects them. Positions are taken per node, not per socket.
        /// </summary>
        private (int AFirst, int BFirst) LocalCrossings(int a, int b, int slot)
        {
            var aLeft = NormalisedPosition(a, slot);
            var aRight = NormalisedPosition(a, slot + 1);
            var bLeft = NormalisedPosition(b, slot);
            var bRight = NormalisedPosition(b, slot + 1);
            var aFirst = 0;
            var bFirst = 0;

            Count(up);
            Count(down);
            return (aFirst, bFirst);

            void Count(List<Link>[] reference)
            {
                var linksB = reference[b];

                if (reference[a].Count == 0 || linksB.Count == 0)
                {
                    return;
                }

                Span<float> farB = linksB.Count <= 256 ? stackalloc float[linksB.Count] : new float[linksB.Count];
                Span<int> spanB = linksB.Count <= 256 ? stackalloc int[linksB.Count] : new int[linksB.Count];

                for (var j = 0; j < linksB.Count; j++)
                {
                    farB[j] = NormalisedPosition(linksB[j].Other);
                    spanB[j] = Math.Abs(rankOf[linksB[j].Other] - rankOf[b]);
                }

                foreach (var linkA in reference[a])
                {
                    var spanA = Math.Abs(rankOf[linkA.Other] - rankOf[a]);
                    var farA = NormalisedPosition(linkA.Other);

                    for (var j = 0; j < linksB.Count; j++)
                    {
                        var shared = (float)Math.Min(spanA, spanB[j]);

                        if (aLeft + ((farA - aLeft) * shared / spanA) > bRight + ((farB[j] - bRight) * shared / spanB[j]))
                        {
                            aFirst++;
                        }

                        if (bLeft + ((farB[j] - bLeft) * shared / spanB[j]) > aRight + ((farA - aRight) * shared / spanA))
                        {
                            bFirst++;
                        }
                    }
                }
            }
        }

        /// <summary>Projections and boundaries counted between two checks of the layout deadline.</summary>
        private const int CrossingCountCheckInterval = 512;

        /// <summary>
        /// Inversions across every rank boundary, counted with a Fenwick tree so hub-heavy ranks
        /// stay affordable. A wire spanning several ranks is projected onto each boundary it
        /// crosses, so ordering sees the wires that have no dummies standing in for them too.
        /// Heights are socket positions normalised per rank, so the two sides of a boundary compare
        /// and two wires into one card cross when their sources sit in the opposite order to the
        /// rows they dock at.
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

                    var fromKey = SocketPosition(i, positionInRank[i], link.Near);
                    var toKey = SocketPosition(link.Other, positionInRank[link.Other], link.Far);
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

        /// <summary>
        /// Gives every rank one column, or several height-capped sub-columns when it is oversized.
        /// The rank order is dealt across its sub-columns in turn, so each one spans the whole rank.
        /// Cards feeding one consumer are dealt as one run, right to left, with the card docking
        /// lowest nearest the consumer, so the farther cards' wires pass over the nearer ones into
        /// higher sockets.
        /// </summary>
        private void AssignColumns()
        {
            var area = 0f;

            for (var i = 0; i < count; i++)
            {
                area += (height[i] + NodeSpacing) * (width[i] + LayerSpacing);
            }

            var heightCap = Math.Max(1200f, MathF.Sqrt(area / PackingAspect));
            var built = new List<List<int>>();
            var rankOfColumn = new List<int>();

            for (var r = 0; r < ranks.Length; r++)
            {
                var members = ranks[r];
                var stack = 0f;

                foreach (var i in members)
                {
                    stack += height[i] + NodeSpacing;
                }

                var wraps = Math.Max(1, (int)MathF.Ceiling(stack / heightCap));
                var first = built.Count;

                for (var c = 0; c < wraps; c++)
                {
                    built.Add([]);
                    rankOfColumn.Add(r);
                }

                var cursor = wraps - 1;
                var n = 0;

                while (n < members.Count)
                {
                    var consumer = ConsumerOf(members[n]);
                    var end = n + 1;

                    while (end < members.Count && end - n < wraps && consumer >= 0 && ConsumerOf(members[end]) == consumer)
                    {
                        end++;
                    }

                    var siblings = members.GetRange(n, end - n);

                    if (siblings.Count > 1)
                    {
                        siblings.Sort((a, b) => DockOf(b, consumer).CompareTo(DockOf(a, consumer)));

                        if (cursor < siblings.Count - 1)
                        {
                            cursor = wraps - 1;
                        }
                    }

                    foreach (var node in siblings)
                    {
                        columnOf[node] = first + cursor;
                        built[first + cursor].Add(node);
                        cursor = cursor == 0 ? wraps - 1 : cursor - 1;
                    }

                    n = end;
                }
            }

            columnX = new float[built.Count];
            var x = 0f;

            for (var c = 0; c < built.Count; c++)
            {
                columnX[c] = x;
                var widest = built[c].Count == 0 ? 0f : built[c].Max(i => width[i]);
                var sameRank = c + 1 < built.Count && rankOfColumn[c + 1] == rankOfColumn[c];
                x += widest + (sameRank ? SubColumnSeparation : LayerSpacing);
            }

            columns = built;
        }

        /// <summary>Height of the socket a node's wire docks at on <paramref name="consumer"/>, relative to the consumer's centre.</summary>
        private float DockOf(int node, int consumer)
        {
            foreach (var link in down[node])
            {
                if (link.Other == consumer)
                {
                    return link.Far;
                }
            }

            return 0f;
        }

        /// <summary>The node a node's outgoing wires pull hardest towards, or -1 when it feeds nothing.</summary>
        private int ConsumerOf(int node)
        {
            var best = -1;
            var bestWeight = 0f;

            foreach (var link in down[node])
            {
                var weight = 0f;

                foreach (var other in down[node])
                {
                    if (other.Other == link.Other)
                    {
                        weight += other.Weight;
                    }
                }

                if (weight > bestWeight)
                {
                    best = link.Other;
                    bestWeight = weight;
                }
            }

            return best;
        }

        /// <summary>Sub-pixel steps per pixel in the integer heights the network simplex works in.</summary>
        private const int HeightScale = 2;

        /// <summary>How much harder a wire pulls with none, one or two long-wire dummies at its ends.</summary>
        private static readonly int[] DummyPull = [1, 2, 8];

        /// <summary>Height difference, in pixels, at which a wire's pull halves when the placement is solved again.</summary>
        private const float ReweightFalloff = 16f;

        /// <summary>
        /// Vertical placement that minimises the weighted sum of how far every wire is from level,
        /// keeping each column's order and gaps.
        /// </summary>
        /// <remarks>
        /// Solved exactly as a network simplex over an auxiliary graph: one extra node per wire sits
        /// at or below both of its socket heights, so the cost of its two edges is the wire's height
        /// difference. The solve is then repeated once with each wire's pull scaled down by how far
        /// from level it ended up.
        /// </remarks>
        /// <param name="stacks">Groups of nodes that share an x range, each in top-to-bottom order.</param>
        /// <param name="stackOf">Index into <paramref name="stacks"/> of each node.</param>
        /// <param name="limit">When to stop improving and keep the heights reached so far.</param>
        private void SolveHeights(List<List<int>> stacks, int[] stackOf, LayoutDeadline limit)
        {
            var links = 0;

            for (var i = 0; i < count; i++)
            {
                links += down[i].Count;
            }

            var simplex = new NetworkSimplex(count + links);
            var tops = new int[count + links];
            var tree = new List<int>(count + links - 1);

            foreach (var stack in stacks)
            {
                for (var j = 1; j < stack.Count; j++)
                {
                    var upper = stack[j - 1];
                    var lower = stack[j];
                    var separation = Scaled(height[upper] + GapBetween(upper, lower));
                    tops[lower] = tops[upper] + separation;
                    tree.Add(simplex.AddEdge(upper, lower, separation, 0));
                }
            }

            var wires = new List<(int From, int To, int FromAnchor, int ToAnchor, int Weight)>(links);

            for (var i = 0; i < count; i++)
            {
                foreach (var link in down[i])
                {
                    var dummies = (i < realCount ? 0 : 1) + (link.Other < realCount ? 0 : 1);
                    var weight = Math.Max(1, (int)MathF.Round(link.Weight * 4f * DummyPull[dummies]));
                    wires.Add((i, link.Other, AnchorFromTop(i, link.Near), AnchorFromTop(link.Other, link.Far), weight));
                }
            }

            // One wire per pair of joined stacks, heaviest first, is drawn level by shifting whole
            // stacks, and with the stacks' gap edges those wires make the tight spanning tree the
            // simplex starts from.
            var joined = new DisjointSets(stacks.Count);
            var spanning = new List<(int Other, int Wire)>[stacks.Count];
            var levelled = new bool[wires.Count];

            for (var c = 0; c < stacks.Count; c++)
            {
                spanning[c] = [];
            }

            foreach (var k in Enumerable.Range(0, wires.Count).OrderByDescending(k => wires[k].Weight))
            {
                var a = stackOf[wires[k].From];
                var b = stackOf[wires[k].To];

                if (joined.Union(a, b))
                {
                    spanning[a].Add((b, k));
                    spanning[b].Add((a, k));
                    levelled[k] = true;
                }
            }

            var shift = new int[stacks.Count];
            var reached = new bool[stacks.Count];
            var pending = new Queue<int>();
            reached[stackOf[0]] = true;
            pending.Enqueue(stackOf[0]);

            while (pending.Count > 0)
            {
                var c = pending.Dequeue();

                foreach (var (other, k) in spanning[c])
                {
                    if (reached[other])
                    {
                        continue;
                    }

                    var (from, to, fromAnchor, toAnchor, _) = wires[k];
                    shift[other] = stackOf[from] == c
                        ? shift[c] + tops[from] + fromAnchor - tops[to] - toAnchor
                        : shift[c] + tops[to] + toAnchor - tops[from] - fromAnchor;
                    reached[other] = true;
                    pending.Enqueue(other);
                }
            }

            for (var i = 0; i < count; i++)
            {
                tops[i] += shift[stackOf[i]];
            }

            var auxEdges = new int[wires.Count];

            for (var k = 0; k < wires.Count; k++)
            {
                var (from, to, fromAnchor, toAnchor, weight) = wires[k];
                var aux = count + k;
                var fromEdge = simplex.AddEdge(aux, from, -fromAnchor, weight);
                var toEdge = simplex.AddEdge(aux, to, -toAnchor, weight);
                var fromHeight = tops[from] + fromAnchor;
                var toHeight = tops[to] + toAnchor;
                tops[aux] = Math.Min(fromHeight, toHeight);
                auxEdges[k] = fromEdge;

                if (levelled[k])
                {
                    tree.Add(fromEdge);
                    tree.Add(toEdge);
                }
                else
                {
                    tree.Add(fromHeight <= toHeight ? fromEdge : toEdge);
                }
            }

            if (simplex.Solve(tops, tree, limit))
            {
                for (var k = 0; k < wires.Count; k++)
                {
                    var (from, to, fromAnchor, toAnchor, weight) = wires[k];
                    var offset = Math.Abs(tops[from] + fromAnchor - tops[to] - toAnchor) / (float)HeightScale;
                    var scaled = Math.Max(1, (int)MathF.Round(weight * 16f * ReweightFalloff / (ReweightFalloff + offset)));
                    simplex.SetWeight(auxEdges[k], scaled);
                    simplex.SetWeight(auxEdges[k] + 1, scaled);
                }

                simplex.Resolve(limit);
            }

            for (var i = 0; i < count; i++)
            {
                centerY[i] = tops[i] / (float)HeightScale + height[i] / 2f;
            }
        }

        private static int Scaled(float value) => (int)MathF.Ceiling((value * HeightScale) - 0.01f);

        /// <summary>A socket height relative to its node's centre, as a scaled offset from the node's top.</summary>
        private int AnchorFromTop(int node, float anchor) => (int)MathF.Round((anchor + height[node] / 2f) * HeightScale);

        private float GapBetween(int upper, int lower)
            => rankOf[upper] == rankOf[lower] && width[upper] == 0f && width[lower] == 0f
                ? DummyLaneHeight
                : NodeSpacing;

        /// <summary>
        /// Where the snap draws each card after the repair has moved cards: the placement solved
        /// once more on the order the repair left each column in, with the long-wire dummies free.
        /// </summary>
        private float[] SnapTargets()
        {
            var stacks = new List<List<int>>(columns.Count + count - realCount);
            var stackOf = new int[count];

            foreach (var column in columns)
            {
                var cards = column.Where(i => i < realCount).OrderBy(i => positions[i].Y).ToList();

                foreach (var i in cards)
                {
                    stackOf[i] = stacks.Count;
                }

                stacks.Add(cards);
            }

            for (var i = realCount; i < count; i++)
            {
                stackOf[i] = stacks.Count;
                stacks.Add([i]);
            }

            var centers = centerY[..];
            SolveHeights(stacks, stackOf, deadline);

            var targets = new float[realCount];

            for (var i = 0; i < realCount; i++)
            {
                targets[i] = centerY[i] - height[i] / 2f;
            }

            centerY = centers;
            return targets;
        }

        /// <summary>
        /// Solves the heights of the long-wire dummies with every card held where it landed. Each
        /// column's dummies are ordered by where the straight line between their wire's sockets
        /// crosses it, dealt over the gaps between its cards no more into a gap than it holds, and
        /// then placed exactly for the levelest chains within their gaps.
        /// </summary>
        /// <remarks>
        /// Simplex node 0 is the origin card heights are measured from, the dummies follow it in
        /// order, and then one auxiliary node per wire.
        /// </remarks>
        private void PlaceDummies()
        {
            if (count == realCount)
            {
                return;
            }

            var graphTop = float.MaxValue;

            for (var i = 0; i < realCount; i++)
            {
                centerY[i] = positions[i].Y + height[i] / 2f;
                graphTop = Math.Min(graphTop, positions[i].Y);
            }

            LayDummiesStraight();

            var wires = 0;

            for (var d = realCount; d < count; d++)
            {
                wires += up[d].Count + down[d].Count(link => link.Other < realCount);
            }

            var spacing = Scaled(DummyLaneHeight);
            var simplex = new NetworkSimplex(count - realCount + 1 + wires);
            var tops = new int[count - realCount + 1 + wires];
            var nextAux = count - realCount + 1;
            var tree = new List<int>();

            foreach (var column in columns)
            {
                var lane = column.Where(i => i >= realCount).OrderBy(i => centerY[i]).ToList();

                if (lane.Count == 0)
                {
                    continue;
                }

                var cards = column.Where(i => i < realCount).OrderBy(i => positions[i].Y).ToList();
                var gaps = new (int Low, int High)[cards.Count + 1];

                for (var g = 0; g <= cards.Count; g++)
                {
                    gaps[g] = (
                        g > 0 ? Scaled(positions[cards[g - 1]].Y + height[cards[g - 1]] + DummyLaneHeight) : int.MinValue,
                        g < cards.Count ? (int)MathF.Floor((positions[cards[g]].Y - DummyLaneHeight) * HeightScale) : int.MaxValue);
                }

                if (cards.Count == 0)
                {
                    gaps[0].Low = Scaled(graphTop) - (lane.Count * spacing);
                }

                var gapOf = DealIntoGaps(lane, gaps, spacing);

                for (int first = 0, last; first < lane.Count; first = last + 1)
                {
                    for (last = first; last + 1 < lane.Count && gapOf[last + 1] == gapOf[first]; last++)
                    {
                        tree.Add(simplex.AddEdge(Node(lane[last]), Node(lane[last + 1]), spacing, 0));
                    }

                    var (low, high) = gaps[gapOf[first]];
                    var span = (last - first) * spacing;
                    var wanted = (long)MathF.Round(centerY[lane[first]] * HeightScale);
                    var fromLow = high == int.MaxValue || (low != int.MinValue && wanted - low <= high - span - wanted);

                    if (low != int.MinValue)
                    {
                        var lowEdge = simplex.AddEdge(0, Node(lane[first]), low, 0);

                        if (fromLow)
                        {
                            tree.Add(lowEdge);
                        }
                    }

                    if (high != int.MaxValue)
                    {
                        var highEdge = simplex.AddEdge(Node(lane[last]), 0, -high, 0);

                        if (!fromLow)
                        {
                            tree.Add(highEdge);
                        }
                    }

                    for (var k = first; k <= last; k++)
                    {
                        tops[Node(lane[k])] = (fromLow ? low : high - span) + ((k - first) * spacing);
                    }
                }
            }

            for (var d = realCount; d < count; d++)
            {
                foreach (var link in up[d])
                {
                    AddWire(d, link);
                }

                foreach (var link in down[d])
                {
                    if (link.Other < realCount)
                    {
                        AddWire(d, link);
                    }
                }
            }

            simplex.Solve(tops, tree, routingDeadline);

            for (var d = realCount; d < count; d++)
            {
                centerY[d] = (tops[Node(d)] - tops[0]) / (float)HeightScale;
            }

            void AddWire(int dummy, Link link)
            {
                var betweenDummies = link.Other >= realCount;
                var weight = Math.Max(1, (int)MathF.Round(link.Weight * 4f));
                var node = Node(dummy);
                var other = betweenDummies ? Node(link.Other) : 0;
                var otherHeight = betweenDummies ? 0 : (int)MathF.Round((centerY[link.Other] + link.Far) * HeightScale);
                var otherTop = tops[other] + otherHeight;
                var aux = nextAux++;
                var nodeEdge = simplex.AddEdge(aux, node, 0, weight);
                var otherEdge = simplex.AddEdge(aux, other, -otherHeight, weight);

                tree.Add(tops[node] <= otherTop ? nodeEdge : otherEdge);
                tops[aux] = Math.Min(tops[node], otherTop);
            }

            int Node(int dummy) => dummy - realCount + 1;
        }

        /// <summary>Puts every dummy where the straight line between its wire's two sockets crosses its column.</summary>
        private void LayDummiesStraight()
        {
            for (var d = realCount; d < count; d++)
            {
                var source = up[d][0];

                if (source.Other >= realCount)
                {
                    continue;
                }

                var chain = new List<int> { d };

                while (down[chain[^1]][0].Other >= realCount)
                {
                    chain.Add(down[chain[^1]][0].Other);
                }

                var target = down[chain[^1]][0];
                var start = new Vector2(columnX[columnOf[source.Other]] + width[source.Other], centerY[source.Other] + source.Far);
                var end = new Vector2(columnX[columnOf[target.Other]], centerY[target.Other] + target.Far);

                foreach (var node in chain)
                {
                    centerY[node] = MathUtils.RemapRange(columnX[columnOf[node]], start.X, end.X, start.Y, end.Y);
                }
            }
        }

        /// <summary>
        /// Deals a column's dummies, in order, over the gaps between its cards so that each gap holds
        /// no more than fit at lane spacing and the dummies move as little as possible.
        /// </summary>
        /// <returns>The gap each dummy goes into.</returns>
        private int[] DealIntoGaps(List<int> lane, (int Low, int High)[] gaps, int spacing)
        {
            var n = lane.Count;
            var cost = new long[gaps.Length + 1, n + 1];
            var start = new int[gaps.Length + 1, n + 1];

            for (var j = 1; j <= n; j++)
            {
                cost[0, j] = long.MaxValue;
            }

            for (var g = 0; g < gaps.Length; g++)
            {
                var (low, high) = gaps[g];
                var capacity = high < low ? 0 : Math.Min(n, (((long)high - low) / spacing) + 1);

                for (var j = 0; j <= n; j++)
                {
                    cost[g + 1, j] = long.MaxValue;
                    var moved = 0L;

                    for (var i = j; i >= 0 && j - i <= capacity; i--)
                    {
                        if (i < j)
                        {
                            var wanted = (long)MathF.Round(centerY[lane[i]] * HeightScale);
                            moved += Math.Max(0, low - wanted) + Math.Max(0, wanted - high);
                        }

                        if (cost[g, i] != long.MaxValue && cost[g, i] + moved < cost[g + 1, j])
                        {
                            cost[g + 1, j] = cost[g, i] + moved;
                            start[g + 1, j] = i;
                        }
                    }
                }
            }

            var gapOf = new int[n];

            for (int g = gaps.Length, j = n; g > 0; g--)
            {
                var i = start[g, j];
                Array.Fill(gapOf, g - 1, i, j - i);
                j = i;
            }

            return gapOf;
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
                routes[wire] = [.. chain.Select(d => new Vector2(columnX[columnOf[d]], centerY[d]))];
            }
        }
    }
}
