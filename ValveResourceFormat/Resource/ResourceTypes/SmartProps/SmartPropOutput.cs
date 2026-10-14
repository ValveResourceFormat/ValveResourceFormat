using System.Globalization;
using System.IO;
using System.Linq;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// How a deformer applies to a model instance.
    /// </summary>
    public enum SmartPropDeformationMode
    {
        /// <summary>No deformation.</summary>
        None,
        /// <summary>The deformer has moved and turned <see cref="SmartPropModelInstance.Transform"/>; the vertices are not bent.</summary>
        Rigid,
        /// <summary>The deformer bends the instance vertices.</summary>
        Vertex,
    }

    /// <summary>
    /// One model placed by a smart prop evaluation.
    /// </summary>
    public sealed class SmartPropModelInstance
    {
        /// <summary>Element path of the element that placed the model.</summary>
        public required int[] ElementPath { get; init; }

        /// <summary>Model resource name.</summary>
        public required string ModelName { get; init; }

        /// <summary>Instance transform in the space the smart prop was placed in.</summary>
        public SmartPropTransform Transform { get; init; }

        /// <summary>Scale applied in model space before <see cref="Transform"/>.</summary>
        public Vector3 ModelScale { get; init; } = Vector3.One;

        /// <summary>Tint color.</summary>
        public Color32 Tint { get; init; } = Color32.White;

        /// <summary>Material group, null for the default.</summary>
        public string? MaterialGroup { get; init; }

        /// <summary>Forced level of detail, -1 for automatic.</summary>
        public int LodLevel { get; init; } = -1;

        /// <summary>Whether the instance casts shadows.</summary>
        public bool CastShadows { get; init; } = true;

        /// <summary>Whether the instance is a detail object (clutter).</summary>
        public bool DetailObject { get; init; }

        /// <summary>Screen size where a detail object starts to fade.</summary>
        public float DetailFadeStartSize { get; init; }

        /// <summary>Screen size where a detail object is gone.</summary>
        public float DetailFadeEndSize { get; init; }

        /// <summary>Whether dynamic deformation is disabled.</summary>
        public bool DisableDynamicDeformable { get; init; }

        /// <summary>Surface property override, null when none.</summary>
        public string? SurfacePropertyOverride { get; init; }

        /// <summary>
        /// Entity keys of entity elements, null for plain models: <c>classname</c> and the integer
        /// <c>deformable_attach_mode</c> and <c>deformable_attach_orientation_mode</c>.
        /// </summary>
        public IReadOnlyDictionary<string, SmartPropValue>? EntityKeys { get; init; }

        /// <summary>Names of the spawn flags set on an entity element, null for plain models.</summary>
        public IReadOnlyList<string>? SpawnFlags { get; init; }

        /// <summary>Index into <see cref="SmartPropOutput.MaterialOverrideSets"/>, -1 for none.</summary>
        public int MaterialOverrideSetIndex { get; init; } = -1;

        /// <summary>Index into <see cref="SmartPropOutput.MaterialTintSets"/>, -1 for none.</summary>
        public int MaterialTintSetIndex { get; init; } = -1;

        /// <summary>Index into <see cref="SmartPropOutput.Deformers"/>, -1 for none.</summary>
        public int DeformerIndex { get; init; } = -1;

        /// <summary>How the deformer applies.</summary>
        public SmartPropDeformationMode DeformationMode { get; init; }

        /// <summary>
        /// The instance matrix: model scale, then the transform, in the row-vector convention.
        /// </summary>
        /// <returns>The world matrix.</returns>
        public Matrix4x4 GetMatrix() => Matrix4x4.CreateScale(ModelScale) * Transform.ToMatrix();
    }

    /// <summary>
    /// Persistent per-element-path state of a placed smart prop: the random seed and the chosen PickOne child.
    /// Hammer stores these with a placed smart prop so it re-evaluates identically.
    /// </summary>
    public sealed class SmartPropElementState
    {
        /// <summary>Value of an unset seed or choice.</summary>
        public const int Unset = int.MinValue;

        /// <summary>Element path.</summary>
        public required int[] ElementPath { get; init; }

        /// <summary>Seed of the element's random stream, <see cref="Unset"/> until first drawn.</summary>
        public int RandomSeed { get; set; } = Unset;

        /// <summary>Element id of the chosen PickOne child, <see cref="Unset"/> when none.</summary>
        public int ChoiceValue { get; set; } = Unset;

        /// <summary>Handle edits keyed by handle name.</summary>
        public Dictionary<string, SmartPropHandleEdit> HandleEdits { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Whether the evaluation that produced this state read it. Only used states are returned in
        /// <see cref="SmartPropOutput.ElementStates"/>, which is what Hammer stores.
        /// </summary>
        public bool Used { get; internal set; }
    }

    /// <summary>
    /// A Hammer handle edit: a locator transform, sizer extents or a rotator angle.
    /// </summary>
    public sealed class SmartPropHandleEdit
    {
        /// <summary>Locator delta transform.</summary>
        public SmartPropTransform DeltaTransform { get; init; } = SmartPropTransform.Identity;

        /// <summary>Sizer minimum delta.</summary>
        public Vector3 DeltaMin { get; init; }

        /// <summary>Sizer maximum delta.</summary>
        public Vector3 DeltaMax { get; init; }

        /// <summary>Rotator angle delta in degrees.</summary>
        public float DeltaValue { get; init; }
    }

    /// <summary>
    /// How a lattice is interpolated between its points.
    /// </summary>
    public enum SmartPropDeformerInterpolation
    {
        /// <summary>Linear.</summary>
        Linear,
        /// <summary>B-spline.</summary>
        BSpline,
        /// <summary>Catmull-Rom spline.</summary>
        CatmullRom,
        /// <summary>Cubic Bezier through the points and handles.</summary>
        Bezier,
    }

    /// <summary>
    /// A lattice deformer produced by a deformer element.
    /// </summary>
    public sealed class SmartPropDeformer
    {
        /// <summary>Lattice local to smart prop space.</summary>
        public SmartPropTransform Frame { get; init; }

        /// <summary>Lattice box size, spanning from the frame origin.</summary>
        public Vector3 Size { get; init; }

        /// <summary>Segment counts along each axis.</summary>
        public (int X, int Y, int Z) Divisions { get; init; }

        /// <summary>Interpolation between the lattice points.</summary>
        public SmartPropDeformerInterpolation InterpolationMode { get; init; } = SmartPropDeformerInterpolation.Bezier;

        /// <summary>Lattice points, (X+1)(Y+1)(Z+1), indexed x * (Y+1)(Z+1) + z * (Y+1) + y.</summary>
        public required Vector3[] Points { get; init; }

        /// <summary>Two Bezier handles per segment and lattice corner.</summary>
        public required Vector3[] Handles { get; init; }

        /// <summary>Deforms a point given in smart prop space.</summary>
        /// <param name="point">The point.</param>
        /// <returns>The deformed point.</returns>
        public Vector3 Deform(Vector3 point) => SmartPropLattice.DeformPoint(this, point);
    }

    /// <summary>
    /// Kind of a Hammer handle created by a smart prop operation.
    /// </summary>
    public enum SmartPropHandleKind
    {
        /// <summary>A move, rotate and scale handle (CreateLocator), edited through <see cref="SmartPropHandleEdit.DeltaTransform"/>.</summary>
        Locator,
        /// <summary>An angle handle (CreateRotator), edited through <see cref="SmartPropHandleEdit.DeltaValue"/>.</summary>
        Rotator,
        /// <summary>A box handle (CreateSizer), edited through <see cref="SmartPropHandleEdit.DeltaMin"/> and <see cref="SmartPropHandleEdit.DeltaMax"/>.</summary>
        Sizer,
    }

    /// <summary>
    /// A Hammer handle an evaluation created. Edits for it go into the element state of <see cref="ElementPath"/>
    /// under <see cref="Name"/>. One element path holds at most one handle of a name.
    /// </summary>
    public sealed class SmartPropHandle
    {
        /// <summary>Element path the edit is stored under.</summary>
        public required int[] ElementPath { get; init; }

        /// <summary>Handle name, the key of its edit.</summary>
        public required string Name { get; init; }

        /// <summary>Kind of handle.</summary>
        public SmartPropHandleKind Kind { get; init; }

        /// <summary>
        /// Where the handle is drawn: the locator at its offset (moved by the active deformer), the rotator at its offset
        /// with +X along the rotation axis, the sizer at the current transform.
        /// </summary>
        public SmartPropTransform Transform { get; init; } = SmartPropTransform.Identity;

        /// <summary>Locator: whether it can be edited; a locator that is not configurable applies no edit.</summary>
        public bool Configurable { get; init; }

        /// <summary>Locator: whether translation, rotation and scale edits apply.</summary>
        public (bool Translation, bool Rotation, bool Scale) AllowedEdits { get; init; }

        /// <summary>Locator: display scale. Rotator: display radius.</summary>
        public float DisplaySize { get; init; } = 1f;

        /// <summary>Rotator: display color.</summary>
        public Color32 DisplayColor { get; init; } = Color32.White;

        /// <summary>Rotator: angle snapping increment in degrees, 0 for none.</summary>
        public float SnappingIncrement { get; init; }

        /// <summary>Rotator: the angle before edits, in degrees.</summary>
        public float InitialAngle { get; init; }

        /// <summary>Rotator: the angle limits when enforced.</summary>
        public (float Min, float Max)? AngleLimits { get; init; }

        /// <summary>Sizer: the extents before edits.</summary>
        public (Vector3 Min, Vector3 Max) InitialExtents { get; init; }

        /// <summary>Sizer: per-axis bounds of the extents; an axis whose maximum is not above its minimum is unbounded.</summary>
        public (Vector3 Min, Vector3 Max) ExtentLimits { get; init; }

        /// <summary>Sizer: whether the model is drawn with the box.</summary>
        public bool DisplayModel { get; init; }

        /// <summary>Sizer: the minimum components that are written to a variable and can be edited.</summary>
        public (bool X, bool Y, bool Z) EditableMin { get; init; }

        /// <summary>Sizer: the maximum components that are written to a variable and can be edited.</summary>
        public (bool X, bool Y, bool Z) EditableMax { get; init; }
    }

    /// <summary>
    /// Shape of a <see cref="SmartPropChoiceHandle"/>.
    /// </summary>
    public enum SmartPropHandleShape
    {
        /// <summary>Not drawn.</summary>
        None,
        /// <summary>A square.</summary>
        Square,
        /// <summary>A circle.</summary>
        Circle,
        /// <summary>A diamond.</summary>
        Diamond,
    }

    /// <summary>
    /// A Hammer handle of a configurable PickOne element, used to cycle its choice through
    /// <see cref="SmartPropEvaluationInput.ChoiceRequests"/>.
    /// </summary>
    public sealed class SmartPropChoiceHandle
    {
        /// <summary>Element path of the PickOne element.</summary>
        public required int[] ElementPath { get; init; }

        /// <summary>Where the handle is drawn.</summary>
        public SmartPropTransform Transform { get; init; } = SmartPropTransform.Identity;

        /// <summary>Display color.</summary>
        public Color32 Color { get; init; } = Color32.White;

        /// <summary>Display size.</summary>
        public int Size { get; init; }

        /// <summary>Display shape.</summary>
        public SmartPropHandleShape Shape { get; init; } = SmartPropHandleShape.Square;
    }

    /// <summary>
    /// Bends the vertices of a model instance by its deformer: positions from model space into smart prop space, and
    /// normals and tangents by the deformation around the vertex.
    /// </summary>
    public sealed class SmartPropVertexDeformer
    {
        private readonly SmartPropDeformer deformer;
        private readonly Matrix4x4 matrix;
        private readonly bool isValid;

        internal SmartPropVertexDeformer(SmartPropDeformer deformer, Matrix4x4 matrix)
        {
            this.deformer = deformer;
            this.matrix = matrix;
            isValid = SmartPropLattice.IsValid(deformer);
        }

        /// <summary>Deforms a model-space position.</summary>
        /// <param name="position">Model-space position.</param>
        /// <returns>The deformed position in smart prop space.</returns>
        public Vector3 DeformPosition(Vector3 position)
        {
            var placed = Vector3.Transform(position, matrix);
            return isValid ? deformer.Deform(placed) : placed;
        }

        /// <summary>
        /// Deforms a model-space normal: two unit offsets perpendicular to it are deformed, and the normal is rebuilt
        /// from their directions.
        /// </summary>
        /// <param name="position">Model-space position of the vertex.</param>
        /// <param name="normal">Model-space normal.</param>
        /// <returns>The deformed unit normal in smart prop space, or the normal unchanged when the deformer has no volume.</returns>
        public Vector3 DeformNormal(Vector3 position, Vector3 normal)
        {
            if (!isValid)
            {
                return normal;
            }

            var n = normal.Length() > 1e-5f ? Vector3.Normalize(normal) : normal;
            Vector3 a, b;

            if (MathF.Abs(n.X) >= 0.001f || MathF.Abs(n.Y) >= 0.001f)
            {
                a = MathUtils.SafeNormalize(new Vector3(-n.Y, n.X, 0f));
                b = MathUtils.SafeNormalize(Vector3.Cross(n, a));
            }
            else
            {
                a = Vector3.UnitY;
                b = new Vector3(-n.Z, 0f, 0f);
            }

            var origin = DeformPosition(position);
            var deformedA = MathUtils.SafeNormalize(DeformPosition(position + a) - origin);
            var deformedB = MathUtils.SafeNormalize(DeformPosition(position + b) - origin);
            return MathUtils.SafeNormalize(Vector3.Cross(deformedA, deformedB));
        }

        /// <summary>Deforms a model-space unit tangent by deforming the point one unit along it.</summary>
        /// <param name="position">Model-space position of the vertex.</param>
        /// <param name="tangent">Model-space unit tangent.</param>
        /// <returns>The deformed unit tangent in smart prop space.</returns>
        public Vector3 DeformTangent(Vector3 position, Vector3 tangent)
            => MathUtils.SafeNormalize(DeformPosition(position + tangent) - DeformPosition(position));
    }

    /// <summary>
    /// Everything a smart prop evaluation produced.
    /// </summary>
    public sealed class SmartPropOutput
    {
        private readonly HashSet<string> handleKeys = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Placed models.</summary>
        public List<SmartPropModelInstance> Models { get; } = [];

        /// <summary>Deformers referenced by models.</summary>
        public List<SmartPropDeformer> Deformers { get; } = [];

        /// <summary>
        /// Material replacement tables (original material to replacement) referenced by models. Both names are
        /// normalized with <see cref="NormalizeResourceName"/>; an empty replacement replaces nothing. The original is
        /// the material a model draws after its material group is applied.
        /// </summary>
        public List<Dictionary<string, string>> MaterialOverrideSets { get; } = [];

        /// <summary>
        /// Material tint tables referenced by models, keyed by names normalized with <see cref="NormalizeResourceName"/>.
        /// A tint applies to the material a model draws after its material group and material overrides are applied,
        /// and only its color channels are used.
        /// </summary>
        public List<Dictionary<string, Color32>> MaterialTintSets { get; } = [];

        /// <summary>Hammer handles the evaluation created, in creation order.</summary>
        public List<SmartPropHandle> Handles { get; } = [];

        /// <summary>Handles of configurable PickOne elements, in evaluation order.</summary>
        public List<SmartPropChoiceHandle> ChoiceHandles { get; } = [];

        /// <summary>
        /// Claims the handle name at an element path, case-insensitively. Only the first claim succeeds; an operation
        /// whose claim fails does nothing.
        /// </summary>
        internal bool TryClaimHandle(int[] elementPath, string name)
        {
            var path = string.Join(',', elementPath.Select(static index => index.ToString(CultureInfo.InvariantCulture)));
            return handleKeys.Add(string.Concat(path, "|", name));
        }

        /// <summary>
        /// The vertex deformer of a model instance whose vertices a deformer bends. It deforms positions, normals and
        /// tangents the way the map compiler does.
        /// </summary>
        /// <param name="instance">A model instance of this output.</param>
        /// <returns>The vertex deformer, or null when the instance is not vertex-deformed.</returns>
        public SmartPropVertexDeformer? GetVertexDeformer(SmartPropModelInstance instance)
        {
            if (instance.DeformationMode != SmartPropDeformationMode.Vertex || instance.DeformerIndex < 0 || instance.DeformerIndex >= Deformers.Count)
            {
                return null;
            }

            return new SmartPropVertexDeformer(Deformers[instance.DeformerIndex], instance.GetMatrix());
        }

        /// <summary>
        /// Normalizes a resource name the way the material tint and override tables are keyed: lower case, forward
        /// slashes, no repeated slashes and no <c>.</c> or <c>..</c> segments. An absolute path or a name without an
        /// extension is not a resource name and gives an empty string.
        /// </summary>
        /// <param name="name">Resource name.</param>
        /// <returns>The normalized name, or an empty string.</returns>
        public static string NormalizeResourceName(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            if (name.Length == 0 || name[0] is '/' or '\\' || (name.Length > 1 && name[1] == ':') || Path.GetExtension(name).Length < 2)
            {
                return string.Empty;
            }

            var segments = new List<string>();

            foreach (var segment in name.ToLowerInvariant().Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == "..")
                {
                    if (segments.Count > 0)
                    {
                        segments.RemoveAt(segments.Count - 1);
                    }
                }
                else if (segment != ".")
                {
                    segments.Add(segment);
                }
            }

            return string.Join('/', segments);
        }

        /// <summary>Element states the evaluation read, including the seeds and choices it created.</summary>
        public List<SmartPropElementState> ElementStates { get; } = [];

        /// <summary>
        /// Writes a text dump of the output: the placed models, deformers, material sets, handles and element states.
        /// </summary>
        /// <param name="writer">The writer.</param>
        public void WriteText(IndentedTextWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);

            writer.WriteLine(Invariant($"Models: {Models.Count}"));
            writer.Indent++;

            foreach (var model in Models)
            {
                writer.WriteLine(Invariant($"{PathText(model.ElementPath)} {model.ModelName}"));
                writer.Indent++;
                WriteTransform(writer, model.Transform);

                if (model.ModelScale != Vector3.One)
                {
                    writer.WriteLine($"Model scale: {Format(model.ModelScale)}");
                }

                if (model.Tint != Color32.White)
                {
                    writer.WriteLine($"Tint: {Format(model.Tint)}");
                }

                if (model.MaterialGroup != null)
                {
                    writer.WriteLine($"Material group: {model.MaterialGroup}");
                }

                if (model.LodLevel >= 0)
                {
                    writer.WriteLine(Invariant($"LoD: {model.LodLevel}"));
                }

                if (!model.CastShadows)
                {
                    writer.WriteLine("No shadows");
                }

                if (model.DetailObject)
                {
                    writer.WriteLine($"Detail object, fade: {Format(model.DetailFadeStartSize)} {Format(model.DetailFadeEndSize)}");
                }

                if (model.DisableDynamicDeformable)
                {
                    writer.WriteLine("Dynamic deformation disabled");
                }

                if (model.SurfacePropertyOverride != null)
                {
                    writer.WriteLine($"Surface property: {model.SurfacePropertyOverride}");
                }

                if (model.DeformerIndex >= 0)
                {
                    writer.WriteLine(Invariant($"Deformation: {model.DeformationMode}, deformer {model.DeformerIndex}"));
                }

                if (model.MaterialOverrideSetIndex >= 0)
                {
                    writer.WriteLine(Invariant($"Material override set: {model.MaterialOverrideSetIndex}"));
                }

                if (model.MaterialTintSetIndex >= 0)
                {
                    writer.WriteLine(Invariant($"Material tint set: {model.MaterialTintSetIndex}"));
                }

                foreach (var (key, value) in model.EntityKeys ?? new Dictionary<string, SmartPropValue>())
                {
                    writer.WriteLine($"Entity key: {key} = {value}");
                }

                foreach (var flag in model.SpawnFlags ?? [])
                {
                    writer.WriteLine($"Spawn flag: {flag}");
                }

                writer.Indent--;
            }

            writer.Indent--;
            writer.WriteLine(Invariant($"Deformers: {Deformers.Count}"));
            writer.Indent++;

            for (var i = 0; i < Deformers.Count; i++)
            {
                var deformer = Deformers[i];
                writer.WriteLine(Invariant($"[{i}] {deformer.InterpolationMode}, divisions {deformer.Divisions.X} {deformer.Divisions.Y} {deformer.Divisions.Z}, size {Format(deformer.Size)}"));
                writer.Indent++;
                WriteTransform(writer, deformer.Frame);
                writer.WriteLine($"Points: {string.Join(", ", deformer.Points.Select(Format))}");
                writer.WriteLine($"Handles: {string.Join(", ", deformer.Handles.Select(Format))}");
                writer.Indent--;
            }

            writer.Indent--;
            WriteSets(writer, "Material override sets", MaterialOverrideSets, static value => value);
            WriteSets(writer, "Material tint sets", MaterialTintSets, Format);
            writer.WriteLine(Invariant($"Handles: {Handles.Count}"));
            writer.Indent++;

            foreach (var handle in Handles)
            {
                writer.WriteLine(Invariant($"{PathText(handle.ElementPath)} {handle.Kind} {handle.Name}"));
                writer.Indent++;
                WriteTransform(writer, handle.Transform);

                switch (handle.Kind)
                {
                    case SmartPropHandleKind.Locator:
                        writer.WriteLine($"Configurable: {handle.Configurable}, translation {handle.AllowedEdits.Translation}, rotation {handle.AllowedEdits.Rotation}, scale {handle.AllowedEdits.Scale}");
                        writer.WriteLine($"Display scale: {Format(handle.DisplaySize)}");
                        break;
                    case SmartPropHandleKind.Rotator:
                        writer.WriteLine($"Initial angle: {Format(handle.InitialAngle)}, snapping {Format(handle.SnappingIncrement)}");

                        if (handle.AngleLimits is var (min, max))
                        {
                            writer.WriteLine($"Limits: {Format(min)} {Format(max)}");
                        }

                        writer.WriteLine($"Display radius: {Format(handle.DisplaySize)}, color {Format(handle.DisplayColor)}");
                        break;
                    case SmartPropHandleKind.Sizer:
                        writer.WriteLine($"Initial extents: {Format(handle.InitialExtents.Min)}, {Format(handle.InitialExtents.Max)}");
                        writer.WriteLine($"Limits: {Format(handle.ExtentLimits.Min)}, {Format(handle.ExtentLimits.Max)}");
                        writer.WriteLine($"Editable: min {handle.EditableMin}, max {handle.EditableMax}, display model {handle.DisplayModel}");
                        break;
                    default:
                        break;
                }

                writer.Indent--;
            }

            writer.Indent--;
            writer.WriteLine(Invariant($"Choice handles: {ChoiceHandles.Count}"));
            writer.Indent++;

            foreach (var handle in ChoiceHandles)
            {
                writer.WriteLine(Invariant($"{PathText(handle.ElementPath)} {handle.Shape}, size {handle.Size}, color {Format(handle.Color)}"));
                writer.Indent++;
                WriteTransform(writer, handle.Transform);
                writer.Indent--;
            }

            writer.Indent--;
            writer.WriteLine(Invariant($"Element states: {ElementStates.Count}"));
            writer.Indent++;

            foreach (var state in ElementStates.OrderBy(static state => PathText(state.ElementPath), StringComparer.Ordinal))
            {
                var choice = state.ChoiceValue == SmartPropElementState.Unset ? "unset" : state.ChoiceValue.ToString(CultureInfo.InvariantCulture);
                var seed = state.RandomSeed == SmartPropElementState.Unset ? "unset" : state.RandomSeed.ToString(CultureInfo.InvariantCulture);
                writer.WriteLine($"{PathText(state.ElementPath)} seed {seed}, choice {choice}");
                writer.Indent++;

                foreach (var (name, edit) in state.HandleEdits)
                {
                    writer.WriteLine($"Edit {name}: delta position {Format(edit.DeltaTransform.Position)}, value {Format(edit.DeltaValue)}, min {Format(edit.DeltaMin)}, max {Format(edit.DeltaMax)}");
                }

                writer.Indent--;
            }

            writer.Indent--;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            using var writer = new IndentedTextWriter();
            WriteText(writer);
            return writer.ToString();
        }

        private static void WriteSets<T>(IndentedTextWriter writer, string title, List<Dictionary<string, T>> sets, Func<T, string> format)
        {
            writer.WriteLine(Invariant($"{title}: {sets.Count}"));
            writer.Indent++;

            for (var i = 0; i < sets.Count; i++)
            {
                writer.WriteLine(Invariant($"[{i}]"));
                writer.Indent++;

                foreach (var (name, value) in sets[i].OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WriteLine($"{name}: {format(value)}");
                }

                writer.Indent--;
            }

            writer.Indent--;
        }

        private static void WriteTransform(IndentedTextWriter writer, SmartPropTransform transform)
        {
            var angles = EntityTransformHelper.ToEulerAngles(transform.Rotation);
            writer.WriteLine($"Position: {Format(transform.Position)}, angles {Format(angles)}, scale {Format(transform.Scale)}");
        }

        private static string Invariant(FormattableString text) => FormattableString.Invariant(text);

        private static string PathText(int[] elementPath) => $"[{string.Join(", ", elementPath.Select(static index => index.ToString(CultureInfo.InvariantCulture)))}]";

        private static string Format(float value)
        {
            var rounded = MathF.Round(value, 3);
            return (rounded == 0f ? 0f : rounded).ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Format(Vector3 value) => $"{Format(value.X)} {Format(value.Y)} {Format(value.Z)}";

        private static string Format(Color32 value) => Invariant($"{value.R} {value.G} {value.B} {value.A}");
    }
}
