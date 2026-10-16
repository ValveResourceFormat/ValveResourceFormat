using System.Linq;

namespace ValveResourceFormat.Graphs;

/// <summary>
/// Reroutes the forward wires that would be drawn across a card through the gaps between the cards
/// of the columns they pass, running level through each column and turning only in the gutters.
/// </summary>
/// <remarks>
/// Runs on final card positions and changes nothing but routes, so it can follow any placement.
/// A wire is only rerouted when the routed curve touches fewer cards than the one it replaces, and
/// adds at most <see cref="CrossingsPerCardCleared"/> crossings for every card it stops covering.
/// </remarks>
internal sealed class GraphWireRouter
{
    /// <summary>Gap a lane keeps from the cards of the column it runs through.</summary>
    private const float CardClearance = 10f;

    /// <summary>
    /// Pixels of climb a change of height costs on top of its size, so a route keeps one lane
    /// across several columns rather than stepping toward its target in every gutter.
    /// </summary>
    private const float StepPenalty = 60f;

    /// <summary>Crossings a rerouted wire may add for each card it no longer covers.</summary>
    private const int CrossingsPerCardCleared = 2;

    /// <summary>One column of cards sharing a left edge, with the cards ordered top to bottom.</summary>
    private sealed record Column(float Left, float Right, int[] Cards)
    {
        /// <summary>Heights of the lanes already taken by routed wires.</summary>
        public List<float> Lanes { get; } = [];
    }

    private readonly Vector2[] positions;
    private readonly Vector2[] sizes;
    private readonly GraphLayoutEdge[] edges;
    private readonly Vector2[]?[] routes;
    private readonly LayoutDeadline deadline;
    private readonly Column[] columns;

    private readonly List<Vector2> curve = [];
    private readonly List<(float Top, float Bottom)> blocked = [];
    private readonly HashSet<int> crossed = [];
    private readonly HashSet<int> touched = [];
    private readonly List<(int, int)> cells = [];

    /// <summary>Segments of every sampled wire by grid cell. An entry is stale once its wire's version moved on.</summary>
    private readonly Dictionary<(int, int), List<(int Wire, int Version, Vector2 A, Vector2 B)>> grid = [];
    private readonly int[] versions;

    /// <param name="positions">Top-left corner of each card.</param>
    /// <param name="sizes">Measured size of each card.</param>
    /// <param name="edges">The wires between the cards.</param>
    /// <param name="routes">Current route of each wire, indexed like <paramref name="edges"/>; updated in place.</param>
    /// <param name="deadline">When to stop rerouting and keep the routes reached so far.</param>
    public GraphWireRouter(Vector2[] positions, Vector2[] sizes, GraphLayoutEdge[] edges, Vector2[]?[] routes, LayoutDeadline deadline)
    {
        this.positions = positions;
        this.sizes = sizes;
        this.edges = edges;
        this.routes = routes;
        this.deadline = deadline;
        versions = new int[edges.Length];

        columns = [.. GraphLayout.GroupByColumn(positions, sizes.Length)
            .Select(group => new Column(
                group.Min(node => positions[node].X),
                group.Max(node => positions[node].X + sizes[node].X),
                [.. group.OrderBy(node => positions[node].Y)]))
            .OrderBy(static column => column.Left)];
    }

    /// <summary>
    /// Reroutes every forward wire that is drawn across a card and has a better way through, until
    /// the deadline passes.
    /// </summary>
    public void Run()
    {
        if (deadline.Expired)
        {
            return;
        }

        var curves = new Vector2[]?[edges.Length];

        for (var wire = 0; wire < edges.Length; wire++)
        {
            if (edges[wire].From != edges[wire].To)
            {
                GraphWireCurve.Sample(From(wire), routes[wire], To(wire), curve);
                curves[wire] = [.. curve];
                Insert(wire, curves[wire]!);
                TakeLanes(routes[wire]);
            }
        }

        for (var wire = 0; wire < edges.Length && !deadline.Expired; wire++)
        {
            var edge = edges[wire];

            if (edge.From == edge.To)
            {
                continue;
            }

            var from = From(wire);
            var to = To(wire);

            if (to.X <= from.X)
            {
                continue;
            }

            var hits = CardsTouched(edge, curves[wire]!);

            if (hits == 0)
            {
                continue;
            }

            if (Reroute(from, to, routes[wire]) is not { } waypoints)
            {
                continue;
            }

            GraphWireCurve.Sample(from, waypoints, to, curve);
            Vector2[] rerouted = [.. curve];

            var cleared = hits - CardsTouched(edge, rerouted);

            if (cleared <= 0)
            {
                continue;
            }

            var allowed = Crossings(wire, curves[wire]!, int.MaxValue) + (CrossingsPerCardCleared * cleared);

            if (Crossings(wire, rerouted, allowed + 1) > allowed)
            {
                continue;
            }

            ReleaseLanes(routes[wire]);
            routes[wire] = waypoints;
            curves[wire] = rerouted;
            versions[wire]++;
            Insert(wire, rerouted);
            TakeLanes(waypoints);
        }

        Vector2 From(int wire) => edges[wire].SourcePoint(positions);
        Vector2 To(int wire) => edges[wire].TargetPoint(positions);
    }

    private void TakeLanes(Vector2[]? route)
    {
        foreach (var (column, y) in LanesOf(route))
        {
            column.Lanes.Add(y);
        }
    }

    private void ReleaseLanes(Vector2[]? route)
    {
        foreach (var (column, y) in LanesOf(route))
        {
            column.Lanes.Remove(y);
        }
    }

    /// <summary>The columns a route runs into at their left edge, with the height it runs at.</summary>
    private IEnumerable<(Column Column, float Y)> LanesOf(Vector2[]? route)
    {
        foreach (var point in route ?? [])
        {
            var c = FirstReaching(point.X);

            if (c < columns.Length && GraphLayout.ColumnKey(columns[c].Left) == GraphLayout.ColumnKey(point.X))
            {
                yield return (columns[c], point.Y);
            }
        }
    }

    private void Insert(int wire, Vector2[] points)
    {
        for (var p = 1; p < points.Length; p++)
        {
            GraphGrid.BoxCells(Vector2.Min(points[p - 1], points[p]), Vector2.Max(points[p - 1], points[p]), cells);

            foreach (var cell in cells)
            {
                if (!grid.TryGetValue(cell, out var list))
                {
                    grid[cell] = list = [];
                }

                list.Add((wire, versions[wire], points[p - 1], points[p]));
            }
        }
    }

    /// <summary>
    /// How many other wires a curve drawn for <paramref name="wire"/> would cross, counted until
    /// the count reaches <paramref name="limit"/>.
    /// </summary>
    private int Crossings(int wire, Vector2[] points, int limit)
    {
        crossed.Clear();

        for (var p = 1; p < points.Length && crossed.Count < limit; p++)
        {
            GraphGrid.BoxCells(Vector2.Min(points[p - 1], points[p]), Vector2.Max(points[p - 1], points[p]), cells);

            foreach (var cell in cells)
            {
                if (!grid.TryGetValue(cell, out var list))
                {
                    continue;
                }

                foreach (var (other, version, a, b) in list)
                {
                    if (other == wire || version != versions[other] || crossed.Contains(other)
                        || edges[wire].SharesSocketWith(edges[other]))
                    {
                        continue;
                    }

                    if (GraphSegmentGeometry.SegmentsIntersect(points[p - 1], points[p], a, b))
                    {
                        crossed.Add(other);
                    }
                }
            }
        }

        return crossed.Count;
    }

    /// <summary>
    /// A level run through every column strictly between the two ends, at free heights picked so
    /// the wire climbs and drops as little and as seldom as it can. Each column offers the free
    /// heights nearest the line the wire takes now, nearest both ends, and nearest the heights
    /// offered by the column before it, so a lane can carry on level from one column to the next.
    /// </summary>
    private Vector2[]? Reroute(Vector2 from, Vector2 to, Vector2[]? current)
    {
        var first = FirstReaching(from.X);
        var passed = new List<Column>();

        for (var c = first; c < columns.Length && columns[c].Left < to.X; c++)
        {
            if (columns[c].Left > from.X)
            {
                passed.Add(columns[c]);
            }
        }

        if (passed.Count == 0)
        {
            return null;
        }

        var candidates = new List<float>[passed.Count];

        for (var c = 0; c < passed.Count; c++)
        {
            var column = passed[c];
            var seeds = new List<float> { IdealHeight(from, current, to, (column.Left + column.Right) / 2f), from.Y, to.Y };

            if (c > 0)
            {
                seeds.AddRange(candidates[c - 1]);
            }

            var found = new List<float>();
            BlockColumn(column);

            foreach (var seed in seeds)
            {
                foreach (var y in FreeHeightsNear(seed))
                {
                    if (!IsNear(found, y))
                    {
                        found.Add(y);
                    }
                }
            }

            candidates[c] = found;
        }

        var cost = new float[passed.Count][];
        var back = new int[passed.Count][];

        for (var c = 0; c < passed.Count; c++)
        {
            cost[c] = new float[candidates[c].Count];
            back[c] = new int[candidates[c].Count];

            for (var k = 0; k < candidates[c].Count; k++)
            {
                var y = candidates[c][k];

                if (c == 0)
                {
                    cost[c][k] = Climb(from.Y, y);
                    continue;
                }

                cost[c][k] = float.MaxValue;

                for (var p = 0; p < candidates[c - 1].Count; p++)
                {
                    var total = cost[c - 1][p] + Climb(candidates[c - 1][p], y);

                    if (total < cost[c][k])
                    {
                        cost[c][k] = total;
                        back[c][k] = p;
                    }
                }
            }
        }

        var last = passed.Count - 1;
        var pick = 0;

        for (var k = 1; k < candidates[last].Count; k++)
        {
            if (cost[last][k] + Climb(candidates[last][k], to.Y) < cost[last][pick] + Climb(candidates[last][pick], to.Y))
            {
                pick = k;
            }
        }

        var waypoints = new Vector2[passed.Count * 2];

        for (var c = last; c >= 0; c--)
        {
            var y = candidates[c][pick];
            waypoints[2 * c] = new Vector2(passed[c].Left, y);
            waypoints[(2 * c) + 1] = new Vector2(passed[c].Right, y);
            pick = back[c][pick];
        }

        return waypoints;
    }

    private static bool IsNear(List<float> heights, float y)
    {
        foreach (var other in heights)
        {
            if (Math.Abs(other - y) < 1f)
            {
                return true;
            }
        }

        return false;
    }

    private static float Climb(float from, float to)
        => Math.Abs(to - from) < 0.5f ? 0f : Math.Abs(to - from) + StepPenalty;

    /// <summary>Where the wire runs at <paramref name="x"/> along the straight segments through its current route.</summary>
    private static float IdealHeight(Vector2 from, Vector2[]? current, Vector2 to, float x)
    {
        var previous = from;

        foreach (var point in current ?? [])
        {
            if (point.X >= x)
            {
                return Lerp(previous, point);
            }

            previous = point;
        }

        return Lerp(previous, to);

        float Lerp(Vector2 a, Vector2 b) => b.X - a.X > 0.01f ? a.Y + ((b.Y - a.Y) * MathUtils.Saturate((x - a.X) / (b.X - a.X))) : b.Y;
    }

    /// <summary>Fills <see cref="blocked"/> with the sorted spans of a column that cards and lanes already take.</summary>
    private void BlockColumn(Column column)
    {
        blocked.Clear();

        foreach (var node in column.Cards)
        {
            blocked.Add((positions[node].Y - CardClearance, positions[node].Y + sizes[node].Y + CardClearance));
        }

        foreach (var lane in column.Lanes)
        {
            blocked.Add((lane - GraphLayout.DummyLaneHeight, lane + GraphLayout.DummyLaneHeight));
        }

        blocked.Sort();
    }

    /// <summary>
    /// The heights a wire could run level through the column <see cref="BlockColumn"/> last
    /// filled in, nearest to <paramref name="ideal"/>: the ideal itself when it is free, otherwise
    /// the closest free height above and below it.
    /// </summary>
    private List<float> FreeHeightsNear(float ideal)
    {
        var top = float.MinValue;
        var bottom = float.MinValue;

        foreach (var (start, end) in blocked)
        {
            if (start > bottom)
            {
                if (ideal >= top && ideal <= bottom)
                {
                    break;
                }

                top = start;
            }

            bottom = Math.Max(bottom, end);
        }

        if (ideal < top || ideal > bottom)
        {
            return [ideal];
        }

        return [top, bottom];
    }

    /// <summary>Index of the first column whose right edge reaches <paramref name="x"/>.</summary>
    private int FirstReaching(float x)
    {
        var low = 0;
        var high = columns.Length;

        while (low < high)
        {
            var mid = (low + high) / 2;

            if (columns[mid].Right < x)
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

    /// <summary>How many cards other than its own two a sampled wire is drawn across.</summary>
    private int CardsTouched(GraphLayoutEdge edge, Vector2[] points)
    {
        touched.Clear();

        for (var p = 1; p < points.Length; p++)
        {
            var a = points[p - 1];
            var b = points[p];
            var minX = Math.Min(a.X, b.X);
            var maxX = Math.Max(a.X, b.X);
            var minY = Math.Min(a.Y, b.Y);
            var maxY = Math.Max(a.Y, b.Y);

            for (var c = FirstReaching(minX); c < columns.Length && columns[c].Left <= maxX; c++)
            {
                foreach (var node in columns[c].Cards)
                {
                    if (node == edge.From || node == edge.To)
                    {
                        continue;
                    }

                    if (positions[node].Y > maxY)
                    {
                        break;
                    }

                    if (GraphSegmentGeometry.CrossesCard(a, b, positions[node], sizes[node]))
                    {
                        touched.Add(node);
                    }
                }
            }
        }

        return touched.Count;
    }
}
