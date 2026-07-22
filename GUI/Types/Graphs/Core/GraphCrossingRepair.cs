using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GUI.Types.Graphs.Core;

/// <summary>
/// Moves placed cards to remove wire crossings, judged on the straight run between real socket
/// pivots.
/// </summary>
/// <remarks>
/// Every ordering pass before this one reasons about nodes, so when two producers feed one
/// consumer it has no reason to prefer either order. The crossing is decided one level lower, by
/// which socket row each wire lands on, and only becomes visible once the cards have coordinates.
/// Three moves are tried, cheapest first: exchange two cards in a column, lift a card out of its
/// column and reinsert it elsewhere, and slide a card to the height that levels one of its wires.
/// Endpoints are cached and refreshed per moved card, because the inner loop runs often enough
/// that a dictionary lookup per pivot dominates everything else.
/// </remarks>
internal sealed class CrossingRepair
{
    private const float ColumnQuantum = 8f;

    private readonly List<GraphNode> component;
    private readonly GraphGeometry geometry;
    private readonly GraphLayoutOptions options;

    private readonly GraphWire[] wires;
    private readonly Vector2[] from;
    private readonly Vector2[] to;
    private readonly float[] minX;
    private readonly float[] maxX;

    /// <summary>
    /// Vertical extent of each wire, kept alongside the horizontal one. Islands are packed in two
    /// dimensions, so most pairs of wires are separated in y rather than in x; rejecting on y as
    /// well as x is what actually discards the bulk of the pairs before the intersection test.
    /// </summary>
    private readonly float[] minY;
    private readonly float[] maxY;

    private readonly Dictionary<GraphNode, List<int>> incident = [];
    private readonly Dictionary<GraphNode, List<GraphNode>> columnOf = [];
    private readonly List<List<GraphNode>> columns = [];


    public CrossingRepair(List<GraphNode> component, List<GraphWire> componentWires, GraphGeometry geometry, GraphLayoutOptions options)
    {
        this.component = component;
        this.geometry = geometry;
        this.options = options;

        wires = [.. componentWires.Where(static w => w.From.Owner != w.To.Owner)];
        from = new Vector2[wires.Length];
        to = new Vector2[wires.Length];
        minX = new float[wires.Length];
        maxX = new float[wires.Length];
        minY = new float[wires.Length];
        maxY = new float[wires.Length];

        for (var i = 0; i < wires.Length; i++)
        {
            Refresh(i);
            Track(wires[i].From.Owner, i);
            Track(wires[i].To.Owner, i);

        }

        unionMarks = new int[wires.Length];
        allWires = [.. Enumerable.Range(0, wires.Length)];

        var buckets = new Dictionary<int, List<GraphNode>>();

        foreach (var node in component)
        {
            var key = (int)MathF.Round(node.Position.X / ColumnQuantum);

            if (!buckets.TryGetValue(key, out var column))
            {
                buckets[key] = column = [];
                columns.Add(column);
            }

            column.Add(node);
            columnOf[node] = column;
        }

    }

    private void Track(GraphNode node, int wire)
    {
        if (!incident.TryGetValue(node, out var list))
        {
            incident[node] = list = [];
        }

        list.Add(wire);
    }

    private void Refresh(int wire)
    {
        from[wire] = geometry.PivotOf(wires[wire].From);
        to[wire] = geometry.PivotOf(wires[wire].To);
        minX[wire] = Math.Min(from[wire].X, to[wire].X);
        maxX[wire] = Math.Max(from[wire].X, to[wire].X);
        minY[wire] = Math.Min(from[wire].Y, to[wire].Y);
        maxY[wire] = Math.Max(from[wire].Y, to[wire].Y);
    }

    private void RefreshNode(GraphNode node)
    {
        if (incident.TryGetValue(node, out var list))
        {
            foreach (var wire in list)
            {
                Refresh(wire);
            }
        }
    }

    private System.Diagnostics.Stopwatch clock = new();

    /// <summary>
    /// Whether the repair has spent its time. Checked between moves rather than inside the
    /// scoring loops, so it always stops on a consistent layout, never half way through a swap.
    /// </summary>
    private bool Spent => options.CrossingRepairBudgetMs > 0 && clock.ElapsedMilliseconds >= options.CrossingRepairBudgetMs;

    public void Run()
    {
        if (wires.Length < 2)
        {
            return;
        }

        clock = options.RepairClock ?? System.Diagnostics.Stopwatch.StartNew();

        for (var pass = 0; pass < options.CrossingRepairPasses && !Spent; pass++)
        {
            var improved = false;

            foreach (var column in columns)
            {
                if (column.Count < 2)
                {
                    continue;
                }

                column.Sort(static (a, b) => a.Position.Y.CompareTo(b.Position.Y));

                for (var i = 0; i + 1 < column.Count; i++)
                {
                    if (TrySwap(column[i], column[i + 1]))
                    {
                        (column[i], column[i + 1]) = (column[i + 1], column[i]);
                        improved = true;
                    }
                }
            }

            // The two ends of a crossing are exactly the cards worth exchanging, and they are
            // often far apart in the column, where an adjacency sweep can never reach them.
            foreach (var (a, b) in Crossings(options.CrossingRepairBudget))
            {
                if (TrySwap(wires[a].From.Owner, wires[b].From.Owner) || TrySwap(wires[a].To.Owner, wires[b].To.Owner))
                {
                    improved = true;
                }
            }

            foreach (var column in columns)
            {
                if (Spent)
                {
                    break;
                }

                improved |= TryReinsert(column);
            }

            // Nothing above or below constrains a card that is alone in its column, so neither
            // move can reach it, yet it is the freest card in the layout.
            foreach (var node in component)
            {
                if (Spent)
                {
                    break;
                }

                improved |= TrySlide(node);
            }

            if (!improved)
            {
                return;
            }
        }
    }

    private bool TrySwap(GraphNode x, GraphNode y)
    {
        if (x == y
            || !columnOf.TryGetValue(x, out var column) || !columnOf.TryGetValue(y, out var other) || column != other
            || (!incident.ContainsKey(x) && !incident.ContainsKey(y)))
        {
            return false;
        }

        var subset = Union(x, y);

        // Deliberately scored against every wire rather than a filtered set. Narrowing costs a
        // pass over all of them, which only pays back when the same subset is then scored many
        // times over; a swap scores twice, so filtering here measured slower than not.
        var candidates = allWires;
        var before = Count(subset, candidates);
        Exchange(x, y);

        // Cards of different heights can land on a neighbour when they trade places, and the
        // layout guarantees no overlapping cards, so such a swap is refused outright.
        if (GraphLayout.OverlapsColumn(x, column, geometry) || GraphLayout.OverlapsColumn(y, column, geometry)
            || Count(subset, candidates) >= before)
        {
            Exchange(x, y);
            return false;
        }

        return true;
    }

    private bool TryReinsert(List<GraphNode> column)
    {
        // Reinsertion restacks a whole column at uniform gaps, which discards the pivot alignment
        // for every card in it. On a small graph that is a good trade for the crossings it buys;
        // on a large one it stretches far more wire than it saves, so it is left off there.
        if (column.Count is < 3 or > 40 || component.Count > options.CrossingReinsertMaxNodes)
        {
            return false;
        }

        column.Sort(static (a, b) => a.Position.Y.CompareTo(b.Position.Y));

        var subset = new List<int>();

        foreach (var node in column)
        {
            if (incident.TryGetValue(node, out var list))
            {
                subset.AddRange(list);
            }
        }

        if (subset.Count == 0)
        {
            return false;
        }

        // Reinsertion restacks the column, so a card can travel the column's whole height.
        var top = column[0].Position.Y;
        var last = column[^1];
        var candidates = LocalCandidates(subset, last.Position.Y + geometry.SizeOf(last).Y - top);

        var best = Count(subset, candidates);
        var bestOrder = new List<GraphNode>(column);
        var order = new List<GraphNode>(column);
        var moved = false;
        var budget = options.CrossingReinsertBudget;

        for (var slot = 0; slot < column.Count && budget > 0; slot++)
        {
            // Only cards that actually cross something are worth relocating.
            if (!Crosses(column[slot], candidates))
            {
                continue;
            }

            for (var target = 0; target < column.Count && budget > 0; target++)
            {
                if (target == slot)
                {
                    continue;
                }

                budget--;

                order.Clear();
                order.AddRange(bestOrder);
                var node = order[slot];
                order.RemoveAt(slot);
                order.Insert(target, node);

                Restack(order, top);
                var score = Count(subset, candidates);

                if (score < best)
                {
                    best = score;
                    bestOrder = new List<GraphNode>(order);
                    moved = true;
                }
            }
        }

        Restack(bestOrder, top);
        column.Clear();
        column.AddRange(bestOrder);
        return moved;
    }

    private bool TrySlide(GraphNode node)
    {
        if (!incident.TryGetValue(node, out var touching) || !columnOf.TryGetValue(node, out var column))
        {
            return false;
        }

        // A slide moves this card by at most the slide limit, so anything outside its wires'
        // bounding box grown by that much can never be crossed no matter which shift is chosen.
        // Filtering once here instead of inside the per-shift loop is the difference between
        // scanning every wire in the graph tens of times per card and scanning it once.
        var candidates = LocalCandidates(touching, options.CrossingSlideLimit);

        var originalY = node.Position.Y;
        var bestY = originalY;
        var best = Count(touching, candidates);

        if (best == 0)
        {
            return false;
        }

        var shifts = new List<float>();

        // A level wire is not the goal, fewest crossings is, so the search is not restricted to
        // heights that straighten something. Alongside the meaningful positions it also sweeps a
        // plain ladder of offsets, which catches the cases where the right answer is simply
        // "a bit further down" and no wire ends up level at all.
        for (var offset = options.CrossingSlideStep; offset <= options.CrossingSlideLimit; offset += options.CrossingSlideStep)
        {
            shifts.Add(offset);
            shifts.Add(-offset);
        }

        foreach (var wire in touching)
        {
            var mineSocket = wires[wire].From.Owner == node ? wires[wire].From : wires[wire].To;
            var theirsSocket = wires[wire].From.Owner == node ? wires[wire].To : wires[wire].From;
            var mine = geometry.PivotOf(mineSocket);

            // The height that makes this wire run dead level.
            shifts.Add(geometry.PivotOf(theirsSocket).Y - mine.Y);

            // Levelling a wire often lands just short of clearing the wire it crosses, because
            // what actually matters is being on the correct side of the other wire's endpoints,
            // not being level with your own. So aim past each crossing partner's ends too.
            foreach (var other in candidates)
            {
                if (other == wire || SharesSocket(wires[wire], wires[other])
                    || !GraphWireGeometry.SegmentsIntersect(from[wire], to[wire], from[other], to[other]))
                {
                    continue;
                }

                foreach (var target in (float[])[from[other].Y, to[other].Y])
                {
                    shifts.Add(target - mine.Y + options.CrossingClearance);
                    shifts.Add(target - mine.Y - options.CrossingClearance);
                }
            }
        }

        foreach (var shift in shifts)
        {
            // Sliding further than a card is a relayout, not a nudge, and costs more in stretched
            // wires elsewhere than the crossing it buys.
            if (Math.Abs(shift) > options.CrossingSlideLimit || Math.Abs(shift) < 0.5f)
            {
                continue;
            }

            Move(node, originalY + shift);

            if (GraphLayout.OverlapsColumn(node, column, geometry))
            {
                continue;
            }

            var score = Count(touching, candidates);

            if (score < best)
            {
                best = score;
                bestY = originalY + shift;
            }
        }

        Move(node, bestY);
        return bestY != originalY;
    }

    private void Move(GraphNode node, float y)
    {
        node.Position = node.Position with { Y = y };
        RefreshNode(node);
    }

    private void Exchange(GraphNode a, GraphNode b)
    {
        var halfA = geometry.SizeOf(a).Y / 2f;
        var halfB = geometry.SizeOf(b).Y / 2f;
        var centerA = a.Position.Y + halfA;
        var centerB = b.Position.Y + halfB;

        Move(a, centerB - halfA);
        Move(b, centerA - halfB);
    }

    private void Restack(List<GraphNode> order, float top)
    {
        var y = top;

        foreach (var node in order)
        {
            Move(node, y);
            y += geometry.SizeOf(node).Y + options.NodeSpacing;
        }
    }

    /// <summary>
    /// The wires of both cards, deduplicated. Uses a stamp array rather than a fresh list and a
    /// linear Contains, because a swap is the most frequently attempted move in the whole repair.
    /// </summary>
    private List<int> Union(GraphNode a, GraphNode b)
    {
        var subset = unionScratch;
        subset.Clear();

        var stamp = ++unionMark;

        Take(a);
        Take(b);

        return subset;

        void Take(GraphNode node)
        {
            if (!incident.TryGetValue(node, out var list))
            {
                return;
            }

            foreach (var wire in list)
            {
                if (unionMarks[wire] != stamp)
                {
                    unionMarks[wire] = stamp;
                    subset.Add(wire);
                }
            }
        }
    }

    private bool Crosses(GraphNode node, ReadOnlySpan<int> candidates)
        => incident.TryGetValue(node, out var list) && Count(list, candidates) > 0;

    private readonly List<int> localScratch = [];
    private int[] allWires = [];
    private readonly List<int> unionScratch = [];
    private int[] unionMarks = [];
    private int unionMark;

    /// <summary>
    /// The wires that could still cross <paramref name="subset"/> once its cards move by up to
    /// <paramref name="slack"/> vertically. Conservative, so the score it feeds is exact.
    /// </summary>
    /// <remarks>
    /// Returns a shared buffer that the next call overwrites: every caller filters once, then
    /// scores many candidate positions against the result, so handing back the buffer avoids an
    /// allocation on a path that runs thousands of times per pass.
    /// </remarks>
    private ReadOnlySpan<int> LocalCandidates(List<int> subset, float slack)
    {
        var left = float.MaxValue;
        var right = float.MinValue;
        var top = float.MaxValue;
        var bottom = float.MinValue;

        foreach (var wire in subset)
        {
            left = Math.Min(left, minX[wire]);
            right = Math.Max(right, maxX[wire]);
            top = Math.Min(top, minY[wire]);
            bottom = Math.Max(bottom, maxY[wire]);
        }

        top -= slack;
        bottom += slack;

        localScratch.Clear();

        for (var i = 0; i < wires.Length; i++)
        {
            if (minX[i] <= right && maxX[i] >= left && minY[i] <= bottom && maxY[i] >= top)
            {
                localScratch.Add(i);
            }
        }

        return CollectionsMarshal.AsSpan(localScratch);
    }

    /// <summary>Crossings between the given wires and the given candidates.</summary>
    private int Count(List<int> subset, ReadOnlySpan<int> candidates)
    {
        var crossings = 0;

        foreach (var wire in subset)
        {
            foreach (var other in candidates)
            {
                if (other == wire || !Overlaps(wire, other) || SharesSocket(wires[wire], wires[other]))
                {
                    continue;
                }

                if (GraphWireGeometry.SegmentsIntersect(from[wire], to[wire], from[other], to[other]))
                {
                    crossings++;
                }
            }
        }

        return crossings;
    }

    private List<(int A, int B)> Crossings(int budget)
    {
        var found = new List<(int, int)>();

        for (var i = 0; i < wires.Length && found.Count < budget; i++)
        {
            for (var j = i + 1; j < wires.Length && found.Count < budget; j++)
            {
                if (!Overlaps(i, j) || SharesSocket(wires[i], wires[j]))
                {
                    continue;
                }

                if (GraphWireGeometry.SegmentsIntersect(from[i], to[i], from[j], to[j]))
                {
                    found.Add((i, j));
                }
            }
        }

        return found;
    }

    private static bool SharesSocket(GraphWire a, GraphWire b)
        => a.From == b.From || a.To == b.To || a.From == b.To || a.To == b.From;

    /// <summary>Whether two wires overlap in both axes, and so could possibly cross.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Overlaps(int a, int b)
        => GraphWireGeometry.BoxesOverlap(minX[a], maxX[a], minY[a], maxY[a], minX[b], maxX[b], minY[b], maxY[b]);
}
