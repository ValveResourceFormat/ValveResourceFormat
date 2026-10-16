using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Graphs;

/// <summary>
/// Moves placed cards to remove wire crossings and to run wires level, judged on the straight run
/// between real socket pivots.
/// </summary>
/// <remarks>
/// Node ordering alone cannot see which socket row a wire lands on, so a crossing between two
/// wires into the same card is only visible once the cards have coordinates. This pass works on
/// those coordinates, trying five moves in increasing cost: exchange two cards in a column,
/// reinsert a card at another slot, slide a card to a height that clears a crossing, move a card
/// out from under a wire that passes across it, and let <see cref="CardSnap"/> shift cards to
/// where their wires run level at no added crossing. Among positions with equal crossings, swaps
/// and slides prefer the one leaving more wires level. Wire endpoints are cached in parallel arrays and refreshed per moved card, since the
/// scoring loop runs tens of millions of times.
/// <para>
/// Whether a move helps depends on nothing but the geometry within its reach, so a refused move
/// is remembered with that reach and not scored again until a later move disturbs it. Most moves
/// are refused on every pass, and this is what keeps the later passes cheap.
/// </para>
/// </remarks>
internal sealed class CrossingRepair
{
    /// <summary>
    /// Distance from a slide's sweep inside which a wire is rescored at every height, kept well
    /// clear of where rounding could flip a crossing test.
    /// </summary>
    private const float SweepMargin = 1f;

    /// <summary>How many times the repair sweeps the graph before giving up.</summary>
    private const int CrossingRepairPasses = 24;

    /// <summary>Most crossings examined per repair pass.</summary>
    private const int CrossingRepairBudget = 3000;

    /// <summary>Furthest a slide may move a single card.</summary>
    private const float CrossingSlideLimit = 90f;

    /// <summary>Step of the offset ladder a slide sweeps alongside the wire-aligned heights.</summary>
    private const float CrossingSlideStep = 14f;

    /// <summary>How far past a crossing wire's end a slide aims, so it clears rather than grazes.</summary>
    private const float CrossingClearance = 12f;

    /// <summary>Margin left between a card and the wire it is moved clear of.</summary>
    private const float WireClearance = 32f;

    /// <summary>Furthest a card may be moved to get out from under a wire.</summary>
    private const float WireClearLimit = 260f;

    /// <summary>Most cards a branch shift moves as one.</summary>
    private const int BranchShiftMaxNodes = 40;

    /// <summary>Furthest a branch may be shifted vertically.</summary>
    private const float BranchShiftLimit = 600f;

    /// <summary>Relocations tried per column per pass.</summary>
    private const int CrossingReinsertBudget = 240;

    /// <summary>Largest island on which cards may be relocated by restacking their column.</summary>
    private const int CrossingReinsertMaxNodes = 200;

    private readonly Vector2[] positions;
    private readonly Vector2[] sizes;
    private readonly LayoutDeadline deadline;

    private readonly GraphLayoutEdge[] wires;
    private readonly Vector2[] from;
    private readonly Vector2[] to;

    /// <summary>Bounding box of each wire, kept current as its cards move.</summary>
    private readonly Box[] bounds;

    private readonly List<int>[] incident;
    private readonly List<int>[] upstream;
    private readonly int[] columnOf;
    private readonly List<List<int>> columns;

    /// <summary>
    /// The island's cards ordered by left edge, with their widths. Every move here is vertical, so
    /// this is built once and stays valid, and it is what the separation veto searches: quantised
    /// columns say nothing about which cards actually overlap in x once widths differ.
    /// </summary>
    private readonly int[] byLeft;
    private readonly float[] lefts;
    private readonly float maxWidth;

    /// <summary>Per wire, every wire whose x range overlaps its own: the only ones it can cross.</summary>
    private readonly int[][] nearWires;

    /// <summary>Per card, every wire whose x range overlaps the card's.</summary>
    private readonly int[][] nearCards;

    /// <summary>Per wire, every card whose x range overlaps the wire's.</summary>
    private readonly int[][] nearWireCards;

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

    private readonly CardSnap snap;

    private readonly List<int> localScratch = [];
    private readonly List<int> unionScratch = [];
    private readonly int[] unionMarks;
    private int unionStamp;
    private int unionMark;

    public CrossingRepair(Vector2[] positions, Vector2[] sizes, GraphLayoutEdge[] edges, LayoutDeadline deadline)
    {
        this.positions = positions;
        this.sizes = sizes;
        this.deadline = deadline;

        wires = [.. edges.Where(static e => e.From != e.To)];
        from = new Vector2[wires.Length];
        to = new Vector2[wires.Length];
        bounds = new Box[wires.Length];

        incident = GraphLayout.IncidentWires(sizes.Length, wires);
        upstream = new List<int>[sizes.Length];
        columnOf = new int[sizes.Length];

        for (var i = 0; i < sizes.Length; i++)
        {
            upstream[i] = [];
        }

        for (var i = 0; i < wires.Length; i++)
        {
            Refresh(i);
            upstream[wires[i].To].Add(wires[i].From);
        }

        unionMarks = new int[wires.Length];
        nearWires = new int[wires.Length][];
        nearCards = new int[sizes.Length][];
        wireBoxes = new Box[wires.Length];
        cardBoxes = new Box[sizes.Length];
        refusedSlides = new Refusal?[sizes.Length];
        refusedClears = new Refusal?[sizes.Length];
        nearWireCards = new int[wires.Length][];

        var spans = new Dictionary<(float, float), int[]>();
        var cardSpans = new Dictionary<(float, float), int[]>();
        var wiresByLeft = Enumerable.Range(0, wires.Length).OrderBy(i => bounds[i].MinX).ToArray();
        var wireLefts = wiresByLeft.Select(i => bounds[i].MinX).ToArray();

        for (var i = 0; i < wires.Length; i++)
        {
            nearWires[i] = WiresSpanning(bounds[i].MinX, bounds[i].MaxX);
            nearWireCards[i] = CardsSpanning(bounds[i].MinX, bounds[i].MaxX);
            wireBoxes[i] = bounds[i];
        }

        for (var node = 0; node < sizes.Length; node++)
        {
            nearCards[node] = WiresSpanning(positions[node].X, positions[node].X + sizes[node].X);
            cardBoxes[node] = CardBox(node);
        }

        landedCrossings = new int[wires.Length];
        landedFrom = [.. from];
        landedTo = [.. to];
        landedMinY = [.. bounds.Select(static box => box.MinY)];
        landedMaxY = [.. bounds.Select(static box => box.MaxY)];

        for (var i = 0; i < wires.Length; i++)
        {
            landedCrossings[i] = Count(i, nearWires[i], int.MaxValue);
        }

        // Cards only ever move vertically, so which wires share an x range never changes, and
        // wires between the same two columns share one list.
        int[] WiresSpanning(float left, float right)
        {
            if (spans.TryGetValue((left, right), out var near))
            {
                return near;
            }

            var found = new List<int>();

            for (var k = 0; k < wiresByLeft.Length && wireLefts[k] <= right; k++)
            {
                if (bounds[wiresByLeft[k]].MaxX >= left)
                {
                    found.Add(wiresByLeft[k]);
                }
            }

            found.Sort();
            return spans[(left, right)] = [.. found];
        }

        int[] CardsSpanning(float left, float right)
        {
            if (!cardSpans.TryGetValue((left, right), out var near))
            {
                cardSpans[(left, right)] = near = [.. Enumerable.Range(0, sizes.Length).Where(n => positions[n].X <= right && positions[n].X + sizes[n].X >= left)];
            }

            return near;
        }

        columns = GraphLayout.GroupByColumn(positions, sizes.Length);

        for (var column = 0; column < columns.Count; column++)
        {
            foreach (var node in columns[column])
            {
                columnOf[node] = column;
            }
        }

        byLeft = [.. Enumerable.Range(0, sizes.Length).OrderBy(n => positions[n].X)];
        lefts = new float[byLeft.Length];

        for (var i = 0; i < byLeft.Length; i++)
        {
            lefts[i] = positions[byLeft[i]].X;
            maxWidth = Math.Max(maxWidth, sizes[byLeft[i]].X);
        }

        snap = new CardSnap(positions, sizes, wires, deadline, crossingsPerLevelWire: 0f, rounds: 1)
        {
            Landed = nodes =>
            {
                foreach (var node in nodes)
                {
                    RefreshNode(node);
                }

                Land(nodes);
            },
        };
    }

    /// <summary>
    /// Whether a card sits too close to another card of its island where it is now: cards that
    /// overlap in x have to keep <see cref="GraphLayout.NodeSpacing"/> between them in y.
    /// Placement leaves every pair like that, so every move is vetoed against it.
    /// </summary>
    private bool Blocked(int node)
    {
        var min = positions[node];
        var max = min + sizes[node];

        for (var i = FirstReaching(min.X); i < byLeft.Length && lefts[i] < max.X; i++)
        {
            var other = byLeft[i];

            if (other == node)
            {
                continue;
            }

            var otherMin = positions[other];

            if (!GraphLayout.CardsClear(min, max, otherMin, otherMin + sizes[other], GraphLayout.MinimumGap))
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
        from[wire] = wires[wire].SourcePoint(positions);
        to[wire] = wires[wire].TargetPoint(positions);
        bounds[wire] = new Box(Math.Min(from[wire].X, to[wire].X), Math.Max(from[wire].X, to[wire].X), Math.Min(from[wire].Y, to[wire].Y), Math.Max(from[wire].Y, to[wire].Y));
    }

    private void RefreshNode(int node)
    {
        foreach (var wire in incident[node])
        {
            Refresh(wire);
        }
    }

    public void Run()
    {
        if (wires.Length < 2)
        {
            return;
        }

        for (var pass = 0; pass < CrossingRepairPasses && !deadline.Expired; pass++)
        {
            var improved = false;

            foreach (var column in columns)
            {
                if (deadline.Expired)
                {
                    break;
                }

                if (column.Count < 2)
                {
                    continue;
                }

                column.Sort((a, b) => positions[a].Y.CompareTo(positions[b].Y));

                for (var i = 0; i + 1 < column.Count && !deadline.Expired; i++)
                {
                    if (TrySwap(column[i], column[i + 1]))
                    {
                        (column[i], column[i + 1]) = (column[i + 1], column[i]);
                        improved = true;
                    }
                }
            }

            foreach (var (a, b) in Crossings(CrossingRepairBudget))
            {
                if (deadline.Expired)
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
                if (deadline.Expired)
                {
                    break;
                }

                improved |= TryReinsert(column);
            }

            for (var node = 0; node < sizes.Length && !deadline.Expired; node++)
            {
                improved |= TrySlide(node);
            }

            for (var node = 0; node < sizes.Length && !deadline.Expired; node++)
            {
                improved |= TryClearWires(node);
            }

            if (!deadline.Expired)
            {
                snap.Run(null);
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
        var xTop = positions[x].Y;
        var yTop = positions[y].Y;
        var (upper, lower) = xTop <= yTop ? (x, y) : (y, x);
        var top = positions[upper].Y;
        var bottom = positions[lower].Y + sizes[lower].Y;

        // Trading centres, or packing the lower card against the upper's top and the upper card
        // against the lower's bottom, which keeps the outer gaps.
        Span<(float XTop, float YTop)> placements =
        [
            (yTop + ((sizes[y].Y - sizes[x].Y) / 2f), xTop + ((sizes[x].Y - sizes[y].Y) / 2f)),
            upper == x ? (bottom - sizes[x].Y, top) : (top, bottom - sizes[y].Y),
        ];

        var best = before;
        var bestLevel = -1;
        var bestPlacement = -1;
        var shift = 0f;

        for (var i = 0; i < placements.Length; i++)
        {
            Move(x, placements[i].XTop);
            Move(y, placements[i].YTop);
            shift = Math.Max(shift, Math.Max(Math.Abs(placements[i].XTop - xTop), Math.Abs(placements[i].YTop - yTop)));

            if (Blocked(x) || Blocked(y))
            {
                continue;
            }

            var score = Count(subset, best + 1);

            if (score > best || (score == best && score == before))
            {
                continue;
            }

            var level = LevelOf(subset);

            if (score == best && level <= bestLevel)
            {
                continue;
            }

            best = score;
            bestLevel = level;
            bestPlacement = i;
        }

        if (bestPlacement < 0)
        {
            Move(x, xTop);
            Move(y, yTop);
            refusal = new Refusal(changes.Count, Reach([x, y], shift));
            return false;
        }

        Move(x, placements[bestPlacement].XTop);
        Move(y, placements[bestPlacement].YTop);
        Commit([x, y]);
        return true;
    }

    private bool TryReinsert(List<int> column)
    {
        if (column.Count is < 3 or > 40 || sizes.Length > CrossingReinsertMaxNodes)
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
        var budget = CrossingReinsertBudget;

        for (var slot = 0; slot < column.Count && budget > 0 && !deadline.Expired; slot++)
        {
            // Asked of the best order stacked, rather than of whatever the last rejected candidate
            // left behind, and of the card that would actually be relocated.
            Restack(bestOrder, top);

            if (!Crosses(bestOrder[slot], candidates))
            {
                continue;
            }

            for (var target = 0; target < column.Count && budget > 0 && !deadline.Expired; target++)
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

            var stacked = column.Sum(node => sizes[node].Y + GraphLayout.NodeSpacing);
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
        var original = best;
        var bestLevel = LevelOf(touching);

        if (best == 0)
        {
            refusedSlides[node] = new Refusal(changes.Count, Reach([node], CrossingSlideLimit));
            return false;
        }

        // Heights are deduplicated at whole-pixel resolution.
        var shifts = new List<float>();
        var seen = new HashSet<int>();

        void Consider(float shift)
        {
            if (Math.Abs(shift) <= CrossingSlideLimit && Math.Abs(shift) >= 0.5f && seen.Add((int)MathF.Round(shift)))
            {
                shifts.Add(shift);
            }
        }

        // Heights that run one of the card's wires level come first, so the whole-pixel
        // deduplication keeps them over nearby ladder rungs.
        foreach (var wire in touching)
        {
            Consider(wires[wire].From == node ? to[wire].Y - from[wire].Y : from[wire].Y - to[wire].Y);
        }

        for (var offset = CrossingSlideStep; offset <= CrossingSlideLimit; offset += CrossingSlideStep)
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
            var sweep = new WireSweep(theirs, mine, -CrossingSlideLimit, CrossingSlideLimit, SweepMargin);

            foreach (var other in nearWires[wire])
            {
                var moves = wires[other].From == node || wires[other].To == node;

                // Everything the wire crosses now lies inside its sweep too, so a partner clear of
                // the sweep's bounds needs nothing further.
                if (other == wire || (!moves && !sweep.Overlaps(bounds[other].MinX, bounds[other].MaxX, bounds[other].MinY, bounds[other].MaxY))
                    || wires[wire].SharesSocketWith(wires[other]))
                {
                    continue;
                }

                var crossing = Overlaps(wire, other) && GraphSegmentGeometry.SegmentsIntersect(from[wire], to[wire], from[other], to[other]);

                if (crossing)
                {
                    foreach (var target in (float[])[from[other].Y, to[other].Y])
                    {
                        Consider(target - mine.Y + CrossingClearance);
                        Consider(target - mine.Y - CrossingClearance);
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
            if (deadline.Expired)
            {
                break;
            }

            Move(node, originalY + shift);

            if (Blocked(node))
            {
                continue;
            }

            var score = settled + Sweeping(best - settled + 1);

            // Ties on crossings go to the height leaving more wires level, but a height that only
            // matches the current count is left to the snap.
            if (score > best || (score == best && score == original))
            {
                continue;
            }

            var level = LevelOf(touching);

            if (score < best || level > bestLevel)
            {
                best = score;
                bestLevel = level;
                bestY = originalY + shift;
            }
        }

        Move(node, bestY);

        if (bestY == originalY)
        {
            refusedSlides[node] = new Refusal(changes.Count, Reach([node], CrossingSlideLimit));
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

    private void Restack(List<int> order, float top)
    {
        var y = top;

        foreach (var node in order)
        {
            Move(node, y);
            y += sizes[node].Y + GraphLayout.NodeSpacing;
        }
    }

    /// <summary>The wires of both cards, deduplicated.</summary>
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

    /// <summary>
    /// The wires that could still cross <paramref name="subset"/> once its cards move by up to
    /// <paramref name="slack"/> vertically. Conservative, so the score it feeds is exact.
    /// </summary>
    /// <remarks>Returns a shared buffer that the next call overwrites.</remarks>
    private ReadOnlySpan<int> LocalCandidates(List<int> subset, float slack)
    {
        var left = float.MaxValue;
        var right = float.MinValue;
        var top = float.MaxValue;
        var bottom = float.MinValue;

        foreach (var wire in subset)
        {
            left = Math.Min(left, bounds[wire].MinX);
            right = Math.Max(right, bounds[wire].MaxX);
            top = Math.Min(top, bounds[wire].MinY);
            bottom = Math.Max(bottom, bounds[wire].MaxY);
        }

        top -= slack;
        bottom += slack;

        localScratch.Clear();

        for (var i = 0; i < wires.Length; i++)
        {
            if (bounds[i].MinX <= right && bounds[i].MaxX >= left && bounds[i].MinY <= bottom && bounds[i].MaxY >= top)
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
            if (other == wire || !Overlaps(wire, other) || wires[wire].SharesSocketWith(wires[other]))
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

        for (var i = 0; i < wires.Length && found.Count < budget && !deadline.Expired; i++)
        {
            foreach (var j in nearWires[i])
            {
                if (found.Count >= budget)
                {
                    break;
                }

                if (j <= i || !Overlaps(i, j) || wires[i].SharesSocketWith(wires[j]))
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
            refusedClears[node] = new Refusal(changes.Count, Reach([node], WireClearLimit));
            return false;
        }

        // Under everything, or over everything. The shorter move is tried first, but leaving on
        // one side can force this card's own wires across the very wire it is escaping, so both
        // directions get a turn before giving up.
        var originalY = positions[node].Y;
        var height = sizes[node].Y;
        var down = lowest + WireClearance - originalY;
        var up = highest - WireClearance - (originalY + height);

        var touching = incident[node];
        var before = CrossingsOf(touching);
        var covering = Covering(node);

        foreach (var shift in Math.Abs(down) <= Math.Abs(up) ? (float[])[down, up] : [up, down])
        {
            if (Math.Abs(shift) < 1f || Math.Abs(shift) > WireClearLimit)
            {
                continue;
            }

            Move(node, originalY + shift);

            // Taken only when the card lands clear of its neighbours, is genuinely out from under
            // wires it was under, and buys that with no crossing of its own.
            if (!Blocked(node) && Underlaps(node, out _, out _) is var after && after < offenders
                && after + Covering(node) < offenders + covering
                && Count(touching, before + 1) <= before)
            {
                Commit([node]);
                return true;
            }

            Move(node, originalY);
        }

        refusedClears[node] = new Refusal(changes.Count, Reach([node], WireClearLimit));
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
        var middle = positions[node].X + (size.X / 2f);

        lowest = float.MinValue;
        highest = float.MaxValue;
        var offenders = 0;

        foreach (var wire in nearCards[node])
        {
            if (wires[wire].From == node || wires[wire].To == node
                || !GraphSegmentGeometry.CrossesCard(from[wire], to[wire], positions[node], size))
            {
                continue;
            }

            var span = bounds[wire].MaxX - bounds[wire].MinX;
            var t = span > 0.01f ? MathUtils.Saturate((middle - bounds[wire].MinX) / span) : 0f;
            var lower = from[wire].X <= to[wire].X ? from[wire] : to[wire];
            var upper = from[wire].X <= to[wire].X ? to[wire] : from[wire];
            var crossing = lower.Y + ((upper.Y - lower.Y) * t);

            lowest = Math.Max(lowest, crossing);
            highest = Math.Min(highest, crossing);
            offenders++;
        }

        return offenders;
    }

    private bool IsLevel(int wire) => GraphLayout.RunsLevel(from[wire], to[wire]);

    private int LevelOf(List<int> subset)
    {
        var level = 0;

        foreach (var wire in subset)
        {
            if (IsLevel(wire))
            {
                level++;
            }
        }

        return level;
    }

    /// <summary>Cards other than their own ends that a card's wires pass across.</summary>
    private int Covering(int node)
    {
        var covering = 0;

        foreach (var wire in incident[node])
        {
            foreach (var card in nearWireCards[wire])
            {
                if (card == wires[wire].From || card == wires[wire].To)
                {
                    continue;
                }

                if (GraphSegmentGeometry.CrossesCard(from[wire], to[wire], positions[card], sizes[card]))
                {
                    covering++;
                }
            }
        }

        return covering;
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

        if (target != wires[wireB].To || wires[wireA].From == wires[wireB].From)
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

        // Parking a branch depends on the whole island.
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

        var shift = from[lowerDock].Y - from[upperDock].Y - WireClearance;

        // Sources sharing a column have to clear each other's cards, not just the sockets.
        if (columnOf[upperSource] == columnOf[lowerSource])
        {
            shift = Math.Min(shift, positions[lowerSource].Y - sizes[upperSource].Y - GraphLayout.NodeSpacing - positions[upperSource].Y);
        }

        var subset = Union(branchUpper);
        var before = CrossingsOf(subset);
        var originalHeights = branchUpper.Select(node => positions[node].Y).ToArray();

        // The shift that just clears the other source, then the one parking the branch clear of the island.
        foreach (var candidate in (float[])[shift, ParkingShift(branchUpper, above: shift < 0f)])
        {
            if (Math.Abs(candidate) < 1f || Math.Abs(candidate) > BranchShiftLimit)
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

        return above
            ? restEdge - GraphLayout.NodeSpacing - branchEdge
            : restEdge + GraphLayout.NodeSpacing - branchEdge;
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

            if (branch.Count > BranchShiftMaxNodes)
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

    private Box CardBox(int node)
        => new(positions[node].X, positions[node].X + sizes[node].X, positions[node].Y, positions[node].Y + sizes[node].Y);

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
                reach = reach.Union(bounds[wire]);
            }
        }

        var margin = travel + GraphLayout.NodeSpacing + 1f;
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
        Land(nodes);
        snap.Reindex(nodes);
    }

    private void Land(IReadOnlyCollection<int> nodes)
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
                if (other == wire || wires[wire].SharesSocketWith(wires[other]))
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
            landedMinY[wire] = bounds[wire].MinY;
            landedMaxY[wire] = bounds[wire].MaxY;
        }

        foreach (var node in nodes)
        {
            changes.Add(cardBoxes[node]);
            cardBoxes[node] = CardBox(node);
            changes.Add(cardBoxes[node]);

            foreach (var wire in incident[node])
            {
                changes.Add(wireBoxes[wire]);
                wireBoxes[wire] = bounds[wire];
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
        var then = GraphSegmentGeometry.BoxesOverlap(bounds[wire].MinX, bounds[wire].MaxX, landedMinY[wire], landedMaxY[wire],
                bounds[other].MinX, bounds[other].MaxX, landedMinY[other], landedMaxY[other])
            && GraphSegmentGeometry.SegmentsIntersect(landedFrom[wire], landedTo[wire], landedFrom[other], landedTo[other]);

        return (now ? 1 : 0) - (then ? 1 : 0);
    }

    /// <summary>Whether two wires overlap in both axes, and so could possibly cross.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Overlaps(int a, int b)
    {
        ref readonly var boxA = ref bounds[a];
        ref readonly var boxB = ref bounds[b];
        return GraphSegmentGeometry.BoxesOverlap(boxA.MinX, boxA.MaxX, boxA.MinY, boxA.MaxY, boxB.MinX, boxB.MaxX, boxB.MinY, boxB.MaxY);
    }
}
