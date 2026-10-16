using System.Linq;

namespace ValveResourceFormat.Graphs;

/// <summary>
/// Moves placed cards to heights that draw more of their wires level. A move is only taken when
/// the cards land clear of their neighbours, their wires run across no more cards than before, and
/// they add at most the given number of crossings per wire levelled. Crossings and covered cards
/// are judged on the straight run between the socket pivots.
/// </summary>
/// <remarks>
/// Candidate heights come from a target per card, normally the solved placement, which may be
/// taken at no gain, and from the height that levels each wire of a card against where its other
/// end sits now, which has to level something more and keep the island within its current top and
/// bottom. Groups of cards whose wires are all level at their targets move together first. A card
/// carries along the cards its level wires join it to and the cards it would land on, so levelling
/// one wire does not cost the ones already level. Wires are bucketed on a grid along their length
/// and cards over their box, and a move re-buckets only what it touched. A card that found no move
/// is not tried again until something near it moves.
/// </remarks>
internal sealed class CardSnap
{
    /// <summary>Most cards one card may carry along when it moves.</summary>
    private const int MaxCarried = 40;

    /// <summary>Most cards moved as one group.</summary>
    private const int MaxGroup = 64;

    private readonly Vector2[] positions;
    private readonly Vector2[] sizes;
    private readonly GraphLayoutEdge[] edges;
    private readonly LayoutDeadline deadline;
    private readonly float crossingsPerLevelWire;
    private readonly int rounds;

    private readonly List<int>[] incident;
    private readonly int[] wireVersion;
    private readonly int[] cardVersion;
    private readonly Dictionary<(int, int), List<(int Id, int Version)>> wireGrid = [];
    private readonly Dictionary<(int, int), List<(int Id, int Version)>> cardGrid = [];

    private readonly int[] wireSeen;
    private readonly int[] cardSeen;
    private int stamp;

    /// <summary>Cards being moved by the move under evaluation; their grid entries are where they were.</summary>
    private readonly HashSet<int> moving = [];
    private readonly List<int> movingWires = [];
    private readonly HashSet<int> movingWireSet = [];
    private readonly List<int> found = [];
    private readonly List<(int, int)> cells = [];

    /// <summary>The card whose crossings and covered count where it stands are cached below, or -1.</summary>
    private int cachedCard = -1;
    private int? cachedCrossings;
    private int? cachedCovered;

    private (float Top, float Bottom) extent;
    private bool extentStale = true;

    /// <summary>Cards tried since anything near them last moved, which would be refused again.</summary>
    private readonly bool[] settled;

    /// <summary>Where each card was when it was last bucketed.</summary>
    private readonly Vector2[] indexedCards;
    private readonly HashSet<(int, int)> disturbed = [];

    /// <summary>Raised with the cards a move has just landed.</summary>
    public Action<List<int>>? Landed { get; init; }

    /// <param name="positions">Top-left corner of each card, updated in place.</param>
    /// <param name="sizes">Measured size of each card.</param>
    /// <param name="edges">The wires between the cards.</param>
    /// <param name="deadline">When to stop moving cards.</param>
    /// <param name="crossingsPerLevelWire">Crossings a move may add for every wire it levels.</param>
    /// <param name="rounds">Sweeps over the cards made at most per <see cref="Run"/>.</param>
    public CardSnap(Vector2[] positions, Vector2[] sizes, GraphLayoutEdge[] edges, LayoutDeadline deadline, float crossingsPerLevelWire, int rounds)
    {
        this.positions = positions;
        this.sizes = sizes;
        this.edges = edges;
        this.deadline = deadline;
        this.crossingsPerLevelWire = crossingsPerLevelWire;
        this.rounds = rounds;

        incident = GraphLayout.IncidentWires(sizes.Length, edges);
        wireVersion = new int[edges.Length];
        cardVersion = new int[sizes.Length];
        wireSeen = new int[edges.Length];
        cardSeen = new int[sizes.Length];
        settled = new bool[sizes.Length];
        indexedCards = new Vector2[sizes.Length];

        for (var i = 0; i < sizes.Length; i++)
        {
            IndexCard(i);
        }

        for (var i = 0; i < edges.Length; i++)
        {
            if (edges[i].From != edges[i].To)
            {
                IndexWire(i);
            }
        }
    }

    /// <summary>
    /// Moves cards towards <paramref name="targets"/> and onto heights that level their wires, until
    /// nothing more moves.
    /// </summary>
    /// <param name="targets">Top edge each card is drawn towards, or null to only level wires.</param>
    /// <returns>Whether any card moved.</returns>
    public bool Run(float[]? targets)
    {
        var candidates = new List<(float Height, int Partner)>();
        var group = new List<int>(1);
        var any = false;

        for (var round = 0; round < rounds && !deadline.Expired; round++)
        {
            var moved = false;

            if (targets != null)
            {
                foreach (var members in Groups(targets))
                {
                    if (deadline.Expired)
                    {
                        break;
                    }

                    moved |= TryMove(members, [.. members.Select(node => targets[node])], allowEven: false);
                }
            }

            for (var node = 0; node < sizes.Length && !deadline.Expired; node++)
            {
                if (settled[node])
                {
                    continue;
                }

                candidates.Clear();

                if (targets != null)
                {
                    candidates.Add((targets[node], -1));
                }

                foreach (var wire in incident[node])
                {
                    var (mine, theirs, partner) = edges[wire].From == node
                        ? (edges[wire].FromPivot.Y, positions[edges[wire].To].Y + edges[wire].ToPivot.Y, edges[wire].To)
                        : (edges[wire].ToPivot.Y, positions[edges[wire].From].Y + edges[wire].FromPivot.Y, edges[wire].From);
                    candidates.Add((theirs - mine, partner));
                }

                List<int>? bestNodes = null;
                float[] bestHeights = [];
                (int Gain, int Added) best = (-1, 0);

                foreach (var (candidate, partner) in candidates)
                {
                    var allowEven = partner < 0;

                    if (Carried(node, partner, candidate - positions[node].Y) is { } carried
                        && Evaluate(carried.Nodes, carried.Heights, allowEven, bounded: true) is { } outcome)
                    {
                        if (Better(outcome, best))
                        {
                            bestNodes = carried.Nodes;
                            bestHeights = carried.Heights;
                            best = outcome;
                        }

                        continue;
                    }

                    group.Clear();
                    group.Add(node);

                    if (Evaluate(group, [candidate], allowEven, !allowEven) is { } single && Better(single, best))
                    {
                        bestNodes = [node];
                        bestHeights = [candidate];
                        best = single;
                    }
                }

                if (bestNodes != null)
                {
                    Commit(bestNodes, bestHeights);
                    moved = true;
                }
                else
                {
                    settled[node] = true;
                }
            }

            any |= moved;

            if (!moved)
            {
                break;
            }
        }

        return any;
    }

    /// <summary>
    /// Cards joined by wires that are level at both ends' targets, among the cards away from their
    /// target, as groups of two or more.
    /// </summary>
    private List<List<int>> Groups(float[] targets)
    {
        var joined = new DisjointSets(sizes.Length);

        for (var wire = 0; wire < edges.Length; wire++)
        {
            var (from, to) = (edges[wire].From, edges[wire].To);

            if (from != to && Away(from) && Away(to)
                && MathF.Abs(targets[from] + edges[wire].FromPivot.Y - targets[to] - edges[wire].ToPivot.Y) <= GraphLayout.LevelTolerance)
            {
                joined.Union(from, to);
            }
        }

        var groups = new Dictionary<int, List<int>>();

        for (var node = 0; node < sizes.Length; node++)
        {
            if (Away(node))
            {
                var key = joined.Find(node);

                if (!groups.TryGetValue(key, out var members))
                {
                    groups[key] = members = [];
                }

                members.Add(node);
            }
        }

        return [.. groups.Values.Where(static members => members.Count is > 1 and <= MaxGroup)];

        bool Away(int node) => MathF.Abs(positions[node].Y - targets[node]) >= GraphLayout.LevelTolerance;
    }

    /// <summary>
    /// A card moved by <paramref name="delta"/> together with every card joined to it by a level
    /// wire and every card it would land on, and so on from each of those, all moved by the same
    /// amount. Null when that takes in <paramref name="partner"/>, grows past
    /// <see cref="MaxCarried"/> cards, or is the card alone.
    /// </summary>
    private (List<int> Nodes, float[] Heights)? Carried(int node, int partner, float delta)
    {
        var nodes = new List<int> { node };
        moving.Clear();
        moving.Add(node);

        for (var i = 0; i < nodes.Count; i++)
        {
            var member = nodes[i];

            foreach (var wire in incident[member])
            {
                var (from, to) = Ends(wire);

                if (GraphLayout.RunsLevel(from, to) && !Take(edges[wire].From == member ? edges[wire].To : edges[wire].From))
                {
                    return null;
                }
            }

            var min = positions[member] with { Y = positions[member].Y + delta };
            var max = min + sizes[member];
            GraphGrid.BoxCells(min with { Y = min.Y - GraphLayout.MinimumGap }, max with { Y = max.Y + GraphLayout.MinimumGap }, cells);
            QueryCards();

            foreach (var other in found)
            {
                if (!GraphLayout.CardsClear(min, max, positions[other], positions[other] + sizes[other], GraphLayout.MinimumGap) && !Take(other))
                {
                    return null;
                }
            }
        }

        return nodes.Count == 1 ? null : (nodes, [.. nodes.Select(n => positions[n].Y + delta)]);

        bool Take(int other)
        {
            if (moving.Contains(other))
            {
                return true;
            }

            if (other == partner || nodes.Count >= MaxCarried)
            {
                return false;
            }

            nodes.Add(other);
            moving.Add(other);
            return true;
        }
    }

    private static bool Better((int Gain, int Added) outcome, (int Gain, int Added) best)
        => outcome.Gain > best.Gain || (outcome.Gain == best.Gain && outcome.Added < best.Added);

    private bool TryMove(List<int> nodes, float[] heights, bool allowEven)
    {
        if (Evaluate(nodes, heights, allowEven) is null)
        {
            return false;
        }

        Commit(nodes, heights);
        return true;
    }

    /// <summary>
    /// How many more of the moving cards' wires run level once <paramref name="nodes"/> are at
    /// <paramref name="heights"/>, or null when the move is refused. Leaves the cards where they were.
    /// </summary>
    private (int Gain, int Added)? Evaluate(List<int> nodes, float[] heights, bool allowEven, bool bounded = false)
    {
        BeginMove(nodes);

        var original = new float[nodes.Count];
        var changed = false;

        for (var i = 0; i < nodes.Count; i++)
        {
            original[i] = positions[nodes[i]].Y;
            changed |= MathF.Abs(heights[i] - original[i]) >= GraphLayout.LevelTolerance;
        }

        if (!changed || (bounded && !StaysWithin(nodes, heights)))
        {
            return null;
        }

        var levelBefore = LevelWires();
        Place(heights);
        var gain = LevelWires() - levelBefore;

        var blocked = (allowEven ? gain < 0 : gain <= 0) || AnyBlocked();
        Place(original);

        if (blocked)
        {
            return null;
        }

        var single = nodes.Count == 1;

        if (single && cachedCard != nodes[0])
        {
            cachedCard = nodes[0];
            cachedCrossings = null;
            cachedCovered = null;
        }

        var crossingsBefore = single ? cachedCrossings ??= Crossings(int.MaxValue) : Crossings(int.MaxValue);
        var allowed = crossingsBefore + (int)(gain * crossingsPerLevelWire);
        var coveredBefore = single ? cachedCovered ??= Covered(int.MaxValue) : Covered(int.MaxValue);

        Place(heights);
        var crossingsAfter = Crossings(allowed + 1);
        var refused = crossingsAfter > allowed || Covered(coveredBefore + 1) > coveredBefore;
        Place(original);

        return refused ? null : (gain, crossingsAfter - crossingsBefore);

        void Place(float[] ys)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                positions[nodes[i]] = positions[nodes[i]] with { Y = ys[i] };
            }
        }
    }

    /// <summary>Whether the cards at <paramref name="heights"/> stay inside the island's current top and bottom.</summary>
    private bool StaysWithin(List<int> nodes, float[] heights)
    {
        if (extentStale)
        {
            extent = (float.MaxValue, float.MinValue);

            for (var node = 0; node < sizes.Length; node++)
            {
                extent = (Math.Min(extent.Top, positions[node].Y), Math.Max(extent.Bottom, positions[node].Y + sizes[node].Y));
            }

            extentStale = false;
        }

        for (var i = 0; i < nodes.Count; i++)
        {
            if (heights[i] < extent.Top || heights[i] + sizes[nodes[i]].Y > extent.Bottom)
            {
                return false;
            }
        }

        return true;
    }

    private void Commit(List<int> nodes, float[] heights)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            positions[nodes[i]] = positions[nodes[i]] with { Y = heights[i] };
        }

        Reindex(nodes);
        Landed?.Invoke(nodes);
    }

    /// <summary>
    /// Re-buckets cards that moved since they were indexed, with their wires, and has the cards at
    /// both ends of those wires and every card and wire end near where the cards were or now are
    /// tried again.
    /// </summary>
    public void Reindex(IReadOnlyCollection<int> nodes)
    {
        cachedCard = -1;
        extentStale = true;
        disturbed.Clear();

        foreach (var node in nodes)
        {
            Disturb(indexedCards[node], sizes[node]);
            cardVersion[node]++;
            IndexCard(node);
            Disturb(positions[node], sizes[node]);
        }

        BeginMove(nodes);

        foreach (var wire in movingWires)
        {
            wireVersion[wire]++;
            IndexWire(wire);
            settled[edges[wire].From] = false;
            settled[edges[wire].To] = false;
        }

        moving.Clear();
        movingWireSet.Clear();
        cells.Clear();
        cells.AddRange(disturbed);
        QueryCards();

        foreach (var card in found)
        {
            settled[card] = false;
        }

        QueryWires();

        foreach (var wire in found)
        {
            settled[edges[wire].From] = false;
            settled[edges[wire].To] = false;
        }

        void Disturb(Vector2 min, Vector2 size)
        {
            GraphGrid.BoxCells(min, min + size, cells);
            disturbed.UnionWith(cells);
        }
    }

    private void BeginMove(IEnumerable<int> nodes)
    {
        moving.Clear();
        movingWires.Clear();
        movingWireSet.Clear();

        foreach (var node in nodes)
        {
            moving.Add(node);

            foreach (var wire in incident[node])
            {
                if (movingWireSet.Add(wire))
                {
                    movingWires.Add(wire);
                }
            }
        }
    }

    private (Vector2 From, Vector2 To) Ends(int wire) => (edges[wire].SourcePoint(positions), edges[wire].TargetPoint(positions));

    /// <summary>Forward wires of the moving cards that run level.</summary>
    private int LevelWires()
    {
        var level = 0;

        foreach (var wire in movingWires)
        {
            var (from, to) = Ends(wire);

            if (GraphLayout.RunsLevel(from, to))
            {
                level++;
            }
        }

        return level;
    }

    private bool AnyBlocked()
    {
        foreach (var node in moving)
        {
            var min = positions[node];
            var max = min + sizes[node];

            foreach (var other in moving)
            {
                if (other != node && !GraphLayout.CardsClear(min, max, positions[other], positions[other] + sizes[other], GraphLayout.MinimumGap))
                {
                    return true;
                }
            }

            GraphGrid.BoxCells(min with { Y = min.Y - GraphLayout.MinimumGap }, max with { Y = max.Y + GraphLayout.MinimumGap }, cells);
            QueryCards();

            foreach (var other in found)
            {
                if (!GraphLayout.CardsClear(min, max, positions[other], positions[other] + sizes[other], GraphLayout.MinimumGap))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Crossings of the moving cards' wires with every wire, counting a pair of two moving wires from both sides.</summary>
    private int Crossings(int limit)
    {
        var crossings = 0;

        foreach (var a in movingWires)
        {
            if (crossings >= limit)
            {
                break;
            }

            var (a0, a1) = Ends(a);
            GraphGrid.SegmentCells(a0, a1, cells);
            QueryWires();
            found.AddRange(movingWires);

            foreach (var b in found)
            {
                if (a == b || edges[a].SharesSocketWith(edges[b]))
                {
                    continue;
                }

                var (b0, b1) = Ends(b);

                if (GraphSegmentGeometry.BoxesOverlap(Math.Min(a0.X, a1.X), Math.Max(a0.X, a1.X), Math.Min(a0.Y, a1.Y), Math.Max(a0.Y, a1.Y),
                        Math.Min(b0.X, b1.X), Math.Max(b0.X, b1.X), Math.Min(b0.Y, b1.Y), Math.Max(b0.Y, b1.Y))
                    && GraphSegmentGeometry.SegmentsIntersect(a0, a1, b0, b1))
                {
                    crossings++;
                }
            }
        }

        return crossings;
    }

    /// <summary>Wires drawn across the moving cards, plus cards the moving cards' wires are drawn across.</summary>
    private int Covered(int limit)
    {
        var covered = 0;

        foreach (var node in moving)
        {
            if (covered >= limit)
            {
                return covered;
            }

            GraphGrid.BoxCells(positions[node], positions[node] + sizes[node], cells);
            QueryWires();
            found.AddRange(movingWires);

            foreach (var wire in found)
            {
                if (edges[wire].From != node && edges[wire].To != node)
                {
                    var (a, b) = Ends(wire);

                    if (GraphSegmentGeometry.CrossesCard(a, b, positions[node], sizes[node]))
                    {
                        covered++;
                    }
                }
            }
        }

        foreach (var wire in movingWires)
        {
            if (covered >= limit)
            {
                break;
            }

            var (a, b) = Ends(wire);
            GraphGrid.SegmentCells(a, b, cells);
            QueryCards();
            found.AddRange(moving);

            foreach (var other in found)
            {
                if (other != edges[wire].From && other != edges[wire].To
                    && GraphSegmentGeometry.CrossesCard(a, b, positions[other], sizes[other]))
                {
                    covered++;
                }
            }
        }

        return covered;
    }

    private void IndexWire(int wire)
    {
        var (a, b) = Ends(wire);
        GraphGrid.SegmentCells(a, b, cells);
        Insert(wireGrid, wire, wireVersion[wire]);
    }

    private void IndexCard(int node)
    {
        indexedCards[node] = positions[node];
        GraphGrid.BoxCells(positions[node], positions[node] + sizes[node], cells);
        Insert(cardGrid, node, cardVersion[node]);
    }

    private void Insert(Dictionary<(int, int), List<(int Id, int Version)>> grid, int id, int version)
    {
        foreach (var cell in cells)
        {
            if (!grid.TryGetValue(cell, out var list))
            {
                grid[cell] = list = [];
            }

            list.Add((id, version));
        }
    }

    /// <summary>Fills <see cref="found"/> with the wires, other than the moving ones, bucketed in any of <see cref="cells"/>.</summary>
    private void QueryWires() => Query(wireGrid, wireVersion, wireSeen, movingWireSet);

    /// <summary>Fills <see cref="found"/> with the cards, other than the moving ones, bucketed in any of <see cref="cells"/>.</summary>
    private void QueryCards() => Query(cardGrid, cardVersion, cardSeen, moving);

    private void Query(Dictionary<(int, int), List<(int Id, int Version)>> grid, int[] versions, int[] seen, HashSet<int> skip)
    {
        found.Clear();
        stamp++;

        foreach (var cell in cells)
        {
            if (!grid.TryGetValue(cell, out var list))
            {
                continue;
            }

            foreach (var (id, version) in list)
            {
                if (version == versions[id] && seen[id] != stamp && !skip.Contains(id))
                {
                    seen[id] = stamp;
                    found.Add(id);
                }
            }
        }
    }
}
