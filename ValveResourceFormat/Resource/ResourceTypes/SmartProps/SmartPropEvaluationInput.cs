namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// A ray cast request from a trace operation or the initial surface query.
    /// </summary>
    /// <param name="Start">Start point.</param>
    /// <param name="Direction">Unit direction.</param>
    /// <param name="Length">Maximum distance.</param>
    public readonly record struct SmartPropTraceQuery(Vector3 Start, Vector3 Direction, float Length);

    /// <summary>
    /// A ray cast hit.
    /// </summary>
    /// <param name="Position">Hit position.</param>
    /// <param name="Normal">Surface normal.</param>
    /// <param name="Material">Material name of the hit surface, null when unknown.</param>
    public readonly record struct SmartPropTraceHit(Vector3 Position, Vector3 Normal, string? Material);

    /// <summary>
    /// Inputs of a smart prop evaluation.
    /// </summary>
    public sealed class SmartPropEvaluationInput
    {
        /// <summary>Placement of the smart prop; output transforms are in the same space.</summary>
        public SmartPropTransform Placement { get; init; } = SmartPropTransform.Identity;

        /// <summary>Placement tint.</summary>
        public Color32 Tint { get; init; } = Color32.White;

        /// <summary>
        /// Seed of the master stream that new element seeds and random PickOne choices are drawn from.
        /// Hammer seeds it from the clock; any fixed value gives one stable variation.
        /// </summary>
        public int MasterSeed { get; init; }

        /// <summary>Variable values by name; a value applies only when it has the variable's type.</summary>
        public IReadOnlyDictionary<string, SmartPropValue>? VariableOverrides { get; init; }

        /// <summary>
        /// Values of exposed variables and the chosen option names of choices, keyed by element id. When set,
        /// exposed variables without an entry take their default.
        /// </summary>
        public IReadOnlyDictionary<int, SmartPropValue>? ParameterOverrides { get; init; }

        /// <summary>Stored element states: seeds, PickOne choices and handle edits.</summary>
        public IReadOnlyList<SmartPropElementState>? ElementStates { get; init; }

        /// <summary>Ray cast used by trace operations; without one every trace misses.</summary>
        public Func<SmartPropTraceQuery, SmartPropTraceHit?>? Trace { get; init; }
    }
}
