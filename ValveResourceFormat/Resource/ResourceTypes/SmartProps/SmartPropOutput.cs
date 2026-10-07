namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// How a deformer applies to a model instance.
    /// </summary>
    public enum SmartPropDeformationMode
    {
        /// <summary>No deformation.</summary>
        None,
        /// <summary>The deformer moves the instance transform only.</summary>
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

        /// <summary>Entity keys of entity elements, null for plain models.</summary>
        public IReadOnlyDictionary<string, string>? EntityKeys { get; init; }

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

        /// <summary>Interpolation mode: 0 linear, 1 B-spline, 2 Catmull-Rom, 3 Bezier.</summary>
        public int InterpolationMode { get; init; } = 3;

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
    /// under <see cref="Name"/>.
    /// </summary>
    public sealed class SmartPropHandle
    {
        /// <summary>Element path the edit is stored under.</summary>
        public required int[] ElementPath { get; init; }

        /// <summary>Handle name, the key of its edit.</summary>
        public required string Name { get; init; }

        /// <summary>Kind of handle.</summary>
        public SmartPropHandleKind Kind { get; init; }

        /// <summary>Locator: whether translation, rotation and scale edits apply.</summary>
        public (bool Translation, bool Rotation, bool Scale) AllowedEdits { get; init; }

        /// <summary>Rotator: the angle before edits, in degrees.</summary>
        public float InitialAngle { get; init; }

        /// <summary>Rotator: the angle limits when enforced.</summary>
        public (float Min, float Max)? AngleLimits { get; init; }

        /// <summary>Sizer: the extents before edits.</summary>
        public (Vector3 Min, Vector3 Max) InitialExtents { get; init; }
    }

    /// <summary>
    /// Everything a smart prop evaluation produced.
    /// </summary>
    public sealed class SmartPropOutput
    {
        /// <summary>Placed models.</summary>
        public List<SmartPropModelInstance> Models { get; } = [];

        /// <summary>Deformers referenced by models.</summary>
        public List<SmartPropDeformer> Deformers { get; } = [];

        /// <summary>Material replacement tables (original material to replacement) referenced by models.</summary>
        public List<Dictionary<string, string>> MaterialOverrideSets { get; } = [];

        /// <summary>Material tint tables referenced by models.</summary>
        public List<Dictionary<string, Color32>> MaterialTintSets { get; } = [];

        /// <summary>Hammer handles the evaluation created.</summary>
        public List<SmartPropHandle> Handles { get; } = [];

        /// <summary>
        /// The per-vertex transform of a model instance whose vertices a deformer bends: it maps a model-space position to
        /// smart prop space through the instance matrix and the deformer, with the local linear map for normals and tangents.
        /// </summary>
        /// <param name="instance">A model instance of this output.</param>
        /// <returns>The transform, or null when the instance is not vertex-deformed.</returns>
        public Func<Vector3, (Vector3 Position, Matrix4x4 Linear)>? GetVertexDeformation(SmartPropModelInstance instance)
        {
            if (instance.DeformationMode != SmartPropDeformationMode.Vertex || instance.DeformerIndex < 0 || instance.DeformerIndex >= Deformers.Count)
            {
                return null;
            }

            var deformer = Deformers[instance.DeformerIndex];
            var matrix = instance.GetMatrix();
            const float Step = 0.05f;

            return vertex =>
            {
                Vector3 Map(Vector3 v) => deformer.Deform(Vector3.Transform(v, matrix));

                var dx = (Map(vertex + new Vector3(Step, 0f, 0f)) - Map(vertex - new Vector3(Step, 0f, 0f))) / (2f * Step);
                var dy = (Map(vertex + new Vector3(0f, Step, 0f)) - Map(vertex - new Vector3(0f, Step, 0f))) / (2f * Step);
                var dz = (Map(vertex + new Vector3(0f, 0f, Step)) - Map(vertex - new Vector3(0f, 0f, Step))) / (2f * Step);

                var linear = new Matrix4x4(
                    dx.X, dx.Y, dx.Z, 0f,
                    dy.X, dy.Y, dy.Z, 0f,
                    dz.X, dz.Y, dz.Z, 0f,
                    0f, 0f, 0f, 1f);

                return (Map(vertex), linear);
            };
        }

        /// <summary>Element states after the evaluation, including the seeds and choices it created.</summary>
        public List<SmartPropElementState> ElementStates { get; } = [];
    }
}
