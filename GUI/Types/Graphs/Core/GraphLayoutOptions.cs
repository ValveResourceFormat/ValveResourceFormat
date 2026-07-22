namespace GUI.Types.Graphs.Core;

/// <summary>
/// Individually toggleable layout improvements. Each flag targets one documented cause of
/// wire overlap; <see cref="GraphLayoutLab"/> enables them cumulatively so every stage can be
/// rendered and measured on its own.
/// </summary>
[Flags]
enum GraphLayoutFeature
{
    /// <summary>Placement exactly as it shipped: node-center alignment, no routing.</summary>
    None = 0,

    /// <summary>
    /// Align each node so its socket pivots line up with the pivots it wires to, instead of
    /// aligning node centers. Straight chains come out straight.
    /// </summary>
    PortAwareAlignment = 1 << 0,

    /// <summary>
    /// Give cycle-breaking wires an explicit route out of the source's right edge, along a
    /// clear channel and back into the target's left edge, instead of one long backward curve.
    /// </summary>
    BackWireRouting = 1 << 1,

    /// <summary>
    /// Spread the wires leaving or entering one socket so a fan does not draw as a single
    /// overlapping bundle.
    /// </summary>
    SocketFanSpread = 1 << 2,

    /// <summary>
    /// Normalized barycenter keys, alternating sweep direction, an adjacent-swap transpose
    /// pass and best-of-sweeps selection by measured crossing count.
    /// </summary>
    BarycentreRepair = 1 << 3,

    /// <summary>
    /// Sit the wrapped sub-columns of one oversized rank closer together than real rank
    /// boundaries, so wrapping costs the wires crossing it far less travel.
    /// </summary>
    RankPreservingWrap = 1 << 4,

    /// <summary>
    /// Insert a dummy node per intermediate rank for every wire spanning more than one rank,
    /// so long wires participate in ordering and route between cards instead of over them.
    /// </summary>
    LongWireDummies = 1 << 5,

    /// <summary>Order the dummy slots inside each gutter into lanes that do not cross.</summary>
    GutterLanes = 1 << 6,

    /// <summary>
    /// Permute the socket rows of nodes that opted in, ordering each side by the barycentre of
    /// what it connects to.
    /// </summary>
    PortOrdering = 1 << 7,

    /// <summary>
    /// Swap nodes vertically whenever doing so removes a crossing, judged on the wires actually
    /// drawn between socket pivots. This is the only pass that sees which socket a wire lands
    /// on, so it catches inversions every node-level heuristic is blind to.
    /// </summary>
    CrossingSwap = 1 << 8,

    /// <summary>Every improvement.</summary>
    All = PortAwareAlignment | BackWireRouting | SocketFanSpread | BarycentreRepair
        | RankPreservingWrap | LongWireDummies | GutterLanes | PortOrdering | CrossingSwap,
}

/// <summary>Layout tuning shared by the placement engines.</summary>
sealed class GraphLayoutOptions
{
    /// <summary>
    /// What a newly created <see cref="GraphView"/> starts with. Frontends lay their graph out
    /// from their own constructor, so anything wanting to compare layouts has to set this
    /// before the view is built; assigning afterwards would measure a graph the previous
    /// settings had already reordered.
    /// </summary>
    public static GraphLayoutOptions Default { get; set; } = new();

    public GraphLayoutFeature Features { get; set; } = GraphLayoutFeature.All;

    /// <summary>
    /// Horizontal gap between layers. Wider gutters cost canvas but buy back most of the wires
    /// that would otherwise be drawn across a card; this sits between the branch's original 220
    /// and the library's 400.
    /// </summary>
    public float LayerSpacing { get; set; } = 320f;

    /// <summary>Vertical gap between cards in one column.</summary>
    public float NodeSpacing { get; set; } = 44f;

    public bool Has(GraphLayoutFeature feature) => (Features & feature) == feature;

    /// <summary>Weight a solid wire pulls with during alignment.</summary>
    public float SolidWireWeight { get; set; } = 1f;

    /// <summary>
    /// Weight a dashed wire pulls with. Dashed means a secondary binding in every viewer
    /// (parameter reads, variable writes, state transitions), so it should not outvote the
    /// primary flow when the two disagree.
    /// </summary>
    public float DashedWireWeight { get; set; } = 0.25f;

    /// <summary>Horizontal step between successive wires leaving or entering one socket.</summary>
    public float SocketFanStep { get; set; } = 9f;

    /// <summary>
    /// Largest fan offset. The reach lengthens the curve's handles, so letting it grow without
    /// bound just bows the wires out across neighbouring cards.
    /// </summary>
    public float SocketFanLimit { get; set; } = 48f;

    /// <summary>Vertical room reserved for one long-wire dummy slot inside a gutter.</summary>
    public float DummyLaneHeight { get; set; } = 14f;

    /// <summary>How many times the crossing repair sweeps the graph before giving up.</summary>
    public int CrossingRepairPasses { get; set; } = 24;

    /// <summary>Furthest the repair may slide a single card to straighten one of its wires.</summary>
    public float CrossingSlideLimit { get; set; } = 90f;

    /// <summary>How far past a crossing wire's end a slide aims, so it clears rather than grazes.</summary>
    public float CrossingClearance { get; set; } = 12f;

    /// <summary>Step of the blind offset ladder a slide sweeps alongside the meaningful heights.</summary>
    public float CrossingSlideStep { get; set; } = 14f;

    /// <summary>
    /// Wall-clock milliseconds the whole repair may spend before it stops and keeps what it has.
    /// Zero removes the limit, which is what the manual full-quality command uses.
    /// </summary>
    public int CrossingRepairBudgetMs { get; set; } = 1000;

    /// <summary>
    /// How far beyond a column's own width its cards are checked against wires. A wire passing
    /// nowhere near a column cannot be crossed by moving that column's cards.
    /// </summary>
    public float CrossingNeighbourhood { get; set; } = 700f;

    /// <summary>Relocations tried per column per pass.</summary>
    public int CrossingReinsertBudget { get; set; } = 240;

    /// <summary>Up to this many wires, every move is scored against the whole graph.</summary>
    public int CrossingExactWireLimit { get; set; } = 4000;

    /// <summary>Largest island on which cards may be relocated by restacking their column.</summary>
    public int CrossingReinsertMaxNodes { get; set; } = 200;

    /// <summary>
    /// Most crossings examined per repair pass. Enumerating every crossing of a thousand-node
    /// graph and acting on all of them costs more than the remaining ones are worth.
    /// </summary>
    public int CrossingRepairBudget { get; set; } = 3000;
}
