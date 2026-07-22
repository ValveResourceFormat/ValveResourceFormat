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
    /// Normalized barycenter keys, alternating sweep direction, an adjacent-swap transpose
    /// pass and best-of-sweeps selection by measured crossing count.
    /// </summary>
    BarycentreRepair = 1 << 1,

    /// <summary>
    /// Insert a dummy node per intermediate rank for every wire spanning more than one rank,
    /// so long wires participate in ordering and route between cards instead of over them.
    /// Worth it on one connected animation graph and harmful on many-island entity graphs, so
    /// the frontends choose rather than it being on by default.
    /// </summary>
    LongWireDummies = 1 << 2,

    /// <summary>
    /// Move cards after placement wherever that removes a crossing, judged on the wires actually
    /// drawn between socket pivots. This is the only pass that sees which socket a wire lands
    /// on, so it catches inversions every node-level heuristic is blind to.
    /// </summary>
    CrossingSwap = 1 << 3,

    /// <summary>Everything that earns its place on every graph shape.</summary>
    All = PortAwareAlignment | BarycentreRepair | CrossingSwap,
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
    public int CrossingRepairBudgetMs { get; set; } = 4000;

    /// <summary>
    /// Clock the budget is measured against, shared by every island of one layout. Without this
    /// each island would start its own, so a map with a hundred islands would be allowed a
    /// hundred times the budget rather than the one the caller asked for.
    /// </summary>
    internal System.Diagnostics.Stopwatch? RepairClock { get; set; }

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
