namespace GUI.Types.Graphs.Core;

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

/// <summary>Layout tuning for the placement engine.</summary>
sealed class GraphLayoutOptions
{
    /// <summary>
    /// Insert a dummy node per intermediate rank for every wire spanning more than one rank,
    /// so long wires participate in ordering and route between cards instead of over them. Suits
    /// one connected DAG; the frontends that have that shape opt in.
    /// </summary>
    public bool LongWireDummies { get; set; }

    /// <summary>
    /// Horizontal gap between layers. Wider gutters cost canvas and buy back wires that would
    /// otherwise be drawn across a card.
    /// </summary>
    public float LayerSpacing { get; set; } = 320f;

    /// <summary>
    /// Vertical gap between cards in one column. Also the room the repair has to work in: it may
    /// only move a card to a position that clears its neighbours, so this bounds how far any card
    /// can travel to undo a crossing or get out from under a wire.
    /// </summary>
    public float NodeSpacing { get; set; } = 60f;

    /// <summary>
    /// Ranks a wire may span before the rank-tightening pass pulls its source
    /// forward. A rank or two of slack is the room the ordering and repair passes move cards in, so
    /// a many-island graph keeps it and closes only the long hauls. One connected DAG has the
    /// opposite balance and sets this to 1; see the animation graph viewers.
    /// </summary>
    public int TightenMinSpan { get; set; } = 2;

    /// <summary>Weight a solid wire pulls with during alignment.</summary>
    public float SolidWireWeight { get; set; } = 1f;

    /// <summary>
    /// Weight a dashed wire pulls with. Dashed means a secondary binding in every viewer
    /// (parameter reads, variable writes, state transitions), so it should not outvote the
    /// primary flow when the two disagree.
    /// </summary>
    public float DashedWireWeight { get; set; } = 0.25f;

    /// <summary>Vertical room reserved for one long-wire dummy slot inside a gutter.</summary>
    public float DummyLaneHeight { get; set; } = 14f;

    /// <summary>How many times the crossing repair sweeps the graph before giving up.</summary>
    public int CrossingRepairPasses { get; set; } = 24;

    /// <summary>Furthest the repair may slide a single card to straighten one of its wires.</summary>
    public float CrossingSlideLimit { get; set; } = 90f;

    /// <summary>Margin left between a card and the wire it is moved clear of.</summary>
    public float WireClearance { get; set; } = 32f;

    /// <summary>
    /// Crossings a card may add by moving out from under a wire. At zero a card stays under a wire
    /// whenever escaping it would put its own wires across something else.
    /// </summary>
    public int ClearCrossingTolerance { get; set; }

    /// <summary>Furthest a card may be moved to get out from under a wire.</summary>
    public float WireClearLimit { get; set; } = 260f;

    /// <summary>How far past a crossing wire's end a slide aims, so it clears rather than grazes.</summary>
    public float CrossingClearance { get; set; } = 12f;

    /// <summary>Step of the offset ladder a slide sweeps alongside the wire-aligned heights.</summary>
    public float CrossingSlideStep { get; set; } = 14f;

    /// <summary>
    /// Wall-clock milliseconds the whole layout may spend refining before it stops and keeps what
    /// it has. Covers both the ordering sweeps and the crossing repair, which share one deadline
    /// per island. Zero removes the limit, which is what the manual full-quality command uses. The
    /// caller splits it across the islands with <see cref="GraphLayout.SplitBudget"/>.
    /// </summary>
    public int LayoutBudgetMs { get; set; } = 4000;

    /// <summary>
    /// Milliseconds the island being laid out right now may spend, timed from when its own layout
    /// starts so no island can eat the time meant for the ones after it. Null lets the island take
    /// the whole of <see cref="LayoutBudgetMs"/>; zero means unlimited, as it does there.
    /// </summary>
    internal int? LayoutSliceMs { get; set; }

    /// <summary>Largest branch that may be shifted as one to reorder two wires into a card.</summary>
    public int BranchShiftMaxNodes { get; set; } = 40;

    /// <summary>Furthest a branch may be shifted vertically.</summary>
    public float BranchShiftLimit { get; set; } = 600f;

    /// <summary>Relocations tried per column per pass.</summary>
    public int CrossingReinsertBudget { get; set; } = 240;

    /// <summary>Largest island on which cards may be relocated by restacking their column.</summary>
    public int CrossingReinsertMaxNodes { get; set; } = 200;

    /// <summary>
    /// Most crossings examined per repair pass. Enumerating every crossing of a thousand-node
    /// graph and acting on all of them costs more than the remaining ones are worth.
    /// </summary>
    public int CrossingRepairBudget { get; set; } = 3000;
}
