namespace GUI.Types.Graphs.Core;

/// <summary>
/// Individually toggleable layout passes. Each flag targets one cause of wire overlap and can
/// be enabled on its own, so a pass can be scored in isolation.
/// </summary>
[Flags]
enum GraphLayoutFeature
{
    /// <summary>Plain layered placement: node-centre alignment, no routing, no repair.</summary>
    None = 0,

    /// <summary>
    /// Align each node so its socket pivots line up with the pivots it wires to, instead of
    /// aligning node centers. Straight chains come out straight.
    /// </summary>
    PortAwareAlignment = 1 << 0,

    /// <summary>
    /// Normalized barycenter keys, alternating sweep direction, an adjacent-swap transpose
    /// pass and best-of-sweeps selection by measured crossing count.
    /// </summary>
    BarycentreRepair = 1 << 1,

    /// <summary>
    /// Insert a dummy node per intermediate rank for every wire spanning more than one rank,
    /// so long wires participate in ordering and route between cards instead of over them. Suits
    /// one connected DAG; the frontends that have that shape opt in.
    /// </summary>
    LongWireDummies = 1 << 2,

    /// <summary>
    /// Move cards after placement wherever that removes a crossing, judged on the wires actually
    /// drawn between socket pivots. This is the only pass that sees which socket a wire lands
    /// on, so it catches inversions every node-level heuristic is blind to.
    /// </summary>
    CrossingSwap = 1 << 3,

    /// <summary>
    /// Rank each node by its nearest consumer rather than by its distance from a source, so a
    /// chain that feeds something far downstream sits beside what it feeds instead of at the
    /// left edge with one wire spanning the whole graph.
    /// </summary>
    TightenRanks = 1 << 4,

    /// <summary>The passes that apply to every graph shape.</summary>
    All = PortAwareAlignment | BarycentreRepair | CrossingSwap | TightenRanks,
}

/// <summary>Layout tuning shared by the placement engines.</summary>
sealed class GraphLayoutOptions
{
    /// <summary>
    /// What a newly created <see cref="GraphView"/> starts with. Frontends lay their graph out
    /// from their own constructor, so anything choosing non-default passes must set this before
    /// the view is built.
    /// </summary>
    public static GraphLayoutOptions Default { get; set; } = new();

    public GraphLayoutFeature Features { get; set; } = GraphLayoutFeature.All;

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
    /// Ranks a wire may span before <see cref="GraphLayoutFeature.TightenRanks"/> pulls its source
    /// forward. A rank or two of slack is the room the ordering and repair passes move cards in, so
    /// a many-island graph keeps it and closes only the long hauls. One connected DAG has the
    /// opposite balance and sets this to 1; see the animation graph viewers.
    /// </summary>
    public int TightenMinSpan { get; set; } = 2;

    public bool Has(GraphLayoutFeature feature) => (Features & feature) == feature;

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
    public int ClearCrossingTolerance { get; set; } = 1;

    /// <summary>Furthest a card may be moved to get out from under a wire.</summary>
    public float WireClearLimit { get; set; } = 260f;

    /// <summary>How far past a crossing wire's end a slide aims, so it clears rather than grazes.</summary>
    public float CrossingClearance { get; set; } = 12f;

    /// <summary>Step of the offset ladder a slide sweeps alongside the wire-aligned heights.</summary>
    public float CrossingSlideStep { get; set; } = 14f;

    /// <summary>
    /// Wall-clock milliseconds the whole repair may spend before it stops and keeps what it has.
    /// Zero removes the limit, which is what the manual full-quality command uses.
    /// </summary>
    public int CrossingRepairBudgetMs { get; set; } = 4000;

    /// <summary>
    /// Clock the budget is counted against, shared by every island of one layout so the budget
    /// covers the whole graph rather than each island separately.
    /// </summary>
    internal System.Diagnostics.Stopwatch? RepairClock { get; set; }

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
