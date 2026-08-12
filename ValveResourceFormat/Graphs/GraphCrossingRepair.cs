using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Graphs;

/// <summary>
/// Moves placed cards to remove wire crossings, judged on the straight run between real socket
/// pivots.
/// </summary>
/// <remarks>
/// Node ordering alone cannot see which socket row a wire lands on, so a crossing between two
/// wires into the same card is only visible once the cards have coordinates. This pass works on
/// those coordinates, trying four moves in increasing cost: exchange two cards in a column,
/// reinsert a card at another slot, slide a card to a height that clears a crossing, and move a
/// card out from under a wire that passes across it. Wire endpoints are cached in parallel arrays
/// and refreshed per moved card, since the scoring loop runs tens of millions of times.
/// </remarks>
internal sealed class CrossingRepair
{
    private const float ColumnQuantum = 8f;

    /// <summary>Slack allowed on the separation test, so gaps laid out at exactly the spacing pass.</summary>
    private const float SeparationTolerance = 0.5f;

    private readonly Vector2[] positions;
    private readonly Vector2[] sizes;
    private readonly GraphLayoutOptions options;
    private readonly LayoutDeadline deadline;

    private readonly GraphLayoutEdge[] wires;
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

    private readonly List<int>[] incident;
    private readonly List<int>[] upstream;
    private readonly int[] columnOf;
    private readonly List<List<int>> columns = [];

    /// <summary>
    /// The island's cards ordered by left edge, with their widths. Every move here is vertical, so
    /// this is built once and stays valid, and it is what the separation veto searches: quantised
    /// columns say nothing about which cards actually overlap in x once widths differ.
    /// </summary>
    private readonly int[] byLeft;
    private readonly float[] lefts;
    private readonly float maxWidth;

    public CrossingRepair(Vector2[] positions, Vector2[] sizes, GraphLayoutEdge[] edges, GraphLayoutOptions options, LayoutDeadline deadline)
    {
        this.positions = positions;
        this.sizes = sizes;
        this.options = options;
        this.deadline = deadline;

        wires = [.. edges.Where(static e => e.From != e.To)];
        from = new Vector2[wires.Length];
        to = new Vector2[wires.Length];
        minX = new float[wires.Length];
        maxX = new float[wires.Length];
        minY = new float[wires.Length];
        maxY = new float[wires.Length];

        incident = new List<int>[sizes.Length];
        upstream = new List<int>[sizes.Length];
        columnOf = new int[sizes.Length];

        for (var i = 0; i < sizes.Length; i++)
        {
            incident[i] = [];
            upstream[i] = [];
        }

        for (var i = 0; i < wires.Length; i++)
        {
            Refresh(i);
            incident[wires[i].From].Add(i);
            incident[wires[i].To].Add(i);
            upstream[wires[i].To].Add(wires[i].From);
        }

        unionMarks = new int[wires.Length];
        allWires = [.. Enumerable.Range(0, wires.Length)];

        var buckets = new Dictionary<int, int>();

        for (var node = 0; node < sizes.Length; node++)
        {
            var key = (int)MathF.Round(positions[node].X / ColumnQuantum);

            if (!buckets.TryGetValue(key, out var column))
            {
                buckets[key] = column = columns.Count;
                columns.Add([]);
            }

            columns[column].Add(node);
            columnOf[node] = column;
        }

        byLeft = [.. Enumerable.Range(0, sizes.Length).OrderBy(n => positions[n].X)];
        lefts = new float[byLeft.Length];

        for (var i = 0; i < byLeft.Length; i++)
        {
            lefts[i] = positions[byLeft[i]].X;
            maxWidth = Math.Max(maxWidth, sizes[byLeft[i]].X);
        }
    }

    /// <summary>
    /// Whether a card sits too close to another card of its island where it is now: cards that
    /// overlap in x have to keep <see cref="GraphLayoutOptions.NodeSpacing"/> between them in y.
    /// Placement leaves every pair like that, so every move is vetoed against it.
    /// </summary>
    private bool Blocked(int node)
    {
        var min = positions[node];
        var max = min + sizes[node];
        var spacing = options.NodeSpacing - SeparationTolerance;

        for (var i = FirstReaching(min.X); i < byLeft.Length && lefts[i] < max.X; i++)
        {
            var other = byLeft[i];

            if (other == node)
            {
                continue;
            }

            var otherMin = positions[other];

            if (!GraphLayout.CardsClear(min, max, otherMin, otherMin + sizes[other], spacing))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>First of the x-sorted cards whose right edge can still reach <paramref name="left"/>.</summary>
    private int FirstReaching(float left)
    {
        var reach = left - maxWidth;
        var low = 0;
        var high = lefts.Length;

        while (low < high)
        {
            var mid = (low + high) / 2;

            if (lefts[mid] < reach)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    private void Refresh(int wire)
    {
        from[wire] = positions[wires[wire].From] + wires[wire].FromPivot;
        to[wire] = positions[wires[wire].To] + wires[wire].ToPivot;
        minX[wire] = Math.Min(from[wire].X, to[wire].X);
        maxX[wire] = Math.Max(from[wire].X, to[wire].X);
        minY[wire] = Math.Min(from[wire].Y, to[wire].Y);
        maxY[wire] = Math.Max(from[wire].Y, to[wire].Y);
    }

    private void RefreshNode(int node)
    {
        foreach (var wire in incident[node])
        {
            Refresh(wire);
        }
    }

    /// <summary>
    /// Whether the layout deadline this island runs under has passed. Checked between moves rather
    /// than inside the scoring loops, so it always stops on a consistent layout, never half way
    /// through a swap.
    /// </summary>
    private bool Spent => deadline.Expired;

    public void Run()
    {
        if (wires.Length < 2)
        {
            return;
        }

        for (var pass = 0; pass < options.CrossingRepairPasses && !Spent; pass++)
        {
            var improved = false;

            foreach (var column in columns)
            {
                if (Spent)
                {
                    break;
                }

                if (column.Count < 2)
                {
                    continue;
                }

                column.Sort((a, b) => positions[a].Y.CompareTo(positions[b].Y));

                for (var i = 0; i + 1 < column.Count && !Spent; i++)
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
                if (Spent)
                {
                    break;
                }

                if (TrySwap(wires[a].From, wires[b].From) || TrySwap(wires[a].To, wires[b].To)
                    || TrySwapBranches(a, b))
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
            for (var node = 0; node < sizes.Length && !Spent; node++)
            {
                improved |= TrySlide(node);
            }

            // Last, because it only makes sense once the cards have stopped moving for crossings.
            for (var node = 0; node < sizes.Length && !Spent; node++)
            {
                improved |= TryClearWires(node);
            }

            if (!improved)
            {
                return;
            }
        }
    }

    private bool TrySwap(int x, int y)
    {
        if (x == y || columnOf[x] != columnOf[y]
            || (incident[x].Count == 0 && incident[y].Count == 0))
        {
            return false;
        }

        var subset = Union(x, y);

        // Scored against every wire. Narrowing the set first costs a pass over all of them, which
        // only pays back when the same subset is scored many times over; a swap scores twice.
        var candidates = allWires;
        var before = Count(subset, candidates);
        Exchange(x, y);

        // Cards of different heights can land on a neighbour when they trade places, and the
        // layout guarantees no overlapping cards, so such a swap is refused outright.
        if (Blocked(x) || Blocked(y) || Count(subset, candidates) >= before)
        {
            Exchange(x, y);
            return false;
        }

        return true;
    }

    private bool TryReinsert(List<int> column)
    {
        // Reinsertion restacks a whole column at uniform gaps, which discards the pivot alignment
        // for every card in it. On a small graph that is a good trade for the crossings it buys;
        // on a large one it stretches far more wire than it saves, so it is left off there.
        if (column.Count is < 3 or > 40 || sizes.Length > options.CrossingReinsertMaxNodes)
        {
            return false;
        }

        column.Sort((a, b) => positions[a].Y.CompareTo(positions[b].Y));

        var subset = new List<int>();

        foreach (var node in column)
        {
            subset.AddRange(incident[node]);
        }

        if (subset.Count == 0)
        {
            return false;
        }

        // Reinsertion restacks the column, so a card can travel the column's whole height.
        var top = positions[column[0]].Y;
        var last = column[^1];
        var candidates = LocalCandidates(subset, positions[last].Y + sizes[last].Y - top);

        var placed = new float[column.Count];

        for (var i = 0; i < column.Count; i++)
        {
            placed[i] = positions[column[i]].Y;
        }

        var best = Count(subset, candidates);
        var bestOrder = new List<int>(column);
        var order = new List<int>(column);
        var moved = false;
        var budget = options.CrossingReinsertBudget;

        for (var slot = 0; slot < column.Count && budget > 0 && !Spent; slot++)
        {
            // Asked of the best order stacked, rather than of whatever the last rejected candidate
            // left behind, and of the card that would actually be relocated.
            Restack(bestOrder, top);

            if (!Crosses(bestOrder[slot], candidates))
            {
                continue;
            }

            for (var target = 0; target < column.Count && budget > 0 && !Spent; target++)
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

                if (!Fits(order))
                {
                    continue;
                }

                var score = Count(subset, candidates);

                if (score < best)
                {
                    best = score;
                    bestOrder = new List<int>(order);
                    moved = true;
                }
            }
        }

        if (!moved)
        {
            for (var i = 0; i < column.Count; i++)
            {
                Move(column[i], placed[i]);
            }

            return false;
        }

        Restack(bestOrder, top);
        column.Clear();
        column.AddRange(bestOrder);
        return true;

        bool Fits(List<int> stacked)
        {
            foreach (var node in stacked)
            {
                if (Blocked(node))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private bool TrySlide(int node)
    {
        var touching = incident[node];

        if (touching.Count == 0)
        {
            return false;
        }

        // A slide moves this card by at most the slide limit, so anything outside its wires'
        // bounding box grown by that much can never be crossed no matter which shift is chosen.
        // Filtering once here instead of inside the per-shift loop is the difference between
        // scanning every wire in the graph tens of times per card and scanning it once.
        var candidates = LocalCandidates(touching, options.CrossingSlideLimit);

        var originalY = positions[node].Y;
        var bestY = originalY;
        var best = Count(touching, candidates);

        if (best == 0)
        {
            return false;
        }

        // Sliding further than a card is a relayout, not a nudge, and costs more in stretched
        // wires elsewhere than the crossing it buys, so out-of-range heights are never generated.
        // Heights are also collected at whole-pixel resolution: a hub card produces one candidate
        // per crossing partner per wire, and on a dense island the vast majority repeat.
        var shifts = new List<float>();
        var seen = new HashSet<int>();

        void Consider(float shift)
        {
            if (Math.Abs(shift) <= options.CrossingSlideLimit && Math.Abs(shift) >= 0.5f && seen.Add((int)MathF.Round(shift)))
            {
                shifts.Add(shift);
            }
        }

        // A level wire is not the goal, fewest crossings is, so the search is not restricted to
        // heights that straighten something. Alongside the meaningful positions it also sweeps a
        // plain ladder of offsets, which catches the cases where the right answer is simply
        // "a bit further down" and no wire ends up level at all.
        for (var offset = options.CrossingSlideStep; offset <= options.CrossingSlideLimit; offset += options.CrossingSlideStep)
        {
            Consider(offset);
            Consider(-offset);
        }

        foreach (var wire in touching)
        {
            var atSource = wires[wire].From == node;
            var mine = atSource ? from[wire] : to[wire];
            var theirs = atSource ? to[wire] : from[wire];

            // The height that makes this wire run dead level.
            Consider(theirs.Y - mine.Y);

            // Levelling a wire often lands just short of clearing the wire it crosses, because
            // what actually matters is being on the correct side of the other wire's endpoints,
            // not being level with your own. So aim past each crossing partner's ends too.
            foreach (var other in candidates)
            {
                if (other == wire || SharesSocket(wires[wire], wires[other])
                    || !GraphSegmentGeometry.SegmentsIntersect(from[wire], to[wire], from[other], to[other]))
                {
                    continue;
                }

                foreach (var target in (float[])[from[other].Y, to[other].Y])
                {
                    Consider(target - mine.Y + options.CrossingClearance);
                    Consider(target - mine.Y - options.CrossingClearance);
                }
            }
        }

        foreach (var shift in shifts)
        {
            // Scoring one height costs a pass over every wire this card can reach, so a card with
            // many wires is where the budget runs out; it is checked here rather than only between
            // cards so one hub cannot overrun it.
            if (Spent)
            {
                break;
            }

            Move(node, originalY + shift);

            if (Blocked(node))
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

    private void Move(int node, float y)
    {
        positions[node] = positions[node] with { Y = y };
        RefreshNode(node);
    }

    private void Exchange(int a, int b)
    {
        var halfA = sizes[a].Y / 2f;
        var halfB = sizes[b].Y / 2f;
        var centerA = positions[a].Y + halfA;
        var centerB = positions[b].Y + halfB;

        Move(a, centerB - halfA);
        Move(b, centerA - halfB);
    }

    private void Restack(List<int> order, float top)
    {
        var y = top;

        foreach (var node in order)
        {
            Move(node, y);
            y += sizes[node].Y + options.NodeSpacing;
        }
    }

    /// <summary>
    /// The wires of both cards, deduplicated. Uses a stamp array rather than a fresh list and a
    /// linear Contains, because a swap is the most frequently attempted move in the whole repair.
    /// </summary>
    private List<int> Union(int a, int b)
    {
        var subset = BeginUnion();
        Take(a);
        Take(b);
        return subset;
    }

    /// <summary>The same union over a whole branch, for the moves that shift many cards at once.</summary>
    private List<int> Union(IEnumerable<int> nodes)
    {
        var subset = BeginUnion();

        foreach (var node in nodes)
        {
            Take(node);
        }

        return subset;
    }

    private List<int> BeginUnion()
    {
        unionScratch.Clear();
        unionStamp = ++unionMark;
        return unionScratch;
    }

    private void Take(int node)
    {
        foreach (var wire in incident[node])
        {
            if (unionMarks[wire] != unionStamp)
            {
                unionMarks[wire] = unionStamp;
                unionScratch.Add(wire);
            }
        }
    }

    private bool Crosses(int node, ReadOnlySpan<int> candidates)
        => Count(incident[node], candidates) > 0;

    private readonly List<int> localScratch = [];
    private int[] allWires = [];
    private readonly List<int> unionScratch = [];
    private int[] unionMarks = [];
    private int unionStamp;
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

                if (GraphSegmentGeometry.SegmentsIntersect(from[wire], to[wire], from[other], to[other]))
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

        for (var i = 0; i < wires.Length && found.Count < budget && !Spent; i++)
        {
            for (var j = i + 1; j < wires.Length && found.Count < budget; j++)
            {
                if (!Overlaps(i, j) || SharesSocket(wires[i], wires[j]))
                {
                    continue;
                }

                if (GraphSegmentGeometry.SegmentsIntersect(from[i], to[i], from[j], to[j]))
                {
                    found.Add((i, j));
                }
            }
        }

        return found;
    }

    private static bool SharesSocket(GraphLayoutEdge a, GraphLayoutEdge b)
        => a.FromSocket == b.FromSocket || a.ToSocket == b.ToSocket
        || a.FromSocket == b.ToSocket || a.ToSocket == b.FromSocket;

    /// <summary>
    /// Moves a card out from under any wire that merely passes across it. Unlike the other moves
    /// this is not a search: covering a long near-horizontal wire barely changes with a small
    /// step, so scoring nudges never finds the way out. Instead the exact distance that clears
    /// every offending wire is computed and taken in one move, if it is allowed.
    /// </summary>
    private bool TryClearWires(int node)
    {
        var offenders = Underlaps(node, out var lowest, out var highest);

        if (offenders == 0)
        {
            return false;
        }

        // Under everything, or over everything. The shorter move is tried first, but leaving on
        // one side can force this card's own wires across the very wire it is escaping, so both
        // directions get a turn before giving up.
        var originalY = positions[node].Y;
        var height = sizes[node].Y;
        var down = lowest + options.WireClearance - originalY;
        var up = highest - options.WireClearance - (originalY + height);

        var touching = incident[node];
        var before = Count(touching, allWires);

        foreach (var shift in Math.Abs(down) <= Math.Abs(up) ? (float[])[down, up] : [up, down])
        {
            if (Math.Abs(shift) < 1f || Math.Abs(shift) > options.WireClearLimit)
            {
                continue;
            }

            Move(node, originalY + shift);

            // Taken only when the card lands clear of its neighbours, is genuinely out from under
            // wires it was under, and buys that with no crossing of its own.
            if (!Blocked(node) && Underlaps(node, out _, out _) < offenders
                && Count(touching, allWires) <= before + options.ClearCrossingTolerance)
            {
                return true;
            }

            Move(node, originalY);
        }

        return false;
    }

    /// <summary>
    /// Wires that merely pass across a card, and the heights they cross its centre line at. Where
    /// a wire sits at that line is what decides which side to leave by; its overall extent could
    /// be dominated by a far-away end.
    /// </summary>
    private int Underlaps(int node, out float lowest, out float highest)
    {
        var size = sizes[node];
        var top = positions[node].Y;
        var bottom = top + size.Y;
        var middle = positions[node].X + (size.X / 2f);

        lowest = float.MinValue;
        highest = float.MaxValue;
        var offenders = 0;

        foreach (var wire in allWires)
        {
            if (wires[wire].From == node || wires[wire].To == node
                || !GraphSegmentGeometry.BoxesOverlap(minX[wire], maxX[wire], minY[wire], maxY[wire],
                    positions[node].X, positions[node].X + size.X, top, bottom)
                || !GraphSegmentGeometry.SegmentCrossesBox(from[wire], to[wire],
                    positions[node], positions[node] + size))
            {
                continue;
            }

            var span = maxX[wire] - minX[wire];
            var t = span > 0.01f ? Math.Clamp((middle - minX[wire]) / span, 0f, 1f) : 0f;
            var lower = from[wire].X <= to[wire].X ? from[wire] : to[wire];
            var upper = from[wire].X <= to[wire].X ? to[wire] : from[wire];
            var crossing = lower.Y + ((upper.Y - lower.Y) * t);

            lowest = Math.Max(lowest, crossing);
            highest = Math.Min(highest, crossing);
            offenders++;
        }

        return offenders;
    }

    /// <summary>
    /// Reorders two wires arriving at the same card by shifting the whole branch behind one of
    /// them. Exchanging the two sources on their own only moves the crossing when their own
    /// feeders dock in the opposite order, so the fix has to travel with everything upstream:
    /// the pair swaps vertical order without disturbing the shape of either branch.
    /// </summary>
    private bool TrySwapBranches(int wireA, int wireB)
    {
        var target = wires[wireA].To;

        if (target != wires[wireB].To)
        {
            return false;
        }

        if (wires[wireA].From == wires[wireB].From)
        {
            return false;
        }

        // The wire docking higher should come from the higher source.
        var upperDock = to[wireA].Y <= to[wireB].Y ? wireA : wireB;
        var lowerDock = upperDock == wireA ? wireB : wireA;

        if (from[upperDock].Y <= from[lowerDock].Y)
        {
            return false;
        }

        var upperSource = wires[upperDock].From;
        var lowerSource = wires[lowerDock].From;

        var branchUpper = Branch(upperSource, target);
        var branchLower = Branch(lowerSource, target);

        if (branchUpper == null || branchLower == null)
        {
            return false;
        }

        // A card feeding both branches cannot move with either of them.
        branchUpper.ExceptWith(branchLower);

        if (branchUpper.Count == 0)
        {
            return false;
        }

        var shift = from[lowerDock].Y - from[upperDock].Y - options.WireClearance;

        // Sharing a column makes the two sources stacked cards as well as inverted docks, so the
        // branch has to travel far enough to clear the other card rather than just its socket.
        if (columnOf[upperSource] == columnOf[lowerSource])
        {
            shift = Math.Min(shift, positions[lowerSource].Y - sizes[upperSource].Y - options.NodeSpacing - positions[upperSource].Y);
        }

        // Only wires touching the branch can change, so scoring the whole island per candidate
        // would repeat an identical count over every wire that cannot move.
        var subset = Union(branchUpper);
        var before = Count(subset, allWires);

        // Just clearing the other source is the smallest move and is tried first, but a packed
        // layout usually leaves the branch landing on top of whatever else occupies those columns.
        // Parking it clear of the island entirely always has room, at the cost of a taller graph.
        foreach (var candidate in (float[])[shift, ParkingShift(branchUpper, above: shift < 0f)])
        {
            if (Math.Abs(candidate) < 1f || Math.Abs(candidate) > options.BranchShiftLimit)
            {
                continue;
            }

            foreach (var node in branchUpper)
            {
                Move(node, positions[node].Y + candidate);
            }

            var blocked = branchUpper.Any(Blocked);
            var after = Count(subset, allWires);

            if (!blocked && after < before)
            {
                return true;
            }

            foreach (var node in branchUpper)
            {
                Move(node, positions[node].Y - candidate);
            }
        }

        return false;
    }

    /// <summary>
    /// Distance that lifts a branch clear above, or drops it clear below, everything it is not
    /// part of. Nothing occupies the space outside the island, so a shift this far always fits.
    /// </summary>
    private float ParkingShift(HashSet<int> branch, bool above)
    {
        var branchEdge = above ? float.MinValue : float.MaxValue;
        var restEdge = above ? float.MaxValue : float.MinValue;

        for (var node = 0; node < sizes.Length; node++)
        {
            var top = positions[node].Y;
            var bottom = top + sizes[node].Y;

            if (branch.Contains(node))
            {
                branchEdge = above ? Math.Max(branchEdge, bottom) : Math.Min(branchEdge, top);
            }
            else
            {
                restEdge = above ? Math.Min(restEdge, top) : Math.Max(restEdge, bottom);
            }
        }

        if (branchEdge == float.MinValue || branchEdge == float.MaxValue
            || restEdge == float.MaxValue || restEdge == float.MinValue)
        {
            return 0f;
        }

        return above
            ? restEdge - options.NodeSpacing - branchEdge
            : restEdge + options.NodeSpacing - branchEdge;
    }

    /// <summary>
    /// Everything upstream of a card, stopping before the consumer it feeds. Null when the cone
    /// grows past what may be shifted at once, so an oversized walk stops rather than
    /// materialising a branch that is going to be refused.
    /// </summary>
    private HashSet<int>? Branch(int node, int stop)
    {
        var branch = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(node);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            if (current == stop || !branch.Add(current))
            {
                continue;
            }

            if (branch.Count > options.BranchShiftMaxNodes)
            {
                return null;
            }

            foreach (var source in upstream[current])
            {
                pending.Push(source);
            }
        }

        return branch;
    }

    /// <summary>Whether two wires overlap in both axes, and so could possibly cross.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Overlaps(int a, int b)
        => GraphSegmentGeometry.BoxesOverlap(minX[a], maxX[a], minY[a], maxY[a], minX[b], maxX[b], minY[b], maxY[b]);
}
