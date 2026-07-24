using System.Linq;
using System.Numerics;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics
{
    /// <summary>
    /// Finite-element (soft body / cloth) model embedded in a physics aggregate (<c>m_pFeModel</c>).
    /// </summary>
    /// <remarks>
    /// Parses the control-node topology needed to reconstruct editable ModelDoc cloth source.
    /// Phase 1 (bone-chain cloth) only uses the control-node names, skeleton parents and inverse
    /// masses to rebuild <c>ClothChain</c> nodes. The raw <see cref="Data"/> object is retained so a
    /// later phase can read the quad/tri/pose arrays (<c>m_Quads</c>, <c>m_Tris</c>, <c>m_InitPose</c>)
    /// to rebuild full proxy-mesh cloth.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/PhysFeModelDesc_t">PhysFeModelDesc_t</seealso>
    public sealed class FeModel
    {
        /// <summary>
        /// Gets the raw key-value object backing this FeModel (kept for fields not yet surfaced as properties).
        /// </summary>
        public KVObject Data { get; }

        /// <summary>
        /// Gets the per-node control names. Auto-generated proxy-mesh nodes are prefixed with <c>$</c>
        /// (e.g. <c>$cloth_m0p3</c>); the remaining entries are real skeleton bone names.
        /// </summary>
        public string[] CtrlNames { get; }

        /// <summary>
        /// Gets the per-node parent node index (index into <see cref="CtrlNames"/>), or -1 for a root.
        /// </summary>
        public int[] SkelParents { get; }

        /// <summary>
        /// Gets the per-node inverse mass. 0 marks a static/pinned anchor node; &gt; 0 marks a simulated node.
        /// </summary>
        public float[] NodeInvMasses { get; }

        /// <summary>
        /// Gets the total number of control nodes.
        /// </summary>
        public int NodeCount { get; }

        /// <summary>
        /// Gets the number of leading static (pinned) nodes.
        /// </summary>
        public int StaticNodeCount { get; }

        /// <summary>
        /// Gets the index of the first position-driven (bone-chain follower) node.
        /// </summary>
        public int FirstPositionDrivenNode { get; }

        /// <summary>
        /// Gets the per-node rest (bind-pose) positions in model space, parsed from the first three
        /// components of each <c>m_InitPose</c> entry (the remaining components are the rest orientation
        /// quaternion). Length matches <see cref="NodeCount"/>.
        /// </summary>
        public Vector3[] InitPosePositions { get; }

        /// <summary>
        /// Gets the cloth surface quads. Each entry is a 4-element array of control-node indices.
        /// </summary>
        public int[][] Quads { get; }

        /// <summary>
        /// Gets the cloth surface triangles. Each entry is a 3-element array of control-node indices.
        /// </summary>
        public int[][] Tris { get; }

        /// <summary>
        /// Gets the structural distance constraints (<c>m_Rods</c>) between pairs of control nodes.
        /// Verified NOT derivable from <see cref="Quads"/>/<see cref="Tris"/> edges or diagonals on
        /// dark_willow (61/61 rods matched neither) - re-declare these directly via explicit ClothSpring
        /// nodes rather than guessing a geometric rule from the surface alone.
        /// </summary>
        public Rod[] Rods { get; }

        /// <summary>A single structural rod (from <c>m_Rods</c>).</summary>
        /// <param name="NodeA">First endpoint control-node index.</param>
        /// <param name="NodeB">Second endpoint control-node index.</param>
        /// <param name="MinDist">Minimum allowed distance (<c>flMinDist</c>).</param>
        /// <param name="MaxDist">Maximum allowed distance (<c>flMaxDist</c>).</param>
        /// <param name="Weight0">Blend weight (<c>flWeight0</c>) - real per-rod data (~38 distinct values
        /// from 0.0 to ~0.66 on dark_willow, similarly wide-ranging on meepo_naruto_set), but NOT
        /// re-authorable via <c>ClothSpring</c>: <c>CModelDocClothSpring</c>'s complete registered attribute
        /// set (Ghidra <c>GetStaticAttributes</c> extraction from physicsbuilder.dll - the same method that
        /// found min_length/max_length/stiffness) has no <c>m_flWeight0</c> at all, and an authored
        /// <c>weight0</c> KV field is silently discarded - the resulting <c>flWeight0</c> reads back as the
        /// compiler's own internal default (0.5) while <c>MinDist</c>/<c>MaxDist</c> on the same rod stay
        /// byte-exact. Kept here as an accurate record of the compiled value, not exposed as an export field.</param>
        /// <param name="RelaxationFactor">Relaxation factor (<c>flRelaxationFactor</c>) - real per-rod data,
        /// not a fixed default: dark_willow ships 1.0 uniformly, but meepo_naruto_set's rods vary between
        /// 1.0 and 0.5. Same non-authorable caveat as <see cref="Weight0"/> applies (no matching
        /// <c>CModelDocClothSpring</c> attribute either) - not currently exposed as an export field.</param>
        public readonly record struct Rod(int NodeA, int NodeB, float MinDist, float MaxDist, float Weight0, float RelaxationFactor);

        /// <summary>
        /// Gets the explicit local orientation basis of certain nodes (<c>m_NodeBases</c>), keyed by
        /// control-node index. Verified (meepo_naruto_set, 0 mismatches across all 77 dynamic nodes) to
        /// appear for EXACTLY the nodes with rod-graph degree &gt;= 2 that are not a ClothChain joint
        /// (chain joints derive orientation from their own parent-child twist/bend physics instead).
        /// <c>qAdjust</c> is compiler-computed from the X0/X1/Y0/Y1 references with no separate authoring
        /// channel (the source ClothNode schema has none) - only the 4 node references are recoverable/
        /// re-authorable via <c>node_base_x0/x1/y0/y1</c>.
        /// </summary>
        public IReadOnlyDictionary<int, NodeBasis> NodeBases { get; }

        /// <summary>A single node's explicit orientation basis (from <c>m_NodeBases</c>).</summary>
        /// <param name="NodeX0">Control-node index defining the local X axis' first endpoint.</param>
        /// <param name="NodeX1">Control-node index defining the local X axis' second endpoint.</param>
        /// <param name="NodeY0">Control-node index defining the local Y axis' first endpoint.</param>
        /// <param name="NodeY1">Control-node index defining the local Y axis' second endpoint.</param>
        public readonly record struct NodeBasis(int NodeX0, int NodeX1, int NodeY0, int NodeY1);

        /// <summary>
        /// Per-node solver integrator parameters - the cloth-to-bind attraction/damping/gravity that keep
        /// the simulated cloth following the animated body (the original anti-clipping mechanism, used in
        /// lieu of explicit collision capsules). Length matches <see cref="NodeCount"/>.
        /// </summary>
        public NodeIntegrator[] NodeIntegrators { get; }

        /// <summary>
        /// A single node's solver integrator parameters (from <c>m_NodeIntegrator</c>).
        /// </summary>
        /// <param name="PointDamping">Velocity damping (<c>flPointDamping</c>).</param>
        /// <param name="ForceAttraction">Goal/force attraction toward the animated pose (<c>flAnimationForceAttraction</c>).</param>
        /// <param name="VertexAttraction">Per-vertex attraction toward the animated pose (<c>flAnimationVertexAttraction</c>).</param>
        /// <param name="Gravity">Gravity acceleration applied to the node (<c>flGravity</c>).</param>
        public readonly record struct NodeIntegrator(float PointDamping, float ForceAttraction, float VertexAttraction, float Gravity);

        /// <summary>Gets the integrator parameters for <paramref name="node"/>, or a zeroed struct when absent.</summary>
        public NodeIntegrator GetIntegrator(int node)
            => node >= 0 && node < NodeIntegrators.Length ? NodeIntegrators[node] : default;

        /// <summary>
        /// Gets the world-collision radii (<c>m_NodeCollisionRadii</c>); empty for models (like
        /// dark_willow) that rely on goal attraction rather than per-node collision. Indexed by DYNAMIC
        /// node (control-node index minus <see cref="StaticNodeCount"/>) - static nodes carry no radius.
        /// </summary>
        public float[] NodeCollisionRadii { get; }

        /// <summary>Gets the world-collision radius for control node <paramref name="node"/>, or 0 when absent.</summary>
        public float GetCollisionRadius(int node)
        {
            var dynamicIndex = node - StaticNodeCount;
            return dynamicIndex >= 0 && dynamicIndex < NodeCollisionRadii.Length ? NodeCollisionRadii[dynamicIndex] : 0f;
        }

        /// <summary>
        /// Gets the control nodes that collide with the world (<c>m_WorldCollisionNodes</c>), from
        /// per-joint <c>world_collision</c> in the source. Empty for cloth without world collision.
        /// </summary>
        public IReadOnlySet<int> WorldCollisionNodes { get; }

        /// <summary>Returns whether <paramref name="node"/> collides with the world.</summary>
        public bool IsWorldCollisionNode(int node) => WorldCollisionNodes.Contains(node);

        /// <summary>
        /// Gets the per-node animation stray radii (<c>m_AnimStrayRadii</c>): the maximum distance a
        /// simulated node may stray from its animated position (per-joint <c>stray_radius</c> in the source).
        /// </summary>
        public IReadOnlyDictionary<int, float> AnimStrayRadii { get; }

        /// <summary>
        /// Gets the control nodes driven by a back-solved fit matrix (<c>m_FitMatrices</c>) - bones whose
        /// orientation is derived from a driving/proxy mesh (<c>ClothProxyMeshFile.back_solve_joints</c>)
        /// rather than simulated directly. A model can have bone-chain cloth AND a proxy mesh that are
        /// fully INDEPENDENT of each other (proxy ships <c>back_solve_joints = false</c>): the presence of
        /// a proxy mesh does not by itself mean every bone chain is back-solved by it - check this set.
        /// </summary>
        public IReadOnlySet<int> FitMatrixNodes { get; }

        /// <summary>
        /// Gets the authored per-vertex skin weights of back-solved proxy-mesh vertices, recovered
        /// VERBATIM from the compiled data (no geometric synthesis), keyed by control node. The original
        /// author's painted weights survive compilation spread across three arrays:
        /// <c>m_FitWeights</c> (each entry's <c>flWeight</c> IS the vertex's authored weight to that fit's
        /// bone, for weights at/above the model's own back_solve_influence_threshold - per-vertex totals
        /// across fits sum to exactly 1.0 whenever nothing fell below the threshold),
        /// <c>m_CtrlOffsets</c> (the vertex's primary rigid-anchor bone), and <c>m_CtrlSoftOffsets</c>
        /// (nested-lerp alphas that recover even the sub-threshold weights the fit ranges drop). Any
        /// remaining authored weight went to a STATIC bone (static bones never receive a fit matrix) and
        /// is assigned to the primary bone's nearest static real ancestor. Verified on dark_willow:
        /// all 39 back-solved simulated vertices reconstruct with error ~1e-7, and every fit matrix's
        /// vCenter equals the weighted centroid of exactly these weights (~1e-5), so re-painting them
        /// reproduces the original fit transforms rather than approximating them.
        /// </summary>
        public IReadOnlyDictionary<int, (string Bone, float Weight)[]> RecoveredSkinWeights { get; }

        /// <summary>
        /// Gets the per-model <c>back_solve_influence_threshold</c> derived from the recovered weights:
        /// the compiled fit ranges omit painted weights below the original's threshold while
        /// <c>m_CtrlSoftOffsets</c> still carries them, so any value in
        /// (max omitted weight, min included weight] reproduces the original fit membership exactly -
        /// the midpoint is used. Null when the model gives no such signal (no omitted weights at all),
        /// in which case the caller's default (0.0) already reproduces the fits.
        /// </summary>
        public float? RecoveredBackSolveThreshold { get; }

        /// <summary>
        /// Gets the control nodes participating in a twist constraint (<c>m_Twists</c>) - i.e. whose
        /// ClothChain joint was authored with <c>twist_relax &gt; 0</c>. Verified on meepo_naruto_set: a
        /// chain with twist_relax left at 0 (the previous hardcoded default) compiles to a plain "Rope"
        /// fallback constraint instead (<c>m_Ropes</c>, a 4-node whole-chain constraint absent from the
        /// original entirely) - re-declaring twist_relax as nonzero for exactly these nodes is what
        /// recovers the original's real Twist network and drops the bogus Ropes.
        /// </summary>
        public IReadOnlySet<int> TwistNodes { get; }

        // The cloth_drag paint compiles to flPointDamping = paint * 30 (measured: 0.2 -> 6.0, 0.5 -> 15.0).
        internal const float ClothDragPointDampingScale = 30f;

        // Base gravity acceleration (inches/s^2) that a source gravity_z of 1.0 maps to; used to turn the
        // compiled per-node flGravity back into the source gravity_z scale (ClothChain joints and ClothNode).
        internal const float ClothSourceBaseGravity = 360f;

        // The source goal_damping drives flAnimationVertexAttraction via an EXPONENTIAL SATURATION curve
        // approaching the ceiling (1 - fa), not a plain linear ramp: va = fa + (1-fa) * (1 - exp(-gd *
        // S(goal_strength) / (1-fa))). S (the curve's initial slope at gd=0) is sampled per goal strength
        // from the compiler (gs=0.5 + gd=0.005 compiles to va=0.15106 - dark_carnival's exact original,
        // deep in the small-gd region where the exponential and a plain linear model agree to 4+ digits).
        // Verified the CURVE SHAPE (not just the small-gd slope) directly against meepo_naruto_set's own
        // standalone ClothNode data: a goal_strength=0.6 node (fa=0.216) with the SAME slope entry (3.72)
        // reproduces a real authored goal_damping=0.3 (fa+excess=0.797273) almost exactly via the
        // exponential inverse (recovers 0.285, within 5%) while the OLD plain-linear inverse
        // (excess/slope, no saturation) recovered 0.156 - 48% off - because 0.3 is far outside the
        // small-gd linear region the original dark_carnival calibration point happened to sit in.
        static readonly (float GoalStrength, float Slope)[] GoalDampingSlopes =
        [
            (0.05f, 163.6f),
            (0.1f, 61.2f),
            (0.2f, 19.9f),
            (0.4f, 7.34f),
            (0.5f, 5.16f),
            (0.6f, 3.72f),
            (0.8f, 1.91f),
        ];

        /// <summary>
        /// Recovers the source <c>goal_damping</c> from the compiled excess of
        /// <c>flAnimationVertexAttraction</c> over <c>flAnimationForceAttraction</c> at the given painted
        /// goal strength, by inverting the compiler's measured exponential-saturation response (log-log
        /// interpolated initial slope). Legacy out-of-range attractions clamp to the strongest
        /// reproducible damping.
        /// </summary>
        public static float GoalDampingFromAttraction(float goalStrength, float vertexAttractionExcess)
        {
            if (vertexAttractionExcess <= 0f || goalStrength <= 0f)
            {
                return 0f;
            }

            float slope;
            if (goalStrength <= GoalDampingSlopes[0].GoalStrength)
            {
                slope = GoalDampingSlopes[0].Slope;
            }
            else if (goalStrength >= GoalDampingSlopes[^1].GoalStrength)
            {
                slope = GoalDampingSlopes[^1].Slope;
            }
            else
            {
                slope = GoalDampingSlopes[^1].Slope;
                for (var i = 1; i < GoalDampingSlopes.Length; i++)
                {
                    if (goalStrength <= GoalDampingSlopes[i].GoalStrength)
                    {
                        var (gs0, s0) = GoalDampingSlopes[i - 1];
                        var (gs1, s1) = GoalDampingSlopes[i];
                        var t = (MathF.Log(goalStrength) - MathF.Log(gs0)) / (MathF.Log(gs1) - MathF.Log(gs0));
                        slope = MathF.Exp(MathF.Log(s0) + (MathF.Log(s1) - MathF.Log(s0)) * t);
                        break;
                    }
                }
            }

            // flAnimationForceAttraction = goal_strength^3 (the same cubing the caller already inverted
            // via cbrt to arrive at goalStrength), so recompute it here to get the true asymptote.
            var forceAttraction = goalStrength * goalStrength * goalStrength;
            var ceiling = 1f - forceAttraction;
            if (ceiling <= 0f)
            {
                return 0f;
            }

            var ratio = vertexAttractionExcess / ceiling;
            if (ratio >= 1f)
            {
                return 1f;
            }

            return Math.Clamp(-ceiling / slope * MathF.Log(1f - ratio), 0f, 1f);
        }

        /// <summary>Gets the stray radius for <paramref name="node"/>, or 0 when unconstrained.</summary>
        public float GetStrayRadius(int node) => AnimStrayRadii.GetValueOrDefault(node);

        // Scalar cloth solver parameters (surfaced as <c>ClothParams</c> when rebuilding source).
#pragma warning disable CS1591
        public float InternalPressure => Data.GetFloatProperty("m_flInternalPressure");
        public float Windage => Data.GetFloatProperty("m_flWindage");
        public float WindDrag => Data.GetFloatProperty("m_flWindDrag");
        public float LocalForce => Data.GetFloatProperty("m_flLocalForce");
        public float LocalRotation => Data.GetFloatProperty("m_flLocalRotation");
        public float AddWorldCollisionRadius => Data.GetFloatProperty("m_flAddWorldCollisionRadius");
        public float DefaultGravityScale => Data.GetFloatProperty("m_flDefaultGravityScale", 1.0f);
        public float DefaultVelAirDrag => Data.GetFloatProperty("m_flDefaultVelAirDrag");
        public float DefaultExpAirDrag => Data.GetFloatProperty("m_flDefaultExpAirDrag");
        public float DefaultThreadStretch => Data.GetFloatProperty("m_flDefaultThreadStretch");
        public float DefaultSurfaceStretch => Data.GetFloatProperty("m_flDefaultSurfaceStretch");
        public float LocalDrag1 => Data.GetFloatProperty("m_flLocalDrag1");
        public int ExtraIterations => Data.GetInt32Property("m_nExtraIterations");
        public int ExtraGoalIterations => Data.GetInt32Property("m_nExtraGoalIterations");
        public int ExtraPressureIterations => Data.GetInt32Property("m_nExtraPressureIterations");
#pragma warning restore CS1591

        /// <summary>
        /// Initializes a new instance of the <see cref="FeModel"/> class from a parsed <c>m_pFeModel</c> sub-object.
        /// </summary>
        public FeModel(KVObject data)
        {
            Data = data;
            CtrlNames = data.GetArray<string>("m_CtrlName") ?? [];
            SkelParents = (data.GetIntegerArray("m_SkelParents")).Select(static v => (int)v).ToArray();
            NodeInvMasses = data.GetFloatArray("m_NodeInvMasses");
            NodeCount = data.GetInt32Property("m_nNodeCount");
            StaticNodeCount = data.GetInt32Property("m_nStaticNodes");
            FirstPositionDrivenNode = data.GetInt32Property("m_nFirstPositionDrivenNode");

            var initPose = data.GetArray("m_InitPose");
            InitPosePositions = initPose is null
                ? []
                : initPose.Select(static p => p.ToVector3()).ToArray();

            Quads = ReadNodeIndexArray(data, "m_Quads", 4);
            Tris = ReadNodeIndexArray(data, "m_Tris", 3);

            var rods = data.GetArray("m_Rods");
            Rods = rods is null
                ? []
                : rods.Select(static o =>
                {
                    var nodes = o.GetIntegerArray("nNode");
                    return new Rod(
                        nodes.Length > 0 ? (int)nodes[0] : -1,
                        nodes.Length > 1 ? (int)nodes[1] : -1,
                        o.GetFloatProperty("flMinDist"),
                        o.GetFloatProperty("flMaxDist"),
                        o.GetFloatProperty("flWeight0"),
                        o.GetFloatProperty("flRelaxationFactor"));
                }).Where(static r => r.NodeA >= 0 && r.NodeB >= 0).ToArray();

            var integrators = data.GetArray("m_NodeIntegrator");
            NodeIntegrators = integrators is null
                ? []
                : integrators.Select(static o => new NodeIntegrator(
                    o.GetFloatProperty("flPointDamping"),
                    o.GetFloatProperty("flAnimationForceAttraction"),
                    o.GetFloatProperty("flAnimationVertexAttraction"),
                    o.GetFloatProperty("flGravity"))).ToArray();

            NodeCollisionRadii = data.GetFloatArray("m_NodeCollisionRadii");
            WorldCollisionNodes = data.ContainsKey("m_WorldCollisionNodes")
                ? data.GetIntegerArray("m_WorldCollisionNodes").Select(static v => (int)v).ToHashSet()
                : new HashSet<int>();

            var strayRadii = new Dictionary<int, float>();
            if (data.GetArray("m_AnimStrayRadii") is { } strayArray)
            {
                foreach (var entry in strayArray)
                {
                    var nodes = entry.GetIntegerArray("nNode");
                    if (nodes.Length > 0)
                    {
                        strayRadii[(int)nodes[0]] = entry.GetFloatProperty("flMaxDist");
                    }
                }
            }

            AnimStrayRadii = strayRadii;

            FitMatrixNodes = data.GetArray("m_FitMatrices") is { } fitMatrices
                ? fitMatrices.Select(static o => o.GetInt32Property("nNode")).ToHashSet()
                : new HashSet<int>();

            var twistNodes = new HashSet<int>();
            if (data.GetArray("m_Twists") is { } twistsArray)
            {
                foreach (var entry in twistsArray)
                {
                    twistNodes.Add(entry.GetInt32Property("nNodeOrient"));
                    twistNodes.Add(entry.GetInt32Property("nNodeEnd"));
                }
            }

            TwistNodes = twistNodes;

            var nodeBases = new Dictionary<int, NodeBasis>();
            if (data.GetArray("m_NodeBases") is { } nodeBasesArray)
            {
                foreach (var entry in nodeBasesArray)
                {
                    nodeBases[entry.GetInt32Property("nNode")] = new NodeBasis(
                        entry.GetInt32Property("nNodeX0"),
                        entry.GetInt32Property("nNodeX1"),
                        entry.GetInt32Property("nNodeY0"),
                        entry.GetInt32Property("nNodeY1"));
                }
            }

            NodeBases = nodeBases;

            (RecoveredSkinWeights, RecoveredBackSolveThreshold) = RecoverAuthoredSkinWeights(data);
        }

        // Recovers the authored per-vertex skin weights from the compiled back-solve bookkeeping - see
        // the RecoveredSkinWeights property remarks for the data model and its verification. This is the
        // same "read the compiled array directly instead of guessing a geometric rule" approach that
        // already made m_Rods/m_Twists/m_NodeBases exact; it supersedes BuildChainSkinInfluences'
        // inverse-square-distance synthesis for every vertex the compiled data still carries weights for
        // (the synthesis remains the fallback for vertices without fit entries, and for models with no
        // fit matrices at all, where this recovery is a structural no-op by construction).
        (IReadOnlyDictionary<int, (string Bone, float Weight)[]>, float?) RecoverAuthoredSkinWeights(KVObject data)
        {
            var recovered = new Dictionary<int, (string Bone, float Weight)[]>();
            var fitMatrices = data.GetArray("m_FitMatrices");
            var ctrlOffsets = data.GetArray("m_CtrlOffsets");
            if (fitMatrices is null || fitMatrices.Count == 0 || ctrlOffsets is null)
            {
                return (recovered, null);
            }

            var fitWeights = data.GetArray("m_FitWeights") ?? [];

            // flWeight per (vertex, fit bone), from each fit matrix's [begin, nEnd) range of m_FitWeights.
            var fitPerVertex = new Dictionary<int, Dictionary<int, float>>();
            var minIncludedWeight = float.MaxValue;
            var begin = 0;
            foreach (var fm in fitMatrices)
            {
                var end = fm.GetInt32Property("nEnd");
                var bone = fm.GetInt32Property("nNode");
                for (var i = begin; i < end && i < fitWeights.Count; i++)
                {
                    var node = fitWeights[i].GetInt32Property("nNode");
                    var weight = fitWeights[i].GetFloatProperty("flWeight");
                    if (!fitPerVertex.TryGetValue(node, out var boneWeights))
                    {
                        boneWeights = [];
                        fitPerVertex[node] = boneWeights;
                    }

                    boneWeights[bone] = weight;
                    minIncludedWeight = MathF.Min(minIncludedWeight, weight);
                }

                begin = end;
            }

            // The primary (rigid-anchor) bone per vertex.
            var rigidParents = new Dictionary<int, int>();
            foreach (var e in ctrlOffsets)
            {
                rigidParents[e.GetInt32Property("nCtrlChild")] = e.GetInt32Property("nCtrlParent");
            }

            // Soft-offset alphas per vertex, kept in ARRAY ORDER - the nested-lerp expansion below only
            // reproduces the fit weights when applied in the order the compiler serialized them (verified:
            // reversing the order breaks exactly the vertices with 2+ soft offsets).
            var softPerVertex = new Dictionary<int, List<(int Parent, float Alpha)>>();
            if (data.GetArray("m_CtrlSoftOffsets") is { } softOffsets)
            {
                foreach (var e in softOffsets)
                {
                    var child = e.GetInt32Property("nCtrlChild");
                    if (!softPerVertex.TryGetValue(child, out var list))
                    {
                        list = [];
                        softPerVertex[child] = list;
                    }

                    list.Add((e.GetInt32Property("nCtrlParent"), e.GetFloatProperty("flAlpha")));
                }
            }

            var maxOmittedWeight = 0f;
            foreach (var (node, primary) in rigidParents)
            {
                if (primary < 0 || primary >= CtrlNames.Length)
                {
                    continue;
                }

                // A pinned vertex is anchored rigidly to exactly its primary bone.
                if (IsStatic(node))
                {
                    recovered[node] = [(CtrlNames[primary], 1f)];
                    continue;
                }

                // A simulated vertex needs at least one fit entry to anchor the ABSOLUTE weight scale
                // (soft-offset alphas alone are renormalized over the dynamic bones). Without any fit
                // entry: no soft offsets either means the compiled data itself says the vertex is
                // anchored 100% to its primary bone (dark_willow's lantern/waist-seam vertices - the
                // geometric fallback used to smear these across nearby chain joints, producing extra
                // m_CtrlSoftOffsets entries absent from the original); with soft offsets but no fit
                // anchor the scale is unknowable, so leave those to the fallback.
                if (!fitPerVertex.TryGetValue(node, out var fits))
                {
                    if (!softPerVertex.ContainsKey(node))
                    {
                        recovered[node] = [(CtrlNames[primary], 1f)];
                    }

                    continue;
                }

                // Expand the nested lerps: start at weight 1 on the primary; each soft offset scales
                // everything accumulated so far by flAlpha and gives (1 - flAlpha) to its own parent.
                var dynamicWeights = new List<(int Bone, float Weight)> { (primary, 1f) };
                if (softPerVertex.TryGetValue(node, out var softs))
                {
                    foreach (var (parent, alpha) in softs)
                    {
                        for (var i = 0; i < dynamicWeights.Count; i++)
                        {
                            dynamicWeights[i] = (dynamicWeights[i].Bone, dynamicWeights[i].Weight * alpha);
                        }

                        var existing = dynamicWeights.FindIndex(w => w.Bone == parent);
                        if (existing >= 0)
                        {
                            dynamicWeights[existing] = (parent, dynamicWeights[existing].Weight + (1f - alpha));
                        }
                        else
                        {
                            dynamicWeights.Add((parent, 1f - alpha));
                        }
                    }
                }

                // Absolute scale from the largest fit-covered component (numerically safest anchor).
                var scale = 1f;
                var bestNormalized = 0f;
                foreach (var (bone, normalized) in dynamicWeights)
                {
                    if (normalized > bestNormalized && fits.TryGetValue(bone, out var fitValue) && normalized > 0f)
                    {
                        bestNormalized = normalized;
                        scale = fitValue / normalized;
                    }
                }

                var influences = new List<(string Bone, float Weight)>(dynamicWeights.Count + 1);
                var total = 0f;
                foreach (var (bone, normalized) in dynamicWeights)
                {
                    var weight = normalized * scale;
                    if (weight <= 0f || bone >= CtrlNames.Length)
                    {
                        continue;
                    }

                    influences.Add((CtrlNames[bone], weight));
                    total += weight;
                    if (!fits.ContainsKey(bone))
                    {
                        maxOmittedWeight = MathF.Max(maxOmittedWeight, weight);
                    }
                }

                // The rest of the authored weight went to a static bone (below the original's back-solve
                // threshold or simply not back-solvable) - the primary's nearest static real ancestor.
                var remainder = 1f - total;
                if (remainder > 1e-4f)
                {
                    var anchor = FindStaticRealAncestor(primary);
                    if (anchor >= 0)
                    {
                        influences.Add((CtrlNames[anchor], remainder));
                    }
                }

                influences.Sort(static (a, b) => b.Weight.CompareTo(a.Weight));
                recovered[node] = [.. influences];
            }

            float? threshold = maxOmittedWeight > 0f && minIncludedWeight < float.MaxValue && maxOmittedWeight < minIncludedWeight
                ? (maxOmittedWeight + minIncludedWeight) * 0.5f
                : null;
            return (recovered, threshold);
        }

        // Walks the skeleton-parent chain from `node` (exclusive) up to the first STATIC real-bone
        // control node - the bone the author's remaining (non-back-solved) skin weight is assigned to.
        int FindStaticRealAncestor(int node)
        {
            var p = node >= 0 && node < SkelParents.Length ? SkelParents[node] : -1;
            var guard = 0;
            while (p >= 0 && p < CtrlNames.Length && guard++ < 256)
            {
                if (!IsProxyNodeName(CtrlNames[p]) && IsStatic(p))
                {
                    return p;
                }

                p = p < SkelParents.Length ? SkelParents[p] : -1;
            }

            return -1;
        }

        // Reads an array of cloth faces (m_Quads/m_Tris), returning each face's nNode index list.
        static int[][] ReadNodeIndexArray(KVObject data, string key, int expectedLength)
        {
            var arr = data.GetArray(key);
            if (arr is null)
            {
                return [];
            }

            var faces = new List<int[]>(arr.Count);
            foreach (var face in arr)
            {
                var nodes = face.GetIntegerArray("nNode");
                if (nodes.Length >= expectedLength)
                {
                    faces.Add(nodes.Take(expectedLength).Select(static v => (int)v).ToArray());
                }
            }

            return [.. faces];
        }

        /// <summary>
        /// Gets a value indicating whether this FeModel carries any control nodes.
        /// </summary>
        public bool HasData => CtrlNames.Length > 0;

        /// <summary>
        /// Returns whether a control-node name is an auto-generated cloth proxy node (not a real skeleton bone).
        /// </summary>
        public static bool IsProxyNodeName(string? name)
            => string.IsNullOrEmpty(name) || name.StartsWith('$');

        /// <summary>
        /// Whether the cloth drives any REAL (non auto-generated proxy) skeleton bone: at least one
        /// position-driven control node (index &gt;= <see cref="FirstPositionDrivenNode"/>) carries a real
        /// bone name. Those bones are back-solved from the simulated proxy nodes - whether the mechanism is
        /// <c>m_FitMatrices</c> (dark_willow's Coattail/HairStrand) or <c>m_CtrlOffsets</c> alone with no
        /// fit matrices at all (primal_beast's leg_chain/back_chain/neck_skin, m_FitMatrices empty). It is
        /// the signal that a reconstructed proxy mesh must emit <c>back_solve_joints = true</c> so those
        /// real bones move with the sim (without it the render mesh skinned to them stays frozen), and it
        /// is a superset of <see cref="FitMatrixNodes"/> being non-empty.
        /// </summary>
        public bool DrivesRealBones
        {
            get
            {
                for (var i = FirstPositionDrivenNode; i < CtrlNames.Length; i++)
                {
                    if (!IsProxyNodeName(CtrlNames[i]))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Returns whether the node at <paramref name="node"/> is a static (pinned, invMass == 0) anchor.
        /// </summary>
        public bool IsStatic(int node)
            => node >= 0 && node < NodeInvMasses.Length && NodeInvMasses[node] == 0f;

        /// <summary>
        /// Walks the skeleton-parent chain from <paramref name="node"/> up to the first real (non
        /// auto-generated cloth proxy) control-node name. This is the skeleton bone that an auto-generated
        /// proxy node is anchored/skinned to.
        /// </summary>
        public string? ResolveSkinBone(int node)
        {
            var index = ResolveSkinBoneNode(node);
            return index >= 0 ? CtrlNames[index] : null;
        }

        // Same walk as ResolveSkinBone, returning the control-node index of the bone instead of its name.
        int ResolveSkinBoneNode(int node)
        {
            var p = node >= 0 && node < SkelParents.Length ? SkelParents[node] : -1;
            var guard = 0;
            while (p >= 0 && p < CtrlNames.Length && guard++ < 256)
            {
                if (!IsProxyNodeName(CtrlNames[p]))
                {
                    return p;
                }

                p = p < SkelParents.Length ? SkelParents[p] : -1;
            }

            return -1;
        }

        // Builds the smooth skin influences of a SIMULATED proxy vertex: inverse-distance weights over the
        // nearest joints of the anchor's bone chain (up to 4, thresholded - see below). The first-real-
        // ancestor walk alone under-covers chains (dark_willow's Coattail_1_L/R middle joints had no
        // skinned vertex at all and dropped out of the recompiled FeModel), and hard single-bone weights
        // (even picked by true nearest-distance, re-verified directly against the compiler) make it drive
        // every chain joint as a point rope instead of back-solving a fit matrix from its weighted
        // vertices - dark_willow's 8 fit matrices collapsed to 1 with hard single-bone weights.
        (string Bone, float Weight)[] BuildChainSkinInfluences(int node)
        {
            var anchor = ResolveSkinBoneNode(node);
            if (anchor < 0)
            {
                return [];
            }

            if (node >= InitPosePositions.Length)
            {
                return [(CtrlNames[anchor], 1f)];
            }

            // Inverse-square distance weights over the (up to) four nearest chain joints. The wider spread
            // matters: a fit matrix needs several well-separated weighted points per bone, or the compiler
            // falls back to a point rope for that joint (dark_willow's Coattail_2 chain tips with 2-joint
            // weighting still compiled as ropes).
            //
            // Weight each candidate joint by inverse-square distance. The original per-vertex weights are
            // hand-painted art data, not a function of bone-to-vertex distance (implied falloff exponents
            // measured from dark_willow's own weight ratios range wildly from -13 to +5), so no distance
            // formula reproduces them exactly; inverse-square is the closest general fit for the back-solve.
            var weighted = new List<(int Node, float Distance)>();
            foreach (var candidate in GetChainComponent(anchor))
            {
                if (candidate < InitPosePositions.Length)
                {
                    weighted.Add((candidate, Vector3.Distance(InitPosePositions[node], InitPosePositions[candidate])));
                }
            }

            if (weighted.Count == 0)
            {
                return [(CtrlNames[anchor], 1f)];
            }

            weighted.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
            if (weighted[0].Distance <= 1e-6f)
            {
                return [(CtrlNames[weighted[0].Node], 1f)];
            }

            // Keep only influences within 16% of the strongest weight (of the 4 nearest). A flat Take(4)
            // over-covers each bone (dark_willow: 118 m_FitWeights vs the original's 89) - the compiler
            // back-solves a fit matrix from whichever vertices reference a bone, so extra long-tail
            // influences just add noise entries. Thresholding lets tightly-clustered vertices keep 2-3
            // influences and sparse ones keep 4, without dropping below 2 (2 candidates collapse 2 of 8 fit
            // matrices back to ropes - see BuildChainSkinInfluences remarks). 0.16 is the closest fit: 88
            // m_FitWeights (vs 89) with all 8 fit matrices intact, measured against the back-solved fit's
            // actual vCenter/bone-translation, not just m_FitWeights count. It is not globally optimal on
            // every bone (lower thresholds trade Coattail_0_L/R accuracy for Coattail_2_L/R), so validate any
            // change against the FULL per-bone vCenter table, not an average, which can hide a per-bone
            // regression behind a gain elsewhere.
            var top = new List<(int Node, float Weight)>(4);
            foreach (var (candidate, distance) in weighted.Take(4))
            {
                top.Add((candidate, 1f / (distance * distance)));
            }

            var maxWeight = top[0].Weight;
            var influences = new List<(string Bone, float Weight)>(4);
            var total = 0f;
            foreach (var (candidate, weight) in top)
            {
                if (weight < maxWeight * 0.16f)
                {
                    continue;
                }

                influences.Add((CtrlNames[candidate], weight));
                total += weight;
            }

            return [.. influences.Select(i => (i.Bone, i.Weight / total))];
        }

        // The real-bone control nodes on the SAME physical chain as `bone`: its real-bone ancestors up to
        // (but not through) the nearest BRANCH POINT - a real ancestor with more than one real-bone child -
        // plus every real descendant below that point.
        //
        // Two sibling chains that only share a common static real ancestor (e.g. dark_willow's
        // CoattailBase_0, parent of BOTH Coattail_0_L and Coattail_0_R) must NOT be merged into one
        // candidate pool: climbing past the branch point would let a proxy vertex's "nearest 4" search draw
        // candidates from BOTH sides at once, so a left-side bone's fit picks up right-side vertices with a
        // spuriously large weight and its back-solved weighted centroid (vCenter) shifts 6-9 units off the
        // original. The original only mixes sides for vertices anchored directly AT the branch point
        // (Coattail_0_L/R), and even then with a small secondary weight; deeper joints have zero cross-side
        // contribution. Stopping the upward walk at the first branch point isolates each side to its own
        // chain; HairStrand_0/1 (whose ancestor "head" has one cloth descendant, never branches) are
        // unaffected.
        List<int> GetChainComponent(int bone)
        {
            var n = CtrlNames.Length;

            // realParent[i]: parent among real bones, or -1.
            var realParent = new int[n];
            for (var i = 0; i < n; i++)
            {
                realParent[i] = -1;
                if (IsProxyNodeName(CtrlNames[i]))
                {
                    continue;
                }

                var p = i < SkelParents.Length ? SkelParents[i] : -1;
                if (p >= 0 && p < n && !IsProxyNodeName(CtrlNames[p]))
                {
                    realParent[i] = p;
                }
            }

            // childCount[p]: number of real-bone nodes whose real parent is p - used to detect a branch
            // point (a shared ancestor of two or more distinct chains) that the upward walk must stop at.
            var childCount = new int[n];
            for (var i = 0; i < n; i++)
            {
                if (realParent[i] >= 0)
                {
                    childCount[realParent[i]]++;
                }
            }

            // A vertex whose OWN nearest real ancestor already IS a branch point (not just an ancestor
            // reached while climbing) must not be smeared across every sibling chain hanging off it either.
            // Verified on dark_willow: EVERY one of the 9 proxy vertices whose real anchor is CoattailBase_0
            // itself (the hub bone, not either coattail side) contributes to ZERO m_FitMatrices in the
            // original (not a small secondary weight either - genuinely absent from every fit) - the
            // original pins these hub-anchored vertices to the hub bone alone rather than distributing them
            // across its children.
            if (childCount[bone] > 1)
            {
                return [bone];
            }

            var root = bone;
            var guard = 0;
            while (realParent[root] >= 0 && childCount[realParent[root]] <= 1 && guard++ < 256)
            {
                root = realParent[root];
            }

            var component = new List<int>();
            var stack = new Stack<int>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                component.Add(current);
                for (var i = 0; i < n; i++)
                {
                    if (realParent[i] == current)
                    {
                        stack.Push(i);
                    }
                }
            }

            return component;
        }

        /// <summary>
        /// The auto-generated cloth proxy mesh (the cloth "sheet"), reconstructed from the FeModel surface
        /// topology. Vertices are the control nodes referenced by <see cref="Quads"/>/<see cref="Tris"/>
        /// (i.e. the <c>$cloth_*</c> proxy nodes); the real bone-chain nodes are intentionally excluded so a
        /// proxy-mesh recompile and a <c>ClothChain</c> recompile do not drive the same nodes twice.
        /// </summary>
        public sealed class ProxyMesh
        {
            /// <summary>Gets the original FeModel control-node index of each proxy vertex.</summary>
            public required int[] NodeIndices { get; init; }
            /// <summary>Gets the model-space rest position of each proxy vertex.</summary>
            public required Vector3[] Positions { get; init; }
            /// <summary>Gets the per-vertex <c>cloth_enable</c> flag (1 = simulated, 0 = pinned anchor).</summary>
            public required float[] ClothEnable { get; init; }
            /// <summary>
            /// Gets the per-vertex goal/force attraction toward the animated pose (recovered from the node
            /// integrator's <c>flAnimationForceAttraction</c>, clamped to the 0..1 paint range). Higher = the
            /// cloth hugs the body more tightly. Emitted as the modern <c>cloth_goal_strength_v2$0</c> paint.
            /// </summary>
            public required float[] GoalStrength { get; init; }
            /// <summary>
            /// Gets the per-vertex goal damping paint, recovered by inverting the vertex-attraction
            /// response (<see cref="GoalDampingFromAttraction"/>). Legacy models with out-of-range
            /// attraction clamp to the strongest reproducible damping.
            /// </summary>
            public required float[] GoalDamping { get; init; }
            /// <summary>Gets the per-vertex self-collision radius (recovered from <c>m_NodeCollisionRadii</c>).</summary>
            public required float[] CollisionRadius { get; init; }
            /// <summary>Gets the per-vertex friction (recovered from <c>m_DynNodeFriction</c>), 0..1 paint range.</summary>
            public required float[] Friction { get; init; }
            /// <summary>Gets the per-vertex air drag (recovered from the FeModel air-drag scalar), 0..1 paint range.</summary>
            public required float[] Drag { get; init; }
            /// <summary>Gets the per-vertex ground-collision weight (recovered where available), 0..1 paint range.</summary>
            public required float[] GroundCollision { get; init; }
            /// <summary>
            /// Gets the per-vertex gravity (the integrator's <c>flGravity</c>, verbatim - the
            /// <c>cloth_gravity$0</c> paint compiles into <c>flGravity</c> with no scaling, unlike the
            /// /360 <c>gravity_z</c> KV field on ClothNode/ClothChain joints). Without the stream the
            /// compiler defaults every vertex to 360, silently discarding authored variation -
            /// dark_willow's hair strands and paper lantern are authored nearly weightless (flGravity=1).
            /// </summary>
            public required float[] Gravity { get; init; }
            /// <summary>
            /// Gets the RAW compiled <c>flAnimationVertexAttraction</c> per vertex. Values above 1.0
            /// (legacy-era compiles: dark_willow ships 15/10.5/6/5.25) are a legacy platform ceiling:
            /// unreachable via the modern goal_strength/goal_damping pipeline, the direct
            /// <c>cloth_animation_attract$0</c> stream is ignored by the proxy importer, and no modern
            /// Valve model ships va &gt; 1. Kept as data for diagnostics, not re-authored.
            /// </summary>
            public required float[] VertexAttraction { get; init; }
            /// <summary>
            /// Gets the skeleton bone influences of each proxy vertex. Pinned anchors carry a single
            /// weight-1 influence on their anchor bone. Simulated vertices are SMOOTHLY weighted across the
            /// nearest joints of the anchor's chain: the compiler back-solves a bone with a proper fit
            /// matrix only when enough weighted vertices reference it - hard single-bone skinning degrades
            /// every chain joint to a point-driven rope (dark_willow: original has 8 fit matrices / 89 fit
            /// weights, hard skinning compiled to 8 ropes / 1 fit and a much denser rod network).
            /// </summary>
            public required (string Bone, float Weight)[][] SkinInfluences { get; init; }
            /// <summary>
            /// Gets the faces (proxy-vertex index quads and triangles) covering the sheet, preserving the
            /// original quad/tri split. Triangulating the quads instead makes the compiler re-derive a much
            /// denser quad/rod network (dark_willow: 15 quads/61 rods became 43 quads/163 rods) and the
            /// recompiled cloth turns rigid.
            /// </summary>
            public required List<int[]> Faces { get; init; }
            /// <summary>Gets the number of simulated (cloth_enable == 1) vertices.</summary>
            public int SimulatedCount { get; init; }
            /// <summary>Gets the number of pinned (cloth_enable == 0) vertices.</summary>
            public int PinnedCount { get; init; }
            /// <summary>
            /// Gets whether the cloth importer is expected to silently PRUNE one or more of this synthesised
            /// island's vertices, which would make any explicit <c>ClothSpring</c> (m_Rods) referencing a
            /// pruned vertex a hard "Cannot find node $cloth_mXpY" compile failure. Two importer behaviours
            /// were reverse-engineered as the cause (verified against real recompiles - see
            /// <see cref="ComputeDropRisk"/>): (1) a pinned vertex whose face-neighbours are ALL pinned (a
            /// fully-static mesh region the solver discards - mars), and (2) a near-coincident vertex pair
            /// the importer welds (chain-ribbon end caps - hoodwink). When true, <c>AddClothProxySprings</c>
            /// skips this island's explicit rods and lets the compiler auto-derive them from the surface
            /// instead (guaranteed to compile, at the cost of exact rod topology for this one island). False
            /// for cleanly-triangulated islands, which keep their exact reconstructed rods.
            /// </summary>
            public bool IsDropRisk { get; init; }
        }

        /// <summary>
        /// Reconstructs the cloth proxy sheets from the FeModel surface arrays, one per connected island.
        /// Original models ship each cloth piece as its OWN proxy mesh (the compiled node names encode it:
        /// <c>$cloth_m0p3</c> = mesh 0, point 3; dark_willow has m0/m1/m2), so a merged single sheet
        /// changes how the compiler numbers and groups the nodes. Returns an empty list when the FeModel
        /// has no surface - e.g. pure bone-chain cloth that only needs ClothChain.
        /// </summary>
        public List<ProxyMesh> BuildProxyMeshes()
        {
            var result = new List<ProxyMesh>();
            var coveredNodes = new HashSet<int>();
            var merged = BuildProxyMesh();

            if (merged is not null)
            {
                coveredNodes.UnionWith(merged.NodeIndices);

                // Union-find over the merged sheet's local vertex indices by face membership.
                var count = merged.NodeIndices.Length;
                var groupOf = Enumerable.Range(0, count).ToArray();
                int Find(int x) { while (groupOf[x] != x) { x = groupOf[x] = groupOf[groupOf[x]]; } return x; }
                foreach (var face in merged.Faces)
                {
                    for (var i = 1; i < face.Length; i++)
                    {
                        groupOf[Find(face[0])] = Find(face[i]);
                    }
                }

                // Islands ordered by smallest control-node index, matching the original mesh order.
                var islands = Enumerable.Range(0, count)
                    .GroupBy(Find)
                    .OrderBy(g => g.Min(v => merged.NodeIndices[v]))
                    .ToList();

                if (islands.Count == 1)
                {
                    result.Add(merged);
                }
                else
                {
                    foreach (var island in islands)
                    {
                        // Ascending MERGED index preserves the authored per-island vertex order the
                        // merged mesh was already sorted into (SortByAuthoredVertexOrder) - re-sorting
                        // by global node index here would undo it.
                        var vertices = island.OrderBy(v => v).ToArray();
                        var remap = new Dictionary<int, int>(vertices.Length);
                        for (var i = 0; i < vertices.Length; i++)
                        {
                            remap[vertices[i]] = i;
                        }

                        T[] Take<T>(T[] source) => [.. vertices.Select(v => source[v])];

                        result.Add(new ProxyMesh
                        {
                            NodeIndices = Take(merged.NodeIndices),
                            Positions = Take(merged.Positions),
                            ClothEnable = Take(merged.ClothEnable),
                            GoalStrength = Take(merged.GoalStrength),
                            GoalDamping = Take(merged.GoalDamping),
                            CollisionRadius = Take(merged.CollisionRadius),
                            Friction = Take(merged.Friction),
                            Drag = Take(merged.Drag),
                            GroundCollision = Take(merged.GroundCollision),
                            Gravity = Take(merged.Gravity),
                            VertexAttraction = Take(merged.VertexAttraction),
                            SkinInfluences = Take(merged.SkinInfluences),
                            Faces = [.. merged.Faces.Where(f => remap.ContainsKey(f[0])).Select(f => f.Select(v => remap[v]).ToArray())],
                            SimulatedCount = vertices.Count(v => merged.ClothEnable[v] != 0f),
                            PinnedCount = vertices.Count(v => merged.ClothEnable[v] == 0f),
                        });
                    }
                }
            }

            // A $cc proxy node whose SKELETON PARENT is a reconstructed bone-chain joint is the compiler's
            // own auto-generated proxy of that ClothChain: on marci every real chain bone (BackpackStrapLwr_
            // K_R, GemRibbon_K_R, Ponytail_K, HairA_K ...) carries 1-2 "$cc<bone>_<n>" proxy nodes parented
            // straight to it. That chain is emitted as a ClothChain (see BuildBoneChains) and the compiler
            // regenerates these proxies FROM it, so rebuilding them here as a rod-only proxy mesh would both
            // double-drive the bones and - for a curved 2-wide ribbon - collapse in the compiler's 2D
            // cloth-mesh import (later rungs weld onto earlier ones, verts get pruned, every explicit
            // ClothSpring to a pruned vert becomes a fatal "Cannot find node $cloth_mXpY" orphan). Mark them
            // covered so the rod-only pass leaves them to the ClothChain. A genuine sheet ($cloth_m panels,
            // or a $cc panel with no real chain bones) has no such parent link and is untouched.
            // Only chains emitted as an INDEPENDENT ClothChain get their proxies suppressed. A chain any of
            // whose joints is back-solved by a fit matrix (dark_willow's Coattail/HairStrand, legion's
            // Banner) is NOT emitted as a ClothChain - it is driven THROUGH its proxy mesh - so suppressing
            // that proxy would delete the cloth entirely (regressed legion_commander: "cloth lost after
            // recompile"). Same fit-matrix exclusion ModelExtract uses to pick independentChains.
            var chainBoneNodes = BuildBoneChains()
                .Where(chain => !chain.Joints.Any(joint => FitMatrixNodes.Contains(joint.Node)))
                .SelectMany(static c => c.Joints).Select(static j => j.Node).ToHashSet();
            if (chainBoneNodes.Count > 0)
            {
                for (var node = 0; node < CtrlNames.Length; node++)
                {
                    if (!IsProxyNodeName(CtrlNames[node]))
                    {
                        continue;
                    }

                    var parent = node < SkelParents.Length ? SkelParents[node] : -1;
                    if (parent >= 0 && chainBoneNodes.Contains(parent))
                    {
                        coveredNodes.Add(node);
                    }
                }
            }

            // "$cloth_*" control nodes that carry no m_Quads/m_Tris of their own: a plain
            // ClothProxyMeshFile import compiles down to a bare distance-constraint (m_Rods) network,
            // discarding the authored surface (see MakeClothQuad remarks in ModelExtract.ValveModel.cs),
            // so these nodes would otherwise be silently dropped instead of round-tripping as a sheet.
            result.AddRange(BuildProxyMeshesFromRodsOnly(coveredNodes));

            return result;
        }

        /// <summary>
        /// Reconstructs the cloth proxy mesh (sheet) from the FeModel surface arrays as ONE merged mesh.
        /// Returns null when the FeModel has no surface (no quads/tris) - e.g. a pure bone-chain cloth
        /// that only needs ClothChain.
        /// </summary>
        public ProxyMesh? BuildProxyMesh()
        {
            if ((Quads.Length == 0 && Tris.Length == 0) || InitPosePositions.Length == 0)
            {
                return null;
            }

            // Collect the control nodes actually used by the surface, in ascending order. These are the
            // proxy-mesh ("sheet") nodes; the bone-chain nodes never appear in a quad/tri.
            var referenced = new SortedSet<int>();
            void Collect(int[][] faces)
            {
                foreach (var face in faces)
                {
                    foreach (var n in face)
                    {
                        if (n >= 0 && n < InitPosePositions.Length)
                        {
                            referenced.Add(n);
                        }
                    }
                }
            }

            Collect(Quads);
            Collect(Tris);

            if (referenced.Count == 0)
            {
                return null;
            }

            var nodeIndices = referenced.ToArray();
            SortByAuthoredVertexOrder(nodeIndices);
            var remap = new Dictionary<int, int>(nodeIndices.Length);
            for (var i = 0; i < nodeIndices.Length; i++)
            {
                remap[nodeIndices[i]] = i;
            }

            var nodeFriction = Data.GetFloatArray("m_DynNodeFriction");

            var positions = new Vector3[nodeIndices.Length];
            var clothEnable = new float[nodeIndices.Length];
            var goalStrength = new float[nodeIndices.Length];
            var goalDamping = new float[nodeIndices.Length];
            var collisionRadius = new float[nodeIndices.Length];
            var friction = new float[nodeIndices.Length];
            var drag = new float[nodeIndices.Length];
            var groundCollision = new float[nodeIndices.Length];
            var gravity = new float[nodeIndices.Length];
            var vertexAttraction = new float[nodeIndices.Length];
            var skinInfluences = new (string Bone, float Weight)[nodeIndices.Length][];
            var simulated = 0;
            var pinned = 0;

            for (var i = 0; i < nodeIndices.Length; i++)
            {
                var node = nodeIndices[i];
                positions[i] = InitPosePositions[node];

                var vertex = ComputeProxyVertexData(node, nodeFriction);
                clothEnable[i] = vertex.IsSim ? 1f : 0f;
                if (vertex.IsSim) { simulated++; } else { pinned++; }
                skinInfluences[i] = vertex.SkinInfluences;
                goalStrength[i] = vertex.GoalStrength;
                goalDamping[i] = vertex.GoalDamping;
                collisionRadius[i] = vertex.CollisionRadius;
                friction[i] = vertex.Friction;
                drag[i] = vertex.Drag;
                groundCollision[i] = 0f;
                gravity[i] = vertex.Gravity;
                vertexAttraction[i] = vertex.VertexAttraction;
            }

            // Faces are fed in the ORIGINAL compile's SIMD lane order (with each face's node order taken
            // from its lane) instead of the compiled m_Quads/m_Tris arrays' own order: those arrays are
            // node-sorted on output, but the SIMD constraint packer consumes the authored DMX face order -
            // feeding the sorted arrays back packs DIFFERENT groups whose leftover lanes get padded with
            // LIVE full-weight replicas of real constraints (dark_willow: 9 of 26 tris solved 2-3x per
            // iteration - measurably stiffer cloth than the original). The lane-major expansion is the
            // closest recoverable stand-in for the authored face order.
            var faces = new List<int[]>(Quads.Length + Tris.Length);
            foreach (var q in OrderFacesBySimdLanes(Quads, "m_SimdQuads"))
            {
                faces.Add([remap[q[0]], remap[q[1]], remap[q[2]], remap[q[3]]]);
            }

            foreach (var t in OrderFacesBySimdLanes(Tris, "m_SimdTris"))
            {
                faces.Add([remap[t[0]], remap[t[1]], remap[t[2]]]);
            }

            return new ProxyMesh
            {
                NodeIndices = nodeIndices,
                Positions = positions,
                ClothEnable = clothEnable,
                GoalStrength = goalStrength,
                GoalDamping = goalDamping,
                CollisionRadius = collisionRadius,
                Friction = friction,
                Drag = drag,
                GroundCollision = groundCollision,
                Gravity = gravity,
                VertexAttraction = vertexAttraction,
                SkinInfluences = skinInfluences,
                Faces = faces,
                SimulatedCount = simulated,
                PinnedCount = pinned,
            };
        }

        // Per-node cloth paint values recovered from the FeModel solver data (goal attraction, damping,
        // collision/friction/drag, gravity, and skin influences), shared by every proxy-mesh
        // reconstruction path - quad/tri-driven (BuildProxyMesh) and rod-only (BuildProxyMeshFromNodeSet).
        readonly record struct ProxyVertexData(
            bool IsSim,
            float GoalStrength,
            float GoalDamping,
            float CollisionRadius,
            float Friction,
            float Drag,
            float Gravity,
            float VertexAttraction,
            (string Bone, float Weight)[] SkinInfluences);

        ProxyVertexData ComputeProxyVertexData(int node, float[] nodeFriction)
        {
            var isSim = node < NodeInvMasses.Length && NodeInvMasses[node] != 0f;

            // The authored weights recovered verbatim from the compiled back-solve data take priority -
            // they reproduce the original fit matrices exactly (see RecoveredSkinWeights remarks).
            // Otherwise: pinned anchors follow their animated anchor bone with full weight, and simulated
            // vertices get smooth inverse-distance weights across the anchor's chain joints (see
            // BuildChainSkinInfluences docs) so the compiler back-solves every chain joint with a
            // proper fit.
            if (!RecoveredSkinWeights.TryGetValue(node, out var skinInfluences))
            {
                if (isSim)
                {
                    skinInfluences = BuildChainSkinInfluences(node);
                }
                else
                {
                    var anchor = ResolveSkinBone(node);
                    skinInfluences = anchor is not null ? [(anchor, 1f)] : [];
                }
            }

            // Recover the per-node paint values. These are 0..1 paint sliders in the cloth editor, so
            // the recovered quantities are clamped into that range (the editor shows a blank/0 slider
            // for out-of-range values - this is what made the old cloth_goal_damping=6.0 paint break).
            var integrator = GetIntegrator(node);

            // The compiler CUBES the painted goal strength: flAnimationForceAttraction =
            // (cloth_goal_strength_v2)^3 (measured exact: 0.24^3=0.013824, 0.45^3=0.091125,
            // 0.75^3=0.421875). Paint the cube ROOT of the recovered force attraction so the compiler's
            // cubing reproduces the ORIGINAL attraction instead of one that is too weak by a cube (the
            // "loose vs tight" / clipping difference). cbrt of a 0..1 value stays in 0..1.
            //
            // goal_damping drives flAnimationVertexAttraction (va) through an exponential-saturation curve
            // that asymptotes to 1.0. Legacy-compiled nodes ship va > 1 (dark_willow 5.25..15, void_spirit
            // up to 21) - structurally impossible for the modern compiler and NOT reproducible: RE of both
            // vphysics2.dll (runtime) and physicsbuilder.dll (compiler) proved the legacy attraction solver
            // path (node-flag bits 9,10) was removed - goal_strength is hard-clamped to [0,1] at read
            // (fa<=1), the raw cloth_animation_attract / _force_attract inputs are dead registry entries no
            // consumer reads, and the 0x600 legacy flag mask is never produced anywhere in the compiler.
            // For va > 1, GoalDampingFromAttraction returns its 1.0 maximum (excess/ceiling >= 1), painting
            // the strongest attraction the modern path can express - the closest faithful reproduction of
            // the original's own compiled values, with no feel-calibrated constants (this saturates va to
            // ~0.98, which reads stiffer than the legacy snap-then-relax, an accepted platform ceiling).
            var goalStrength = MathF.Cbrt(Math.Clamp(integrator.ForceAttraction, 0f, 1f));
            var goalDamping = GoalDampingFromAttraction(goalStrength, integrator.VertexAttraction - integrator.ForceAttraction);

            var collisionRadius = GetCollisionRadius(node);

            // m_DynNodeFriction is indexed by dynamic node, like m_NodeCollisionRadii.
            var dynamicIndex = node - StaticNodeCount;
            var friction = dynamicIndex >= 0 && dynamicIndex < nodeFriction.Length
                ? Math.Clamp(nodeFriction[dynamicIndex], 0f, 1f)
                : 0f;

            // The cloth_drag paint compiles to flPointDamping = paint * 30 (measured exact: 0.2 -> 6.0,
            // 0.5 -> 15.0), so the paint is recovered as pd/30. This velocity damping is what keeps the
            // original cloth calm - a 0 paint leaves the sheet swinging undamped (dark_willow ships
            // pd=6 on every simulated sheet node).
            var drag = Math.Clamp(integrator.PointDamping / ClothDragPointDampingScale, 0f, 1f);

            // Per-vertex gravity: the cloth_gravity$0 paint compiles into flGravity VERBATIM (measured:
            // painting 0.002778 lands 0.002778, painting 1.0 lands 1 - no 360 scale, unlike the
            // gravity_z KV field on ClothNode/ClothChain joints). Without the stream the compiler
            // defaults every vertex to 360, silently discarding authored variation - dark_willow's hair
            // strands and paper lantern are authored nearly weightless (flGravity=1) while the coattail
            // is full weight (360); without the stream all of them compile at 360.
            var gravity = integrator.Gravity;

            // The raw compiled vertex attraction is NOT re-authorable: values above 1.0 (legacy-era
            // compiles: dark_willow ships 15/10.5/6/5.25) exceed the modern goal_strength/goal_damping
            // pipeline's structural ceiling, the cloth_animation_attract$0 paint stream is ignored by
            // the proxy-mesh importer (verified inert on a clean compile; the name belongs to
            // ClothMapFilter's map list, not the importer's), and no modern Valve model ships va > 1
            // (checked muerta/ringmaster/kez/primal_beast) - a genuine legacy platform ceiling.
            var vertexAttraction = integrator.VertexAttraction;

            return new ProxyVertexData(isSim, goalStrength, goalDamping, collisionRadius, friction, drag, gravity, vertexAttraction, skinInfluences);
        }

        // Extracts the mesh index the compiler already encodes in an auto-generated proxy control-node
        // name ("$cloth_m3p12" -> 3), or -1 if the name does not follow that convention.
        static int ParseProxyMeshIndex(string name)
        {
            const string Prefix = "$cloth_m";
            if (!name.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return -1;
            }

            var pIndex = name.IndexOf('p', Prefix.Length);
            if (pIndex < 0)
            {
                return -1;
            }

            return int.TryParse(name.AsSpan(Prefix.Length, pIndex - Prefix.Length), out var index) ? index : -1;
        }

        // Extracts the AUTHORED local vertex index from an auto-generated proxy control-node name
        // ("$cloth_m3p12" -> 12) - the compiler assigns p{N} as the vertex's position in the authored
        // DMX's own position array, so the original author's vertex ORDER survives compilation inside the
        // node names. int.MaxValue for non-proxy names.
        static int ParseProxyVertexIndex(string name)
        {
            const string Prefix = "$cloth_m";
            if (!name.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return int.MaxValue;
            }

            var pIndex = name.IndexOf('p', Prefix.Length);
            if (pIndex < 0)
            {
                return int.MaxValue;
            }

            return int.TryParse(name.AsSpan(pIndex + 1), out var index) ? index : int.MaxValue;
        }

        // Returns the given faces reordered to the original compile's SIMD lane-major order (first
        // occurrence of each face wins; padding replicas dedup away), with each face's own node order
        // taken from its first SIMD lane. Faces without a SIMD lane (shouldn't happen - the SIMD arrays
        // pack every logical face) keep their original array order at the end. See the call site in
        // BuildProxyMesh for why the order matters.
        int[][] OrderFacesBySimdLanes(int[][] faces, string simdKey)
        {
            var simd = Data.GetArray(simdKey);
            if (simd is null || simd.Count == 0 || faces.Length == 0)
            {
                return faces;
            }

            var rows = faces[0].Length;
            static string FaceKey(IEnumerable<int> nodes) => string.Join(',', nodes.Order());

            var remaining = new Dictionary<string, List<int[]>>();
            foreach (var face in faces)
            {
                var k = FaceKey(face);
                if (!remaining.TryGetValue(k, out var list))
                {
                    list = [];
                    remaining[k] = list;
                }

                list.Add(face);
            }

            var ordered = new List<int[]>(faces.Length);
            foreach (var entry in simd)
            {
                // nNode is rows x 4 lanes; the KV3 form may present it as nested arrays or one
                // flattened row-major array - handle both.
                if (!entry.TryGetValue("nNode", out var nNodeValue) || !nNodeValue.IsArray)
                {
                    return faces;
                }

                var flat = new List<int>(rows * 4);
                foreach (var row in nNodeValue.AsArraySpan())
                {
                    if (row.IsArray)
                    {
                        foreach (var lane in row.AsArraySpan())
                        {
                            flat.Add((int)(long)lane);
                        }
                    }
                    else
                    {
                        flat.Add((int)(long)row);
                    }
                }

                if (flat.Count < rows * 4)
                {
                    return faces; // unexpected shape - keep original order rather than guessing
                }

                for (var lane = 0; lane < 4; lane++)
                {
                    var laneNodes = new int[rows];
                    for (var r = 0; r < rows; r++)
                    {
                        laneNodes[r] = flat[r * 4 + lane];
                    }

                    var k = FaceKey(laneNodes);
                    if (remaining.TryGetValue(k, out var list) && list.Count > 0)
                    {
                        list.RemoveAt(list.Count - 1);
                        ordered.Add(laneNodes);
                    }
                }
            }

            foreach (var leftovers in remaining.Values)
            {
                ordered.AddRange(leftovers);
            }

            return [.. ordered];
        }

        // Orders proxy vertices by (mesh index, AUTHORED vertex index) recovered from their compiled
        // "$cloth_m{mesh}p{vertex}" names, instead of ascending global node index. The compiler's SIMD
        // constraint packing depends on the DMX's local vertex order: re-importing the same faces with a
        // DIFFERENT vertex order packs different SIMD groups and pads the leftover lanes with LIVE
        // (full-weight) replicas of real constraints - dark_willow got 9 of its 26 tris solved 2-3x per
        // iteration (stiffer cloth) purely from an ascending-global order. Restoring the authored order
        // reproduces the original packing and makes the recompile reassign identical "$cloth_mXpY" names.
        void SortByAuthoredVertexOrder(int[] nodeIndices)
        {
            Array.Sort(nodeIndices, (x, y) =>
            {
                var mx = ParseProxyMeshIndex(CtrlNames[x]);
                var my = ParseProxyMeshIndex(CtrlNames[y]);
                if (mx != my)
                {
                    return mx.CompareTo(my);
                }

                var px = ParseProxyVertexIndex(CtrlNames[x]);
                var py = ParseProxyVertexIndex(CtrlNames[y]);
                if (px != py)
                {
                    return px.CompareTo(py);
                }

                return x.CompareTo(y);
            });
        }

        /// <summary>
        /// Reconstructs proxy-mesh islands for "$cloth_*" control nodes that carry no <see cref="Quads"/>/
        /// <see cref="Tris"/> of their own. A plain (non-<c>ClothQuad</c>) <c>ClothProxyMeshFile</c> import
        /// compiles down to a bare distance-constraint (<c>m_Rods</c>) network, discarding the authored
        /// surface entirely (verified: no import setting preserves it - see the <c>MakeClothQuad</c> remarks
        /// in <c>ModelExtract.ValveModel.cs</c>), so these nodes are otherwise silently dropped instead of
        /// round-tripping as a sheet. Grouped by the "$cloth_m&lt;N&gt;p&lt;M&gt;" mesh index the compiler
        /// already encodes in the node name - one island per otherwise-uncovered index - with faces
        /// synthesised by 2D triangulation: the compiler re-derives its own rod network from whatever
        /// surface is imported anyway (same discarding behaviour), so an approximate triangulation is
        /// enough to recover working physics instead of the exact original faces.
        ///
        /// Coverage is checked per NODE, not per mesh index: a single authored proxy DMX can contain both
        /// a small quad/tri-covered patch AND many more vertices connected only by rods (verified on
        /// meepo_naruto_set's jaket proxy - 1 quad covering 4 of the mesh's 75 "$cloth_m0*" nodes, the
        /// other 71 rods-only). Skipping by mesh index there would drop all 71 just because 4 siblings
        /// already got a face-based island - the two groups end up as separate exported proxy files
        /// instead of one, but every node's physics data still round-trips instead of being silently lost.
        ///
        /// (A ClothNode/ClothSpring reconstruction reproduces m_Rods byte-exact for the constraint data
        /// itself, but ClothNode always creates an independent new goal-attraction point; it cannot
        /// back-solve an EXISTING named bone the way ClothProxyMeshFile's back_solve_joints does. Bone-chain
        /// cloth like legion_commander's banner needs exactly that back-solve - the render mesh is skinned to
        /// the real Banner_L/R bones, not to any node ClothNode could create - so the mesh-import path stays
        /// the only route there despite the topology being approximate.)
        /// </summary>
        List<ProxyMesh> BuildProxyMeshesFromRodsOnly(HashSet<int> coveredNodes)
        {
            var result = new List<ProxyMesh>();
            if (InitPosePositions.Length == 0)
            {
                return result;
            }

            // A still-uncovered "$..." control node is a rod-only proxy vertex (real skeleton bones are
            // handled by BuildBoneChains / the driven-bone path, not here).
            var n = CtrlNames.Length;
            var isProxy = new bool[n];
            for (var node = 0; node < n && node < InitPosePositions.Length; node++)
            {
                isProxy[node] = IsProxyNodeName(CtrlNames[node]) && !string.IsNullOrEmpty(CtrlNames[node])
                    && !coveredNodes.Contains(node);
            }

            // Group rod-only proxy vertices by ROD CONNECTIVITY, not by name. One authored cloth panel can
            // span several proxy-node name prefixes - kez_base's cape is "$ccCapeA".."$ccCapeE" plus
            // "$ccCapeLeafA..C", all wired into one sheet by rods - so grouping by name would split a
            // connected panel across multiple proxy meshes and orphan every rod that crosses the split
            // ("Cannot find node $cloth_mXpY", a hard compile failure). Union-find over rods whose BOTH
            // endpoints are uncovered proxy vertices yields exactly the original's per-panel meshes:
            // primal_beast's leg/back/neck chains and snapfire's two panels have no rods joining them, so
            // they stay separate exactly as before; meepo/dark_willow single-mesh islands are one component.
            var parent = new int[n];
            for (var i = 0; i < n; i++)
            {
                parent[i] = i;
            }

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    x = parent[x] = parent[parent[x]];
                }

                return x;
            }

            foreach (var rod in Rods)
            {
                if (rod.NodeA >= 0 && rod.NodeA < n && rod.NodeB >= 0 && rod.NodeB < n
                    && isProxy[rod.NodeA] && isProxy[rod.NodeB])
                {
                    parent[Find(rod.NodeA)] = Find(rod.NodeB);
                }
            }

            // Also union nodes the compiler already assigned to the same "$cloth_m{N}" mesh: that index is
            // an authoritative grouping the name encodes, and a single panel can contain a vertex with no
            // rod to the rest (meepo_naruto_set's jaket has one such isolated "$cloth_m0" node - grouping
            // by rod connectivity alone would strand it in a <3-vertex singleton and drop it, losing a node).
            // "$cc" names carry no mesh index (ParseProxyMeshIndex returns -1) and rely on rod connectivity.
            var meshIndexRep = new Dictionary<int, int>();
            for (var node = 0; node < n; node++)
            {
                if (!isProxy[node])
                {
                    continue;
                }

                var meshIndex = ParseProxyMeshIndex(CtrlNames[node]);
                if (meshIndex < 0)
                {
                    continue;
                }

                if (meshIndexRep.TryGetValue(meshIndex, out var rep))
                {
                    parent[Find(node)] = Find(rep);
                }
                else
                {
                    meshIndexRep[meshIndex] = node;
                }
            }

            var groups = new Dictionary<int, List<int>>();
            for (var node = 0; node < n; node++)
            {
                if (!isProxy[node])
                {
                    continue;
                }

                var root = Find(node);
                if (!groups.TryGetValue(root, out var nodes))
                {
                    groups[root] = nodes = [];
                }

                nodes.Add(node);
            }

            var nodeFriction = Data.GetFloatArray("m_DynNodeFriction");
            // Smallest member index first, so the proxy-mesh numbering is deterministic and follows the
            // original control-node order.
            foreach (var (_, nodeIndices) in groups.OrderBy(static kv => kv.Value.Min()))
            {
                // Need at least a triangle's worth of points to synthesise a surface.
                if (nodeIndices.Count < 3)
                {
                    continue;
                }

                var mesh = BuildProxyMeshFromNodeSet(nodeIndices, nodeFriction);
                if (mesh is not null)
                {
                    result.Add(mesh);
                }
            }

            return result;
        }

        ProxyMesh? BuildProxyMeshFromNodeSet(List<int> nodeIndices, float[] nodeFriction)
        {
            // Same authored-vertex-order restoration as BuildProxyMesh - the SIMD constraint packing
            // (and the recompile's own "$cloth_mXpY" numbering) follows the DMX vertex order.
            var sorted = nodeIndices.ToArray();
            SortByAuthoredVertexOrder(sorted);
            nodeIndices = [.. sorted];

            var count = nodeIndices.Count;
            var positions = new Vector3[count];
            var clothEnable = new float[count];
            var goalStrength = new float[count];
            var goalDamping = new float[count];
            var collisionRadius = new float[count];
            var friction = new float[count];
            var drag = new float[count];
            var groundCollision = new float[count];
            var gravity = new float[count];
            var vertexAttraction = new float[count];
            var skinInfluences = new (string Bone, float Weight)[count][];
            var simulated = 0;
            var pinned = 0;

            for (var i = 0; i < count; i++)
            {
                var node = nodeIndices[i];
                positions[i] = InitPosePositions[node];

                var vertex = ComputeProxyVertexData(node, nodeFriction);
                clothEnable[i] = vertex.IsSim ? 1f : 0f;
                if (vertex.IsSim) { simulated++; } else { pinned++; }
                skinInfluences[i] = vertex.SkinInfluences;
                goalStrength[i] = vertex.GoalStrength;
                goalDamping[i] = vertex.GoalDamping;
                collisionRadius[i] = vertex.CollisionRadius;
                friction[i] = vertex.Friction;
                drag[i] = vertex.Drag;
                groundCollision[i] = 0f;
                gravity[i] = vertex.Gravity;
                vertexAttraction[i] = vertex.VertexAttraction;
            }

            var faces = TriangulateDominantPlane(positions);
            EnsureAllVerticesFaced(positions, faces);
            if (faces.Count == 0)
            {
                return null;
            }

            var isDropRisk = ComputeDropRisk(positions, clothEnable, faces);

            return new ProxyMesh
            {
                NodeIndices = [.. nodeIndices],
                Positions = positions,
                ClothEnable = clothEnable,
                GoalStrength = goalStrength,
                GoalDamping = goalDamping,
                CollisionRadius = collisionRadius,
                Friction = friction,
                Drag = drag,
                GroundCollision = groundCollision,
                Gravity = gravity,
                VertexAttraction = vertexAttraction,
                SkinInfluences = skinInfluences,
                Faces = faces,
                SimulatedCount = simulated,
                PinnedCount = pinned,
                IsDropRisk = isDropRisk,
            };
        }

        // Projects a 3D point set onto its two dominant-extent axes (the same "biggest bounding-box
        // spread" heuristic ModelExtract.Mesh uses for proxy UVs) - good enough for the near-planar cloth
        // sheets these control-node islands represent.
        static Vector2[] ProjectToDominantPlane(Vector3[] positions)
        {
            var min = positions.Aggregate(Vector3.Min);
            var max = positions.Aggregate(Vector3.Max);
            var extent = max - min;
            Span<int> axes = [0, 1, 2];
            axes.Sort((a, b) => extent[b].CompareTo(extent[a]));
            var (axisU, axisV) = (axes[0], axes[1]);

            var projected = new Vector2[positions.Length];
            for (var i = 0; i < positions.Length; i++)
            {
                projected[i] = new Vector2(positions[i][axisU], positions[i][axisV]);
            }

            return projected;
        }

        // Incremental Bowyer-Watson Delaunay triangulation over the dominant-plane projection.
        //
        // A vertex not referenced by ANY face is NOT registered as a valid FeModel control node at all, and
        // every unfaced vertex then hard-fails to compile ("Cannot find Fx Bone"/"Cannot find node"). A
        // sparser cover is not an option even though the ORIGINAL's own compiled m_Tris/m_Quads for a 75-node
        // jaket proxy island add up to only 1 quad + 1 tri: the author's source DMX carries a much denser
        // authored topology than its OWN compiled output, which the importer collapses into m_Rods - not
        // reconstructable by feeding a sparser face set. A full triangulation (this function) is what gets
        // every vertex registered: compiles clean with m_nNodeCount exact (106/106) and m_Rods close (331 vs
        // 325), even though the resulting m_Quads/m_Tris don't match (the compiler's own quad-vs-rod-collapse
        // heuristic isn't reverse-engineered here). Do not "simplify" this back to a sparser cover without
        // re-verifying node count first.
        //
        // Do NOT thin this to a sparser cover: a high cloth_make_rods paint makes the compiler match
        // m_Quads/m_Tris exactly, but its auto-derived rods then STACK with AddClothProxySprings' own exact
        // m_Rods (inflating 331->515), and any sparser fan/set-cover either leaves vertices unfaced (they
        // fail to register: "Cannot find Fx Bone") or needs a per-model minimum-degree that isn't universal
        // (>=3 fixes meepo's jaket but breaks legion's island). Correct per-edge rod topology matters more
        // for simulated behaviour than the compiled quad/tri surface count, so keep the full Delaunay output.
        //
        // The compiler only registers a proxy vertex as an FeModel control node if it is referenced by at
        // least one face (see the TriangulateDominantPlane remarks). A Delaunay triangulation of a curved
        // or near-collinear rod-only island can still leave boundary vertices - or vertices that overlap
        // once projected to the dominant plane (snapfire's two curved panels) - out of every face. Those
        // vertices then can't be targeted by their m_Rods' ClothSprings ("Cannot find node"), so the whole
        // rod-only island's cloth is lost. Attach each still-unfaced vertex to its two nearest non-collinear
        // neighbours, guaranteeing every vertex registers and its rods survive. This is purely ADDITIVE:
        // fully-triangulated islands (dark_willow / meepo_naruto_set / legion_commander) have no unfaced
        // vertices, so no triangle is added and their compiled output stays byte-exact.
        static void EnsureAllVerticesFaced(Vector3[] positions, List<int[]> faces)
        {
            var n = positions.Length;
            if (n < 3)
            {
                return;
            }

            var faced = new HashSet<int>();
            foreach (var face in faces)
            {
                foreach (var v in face)
                {
                    faced.Add(v);
                }
            }

            for (var i = 0; i < n; i++)
            {
                if (faced.Contains(i))
                {
                    continue;
                }

                // Nearest distinct-position vertex first, then the nearest one after it that is not
                // collinear with i and the first pick, so the synthesized triangle has real area.
                var ordered = Enumerable.Range(0, n)
                    .Where(j => j != i && positions[j] != positions[i])
                    .OrderBy(j => Vector3.DistanceSquared(positions[i], positions[j]))
                    .ToList();

                if (ordered.Count < 2)
                {
                    continue;
                }

                var a = ordered[0];
                var b = -1;
                for (var k = 1; k < ordered.Count; k++)
                {
                    var cross = Vector3.Cross(positions[a] - positions[i], positions[ordered[k]] - positions[i]);
                    if (cross.LengthSquared() > 1e-6f)
                    {
                        b = ordered[k];
                        break;
                    }
                }

                if (b < 0)
                {
                    continue;
                }

                faces.Add([i, a, b]);
                faced.Add(i);
                faced.Add(a);
                faced.Add(b);
            }
        }

        // Predicts whether the cloth mesh importer will silently prune any vertex of a synthesised rod-only
        // island (which would orphan an explicit ClothSpring referencing it - a hard compile failure). Two
        // importer prune behaviours (verified byte-exact on mars m3 and hoodwink m2/m4/m5):
        //   (1) ISOLATED-PINNED: a pinned (cloth_enable == 0) vertex whose face-neighbours are ALL pinned has
        //       no simulated neighbour, so it is a fully-static mesh region the solver has no use for and
        //       discards. (mars m3: the two spine_2-pinned tip verts p21/p22, surrounded only by other
        //       pinned verts, were dropped; the sibling tip vert p23 kept a simulated neighbour and survived.)
        //   (2) NEAR-COINCIDENT WELD: two vertices much closer than the island's typical edge length get
        //       welded into one by the importer, dropping the duplicate. (hoodwink's chain-ribbon end caps:
        //       the two sides converge to ~0.9-unit-apart pairs, ~0.18x the ~5-unit rod length.)
        // Either signal marks the whole island as drop-risk; the caller then omits its explicit rods and lets
        // the compiler auto-derive them (always compiles). Convex, uniformly-spaced islands trip neither and
        // keep their exact reconstructed rods. Both signals are scale-relative / topological - no value is
        // peeked from any specific model's source.
        static bool ComputeDropRisk(Vector3[] positions, float[] clothEnable, List<int[]> faces)
        {
            var n = positions.Length;
            if (n == 0)
            {
                return false;
            }

            // (1) isolated-pinned: build face adjacency, flag a pinned vertex with no simulated neighbour.
            var adjacency = new HashSet<int>[n];
            for (var i = 0; i < n; i++)
            {
                adjacency[i] = [];
            }

            foreach (var face in faces)
            {
                foreach (var a in face)
                {
                    foreach (var b in face)
                    {
                        if (a != b)
                        {
                            adjacency[a].Add(b);
                        }
                    }
                }
            }

            for (var i = 0; i < n; i++)
            {
                if (clothEnable[i] != 0f)
                {
                    continue;
                }

                var hasSimulatedNeighbour = false;
                foreach (var nb in adjacency[i])
                {
                    if (clothEnable[nb] != 0f)
                    {
                        hasSimulatedNeighbour = true;
                        break;
                    }
                }

                if (!hasSimulatedNeighbour)
                {
                    return true;
                }
            }

            // (2) near-coincident weld: any vertex pair closer than a fraction of the island's median edge.
            var edges = new List<float>();
            foreach (var face in faces)
            {
                for (var a = 0; a < face.Length; a++)
                {
                    var b = (a + 1) % face.Length;
                    edges.Add(Vector3.Distance(positions[face[a]], positions[face[b]]));
                }
            }

            if (edges.Count > 0)
            {
                edges.Sort();
                var weldDistance = edges[edges.Count / 2] * 0.25f;
                for (var i = 0; i < n; i++)
                {
                    for (var j = i + 1; j < n; j++)
                    {
                        if (Vector3.Distance(positions[i], positions[j]) < weldDistance)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        static List<int[]> TriangulateDominantPlane(Vector3[] positions)
        {
            var faces = new List<int[]>();
            var n = positions.Length;
            if (n < 3)
            {
                return faces;
            }

            var points = ProjectToDominantPlane(positions);

            var min = points.Aggregate(Vector2.Min);
            var max = points.Aggregate(Vector2.Max);
            var center = (min + max) * 0.5f;
            var size = MathF.Max(max.X - min.X, max.Y - min.Y) * 10f + 1f;

            // Super-triangle enclosing every point, at indices n, n+1, n+2 (stripped out at the end).
            var allPoints = new Vector2[n + 3];
            Array.Copy(points, allPoints, n);
            allPoints[n] = center + new Vector2(0f, size * 2f);
            allPoints[n + 1] = center + new Vector2(-size * 2f, -size);
            allPoints[n + 2] = center + new Vector2(size * 2f, -size);

            var triangles = new List<(int A, int B, int C)> { (n, n + 1, n + 2) };

            for (var p = 0; p < n; p++)
            {
                var bad = triangles.Where(tri => InCircumcircle(allPoints[tri.A], allPoints[tri.B], allPoints[tri.C], allPoints[p])).ToList();

                // The hole's boundary: edges of bad triangles that are not shared with another bad triangle.
                var polygon = new List<(int A, int B)>();
                foreach (var tri in bad)
                {
                    foreach (var edge in new[] { (tri.A, tri.B), (tri.B, tri.C), (tri.C, tri.A) })
                    {
                        var shared = false;
                        foreach (var other in bad)
                        {
                            if (!other.Equals(tri) && HasEdge(other, edge.Item1, edge.Item2))
                            {
                                shared = true;
                                break;
                            }
                        }

                        if (!shared)
                        {
                            polygon.Add(edge);
                        }
                    }
                }

                triangles.RemoveAll(bad.Contains);
                foreach (var (a, b) in polygon)
                {
                    triangles.Add((a, b, p));
                }
            }

            foreach (var tri in triangles)
            {
                if (tri.A < n && tri.B < n && tri.C < n)
                {
                    faces.Add([tri.A, tri.B, tri.C]);
                }
            }

            return faces;
        }

        static bool HasEdge((int A, int B, int C) tri, int a, int b)
            => (tri.A == a && tri.B == b) || (tri.A == b && tri.B == a)
            || (tri.B == a && tri.C == b) || (tri.B == b && tri.C == a)
            || (tri.C == a && tri.A == b) || (tri.C == b && tri.A == a);

        static bool InCircumcircle(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
        {
            var ax = a.X - p.X; var ay = a.Y - p.Y;
            var bx = b.X - p.X; var by = b.Y - p.Y;
            var cx = c.X - p.X; var cy = c.Y - p.Y;

            var det =
                (ax * ax + ay * ay) * (bx * cy - cx * by) -
                (bx * bx + by * by) * (ax * cy - cx * ay) +
                (cx * cx + cy * cy) * (ax * by - bx * ay);

            var area = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
            return area >= 0 ? det > 0 : det < 0;
        }

        /// <summary>A cloth collision capsule recovered from <c>m_TaperedCapsuleRigids</c>.</summary>
        public sealed class CollisionCapsule
        {
            /// <summary>Gets the bone the capsule is attached to (its node resolved to a real skeleton bone).</summary>
            public required string? ParentBone { get; init; }
            /// <summary>Gets the first end-cap centre (bone-local).</summary>
            public required Vector3 Point0 { get; init; }
            /// <summary>Gets the first end-cap radius.</summary>
            public required float Radius0 { get; init; }
            /// <summary>Gets the second end-cap centre (bone-local).</summary>
            public required Vector3 Point1 { get; init; }
            /// <summary>Gets the second end-cap radius.</summary>
            public required float Radius1 { get; init; }
            /// <summary>Gets the 4-bit collision-layer mask.</summary>
            public int CollisionMask { get; init; }
        }

        /// <summary>A cloth collision sphere recovered from <c>m_SphereRigids</c>.</summary>
        public sealed class CollisionSphere
        {
            /// <summary>Gets the bone the sphere is attached to.</summary>
            public required string? ParentBone { get; init; }
            /// <summary>Gets the sphere centre (bone-local).</summary>
            public required Vector3 Center { get; init; }
            /// <summary>Gets the sphere radius.</summary>
            public required float Radius { get; init; }
            /// <summary>Gets the 4-bit collision-layer mask.</summary>
            public int CollisionMask { get; init; }
        }

        // Resolves a rigid's nNode to its bone name. Collision rigids are anchored to a real bone; if the
        // node happens to be an auto-generated proxy node, walk up to the first real bone.
        string? ResolveRigidBone(int node)
        {
            if (node < 0 || node >= CtrlNames.Length)
            {
                return null;
            }

            return IsProxyNodeName(CtrlNames[node]) ? ResolveSkinBone(node) : CtrlNames[node];
        }

        /// <summary>
        /// Reconstructs the cloth collision capsules (<c>m_TaperedCapsuleRigids</c>). Each rigid has two
        /// spheres (the tapered end-caps); <c>vSphere[i]</c> is xyz = centre, w = radius. Returns an empty
        /// list when the model has no capsule rigids (e.g. dark_willow).
        /// </summary>
        public List<CollisionCapsule> BuildCollisionCapsules()
        {
            var result = new List<CollisionCapsule>();
            var rigids = Data.GetArray("m_TaperedCapsuleRigids");
            if (rigids is null)
            {
                return result;
            }

            foreach (var rigid in rigids)
            {
                var spheres = rigid.GetArray("vSphere");
                if (spheres is null || spheres.Count < 2)
                {
                    continue;
                }

                var s0 = spheres[0].ToVector4();
                var s1 = spheres[1].ToVector4();
                var node = rigid.GetInt32Property("nNode");

                result.Add(new CollisionCapsule
                {
                    ParentBone = ResolveRigidBone(node),
                    Point0 = new Vector3(s0.X, s0.Y, s0.Z),
                    Radius0 = s0.W,
                    Point1 = new Vector3(s1.X, s1.Y, s1.Z),
                    Radius1 = s1.W,
                    CollisionMask = rigid.GetInt32Property("nCollisionMask"),
                });
            }

            return result;
        }

        /// <summary>
        /// Reconstructs the cloth collision spheres (<c>m_SphereRigids</c>). Returns an empty list when the
        /// model has no sphere rigids.
        /// </summary>
        public List<CollisionSphere> BuildCollisionSpheres()
        {
            var result = new List<CollisionSphere>();
            var rigids = Data.GetArray("m_SphereRigids");
            if (rigids is null)
            {
                return result;
            }

            foreach (var rigid in rigids)
            {
                // m_SphereRigids entries store a single sphere either as a flat vSphere [x,y,z,r] array
                // (unlike m_TaperedCapsuleRigids' vSphere, which nests TWO such arrays for its end-caps)
                // or m_vCenter+m_flRadius.
                Vector4 sphere;
                if (rigid.GetArray<float>("vSphere") is { Length: 4 } s)
                {
                    sphere = new Vector4(s[0], s[1], s[2], s[3]);
                }
                else if (rigid.ContainsKey("m_vSphere"))
                {
                    sphere = rigid.GetSubCollection("m_vSphere").ToVector4();
                }
                else
                {
                    continue;
                }

                var node = rigid.GetInt32Property("nNode");
                result.Add(new CollisionSphere
                {
                    ParentBone = ResolveRigidBone(node),
                    Center = new Vector3(sphere.X, sphere.Y, sphere.Z),
                    Radius = sphere.W,
                    CollisionMask = rigid.GetInt32Property("nCollisionMask"),
                });
            }

            return result;
        }

        /// <summary>
        /// A single joint within a reconstructed bone chain.
        /// </summary>
        public sealed class BoneChainJoint
        {
            /// <summary>Gets the control-node index of this joint.</summary>
            public int Node { get; init; }
            /// <summary>Gets the bone name of this joint.</summary>
            public required string Name { get; init; }
            /// <summary>Gets the control-node index of the chain parent, or -1 if this is the chain root.</summary>
            public int ParentNode { get; init; }
            /// <summary>Gets the bone name of the chain parent, or null if this is the chain root.</summary>
            public string? ParentName { get; init; }
            /// <summary>Gets the inverse mass for this node (0 = static anchor).</summary>
            public float InvMass { get; init; }
            /// <summary>
            /// Gets the number of auto-generated <c>$cc</c> proxy nodes the compiler placed on THIS joint
            /// (its local ribbon width). Usually equal to the chain's <see cref="BoneChain.ExtrudeSides"/>,
            /// but an end-cap joint can fan wider (primal_beast back_chain body 2, tip 4). Used to override
            /// the chain-level extrude per joint so an end-cap fan is not lost to the uniform chain width.
            /// </summary>
            public int ExtrudeSides { get; set; }
            /// <summary>Gets a value indicating whether this joint is simulated (invMass &gt; 0).</summary>
            public bool Simulated => InvMass > 0f;
            /// <summary>Gets a value indicating whether this joint is the chain root.</summary>
            public bool IsRoot => ParentNode < 0;
        }

        /// <summary>
        /// A reconstructed bone chain: a static anchor bone plus all of its simulated descendants.
        /// </summary>
        public sealed class BoneChain
        {
            /// <summary>Gets the anchor (root) bone name.</summary>
            public required string RootBone { get; init; }
            /// <summary>Gets the joints, root first, in pre-order (a parent always precedes its children).</summary>
            public List<BoneChainJoint> Joints { get; } = [];
            /// <summary>
            /// Gets the ribbon width the compiler baked as auto-generated <c>$cc</c> proxy nodes per joint:
            /// 0/1 = a plain 1-wide rope (no extrude), 2+ = an extruded strip/tube (marci BackpackStrapLwr =
            /// 2, phantom_assassin hair = 3). Drives the ClothChain's <c>extrude_sides</c> so the recompile
            /// regenerates the same proxy count instead of halving a 2-wide strip to a 1-wide rope.
            /// </summary>
            public int ExtrudeSides { get; set; }
            /// <summary>Gets the mean distance from a joint bone to its <c>$cc</c> proxy nodes (the extrude half-width).</summary>
            public float ExtrudeRadius { get; set; }
        }

        /// <summary>
        /// Reconstructs bone chains from the control-node topology, ignoring auto-generated cloth proxy nodes.
        /// Each chain is rooted at a real bone with no real-bone parent and contains all of its real descendants.
        /// </summary>
        public List<BoneChain> BuildBoneChains()
        {
            var chains = new List<BoneChain>();
            var n = CtrlNames.Length;
            if (n == 0)
            {
                return chains;
            }

            // Mark real skeleton bones (everything that is not an auto-generated cloth proxy node).
            var isReal = new bool[n];
            for (var i = 0; i < n; i++)
            {
                isReal[i] = !IsProxyNodeName(CtrlNames[i]);
            }

            // For each real node, resolve its parent among real nodes. The direct skeleton parent is used when
            // it is itself a real bone; otherwise the node is treated as a chain root. (Proxy-mesh parenting is
            // intentionally not followed here - that topology belongs to the later proxy-mesh phase.)
            //
            // m_SkelParents is indexed in CONTROL-NODE space, so it silently collapses through any
            // intermediate real skeleton bone that never became a control node itself - e.g.
            // meepo_naruto_set's 5 standalone "neck_nodes" bones (each a distant, otherwise-unrelated
            // descendant of "root_0" with nothing in between ever referenced by a cloth construct) all
            // resolve their "real parent" to root_0 directly, reading as one bogus 5-way chain even though
            // they are only connected to EACH OTHER (sparsely) via explicit ClothSpring, never to root_0.
            // Require an actual m_Rods entry between a node and its candidate real parent before trusting
            // the link (verified: root_0 has ZERO rods touching it at all in that model) - a genuine
            // authored chain's own joint-to-joint rods (see AddClothProxySprings remarks: a chain compiles
            // to a fully-connected local rod mesh among its own joints) always include the direct
            // parent-child pair, so this never rejects a real chain link, only a coincidental one.
            var rodPairs = new HashSet<(int, int)>();
            foreach (var rod in Rods)
            {
                rodPairs.Add(rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA));
            }

            // Each real bone -> the auto-generated "$cc<bone>" proxy nodes parented straight to it (its
            // ribbon width). Restricted to the "$cc" prefix (the ClothChain extrude proxies), NOT every
            // "$"-node: a "$cloth_m" SHEET must not be mistaken for a chain's own width (would disturb
            // meepo's jaket). Used below to keep a ribbon's position-driven TIP joint in the chain, and
            // later to recover each chain's extrude width.
            var proxyChildrenOf = new Dictionary<int, List<int>>();
            for (var node = 0; node < n; node++)
            {
                if (!CtrlNames[node].StartsWith("$cc", StringComparison.Ordinal))
                {
                    continue;
                }

                var pp = node < SkelParents.Length ? SkelParents[node] : -1;
                if (pp >= 0)
                {
                    if (!proxyChildrenOf.TryGetValue(pp, out var list))
                    {
                        proxyChildrenOf[pp] = list = [];
                    }

                    list.Add(node);
                }
            }

            var realParent = new int[n];
            var children = new List<int>?[n];
            var roots = new List<int>();

            for (var i = 0; i < n; i++)
            {
                realParent[i] = -1;

                if (!isReal[i])
                {
                    continue;
                }

                var p = i < SkelParents.Length ? SkelParents[i] : -1;
                if (p < 0 || p >= n || !isReal[p])
                {
                    continue;
                }

                var rodLinked = rodPairs.Contains(p < i ? (p, i) : (i, p));

                // A $cc-proxied chain (marci's BackpackStrapLwr/GemRibbon/Ponytail/Backpack/SkirtHlp,
                // primal_beast's leg/back/neck) carries its rods among the auto-generated $cc PROXY nodes,
                // never between the real chain bones, so the rod test alone never links it and the chain
                // silently falls through to BuildProxyMeshesFromRodsOnly - where a curved 2-wide ribbon then
                // collapses in the compiler's 2D cloth-mesh import. Link two consecutive position-driven
                // SIMULATED real bones directly instead. The meepo neck_nodes false-chain the rod test guards
                // against (5 distant descendants of a static root_0 that m_SkelParents collapses onto) can't
                // satisfy this: they are STATIC (invMass 0) and their resolved parent root_0 is static too.
                var bothDrivenSim = i >= FirstPositionDrivenNode && p >= FirstPositionDrivenNode
                    && i < NodeInvMasses.Length && NodeInvMasses[i] != 0f
                    && p < NodeInvMasses.Length && NodeInvMasses[p] != 0f;

                // A node that carries its own $cc proxies is unambiguously a ribbon joint, so link it to its
                // real parent cloth node whatever that parent's role: a simulated BODY bone (extends the
                // strip inward), another $cc-proxied ribbon bone (a per-side anchor like back_chain_0), OR a
                // pinned SHARED anchor that carries no proxies of its own (ringmaster's cape_top, from which
                // both cape_L and cape_R hang - dropping it lost 1 node, firstPD 46->45). p is already
                // guaranteed a real cloth node (isReal[p] checked above), so no extra role test is needed;
                // requiring i to be $cc-proxied is itself the guard against the meepo neck_nodes false-chain
                // (those static root_0 descendants carry NO proxies, so this never links them).
                var proxyRibbon = proxyChildrenOf.ContainsKey(i);

                if (rodLinked || bothDrivenSim || proxyRibbon)
                {
                    realParent[i] = p;
                }
            }

            for (var i = 0; i < n; i++)
            {
                if (!isReal[i])
                {
                    continue;
                }

                var p = realParent[i];
                if (p < 0)
                {
                    roots.Add(i);
                }
                else
                {
                    (children[p] ??= []).Add(i);
                }
            }

            foreach (var rootNode in roots)
            {
                // A real bone with no real descendants is not a cloth chain - skip it.
                if (children[rootNode] is null)
                {
                    continue;
                }

                var chain = new BoneChain { RootBone = CtrlNames[rootNode] };

                void Visit(int node)
                {
                    var parent = realParent[node];
                    chain.Joints.Add(new BoneChainJoint
                    {
                        Node = node,
                        Name = CtrlNames[node],
                        ParentNode = parent,
                        ParentName = parent >= 0 ? CtrlNames[parent] : null,
                        InvMass = node < NodeInvMasses.Length ? NodeInvMasses[node] : 0f,
                    });

                    if (children[node] is { } kids)
                    {
                        kids.Sort();
                        foreach (var child in kids)
                        {
                            Visit(child);
                        }
                    }
                }

                Visit(rootNode);

                // Recover the ribbon width the compiler baked into $cc proxy nodes: how many it placed per
                // joint (extrude_sides) and their mean offset (extrude_radius). Without this a ClothChain
                // regenerates only ONE proxy per joint, halving a 2-wide strip (marci BackpackStrapLwr,
                // primal_beast leg_chain) to a 1-wide rope.
                //
                // extrude_sides forces EVERY joint to the same width, so it reproduces a UNIFORM strip
                // exactly (leg_chain [2,2,2,2,2,2] -> extrude 2 -> identical) but cannot reproduce a ribbon
                // whose END-CAP joint fans wider than its body (back_chain [2,2,2,4], hoodwink tail
                // [2,2,2,2,2,2,4]). Use the MODE (the width most joints share = the true body width), NOT the
                // max: picking the tip's 4 would re-extrude the whole body 4-wide ([4,4,4,4]), inflating the
                // node count and distorting the shape (hoodwink went +52). The mode reproduces the body
                // exactly and just drops the unreproducible tip fan (a few nodes). 0/1 stays a plain rope (no
                // extrude) so genuine 1-wide chains - meepo/dark_willow/legion - are byte-identical as before.
                var sideFrequency = new Dictionary<int, int>();
                var radii = new List<float>();
                foreach (var joint in chain.Joints)
                {
                    if (!proxyChildrenOf.TryGetValue(joint.Node, out var proxies) || proxies.Count == 0)
                    {
                        continue;
                    }

                    joint.ExtrudeSides = Math.Min(proxies.Count, 4);
                    sideFrequency[proxies.Count] = sideFrequency.GetValueOrDefault(proxies.Count) + 1;
                    if (joint.Node < InitPosePositions.Length)
                    {
                        foreach (var proxy in proxies)
                        {
                            if (proxy < InitPosePositions.Length)
                            {
                                radii.Add(Vector3.Distance(InitPosePositions[joint.Node], InitPosePositions[proxy]));
                            }
                        }
                    }
                }

                // Body width = most common per-joint count; tie-break toward the SMALLER (an end cap only
                // ever ADDS proxies, so the smaller of two equally-common widths is the body, not the cap).
                var bodySides = sideFrequency
                    .OrderByDescending(static kv => kv.Value)
                    .ThenBy(static kv => kv.Key)
                    .Select(static kv => kv.Key)
                    .FirstOrDefault();

                // Extrude whenever the body carries proxies at all (>= 1), not only 2-wide strips. A 1-wide
                // body (hoodwink face_tuft/cape_back, one $cc proxy per joint) is NOT the same as a genuine
                // 0-proxy rope (meepo/dark_willow): dropping its extrude would leave the joints as bare chain
                // nodes and lose that per-joint proxy (hoodwink lost ~24 nodes this way). A 0-width body
                // (empty sideFrequency -> bodySides 0) still gets no extrude, keeping those models byte-exact.
                if (bodySides >= 1)
                {
                    // extrude_sides' authored range is [0,4]; a wider strip is clamped (best-effort width).
                    chain.ExtrudeSides = Math.Min(bodySides, 4);
                    chain.ExtrudeRadius = radii.Count > 0 ? radii.Average() : 0f;
                }

                chains.Add(chain);
            }

            return chains;
        }

        /// <summary>
        /// A cloth sheet generated over a group of neighbouring bone chains (rows = positions along the
        /// chains, columns = chains plus interpolated columns between them). Mirrors the proxy grids item
        /// authors hand-build for skirts/capes: the sheet simulates the surface between the chains and
        /// drives the render mesh directly.
        /// </summary>
        public sealed class ChainGrid
        {
            /// <summary>Gets the model-space rest position of each grid vertex.</summary>
            public required Vector3[] Positions { get; init; }
            /// <summary>Gets the grid-parameter UVs (u = across chains, v = along them).</summary>
            public required Vector2[] Texcoords { get; init; }
            /// <summary>Gets the (bone, weight) influences of each vertex (up to 4, bilinear over the chains).</summary>
            public required (string Bone, float Weight)[][] SkinInfluences { get; init; }
            /// <summary>Gets the per-vertex cloth_enable paint (0 = pinned anchor row).</summary>
            public required float[] ClothEnable { get; init; }
            /// <summary>Gets the per-vertex goal strength paint (cbrt of the recovered force attraction).</summary>
            public required float[] GoalStrength { get; init; }
            /// <summary>Gets the per-vertex collision radius paint.</summary>
            public required float[] CollisionRadius { get; init; }
            /// <summary>Gets the per-vertex goal damping paint.</summary>
            public required float[] GoalDamping { get; init; }
            /// <summary>Gets the per-vertex friction paint.</summary>
            public required float[] Friction { get; init; }
            /// <summary>Gets the per-vertex drag paint.</summary>
            public required float[] Drag { get; init; }
            /// <summary>Gets the quads covering the grid.</summary>
            public required List<int[]> Faces { get; init; }
        }

        // How close chain root joints must rest to be considered part of one sheet, in inches.
        const float ChainGridRootDistance = 30f;
        // Interpolated columns inserted between adjacent chains.
        const int ChainGridSubdivisions = 3;

        /// <summary>
        /// Generates cloth sheet grids over groups of neighbouring bone chains. Branched chains are
        /// decomposed into root-to-leaf PATHS (a shared coattail base becomes two columns); paths with
        /// 3+ joints whose roots rest within <see cref="ChainGridRootDistance"/> form one sheet. Returns
        /// an empty list when no group of 2+ paths exists - e.g. cloth made of one isolated strand.
        /// </summary>
        public List<ChainGrid> BuildChainGrids()
        {
            var grids = new List<ChainGrid>();
            var paths = new List<List<BoneChainJoint>>();

            foreach (var chain in BuildBoneChains())
            {
                var byNode = chain.Joints.ToDictionary(j => j.Node);
                var isParent = chain.Joints.Select(j => j.ParentNode).ToHashSet();

                foreach (var leaf in chain.Joints.Where(j => !isParent.Contains(j.Node)))
                {
                    var path = new List<BoneChainJoint>();
                    var current = leaf;
                    while (true)
                    {
                        path.Insert(0, current);
                        if (current.IsRoot || !byNode.TryGetValue(current.ParentNode, out var parent))
                        {
                            break;
                        }

                        current = parent;
                    }

                    if (path.Count >= 3 && path[0].Node < InitPosePositions.Length)
                    {
                        paths.Add(path);
                    }
                }
            }

            if (paths.Count < 2)
            {
                return grids;
            }

            // Union-find style grouping by root rest distance.
            var groupOf = Enumerable.Range(0, paths.Count).ToArray();
            int Find(int x) { while (groupOf[x] != x) { x = groupOf[x] = groupOf[groupOf[x]]; } return x; }
            for (var a = 0; a < paths.Count; a++)
            {
                for (var b = a + 1; b < paths.Count; b++)
                {
                    var da = InitPosePositions[paths[a][0].Node];
                    var db = InitPosePositions[paths[b][0].Node];
                    if (Vector3.Distance(da, db) <= ChainGridRootDistance)
                    {
                        groupOf[Find(a)] = Find(b);
                    }
                }
            }

            foreach (var group in Enumerable.Range(0, paths.Count).GroupBy(Find))
            {
                var members = group.Select(i => paths[i]).ToList();
                if (members.Count < 2)
                {
                    continue;
                }

                grids.Add(BuildGridForChains(members));
            }

            return grids;
        }

        ChainGrid BuildGridForChains(List<List<BoneChainJoint>> members)
        {
            // Order the paths around the centroid of their roots (skirts wrap around the hips).
            var centroid = Vector3.Zero;
            foreach (var path in members)
            {
                centroid += InitPosePositions[path[0].Node];
            }

            centroid /= members.Count;
            members.Sort((a, b) =>
            {
                var pa = InitPosePositions[a[0].Node] - centroid;
                var pb = InitPosePositions[b[0].Node] - centroid;
                return MathF.Atan2(pa.Y, pa.X).CompareTo(MathF.Atan2(pb.Y, pb.X));
            });

            var rows = members.Max(c => c.Count);
            var nodeFriction = Data.GetFloatArray("m_DynNodeFriction");
            float FrictionAt(int node)
            {
                var dynamicIndex = node - StaticNodeCount;
                return dynamicIndex >= 0 && dynamicIndex < nodeFriction.Length
                    ? Math.Clamp(nodeFriction[dynamicIndex], 0f, 1f)
                    : 0f;
            }

            // Sample each chain at uniform arc-length fractions; remember the bracketing joints so the
            // vertex can be skinned/painted by interpolating them.
            var columnSamples = new List<(Vector3 Position, (string Bone, float Weight)[] Influences, float Enable, float Strength, float Radius, float Damping, float Friction, float Drag)[]>();
            foreach (var joints in members)
            {
                var lengths = new float[joints.Count];
                for (var j = 1; j < joints.Count; j++)
                {
                    lengths[j] = lengths[j - 1] + Vector3.Distance(
                        InitPosePositions[joints[j - 1].Node], InitPosePositions[joints[j].Node]);
                }

                var total = MathF.Max(lengths[^1], 1e-4f);
                var samples = new (Vector3, (string, float)[], float, float, float, float, float, float)[rows];
                for (var r = 0; r < rows; r++)
                {
                    var target = total * r / (rows - 1);
                    var j = 1;
                    while (j < joints.Count - 1 && lengths[j] < target) { j++; }
                    var t = Math.Clamp((target - lengths[j - 1]) / MathF.Max(lengths[j] - lengths[j - 1], 1e-4f), 0f, 1f);

                    var a = joints[j - 1];
                    var b = joints[j];
                    var position = Vector3.Lerp(InitPosePositions[a.Node], InitPosePositions[b.Node], t);
                    var influences = t < 1e-3f ? new[] { (a.Name, 1f) }
                        : t > 1f - 1e-3f ? new[] { (b.Name, 1f) }
                        : new[] { (a.Name, 1f - t), (b.Name, t) };

                    var ia = GetIntegrator(a.Node);
                    var ib = GetIntegrator(b.Node);
                    var strength = MathF.Cbrt(Math.Clamp(ia.ForceAttraction + (ib.ForceAttraction - ia.ForceAttraction) * t, 0f, 1f));
                    var radius = GetCollisionRadius(a.Node) + (GetCollisionRadius(b.Node) - GetCollisionRadius(a.Node)) * t;
                    var attractionExcess = (ia.VertexAttraction - ia.ForceAttraction) + ((ib.VertexAttraction - ib.ForceAttraction) - (ia.VertexAttraction - ia.ForceAttraction)) * t;
                    var damping = GoalDampingFromAttraction(strength, attractionExcess);
                    var friction = FrictionAt(a.Node) + (FrictionAt(b.Node) - FrictionAt(a.Node)) * t;
                    var drag = Math.Clamp((ia.PointDamping + (ib.PointDamping - ia.PointDamping) * t) / ClothDragPointDampingScale, 0f, 1f);

                    samples[r] = (position, influences, r == 0 ? 0f : 1f, strength, radius, damping, friction, drag);
                }

                columnSamples.Add(samples);
            }

            // Expand to full columns: each chain column plus interpolated columns between neighbours.
            var columns = new List<(Vector3, (string, float)[], float, float, float, float, float, float)[]>();
            for (var c = 0; c < columnSamples.Count; c++)
            {
                columns.Add(columnSamples[c]);
                if (c == columnSamples.Count - 1)
                {
                    break;
                }

                for (var s = 1; s <= ChainGridSubdivisions; s++)
                {
                    var u = (float)s / (ChainGridSubdivisions + 1);
                    var mid = new (Vector3, (string, float)[], float, float, float, float, float, float)[rows];
                    for (var r = 0; r < rows; r++)
                    {
                        var left = columnSamples[c][r];
                        var right = columnSamples[c + 1][r];
                        var influences = left.Item2.Select(i => (i.Item1, i.Item2 * (1f - u)))
                            .Concat(right.Item2.Select(i => (i.Item1, i.Item2 * u)))
                            .OrderByDescending(i => i.Item2)
                            .Take(4)
                            .ToArray();

                        mid[r] = (
                            Vector3.Lerp(left.Item1, right.Item1, u),
                            influences,
                            r == 0 ? 0f : 1f,
                            left.Item4 + (right.Item4 - left.Item4) * u,
                            left.Item5 + (right.Item5 - left.Item5) * u,
                            left.Item6 + (right.Item6 - left.Item6) * u,
                            left.Item7 + (right.Item7 - left.Item7) * u,
                            left.Item8 + (right.Item8 - left.Item8) * u);
                    }

                    columns.Add(mid);
                }
            }

            var columnCount = columns.Count;
            var positions = new Vector3[columnCount * rows];
            var texcoords = new Vector2[columnCount * rows];
            var skin = new (string Bone, float Weight)[columnCount * rows][];
            var enable = new float[columnCount * rows];
            var strengthArr = new float[columnCount * rows];
            var radiusArr = new float[columnCount * rows];
            var dampingArr = new float[columnCount * rows];
            var frictionArr = new float[columnCount * rows];
            var dragArr = new float[columnCount * rows];

            for (var c = 0; c < columnCount; c++)
            {
                for (var r = 0; r < rows; r++)
                {
                    var v = c * rows + r;
                    var sample = columns[c][r];
                    positions[v] = sample.Item1;
                    skin[v] = [.. sample.Item2.Select(i => (i.Item1, i.Item2))];
                    enable[v] = sample.Item3;
                    strengthArr[v] = sample.Item4;
                    radiusArr[v] = sample.Item5;
                    dampingArr[v] = sample.Item6;
                    frictionArr[v] = sample.Item7;
                    dragArr[v] = sample.Item8;
                    texcoords[v] = new Vector2(columnCount > 1 ? (float)c / (columnCount - 1) : 0f, rows > 1 ? (float)r / (rows - 1) : 0f);
                }
            }

            var faces = new List<int[]>((columnCount - 1) * (rows - 1));
            for (var c = 0; c < columnCount - 1; c++)
            {
                for (var r = 0; r < rows - 1; r++)
                {
                    faces.Add([
                        c * rows + r,
                        (c + 1) * rows + r,
                        (c + 1) * rows + r + 1,
                        c * rows + r + 1,
                    ]);
                }
            }

            return new ChainGrid
            {
                Positions = positions,
                Texcoords = texcoords,
                SkinInfluences = skin,
                ClothEnable = enable,
                GoalStrength = strengthArr,
                CollisionRadius = radiusArr,
                GoalDamping = dampingArr,
                Friction = frictionArr,
                Drag = dragArr,
                Faces = faces,
            };
        }
    }
}
