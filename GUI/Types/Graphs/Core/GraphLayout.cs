using System.Linq;

namespace GUI.Types.Graphs.Core;

/// <summary>Node placement engine used by <see cref="GraphView.LayoutNodesPacked"/>.</summary>
enum GraphPlacement
{
    /// <summary>Layered Sugiyama flow, left to right.</summary>
    Layered,

    /// <summary>MDS stress majorization.</summary>
    Organic,
}

/// <summary>
/// Native graph layout: MDS stress majorization or layered Sugiyama placement per island and
/// 16:9 shelf packing of the island rectangles. Wires draw as plain curves between their
/// socket pivots; self-referencing wires get a synthetic orbit route.
/// </summary>
internal static class GraphLayout
{


    private const float PackingAspect = 16f / 9f;

    /// <summary>Gap between the wrapped sub-columns of one oversized rank.</summary>
    private const float SubColumnSeparation = 64f;

    public static void Layout(
        List<GraphNode> component,
        List<GraphWire> componentWires,
        GraphPlacement placement,
        GraphGeometry geometry,
        GraphLayoutOptions options)
    {
        if (component.Count > 1)
        {
            if (placement == GraphPlacement.Organic)
            {
                LayoutOrganic(component, componentWires, geometry, options);
            }
            else
            {
                new LayeredSolver(component, componentWires, geometry, options).Run();
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

    /// <summary>Rebuilds the synthetic loop route of a self-referencing wire from its current pivots.</summary>
    public static void SynthesizeSelfLoop(GraphWire wire, GraphGeometry geometry)
    {
        // Out the right side, over the top of the node, back into the left side.
        var owner = wire.From.Owner;
        var from = geometry.PivotOf(wire.From);
        var to = geometry.PivotOf(wire.To);
        var top = owner.Position.Y - 26f;

        var route = geometry.RouteOf(wire);
        route.CurvePath = null;
        route.Waypoints =
        [
            new Vector2(from.X + 36f, from.Y),
            new Vector2(from.X + 36f, top),
            new Vector2(to.X - 36f, top),
            new Vector2(to.X - 36f, to.Y),
        ];
    }

    /// <summary>
    /// Swaps vertically adjacent nodes in a column whenever that removes a crossing, measured on
    /// the straight run between the real socket pivots.
    /// </summary>
    /// <remarks>
    /// Every other ordering pass reasons about nodes: it sees that two producers both feed one
    /// consumer and has no reason to prefer either order. The crossing is created further down,
    /// by which socket row each wire lands on, and is only visible once the cards have real
    /// coordinates. So this runs last, on geometry, and is the only pass that can fix the case
    /// where two wires into one node cross because its input rows are in the opposite order to
    /// their sources.
    /// </remarks>
    public static void RepairCrossings(List<GraphNode> component, List<GraphWire> componentWires, GraphGeometry geometry, GraphLayoutOptions options)
    {
        if (options.Has(GraphLayoutFeature.CrossingSwap) && component.Count > 1)
        {
            new CrossingRepair(component, componentWires, geometry, options).Run();
        }
    }

    internal static bool OverlapsColumn(GraphNode node, List<GraphNode> column, GraphGeometry geometry)
    {
        var top = node.Position.Y;
        var bottom = top + geometry.SizeOf(node).Y;

        foreach (var other in column)
        {
            if (other == node)
            {
                continue;
            }

            var otherTop = other.Position.Y;

            if (top < otherTop + geometry.SizeOf(other).Y && otherTop < bottom)
            {
                return true;
            }
        }

        return false;
    }

    // Stress majorization over graph-theoretic hop distances, then pairwise overlap removal.
    private static void LayoutOrganic(List<GraphNode> component, List<GraphWire> componentWires, GraphGeometry geometry, GraphLayoutOptions options)
    {
        var n = component.Count;
        var index = new Dictionary<GraphNode, int>(n);
        var halfSizes = new Vector2[n];
        var idealEdge = 0f;

        for (var i = 0; i < n; i++)
        {
            index[component[i]] = i;
            halfSizes[i] = geometry.SizeOf(component[i]) / 2f;
            idealEdge += halfSizes[i].Length() * 2f;
        }

        // Ideal edge length scales with the card diagonals so directly connected cards
        // come out separated instead of stacked; overlap removal only handles the rest.
        idealEdge = idealEdge / n + 100f;

        var neighbors = new List<int>[n];

        for (var i = 0; i < n; i++)
        {
            neighbors[i] = [];
        }

        foreach (var wire in componentWires)
        {
            if (wire.From.Owner == wire.To.Owner)
            {
                continue;
            }

            var a = index[wire.From.Owner];
            var b = index[wire.To.Owner];
            neighbors[a].Add(b);
            neighbors[b].Add(a);
        }

        var hops = new int[n][];
        var queue = new Queue<int>();

        for (var start = 0; start < n; start++)
        {
            var distances = new int[n];
            Array.Fill(distances, -1);
            distances[start] = 0;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                foreach (var next in neighbors[current])
                {
                    if (distances[next] < 0)
                    {
                        distances[next] = distances[current] + 1;
                        queue.Enqueue(next);
                    }
                }
            }

            hops[start] = distances;
        }

        // Deterministic golden-angle spiral start; majorization needs no randomness.
        var positions = new Vector2[n];

        for (var i = 0; i < n; i++)
        {
            var angle = i * 2.3999632f;
            positions[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (idealEdge * 0.5f * MathF.Sqrt(i));
        }

        for (var iteration = 0; iteration < 48; iteration++)
        {
            for (var i = 0; i < n; i++)
            {
                var weightSum = 0f;
                var target = Vector2.Zero;

                for (var j = 0; j < n; j++)
                {
                    var hop = hops[i][j];

                    if (j == i || hop <= 0)
                    {
                        continue;
                    }

                    var idealDistance = hop * idealEdge;
                    var weight = 1f / (idealDistance * idealDistance);
                    var delta = positions[i] - positions[j];
                    var length = delta.Length();
                    var direction = length > 0.01f ? delta / length : DirectionFor(i + j);

                    target += weight * (positions[j] + direction * idealDistance);
                    weightSum += weight;
                }

                if (weightSum > 0f)
                {
                    positions[i] = target / weightSum;
                }
            }
        }

        var clean = false;

        for (var pass = 0; pass < 150 && !clean; pass++)
        {
            clean = true;

            for (var i = 0; i < n; i++)
            {
                for (var j = i + 1; j < n; j++)
                {
                    var needX = halfSizes[i].X + halfSizes[j].X + options.NodeSpacing;
                    var needY = halfSizes[i].Y + halfSizes[j].Y + options.NodeSpacing;
                    var delta = positions[j] - positions[i];
                    var overlapX = needX - Math.Abs(delta.X);
                    var overlapY = needY - Math.Abs(delta.Y);

                    if (overlapX <= 0f || overlapY <= 0f)
                    {
                        continue;
                    }

                    clean = false;

                    if (overlapX < overlapY)
                    {
                        var push = overlapX / 2f * (delta.X >= 0f ? 1f : -1f);
                        positions[i].X -= push;
                        positions[j].X += push;
                    }
                    else
                    {
                        var push = overlapY / 2f * (delta.Y != 0f ? Math.Sign(delta.Y) : DirectionFor(i + j).Y >= 0f ? 1f : -1f);
                        positions[i].Y -= push;
                        positions[j].Y += push;
                    }
                }
            }
        }

        // Pairwise pushes can fail to converge on dense clusters; marching each remaining
        // offender outward from the centroid until it finds free space is guaranteed to
        // finish, so no card can end up under another no matter the graph.
        if (!clean)
        {
            var centroid = Vector2.Zero;

            for (var i = 0; i < n; i++)
            {
                centroid += positions[i];
            }

            centroid /= n;

            for (var i = 0; i < n; i++)
            {
                var direction = positions[i] - centroid;
                var length = direction.Length();
                direction = length > 1f ? direction / length : DirectionFor(i);

                var guard = 0;

                while (OverlapsAny(i) && guard++ < 4000)
                {
                    positions[i] += direction * options.NodeSpacing;
                }
            }

            bool OverlapsAny(int i)
            {
                for (var j = 0; j < n; j++)
                {
                    if (j == i)
                    {
                        continue;
                    }

                    var delta = positions[j] - positions[i];

                    if (Math.Abs(delta.X) < halfSizes[i].X + halfSizes[j].X + options.NodeSpacing &&
                        Math.Abs(delta.Y) < halfSizes[i].Y + halfSizes[j].Y + options.NodeSpacing)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        for (var i = 0; i < n; i++)
        {
            component[i].Position = positions[i] - halfSizes[i];
        }

        if (options.Has(GraphLayoutFeature.PortAwareAlignment))
        {
            SnapOrganicPivots(component, componentWires, geometry, index, halfSizes, options);
        }

        RepairCrossings(component, componentWires, geometry, options);
    }

    // Majorization places node centers; the wires dock at socket pivots. Nudging each card so
    // its busiest wire runs level costs nothing in stress terms and removes the small diagonal
    // every wire otherwise picks up.
    private static void SnapOrganicPivots(
        List<GraphNode> component,
        List<GraphWire> componentWires,
        GraphGeometry geometry,
        Dictionary<GraphNode, int> index,
        Vector2[] halfSizes,
        GraphLayoutOptions options)
    {
        var shifts = new List<(float Offset, float Weight)>[component.Count];

        for (var i = 0; i < component.Count; i++)
        {
            shifts[i] = [];
        }

        foreach (var wire in componentWires)
        {
            if (wire.From.Owner == wire.To.Owner)
            {
                continue;
            }

            var weight = wire.Dashed ? options.DashedWireWeight : options.SolidWireWeight;
            var delta = geometry.PivotOf(wire.From).Y - geometry.PivotOf(wire.To).Y;

            shifts[index[wire.To.Owner]].Add((delta, weight));
            shifts[index[wire.From.Owner]].Add((-delta, weight));
        }

        for (var i = 0; i < component.Count; i++)
        {
            if (shifts[i].Count == 0)
            {
                continue;
            }

            var shift = WeightedMedian(shifts[i]);

            // Only take the nudge when it cannot reintroduce an overlap.
            if (Math.Abs(shift) > halfSizes[i].Y + options.NodeSpacing)
            {
                continue;
            }

            component[i].Position += new Vector2(0f, shift);
        }
    }

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

    private static Vector2 DirectionFor(int seed)
    {
        var angle = seed * 2.3999632f;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    /// <summary>
    /// Packs island rectangles into shelves aiming for a 16:9 overall shape. Returns the
    /// top-left origin for each rectangle in input order.
    /// </summary>
    public static Vector2[] PackComponents(Vector2[] sizes, float padding)
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
        var bestScore = float.MaxValue;

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
            var score = Math.Max(aspect / PackingAspect, PackingAspect / aspect);

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
        List<GraphNode> component,
        List<GraphWire> componentWires,
        GraphGeometry geometry,
        GraphLayoutOptions options)
    {
        /// <summary>
        /// One entry of a node's adjacency. <paramref name="Far"/> and <paramref name="Near"/> are
        /// the two socket heights relative to their own node centers, so the height this node wants
        /// in order to draw the wire level is <c>centerY[Other] + Far - Near</c>.
        /// </summary>
        private readonly record struct Link(int Other, float Far, float Near, float Weight);

        private readonly int realCount = component.Count;
        private readonly Dictionary<GraphNode, int> index = BuildIndex(component);

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
        private readonly Dictionary<GraphWire, List<int>> chains = [];
        private readonly HashSet<GraphWire> reversed = [];

        private static Dictionary<GraphNode, int> BuildIndex(List<GraphNode> component)
        {
            var map = new Dictionary<GraphNode, int>(component.Count);

            for (var i = 0; i < component.Count; i++)
            {
                map[component[i]] = i;
            }

            return map;
        }

        public void Run()
        {
            var crossWires = componentWires.Where(static w => w.From.Owner != w.To.Owner).ToList();
            FindBackWires(crossWires);
            AssignRanks(crossWires);
            BuildLayers(crossWires);
            OrderLayers();
            AssignColumns();
            AssignHeights();
            ApplyPositions();

            // Runs on the placed cards, before the routes are built from them.
            RepairCrossings(component, componentWires, geometry, options);

            EmitRoutes();
        }

        // DFS back-edge detection; the edges it finds are reversed for ranking so the rest of
        // the pipeline sees an acyclic graph.
        private void FindBackWires(List<GraphWire> crossWires)
        {
            var outgoing = new List<GraphWire>[realCount];

            for (var i = 0; i < realCount; i++)
            {
                outgoing[i] = [];
            }

            foreach (var wire in crossWires)
            {
                outgoing[index[wire.From.Owner]].Add(wire);
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

                    var target = index[outgoing[nodeIdx][edgeIdx].To.Owner];

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

        private (int From, int To) Effective(GraphWire wire)
            => reversed.Contains(wire)
                ? (index[wire.To.Owner], index[wire.From.Owner])
                : (index[wire.From.Owner], index[wire.To.Owner]);

        // Longest-path ranks over the acyclic orientation, then a tightening pass that pulls
        // every node as far right as its successors allow so wires span as few ranks as possible.
        private void AssignRanks(List<GraphWire> crossWires)
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

            if (!options.Has(GraphLayoutFeature.LongWireDummies))
            {
                return;
            }

            // Longest-path ranking maximises span, and every extra rank a wire crosses is a
            // dummy and a chance to cross something. Walking the order backwards and pulling
            // each node up against its nearest successor removes most of that slack.
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

                if (tightest != int.MaxValue && tightest - 1 > rankOf[node])
                {
                    rankOf[node] = tightest - 1;
                }
            }
        }

        // Builds the per-rank membership lists plus the up/down adjacency the ordering and
        // alignment passes run on, inserting a dummy per intermediate rank for long wires.
        private void BuildLayers(List<GraphWire> crossWires)
        {
            var rankCount = realCount == 0 ? 1 : rankOf.Max() + 1;
            var wantDummies = options.Has(GraphLayoutFeature.LongWireDummies);
            var portAware = options.Has(GraphLayoutFeature.PortAwareAlignment);

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
                var size = geometry.SizeOf(component[i]);
                width[i] = size.X;
                height[i] = size.Y;
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
                var weight = wire.Dashed ? options.DashedWireWeight : options.SolidWireWeight;

                // A reversed wire still docks at its real sockets, so the anchors follow the
                // wire's own direction rather than the ranking direction.
                var sourceAnchor = portAware ? AnchorOf(wire.From) : 0f;
                var targetAnchor = portAware ? AnchorOf(wire.To) : 0f;
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
        private float AnchorOf(GraphSocket socket)
            => geometry.PivotOffsetOf(socket).Y - geometry.SizeOf(socket.Owner).Y / 2f;

        // Layer-by-layer barycentre ordering. The repaired version normalises the keys so ranks
        // of different sizes compare, alternates sweep direction, runs an adjacent-swap pass and
        // keeps whichever sweep actually measured fewest crossings.
        private void OrderLayers()
        {
            if (!options.Has(GraphLayoutFeature.BarycentreRepair))
            {
                OrderLayersLegacy();
                return;
            }

            var best = Snapshot();
            var bestCrossings = CountCrossings();

            for (var sweep = 0; sweep < 12; sweep++)
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

        // Inversions between consecutive ranks, counted with a Fenwick tree so hub-heavy ranks
        // stay affordable.
        private int CountCrossings()
        {
            var total = 0;

            for (var r = 0; r + 1 < ranks.Length; r++)
            {
                var edges = new List<(int Upper, int Lower)>();

                foreach (var node in ranks[r])
                {
                    foreach (var link in down[node])
                    {
                        if (rankOf[link.Other] == r + 1)
                        {
                            edges.Add((positionInRank[node], positionInRank[link.Other]));
                        }
                    }
                }

                if (edges.Count < 2)
                {
                    continue;
                }

                edges.Sort(static (a, b) => a.Upper != b.Upper ? a.Upper.CompareTo(b.Upper) : a.Lower.CompareTo(b.Lower));

                var size = ranks[r + 1].Count + 1;
                var tree = new int[size + 1];
                var seen = 0;

                foreach (var (_, lower) in edges)
                {
                    // Everything already inserted strictly to the right of this edge crosses it.
                    var greater = seen - Query(lower + 1);
                    total += greater;
                    Add(lower + 1);
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

        // The shipped ordering: one direction, unnormalised keys, no crossing measurement.
        private void OrderLayersLegacy()
        {
            var neighbors = new List<int>[count];

            for (var i = 0; i < count; i++)
            {
                neighbors[i] = [.. up[i].Select(static l => l.Other), .. down[i].Select(static l => l.Other)];
            }

            for (var sweep = 0; sweep < 12; sweep++)
            {
                for (var r = 0; r < ranks.Length; r++)
                {
                    var rank = ranks[r];

                    var keyed = rank
                        .Select(i => (Index: i, Key: neighbors[i].Count == 0 ? positionInRank[i] : (float)neighbors[i].Average(other => positionInRank[other])))
                        .OrderBy(static entry => entry.Key)
                        .ToList();

                    for (var order = 0; order < keyed.Count; order++)
                    {
                        positionInRank[keyed[order].Index] = order;
                    }

                    ranks[r] = [.. keyed.Select(static entry => entry.Index)];
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
            var columns = new List<List<int>>();
            var rankOfColumn = new List<int>();

            for (var r = 0; r < ranks.Length; r++)
            {
                // The rank order is the crossing-minimised one; wrapping must chunk it, never
                // re-sort it. Reordering a wrapped rank by flow affinity was tried and tripled
                // the crossings on a1_intro_world, because it discards that ordering wholesale.
                var members = ranks[r];

                var column = new List<int>();
                columns.Add(column);
                rankOfColumn.Add(r);
                var stack = 0f;

                foreach (var i in members)
                {
                    if (column.Count > 0 && stack + height[i] > heightCap)
                    {
                        column = [];
                        columns.Add(column);
                        rankOfColumn.Add(r);
                        stack = 0f;
                    }

                    columnOf[i] = columns.Count - 1;
                    column.Add(i);
                    stack += height[i] + options.NodeSpacing;
                }
            }

            // Horizontal: each column wide enough for its widest card. Sub-columns of one rank
            // sit closer together than real rank boundaries so wrapping costs less travel.
            columnX = new float[columns.Count];
            var x = 0f;

            for (var c = 0; c < columns.Count; c++)
            {
                columnX[c] = x;
                var widest = columns[c].Count == 0 ? 0f : columns[c].Max(i => width[i]);
                // Sub-columns of one wrapped rank sit closer than a real rank boundary, so wires
                // crossing the wrap travel less than a full layer to do it.
                var sameRank = c + 1 < columns.Count && rankOfColumn[c + 1] == rankOfColumn[c];
                x += widest + (sameRank ? SubColumnSeparation : options.LayerSpacing);
            }

            this.columns = columns;
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
                var size = geometry.SizeOf(component[i]);
                component[i].Position = new Vector2(columnX[columnOf[i]], centerY[i] - size.Y / 2f);
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

                var route = geometry.RouteOf(wire);
                route.CurvePath = null;
                route.Waypoints = [.. chain.Select(d => new Vector2(columnX[columnOf[d]], centerY[d]))];
            }
        }
    }
}
