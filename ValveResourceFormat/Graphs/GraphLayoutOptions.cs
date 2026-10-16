namespace ValveResourceFormat.Graphs;

/// <summary>
/// Wall-clock cutoff shared by the refining passes of one island's layout. A default instance
/// never expires, which is what the unlimited full-quality command runs with.
/// </summary>
readonly struct LayoutDeadline
{
    private readonly long expiresAt;

    private LayoutDeadline(long expiresAt)
    {
        this.expiresAt = expiresAt;
    }

    /// <summary>A deadline <paramref name="milliseconds"/> from now, or an unlimited one when that is not positive.</summary>
    public static LayoutDeadline After(int milliseconds) => milliseconds > 0
        ? new LayoutDeadline(System.Diagnostics.Stopwatch.GetTimestamp() + (milliseconds * System.Diagnostics.Stopwatch.Frequency / 1000))
        : default;

    public bool Expired => expiresAt != 0 && System.Diagnostics.Stopwatch.GetTimestamp() >= expiresAt;
}

/// <summary>
/// Shares one layout budget across islands laid out one after another. Each island is offered its
/// share of what is left when its turn comes, so the time a quick island leaves unused passes on to
/// the islands after it. Laying islands out smallest first hands that time to the biggest ones.
/// </summary>
/// <param name="nodeCounts">Node count of each island, in the order they will be laid out.</param>
/// <param name="totalMs">Milliseconds the whole layout may spend, or zero for unlimited.</param>
internal sealed class IslandBudget(IReadOnlyList<int> nodeCounts, int totalMs)
{
    private readonly long start = System.Diagnostics.Stopwatch.GetTimestamp();

    /// <summary>Milliseconds the island at <paramref name="index"/> may spend, or zero for unlimited.</summary>
    public int SliceFor(int index)
    {
        if (totalMs <= 0)
        {
            return 0;
        }

        var elapsed = (int)System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var remaining = Math.Max(1, totalMs - elapsed);
        var rest = new int[nodeCounts.Count - index];

        for (var i = 0; i < rest.Length; i++)
        {
            rest[i] = nodeCounts[index + i];
        }

        return GraphLayout.SplitBudget(rest, remaining)[0];
    }
}

/// <summary>Layout tuning for the placement engine.</summary>
internal sealed class GraphLayoutOptions
{
    /// <summary>
    /// Insert a dummy node per intermediate rank for every wire spanning more than one rank,
    /// so long wires participate in ordering and route between cards instead of over them. Suits
    /// one connected DAG; the frontends that have that shape opt in.
    /// </summary>
    public bool LongWireDummies { get; set; }

    /// <summary>
    /// Wall-clock milliseconds the layout may spend refining before it keeps what it has. Zero
    /// removes the limit. The caller shares it across islands with <see cref="IslandBudget"/>,
    /// and each island's share is split between placement and wire routing.
    /// </summary>
    public int LayoutBudgetMs { get; set; } = 4000;
}
