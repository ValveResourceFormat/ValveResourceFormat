using System.Collections.ObjectModel;

namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// Surfaces a ray cast should pass through.
    /// </summary>
    [Flags]
    public enum SmartPropTraceFlags
    {
        /// <summary>Hit everything.</summary>
        None = 0,
        /// <summary>Ignore surfaces with tool materials.</summary>
        IgnoreToolMaterials = 1 << 0,
        /// <summary>Ignore sky surfaces.</summary>
        IgnoreSky = 1 << 1,
        /// <summary>Ignore nodraw surfaces.</summary>
        IgnoreNoDraw = 1 << 2,
        /// <summary>Ignore translucent surfaces.</summary>
        IgnoreTranslucent = 1 << 3,
        /// <summary>Ignore models.</summary>
        IgnoreModels = 1 << 4,
        /// <summary>Ignore entities.</summary>
        IgnoreEntities = 1 << 5,
        /// <summary>Ignore cables.</summary>
        IgnoreCables = 1 << 6,
    }

    /// <summary>
    /// A ray cast request from a trace operation or the initial surface query.
    /// </summary>
    /// <param name="Start">Start point.</param>
    /// <param name="Direction">Unit direction.</param>
    /// <param name="Length">Maximum distance.</param>
    /// <param name="Flags">Surfaces the ray passes through.</param>
    public readonly record struct SmartPropTraceQuery(Vector3 Start, Vector3 Direction, float Length, SmartPropTraceFlags Flags = SmartPropTraceFlags.None);

    /// <summary>
    /// A ray cast hit.
    /// </summary>
    /// <param name="Position">Hit position.</param>
    /// <param name="Normal">Surface normal.</param>
    /// <param name="Material">Material name of the hit surface, null when unknown.</param>
    public readonly record struct SmartPropTraceHit(Vector3 Position, Vector3 Normal, string? Material);

    /// <summary>
    /// A change to the stored choice of a PickOne element, as made by the Hammer configuration helper.
    /// </summary>
    public enum SmartPropChoiceAction
    {
        /// <summary>Keep the stored choice.</summary>
        None,
        /// <summary>Draw a new choice; only RANDOM PickOne elements redraw.</summary>
        Randomize,
        /// <summary>Choose the next candidate, wrapping around.</summary>
        Next,
        /// <summary>Choose the previous candidate, wrapping around.</summary>
        Previous,
        /// <summary>Choose no child.</summary>
        ChooseNone,
    }

    /// <summary>
    /// A requested change to the choice of the PickOne element at <see cref="ElementPath"/>.
    /// </summary>
    /// <param name="ElementPath">Element path of the PickOne element; an empty path applies to every PickOne element.</param>
    /// <param name="Action">The change.</param>
    public readonly record struct SmartPropChoiceRequest(int[] ElementPath, SmartPropChoiceAction Action);

    /// <summary>
    /// A knot of an input path. The tangents are offsets from <see cref="Position"/> to the Bezier handles of the
    /// segments before and after the knot.
    /// </summary>
    /// <param name="Position">Knot position in path space.</param>
    /// <param name="InTangent">Offset to the handle of the segment ending at the knot.</param>
    /// <param name="OutTangent">Offset to the handle of the segment starting at the knot.</param>
    public readonly record struct SmartPropPathKnot(Vector3 Position, Vector3 InTangent, Vector3 OutTangent);

    /// <summary>
    /// A named path shape attached to a placed smart prop (a Hammer path object), read by PlaceOnPath elements
    /// whose path name matches.
    /// </summary>
    public sealed class SmartPropInputPath
    {
        /// <summary>Name that PlaceOnPath elements refer to, matched case-insensitively.</summary>
        public required string Name { get; init; }

        /// <summary>Path space to the space of <see cref="SmartPropEvaluationInput.Placement"/>.</summary>
        public SmartPropTransform Transform { get; init; } = SmartPropTransform.Identity;

        /// <summary>Knots in path space; a path with fewer than two knots places nothing.</summary>
        public required IReadOnlyList<SmartPropPathKnot> Knots { get; init; }
    }

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
        /// Values of exposed variables and the chosen option names of choices, keyed by element id, for the
        /// evaluated smart prop (nested smart props never use them). Hammer always passes such a set, which is the
        /// default: exposed variables without an entry take their default, ignoring <see cref="VariableOverrides"/>,
        /// and every choice applies its entry or else its default option. Null models a host that passes no set:
        /// exposed variables keep their default or name override, and no choice options are applied.
        /// </summary>
        public IReadOnlyDictionary<int, SmartPropValue>? ParameterOverrides { get; init; } = ReadOnlyDictionary<int, SmartPropValue>.Empty;

        /// <summary>Stored element states: seeds, PickOne choices and handle edits.</summary>
        public IReadOnlyList<SmartPropElementState>? ElementStates { get; init; }

        /// <summary>Ray cast used by trace operations; without one every trace misses.</summary>
        public Func<SmartPropTraceQuery, SmartPropTraceHit?>? Trace { get; init; }

        /// <summary>
        /// Surface property of a material, by material resource name, read by surface property filters; null when
        /// unknown. Without it every surface has no surface property.
        /// </summary>
        public Func<string, string?>? GetSurfaceProperty { get; init; }

        /// <summary>
        /// Integer attribute of a material, by material resource name and attribute name, read by material attribute
        /// filters; 0 when the material does not set it. Without it no material sets any attribute.
        /// </summary>
        public Func<string, string, int>? GetMaterialAttribute { get; init; }

        /// <summary>Pending changes to stored PickOne choices; the first request matching an element applies.</summary>
        public IReadOnlyList<SmartPropChoiceRequest>? ChoiceRequests { get; init; }

        /// <summary>Named paths for PlaceOnPath elements; an element without a matching path uses its default path.</summary>
        public IReadOnlyList<SmartPropInputPath>? InputPaths { get; init; }
    }
}
