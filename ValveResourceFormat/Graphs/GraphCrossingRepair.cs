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
/// <para>
/// Whether a move helps depends on nothing but the geometry within its reach, so a refused move
/// is remembered with that reach and not scored again until a later move disturbs it. Most moves
/// are refused on every pass, and this is what keeps the later passes cheap.
/// </para>
/// </remarks>
internal sealed class CrossingRepair
{
    private const float ColumnQuantum = 8f;

    /// <summary>
    /// Distance from a slide's sweep inside which a wire is rescored at every height, kept well
    /// clear of where rounding could flip a crossing test.
    /// </summary>
    private const float SweepMargin = 1f;

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
    /// Vertical extent of each wire. Islands are packed in two dimensions, so most wire pairs are
    /// separated in y, not x; rejecting on y as well as x discards most pairs before the
    /// intersection test.
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
        nearWires = new int[wires.Length][];
        nearCards = new int[sizes.Length][];
        wireBoxes = new Box[wires.Length];
        cardBoxes = new Box[sizes.Length];
        refusedSlides = new Refusal?[sizes.Length];
        refusedClears = new Refusal?[sizes.Length];

        var spans = new Dictionary<(float, float), int[]>();

        for (var i = 0; i < wires.Length; i++)
        {
            nearWires[i] = WiresSpanning(minX[i], maxX[i]);
            wireBoxes[i] = WireBox(i);
        }

        for (var node = 0; node < sizes.Length; node++)
        {
            nearCards[node] = WiresSpanning(positions[node].X, positions[node].X + sizes[node].X);
            cardBoxes[node] = CardBox(node);
        }

        landedCrossings = new int[wires.Length];
        landedFrom = [.. from];
        landedTo = [.. to];
        landedMinY = [.. minY];
        landedMaxY = [.. maxY];

        for (var i = 0; i < wires.Length; i++)
        {
            landedCrossings[i] = Count(i, nearWires[i], int.MaxValue);
        }

        // Cards only ever move vertically, so which wires share an x range never changes, and
        // wires between the same two columns share one list.
        int[] WiresSpanning(float left, float right)
        {
            if (!spans.TryGetValue((left, right), out var near))
            {
                spans[(left, right)] = near = [.. Enumerable.Range(0, wires.Length).Where(i => minX[i] <= right && maxX[i] >= left)];
            }

            return near;
        }

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

        ref var refusal = ref CollectionsMarshal.GetValueRefOrAddDefault(refusedSwaps, (x, y), out _);

        if (StillRefused(ref refusal))
        {
            return false;
        }

        var subset = Union(x, y);
        var before = CrossingsOf(subset);
        var originalX = positions[x];
        var originalY = positions[y];
        Exchange(x, y);

        // Cards of different heights can land on a neighbour when they trade places, and the
        // layout guarantees no overlapping cards, so such a swap is refused outright.
        if (Blocked(x) || Blocked(y) || Count(subset, before) >= before)
        {
            var shift = Math.Max(Math.Abs(positions[x].Y - originalX.Y), Math.Abs(positions[y].Y - originalY.Y));
            Exchange(x, y);

            if (positions[x] != originalX || positions[y] != originalY)
            {
                Commit([x, y]);
            }
            else
            {
                refusal = new Refusal(changes.Count, Reach([x, y], shift));
            }

            return false;
        }

        Commit([x, y]);
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

        ref var refusal = ref CollectionsMarshal.GetValueRefOrAddDefault(refusedReinserts, column, out _);

        if (StillRefused(ref refusal))
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
            refusal = new Refusal(changes.Count, Reach(column, 0f));
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

        var best = CrossingsOf(subset);
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

                var score = Count(subset, candidates, best);

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

            var stacked = column.Sum(node => sizes[node].Y + options.NodeSpacing);
            refusal = new Refusal(changes.Count, Reach(column, stacked + positions[last].Y + sizes[last].Y - top));
            return false;
        }

        Restack(bestOrder, top);
        column.Clear();
        column.AddRange(bestOrder);
        Commit(column);
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

        if (touching.Count == 0 || StillRefused(ref refusedSlides[node]))
        {
            return false;
        }

        var originalY = positions[node].Y;
        var bestY = originalY;
        var best = CrossingsOf(touching);

        if (best == 0)
        {
            refusedSlides[node] = new Refusal(changes.Count, Reach([node], options.CrossingSlideLimit));
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

        // Only the card's end of each wire travels, so over every height a slide can try a wire
        // sweeps a triangle fanning out from its far end. A partner that stays clear of that
        // triangle, of the far end and of the path the near end takes crosses the wire at every
        // height or at none, so it keeps its share of the current count and is never rescored.
        var sweeping = new List<(int Wire, int Other)>();
        var settled = best;

        foreach (var wire in touching)
        {
            var atSource = wires[wire].From == node;
            var mine = atSource ? from[wire] : to[wire];
            var theirs = atSource ? to[wire] : from[wire];
            var sweep = new WireSweep(theirs, mine, -options.CrossingSlideLimit, options.CrossingSlideLimit, SweepMargin);

            // The height that makes this wire run dead level.
            Consider(theirs.Y - mine.Y);

            foreach (var other in nearWires[wire])
            {
                var moves = wires[other].From == node || wires[other].To == node;

                // Everything the wire crosses now lies inside its sweep too, so a partner clear of
                // the sweep's bounds needs nothing further.
                if (other == wire || (!moves && !sweep.Overlaps(minX[other], maxX[other], minY[other], maxY[other]))
                    || SharesSocket(wires[wire], wires[other]))
                {
                    continue;
                }

                var crossing = Overlaps(wire, other) && GraphSegmentGeometry.SegmentsIntersect(from[wire], to[wire], from[other], to[other]);

                // Levelling a wire often lands just short of clearing the wire it crosses, because
                // what actually matters is being on the correct side of the other wire's endpoints,
                // not being level with your own. So aim past each crossing partner's ends too.
                if (crossing)
                {
                    foreach (var target in (float[])[from[other].Y, to[other].Y])
                    {
                        Consider(target - mine.Y + options.CrossingClearance);
                        Consider(target - mine.Y - options.CrossingClearance);
                    }
                }

                if (!moves && !sweep.Reaches(from[other], to[other]))
                {
                    continue;
                }

                sweeping.Add((wire, other));

                if (crossing)
                {
                    settled--;
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

            var score = settled + Sweeping(best - settled);

            if (score < best)
            {
                best = score;
                bestY = originalY + shift;
            }
        }

        Move(node, bestY);

        if (bestY == originalY)
        {
            refusedSlides[node] = new Refusal(changes.Count, Reach([node], options.CrossingSlideLimit));
            return false;
        }

        Commit([node]);
        return true;

        int Sweeping(int limit)
        {
            var crossings = 0;

            foreach (var (wire, other) in sweeping)
            {
                if (Overlaps(wire, other) && GraphSegmentGeometry.SegmentsIntersect(from[wire], to[wire], from[other], to[other])
                    && ++crossings >= limit)
                {
                    break;
                }
            }

            return crossings;
        }
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
        => Count(incident[node], candidates, 1) > 0;

    private readonly List<int> localScratch = [];

    /// <summary>Per wire, every wire whose x range overlaps its own: the only ones it can cross.</summary>
    private readonly int[][] nearWires;

    /// <summary>Per card, every wire whose x range overlaps the card's.</summary>
    private readonly int[][] nearCards;
    private readonly List<int> unionScratch = [];
    private readonly int[] unionMarks = [];
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

    /// <summary>
    /// Crossings between the given wires and the given candidates, counted up to
    /// <paramref name="limit"/>: callers only compare the result against a score to beat.
    /// </summary>
    private int Count(List<int> subset, ReadOnlySpan<int> candidates, int limit)
    {
        var crossings = 0;

        foreach (var wire in subset)
        {
            crossings += Count(wire, candidates, limit - crossings);

            if (crossings >= limit)
            {
                break;
            }
        }

        return crossings;
    }

    /// <summary>
    /// Crossings of each wire with every other wire where the cards have landed, kept current by
    /// <see cref="Commit"/>. Every move starts from the landed layout, so its score to beat comes
    /// from here rather than from a count.
    /// </summary>
    private readonly int[] landedCrossings;

    /// <summary>Wire ends where the cards have landed, which is what <see cref="landedCrossings"/> was counted on.</summary>
    private readonly Vector2[] landedFrom;
    private readonly Vector2[] landedTo;
    private readonly float[] landedMinY;
    private readonly float[] landedMaxY;

    private int CrossingsOf(List<int> subset)
    {
        var total = 0;

        foreach (var wire in subset)
        {
            total += landedCrossings[wire];
        }

        return total;
    }

    /// <summary>Crossings between the given wires and every other wire, counted up to <paramref name="limit"/>.</summary>
    private int Count(List<int> subset, int limit)
    {
        var crossings = 0;

        foreach (var wire in subset)
        {
            crossings += Count(wire, nearWires[wire], limit - crossings);

            if (crossings >= limit)
            {
                break;
            }
        }

        return crossings;
    }

    private int Count(int wire, ReadOnlySpan<int> candidates, int limit)
    {
        var crossings = 0;

        foreach (var other in candidates)
        {
            if (other == wire || !Overlaps(wire, other) || SharesSocket(wires[wire], wires[other]))
            {
                continue;
            }

            if (GraphSegmentGeometry.SegmentsIntersect(from[wire], to[wire], from[other], to[other]) && ++crossings >= limit)
            {
                break;
            }
        }

        return crossings;
    }

    private List<(int A, int B)> Crossings(int budget)
    {
        var found = new List<(int, int)>();

        for (var i = 0; i < wires.Length && found.Count < budget && !Spent; i++)
        {
            foreach (var j in nearWires[i])
            {
                if (found.Count >= budget)
                {
                    break;
                }

                if (j <= i || !Overlaps(i, j) || SharesSocket(wires[i], wires[j]))
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
        if (StillRefused(ref refusedClears[node]))
        {
            return false;
        }

        var offenders = Underlaps(node, out var lowest, out var highest);

        if (offenders == 0)
        {
            refusedClears[node] = new Refusal(changes.Count, Reach([node], options.WireClearLimit));
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
        var before = CrossingsOf(touching);

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
                && Count(touching, before + options.ClearCrossingTolerance + 1) <= before + options.ClearCrossingTolerance)
            {
                Commit([node]);
                return true;
            }

            Move(node, originalY);
        }

        refusedClears[node] = new Refusal(changes.Count, Reach([node], options.WireClearLimit));
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

        foreach (var wire in nearCards[node])
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
            var t = span > 0.01f ? MathUtils.Saturate((middle - minX[wire]) / span) : 0f;
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

        ref var refusal = ref CollectionsMarshal.GetValueRefOrAddDefault(refusedBranchSwaps, (wireA, wireB), out _);

        if (StillRefused(ref refusal))
        {
            return false;
        }

        // Parking a branch measures the whole island, so any move anywhere can change the outcome.
        var everywhere = new Refusal(changes.Count, Box.Everything);

        var upperSource = wires[upperDock].From;
        var lowerSource = wires[lowerDock].From;

        var branchUpper = Branch(upperSource, target);
        var branchLower = Branch(lowerSource, target);

        if (branchUpper == null || branchLower == null)
        {
            refusal = everywhere;
            return false;
        }

        // A card feeding both branches cannot move with either of them.
        branchUpper.ExceptWith(branchLower);

        if (branchUpper.Count == 0)
        {
            refusal = everywhere;
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
        var before = CrossingsOf(subset);
        var originalHeights = branchUpper.Select(node => positions[node].Y).ToArray();

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

            if (!branchUpper.Any(Blocked) && Count(subset, before) < before)
            {
                Commit(branchUpper);
                return true;
            }

            foreach (var node in branchUpper)
            {
                Move(node, positions[node].Y - candidate);
            }
        }

        if (branchUpper.Select(node => positions[node].Y).SequenceEqual(originalHeights))
        {
            refusal = everywhere;
        }
        else
        {
            Commit(branchUpper);
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

    /// <summary>An axis-aligned box: what a refused move depended on, or what a landed move disturbed.</summary>
    private readonly record struct Box(float MinX, float MaxX, float MinY, float MaxY)
    {
        public static Box Everything { get; } = new(float.MinValue, float.MaxValue, float.MinValue, float.MaxValue);

        public bool Overlaps(Box other)
            => GraphSegmentGeometry.BoxesOverlap(MinX, MaxX, MinY, MaxY, other.MinX, other.MaxX, other.MinY, other.MaxY);

        public Box Union(Box other)
            => new(Math.Min(MinX, other.MinX), Math.Max(MaxX, other.MaxX), Math.Min(MinY, other.MinY), Math.Max(MaxY, other.MaxY));
    }

    /// <summary>
    /// A refused move: the length of <see cref="changes"/> when it was refused, and the box
    /// holding every card and wire its outcome depended on.
    /// </summary>
    private readonly record struct Refusal(int Since, Box Reach);

    /// <summary>
    /// The boxes landed moves disturbed, in order: where each moved card and each of its wires was
    /// before the move and where it is after it.
    /// </summary>
    private readonly List<Box> changes = [];

    /// <summary>Where each card and wire was when it last went into <see cref="changes"/>.</summary>
    private readonly Box[] cardBoxes;
    private readonly Box[] wireBoxes;

    private readonly Dictionary<(int, int), Refusal?> refusedSwaps = [];
    private readonly Dictionary<(int, int), Refusal?> refusedBranchSwaps = [];
    private readonly Dictionary<List<int>, Refusal?> refusedReinserts = [];
    private readonly Refusal?[] refusedSlides;
    private readonly Refusal?[] refusedClears;

    private Box CardBox(int node)
        => new(positions[node].X, positions[node].X + sizes[node].X, positions[node].Y, positions[node].Y + sizes[node].Y);

    private Box WireBox(int wire) => new(minX[wire], maxX[wire], minY[wire], maxY[wire]);

    /// <summary>
    /// Everything a move of <paramref name="nodes"/> by up to <paramref name="travel"/> vertically
    /// can touch: the cards and their wires, grown by that travel plus the card spacing the
    /// separation veto measures.
    /// </summary>
    private Box Reach(IEnumerable<int> nodes, float travel)
    {
        var reach = new Box(float.MaxValue, float.MinValue, float.MaxValue, float.MinValue);

        foreach (var node in nodes)
        {
            reach = reach.Union(CardBox(node));

            foreach (var wire in incident[node])
            {
                reach = reach.Union(WireBox(wire));
            }
        }

        var margin = travel + options.NodeSpacing + 1f;
        return new Box(reach.MinX - 1f, reach.MaxX + 1f, reach.MinY - margin, reach.MaxY + margin);
    }

    /// <summary>
    /// Records that the given cards moved: brings the crossing counts of their wires and of every
    /// wire those cross or crossed up to date, and logs what moved for the refusals to check.
    /// Undoing a refused move can leave a card a rounding error away from where it was, which
    /// counts as a move too.
    /// </summary>
    private void Commit(IReadOnlyCollection<int> nodes)
    {
        var moved = new HashSet<int>();

        foreach (var node in nodes)
        {
            moved.UnionWith(incident[node]);
        }

        // Each wire's count is judged from its own side, as a count would judge it, so both ends of
        // a pair are tested in their own order.
        foreach (var wire in moved)
        {
            foreach (var other in nearWires[wire])
            {
                if (other == wire || SharesSocket(wires[wire], wires[other]))
                {
                    continue;
                }

                landedCrossings[wire] += Delta(wire, other);

                if (!moved.Contains(other))
                {
                    landedCrossings[other] += Delta(other, wire);
                }
            }
        }

        foreach (var wire in moved)
        {
            landedFrom[wire] = from[wire];
            landedTo[wire] = to[wire];
            landedMinY[wire] = minY[wire];
            landedMaxY[wire] = maxY[wire];
        }

        foreach (var node in nodes)
        {
            changes.Add(cardBoxes[node]);
            cardBoxes[node] = CardBox(node);
            changes.Add(cardBoxes[node]);

            foreach (var wire in incident[node])
            {
                changes.Add(wireBoxes[wire]);
                wireBoxes[wire] = WireBox(wire);
                changes.Add(wireBoxes[wire]);
            }
        }
    }

    /// <summary>
    /// Whether a move refused earlier would be refused again: nothing landed since has disturbed
    /// its reach. A refusal that still holds is brought up to date, so it is not checked against
    /// the same changes twice.
    /// </summary>
    private bool StillRefused(ref Refusal? refusal)
    {
        if (refusal is not { } refused)
        {
            return false;
        }

        for (var i = refused.Since; i < changes.Count; i++)
        {
            if (changes[i].Overlaps(refused.Reach))
            {
                refusal = null;
                return false;
            }
        }

        refusal = refused with { Since = changes.Count };
        return true;
    }

    /// <summary>How the crossing of two wires changed since they last landed, judged as <see cref="Count(int, ReadOnlySpan{int}, int)"/> judges it.</summary>
    private int Delta(int wire, int other)
    {
        var now = Overlaps(wire, other) && GraphSegmentGeometry.SegmentsIntersect(from[wire], to[wire], from[other], to[other]);
        // Cards only move vertically, so a wire's x extent where it landed is its x extent now.
        var then = GraphSegmentGeometry.BoxesOverlap(minX[wire], maxX[wire], landedMinY[wire], landedMaxY[wire],
                minX[other], maxX[other], landedMinY[other], landedMaxY[other])
            && GraphSegmentGeometry.SegmentsIntersect(landedFrom[wire], landedTo[wire], landedFrom[other], landedTo[other]);

        return (now ? 1 : 0) - (then ? 1 : 0);
    }

    /// <summary>Whether two wires overlap in both axes, and so could possibly cross.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Overlaps(int a, int b)
        => GraphSegmentGeometry.BoxesOverlap(minX[a], maxX[a], minY[a], maxY[a], minX[b], maxX[b], minY[b], maxY[b]);
}
