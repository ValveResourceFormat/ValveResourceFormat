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
    private const float NodeSeparation = 36f;
    private const float LayerSeparation = 220f;
    private const float PackingAspect = 16f / 9f;

    public static void Layout(List<GraphNode> component, List<GraphWire> componentWires, GraphPlacement placement, GraphGeometry geometry)
    {
        if (component.Count > 1)
        {
            if (placement == GraphPlacement.Organic)
            {
                LayoutOrganic(component, componentWires, geometry);
            }
            else
            {
                LayoutLayered(component, componentWires, geometry);
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

    // Stress majorization over graph-theoretic hop distances, then pairwise overlap removal.
    private static void LayoutOrganic(List<GraphNode> component, List<GraphWire> componentWires, GraphGeometry geometry)
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
                    var needX = halfSizes[i].X + halfSizes[j].X + NodeSeparation;
                    var needY = halfSizes[i].Y + halfSizes[j].Y + NodeSeparation;
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
                    positions[i] += direction * NodeSeparation;
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

                    if (Math.Abs(delta.X) < halfSizes[i].X + halfSizes[j].X + NodeSeparation &&
                        Math.Abs(delta.Y) < halfSizes[i].Y + halfSizes[j].Y + NodeSeparation)
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
    }

    private static Vector2 DirectionFor(int seed)
    {
        var angle = seed * 2.3999632f;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    // Sugiyama-style layered placement: cycle break, longest-path ranks, barycenter crossing
    // reduction, then vertical alignment of each node on the median of its neighbors.
    private static void LayoutLayered(List<GraphNode> component, List<GraphWire> componentWires, GraphGeometry geometry)
    {
        var n = component.Count;
        var index = new Dictionary<GraphNode, int>(n);

        for (var i = 0; i < n; i++)
        {
            index[component[i]] = i;
        }

        var crossWires = componentWires.Where(static w => w.From.Owner != w.To.Owner).ToList();
        var reversed = FindBackWires(component, crossWires, index);

        (int From, int To) Effective(GraphWire wire)
            => reversed.Contains(wire)
                ? (index[wire.To.Owner], index[wire.From.Owner])
                : (index[wire.From.Owner], index[wire.To.Owner]);

        // Longest-path ranks over the acyclic orientation.
        var rankOf = new int[n];
        var incomingCount = new int[n];
        var outgoing = new List<int>[n];
        var neighbors = new List<int>[n];

        for (var i = 0; i < n; i++)
        {
            outgoing[i] = [];
            neighbors[i] = [];
        }

        foreach (var wire in crossWires)
        {
            var (from, to) = Effective(wire);
            outgoing[from].Add(to);
            incomingCount[to]++;
            neighbors[from].Add(to);
            neighbors[to].Add(from);
        }

        var queue = new Queue<int>();

        for (var i = 0; i < n; i++)
        {
            if (incomingCount[i] == 0)
            {
                queue.Enqueue(i);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var target in outgoing[current])
            {
                rankOf[target] = Math.Max(rankOf[target], rankOf[current] + 1);

                if (--incomingCount[target] == 0)
                {
                    queue.Enqueue(target);
                }
            }
        }

        var rankCount = rankOf.Max() + 1;
        var ranks = new List<int>[rankCount];

        for (var r = 0; r < rankCount; r++)
        {
            ranks[r] = [];
        }

        var orderOf = new float[n];

        for (var i = 0; i < n; i++)
        {
            orderOf[i] = ranks[rankOf[i]].Count;
            ranks[rankOf[i]].Add(i);
        }

        // Barycenter sweeps: order every rank by the average order of its neighbors.
        for (var sweep = 0; sweep < 12; sweep++)
        {
            for (var r = 0; r < rankCount; r++)
            {
                var rank = ranks[r];

                var keyed = rank
                    .Select(i => (Index: i, Key: neighbors[i].Count == 0 ? orderOf[i] : (float)neighbors[i].Average(other => orderOf[other])))
                    .OrderBy(static entry => entry.Key)
                    .ToList();

                for (var order = 0; order < keyed.Count; order++)
                {
                    orderOf[keyed[order].Index] = order;
                }

                ranks[r] = keyed.Select(static entry => entry.Index).ToList();
            }
        }

        // Oversized ranks wrap into several height-capped columns so hub-heavy graphs come
        // out block-shaped instead of as one enormous strip.
        var heights = new float[n];
        var area = 0f;

        for (var i = 0; i < n; i++)
        {
            var size = geometry.SizeOf(component[i]);
            heights[i] = size.Y;
            area += (size.Y + NodeSeparation) * (size.X + LayerSeparation);
        }

        var heightCap = Math.Max(1200f, MathF.Sqrt(area / PackingAspect));
        var columns = new List<List<int>>();
        var columnOf = new int[n];

        foreach (var rank in ranks)
        {
            var column = new List<int>();
            columns.Add(column);
            var height = 0f;

            foreach (var i in rank)
            {
                if (column.Count > 0 && height + heights[i] > heightCap)
                {
                    column = [];
                    columns.Add(column);
                    height = 0f;
                }

                columnOf[i] = columns.Count - 1;
                column.Add(i);
                height += heights[i] + NodeSeparation;
            }
        }

        // Horizontal: each column wide enough for its widest card.
        var columnX = new float[columns.Count];
        var x = 0f;

        for (var c = 0; c < columns.Count; c++)
        {
            columnX[c] = x;
            x += columns[c].Max(i => geometry.SizeOf(component[i]).X) + LayerSeparation;
        }

        // Vertical: stack, then align every node on the median of its neighbor centers and
        // re-impose the order with minimum gaps.
        var centerY = new float[n];

        foreach (var column in columns)
        {
            var y = 0f;

            foreach (var i in column)
            {
                centerY[i] = y + heights[i] / 2f;
                y += heights[i] + NodeSeparation;
            }
        }

        var desired = new List<float>();

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
                    if (neighbors[i].Count == 0)
                    {
                        continue;
                    }

                    desired.Clear();

                    foreach (var other in neighbors[i])
                    {
                        desired.Add(centerY[other]);
                    }

                    desired.Sort();
                    centerY[i] = desired[desired.Count / 2];
                }

                for (var pass = 0; pass < 2; pass++)
                {
                    for (var j = 1; j < column.Count; j++)
                    {
                        var upper = column[j - 1];
                        var lower = column[j];
                        var overlap = centerY[upper] + heights[upper] / 2f + NodeSeparation - (centerY[lower] - heights[lower] / 2f);

                        if (overlap > 0f)
                        {
                            centerY[upper] -= overlap / 2f;
                            centerY[lower] += overlap / 2f;
                        }
                    }
                }

                EnforceColumnGaps(column, centerY, heights);

                // Overlap resolution biases downward; re-centering each column on its
                // neighbors stops the drift from compounding into a staircase.
                foreach (var i in column)
                {
                    if (neighbors[i].Count > 0)
                    {
                        drift += centerY[i] - neighbors[i].Average(other => centerY[other]);
                        anchored++;
                    }
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
        SquashOutliers(columns, centerY, heights);

        for (var i = 0; i < n; i++)
        {
            var size = geometry.SizeOf(component[i]);
            component[i].Position = new Vector2(columnX[columnOf[i]], centerY[i] - size.Y / 2f);
        }
    }

    // Top-down pass guaranteeing minimum vertical gaps within a column, preserving order.
    private static void EnforceColumnGaps(List<int> column, float[] centerY, float[] heights)
    {
        for (var j = 1; j < column.Count; j++)
        {
            var upper = column[j - 1];
            var lower = column[j];
            var overlap = centerY[upper] + heights[upper] / 2f + NodeSeparation - (centerY[lower] - heights[lower] / 2f);

            if (overlap > 0f)
            {
                centerY[lower] += overlap;
            }
        }
    }

    private static void SquashOutliers(List<List<int>> columns, float[] centerY, float[] heights)
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

            EnforceColumnGaps(column, centerY, heights);
        }
    }

    private static HashSet<GraphWire> FindBackWires(List<GraphNode> component, List<GraphWire> crossWires, Dictionary<GraphNode, int> index)
    {
        var outgoing = new List<GraphWire>[component.Count];

        for (var i = 0; i < component.Count; i++)
        {
            outgoing[i] = [];
        }

        foreach (var wire in crossWires)
        {
            outgoing[index[wire.From.Owner]].Add(wire);
        }

        var reversed = new HashSet<GraphWire>();
        var state = new byte[component.Count]; // 0 unvisited, 1 on stack, 2 done
        var stack = new Stack<(int NodeIdx, int EdgeIdx)>();

        for (var start = 0; start < component.Count; start++)
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

        return reversed;
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
}
