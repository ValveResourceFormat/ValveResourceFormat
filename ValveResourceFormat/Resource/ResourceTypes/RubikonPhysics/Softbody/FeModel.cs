using System.Diagnostics;
using System.Globalization;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    /// <summary>
    /// Soft-body (cloth) model embedded in a physics aggregate (<c>m_pFeModel</c>).
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/PhysFeModelDesc_t">PhysFeModelDesc_t</seealso>
    public sealed partial class FeModel
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
        public int[] SkelParents { get; private set; }

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
        /// Gets the index of the first position-driven node, or <see cref="NodeCount"/> when there is none. Derived from the
        /// compiled arrays when the compile omits <c>m_nFirstPositionDrivenNode</c>.
        /// </summary>
        public int FirstPositionDrivenNode { get; }

        /// <summary>
        /// Gets whether the compile wrote <c>m_nFirstPositionDrivenNode</c> itself, as opposed to
        /// <see cref="FirstPositionDrivenNode"/> having been derived from the compiled arrays.
        /// </summary>
        public bool HasCompiledFirstPositionDrivenNode { get; }

        /// <summary>
        /// Gets the per-node rest (bind-pose) positions in model space, parsed from the first three
        /// components of each <c>m_InitPose</c> entry (the remaining components are the rest orientation
        /// quaternion). Length matches <see cref="NodeCount"/>.
        /// </summary>
        public Vector3[] InitPosePositions { get; }

        /// <summary>
        /// Gets the per-node rest orientations, parsed from the last four components of each
        /// <c>m_InitPose</c> entry. Length matches <see cref="InitPosePositions"/>.
        /// </summary>
        public Quaternion[] InitPoseRotations { get; }

        /// <summary>
        /// Gets the cloth surface quads. Each entry is a 4-element array of control-node indices.
        /// </summary>
        public int[][] Quads { get; }

        /// <summary>
        /// Gets the cloth surface triangles. Each entry is a 3-element array of control-node indices.
        /// </summary>
        public int[][] Tris { get; }

        /// <summary>Gets whether the cloth has quad or triangle solve elements.</summary>
        public bool HasSurfaceElements => Quads.Length > 0 || Tris.Length > 0;

        /// <summary>Gets the distance constraints between pairs of control nodes (<c>m_Rods</c>).</summary>
        public Rod[] Rods { get; }

        /// <summary>A single structural rod (from <c>m_Rods</c>).</summary>
        /// <param name="NodeA">First endpoint control-node index.</param>
        /// <param name="NodeB">Second endpoint control-node index.</param>
        /// <param name="MinDist">Minimum allowed distance (<c>flMinDist</c>).</param>
        /// <param name="MaxDist">Maximum allowed distance (<c>flMaxDist</c>).</param>
        /// <param name="Weight0">Share of <paramref name="NodeA"/> in the correction (<c>flWeight0</c>).</param>
        /// <param name="RelaxationFactor">Relaxation factor (<c>flRelaxationFactor</c>).</param>
        public readonly record struct Rod(int NodeA, int NodeB, float MinDist, float MaxDist, float Weight0, float RelaxationFactor);

        private HashSet<(int, int)>? animRodPairs;
        private List<AnimRod>? animRods;

        /// <summary>
        /// Gets the unordered node pairs of <c>m_SimdRodsAnim</c>, the rods of chain joints declared with
        /// <c>animated_length</c>. These rods appear nowhere else in the file.
        /// </summary>
        public IReadOnlySet<(int, int)> AnimRodPairs => animRodPairs ??= AnimRods
            .Select(static rod => rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA))
            .ToHashSet();

        /// <summary>
        /// One rod of <c>m_SimdRodsAnim</c>: its two nodes in lane order and the <c>f4Weight0</c> lane value, which is
        /// the share of <paramref name="NodeA"/> exactly as <see cref="Rod.Weight0"/> is on a rod kept in <c>m_Rods</c>.
        /// </summary>
        /// <param name="NodeA">First node of the lane.</param>
        /// <param name="NodeB">Second node of the lane.</param>
        /// <param name="Weight0">Blend weight of <paramref name="NodeA"/>.</param>
        public readonly record struct AnimRod(int NodeA, int NodeB, float Weight0);

        /// <summary>
        /// Gets the rods of <c>m_SimdRodsAnim</c>, one per distinct lane: the SIMD packing repeats a lane to fill a
        /// block of four.
        /// </summary>
        public IReadOnlyList<AnimRod> AnimRods => animRods ??= BuildAnimRods();

        private List<AnimRod> BuildAnimRods()
        {
            var rods = new List<AnimRod>();
            foreach (var entry in Data.GetArray("m_SimdRodsAnim") ?? [])
            {
                if (!entry.TryGetValue("nNode", out var nNodeValue) || !nNodeValue.IsArray)
                {
                    continue;
                }

                var flat = new List<int>(8);
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

                if (flat.Count < 8)
                {
                    continue;
                }

                var weights = entry.GetFloatArray("f4Weight0");
                for (var lane = 0; lane < 4; lane++)
                {
                    var rod = new AnimRod(flat[lane], flat[4 + lane], lane < weights.Length ? weights[lane] : 0.5f);
                    if (rod.NodeA != rod.NodeB && !rods.Contains(rod))
                    {
                        rods.Add(rod);
                    }
                }
            }

            return rods;
        }

        /// <summary>
        /// Gets the explicit local orientation basis of certain nodes (<c>m_NodeBases</c>), keyed by control-node index.
        /// Where a node has several records, the last one is kept.
        /// </summary>
        public IReadOnlyDictionary<int, NodeBasis> NodeBases { get; }

        /// <summary>
        /// Gets every <c>m_NodeBases</c> record in array order, as (node, basis). A modern compile keeps one
        /// record per node; an old-era one can carry several consecutive records for one node, of which
        /// <see cref="NodeBases"/> keeps the last.
        /// </summary>
        public IReadOnlyList<(int Node, NodeBasis Basis)> NodeBaseRecords { get; }

        /// <summary>A single node's explicit orientation basis (from <c>m_NodeBases</c>).</summary>
        /// <param name="NodeX0">Control-node index defining the local X axis' first endpoint.</param>
        /// <param name="NodeX1">Control-node index defining the local X axis' second endpoint.</param>
        /// <param name="NodeY0">Control-node index defining the local Y axis' first endpoint.</param>
        /// <param name="NodeY1">Control-node index defining the local Y axis' second endpoint.</param>
        public readonly record struct NodeBasis(int NodeX0, int NodeX1, int NodeY0, int NodeY1);

        /// <summary>
        /// Gets the <c>transform_alignment</c> and <c>node_base</c> references that compile to the node's
        /// <c>m_NodeBases</c> entry, or null when it has none. Alignment 3 returns X0 and Y0 as -1.
        /// </summary>
        public (int TransformAlignment, NodeBasis References)? ClothNodeBasisPreset(int node)
        {
            if (!NodeBases.TryGetValue(node, out var basis))
            {
                return null;
            }

            if (basis.NodeX0 == node && (basis.NodeY0 == node || basis.NodeY1 == node))
            {
                return (3, new NodeBasis(-1, basis.NodeX1, -1, basis.NodeY0 == node ? basis.NodeY1 : basis.NodeY0));
            }

            return (4, basis);
        }

        /// <summary>Gets the per-node solver integrator parameters (<c>m_NodeIntegrator</c>).</summary>
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
        /// Gets the world-collision radii (<c>m_NodeCollisionRadii</c>), indexed by dynamic node
        /// (control-node index minus <see cref="StaticNodeCount"/>).
        /// </summary>
        public float[] NodeCollisionRadii { get; }

        /// <summary>Gets the per-dynamic-node friction (<c>m_DynNodeFriction</c>).</summary>
        public float[] DynNodeFriction { get; }

        /// <summary>
        /// Reads a per-dynamic-node array at control node <paramref name="node"/>, or 0 when the node has
        /// no entry. The static nodes lead the control-node array, so these arrays start past them.
        /// </summary>
        internal float DynamicNodeValue(float[] values, int node)
        {
            var dynamicIndex = node - StaticNodeCount;
            return dynamicIndex >= 0 && dynamicIndex < values.Length ? values[dynamicIndex] : 0f;
        }

        /// <summary>Gets the world-collision radius for control node <paramref name="node"/>, or 0 when absent.</summary>
        public float GetCollisionRadius(int node) => DynamicNodeValue(NodeCollisionRadii, node);

        /// <summary>
        /// The collision mask a node carries when nothing in the source declares one: the compiler's own
        /// all-sixteen-layers default.
        /// </summary>
        public const int DefaultNodeCollisionMask = 0xFFFF;

        /// <summary>
        /// Gets the per-dynamic-node collision mask, the first <c>dynamicNodes</c> entries of
        /// <c>m_TreeCollisionMasks</c>. The rest of that array is the OR of each subtree and carries
        /// nothing of its own. Empty when the array is absent or not <c>2 * dynamicNodes - 1</c> long.
        /// </summary>
        public int[] NodeCollisionMasks { get; }

        /// <summary>
        /// Gets the collision mask of control node <paramref name="node"/>, or
        /// <see cref="DefaultNodeCollisionMask"/> where the model records none.
        /// </summary>
        public int GetNodeCollisionMask(int node)
        {
            var dynamicIndex = node - StaticNodeCount;
            return dynamicIndex >= 0 && dynamicIndex < NodeCollisionMasks.Length
                ? NodeCollisionMasks[dynamicIndex]
                : DefaultNodeCollisionMask;
        }

        /// <summary>
        /// Gets the control nodes that collide with the world (<c>m_WorldCollisionNodes</c>), from
        /// per-joint <c>world_collision</c> in the source. Empty for cloth without world collision.
        /// </summary>
        public IReadOnlySet<int> WorldCollisionNodes { get; }

        /// <summary>
        /// Gets the world and ground friction of each world-colliding node
        /// (<c>m_WorldCollisionParams</c>), from per-joint <c>world_friction</c>/<c>ground_friction</c>.
        /// </summary>
        public IReadOnlyDictionary<int, (float World, float Ground)> WorldCollisionFriction { get; }

        /// <summary>Gets the world and ground friction for <paramref name="node"/>, or zero for both.</summary>
        public (float World, float Ground) GetWorldFriction(int node)
            => WorldCollisionFriction.GetValueOrDefault(node);

        /// <summary>Returns whether <paramref name="node"/> collides with the world.</summary>
        public bool IsWorldCollisionNode(int node) => WorldCollisionNodes.Contains(node);

        /// <summary>
        /// Gets the per-node animation stray radii (<c>m_AnimStrayRadii</c>): the maximum distance a
        /// simulated node may stray from its animated position (per-joint <c>stray_radius</c> in the source).
        /// </summary>
        public IReadOnlyDictionary<int, (float MaxDistance, float RelaxationFactor)> AnimStrayRadii { get; }

        /// <summary>Gets the control nodes driven by a back-solved fit matrix (<c>m_FitMatrices</c>).</summary>
        public IReadOnlySet<int> FitMatrixNodes { get; }

        /// <summary>
        /// Gets the subset of <see cref="FitMatrixNodes"/> whose fit covers a proxy sheet vertex
        /// (<c>$cloth_m&lt;N&gt;p&lt;S&gt;</c>), i.e. the bones a proxy sheet back-solves.
        /// </summary>
        public IReadOnlySet<int> ProxyFitMatrixNodes { get; }

        /// <summary>
        /// Gets the control nodes each <c>m_FitMatrices</c> entry is fit over, from its own
        /// <c>m_FitWeights</c> range, keyed by the bone the fit drives.
        /// </summary>
        public IReadOnlyDictionary<int, int[]> FitMatrixTargets { get; }

        /// <summary>
        /// Gets the authored skin weights of back-solved proxy-sheet vertices, keyed by control node, recovered from
        /// <c>m_FitWeights</c>, <c>m_CtrlOffsets</c> and <c>m_CtrlSoftOffsets</c>.
        /// </summary>
        public IReadOnlyDictionary<int, (string Bone, float Weight)[]> RecoveredSkinWeights { get; }

        /// <summary>
        /// Gets the offset-network skin weights of the proxy-sheet vertices <see cref="RecoveredSkinWeights"/> leaves out,
        /// keyed by control node.
        /// </summary>
        public IReadOnlyDictionary<int, (string Bone, float Weight)[]> DeferredOffsetSkinWeights { get; }

        /// <summary>Gets the control nodes named by any twist constraint (<c>m_Twists</c>).</summary>
        public IReadOnlySet<int> TwistNodes { get; }

        /// <summary>
        /// Gets every <c>m_Twists</c> record in array order. The compiler appends one record per directed
        /// pair per chain declaration and never de-duplicates, so a node or a pair can carry several.
        /// </summary>
        public IReadOnlyList<TwistRecord> TwistRecords { get; }

        /// <summary>A single twist constraint record (from <c>m_Twists</c>).</summary>
        /// <param name="Orient">The node whose frame the twist is measured in.</param>
        /// <param name="End">The node the twist is measured toward.</param>
        /// <param name="TwistRelax">The record's <c>flTwistRelax</c>.</param>
        /// <param name="SwingRelax">The record's <c>flSwingRelax</c>.</param>
        public readonly record struct TwistRecord(int Orient, int End, float TwistRelax, float SwingRelax);

        /// <summary>Gets the unordered node pairs a twist constraint spans.</summary>
        public IReadOnlySet<(int, int)> TwistLinks { get; }

        /// <summary>
        /// Gets whether a twist constraint spans <paramref name="node"/> and its chain parent
        /// <paramref name="parent"/>, which is what that joint's own <c>twist_relax</c> generates.
        /// </summary>
        public bool HasTwistToParent(int node, int parent)
            => parent >= 0 && TwistLinks.Contains(node < parent ? (node, parent) : (parent, node));

        /// <summary>Gets whether the joint at <paramref name="node"/> was authored with a non-zero <c>twist_relax</c>.</summary>
        public bool HasAuthoredTwist(int node, int parent)
            => parent >= 0 ? HasTwistToParent(node, parent) : TwistNodes.Contains(node);

        /// <summary>
        /// Gets the <c>flTwistRelax</c> of each directed (<c>nNodeOrient</c>, <c>nNodeEnd</c>) pair; the last entry wins
        /// where a pair has several.
        /// </summary>
        public IReadOnlyDictionary<(int Orient, int End), float> TwistRelaxByLink { get; }

        /// <summary>
        /// Gets every <c>flTwistRelax</c> of each directed pair in array order, one per chain declaration that wrote it.
        /// </summary>
        public IReadOnlyDictionary<(int Orient, int End), IReadOnlyList<float>> TwistRelaxCopies { get; }

        internal const float TwistRelaxToParentFactor = 0.618f;
        internal const float TwistRelaxToChildFactor = 0.382f;

        /// <summary>
        /// Gets whether a twist link naming <paramref name="node"/> carries a zero <c>flTwistRelax</c> in both directions.
        /// </summary>
        public bool HasRelaxlessTwistLink(int node) => relaxlessTwistNodes.Contains(node);

        /// <summary>Gets whether <paramref name="node"/> orients a twist entry with a zero <c>flTwistRelax</c>.</summary>
        public bool OrientsRelaxlessTwist(int node) => relaxlessTwistOrients.Contains(node);

        private readonly HashSet<int> relaxlessTwistNodes = [];

        private readonly HashSet<int> relaxlessTwistOrients = [];

        /// <summary>
        /// Recovers the joint's authored <c>twist_relax</c> from its entry toward its ring node
        /// <paramref name="proxyNode"/>, else toward <paramref name="parent"/>, else from any entry it orients.
        /// </summary>
        public float GetAuthoredTwistRelax(int node, int parent, int proxyNode)
        {
            if (proxyNode >= 0 && TwistRelaxByLink.TryGetValue((node, proxyNode), out var toRing))
            {
                return toRing / TwistRelaxToChildFactor;
            }

            if (parent >= 0 && TwistRelaxByLink.TryGetValue((node, parent), out var toParent))
            {
                return toParent / TwistRelaxToParentFactor;
            }

            return twistOrientFallback.TryGetValue(node, out var toAnyChild)
                ? toAnyChild / TwistRelaxToChildFactor
                : 0f;
        }

        /// <summary>
        /// The <c>twist_relax</c> the declaration at <paramref name="rank"/> stated toward the joint's
        /// own parent, or null where the pair carries no entry at that rank.
        /// </summary>
        public float? TwistRelaxDeclaredAt(int node, int parent, int rank)
            => parent >= 0 && TwistRelaxCopies.TryGetValue((node, parent), out var copies)
                && rank >= 0 && rank < copies.Count
                ? copies[rank] / TwistRelaxToParentFactor
                : null;

        private readonly Dictionary<int, float> twistOrientFallback = [];

        internal const float ClothDragPointDampingScale = 30f;

        internal const float ClothSourceBaseGravity = 360f;

        internal const float GoalDampingSolveMaxAttraction = 0.9999f;
        internal const float GoalDampingSolveMinAttraction = 0.0001f;

        /// <summary>
        /// Recovers the source <c>goal_strength</c> from a node's compiled
        /// <c>flAnimationForceAttraction</c>, which the compiler writes as the cube of it.
        /// </summary>
        public static float GoalStrengthFromAttraction(float forceAttraction)
            => MathF.Cbrt(Math.Clamp(forceAttraction, 0f, 1f));

        /// <summary>
        /// Gets the authored <c>ClothParams.goal_strength_bias</c>: the gap between the cube roots of the force and vertex
        /// attractions shared by most goal-damped nodes, or 0 when no such majority exists.
        /// </summary>
        public float GoalStrengthBias => goalStrengthBias ??= ComputeGoalStrengthBias();

        private float? goalStrengthBias;

        private const float GoalStrengthBiasQuantum = 10000f;

        private const int GoalStrengthBiasMinNodes = 8;

        private const float GoalStrengthBiasMinShare = 0.5f;

        private float ComputeGoalStrengthBias()
        {
            var counts = new Dictionary<int, int>();
            var constraining = 0;
            for (var node = 0; node < NodeCount; node++)
            {
                var integrator = GetIntegrator(node);
                var fa = integrator.ForceAttraction;
                var va = integrator.VertexAttraction;
                if (fa <= 0f || fa >= 1f || va <= 0f || !UsesGoalDampedIntegrator(node))
                {
                    continue;
                }

                constraining++;
                var gap = (int)MathF.Round((MathF.Cbrt(fa) - MathF.Cbrt(va)) * GoalStrengthBiasQuantum);
                counts[gap] = counts.GetValueOrDefault(gap) + 1;
            }

            if (constraining < GoalStrengthBiasMinNodes)
            {
                return 0f;
            }

            var mode = 0;
            var agreeing = 0;
            foreach (var (gap, count) in counts)
            {
                if (count > agreeing)
                {
                    agreeing = count;
                    mode = gap;
                }
            }

            if (mode > 0 && agreeing >= GoalStrengthBiasMinShare * constraining)
            {
                return mode / GoalStrengthBiasQuantum;
            }

            var top = counts.Keys.Max();
            return top > 0 && counts[top] + counts.GetValueOrDefault(top - 1) >= GoalStrengthBiasMinSupport
                ? top / GoalStrengthBiasQuantum
                : 0f;
        }

        private const int GoalStrengthBiasMinSupport = 3;

        /// <summary>
        /// Gets the <c>cloth_goal_strength_v2</c> paint for a compiled force attraction: its cube root less
        /// <see cref="GoalStrengthBias"/>. A saturated attraction keeps the plain cube root.
        /// </summary>
        public float GoalStrengthPaint(float forceAttraction)
            => GoalStrengthBias <= 0f || forceAttraction >= 1f
                ? GoalStrengthFromAttraction(forceAttraction)
                : forceAttraction <= 0f
                    ? -MathF.Cbrt(GoalStrengthBias)
                    : Math.Clamp(GoalStrengthFromAttraction(forceAttraction) - GoalStrengthBias, 0f, 1f);

        /// <summary>
        /// Gets the <c>cloth_goal_damping</c> paint that goes with <see cref="GoalStrengthPaint"/>, solved against the
        /// unbiased goal strength.
        /// </summary>
        public float GoalDampingPaint(float forceAttraction, float vertexAttraction)
        {
            if (GoalStrengthBias <= 0f || forceAttraction >= 1f)
            {
                return GoalDampingFromAttraction(forceAttraction, vertexAttraction);
            }

            var strength = Math.Max(GoalStrengthPaint(forceAttraction), 0f);
            return GoalDampingFromAttraction(strength * strength * strength, vertexAttraction);
        }

        /// <summary>
        /// Recovers the source <c>goal_damping</c> by inverting <c>va = 1 - ((1-fa) / (sqrt((1-fa)*fa + d*d) + d))^2 * fa</c>.
        /// </summary>
        public static float GoalDampingFromAttraction(float forceAttraction, float vertexAttraction)
        {
            if (forceAttraction is >= GoalDampingSolveMaxAttraction or < GoalDampingSolveMinAttraction)
            {
                return Math.Clamp(vertexAttraction, 0f, 1f);
            }

            var t = MathF.Sqrt(Math.Clamp(1f - vertexAttraction, 0f, 1f) / forceAttraction);
            if (t <= 0f)
            {
                return 1f;
            }

            var s = (1f - forceAttraction) / t;
            return Math.Clamp((s * s - (1f - forceAttraction) * forceAttraction) / (2f * s), 0f, 1f);
        }

        internal const float ClothRawGoalScale = 30f;

        const uint NodeFlagGoalAttraction = 0x80;
        const uint NodeFlagRawForceAttraction = 0x200;
        const uint NodeFlagRawVertexAttraction = 0x400;

        /// <summary>
        /// Gets whether <paramref name="node"/> compiled on the goal-damped spring integrator rather than the raw one.
        /// </summary>
        public bool UsesGoalDampedIntegrator(int node)
        {
            var dynamicIndex = node - StaticNodeCount;
            if (dynamicIndex >= 0 && (dynamicIndex >> 5) < GoalDampedSpringIntegrators.Length)
            {
                return (GoalDampedSpringIntegrators[dynamicIndex >> 5] & (1u << (dynamicIndex & 31))) != 0;
            }

            var flags = dynamicIndex >= 0 ? DynamicNodeFlags : StaticNodeFlags;
            if ((flags & (NodeFlagRawForceAttraction | NodeFlagRawVertexAttraction)) == 0)
            {
                return true;
            }

            if ((flags & NodeFlagGoalAttraction) == 0)
            {
                return false;
            }

            var integrator = GetIntegrator(node);
            return GoalSolveCanProduce(integrator.ForceAttraction, integrator.VertexAttraction);
        }

        /// <summary>Gets whether the goal-damped solve can produce this pair of attractions.</summary>
        static bool GoalSolveCanProduce(float forceAttraction, float vertexAttraction)
        {
            if (forceAttraction is < 0f or > 1f || vertexAttraction is < 0f or > 1f)
            {
                return false;
            }

            return forceAttraction is >= GoalDampingSolveMaxAttraction or < GoalDampingSolveMinAttraction
                || vertexAttraction >= forceAttraction - 1e-4f;
        }

        static int[] BuildRopeParents(KVObject data)
        {
            var nodeCount = data.GetInt32Property("m_nNodeCount");
            if (nodeCount <= 0)
            {
                return [];
            }

            var parents = new int[nodeCount];
            Array.Fill(parents, -1);
            var parented = false;

            void Adopt(int node, int parent)
            {
                if (node >= 0 && node < nodeCount && parent >= 0 && parent < nodeCount
                    && node != parent && parents[node] < 0)
                {
                    parents[node] = parent;
                    parented = true;
                }
            }

            var ropeCount = data.GetInt32Property("m_nRopeCount");
            var ropes = data.GetIntegerArray("m_Ropes");
            if (ropeCount > 0 && ropes.Length > ropeCount)
            {
                var begin = ropeCount;
                for (var rope = 0; rope < ropeCount; rope++)
                {
                    var end = Math.Min((int)ropes[rope], ropes.Length);
                    for (var i = begin + 1; i < end; i++)
                    {
                        Adopt((int)ropes[i], (int)ropes[i - 1]);
                    }

                    begin = end;
                }
            }

            foreach (var follow in data.GetArray("m_FollowNodes") ?? [])
            {
                Adopt(follow.GetInt32Property("nChildNode"), follow.GetInt32Property("nParentNode"));
            }

            var names = data.GetArray<string>("m_CtrlName") ?? [];
            var twists = data.GetArray("m_Twists") ?? [];
            for (var k = 0; k < twists.Count; k++)
            {
                var orient = twists[k].GetInt32Property("nNodeOrient");
                var end = twists[k].GetInt32Property("nNodeEnd");
                var paired = k + 1 < twists.Count && twists[k + 1].GetInt32Property("nNodeOrient") == end
                    && twists[k + 1].GetInt32Property("nNodeEnd") == orient;
                if (orient < names.Length && end < names.Length && !IsProxyNodeName(names[orient]) && !IsProxyNodeName(names[end]))
                {
                    if (paired)
                    {
                        Adopt(end, orient);
                    }
                    else
                    {
                        Adopt(orient, end);
                    }
                }

                if (paired)
                {
                    k++;
                }
            }

            return parented ? parents : [];
        }

        static void ExpectPair(Dictionary<(int, int), List<float>> expectations, int a, int b, float relaxation)
        {
            if (a < 0 || b < 0)
            {
                return;
            }

            var key = a < b ? (a, b) : (b, a);
            if (!expectations.TryGetValue(key, out var values))
            {
                expectations[key] = values = [];
            }

            values.Add(relaxation);
        }

        /// <summary>
        /// Gets the rods <paramref name="chains"/> regenerate by themselves, keyed by unordered pair, each entry the rod's
        /// expected relaxation factor.
        /// </summary>
        Dictionary<(int, int), List<float>> ChainGeneratedSpans(List<BoneChain> chains)
        {
            var generated = new Dictionary<(int, int), List<float>>();
            var sliderScale = MathF.Exp(-DefaultSurfaceStretch);

            void Generate(int a, int b, float slider) => ExpectPair(generated, a, b, slider * sliderScale);

            foreach (var chain in chains)
            {
                var byNode = chain.Joints.ToDictionary(static j => j.Node);
                var rootNode = chain.Joints.Find(static j => j.IsRoot)?.Node ?? -1;
                foreach (var joint in chain.Joints)
                {
                    var parent = joint.ParentNode;
                    var grandParent = parent >= 0 && byNode.TryGetValue(parent, out var p1) ? p1.ParentNode : -1;
                    var greatGrandParent = grandParent >= 0 && byNode.TryGetValue(grandParent, out var p2)
                        ? p2.ParentNode
                        : -1;

                    for (var copy = 0; copy <= joint.ExtraIterations; copy++)
                    {
                        if (joint.StretchStiffness != 0f)
                        {
                            Generate(parent, joint.Node, joint.StretchStiffness);
                        }

                        if (joint.BendSpring)
                        {
                            Generate(grandParent, joint.Node, joint.BendStiffness);
                        }

                        if (joint.TorsionSpring)
                        {
                            Generate(greatGrandParent, joint.Node, joint.TorsionStiffness);
                        }

                        if (joint.Suspender != 0f)
                        {
                            ExpectPair(generated, rootNode, joint.Node, joint.Suspender);
                        }

                        if (joint.ChildSiblingSpring != 0f)
                        {
                            var kids = chain.Joints.FindAll(other => other.ParentNode == joint.Node);
                            for (var i = 0; i < kids.Count; i++)
                            {
                                for (var j = i + 1; j < kids.Count; j++)
                                {
                                    Generate(kids[i].Node, kids[j].Node, joint.ChildSiblingSpring);
                                }
                            }
                        }
                    }
                }
            }

            return generated;
        }

        /// <summary>
        /// Gets the two-corner source elements between chain joints or <c>$cc</c> ring nodes that the chains do not span,
        /// with how many rod copies each one has to declare.
        /// </summary>
        public List<(int A, int B, int Copies)> GetAuthoredSourceSprings(List<BoneChain> chains)
        {
            if (SourceSprings.Length == 0)
            {
                return [];
            }

            bool IsChainRing(int node) => node >= 0 && node < CtrlNames.Length
                && CtrlNames[node].StartsWith("$cc", StringComparison.Ordinal);

            var joints = new HashSet<int>();
            foreach (var chain in chains)
            {
                foreach (var joint in chain.Joints)
                {
                    joints.Add(joint.Node);
                }
            }

            bool IsEndpoint(int node) => IsChainRing(node) || joints.Contains(node);

            var spanned = ChainGeneratedSpans(chains);
            var authored = new List<(int, int)>(SourceSprings.Length);
            var occurrences = new Dictionary<(int, int), int>();
            foreach (var (a, b) in SourceSprings)
            {
                if (IsEndpoint(a) && IsEndpoint(b))
                {
                    authored.Add((a, b));
                    var key = a < b ? (a, b) : (b, a);
                    occurrences[key] = occurrences.GetValueOrDefault(key) + 1;
                }
            }

            var copies = new Dictionary<(int, int), int>();
            var clusterRods = SelfCollisionClusterRods;
            for (var i = 0; i < Rods.Length; i++)
            {
                var key = Rods[i].NodeA < Rods[i].NodeB
                    ? (Rods[i].NodeA, Rods[i].NodeB)
                    : (Rods[i].NodeB, Rods[i].NodeA);
                if (!clusterRods.Contains(i) && occurrences.ContainsKey(key))
                {
                    copies[key] = copies.GetValueOrDefault(key) + 1;
                }
            }

            var springs = new List<(int, int, int)>(authored.Count);
            foreach (var (a, b) in authored)
            {
                var key = a < b ? (a, b) : (b, a);

                var generated = spanned.TryGetValue(key, out var spans) ? spans.Count : 0;
                if (occurrences[key] > 1)
                {
                    if (generated == 0)
                    {
                        springs.Add((a, b, 1));
                    }

                    continue;
                }

                var surplus = Math.Max(1, copies.GetValueOrDefault(key)) - generated;
                if (surplus > 0)
                {
                    springs.Add((a, b, surplus));
                }
            }

            return springs;
        }

        /// <summary>Returns the rods that <paramref name="chains"/> and the authored source springs do not regenerate.</summary>
        /// <param name="chains">The chains the export emits.</param>
        /// <param name="surfaceFansRegenerate">Whether the rods folded across shared face edges are regenerated.</param>
        public List<Rod> GetUngeneratedRods(List<BoneChain> chains, bool surfaceFansRegenerate = false)
        {
            var generated = ChainGeneratedSpans(chains);

            foreach (var (a, b, copies) in GetAuthoredSourceSprings(chains))
            {
                for (var copy = 0; copy < copies; copy++)
                {
                    ExpectPair(generated, a, b, float.NaN);
                }
            }

            var entriesByPair = new Dictionary<(int, int), List<int>>();
            for (var i = 0; i < Rods.Length; i++)
            {
                var key = Rods[i].NodeA < Rods[i].NodeB
                    ? (Rods[i].NodeA, Rods[i].NodeB)
                    : (Rods[i].NodeB, Rods[i].NodeA);
                if (!entriesByPair.TryGetValue(key, out var entries))
                {
                    entriesByPair[key] = entries = [];
                }

                entries.Add(i);
            }

            var claimed = new bool[Rods.Length];

            foreach (var index in SelfCollisionClusterRods)
            {
                claimed[index] = true;
            }

            if (surfaceFansRegenerate)
            {
                for (var i = 0; i < Rods.Length; i++)
                {
                    claimed[i] |= IsSurfaceFanRod(Rods[i], banded: false);
                }
            }

            foreach (var (key, expectations) in generated)
            {
                if (!entriesByPair.TryGetValue(key, out var entries))
                {
                    continue;
                }

                foreach (var expected in expectations)
                {
                    var best = -1;
                    var bestScore = float.MaxValue;
                    foreach (var index in entries)
                    {
                        if (claimed[index])
                        {
                            continue;
                        }

                        var rod = Rods[index];
                        var banded = MathF.Abs(rod.MinDist - rod.MaxDist)
                            > 1e-4f * MathF.Max(1f, MathF.Abs(rod.MaxDist));
                        var score = (banded ? 1f : 0f) + (float.IsNaN(expected)
                            ? 0f
                            : 0.5f * MathF.Min(1f, MathF.Abs(rod.RelaxationFactor - expected)));
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = index;
                        }
                    }

                    if (best >= 0)
                    {
                        claimed[best] = true;
                    }
                }
            }

            var surplus = new List<Rod>();
            for (var i = 0; i < Rods.Length; i++)
            {
                if (!claimed[i])
                {
                    surplus.Add(Rods[i]);
                }
            }

            return surplus;
        }

        /// <summary>
        /// Gets whether every simulated node collides with the world, which is how the source's
        /// force-world-collision-on-all-nodes switch shows up (the switch itself leaves no flag bit).
        /// </summary>
        public bool ForcesWorldCollisionOnAllNodes
            => NodeCount > StaticNodeCount && WorldCollisionNodes.Count == NodeCount - StaticNodeCount;

        /// <summary>
        /// Gets the ground friction shared by the world-colliding nodes, which is what the source authored
        /// as the cloth's default. Zero when the model has no world collision params.
        /// </summary>
        public float DefaultGroundFriction => WorldCollisionFriction.Count > 0
            ? WorldCollisionFriction.Values.GroupBy(static f => f.Ground).OrderByDescending(static g => g.Count()).First().Key
            : 0f;

        /// <summary>Gets the stray radius for <paramref name="node"/>, or 0 when unconstrained.</summary>
        public float GetStrayRadius(int node) => AnimStrayRadii.GetValueOrDefault(node).MaxDistance;

        /// <summary>
        /// Gets the scale every compiled <c>flRelaxationFactor</c> of <c>m_AnimStrayRadii</c> carries:
        /// <c>exp(-m_flDefaultThreadStretch)</c>, and 1 for a model that authors no thread stretch.
        /// </summary>
        float StrayRelaxationScale => DefaultThreadStretch <= 0f ? 1f : MathF.Exp(-DefaultThreadStretch);

        /// <summary>
        /// Gets the authored relaxation factor of <paramref name="node"/>'s stray radius, or 1 for a node with none.
        /// </summary>
        public float GetStrayRelaxationFactor(int node)
            => AnimStrayRadii.TryGetValue(node, out var stray)
                ? Math.Clamp(stray.RelaxationFactor / StrayRelaxationScale, 0f, 1f)
                : 1f;

        /// <summary>Gets the authored stray-radius stretchiness of <paramref name="node"/>, 0 for a node with none.</summary>
        public float GetStrayStretchiness(int node)
            => AnimStrayRadii.ContainsKey(node) ? 1f - GetStrayRelaxationFactor(node) : 0f;

        /// <summary>
        /// Gets the node a chain joint's stray radius is recorded on: the joint node itself, else the first of its
        /// extruded proxies that carries one.
        /// </summary>
        public int StrayRadiusNode(int node, string jointName)
        {
            if (AnimStrayRadii.ContainsKey(node))
            {
                return node;
            }

            for (var proxy = 0; proxy < CtrlNames.Length; proxy++)
            {
                if (AnimStrayRadii.ContainsKey(proxy) && IsChainProxyOf(CtrlNames[proxy], jointName))
                {
                    return proxy;
                }
            }

            return node;
        }

        /// <summary>
        /// Gets whether <paramref name="ctrlName"/> is <c>$cc&lt;joint&gt;_Ctr</c> or <c>$cc&lt;joint&gt;_&lt;index&gt;</c>.
        /// </summary>
        static bool IsChainProxyOf(string ctrlName, string jointName)
        {
            if (!ctrlName.StartsWith("$cc", StringComparison.Ordinal))
            {
                return false;
            }

            var body = ctrlName.AsSpan(3);
            if (!body.StartsWith(jointName, StringComparison.Ordinal) || body.Length <= jointName.Length + 1
                || body[jointName.Length] != '_')
            {
                return false;
            }

            var suffix = body[(jointName.Length + 1)..];
            if (suffix.SequenceEqual("Ctr"))
            {
                return true;
            }

            foreach (var c in suffix)
            {
                if (!char.IsAsciiDigit(c))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Gets whether <paramref name="node"/> keeps its rotation free. Static nodes are ordered
        /// rotation-locked first, so the lock is exactly the nodes below
        /// <see cref="RotationLockedStaticNodeCount"/>.
        /// </summary>
        public bool AllowsRotation(int node) => node >= RotationLockedStaticNodeCount;

        /// <summary>Gets whether the node is position-driven (back-solved rather than simulated).</summary>
        public bool IsPositionDriven(int node) => node >= FirstPositionDrivenNode;

        static int DeriveFirstPositionDrivenNode(KVObject data, string[] ctrlNames, int nodeCount, int staticNodes)
        {
            var driven = new HashSet<int>();

            foreach (var fit in data.GetArray("m_FitMatrices") ?? [])
            {
                driven.Add(fit.GetInt32Property("nNode"));
            }

            foreach (var offset in data.GetArray("m_ReverseOffsets") ?? [])
            {
                driven.Add(offset.GetInt32Property("nBoneCtrl"));
            }

            var ringSides = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var name in ctrlNames)
            {
                if (!name.StartsWith("$cc", StringComparison.Ordinal))
                {
                    continue;
                }

                var split = name.LastIndexOf('_');
                if (split <= 3 || !int.TryParse(name.AsSpan(split + 1), NumberStyles.None, CultureInfo.InvariantCulture, out _))
                {
                    continue;
                }

                var owner = name[3..split];
                ringSides[owner] = ringSides.GetValueOrDefault(owner) + 1;
            }

            foreach (var (owner, sides) in ringSides)
            {
                var joint = sides >= 2 ? Array.IndexOf(ctrlNames, owner) : -1;
                if (joint >= 0)
                {
                    driven.Add(joint);
                }
            }

            var first = nodeCount;
            while (first > staticNodes && driven.Contains(first - 1))
            {
                first--;
            }

            return first;
        }

        /// <summary>
        /// Gets whether <paramref name="node"/> is held at a fixed offset from its parent
        /// (<c>m_LockToParent</c>) rather than simulated.
        /// </summary>
        public bool IsLockedToParent(int node) => Array.Exists(LockToParent, link => link.CtrlChild == node);

        /// <summary>
        /// Gets whether <paramref name="node"/> is held at its animated goal (<c>m_LockToGoal</c>), the
        /// lock a non-simulated node takes when it has no parent to be offset from.
        /// </summary>
        public bool IsLockedToGoal(int node) => Array.IndexOf(LockToGoal, node) >= 0;

        /// <summary>
        /// Gets whether <paramref name="node"/> was authored with <c>lock_translation</c>: its parent or goal lock is one
        /// that the fit-influence pass could not have written for it.
        /// </summary>
        public bool LocksTranslation(int node, int chainVersion = 2, BoneChain? chain = null)
            => (IsLockedToParent(node) && !((chainVersion < 2 || !ChainPresetsJoint(node, chain))
                    && ReachesParentLockUnkeyed(node) && ChainStagesFitGroup(node, chainVersion, chain)))
                || (IsLockedToGoal(node) && !IsStatic(node));

        bool ChainPresetsJoint(int node, BoneChain? chain)
        {
            var joint = chain?.Joints.Find(candidate => candidate.Node == node);
            if (chain is null || joint is null)
            {
                return true;
            }

            var child = chain.Joints.Find(candidate => candidate.ParentNode == node);
            return child is not null && ChainNodeBaseCandidates(joint, child) is not null;
        }

        bool ChainStagesFitGroup(int node, int chainVersion, BoneChain? chain)
        {
            var joint = chain?.Joints.Find(candidate => candidate.Node == node);
            if (chain is null || joint is null || ProxyFitMatrixNodes.Contains(node))
            {
                return true;
            }

            if (joint.Simulated)
            {
                return false;
            }

            var table = FitListOf(node);
            var stagesOwnList = table.Count > 1 || !table.Contains(node);
            foreach (var child in chain.Joints)
            {
                if (child.ParentNode == node)
                {
                    table.UnionWith(FitListOf(child.Node));
                }
            }

            var hasParent = joint.ParentNode >= 0 || (node < SkelParents.Length && SkelParents[node] >= 0);
            return (stagesOwnList && table.Count >= 3) || (chainVersion >= 1 && table.Count is > 0 and < 3 && hasParent);
        }

        /// <summary>
        /// The nodes a chain joint stages fit influences from: its numbered ring nodes and its <c>end_effector</c> centre node, and the
        /// joint itself when its ring is narrower than two nodes. Rings are matched by name, so a compile without <c>m_SkelParents</c>
        /// reads them too.
        /// </summary>
        HashSet<int> FitListOf(int jointNode)
        {
            var list = new HashSet<int>();
            var prefix = "$cc" + CtrlNames[jointNode] + "_";
            var wide = false;
            for (var node = 0; node < CtrlNames.Length; node++)
            {
                var name = CtrlNames[node];
                if (!name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var suffix = name.AsSpan(prefix.Length);
                if (suffix.SequenceEqual("Ctr") || (suffix.Length > 0 && int.TryParse(suffix, out _)))
                {
                    list.Add(node);
                    wide |= suffix.SequenceEqual("1");
                }
            }

            if (!wide)
            {
                list.Add(jointNode);
            }

            return list;
        }

        bool ReachesParentLockUnkeyed(int node)
        {
            if (!IsStatic(node) || !AllowsRotation(node) || (!NodeBases.ContainsKey(node) && !FitMatrixNodes.Contains(node)))
            {
                return false;
            }

            return Array.Exists(LockToParent, link => link.CtrlChild == node
                && (!IsStatic(link.CtrlParent) || AllowsRotation(link.CtrlParent))
                && !FitsOverInfluencesOf(link.CtrlParent, node));
        }

        bool FitsOverInfluencesOf(int parent, int child)
        {
            if (!FitMatrixTargets.TryGetValue(parent, out var targets))
            {
                return false;
            }

            int[] influences = FitMatrixTargets.TryGetValue(child, out var own) ? own
                : NodeBases.TryGetValue(child, out var basis) ? [basis.NodeX0, basis.NodeX1, basis.NodeY0, basis.NodeY1]
                : [];
            return influences.Length > 0 && Array.TrueForAll(influences, influence => Array.IndexOf(targets, influence) >= 0)
                || own is not null && HoldsOneWideEntryOf(targets, child);
        }

        bool HoldsOneWideEntryOf(int[] targets, int node)
        {
            if (Array.IndexOf(targets, node) < 0)
            {
                return false;
            }

            var children = 0;
            for (var i = 0; i < SkelParents.Length; i++)
            {
                if (SkelParents[i] != node)
                {
                    continue;
                }

                if (Array.IndexOf(targets, i) < 0)
                {
                    return false;
                }

                children++;
            }

            return children > 0;
        }

        /// <summary>Recovers each proxy vertex's normal as the local +Z of its rest orientation.</summary>
        public Vector3[] RecoverRestNormals(ProxyMesh proxy)
        {
            var normals = new Vector3[proxy.Positions.Length];
            for (var v = 0; v < normals.Length; v++)
            {
                var node = v < proxy.NodeIndices.Length ? proxy.NodeIndices[v] : -1;
                var rotation = node >= 0 && node < InitPoseRotations.Length
                    ? InitPoseRotations[node]
                    : Quaternion.Identity;

                var axis = Vector3.Transform(Vector3.UnitZ, rotation);
                normals[v] = axis.LengthSquared() > 1e-12f ? Vector3.Normalize(axis) : Vector3.UnitZ;
            }

            return normals;
        }

        /// <summary>
        /// Recovers the <c>cloth_mass</c> paint of an authored-face proxy sheet from what each node's mass carries beyond
        /// its geometric term, or null when the sheet carries none.
        /// </summary>
        public float[]? RecoverMassPaint(ProxyMesh proxy)
        {
            if (!proxy.UsesAuthoredFaces || HasExplicitMasses)
            {
                return null;
            }

            var count = proxy.Positions.Length;
            var geometric = GeometricNodeMasses();
            var paint = new float[count];
            var painted = new List<(int Vertex, float Tolerance)>();
            var clamped = 0;

            for (var v = 0; v < count; v++)
            {
                var node = proxy.NodeIndices[v];
                var invMass = node >= 0 && node < NodeInvMasses.Length ? NodeInvMasses[node] : 0f;
                if (invMass <= 0f || invMass >= 1f)
                {
                    continue;
                }

                if (node >= geometric.Length || geometric[node] <= 0f)
                {
                    clamped++;
                    continue;
                }

                var residual = 1f / invMass - geometric[node];

                if (residual <= MinRecoverableMassPaintTerm || residual > MaxRecoverableMassPaintTerm)
                {
                    clamped++;
                    continue;
                }

                paint[v] = MathF.Log(residual);
                painted.Add((v, (MathF.BitIncrement(invMass) - invMass) / (invMass * invMass) / residual));
            }

            if (painted.Count <= clamped)
            {
                return null;
            }

            if (UniformMassPaint([.. painted.Select(p => (paint[p.Vertex], p.Tolerance))]) is { } shared)
            {
                foreach (var (vertex, _) in painted)
                {
                    paint[vertex] = shared;
                }
            }
            else
            {
                var vertexOfNode = new Dictionary<int, int>();
                foreach (var (vertex, _) in painted)
                {
                    vertexOfNode[proxy.NodeIndices[vertex]] = vertex;
                }

                var rods = new List<(int A, int B, float Weight0)>();
                foreach (var (_, _, rod) in AuthoredFaceRods([.. vertexOfNode.Keys]))
                {
                    if (vertexOfNode.TryGetValue(rod.NodeA, out var a) && vertexOfNode.TryGetValue(rod.NodeB, out var b))
                    {
                        rods.Add((a, b, rod.Weight0));
                    }
                }

                paint = RefineMassPaint(paint, painted, rods);
            }

            return paint;
        }

        /// <summary>
        /// Refines <c>cloth_mass</c> readings with the face rods between painted vertices, each of which states
        /// <c>paint[b] - paint[a] = ln(w / (1 - w))</c>. A connected set keeps its readings unless every refined value
        /// stays within <see cref="UniformMassPaintSteps"/> of them.
        /// </summary>
        internal static float[] RefineMassPaint(float[] paint, IReadOnlyList<(int Vertex, float Tolerance)> painted,
            IReadOnlyList<(int A, int B, float Weight0)> rods)
        {
            var tolerance = new Dictionary<int, float>(painted.Count);
            foreach (var (vertex, step) in painted)
            {
                tolerance[vertex] = step;
            }

            var neighbours = new Dictionary<int, List<(int Other, double Difference)>>();
            foreach (var (a, b, weight) in rods)
            {
                if (a == b || !tolerance.ContainsKey(a) || !tolerance.ContainsKey(b) || weight <= 0f || weight >= 1f)
                {
                    continue;
                }

                var difference = Math.Log(weight / (1.0 - weight));
                (neighbours.TryGetValue(a, out var fromA) ? fromA : neighbours[a] = []).Add((b, difference));
                (neighbours.TryGetValue(b, out var fromB) ? fromB : neighbours[b] = []).Add((a, -difference));
            }

            var refined = (float[])paint.Clone();
            var seen = new HashSet<int>();
            foreach (var start in neighbours.Keys.Order())
            {
                if (!seen.Add(start))
                {
                    continue;
                }

                var value = new Dictionary<int, double> { [start] = paint[start] };
                var component = new List<int> { start };
                for (var i = 0; i < component.Count; i++)
                {
                    foreach (var (other, difference) in neighbours[component[i]])
                    {
                        if (seen.Add(other))
                        {
                            value[other] = value[component[i]] + difference;
                            component.Add(other);
                        }
                    }
                }

                var anchor = component.Average(vertex => (double)paint[vertex]);
                for (var sweep = 0; sweep < MassPaintRefineSweeps; sweep++)
                {
                    var moved = 0.0;
                    foreach (var vertex in component)
                    {
                        var next = neighbours[vertex].Average(edge => value[edge.Other] - edge.Difference);
                        moved = Math.Max(moved, Math.Abs(next - value[vertex]));
                        value[vertex] = next;
                    }

                    var shift = anchor - component.Average(vertex => value[vertex]);
                    foreach (var vertex in component)
                    {
                        value[vertex] += shift;
                    }

                    if (moved < 1e-12)
                    {
                        break;
                    }
                }

                if (component.TrueForAll(vertex =>
                    Math.Abs(value[vertex] - paint[vertex]) <= UniformMassPaintSteps * tolerance[vertex]))
                {
                    foreach (var vertex in component)
                    {
                        refined[vertex] = (float)value[vertex];
                    }
                }
            }

            return refined;
        }

        const int MassPaintRefineSweeps = 400;

        /// <summary>
        /// Gets the median of the <c>cloth_mass</c> readings when every reading lies within
        /// <see cref="UniformMassPaintSteps"/> of its tolerance from it, else null.
        /// </summary>
        internal static float? UniformMassPaint(IReadOnlyList<(float Value, float Tolerance)> readings)
        {
            if (readings.Count < 2)
            {
                return null;
            }

            var sorted = readings.Select(static reading => reading.Value).Order().ToList();
            var median = sorted[(sorted.Count - 1) / 2];
            foreach (var (value, tolerance) in readings)
            {
                if (MathF.Abs(value - median) > UniformMassPaintSteps * tolerance)
                {
                    return null;
                }
            }

            return median;
        }

        const float UniformMassPaintSteps = 16f;

        /// <summary>
        /// Gets the rod of each edge and diagonal of the authored faces within <paramref name="nodes"/>: the record on
        /// that pair whose maximum is the endpoints' rest distance.
        /// </summary>
        List<((int A, int B) Pair, bool Diagonal, Rod Rod)> AuthoredFaceRods(HashSet<int> nodes)
        {
            var byPair = new Dictionary<(int, int), List<Rod>>();
            foreach (var rod in Rods)
            {
                var key = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
                (byPair.TryGetValue(key, out var list) ? list : byPair[key] = []).Add(rod);
            }

            var kinds = new Dictionary<(int, int), bool>();
            foreach (var face in SourceFaces)
            {
                if (face.Length is not (3 or 4) || !Array.TrueForAll(face, nodes.Contains))
                {
                    continue;
                }

                for (var i = 0; i < face.Length; i++)
                {
                    Add(face[i], face[(i + 1) % face.Length], false);
                }

                if (face.Length == 4)
                {
                    Add(face[0], face[2], true);
                    Add(face[1], face[3], true);
                }
            }

            var found = new List<((int, int), bool, Rod)>(kinds.Count);
            foreach (var (pair, diagonal) in kinds)
            {
                if (!byPair.TryGetValue(pair, out var candidates))
                {
                    continue;
                }

                var rest = Vector3.Distance(InitPosePositions[pair.Item1], InitPosePositions[pair.Item2]);
                var authored = candidates.Count == 1
                    ? candidates[0]
                    : candidates.MinBy(r => MathF.Abs(r.MaxDist - rest));
                if (MathF.Abs(authored.MaxDist - rest) <= FaceRodRestTolerance * MathF.Max(1f, rest))
                {
                    found.Add((pair, diagonal, authored));
                }
            }

            return found;

            void Add(int x, int y, bool diagonal)
            {
                if (x != y && x >= 0 && y >= 0 && x < InitPosePositions.Length && y < InitPosePositions.Length)
                {
                    kinds[x < y ? (x, y) : (y, x)] = diagonal;
                }
            }
        }

        const float FaceRodRestTolerance = 1e-3f;

        /// <summary>
        /// Solves a per-node paint from pair sums <c>p[a] + p[b] = stated</c>, or null when they contradict each other
        /// or force a value outside <c>[0, <paramref name="upper"/>]</c>.
        /// </summary>
        /// <param name="stated">The pair sums, one per node pair.</param>
        /// <param name="fallback">The value an unpainted vertex has.</param>
        /// <param name="upper">The largest value a vertex may take.</param>
        /// <param name="chooseFree">
        /// Picks a component's free parameter from each node's sign and offset (<c>value = sign * free + offset</c>), or
        /// returns null to take the choice closest to <paramref name="fallback"/>.
        /// </param>
        static Dictionary<int, float>? SolvePairSumPaint(Dictionary<(int A, int B), float> stated,
            float fallback, float upper,
            Func<IReadOnlyDictionary<int, float>, IReadOnlyDictionary<int, float>, float?>? chooseFree = null)
        {
            var adjacency = new Dictionary<int, List<(int Other, float Sum)>>();
            foreach (var ((a, b), sum) in stated)
            {
                (adjacency.TryGetValue(a, out var na) ? na : adjacency[a] = []).Add((b, sum));
                (adjacency.TryGetValue(b, out var nb) ? nb : adjacency[b] = []).Add((a, sum));
            }

            var solved = new Dictionary<int, float>(adjacency.Count);
            var sign = new Dictionary<int, float>(adjacency.Count);
            var offset = new Dictionary<int, float>(adjacency.Count);
            var component = new List<int>();
            var stack = new Stack<int>();

            foreach (var start in adjacency.Keys)
            {
                if (solved.ContainsKey(start))
                {
                    continue;
                }

                sign.Clear();
                offset.Clear();
                component.Clear();
                sign[start] = 1f;
                offset[start] = 0f;
                component.Add(start);
                stack.Push(start);

                float? pinned = null;
                while (stack.Count > 0)
                {
                    var node = stack.Pop();
                    foreach (var (other, sum) in adjacency[node])
                    {
                        var otherSign = -sign[node];
                        var otherOffset = sum - offset[node];
                        if (sign.TryGetValue(other, out var known))
                        {
                            if (known != otherSign)
                            {
                                var forced = (otherOffset - offset[other]) / (known - otherSign);
                                if (pinned is { } already && MathF.Abs(already - forced) > PaintSolveTolerance)
                                {
                                    return null;
                                }

                                pinned ??= forced;
                            }
                            else if (MathF.Abs(offset[other] - otherOffset) > PaintSolveTolerance)
                            {
                                return null;
                            }

                            continue;
                        }

                        sign[other] = otherSign;
                        offset[other] = otherOffset;
                        component.Add(other);
                        stack.Push(other);
                    }
                }

                var free = pinned ?? chooseFree?.Invoke(sign, offset)
                    ?? component.Sum(node => sign[node] * (fallback - offset[node])) / component.Count;
                foreach (var node in component)
                {
                    var value = sign[node] * free + offset[node];
                    if (value < -PaintSolveTolerance || value > upper + PaintSolveTolerance)
                    {
                        return null;
                    }

                    solved[node] = Math.Clamp(value, 0f, upper);
                }
            }

            return solved;
        }

        const float PaintSolveTolerance = 2e-3f;

        /// <summary>
        /// Recovers the <c>cloth_antishrink</c> paint of a proxy sheet from its face rods, or null when it is uniformly
        /// <see cref="SheetAntishrinkDefault"/> or the rods contradict each other.
        /// </summary>
        public float[]? RecoverAntishrinkPaint(ProxyMesh proxy)
        {
            var nodes = new HashSet<int>(proxy.NodeIndices);
            var stated = new Dictionary<(int A, int B), float>();
            foreach (var (pair, _, rod) in AuthoredFaceRods(nodes))
            {
                if (rod.MaxDist > FaceRodRestTolerance)
                {
                    stated[pair] = 2f * (rod.MinDist / rod.MaxDist);
                }
            }

            if (stated.Count == 0 || SolvePairSumPaint(stated, SheetAntishrinkDefault, 1f) is not { } solved)
            {
                return null;
            }

            var paint = new float[proxy.NodeIndices.Length];
            var painted = 0;
            for (var v = 0; v < paint.Length; v++)
            {
                paint[v] = solved.TryGetValue(proxy.NodeIndices[v], out var value) ? value : SheetAntishrinkDefault;
                if (MathF.Abs(paint[v] - SheetAntishrinkDefault) > PaintSolveTolerance)
                {
                    painted++;
                }
            }

            return painted > 0 ? paint : null;
        }

        /// <summary>The <c>cloth_antishrink</c> of a proxy-sheet vertex that is not painted.</summary>
        public const float SheetAntishrinkDefault = 0.75f;

        /// <summary>
        /// Gets the per-node <c>cloth_shear_resistance</c> of the proxy sheets relative to the stiffest face diagonal's
        /// relaxation, or null when every diagonal states one value.
        /// </summary>
        internal (Dictionary<int, float> Paint, float BaseRelaxation)? ShearResistance
        {
            get
            {
                if (shearResistance is null)
                {
                    shearResistance = SolveShearResistance();
                    hasShearResistance = true;
                }

                return hasShearResistance ? shearResistance : null;
            }
        }

        (Dictionary<int, float> Paint, float BaseRelaxation)? shearResistance;
        bool hasShearResistance;

        (Dictionary<int, float>, float)? SolveShearResistance()
        {
            var sheetNodes = new HashSet<int>();
            for (var node = 0; node < CtrlNames.Length; node++)
            {
                if (IsProxyMeshNode(node))
                {
                    sheetNodes.Add(node);
                }
            }

            var faceRods = AuthoredFaceRods(sheetNodes);
            var diagonals = faceRods.Where(static entry => entry.Diagonal).ToList();
            var baseRelaxation = 0f;
            foreach (var (_, _, rod) in diagonals)
            {
                baseRelaxation = MathF.Max(baseRelaxation, UnstretchedRelaxation(rod));
            }

            if (baseRelaxation <= 0f)
            {
                return null;
            }

            var stated = new Dictionary<(int A, int B), float>(diagonals.Count);
            foreach (var (pair, _, rod) in diagonals)
            {
                stated[pair] = 2f * MathF.Cbrt(Math.Clamp(UnstretchedRelaxation(rod) / baseRelaxation, 0f, 1f));
            }

            var unbuilt = new HashSet<int>();
            foreach (var pair in UnbuiltFaceDiagonals(sheetNodes, faceRods))
            {
                stated[pair] = 0f;
                unbuilt.Add(pair.A);
                unbuilt.Add(pair.B);
            }

            float? ZeroAtUnbuilt(IReadOnlyDictionary<int, float> sign, IReadOnlyDictionary<int, float> offset)
            {
                foreach (var node in unbuilt)
                {
                    if (sign.TryGetValue(node, out var nodeSign))
                    {
                        return -offset[node] * nodeSign;
                    }
                }

                return null;
            }

            if (SolvePairSumPaint(stated, 1f, MaxStatedShearResistance, unbuilt.Count > 0 ? ZeroAtUnbuilt : null) is not { } solved
                || solved.Values.All(static value => MathF.Abs(value - 1f) <= PaintSolveTolerance))
            {
                return null;
            }

            return (solved, baseRelaxation);
        }

        /// <summary>
        /// The diagonals of the proxy quads that made their four edges but carry no rod at all along the diagonal. A span between two
        /// static nodes constrains nothing and is never built, so it neither counts against the face nor states anything itself.
        /// </summary>
        IEnumerable<(int A, int B)> UnbuiltFaceDiagonals(HashSet<int> nodes,
            List<((int A, int B) Pair, bool Diagonal, Rod Rod)> faceRods)
        {
            var edges = faceRods.Where(static entry => !entry.Diagonal).Select(static entry => entry.Pair).ToHashSet();
            var rodPairs = Rods.Select(static rod => rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA)).ToHashSet();
            static (int, int) Key(int x, int y) => x < y ? (x, y) : (y, x);
            bool BothStatic((int A, int B) pair) => IsStatic(pair.A) && IsStatic(pair.B);
            bool Spans((int, int) pair) => edges.Contains(pair) || BothStatic(pair);

            foreach (var face in SourceFaces)
            {
                if (face.Length != 4 || !Array.TrueForAll(face, nodes.Contains)
                    || !Spans(Key(face[0], face[1])) || !Spans(Key(face[1], face[2]))
                    || !Spans(Key(face[2], face[3])) || !Spans(Key(face[3], face[0])))
                {
                    continue;
                }

                foreach (var diagonal in new[] { Key(face[0], face[2]), Key(face[1], face[3]) })
                {
                    if (!rodPairs.Contains(diagonal) && !BothStatic(diagonal))
                    {
                        yield return diagonal;
                    }
                }
            }
        }

        /// <summary>The largest <c>cloth_shear_resistance</c> a vertex can state.</summary>
        const float MaxStatedShearResistance = 2f;

        /// <summary>
        /// Recovers the per-vertex <c>cloth_shear_resistance</c> paint of a proxy sheet, or null when the
        /// sheet's diagonals state one uniform value. See <see cref="ShearResistance"/>.
        /// </summary>
        public float[]? RecoverShearResistancePaint(ProxyMesh proxy)
        {
            if (ShearResistance is not { } shear)
            {
                return null;
            }

            var paint = new float[proxy.NodeIndices.Length];
            var painted = 0;
            for (var v = 0; v < paint.Length; v++)
            {
                paint[v] = shear.Paint.TryGetValue(proxy.NodeIndices[v], out var value) ? value : 1f;
                if (MathF.Abs(paint[v] - 1f) > PaintSolveTolerance)
                {
                    painted++;
                }
            }

            return painted > 0 ? paint : null;
        }

        /// <summary>
        /// Gets the per-node <c>cloth_stretch</c> of the proxy sheets solved from their face edges, or null where they
        /// state none.
        /// </summary>
        internal Dictionary<int, float>? StretchPaint
        {
            get
            {
                if (!hasStretchPaint)
                {
                    stretchPaint = SolveStretchPaint();
                    hasStretchPaint = true;
                }

                return stretchPaint;
            }
        }

        Dictionary<int, float>? stretchPaint;
        bool hasStretchPaint;

        Dictionary<int, float>? SolveStretchPaint()
        {
            var sheetNodes = new HashSet<int>();
            for (var node = 0; node < CtrlNames.Length; node++)
            {
                if (IsProxyMeshNode(node))
                {
                    sheetNodes.Add(node);
                }
            }

            var thread = DefaultSurfaceStretch > 0f ? MathF.Exp(-DefaultSurfaceStretch) : 1f;
            var stated = new Dictionary<(int A, int B), float>();
            var diagonals = new List<(int A, int B, float Relaxation)>();
            foreach (var (pair, diagonal, rod) in AuthoredFaceRods(sheetNodes))
            {
                if (!diagonal)
                {
                    stated[pair] = 2f * (1f - MathF.Cbrt(Math.Clamp(rod.RelaxationFactor / thread, 0f, 1f)));
                }
                else
                {
                    diagonals.Add((pair.A, pair.B, rod.RelaxationFactor));
                }
            }

            if (stated.Count == 0
                || SolvePairSumPaint(stated, 0f, MaxStatedStretch, (sign, offset) => DiagonalStretchFree(diagonals, sign, offset)) is not { } solved
                || solved.Values.All(static value => value <= PaintSolveTolerance))
            {
                return null;
            }

            return solved;
        }

        /// <summary>
        /// Reads the free parameter the face edges leave on the stretch paint off the face diagonals, or null unless
        /// diagonals of both colours state one consistent value.
        /// </summary>
        static float? DiagonalStretchFree(List<(int A, int B, float Relaxation)> diagonals,
            IReadOnlyDictionary<int, float> sign, IReadOnlyDictionary<int, float> offset)
        {
            double aa = 0, ab = 0, bb = 0, ay = 0, by = 0;
            var rows = new List<(double Open, double Colour, double Root)>();
            var colours = new HashSet<float>();
            foreach (var (a, b, relaxation) in diagonals)
            {
                if (relaxation <= 0f || !sign.TryGetValue(a, out var signA) || !sign.TryGetValue(b, out var signB) || signA != signB)
                {
                    continue;
                }

                double open = 1.0 - (0.5 * (offset[a] + offset[b]));
                double colour = -signA;
                var root = Math.Cbrt(relaxation);
                rows.Add((open, colour, root));
                colours.Add(signA);
                aa += open * open;
                ab += open * colour;
                bb += colour * colour;
                ay += open * root;
                by += colour * root;
            }

            var determinant = (aa * bb) - (ab * ab);
            if (colours.Count < 2 || Math.Abs(determinant) < 1e-12)
            {
                return null;
            }

            var factor = ((ay * bb) - (by * ab)) / determinant;
            var shifted = ((by * aa) - (ay * ab)) / determinant;
            if (factor <= 1e-6)
            {
                return null;
            }

            foreach (var (open, colour, root) in rows)
            {
                if (Math.Abs((factor * open) + (shifted * colour) - root) > PaintSolveTolerance * factor)
                {
                    return null;
                }
            }

            return (float)(shifted / factor);
        }

        /// <summary>
        /// The largest <c>cloth_stretch</c> a compiled sheet can state. The compiler clamps the cube of one minus the
        /// endpoints' MEAN, so one vertex may sit above 1 as long as its partner sits below.
        /// </summary>
        const float MaxStatedStretch = 2f;

        /// <summary>
        /// A sheet rod's relaxation with the recovered <c>cloth_stretch</c> factor taken back out, which is what its
        /// shear terms and the model's own stretch scalars left on it.
        /// </summary>
        float UnstretchedRelaxation(Rod rod)
        {
            if (StretchPaint is not { } paint)
            {
                return rod.RelaxationFactor;
            }

            var open = 1f - (0.5f * (paint.GetValueOrDefault(rod.NodeA) + paint.GetValueOrDefault(rod.NodeB)));
            var factor = Math.Clamp(open * open * open, 0f, 1f);
            return factor > 0f ? Math.Min(1f, rod.RelaxationFactor / factor) : rod.RelaxationFactor;
        }

        /// <summary>
        /// Recovers the per-vertex <c>cloth_stretch</c> paint of a proxy sheet, or null when the sheet carries none.
        /// See <see cref="StretchPaint"/>.
        /// </summary>
        public float[]? RecoverStretchPaint(ProxyMesh proxy)
        {
            if (StretchPaint is not { } byNode)
            {
                return null;
            }

            var paint = new float[proxy.NodeIndices.Length];
            var painted = 0;
            for (var v = 0; v < paint.Length; v++)
            {
                paint[v] = byNode.GetValueOrDefault(proxy.NodeIndices[v]);
                if (paint[v] > PaintSolveTolerance)
                {
                    painted++;
                }
            }

            return painted > 0 ? paint : null;
        }

        /// <summary>
        /// Recovers the authored <c>mass</c> multiplier of a cloth node, or null when it is the default 1 or cannot be read.
        /// </summary>
        public float? RecoverMassMultiplier(int node)
        {
            var multiplier = HasExplicitMasses ? ExplicitMassOf(node) : MassMultiplierOf(node);
            return multiplier is { } value && MathF.Abs(value - 1f) > MassMultiplierTolerance
                ? value
                : null;
        }

        /// <summary>
        /// Gets whether the model was compiled with <c>ClothParams explicit_masses</c>.
        /// </summary>
        public bool HasExplicitMasses => hasExplicitMasses ??= ComputeHasExplicitMasses();
        bool? hasExplicitMasses;

        bool ComputeHasExplicitMasses()
        {
            var end = FirstPositionDrivenNode > 0 && FirstPositionDrivenNode <= NodeInvMasses.Length
                ? FirstPositionDrivenNode
                : NodeInvMasses.Length;

            bool Simulated(int node) => node >= StaticNodeCount && node < end && NodeInvMasses[node] > 0f;

            float? shared = null;
            var uniform = true;
            var simulated = 0;
            for (var node = StaticNodeCount; node < end; node++)
            {
                var invMass = NodeInvMasses[node];
                if (invMass <= 0f)
                {
                    continue;
                }

                var mass = 1f / invMass;
                if (invMass < 0.05f || invMass > 20f
                    || MathF.Abs(mass - MathF.Round(mass, 2)) > 1e-3f * MathF.Max(1f, mass))
                {
                    return false;
                }

                if (shared is { } first && MathF.Abs(invMass - first) > 1e-6f * MathF.Max(invMass, first))
                {
                    uniform = false;
                }

                shared ??= invMass;
                simulated++;
            }

            var anyRod = false;
            var unequal = 0;
            var proportional = 0;
            foreach (var rod in Rods)
            {
                if (rod.NodeA == rod.NodeB)
                {
                    continue;
                }

                anyRod = true;
                if (!Simulated(rod.NodeA) || !Simulated(rod.NodeB))
                {
                    continue;
                }

                var (a, b) = (NodeInvMasses[rod.NodeA], NodeInvMasses[rod.NodeB]);
                if (MathF.Abs(a - b) <= 1e-6f * MathF.Max(a, b))
                {
                    continue;
                }

                unequal++;
                if (MathF.Abs(rod.Weight0 - a / (a + b)) <= 2e-5f)
                {
                    proportional++;
                }
            }

            return simulated > 0 && anyRod && (unequal > 0 ? proportional > 0 : uniform);
        }

        float? ExplicitMassOf(int node)
            => node >= 0 && node < NodeInvMasses.Length && NodeInvMasses[node] > 0f ? 1f / NodeInvMasses[node] : null;

        /// <summary>
        /// Recovers the authored <c>mass</c> of a chain joint from its node and ring nodes, or null when none can be read
        /// or they disagree.
        /// </summary>
        public float? RecoverJointMassMultiplier(int joint)
        {
            float? multiplier = null;
            foreach (var node in JointMassNodes(joint))
            {
                if (IsStatic(node) || (!HasExplicitMasses && NodeInvMasses[node] == 1f))
                {
                    continue;
                }

                if ((HasExplicitMasses ? ExplicitMassOf(node) : ChainMassMultiplierOf(node))
                    is not { } nodeMultiplier)
                {
                    return null;
                }

                if (multiplier is { } first && MathF.Abs(nodeMultiplier - first) > MassMultiplierTolerance * first)
                {
                    return null;
                }

                multiplier ??= nodeMultiplier;
            }

            return multiplier;
        }

        /// <summary>
        /// The <c>mass</c> a chain joint's own row has to state, or null where the chain's
        /// <paramref name="chainDefault"/> already states it and the row may omit the key.
        /// </summary>
        public float? RecoverJointMass(int joint, float chainDefault)
            => RecoverJointMassMultiplier(joint) is { } value
                && MathF.Abs(value - chainDefault) > MassMultiplierTolerance * chainDefault
                ? value
                : null;

        /// <summary>
        /// Gets the <c>mass</c> shared by more than half of the chain's readable joints, else 1.
        /// </summary>
        public float RecoverChainMassDefault(BoneChain chain)
        {
            var readings = new List<float>();
            foreach (var joint in chain.Joints)
            {
                if (RecoverJointMassMultiplier(joint.Node) is { } value)
                {
                    readings.Add(value);
                }
            }

            var common = 1f;
            var best = 0;
            foreach (var candidate in readings)
            {
                var shared = 0;
                foreach (var value in readings)
                {
                    if (MathF.Abs(value - candidate) <= MassMultiplierTolerance * candidate)
                    {
                        shared++;
                    }
                }

                if (shared > best)
                {
                    (best, common) = (shared, candidate);
                }
            }

            return best * 2 > readings.Count ? common : 1f;
        }

        /// <summary>Gets the mass multiplier of a chain joint's node.</summary>
        float? ChainMassMultiplierOf(int node)
        {
            if (node < 0 || node >= NodeInvMasses.Length)
            {
                return null;
            }

            var invMass = NodeInvMasses[node];
            if (invMass <= 0f || invMass == 1f)
            {
                return null;
            }

            var geometric = RodEndpoints.Contains(node) ? RodMassPass : GeometricMasses;
            if (node >= geometric.Length || geometric[node] <= 0f)
            {
                return null;
            }

            var ratio = 1f / invMass / geometric[node];
            return ratio > 0f ? MathF.Sqrt(ratio) : null;
        }

        float? MassMultiplierOf(int node)
        {
            if (node < 0 || node >= NodeInvMasses.Length)
            {
                return null;
            }

            var invMass = NodeInvMasses[node];
            if (invMass <= 0f || invMass == 1f)
            {
                return null;
            }

            float[] geometric;
            if (!RodEndpoints.Contains(node))
            {
                geometric = GeometricMasses;
            }
            else if (!HasProxyMeshNodes)
            {
                geometric = RodMassPass;
            }
            else
            {
                return null;
            }

            if (node >= geometric.Length || geometric[node] <= 0f)
            {
                return null;
            }

            var ratio = 1f / invMass / geometric[node];
            return ratio > 0f ? MathF.Sqrt(ratio) : null;
        }

        IEnumerable<int> JointMassNodes(int joint)
        {
            yield return joint;
            for (var node = 0; node < CtrlNames.Length; node++)
            {
                if (CtrlNames[node].StartsWith("$cc", StringComparison.Ordinal) && ParentNodeOf(node) == joint)
                {
                    yield return node;
                }
            }
        }

        float[] GeometricMasses => geometricMasses ??= GeometricNodeMasses();
        float[]? geometricMasses;

        float[] RodMassPass => rodMassPass ??= GeometricNodeMassesWithRods();
        float[]? rodMassPass;

        bool HasProxyMeshNodes => hasProxyMeshNodes ??= CtrlNames.Any(static name => name.StartsWith("$cloth_m", StringComparison.Ordinal));
        bool? hasProxyMeshNodes;

        HashSet<int> RodEndpoints => rodEndpoints ??= Rods
            .Where(static rod => rod.NodeA != rod.NodeB)
            .SelectMany(static rod => new[] { rod.NodeA, rod.NodeB })
            .ToHashSet();
        HashSet<int>? rodEndpoints;

        const float MassMultiplierTolerance = 1e-3f;

        /// <summary>
        /// Gets the mass the compiler derives from the cloth's geometry per control node: the solve elements, the rods
        /// built from the authored elements that did not stay solve elements, and the volumetric selections.
        /// </summary>
        float[] GeometricNodeMasses()
        {
            var mass = new float[InitPosePositions.Length];
            var elements = MassElements();
            AddElementNodeMasses(mass, elements);
            AddAuthoredRodNodeMasses(mass, elements);
            AddVolumetricNodeMasses(mass);
            return mass;
        }

        void AddElementNodeMasses(float[] mass, List<int[]> elements)
        {
            foreach (var element in elements)
            {
                for (var k = 1; k < 4; k++)
                {
                    for (var j = 0; j < k; j++)
                    {
                        var (a, b) = (element[j], element[k]);
                        if (a == b || a < 0 || b < 0 || a >= mass.Length || b >= mass.Length)
                        {
                            continue;
                        }

                        var term = ElementMassPerUnitLength
                            * Vector3.Distance(InitPosePositions[a], InitPosePositions[b]);
                        mass[a] += term;
                        mass[b] += term;
                    }
                }
            }
        }

        /// <summary>
        /// Gets the geometric masses of a cloth with no proxy sheet, where every shipped rod weighs except those the
        /// compiler folded across shared edges or left unbounded.
        /// </summary>
        float[] GeometricNodeMassesWithRods()
        {
            var mass = new float[InitPosePositions.Length];
            AddElementNodeMasses(mass, MassElements());
            AddVolumetricNodeMasses(mass);

            var cycles = new List<int[]>();
            foreach (var face in SourceFaces)
            {
                if (face.Length is 3 or 4)
                {
                    cycles.Add(CompiledElementOrder(face, IsStatic, RestPositionOf));
                }
            }

            var derived = PredictBendRods([.. FoldWalkSolveElements(), .. SourceElementWalk()], IsStatic);

            var rodsOnPair = new Dictionary<(int, int), int>();
            foreach (var rod in Rods)
            {
                if (rod.NodeA != rod.NodeB)
                {
                    var pair = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
                    rodsOnPair[pair] = rodsOnPair.GetValueOrDefault(pair) + 1;
                }
            }

            foreach (var cycle in cycles)
            {
                for (var j = 0; j < cycle.Length; j++)
                {
                    var (p, q) = (cycle[j], cycle[(j + 1) % cycle.Length]);
                    var edge = p < q ? (p, q) : (q, p);
                    if (rodsOnPair.GetValueOrDefault(edge) == 1)
                    {
                        derived.Remove(edge);
                    }
                }
            }

            foreach (var rod in Rods)
            {
                var (a, b) = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
                if (a == b || a < 0 || b >= mass.Length
                    || rod.MaxDist >= UnboundedRodDistance || (FoldedAfterMass(rod) && derived.Remove((a, b))))
                {
                    continue;
                }

                var term = RodMassPerUnitLength * Vector3.Distance(InitPosePositions[a], InitPosePositions[b]);
                mass[a] += term;
                mass[b] += term;
            }

            return mass;

            Vector3 RestPositionOf(int node)
                => node >= 0 && node < InitPosePositions.Length ? InitPosePositions[node] : Vector3.Zero;
        }

        /// <summary>
        /// Gets the solve elements in the corner order the compiler's fold walk meets them.
        /// </summary>
        List<int[]> FoldWalkSolveElements()
        {
            var rings = new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal);
            for (var node = 0; node < CtrlNames.Length; node++)
            {
                var name = CtrlNames[node];
                var index = RingSuffixIndex(name);
                if (index >= 0 && name.StartsWith("$cc", StringComparison.Ordinal))
                {
                    var ring = name[..name.LastIndexOf('_')];
                    (rings.TryGetValue(ring, out var members) ? members : rings[ring] = [])[index] = node;
                }
            }

            Dictionary<int, int>? TwoWideRingOf(int a, int b)
            {
                var name = CtrlNames[a];
                return RingSuffixIndex(name) >= 0 && rings.TryGetValue(name[..name.LastIndexOf('_')], out var members)
                    && members.Count == 2 && members.ContainsKey(0) && members.ContainsKey(1)
                    && members.ContainsValue(a) && members.ContainsValue(b) && a != b
                    ? members
                    : null;
            }

            var elements = MassElements();
            for (var i = 0; i < elements.Count; i++)
            {
                var quad = elements[i];
                if (quad.Length == 4 && quad.Distinct().Count() == 4 && quad.All(node => node >= 0 && node < CtrlNames.Length)
                    && TwoWideRingOf(quad[0], quad[1]) is not null && TwoWideRingOf(quad[2], quad[3]) is { } far)
                {
                    elements[i] = [quad[0], quad[1], far[1], far[0]];
                }
            }

            return elements;
        }

        /// <summary>
        /// The faces the importer built into rods, in the order and corner order the compiler walks them for its
        /// fold rods. <c>m_SourceElems</c> packs each corner-count group from its end, so every group is read backwards.
        /// </summary>
        IEnumerable<int[]> SourceElementWalk()
            => SourceFaces.Where(static face => face.Length == 3).Reverse()
                .Concat(SourceFaces.Where(static face => face.Length == 4).Reverse());

        /// <summary>
        /// Credits both ends of every distinct corner pair of the authored elements that are not solve elements with
        /// <see cref="RodMassPerUnitLength"/> per unit of rest length.
        /// </summary>
        void AddAuthoredRodNodeMasses(float[] mass, List<int[]> elements)
        {
            var solved = new HashSet<(int, int, int, int)>(elements.Count);
            foreach (var element in elements)
            {
                solved.Add(CornerKey(element));
            }

            var sheetNodes = new HashSet<int>();
            for (var node = 0; node < CtrlNames.Length; node++)
            {
                if (IsProxyMeshNode(node))
                {
                    sheetNodes.Add(node);
                }
            }

            var unbuilt = UnbuiltFaceDiagonals(sheetNodes, AuthoredFaceRods(sheetNodes)).ToHashSet();
            var rods = new Dictionary<(int A, int B), float>();
            foreach (var face in SourceFaces)
            {
                if (face.Length < 3 || solved.Contains(CornerKey(face)))
                {
                    continue;
                }

                for (var i = 0; i < face.Length; i++)
                {
                    for (var j = i + 1; j < face.Length; j++)
                    {
                        var (a, b) = face[i] < face[j] ? (face[i], face[j]) : (face[j], face[i]);
                        if (a == b || a < 0 || b >= mass.Length || unbuilt.Contains((a, b)))
                        {
                            continue;
                        }

                        rods[(a, b)] = Vector3.Distance(InitPosePositions[a], InitPosePositions[b]);
                    }
                }
            }

            foreach (var ((a, b), length) in rods)
            {
                var term = RodMassPerUnitLength * length;
                mass[a] += term;
                mass[b] += term;
            }
        }

        static (int, int, int, int) CornerKey(int[] corners)
        {
            var sorted = corners.Distinct().Order().ToArray();
            return (sorted.Length > 0 ? sorted[0] : -1, sorted.Length > 1 ? sorted[1] : -1,
                sorted.Length > 2 ? sorted[2] : -1, sorted.Length > 3 ? sorted[3] : -1);
        }

        /// <summary>
        /// The elements the mass pass ran over, each as four corners with a triangle repeating its last
        /// one, rebuilt from the compiled surface by merging every triangle pair the compiler split an
        /// over-bent quad into back into that quad.
        /// </summary>
        List<int[]> MassElements()
        {
            var elements = new List<int[]>(Quads.Length + Tris.Length);
            elements.AddRange(Quads);

            var (splitQuads, splitHalves) = MergeSplitQuads();
            foreach (var tri in Tris)
            {
                var key = SortedTriKey(tri);
                if (splitHalves.Contains(key))
                {
                    continue;
                }

                elements.Add(splitQuads.TryGetValue(key, out var quad) ? quad : [tri[0], tri[1], tri[2], tri[2]]);
            }

            return elements;
        }

        /// <summary>
        /// Credits every node a volumetric selection covers with 12 per unit of the summed bounding-box
        /// extent of that selection's own nodes, scaled by how strongly the node belongs to it. A
        /// selection the solver does not solve volumetrically weighs nothing.
        /// </summary>
        void AddVolumetricNodeMasses(float[] mass)
        {
            foreach (var map in VertexMaps)
            {
                if (map.VolumetricSolveStrength < MinVolumetricSolveStrength)
                {
                    continue;
                }

                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);
                var covered = false;
                for (var i = 0; i < map.Weights.Length; i++)
                {
                    var node = map.VertexBase + i;
                    if (map.Weights[i] <= 0f || node < 0 || node >= mass.Length)
                    {
                        continue;
                    }

                    min = Vector3.Min(min, InitPosePositions[node]);
                    max = Vector3.Max(max, InitPosePositions[node]);
                    covered = true;
                }

                if (!covered)
                {
                    continue;
                }

                var extent = max - min;
                var term = VolumetricMassPerUnitExtent * (extent.X + extent.Y + extent.Z);
                for (var i = 0; i < map.Weights.Length; i++)
                {
                    var node = map.VertexBase + i;
                    if (map.Weights[i] > 0f && node >= 0 && node < mass.Length)
                    {
                        mass[node] += map.Weights[i] * term;
                    }
                }
            }
        }

        /// <summary>
        /// Recovers the <c>cloth_stray_radius</c> paint of a proxy sheet, or null when none of its vertices has one.
        /// Vertices owned by an independent chain are skipped.
        /// </summary>
        public float[]? RecoverStrayRadiusPaint(ProxyMesh proxy)
        {
            if (AnimStrayRadii.Count == 0)
            {
                return null;
            }

            var chainNodes = IndependentChainCoveredNodes();

            var paint = new float[proxy.NodeIndices.Length];
            var painted = 0;
            for (var v = 0; v < paint.Length; v++)
            {
                var node = proxy.NodeIndices[v];
                if (chainNodes.Contains(node))
                {
                    continue;
                }

                if (AnimStrayRadii.TryGetValue(node, out var stray))
                {
                    paint[v] = stray.MaxDistance;
                    painted++;
                }
            }

            return painted > 0 ? paint : null;
        }

        /// <summary>
        /// The largest <c>cloth_stray_radius_stretchiness</c> a proxy vertex can carry and keep its
        /// stray radius: at or above it the compiler cancels the radius instead of relaxing it.
        /// </summary>
        const float MaxProxyStrayStretchiness = 0.9999998f;

        /// <summary>
        /// Recovers the <c>cloth_stray_radius_stretchiness</c> paint of a proxy sheet, or null when none of its vertices
        /// has one. Vertices owned by an independent chain are skipped.
        /// </summary>
        public float[]? RecoverStrayStretchinessPaint(ProxyMesh proxy)
        {
            if (AnimStrayRadii.Count == 0)
            {
                return null;
            }

            var chainNodes = IndependentChainCoveredNodes();

            var paint = new float[proxy.NodeIndices.Length];
            var painted = 0;
            for (var v = 0; v < paint.Length; v++)
            {
                var node = proxy.NodeIndices[v];
                if (chainNodes.Contains(node) || !AnimStrayRadii.ContainsKey(node))
                {
                    continue;
                }

                var slack = 1f - GetStrayRelaxationFactor(node);
                var squared = slack * slack;
                var stretchiness = squared * squared;
                stretchiness *= stretchiness;
                stretchiness *= stretchiness;
                if (stretchiness > 0f)
                {
                    paint[v] = Math.Min(stretchiness, MaxProxyStrayStretchiness);
                    painted++;
                }
            }

            return painted > 0 ? paint : null;
        }

        /// <summary>
        /// Gets the joints of the <see cref="IndependentBoneChains"/> and the <c>$cc</c> nodes parented to them.
        /// </summary>
        HashSet<int> IndependentChainCoveredNodes()
        {
            var chainBoneNodes = IndependentBoneChains().SelectMany(static c => c.Joints).Select(static j => j.Node).ToHashSet();
            if (chainBoneNodes.Count == 0)
            {
                return chainBoneNodes;
            }

            var covered = new HashSet<int>(chainBoneNodes);
            for (var node = 0; node < CtrlNames.Length; node++)
            {
                if (CtrlNames[node].StartsWith("$cc", StringComparison.Ordinal) && chainBoneNodes.Contains(ParentNodeOf(node)))
                {
                    covered.Add(node);
                }
            }

            return covered;
        }

        /// <summary>
        /// Gets the parent control node of <paramref name="node"/>, or -1 for a root: its skeleton parent,
        /// or on an original that ships no <c>m_SkelParents</c> the parent its ctrl offset names.
        /// </summary>
        int ParentNodeOf(int node)
        {
            var parent = node >= 0 && node < SkelParents.Length ? SkelParents[node] : -1;
            if (parent >= 0 || HasCompiledSkelParents)
            {
                return parent;
            }

            if (offsetParentByNode is null)
            {
                offsetParentByNode = new Dictionary<int, int>(CtrlOffsets.Length);
                foreach (var off in CtrlOffsets)
                {
                    offsetParentByNode[off.CtrlChild] = off.CtrlParent;
                }
            }

            return offsetParentByNode.GetValueOrDefault(node, -1);
        }

        Dictionary<int, int>? offsetParentByNode;

        const float ElementMassPerUnitLength = 4f;

        const float RodMassPerUnitLength = 8f;

        const float VolumetricMassPerUnitExtent = 12f;

        const float MinVolumetricSolveStrength = 1.1920929e-7f;

        const float MinRecoverableMassPaintTerm = 0.05f;
        const float MaxRecoverableMassPaintTerm = 1e6f;

        /// <summary>Gets the friction painted on <paramref name="node"/>, or 0 when it has none.</summary>
        public float GetNodeFriction(int node) => DynamicNodeValue(DynNodeFriction, node);

        /// <summary>Gets <c>m_flInternalPressure</c>.</summary>
        public float InternalPressure => Data.GetFloatProperty("m_flInternalPressure");

        /// <summary>Gets <c>m_flWindage</c>.</summary>
        public float Windage => Data.GetFloatProperty("m_flWindage");

        /// <summary>Gets <c>m_flWindDrag</c>.</summary>
        public float WindDrag => Data.GetFloatProperty("m_flWindDrag");

        /// <summary>Gets <c>m_flLocalForce</c>.</summary>
        public float LocalForce => Data.GetFloatProperty("m_flLocalForce");

        /// <summary>Gets <c>m_flLocalRotation</c>.</summary>
        public float LocalRotation => Data.GetFloatProperty("m_flLocalRotation");

        /// <summary>Gets <c>m_flAddWorldCollisionRadius</c>.</summary>
        public float AddWorldCollisionRadius => Data.GetFloatProperty("m_flAddWorldCollisionRadius");

        /// <summary>Gets <c>m_flDefaultGravityScale</c>, 1 when absent.</summary>
        public float DefaultGravityScale => Data.GetFloatProperty("m_flDefaultGravityScale", 1.0f);

        /// <summary>Gets <c>m_flDefaultVelAirDrag</c>.</summary>
        public float DefaultVelAirDrag => Data.GetFloatProperty("m_flDefaultVelAirDrag");

        /// <summary>Gets <c>m_flDefaultExpAirDrag</c>.</summary>
        public float DefaultExpAirDrag => Data.GetFloatProperty("m_flDefaultExpAirDrag");

        /// <summary>Gets <c>m_flDefaultThreadStretch</c>.</summary>
        public float DefaultThreadStretch => Data.GetFloatProperty("m_flDefaultThreadStretch");

        /// <summary>Gets <c>m_flDefaultSurfaceStretch</c>.</summary>
        public float DefaultSurfaceStretch => Data.GetFloatProperty("m_flDefaultSurfaceStretch");

        /// <summary>Gets <c>m_flLocalDrag1</c>.</summary>
        public float LocalDrag1 => Data.GetFloatProperty("m_flLocalDrag1");

        /// <summary>Gets <c>m_nExtraIterations</c>.</summary>
        public int ExtraIterations => Data.GetInt32Property("m_nExtraIterations");

        /// <summary>Gets <c>m_nExtraGoalIterations</c>.</summary>
        public int ExtraGoalIterations => Data.GetInt32Property("m_nExtraGoalIterations");

        /// <summary>Gets <c>m_nExtraPressureIterations</c>.</summary>
        public int ExtraPressureIterations => Data.GetInt32Property("m_nExtraPressureIterations");

        /// <summary>Gets <c>m_flRodVelocitySmoothRate</c>.</summary>
        public float VelocitySmoothRate => Data.GetFloatProperty("m_flRodVelocitySmoothRate");

        /// <summary>Gets <c>m_nRodVelocitySmoothIterations</c>.</summary>
        public int VelocitySmoothIterations => Data.GetInt32Property("m_nRodVelocitySmoothIterations");

        /// <summary>Gets <c>m_nDynamicNodeFlags</c>.</summary>
        public uint DynamicNodeFlags => Data.GetUInt32Property("m_nDynamicNodeFlags");

        /// <summary>Gets <c>m_nStaticNodeFlags</c>.</summary>
        public uint StaticNodeFlags => Data.GetUInt32Property("m_nStaticNodeFlags");

        /// <summary>Gets <c>m_nRotLockStaticNodes</c>, the number of leading static nodes whose rotation is locked.</summary>
        public int RotationLockedStaticNodeCount => Data.GetInt32Property("m_nRotLockStaticNodes");

        /// <summary>Gets <c>m_flMotionSmoothCDT</c>.</summary>
        public float MotionSmoothCdt => Data.GetFloatProperty("m_flMotionSmoothCDT");

        /// <summary>Gets <c>m_flDefaultTimeDilation</c>.</summary>
        public float DefaultTimeDilation => Data.GetFloatProperty("m_flDefaultTimeDilation");

        /// <summary>Gets <c>m_flDefaultVolumetricSolveAmount</c>.</summary>
        public float DefaultVolumetricSolveAmount => Data.GetFloatProperty("m_flDefaultVolumetricSolveAmount");

        /// <summary>Gets <c>m_flDefaultVelQuadAirDrag</c>.</summary>
        public float DefaultVelQuadAirDrag => Data.GetFloatProperty("m_flDefaultVelQuadAirDrag");

        /// <summary>Gets <c>m_flDefaultExpQuadAirDrag</c>.</summary>
        public float DefaultExpQuadAirDrag => Data.GetFloatProperty("m_flDefaultExpQuadAirDrag");

        /// <summary>Gets <c>m_flDefaultVelRodAirDrag</c>.</summary>
        public float DefaultVelRodAirDrag => Data.GetFloatProperty("m_flDefaultVelRodAirDrag");

        /// <summary>Gets <c>m_flDefaultExpRodAirDrag</c>.</summary>
        public float DefaultExpRodAirDrag => Data.GetFloatProperty("m_flDefaultExpRodAirDrag");

        /// <summary>Gets <c>m_flQuadVelocitySmoothRate</c>.</summary>
        public float QuadVelocitySmoothRate => Data.GetFloatProperty("m_flQuadVelocitySmoothRate");

        /// <summary>Gets <c>m_nQuadVelocitySmoothIterations</c>.</summary>
        public int QuadVelocitySmoothIterations => Data.GetInt32Property("m_nQuadVelocitySmoothIterations");

        /// <summary>Gets whether the cloth carries per-node local force or rotation values.</summary>
        public bool HasPerNodeLocalForce
            => Data.GetFloatArray("m_LocalForce").Length > 0 || Data.GetFloatArray("m_LocalRotation").Length > 0;

        /// <summary>Gets the per-node local force multipliers, empty when the cloth uses the global one.</summary>
        public float[] LocalForceValues => Data.GetFloatArray("m_LocalForce");

        /// <summary>Gets the per-node local rotation multipliers, empty when the cloth uses the global one.</summary>
        public float[] LocalRotationValues => Data.GetFloatArray("m_LocalRotation");

        /// <summary>
        /// Gets whether the cloth carries axial bend edges, which is what the source's rigid-edge-hinge
        /// switch produces: one entry per interior edge of the sheet.
        /// </summary>
        public bool HasAxialEdges => Data.GetArray("m_AxialEdges") is { Count: > 0 };

        /// <summary>
        /// Gets the three-node bend constraints (<c>m_KelagerBends</c>) the compiler builds for a chain
        /// joint authored with a stiff hinge.
        /// </summary>
        public IReadOnlyList<KelagerBend> KelagerBends { get; } = [];

        /// <summary>A three-node bend constraint (from <c>m_KelagerBends</c>).</summary>
        /// <param name="MidNode">The bent node, whose joint carries the authored stiff hinge.</param>
        /// <param name="End0">The first node the bend measures against.</param>
        /// <param name="End1">The second node the bend measures against.</param>
        /// <param name="MidWeight">Solver share of <paramref name="MidNode"/>.</param>
        /// <param name="End0Weight">Solver share of <paramref name="End0"/>.</param>
        /// <param name="End1Weight">Solver share of <paramref name="End1"/>.</param>
        /// <param name="Height">Distance from the bent node to the triple's centroid the bend allows.</param>
        public readonly record struct KelagerBend(int MidNode, int End0, int End1,
            float MidWeight, float End0Weight, float End1Weight, float Height);

        /// <summary>
        /// Gets whether <paramref name="bend"/> is a ring bend laid over a chain: its hub's joint lies between the joints
        /// owning its first and second ends.
        /// </summary>
        public bool IsChainRingBend(KelagerBend bend)
        {
            var hub = BendOwner(bend.MidNode);
            var first = BendOwner(bend.End0);
            var second = BendOwner(bend.End1);
            return hub >= 0 && first >= 0 && second >= 0
                && hub < SkelParents.Length && SkelParents[hub] == first
                && second < SkelParents.Length && SkelParents[second] == hub;
        }

        /// <summary>
        /// Gets whether any <see cref="KelagerBends"/> record is a chain ring bend (<see cref="IsChainRingBend"/>), which only
        /// <c>rigid_edge_hinges</c> builds.
        /// </summary>
        public bool HasChainRingBends => KelagerBends.Any(IsChainRingBend);

        int BendOwner(int node)
            => node < 0 || node >= CtrlNames.Length ? -1
                : !IsProxyNodeName(CtrlNames[node]) ? node
                : node < SkelParents.Length ? SkelParents[node] : -1;

        /// <summary>
        /// Recovers the <c>stiff_hinge</c> stiffness, <c>stiff_hinge_angle</c> in degrees and <c>motion_bias</c> of the
        /// joint at <paramref name="jointNode"/> from the bend whose first end is the joint or one of its proxies, or null
        /// when it has none.
        /// </summary>
        /// <param name="jointNode">The node of the joint whose stiff hinge to recover.</param>
        /// <param name="rank">Which of the joint's bends to read, in declaration order.</param>
        public (float Stiffness, float Angle, float MotionBias)? GetStiffHinge(int jointNode, int rank = 0)
        {
            var seen = 0;

            foreach (var bend in KelagerBends)
            {
                var owner = bend.End0 >= 0 && bend.End0 < CtrlNames.Length && IsProxyNodeName(CtrlNames[bend.End0])
                    && bend.End0 < SkelParents.Length
                        ? SkelParents[bend.End0]
                        : -1;
                if ((bend.End0 != jointNode && owner != jointNode) || IsChainRingBend(bend))
                {
                    continue;
                }

                var midMass = InverseMassOf(bend.MidNode);
                if ((4f * midMass) + InverseMassOf(bend.End0) + InverseMassOf(bend.End1) <= 0f)
                {
                    continue;
                }

                var stiffness = (bend.End0Weight + bend.End1Weight - (2f * bend.MidWeight)) / 3f;
                if (stiffness <= 0f)
                {
                    continue;
                }

                var fullBias = midMass > 0f && MathF.Abs(bend.MidWeight) < FullMotionBiasEpsilon
                    && MathF.Abs(bend.End0Weight) > FullMotionBiasEpsilon;

                if (seen++ < rank)
                {
                    continue;
                }

                return (Math.Clamp(stiffness, 0f, 1f), BendAngle(bend), fullBias ? 1f : 0f);
            }

            return null;
        }

        const float FullMotionBiasEpsilon = 1e-6f;

        const float KelagerHeightFloor = 0.001f;

        const float MotionBiasTolerance = 1e-3f;

        /// <summary>
        /// Recovers the <c>motion_bias</c> of a chain joint from the weights of the rods between it and its parent, or
        /// null where they carry no reading.
        /// </summary>
        public float? GetMotionBias(BoneChainJoint joint)
        {
            if (joint.ParentNode < 0)
            {
                return null;
            }

            var parentMass = InverseMassOf(joint.ParentNode);
            var jointMass = InverseMassOf(joint.Node);
            if (parentMass <= 0f || jointMass <= 0f)
            {
                return null;
            }

            if (!HasExplicitMasses)
            {
                parentMass = 1f / (RecoverJointMassMultiplier(joint.ParentNode) ?? 1f);
                jointMass = 1f / (RecoverJointMassMultiplier(joint.Node) ?? 1f);
            }

            float? bias = null;
            var spanRods = Rods.Select(static rod => (rod.NodeA, rod.NodeB, rod.Weight0))
                .Concat(AnimRods.Select(static rod => (rod.NodeA, rod.NodeB, rod.Weight0)));
            foreach (var (nodeA, nodeB, weight0) in spanRods)
            {
                var weight = nodeA == joint.ParentNode && nodeB == joint.Node ? weight0
                    : nodeB == joint.ParentNode && nodeA == joint.Node ? 1f - weight0
                    : float.NaN;
                if (float.IsNaN(weight))
                {
                    continue;
                }

                var reading = weight <= 0f ? 1f : weight >= 1f ? -1f : Bias(weight);
                if (bias is { } seen && MathF.Abs(seen - reading) > MotionBiasTolerance)
                {
                    return null;
                }

                bias ??= reading;
            }

            return bias is { } value && MathF.Abs(value) > MotionBiasTolerance ? value : null;

            float Bias(float weight)
            {
                var ratio = weight * jointMass / ((1f - weight) * parentMass);
                return ratio <= 1f ? 1f - ratio : -(1f - (1f / ratio));
            }
        }

        float InverseMassOf(int node)
            => node >= 0 && node < NodeInvMasses.Length ? NodeInvMasses[node] : 0f;

        float BendAngle(KelagerBend bend)
        {
            if (bend.MidNode >= InitPosePositions.Length || bend.End0 >= InitPosePositions.Length
                || bend.End1 >= InitPosePositions.Length || bend.MidNode < 0 || bend.End0 < 0 || bend.End1 < 0)
            {
                return 0f;
            }

            var toEnd0 = InitPosePositions[bend.MidNode] - InitPosePositions[bend.End0];
            var toEnd1 = InitPosePositions[bend.MidNode] - InitPosePositions[bend.End1];
            var restHeight = (toEnd0 + toEnd1).Length() / 3f;
            var l0 = toEnd0.Length();
            var l1 = toEnd1.Length();
            if (bend.Height <= MathF.Max(restHeight * 1.0001f, KelagerHeightFloor) || l0 <= 0f || l1 <= 0f)
            {
                return 0f;
            }

            var cosine = ((l0 * l0) + (l1 * l1) - (9f * bend.Height * bend.Height)) / (2f * l0 * l1);
            return float.RadiansToDegrees(MathF.Acos(Math.Clamp(cosine, -1f, 1f)));
        }

        /// <summary>
        /// Gets the <c>add_curvature</c> of a model compiled with <c>rigid_edge_hinges</c>, read off the heights of its
        /// sheet-hub <see cref="KelagerBends"/>; <see cref="SaturatedCurvature"/> when they do not agree.
        /// </summary>
        public float RigidHingeCurvature
        {
            get
            {
                if (KelagerBends.Count == 0)
                {
                    return 0f;
                }

                var lowest = float.MaxValue;
                var highest = 0f;
                foreach (var bend in KelagerBends)
                {
                    if (bend.MidNode < 0 || bend.End0 < 0 || bend.End1 < 0
                        || bend.MidNode >= InitPosePositions.Length
                        || bend.End0 >= InitPosePositions.Length || bend.End1 >= InitPosePositions.Length
                        || bend.MidNode >= CtrlNames.Length || !IsProxyNodeName(CtrlNames[bend.MidNode]))
                    {
                        continue;
                    }

                    var toEnd0 = InitPosePositions[bend.End0] - InitPosePositions[bend.MidNode];
                    var toEnd1 = InitPosePositions[bend.End1] - InitPosePositions[bend.MidNode];
                    var l0 = toEnd0.Length();
                    var l1 = toEnd1.Length();
                    if (l0 <= 0f || l1 <= 0f
                        || bend.Height <= (toEnd0 + toEnd1).Length() / 3f * 1.0001f)
                    {
                        continue;
                    }

                    var cosine = ((9f * bend.Height * bend.Height) - (l0 * l0) - (l1 * l1)) / (2f * l0 * l1);
                    var reading = MathF.Acos(Math.Clamp(cosine, -1f, 1f)) / MathF.PI;
                    lowest = MathF.Min(lowest, reading);
                    highest = MathF.Max(highest, reading);
                }

                if (lowest is float.MaxValue
                    || highest - lowest > ChainRingCurvatureAgreement * MathF.Max(highest, ChainRingCurvatureAgreement)
                    || highest >= 1f - ChainRingCurvatureAgreement)
                {
                    return SaturatedCurvature;
                }

                return Math.Clamp(highest, 0f, 1f);
            }
        }

        internal const float SaturatedCurvature = 1f;

        /// <summary>
        /// Gets the per-hub <c>cloth_bend_stiffness</c> of a rigid-hinged model whose hubs fold by different angles, or
        /// null when <see cref="RigidHingeCurvature"/> accounts for every hub.
        /// </summary>
        public Dictionary<int, float>? RigidHingeBendPaint
        {
            get
            {
                if (KelagerBends.Count == 0 || RigidHingeCurvature != SaturatedCurvature)
                {
                    return null;
                }

                var lowest = new Dictionary<int, float>();
                var highest = new Dictionary<int, float>();
                var shut = new HashSet<int>();
                foreach (var bend in KelagerBends)
                {
                    if (bend.MidNode < 0 || bend.End0 < 0 || bend.End1 < 0
                        || bend.MidNode >= InitPosePositions.Length
                        || bend.End0 >= InitPosePositions.Length || bend.End1 >= InitPosePositions.Length
                        || bend.MidNode >= CtrlNames.Length || !IsProxyNodeName(CtrlNames[bend.MidNode]))
                    {
                        continue;
                    }

                    var toEnd0 = InitPosePositions[bend.End0] - InitPosePositions[bend.MidNode];
                    var toEnd1 = InitPosePositions[bend.End1] - InitPosePositions[bend.MidNode];
                    var l0 = toEnd0.Length();
                    var l1 = toEnd1.Length();
                    if (l0 <= 0f || l1 <= 0f)
                    {
                        continue;
                    }

                    if (bend.Height <= (toEnd0 + toEnd1).Length() / 3f * 1.0001f)
                    {
                        shut.Add(bend.MidNode);
                        continue;
                    }

                    var cosine = ((9f * bend.Height * bend.Height) - (l0 * l0) - (l1 * l1)) / (2f * l0 * l1);
                    var reading = MathF.Acos(Math.Clamp(cosine, -1f, 1f)) / MathF.PI;
                    lowest[bend.MidNode] = MathF.Min(lowest.GetValueOrDefault(bend.MidNode, float.MaxValue), reading);
                    highest[bend.MidNode] = MathF.Max(highest.GetValueOrDefault(bend.MidNode), reading);
                }

                var paint = new Dictionary<int, float>();
                foreach (var (hub, high) in highest)
                {
                    if (high - lowest[hub] > ChainRingCurvatureAgreement * MathF.Max(high, ChainRingCurvatureAgreement))
                    {
                        return null;
                    }

                    paint[hub] = high >= 1f - ChainRingCurvatureAgreement ? 1f : Math.Clamp(high, 0f, 1f);
                }

                foreach (var hub in shut)
                {
                    paint.TryAdd(hub, 1f);
                }

                return paint.Values.Any(static value => value > ChainRingCurvatureAgreement) ? paint : null;
            }
        }

        /// <summary>
        /// Recovers the per-vertex <c>cloth_bend_stiffness</c> paint of a rigid-hinged proxy sheet, or null when the
        /// sheet's hubs state none. See <see cref="RigidHingeBendPaint"/>.
        /// </summary>
        public float[]? RecoverRigidHingeBendPaint(ProxyMesh proxy)
        {
            if (RigidHingeBendPaint is not { } byNode)
            {
                return null;
            }

            var paint = new float[proxy.NodeIndices.Length];
            var painted = 0;
            for (var v = 0; v < paint.Length; v++)
            {
                paint[v] = byNode.GetValueOrDefault(proxy.NodeIndices[v]);
                if (paint[v] > 0f)
                {
                    painted++;
                }
            }

            return painted > 0 ? paint : null;
        }

        const string HingeAnchorPrefix = "$ha_";

        /// <summary>
        /// The hinge constraint a chain joint was authored with. <see cref="Vector"/> spans the joint to
        /// one side of its proxy ring, so its LENGTH is the ring's half-width and overrides the joint's
        /// own extrude radius; the limits are in degrees.
        /// </summary>
        /// <param name="Vector">World-space hinge axis, its length the ring half-width.</param>
        /// <param name="LimitCw">Clockwise angular limit.</param>
        /// <param name="LimitCcw">Counter-clockwise angular limit.</param>
        public readonly record struct ChainHinge(Vector3 Vector, float LimitCw, float LimitCcw);

        /// <summary>Gets the hinge authored on the joint, or null when it carries none.</summary>
        public ChainHinge? GetChainHinge(string boneName, int jointNode)
        {
            var ring = ProxyRingOf(jointNode);
            if (ring.Count < 2 || ring[0] >= InitPosePositions.Length || ring[1] >= InitPosePositions.Length)
            {
                return null;
            }

            var limit = HingeLimitOverRing(ring);
            if (limit is null && Array.IndexOf(CtrlNames, HingeAnchorPrefix + boneName) < 0)
            {
                return null;
            }

            var axis = (InitPosePositions[ring[1]] - InitPosePositions[ring[0]]) * 0.5f;
            if (axis.LengthSquared() <= 0f)
            {
                return null;
            }

            axis = BreakHingeFanQuadTie(ring, BreakEndEffectorQuadTie(ring, axis), Quaternion.Identity);

            var (cw, ccw) = limit is { } hinge ? HingeLimitsOf(hinge) : (0f, 0f);
            return new ChainHinge(axis, cw, ccw);
        }

        KVObject? HingeLimitOverRing(List<int> ring)
        {
            foreach (var hinge in Data.GetArray("m_HingeLimits") ?? [])
            {
                var nodes = hinge.GetIntegerArray("nNode");
                if (nodes.Length >= 2 && (int)nodes[0] == ring[0] && (int)nodes[1] == ring[1])
                {
                    return hinge;
                }
            }

            return null;
        }

        (float Cw, float Ccw) HingeLimitsOf(KVObject hinge)
        {
            var extents = hinge.GetFloatProperty("flAngleExtents");
            var span = float.RadiansToDegrees(extents) * 2f;
            var rest = HingeRestAngle(hinge);
            if (rest is null)
            {
                return (0f, span);
            }

            var solved = float.RadiansToDegrees(WrapAngle(hinge.GetFloatProperty("flAngleCenter") + extents - rest.Value));
            var cw = Math.Clamp(solved, 0f, span);

            return MathF.Abs(cw - solved) < 0.01f ? (cw, span - cw) : (0f, span);
        }

        float? HingeRestAngle(KVObject hinge)
        {
            var nodes = hinge.GetIntegerArray("nNode");
            if (nodes.Length < 6 || nodes.Any(node => node < 0 || node >= InitPosePositions.Length))
            {
                return null;
            }

            Vector3 Blend(int a, int b, float weight)
                => Vector3.Lerp(InitPosePositions[a], InitPosePositions[b], weight);

            var origin = InitPosePositions[(int)nodes[0]];
            var axis = Vector3.Normalize(InitPosePositions[(int)nodes[1]] - origin);
            if (!float.IsFinite(axis.X))
            {
                return null;
            }

            Vector3 Perpendicular(Vector3 point)
            {
                var arm = point - origin;
                return Vector3.Normalize(arm - (Vector3.Dot(arm, axis) * axis));
            }

            var reference = Perpendicular(Blend((int)nodes[2], (int)nodes[4], hinge.GetFloatProperty("flWeight4")));
            var arm = Perpendicular(Blend((int)nodes[3], (int)nodes[5], hinge.GetFloatProperty("flWeight5")));
            if (!float.IsFinite(reference.X) || !float.IsFinite(arm.X))
            {
                return null;
            }

            var angle = MathF.Atan2(Vector3.Dot(arm, reference), Vector3.Dot(Vector3.Cross(reference, axis), arm));
            return WrapAngle(angle - (MathF.PI / 2f));
        }

        static float WrapAngle(float angle)
        {
            var wrapped = angle % MathF.Tau;
            if (wrapped > MathF.PI)
            {
                wrapped -= MathF.Tau;
            }
            else if (wrapped <= -MathF.PI)
            {
                wrapped += MathF.Tau;
            }

            return wrapped;
        }

        Vector3 BreakEndEffectorQuadTie(List<int> ring, Vector3 axis)
        {
            if (ring.Count < 4 || ring[2] >= InitPosePositions.Length || ring[3] >= InitPosePositions.Length)
            {
                return axis;
            }

            var longest = 0f;
            var shortest = float.MaxValue;
            for (var near = 0; near < 2; near++)
            {
                for (var far = 2; far < 4; far++)
                {
                    var span = Vector3.Distance(InitPosePositions[ring[near]], InitPosePositions[ring[far]]);
                    longest = MathF.Max(longest, span);
                    shortest = MathF.Min(shortest, span);
                }
            }

            if (longest <= 0f || longest - shortest > longest * 1e-5f)
            {
                return axis;
            }

            var tip = InitPosePositions[ring[2]] - InitPosePositions[ring[3]];
            if (tip.LengthSquared() <= 0f)
            {
                return axis;
            }

            return axis + (Vector3.Normalize(tip) * MathF.Max(axis.Length() * 1e-5f, 1e-5f));
        }

        /// <summary>
        /// Tilts a two-node hinge ring's vector along the child quad's diagonals where the quad's four ring-to-ring spans
        /// tie. <paramref name="toVectorFrame"/> maps model space into the vector's frame.
        /// </summary>
        Vector3 BreakHingeFanQuadTie(List<int> ring, Vector3 vector, Quaternion toVectorFrame)
        {
            if (ring.Count != 2)
            {
                return vector;
            }

            int[]? tied = null;
            foreach (var quad in Quads)
            {
                if (quad.Length != 4 || !((quad[0] == ring[0] && quad[1] == ring[1]) || (quad[0] == ring[1] && quad[1] == ring[0]))
                    || !Array.TrueForAll(quad, corner => corner >= 0 && corner < InitPosePositions.Length))
                {
                    continue;
                }

                var spans = new[]
                {
                    Vector3.Distance(InitPosePositions[quad[0]], InitPosePositions[quad[2]]),
                    Vector3.Distance(InitPosePositions[quad[1]], InitPosePositions[quad[3]]),
                    Vector3.Distance(InitPosePositions[quad[0]], InitPosePositions[quad[3]]),
                    Vector3.Distance(InitPosePositions[quad[1]], InitPosePositions[quad[2]]),
                };
                var longest = spans.Max();
                if (longest <= 0f || longest - spans.Min() > longest * 1e-5f)
                {
                    continue;
                }

                if (tied is not null)
                {
                    return vector;
                }

                tied = quad;
            }

            if (tied is null)
            {
                return vector;
            }

            var across = InitPosePositions[tied[3]] - InitPosePositions[tied[2]];
            if (tied[0] == ring[0])
            {
                across = -across;
            }

            if (across.LengthSquared() <= 0f)
            {
                return vector;
            }

            var direction = Vector3.Normalize(Vector3.Transform(across, toVectorFrame));
            return vector + (direction * MathF.Max(vector.Length() * 1e-5f, 1e-5f));
        }

        /// <summary>Gets how many auto-generated proxy nodes the compiler extruded from a joint.</summary>
        public int ProxyCountOf(int jointNode) => ProxyRingOf(jointNode).Count;

        /// <summary>Gets whether a chain joint carries a hinge constraint.</summary>
        public bool IsHingedJoint(int jointNode)
        {
            var ring = ProxyRingOf(jointNode);
            return ring.Count >= 2 && HingeLimitOverRing(ring) is not null;
        }

        bool IsHingeRegeneratedProxy(int node)
        {
            if (node >= CtrlNames.Length || !IsProxyNodeName(CtrlNames[node]))
            {
                return false;
            }

            var parent = node < SkelParents.Length ? SkelParents[node] : -1;
            if (parent < 0 || parent >= CtrlNames.Length)
            {
                return false;
            }

            if (Array.IndexOf(CtrlNames, HingeAnchorPrefix + CtrlNames[parent]) >= 0)
            {
                return true;
            }

            return IsHingedJoint(parent) || RigidHingeJoints.ContainsKey(parent);
        }

        List<int> ProxyRingOf(int jointNode)
        {
            var ring = new List<int>();
            for (var node = 0; node < CtrlNames.Length; node++)
            {
                if (node < SkelParents.Length && SkelParents[node] == jointNode
                    && CtrlNames[node].StartsWith("$cc", StringComparison.Ordinal))
                {
                    ring.Add(node);
                }
            }

            ring.Sort((a, b) => string.CompareOrdinal(CtrlNames[a], CtrlNames[b]));
            return ring;
        }

        /// <summary>
        /// Gets the <c>$cc</c> nodes joined to another <c>$cc</c> node by a source face edge or diagonal.
        /// </summary>
        HashSet<int> SourceFaceRingNodes()
        {
            bool IsChainRing(int node) => node >= 0 && node < CtrlNames.Length
                && CtrlNames[node].StartsWith("$cc", StringComparison.Ordinal);

            var nodes = new HashSet<int>();
            foreach (var (a, b) in DeriveRodsFromFaces(SourceFaces))
            {
                if (IsChainRing(a) && IsChainRing(b))
                {
                    nodes.Add(a);
                    nodes.Add(b);
                }
            }

            return nodes;
        }

        /// <summary>
        /// Gets whether a surface element joins a hinged joint of <paramref name="chain"/> to one of its children.
        /// </summary>
        public bool HasRigidHingeLink(BoneChain chain)
        {
            var groupOf = new Dictionary<int, int>();
            foreach (var joint in chain.Joints)
            {
                groupOf[joint.Node] = joint.Node;
                foreach (var proxy in ProxyRingOf(joint.Node))
                {
                    groupOf[proxy] = joint.Node;
                }
            }

            var hingedLinks = chain.Joints
                .Where(joint => !joint.IsRoot && IsHingedJoint(joint.ParentNode))
                .Select(static joint => (joint.ParentNode, joint.Node))
                .ToList();

            bool JoinsAHingedLink(int[] face, bool overTheRing)
            {
                var groups = new HashSet<int>();
                foreach (var node in face)
                {
                    if (groupOf.TryGetValue(node, out var group))
                    {
                        groups.Add(group);
                    }
                }

                return hingedLinks.Exists(link => groups.Contains(link.ParentNode) && groups.Contains(link.Node)
                    && (!overTheRing || (ProxyRingOf(link.ParentNode) is { Count: 2 } ring && SpansRing(face, ring))));
            }

            foreach (var quad in Quads)
            {
                if (JoinsAHingedLink(quad, overTheRing: false))
                {
                    return true;
                }
            }

            foreach (var tri in Tris)
            {
                if (JoinsAHingedLink(tri, overTheRing: true))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets whether a bounded rod spans a parent-child link of <paramref name="chain"/>.
        /// </summary>
        public bool HasChainRods(BoneChain chain)
        {
            var groupOf = new Dictionary<int, int>();
            foreach (var joint in chain.Joints)
            {
                groupOf[joint.Node] = joint.Node;
                foreach (var proxy in ProxyRingOf(joint.Node))
                {
                    groupOf[proxy] = joint.Node;
                }
            }

            var links = chain.Joints
                .Where(static joint => !joint.IsRoot)
                .Select(static joint => (joint.ParentNode, joint.Node))
                .ToHashSet();

            foreach (var rod in Rods)
            {
                if (rod.MaxDist < UnboundedRodDistance
                    && groupOf.TryGetValue(rod.NodeA, out var a) && groupOf.TryGetValue(rod.NodeB, out var b)
                    && (links.Contains((a, b)) || links.Contains((b, a))))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets whether an unbounded rod joins two of the chains' generated nodes, which only <c>add_bend_only_rods</c> builds.
        /// </summary>
        public bool HasChainBendOnlyRods(List<BoneChain> chains)
        {
            var generated = chains
                .SelectMany(static chain => chain.Joints)
                .SelectMany(joint => ProxyRingOf(joint.Node))
                .ToHashSet();

            if (generated.Count == 0)
            {
                generated = SourceFaceRingNodes();
            }

            return Rods.Any(rod => rod.MaxDist >= UnboundedRodDistance
                && generated.Contains(rod.NodeA) && generated.Contains(rod.NodeB));
        }

        /// <summary>
        /// Gets whether a banded rod joins two of the chains' generated nodes or a surface fold exists, which only
        /// <c>add_stiffness_rods</c> builds.
        /// </summary>
        public bool HasChainStiffnessRods(List<BoneChain> chains)
        {
            var generated = chains
                .SelectMany(static chain => chain.Joints)
                .SelectMany(joint => ProxyRingOf(joint.Node))
                .ToHashSet();

            if (generated.Count == 0)
            {
                generated = SourceFaceRingNodes();
            }

            return Rods.Any(rod => rod.MaxDist < UnboundedRodDistance && rod.MinDist < rod.MaxDist
                && rod.NodeA != rod.NodeB
                && generated.Contains(rod.NodeA) && generated.Contains(rod.NodeB))
                || Rods.Any(rod => IsSurfaceFanRod(rod, banded: true));
        }

        /// <summary>
        /// Gets whether the compiler folded rods across this model's own faces: some banded rod on a folded pair carries its
        /// endpoints' final inverse-mass ratio as its weight, which only a rod the compiler built itself does.
        /// </summary>
        public bool HasSurfaceFolds => hasSurfaceFolds ??= Rods.Any(rod => IsSurfaceFanRod(rod, banded: true));

        bool? hasSurfaceFolds;

        /// <summary>
        /// Returns whether <paramref name="rod"/> is a banded rod the compiler folded across a face edge on its own: it carries its
        /// endpoints' final inverse-mass ratio as its weight, which no declaration does.
        /// </summary>
        public bool IsSurfaceFold(Rod rod) => IsSurfaceFanRod(rod, banded: true);

        /// <summary>
        /// The pairs <c>add_stiffness_rods</c> makes the compiler fold across the edges this model's own faces
        /// share, in the order its fold walk meets them: the solve elements (see <see cref="FoldWalkSolveElements"/>),
        /// then the faces that were built into rods instead (see <see cref="SourceElementWalk"/>).
        /// </summary>
        HashSet<(int, int)> SurfaceFanPairs => surfaceFanPairs ??= PredictBendRods(
            [.. FoldWalkSolveElements(), .. SourceElementWalk()], IsStatic);

        HashSet<(int, int)>? surfaceFanPairs;

        /// <summary>
        /// The node pairs whose every rod is one the compiler folded across a face edge on its own (see
        /// <see cref="IsSurfaceFanRod"/>), so that no declaration put any rod there.
        /// </summary>
        HashSet<(int, int)> SurfaceFoldOnlyPairs => surfaceFoldOnlyPairs ??= Rods
            .Where(static rod => rod.NodeA != rod.NodeB)
            .GroupBy(static rod => rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA))
            .Where(group => group.All(rod => IsSurfaceFanRod(rod, banded: true)))
            .Select(static group => group.Key)
            .ToHashSet();

        HashSet<(int, int)>? surfaceFoldOnlyPairs;

        /// <summary>
        /// Gets whether <paramref name="rod"/> lies on a surface fold pair and carries its endpoints' final inverse-mass
        /// ratio as its weight. With <paramref name="banded"/>, the rod must also be banded.
        /// </summary>
        bool IsSurfaceFanRod(Rod rod, bool banded)
        {
            if (rod.NodeA == rod.NodeB || rod.MaxDist >= UnboundedRodDistance
                || (banded && rod.MinDist >= rod.MaxDist - SurfaceFanBandTolerance * MathF.Max(1f, rod.MaxDist))
                || rod.NodeA < 0 || rod.NodeA >= NodeInvMasses.Length || rod.NodeB < 0 || rod.NodeB >= NodeInvMasses.Length)
            {
                return false;
            }

            var sum = NodeInvMasses[rod.NodeA] + NodeInvMasses[rod.NodeB];
            if (sum <= 0f)
            {
                return false;
            }

            var ratio = NodeInvMasses[rod.NodeA] / sum;
            if (MathF.Abs(ratio - 0.5f) <= SurfaceFanWeightTolerance
                || MathF.Abs(rod.Weight0 - ratio) > SurfaceFanWeightTolerance)
            {
                return false;
            }

            return SurfaceFanPairs.Contains(rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA));
        }

        /// <summary>
        /// Gets whether a rod on a surface fold pair joined the network after the mass pass.
        /// </summary>
        bool FoldedAfterMass(Rod rod)
        {
            var sum = rod.NodeA >= 0 && rod.NodeA < NodeInvMasses.Length && rod.NodeB >= 0 && rod.NodeB < NodeInvMasses.Length
                ? NodeInvMasses[rod.NodeA] + NodeInvMasses[rod.NodeB]
                : 0f;
            if (sum > 0f && MathF.Abs(NodeInvMasses[rod.NodeA] / sum - 0.5f) > SurfaceFanWeightTolerance)
            {
                return IsSurfaceFanRod(rod, banded: false);
            }

            return !(surfaceFoldsAbsent ??= Rods.Any(IsUnequalFoldedPair) && !Rods.Any(other => IsSurfaceFanRod(other, banded: false)));
        }

        bool? surfaceFoldsAbsent;

        bool IsUnequalFoldedPair(Rod rod)
        {
            if (rod.NodeA == rod.NodeB || rod.NodeA < 0 || rod.NodeA >= NodeInvMasses.Length
                || rod.NodeB < 0 || rod.NodeB >= NodeInvMasses.Length)
            {
                return false;
            }

            var sum = NodeInvMasses[rod.NodeA] + NodeInvMasses[rod.NodeB];
            return sum > 0f && MathF.Abs(NodeInvMasses[rod.NodeA] / sum - 0.5f) > SurfaceFanWeightTolerance
                && SurfaceFanPairs.Contains(rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA));
        }

        const float SurfaceFanBandTolerance = 1e-4f;

        const float SurfaceFanWeightTolerance = 2e-4f;

        /// <summary>The maximum length a rod that is not length-limited at all is given.</summary>
        public const float UnboundedRodDistance = 16384f;

        /// <summary>Gets the named vertex selections the cloth carries, empty when it has none.</summary>
        public IReadOnlyList<VertexMap> VertexMaps { get; private set; } = [];

        /// <summary>
        /// Orders <paramref name="proxy"/>'s selections so each node's <c>m_DynNodeVertexSet</c> winner precedes every
        /// other selection painting it at the same weight. Returns the proxy's own order when that is not possible.
        /// </summary>
        public string[] VertexSetStreamOrder(ProxyMesh proxy)
        {
            var names = proxy.VertexMaps.Select(static map => map.Name).ToArray();
            if (names.Length < 2 || DynNodeVertexSet.Length == 0)
            {
                return names;
            }

            var nameOfHash = new Dictionary<uint, string>(VertexMaps.Count);
            foreach (var map in VertexMaps)
            {
                nameOfHash.TryAdd(map.NameHash, map.Name);
            }

            var constraints = new List<(string Winner, string Rival)>();
            for (var vertex = 0; vertex < proxy.NodeIndices.Length; vertex++)
            {
                var dynamic = proxy.NodeIndices[vertex] - StaticNodeCount;
                if (dynamic < 0 || dynamic >= DynNodeVertexSet.Length)
                {
                    continue;
                }

                var set = DynNodeVertexSet[dynamic];
                if (set >= VertexSetNames.Length
                    || !nameOfHash.TryGetValue(VertexSetNames[set], out var winner))
                {
                    continue;
                }

                var top = 0f;
                foreach (var (_, weights) in proxy.VertexMaps)
                {
                    if (vertex < weights.Length)
                    {
                        top = MathF.Max(top, weights[vertex]);
                    }
                }

                if (top <= 0f)
                {
                    continue;
                }

                var winnerCovers = false;
                var rivals = new List<string>();
                foreach (var (mapName, weights) in proxy.VertexMaps)
                {
                    if (vertex >= weights.Length || weights[vertex] < top)
                    {
                        continue;
                    }

                    if (mapName == winner)
                    {
                        winnerCovers = true;
                    }
                    else
                    {
                        rivals.Add(mapName);
                    }
                }

                if (!winnerCovers)
                {
                    continue;
                }

                foreach (var rival in rivals)
                {
                    constraints.Add((winner, rival));
                }
            }

            return OrderByFirstWriterWins(names, constraints);
        }

        internal static string[] OrderByFirstWriterWins(IReadOnlyList<string> names,
            IReadOnlyList<(string Winner, string Rival)> constraints)
        {
            var incoming = new Dictionary<string, HashSet<string>>(names.Count, StringComparer.Ordinal);
            foreach (var name in names)
            {
                incoming[name] = [];
            }

            foreach (var (winner, rival) in constraints)
            {
                if (winner != rival && incoming.ContainsKey(winner) && incoming.TryGetValue(rival, out var before))
                {
                    before.Add(winner);
                }
            }

            var ordered = new List<string>(names.Count);
            var placed = new HashSet<string>(StringComparer.Ordinal);
            while (ordered.Count < names.Count)
            {
                var next = names.FirstOrDefault(name => !placed.Contains(name)
                    && incoming[name].All(placed.Contains));
                if (next is null)
                {
                    return [.. names];
                }

                ordered.Add(next);
                placed.Add(next);
            }

            return [.. ordered];
        }

        /// <summary>Gets the names of the <c>m_VertexMaps</c> records that cover no vertex.</summary>
        public IReadOnlyList<string> ZeroVertexSelectionNames { get; private set; } = [];

        /// <summary>Gets the name a selection rebuilt from <see cref="VertexSetNames"/> is exported under.</summary>
        static string SynthesizedVertexSetName(int set)
            => string.Create(CultureInfo.InvariantCulture, $"vertex_set_{set}");

        /// <summary>
        /// Rebuilds the named selections from <see cref="VertexSetNames"/> and <see cref="DynNodeVertexSet"/>.
        /// </summary>
        List<VertexMap> BuildVertexMapsFromSets()
        {
            var sets = new List<VertexMap>();
            for (var set = 0; set < VertexSetNames.Length; set++)
            {
                var weights = new float[DynNodeVertexSet.Length];
                var members = 0;
                for (var node = 0; node < DynNodeVertexSet.Length; node++)
                {
                    if (DynNodeVertexSet[node] == set)
                    {
                        weights[node] = 1f;
                        members++;
                    }
                }

                if (members > 0)
                {
                    sets.Add(new VertexMap(SynthesizedVertexSetName(set), VertexSetNames[set],
                        StaticNodeCount, DynNodeVertexSet.Length, default, weights));
                }
            }

            return sets;
        }

        bool vertexMapsFromSets;

        /// <summary>
        /// Drops the selection rebuilt from the vertex set named after the model's file. Selections read from
        /// <c>m_VertexMaps</c> are kept.
        /// </summary>
        /// <param name="modelFileName">The model's file name without directory or extension.</param>
        public void DropModelNameVertexSet(string modelFileName)
        {
            if (!vertexMapsFromSets)
            {
                return;
            }

            var hash = StringToken.Get(modelFileName);
            VertexMaps = [.. VertexMaps.Where(map => map.NameHash != hash)];
        }

        /// <summary>
        /// Drops the selection rebuilt from the vertex set with name hash 0. Selections read from <c>m_VertexMaps</c> are kept.
        /// </summary>
        public void DropUnnamedVertexSet()
        {
            if (!vertexMapsFromSets)
            {
                return;
            }

            VertexMaps = [.. VertexMaps.Where(static map => map.NameHash != 0)];
        }

        /// <summary>A named vertex selection, used to target cloth effects and joint vertex maps.</summary>
        /// <param name="Name">The authored selection name.</param>
        /// <param name="NameHash">The hash the compiler keys the selection by.</param>
        /// <param name="VertexBase">The first control node the selection covers.</param>
        /// <param name="VertexCount">How many consecutive control nodes it covers.</param>
        /// <param name="CenterOfMass">The selection's centre of mass.</param>
        /// <param name="Weights">Membership weight of each covered node, 0..1, indexed from <paramref name="VertexBase"/>.</param>
        /// <param name="VolumetricSolveStrength">How strongly the selection is solved as a volume.</param>
        /// <param name="ScaleSourceNode">The control node whose scale the selection follows, or -1.</param>
        public readonly record struct VertexMap(string Name, uint NameHash, int VertexBase, int VertexCount,
            Vector3 CenterOfMass, float[] Weights, float VolumetricSolveStrength = 0f, int ScaleSourceNode = -1)
        {
            /// <summary>Gets how strongly <paramref name="node"/> belongs to this selection, 0 when it does not.</summary>
            public float WeightOf(int node)
            {
                var index = node - VertexBase;
                return index >= 0 && index < Weights.Length ? Weights[index] : 0f;
            }
        }

        /// <summary>
        /// Gets the selections <paramref name="node"/> belongs to as <c>name[=weight],...</c>, or null when it belongs to
        /// none. A weight of 1 is written as the bare name.
        /// </summary>
        public string? GetVertexMapNames(int node)
        {
            var names = new List<string>();
            foreach (var map in VertexMaps)
            {
                var weight = map.WeightOf(node);
                if (weight <= 0f)
                {
                    continue;
                }

                names.Add(weight >= 1f
                    ? map.Name
                    : string.Create(CultureInfo.InvariantCulture, $"{map.Name}={weight}"));
            }

            return names.Count > 0 ? string.Join(',', names) : null;
        }

        /// <summary>
        /// Gets how strongly <paramref name="node"/> belongs to the selection named
        /// <paramref name="mapName"/>, 0 when the selection does not exist or does not cover it.
        /// </summary>
        public float VertexMapWeight(string mapName, int node)
        {
            foreach (var map in VertexMaps)
            {
                if (map.Name == mapName)
                {
                    return map.WeightOf(node);
                }
            }

            return 0f;
        }

        /// <summary>
        /// Gets the one partial weight every node covered by the selection shares, or null when there is none.
        /// </summary>
        public float? UniformVertexMapWeight(string mapName)
        {
            float? shared = null;
            foreach (var map in VertexMaps)
            {
                if (map.Name != mapName)
                {
                    continue;
                }

                for (var node = 0; node < CtrlNames.Length; node++)
                {
                    var weight = map.WeightOf(node);
                    if (weight <= 0f)
                    {
                        continue;
                    }

                    if (shared is { } first && MathF.Abs(weight - first) > 0.5f / 255f)
                    {
                        return null;
                    }

                    shared ??= weight;
                }

                break;
            }

            return shared is { } value && value < 1f ? value : null;
        }

        /// <summary>Strips the optional <c>=weight</c> suffix off one entry of a <see cref="GetVertexMapNames"/> list.</summary>
        public static string VertexMapName(string entry)
        {
            var weight = entry.IndexOf('=', StringComparison.Ordinal);
            return weight < 0 ? entry : entry[..weight];
        }

        /// <summary>
        /// Gets the selection that covers exactly the simulated nodes of <paramref name="proxy"/> (or of the sheets of
        /// <paramref name="group"/> it overlaps) and is not registered as a vertex set, or null when no single one does.
        /// </summary>
        public string? GetProxyVertexMapName(ProxyMesh proxy, IReadOnlyList<ProxyMesh>? group = null)
        {
            var simulated = SimulatedProxyNodes(proxy);
            if (simulated.Count == 0)
            {
                return null;
            }

            string? found = null;
            foreach (var map in VertexMaps)
            {
                if (Array.IndexOf(VertexSetNames, map.NameHash) >= 0)
                {
                    continue;
                }

                var members = new HashSet<int>();
                for (var i = 0; i < map.Weights.Length; i++)
                {
                    if (map.Weights[i] > 0f)
                    {
                        members.Add(map.VertexBase + i);
                    }
                }

                if (!members.Overlaps(simulated))
                {
                    continue;
                }

                var covered = new HashSet<int>();
                foreach (var sibling in group ?? [proxy])
                {
                    var siblingNodes = SimulatedProxyNodes(sibling);
                    if (members.Overlaps(siblingNodes))
                    {
                        covered.UnionWith(siblingNodes);
                    }
                }

                if (members.SetEquals(covered))
                {
                    if (found is not null)
                    {
                        if (VertexMapAliases(found).Contains(map.Name))
                        {
                            continue;
                        }

                        return null;
                    }

                    found = map.Name;
                }
            }

            return found;
        }

        /// <summary>Gets whether <paramref name="nameHash"/> is registered in <see cref="VertexSetNames"/>.</summary>
        public bool RegistersVertexSet(uint nameHash) => Array.IndexOf(VertexSetNames, nameHash) >= 0;

        /// <summary>
        /// Gets every selection, not registered as a vertex set, with the same membership as <paramref name="mapName"/>,
        /// in compiled order and including it. Empty when no selection has that name.
        /// </summary>
        public IReadOnlyList<string> VertexMapAliases(string mapName)
        {
            var source = VertexMaps.FirstOrDefault(map => map.Name == mapName);
            if (source.Name != mapName)
            {
                return [];
            }

            return [.. VertexMaps
                .Where(map => map.Name == mapName
                    || (Array.IndexOf(VertexSetNames, map.NameHash) < 0 && HasSameMembership(map, source)))
                .Select(static map => map.Name)];
        }

        static bool HasSameMembership(VertexMap a, VertexMap b)
        {
            var first = Math.Min(a.VertexBase, b.VertexBase);
            var last = Math.Max(a.VertexBase + a.VertexCount, b.VertexBase + b.VertexCount);
            for (var node = first; node < last; node++)
            {
                if (MathF.Abs(a.WeightOf(node) - b.WeightOf(node)) > 0.5f / 255f)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The control nodes of <paramref name="proxy"/> whose vertices its sheet simulates.</summary>
        public static HashSet<int> SimulatedProxyNodes(ProxyMesh proxy)
        {
            var simulated = new HashSet<int>();
            for (var v = 0; v < proxy.NodeIndices.Length; v++)
            {
                if (v < proxy.ClothEnable.Length && proxy.ClothEnable[v] != 0f)
                {
                    simulated.Add(proxy.NodeIndices[v]);
                }
            }

            return simulated;
        }

        /// <summary>An anti-tunnelling probe (from <c>m_AntiTunnelProbes</c>).</summary>
        public readonly record struct AntiTunnelProbe(float Weight, uint Flags, int ProbeNode, int Count, int Begin,
            float ActivationDistance, float CurvatureRadius, float Bias);

        /// <summary>Gets the anti-tunnelling probes (<c>m_AntiTunnelProbes</c>).</summary>
        public AntiTunnelProbe[] AntiTunnelProbes { get; }

        /// <summary>Gets the control nodes targeted by <see cref="AntiTunnelProbes"/> (<c>m_AntiTunnelTargetNodes</c>).</summary>
        public int[] AntiTunnelTargetNodes { get; }

        /// <summary>Gets the anti-tunnelling probe bytecode (<c>m_AntiTunnelBytecode</c>).</summary>
        public uint[] AntiTunnelBytecode { get; }

        /// <summary>A dynamic-to-kinematic node link (from <c>m_DynKinLinks</c>).</summary>
        public readonly record struct DynKinLink(int Parent, int Child);

        /// <summary>Gets the dynamic-to-kinematic node links (<c>m_DynKinLinks</c>), one per authored <c>ClothFollowBone</c>.</summary>
        public DynKinLink[] DynKinLinks { get; }

        /// <summary>A collision plane (from <c>m_CollisionPlanes</c>).</summary>
        public readonly record struct CollisionPlane(int CtrlParent, int ChildNode, Vector3 PlaneNormal,
            float PlaneOffset, float Stickiness, float Strength);

        /// <summary>Gets the collision planes (<c>m_CollisionPlanes</c>).</summary>
        public CollisionPlane[] CollisionPlanes { get; }

        /// <summary>A signed-distance-field collision volume (from <c>m_SDFRigids</c>).</summary>
        public readonly record struct SDFRigid(Vector3 LocalMin, Vector3 LocalMax, float Bounciness, int Node,
            int CollisionMask, int VertexMapIndex, uint Flags, float[] Distances, int Width, int Height, int Depth);

        /// <summary>Gets the signed-distance-field collision volumes (<c>m_SDFRigids</c>).</summary>
        public SDFRigid[] SDFRigids { get; }

        /// <summary>
        /// Gets the goal-damped spring integrator bitmask (<c>m_GoalDampedSpringIntegrators</c>), one bit per dynamic node.
        /// </summary>
        public uint[] GoalDampedSpringIntegrators { get; }

        /// <summary>
        /// Gets, per control node, whether its goal values are exported through the raw attraction paints instead of the
        /// goal-strength pair.
        /// </summary>
        public bool[] RawGoalPaintNodes { get; }

        /// <summary>
        /// A named cloth effect (from <c>m_Effects</c>). <see cref="Params"/> is the unparsed per-type parameter block.
        /// </summary>
        public readonly record struct Effect(string Name, uint NameHash, int Type, KVObject Params);

        /// <summary>Gets the named cloth effects (<c>m_Effects</c>).</summary>
        public Effect[] Effects { get; }

        /// <summary>A deprecated morph layer (from <c>m_MorphLayers</c>).</summary>
        public readonly record struct MorphLayer(string Name, uint NameHash, int[] Nodes, Vector3[] InitPos,
            float[] Gravity, float[] GoalStrength, float[] GoalDamping, uint Flags);

        /// <summary>Gets the deprecated morph layers (<c>m_MorphLayers</c>).</summary>
        public MorphLayer[] MorphLayers { get; }

        /// <summary>Gets the raw morph-set data (<c>m_MorphSetData</c>).</summary>
        public byte[] MorphSetData { get; }

        /// <summary>A self-collision layer (from <c>m_SelfCollisionLayers</c>).</summary>
        public readonly record struct SelfCollisionLayer(string Name, int[] Nodes, float ParentReaction,
            uint Flags, uint[] EndIndices);

        /// <summary>Gets the self-collision layers (<c>m_SelfCollisionLayers</c>).</summary>
        public SelfCollisionLayer[] SelfCollisionLayers { get; }

        /// <summary>A node stray-box constraint (from <c>m_NodeStrayBoxes</c>).</summary>
        public readonly record struct NodeStrayBox(Vector3 Min, Vector3 Max, uint Flags, int NodeA, int NodeB);

        /// <summary>Gets the node stray-box constraints (<c>m_NodeStrayBoxes</c>).</summary>
        public NodeStrayBox[] NodeStrayBoxes { get; }

        /// <summary>A tapered-capsule stretch constraint (from <c>m_TaperedCapsuleStretches</c>).</summary>
        public readonly record struct TaperedCapsuleStretch(int NodeA, int NodeB, int CollisionMask,
            float RadiusA, float RadiusB);

        /// <summary>Gets the tapered-capsule stretch constraints (<c>m_TaperedCapsuleStretches</c>).</summary>
        public TaperedCapsuleStretch[] TaperedCapsuleStretches { get; }

        /// <summary>A per-pair spring constraint (from <c>m_SpringIntegrator</c>).</summary>
        public readonly record struct SpringIntegrator(int NodeA, int NodeB, float RestLength,
            float SpringConstant, float SpringDamping, float NodeWeight0);

        /// <summary>Gets the spring constraints (<c>m_SpringIntegrator</c>).</summary>
        public SpringIntegrator[] SpringIntegrators { get; }

        /// <summary>
        /// One row of <c>m_RigidColliderPriorities</c>: the index at which a priority group starts in each
        /// rigid-collider array. Row <c>g</c> holds group <c>g</c>'s first index in every array and the last
        /// row holds each array's element count, so group <c>g</c> owns <c>[row[g], row[g + 1])</c>.
        /// </summary>
        public readonly record struct RigidColliderIndices(int TaperedCapsuleRigidIndex, int SphereRigidIndex,
            int BoxRigidIndex, int SDFRigidIndex, int CollisionPlaneIndex);

        /// <summary>Gets the rigid-collider priority groups (<c>m_RigidColliderPriorities</c>).</summary>
        public RigidColliderIndices[] RigidColliderPriorities { get; }

        /// <summary>A jiggle bone's physical parameters (from <c>CFeJiggleBone</c>).</summary>
        public readonly record struct JiggleBone(
            uint Flags, float Length, float TipMass,
            float YawStiffness, float YawDamping, float PitchStiffness, float PitchDamping,
            float AlongStiffness, float AlongDamping, float AngleLimit,
            float MinYaw, float MaxYaw, float YawFriction, float YawBounce,
            float MinPitch, float MaxPitch, float PitchFriction, float PitchBounce,
            float BaseMass, float BaseStiffness, float BaseDamping,
            float BaseMinLeft, float BaseMaxLeft, float BaseLeftFriction,
            float BaseMinUp, float BaseMaxUp, float BaseUpFriction,
            float BaseMinForward, float BaseMaxForward, float BaseForwardFriction,
            float Radius0, float Radius1, Vector3 Point0, Vector3 Point1, int CollisionMask);

        /// <summary>A jiggle bone keyed to its control node (from <c>m_JiggleBones</c>).</summary>
        public readonly record struct IndexedJiggleBone(int Node, int JiggleParent, JiggleBone Bone);

        /// <summary>Gets the jiggle bones (<c>m_JiggleBones</c>).</summary>
        public IndexedJiggleBone[] JiggleBones { get; }

        /// <summary>A bone-merge link (from <c>m_BoneMergeLinks</c>).</summary>
        public readonly record struct BoneMergeLink(uint ParentHash, int ChildNode);

        /// <summary>Gets the bone-merge links (<c>m_BoneMergeLinks</c>).</summary>
        public BoneMergeLink[] BoneMergeLinks { get; }

        /// <summary>A node locked to its parent's offset transform (from <c>m_LockToParent</c>).</summary>
        public readonly record struct LockToParentLink(Vector3 Offset, int CtrlParent, int CtrlChild);

        /// <summary>Gets the parent-locked node links (<c>m_LockToParent</c>).</summary>
        public LockToParentLink[] LockToParent { get; }

        /// <summary>Gets the control nodes locked to their animated goal (<c>m_LockToGoal</c>).</summary>
        public int[] LockToGoal { get; }

        /// <summary>A strip's column pairing (from <c>m_CtrlOsOffsets</c>).</summary>
        public readonly record struct CtrlOsOffset(int CtrlParent, int CtrlChild);

        /// <summary>Gets the strip column pairings (<c>m_CtrlOsOffsets</c>).</summary>
        public CtrlOsOffset[] CtrlOsOffsets { get; }

        /// <summary>
        /// Gets whether the cloth was authored as ModelDoc's <c>ImportedCloth</c> node: it carries a field only an imported
        /// node row writes and none of the ring, sheet or fit data other constructs produce.
        /// </summary>
        public bool IsImportedCloth
            => (CtrlOsOffsets.Length > 0 || HasImportedNodeFields)
                && CtrlOffsets.Length == 0
                && Quads.Length == 0 && Tris.Length == 0
                && FitMatrixNodes.Count == 0
                && CtrlNames.Length > 0
                && !Array.Exists(CtrlNames, IsCompilerGeneratedNodeName);

        /// <summary>
        /// Gets both columns of every <c>m_CtrlOsOffsets</c> pair on a model without a surface that is not
        /// <see cref="IsImportedCloth"/> as a whole.
        /// </summary>
        public IReadOnlySet<int> ImportedStripNodes => importedStripNodes ??= BuildImportedStripNodes();

        private HashSet<int>? importedStripNodes;

        private HashSet<int> BuildImportedStripNodes()
        {
            var strip = new HashSet<int>();
            if (CtrlOsOffsets.Length == 0 || IsImportedCloth || Quads.Length > 0 || Tris.Length > 0)
            {
                return strip;
            }

            foreach (var pair in CtrlOsOffsets)
            {
                if (pair.CtrlParent >= 0 && pair.CtrlParent < CtrlNames.Length && pair.CtrlChild >= 0 && pair.CtrlChild < CtrlNames.Length
                    && !IsCompilerGeneratedNodeName(CtrlNames[pair.CtrlParent]) && !IsCompilerGeneratedNodeName(CtrlNames[pair.CtrlChild]))
                {
                    strip.Add(pair.CtrlParent);
                    strip.Add(pair.CtrlChild);
                }
            }

            return strip;
        }

        bool HasImportedNodeFields
        {
            get
            {
                var flags = StaticNodeFlags | DynamicNodeFlags;
                return ((flags & (NodeFlagRawForceAttraction | NodeFlagRawVertexAttraction)) != 0 && (flags & NodeFlagGoalAttraction) == 0)
                    || FollowNodeLinks.Count > 0
                    || Array.Exists(LegacyStretchForce, static force => force != 0f);
            }
        }

        static bool IsCompilerGeneratedNodeName(string? name)
            => string.IsNullOrEmpty(name)
                || name.StartsWith("$cc", StringComparison.Ordinal)
                || name.StartsWith("$cloth_m", StringComparison.Ordinal)
                || name.StartsWith(FreeClothNodePrefix, StringComparison.Ordinal)
                || name.StartsWith("$cloth_root", StringComparison.Ordinal)
                || name.StartsWith("$ha_", StringComparison.Ordinal);

        /// <summary>
        /// Gets each node's parent along the <c>m_Ropes</c> runs alone, without the <c>m_FollowNodes</c> fallback of
        /// <see cref="BuildRopeParents"/>.
        /// </summary>
        public IReadOnlyDictionary<int, int> RopeRunParents
        {
            get
            {
                var parents = new Dictionary<int, int>();
                var ropeCount = Data.GetInt32Property("m_nRopeCount");
                var ropes = Data.GetIntegerArray("m_Ropes");
                if (ropeCount <= 0 || ropes.Length <= ropeCount)
                {
                    return parents;
                }

                var begin = ropeCount;
                for (var rope = 0; rope < ropeCount; rope++)
                {
                    var end = Math.Min((int)ropes[rope], ropes.Length);
                    for (var i = begin + 1; i < end; i++)
                    {
                        parents.TryAdd((int)ropes[i], (int)ropes[i - 1]);
                    }

                    begin = end;
                }

                return parents;
            }
        }

        /// <summary>
        /// Gets whether a node lies on an <c>m_Ropes</c> run of two or more nodes. The rope pass never starts or keeps a
        /// run on a node whose class byte is 1, which is what a <c>ClothNode</c> at the default alignment compiles to.
        /// </summary>
        public bool IsRopeNode(int node)
        {
            if (ropeNodes is null)
            {
                ropeNodes = [];
                var ropeCount = Data.GetInt32Property("m_nRopeCount");
                var ropes = Data.GetIntegerArray("m_Ropes");
                if (ropeCount > 0 && ropes.Length > ropeCount)
                {
                    var begin = ropeCount;
                    for (var rope = 0; rope < ropeCount; rope++)
                    {
                        var end = Math.Min((int)ropes[rope], ropes.Length);
                        for (var i = begin; i < end && end - begin >= 2; i++)
                        {
                            ropeNodes.Add((int)ropes[i]);
                        }

                        begin = end;
                    }
                }
            }

            return ropeNodes.Contains(node);
        }

        HashSet<int>? ropeNodes;

        /// <summary>Gets each follower node's leader and follow weight (<c>m_FollowNodes</c>).</summary>
        public IReadOnlyDictionary<int, (int Parent, float Weight)> FollowNodeLinks
        {
            get
            {
                var links = new Dictionary<int, (int, float)>();
                foreach (var follow in Data.GetArray("m_FollowNodes") ?? [])
                {
                    links.TryAdd(follow.GetInt32Property("nChildNode"),
                        (follow.GetInt32Property("nParentNode"), follow.GetFloatProperty("flWeight")));
                }

                return links;
            }
        }

        /// <summary>A generated node's bone-local anchor offset (from <c>m_CtrlOffsets</c>).</summary>
        public readonly record struct CtrlOffset(Vector3 Offset, int CtrlParent, int CtrlChild);

        /// <summary>Gets the generated-node anchor offsets (<c>m_CtrlOffsets</c>).</summary>
        public CtrlOffset[] CtrlOffsets { get; }

        /// <summary>
        /// Gets the named vertex sets' name hashes (<c>m_VertexSetNames</c>), paired with
        /// <see cref="DynNodeVertexSet"/>.
        /// </summary>
        public uint[] VertexSetNames { get; }

        /// <summary>
        /// Gets each dynamic node's vertex-set index into <see cref="VertexSetNames"/>
        /// (<c>m_DynNodeVertexSet</c>).
        /// </summary>
        public byte[] DynNodeVertexSet { get; }

        /// <summary>Gets the legacy per-node stretch force (<c>m_LegacyStretchForce</c>).</summary>
        public float[] LegacyStretchForce { get; }

        /// <summary>Gets the raw, uninterpreted <c>m_CollisionSpheres</c> entries.</summary>
        public IReadOnlyList<KVObject> CollisionSpheres { get; }

        static readonly HashSet<string> DerivedKeys =
        [
            "m_CtrlHash",
            "m_TreeParents", "m_TreeChildren", "m_nTreeDepth",
            "m_SimdRods", "m_SimdNodeBases", "m_SimdAnimStrayRadii", "m_SimdRodsAnim", "m_SimdSpringIntegrator",
            "m_SimdQuads", "m_SimdTris",
            "m_FreeNodes",
            "m_nQuadCount1", "m_nQuadCount2", "m_nTriCount1", "m_nTriCount2",
            "m_nSimdQuadCount1", "m_nSimdQuadCount2", "m_nSimdTriCount1", "m_nSimdTriCount2",
            "m_nReservedUint8", "m_nNodeBaseJiggleboneDependsCount",
            "m_nReserved",
            "m_nCollisionSphereInclusiveCount",
            "m_SimdFitMatrices", "m_nFitMatrixCount1", "m_nFitMatrixCount2",
            "m_nSimdFitMatrixCount1", "m_nSimdFitMatrixCount2",
            "m_DynNodeWindBases",
            "m_ReverseOffsets",
        ];

        static readonly HashSet<string> ParsedKeys =
        [
            "m_CtrlName", "m_SkelParents", "m_NodeInvMasses", "m_nNodeCount", "m_nStaticNodes",
            "m_nFirstPositionDrivenNode", "m_InitPose", "m_Quads", "m_Tris", "m_SourceElems",
            "m_HingeLimits", "m_KelagerBends", "m_VertexMapValues", "m_VertexMaps", "m_Rods",
            "m_NodeIntegrator", "m_NodeCollisionRadii", "m_WorldCollisionNodes", "m_WorldCollisionParams",
            "m_DynNodeFriction", "m_AnimStrayRadii", "m_FitMatrices", "m_Twists", "m_NodeBases",
            "m_TreeCollisionMasks",
            "m_CtrlOffsets", "m_CtrlSoftOffsets", "m_FitWeights", "m_TaperedCapsuleRigids", "m_BoxRigids",
            "m_SphereRigids", "m_AxialEdges", "m_Ropes", "m_nRopeCount", "m_FollowNodes",
            "m_LocalForce", "m_LocalRotation",
            "m_flInternalPressure", "m_flWindage", "m_flWindDrag", "m_flLocalForce", "m_flLocalRotation",
            "m_flAddWorldCollisionRadius", "m_flDefaultGravityScale", "m_flDefaultVelAirDrag",
            "m_flDefaultExpAirDrag", "m_flDefaultThreadStretch", "m_flDefaultSurfaceStretch", "m_flLocalDrag1",
            "m_nExtraIterations", "m_nExtraGoalIterations", "m_nExtraPressureIterations",
            "m_flRodVelocitySmoothRate", "m_nRodVelocitySmoothIterations", "m_nDynamicNodeFlags",
            "m_nStaticNodeFlags", "m_nRotLockStaticNodes", "m_flMotionSmoothCDT",

            "m_VertexSetNames", "m_DynNodeVertexSet", "m_LockToGoal", "m_LockToParent", "m_LegacyStretchForce",
            "m_CtrlOsOffsets", "m_AntiTunnelProbes", "m_AntiTunnelTargetNodes", "m_AntiTunnelBytecode",
            "m_SDFRigids", "m_GoalDampedSpringIntegrators", "m_DynKinLinks", "m_CollisionPlanes", "m_Effects",
            "m_MorphLayers", "m_MorphSetData", "m_SelfCollisionLayers", "m_NodeStrayBoxes",
            "m_TaperedCapsuleStretches", "m_SpringIntegrator", "m_RigidColliderPriorities", "m_JiggleBones",
            "m_BoneMergeLinks", "m_CollisionSpheres",
            "m_flDefaultTimeDilation", "m_flDefaultVolumetricSolveAmount", "m_flDefaultVelQuadAirDrag",
            "m_flDefaultExpQuadAirDrag", "m_flQuadVelocitySmoothRate", "m_nQuadVelocitySmoothIterations",
            "m_flDefaultVelRodAirDrag", "m_flDefaultExpRodAirDrag",
        ];

        /// <summary>
        /// Verifies every top-level <c>m_pFeModel</c> key is either parsed (<see cref="ParsedKeys"/>) or
        /// known-derived (<see cref="DerivedKeys"/>), so a compiler adding a new key is caught here instead
        /// of silently dropped. Debug-only.
        /// </summary>
        [Conditional("DEBUG")]
        static void AssertAllKeysAccountedFor(KVObject data)
        {
            foreach (var key in data.Keys)
            {
                Debug.Assert(ParsedKeys.Contains(key) || DerivedKeys.Contains(key),
                    $"FeModel key '{key}' is neither parsed nor in the derived-key list.");
            }
        }

        static T[] ReadArray<T>(KVObject data, string key, Func<KVObject, T> map)
        {
            var arr = data.GetArray(key);
            return arr is null ? [] : arr.Select(map).ToArray();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FeModel"/> class from a parsed <c>m_pFeModel</c> sub-object.
        /// </summary>
        public FeModel(KVObject data)
        {
            Data = data;
            CtrlNames = data.GetArray<string>("m_CtrlName") ?? [];
            SkelParents = (data.GetIntegerArray("m_SkelParents")).Select(static v => (int)v).ToArray();
            HasCompiledSkelParents = SkelParents.Length > 0;
            if (SkelParents.Length == 0)
            {
                SkelParents = BuildRopeParents(data);
            }
            NodeInvMasses = data.GetFloatArray("m_NodeInvMasses");
            NodeCount = data.GetInt32Property("m_nNodeCount");
            StaticNodeCount = data.GetInt32Property("m_nStaticNodes");
            HasCompiledFirstPositionDrivenNode = data.ContainsKey("m_nFirstPositionDrivenNode");
            FirstPositionDrivenNode = HasCompiledFirstPositionDrivenNode
                ? data.GetInt32Property("m_nFirstPositionDrivenNode")
                : DeriveFirstPositionDrivenNode(data, CtrlNames, NodeCount, StaticNodeCount);

            var initPose = data.GetArray("m_InitPose");
            InitPosePositions = initPose is null
                ? []
                : initPose.Select(static p => p.ToTransform().Position).ToArray();
            InitPoseRotations = initPose is null
                ? []
                : initPose.Select(static p => p.ToTransform().Rotation).ToArray();

            Quads = ReadNodeIndexArray(data, "m_Quads", 4);
            Tris = ReadNodeIndexArray(data, "m_Tris", 3);
            (SourceFaces, SourceSprings) = ReadSourceElems(data);

            var kelagerBends = new List<KelagerBend>();
            foreach (var bend in data.GetArray("m_KelagerBends") ?? [])
            {
                var nodes = bend.GetIntegerArray("nNode");
                var weights = bend.GetFloatArray("flWeight");
                if (nodes.Length >= 3 && weights.Length >= 3)
                {
                    kelagerBends.Add(new KelagerBend((int)nodes[0], (int)nodes[1], (int)nodes[2],
                        weights[0], weights[1], weights[2], bend.GetFloatProperty("flHeight0")));
                }
            }

            KelagerBends = kelagerBends;

            var mapValues = data.GetIntegerArray("m_VertexMapValues");
            var vertexMaps = new List<VertexMap>();
            foreach (var map in data.GetArray("m_VertexMaps") ?? [])
            {
                var count = map.GetInt32Property("nVertexCount");
                var offset = map.GetInt32Property("nMapOffset");
                var weights = new float[count];
                for (var i = 0; i < count && offset + i < mapValues.Length; i++)
                {
                    weights[i] = mapValues[offset + i] / 255f;
                }

                vertexMaps.Add(new VertexMap(
                    map.GetStringProperty("sName") ?? string.Empty,
                    map.GetUInt32Property("nNameHash"),
                    map.GetInt32Property("nVertexBase"),
                    count,
                    map.GetSubCollection("vCenterOfMass") is { } c ? c.ToVector3() : default,
                    weights,
                    map.GetFloatProperty("flVolumetricSolveStrength"),
                    map.GetInt32Property("nScaleSourceNode")));
            }

            VertexMaps = vertexMaps;
            ZeroVertexSelectionNames = [.. vertexMaps
                .Where(static map => map.VertexCount == 0 && map.Name.Length > 0)
                .Select(static map => map.Name)];

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
                }).Where(static r => r.NodeA >= 0 && r.NodeB >= 0 && r.NodeA != r.NodeB).ToArray();

            var integrators = data.GetArray("m_NodeIntegrator");
            NodeIntegrators = integrators is null
                ? []
                : integrators.Select(static o => new NodeIntegrator(
                    o.GetFloatProperty("flPointDamping"),
                    o.GetFloatProperty("flAnimationForceAttraction"),
                    o.GetFloatProperty("flAnimationVertexAttraction"),
                    o.GetFloatProperty("flGravity"))).ToArray();

            NodeCollisionRadii = data.GetFloatArray("m_NodeCollisionRadii");

            var treeMasks = data.ContainsKey("m_TreeCollisionMasks")
                ? data.GetIntegerArray("m_TreeCollisionMasks")
                : [];
            var dynamicNodeCount = NodeCount - StaticNodeCount;
            NodeCollisionMasks = dynamicNodeCount > 0 && treeMasks.Length == (2 * dynamicNodeCount) - 1
                ? [.. treeMasks.Take(dynamicNodeCount).Select(static v => (int)v)]
                : [];
            var worldCollisionOrder = data.ContainsKey("m_WorldCollisionNodes")
                ? data.GetIntegerArray("m_WorldCollisionNodes").Select(static v => (int)v).ToArray()
                : [];
            WorldCollisionNodes = worldCollisionOrder.ToHashSet();

            var worldFriction = new Dictionary<int, (float World, float Ground)>();
            foreach (var entry in data.GetArray("m_WorldCollisionParams") ?? [])
            {
                var begin = entry.GetInt32Property("nListBegin");
                var end = Math.Min(entry.GetInt32Property("nListEnd"), worldCollisionOrder.Length);
                var frictions = (entry.GetFloatProperty("flWorldFriction"), entry.GetFloatProperty("flGroundFriction"));
                for (var i = Math.Max(begin, 0); i < end; i++)
                {
                    worldFriction[worldCollisionOrder[i]] = frictions;
                }
            }

            WorldCollisionFriction = worldFriction;

            DynNodeFriction = data.GetFloatArray("m_DynNodeFriction");

            var strayRadii = new Dictionary<int, (float, float)>();
            if (data.GetArray("m_AnimStrayRadii") is { } strayArray)
            {
                foreach (var entry in strayArray)
                {
                    var nodes = entry.GetIntegerArray("nNode");
                    if (nodes.Length >= 2 && nodes[0] == nodes[1])
                    {
                        strayRadii[(int)nodes[0]] = (
                            entry.GetFloatProperty("flMaxDist"),
                            entry.GetFloatProperty("flRelaxationFactor"));
                    }
                }
            }

            AnimStrayRadii = strayRadii;

            var fitMatrices = data.GetArray("m_FitMatrices");
            FitMatrixNodes = fitMatrices is not null
                ? fitMatrices.Select(static o => o.GetInt32Property("nNode")).ToHashSet()
                : new HashSet<int>();

            var proxyFitNodes = new HashSet<int>();
            var fitTargets = new Dictionary<int, int[]>();
            if (fitMatrices is not null)
            {
                var fitRangeWeights = data.GetArray("m_FitWeights") ?? [];
                var fitRangeBegin = 0;
                foreach (var fit in fitMatrices)
                {
                    var fitRangeEnd = fit.GetInt32Property("nEnd");
                    var bone = fit.GetInt32Property("nNode");
                    var targets = new List<int>();
                    for (var i = fitRangeBegin; i < fitRangeEnd && i < fitRangeWeights.Count; i++)
                    {
                        var target = fitRangeWeights[i].GetInt32Property("nNode");
                        targets.Add(target);
                        if (target >= 0 && target < CtrlNames.Length && ParseProxyMeshIndex(CtrlNames[target]) >= 0)
                        {
                            proxyFitNodes.Add(bone);
                        }
                    }

                    fitTargets[bone] = [.. targets];
                    fitRangeBegin = fitRangeEnd;
                }
            }

            ProxyFitMatrixNodes = proxyFitNodes;
            FitMatrixTargets = fitTargets;

            var twistNodes = new HashSet<int>();
            var twistRecords = new List<TwistRecord>();
            var twistLinks = new HashSet<(int, int)>();
            var twistRelaxByLink = new Dictionary<(int, int), float>();
            var twistRelaxCopies = new Dictionary<(int, int), IReadOnlyList<float>>();
            if (data.GetArray("m_Twists") is { } twistsArray)
            {
                foreach (var entry in twistsArray)
                {
                    var relax = entry.GetFloatProperty("flTwistRelax");
                    var orient = entry.GetInt32Property("nNodeOrient");
                    var end = entry.GetInt32Property("nNodeEnd");
                    twistNodes.Add(orient);
                    twistNodes.Add(end);
                    twistRecords.Add(new TwistRecord(orient, end, relax, entry.GetFloatProperty("flSwingRelax")));
                    twistLinks.Add(orient < end ? (orient, end) : (end, orient));
                    twistRelaxByLink[(orient, end)] = relax;
                    twistOrientFallback.TryAdd(orient, relax);

                    if (!twistRelaxCopies.TryGetValue((orient, end), out var copies))
                    {
                        copies = new List<float>();
                        twistRelaxCopies[(orient, end)] = copies;
                    }

                    ((List<float>)copies).Add(relax);
                }

                foreach (var ((orient, end), relax) in twistRelaxByLink)
                {
                    if (relax != 0f)
                    {
                        continue;
                    }

                    if (!twistRelaxByLink.TryGetValue((end, orient), out var back))
                    {
                        relaxlessTwistOrients.Add(orient);
                    }
                    else if (back == 0f)
                    {
                        relaxlessTwistNodes.Add(orient);
                        relaxlessTwistNodes.Add(end);
                    }
                }
            }

            TwistNodes = twistNodes;
            TwistRecords = twistRecords;
            TwistLinks = twistLinks;
            TwistRelaxByLink = twistRelaxByLink;
            TwistRelaxCopies = twistRelaxCopies;

            var nodeBases = new Dictionary<int, NodeBasis>();
            var nodeBaseRecords = new List<(int Node, NodeBasis Basis)>();
            if (data.GetArray("m_NodeBases") is { } nodeBasesArray)
            {
                foreach (var entry in nodeBasesArray)
                {
                    var node = entry.GetInt32Property("nNode");
                    var basis = new NodeBasis(
                        entry.GetInt32Property("nNodeX0"),
                        entry.GetInt32Property("nNodeX1"),
                        entry.GetInt32Property("nNodeY0"),
                        entry.GetInt32Property("nNodeY1"));
                    nodeBaseRecords.Add((node, basis));
                    nodeBases[node] = basis;
                }
            }

            NodeBases = nodeBases;
            NodeBaseRecords = nodeBaseRecords;

            AntiTunnelProbes = ReadArray(data, "m_AntiTunnelProbes", static o => new AntiTunnelProbe(
                o.GetFloatProperty("flWeight"), o.GetUInt32Property("nFlags"), o.GetInt32Property("nProbeNode"),
                o.GetInt32Property("nCount"), o.GetInt32Property("nBegin"),
                o.GetFloatProperty("flActivationDistance"), o.GetFloatProperty("flCurvatureRadius"),
                o.GetFloatProperty("flBias")));
            AntiTunnelTargetNodes = data.GetIntegerArray("m_AntiTunnelTargetNodes").Select(static v => (int)v).ToArray();
            AntiTunnelBytecode = data.GetArray<uint>("m_AntiTunnelBytecode") ?? [];

            DynKinLinks = ReadArray(data, "m_DynKinLinks", static o => new DynKinLink(
                o.GetInt32Property("m_nParent"), o.GetInt32Property("m_nChild")));

            CollisionPlanes = ReadArray(data, "m_CollisionPlanes", static o =>
            {
                var plane = o.GetSubCollection("m_Plane");
                return new CollisionPlane(
                    o.GetInt32Property("nCtrlParent"), o.GetInt32Property("nChildNode"),
                    plane?.GetSubCollection("m_vNormal") is { } normal ? normal.ToVector3() : default,
                    plane?.GetFloatProperty("m_flOffset") ?? 0f,
                    o.GetFloatProperty("flStickiness"), o.GetFloatProperty("flStrength"));
            });

            SDFRigids = ReadArray(data, "m_SDFRigids", static o => new SDFRigid(
                o.GetSubCollection("vLocalMin") is { } lmin ? lmin.ToVector3() : default,
                o.GetSubCollection("vLocalMax") is { } lmax ? lmax.ToVector3() : default,
                o.GetFloatProperty("flBounciness"), o.GetInt32Property("nNode"),
                o.GetInt32Property("nCollisionMask"), o.GetInt32Property("nVertexMapIndex"),
                o.GetUInt32Property("nFlags"), o.GetFloatArray("m_Distances"),
                o.GetInt32Property("m_nWidth"), o.GetInt32Property("m_nHeight"), o.GetInt32Property("m_nDepth")));

            GoalDampedSpringIntegrators = data.GetArray<uint>("m_GoalDampedSpringIntegrators") ?? [];

            Effects = ReadArray(data, "m_Effects", static o => new Effect(
                o.GetStringProperty("sName") ?? string.Empty, o.GetUInt32Property("nNameHash"),
                o.GetInt32Property("nType"), o.GetSubCollection("m_Params")));

            MorphLayers = ReadArray(data, "m_MorphLayers", static o => new MorphLayer(
                o.GetStringProperty("m_Name") ?? string.Empty, o.GetUInt32Property("m_nNameHash"),
                o.GetIntegerArray("m_Nodes").Select(static v => (int)v).ToArray(),
                (o.GetArray("m_InitPos") ?? []).Select(static p => p.ToVector3()).ToArray(),
                o.GetFloatArray("m_Gravity"), o.GetFloatArray("m_GoalStrength"), o.GetFloatArray("m_GoalDamping"),
                o.GetUInt32Property("m_nFlags")));
            MorphSetData = data.GetArray<byte>("m_MorphSetData") ?? [];

            SelfCollisionLayers = ReadArray(data, "m_SelfCollisionLayers", static o => new SelfCollisionLayer(
                o.GetStringProperty("m_Name") ?? string.Empty,
                o.GetIntegerArray("m_Nodes").Select(static v => (int)v).ToArray(),
                o.GetFloatProperty("m_flParentReaction"), o.GetUInt32Property("m_nFlags"),
                o.GetArray<uint>("m_nEndIdx") ?? []));

            NodeStrayBoxes = ReadArray(data, "m_NodeStrayBoxes", static o =>
            {
                var nodes = o.GetIntegerArray("nNode");
                return new NodeStrayBox(
                    o.GetSubCollection("vMin") is { } smin ? smin.ToVector3() : default,
                    o.GetSubCollection("vMax") is { } smax ? smax.ToVector3() : default,
                    o.GetUInt32Property("nFlags"),
                    nodes.Length > 0 ? (int)nodes[0] : -1, nodes.Length > 1 ? (int)nodes[1] : -1);
            });

            TaperedCapsuleStretches = ReadArray(data, "m_TaperedCapsuleStretches", static o =>
            {
                var nodes = o.GetIntegerArray("nNode");
                var radii = o.GetFloatArray("flRadius");
                return new TaperedCapsuleStretch(
                    nodes.Length > 0 ? (int)nodes[0] : -1, nodes.Length > 1 ? (int)nodes[1] : -1,
                    o.GetInt32Property("nCollisionMask"),
                    radii.Length > 0 ? radii[0] : 0f, radii.Length > 1 ? radii[1] : 0f);
            });

            SpringIntegrators = ReadArray(data, "m_SpringIntegrator", static o =>
            {
                var nodes = o.GetIntegerArray("nNode");
                return new SpringIntegrator(
                    nodes.Length > 0 ? (int)nodes[0] : -1, nodes.Length > 1 ? (int)nodes[1] : -1,
                    o.GetFloatProperty("flSpringRestLength"), o.GetFloatProperty("flSpringConstant"),
                    o.GetFloatProperty("flSpringDamping"), o.GetFloatProperty("flNodeWeight0"));
            });

            RigidColliderPriorities = ReadArray(data, "m_RigidColliderPriorities", static o => new RigidColliderIndices(
                o.GetInt32Property("m_nTaperedCapsuleRigidIndex"), o.GetInt32Property("m_nSphereRigidIndex"),
                o.GetInt32Property("m_nBoxRigidIndex"), o.GetInt32Property("m_nSDFRigidIndex"),
                o.GetInt32Property("m_nCollisionPlaneIndex")));

            JiggleBones = ReadArray(data, "m_JiggleBones", static o =>
            {
                var bone = o.GetSubCollection("m_jiggleBone");
                return new IndexedJiggleBone(o.GetInt32Property("m_nNode"), unchecked((int)o.GetUInt32Property("m_nJiggleParent")),
                    bone is null ? default : new JiggleBone(
                        bone.GetUInt32Property("m_nFlags"), bone.GetFloatProperty("m_flLength"),
                        bone.GetFloatProperty("m_flTipMass"),
                        bone.GetFloatProperty("m_flYawStiffness"), bone.GetFloatProperty("m_flYawDamping"),
                        bone.GetFloatProperty("m_flPitchStiffness"), bone.GetFloatProperty("m_flPitchDamping"),
                        bone.GetFloatProperty("m_flAlongStiffness"), bone.GetFloatProperty("m_flAlongDamping"),
                        bone.GetFloatProperty("m_flAngleLimit"),
                        bone.GetFloatProperty("m_flMinYaw"), bone.GetFloatProperty("m_flMaxYaw"),
                        bone.GetFloatProperty("m_flYawFriction"), bone.GetFloatProperty("m_flYawBounce"),
                        bone.GetFloatProperty("m_flMinPitch"), bone.GetFloatProperty("m_flMaxPitch"),
                        bone.GetFloatProperty("m_flPitchFriction"), bone.GetFloatProperty("m_flPitchBounce"),
                        bone.GetFloatProperty("m_flBaseMass"), bone.GetFloatProperty("m_flBaseStiffness"),
                        bone.GetFloatProperty("m_flBaseDamping"),
                        bone.GetFloatProperty("m_flBaseMinLeft"), bone.GetFloatProperty("m_flBaseMaxLeft"),
                        bone.GetFloatProperty("m_flBaseLeftFriction"),
                        bone.GetFloatProperty("m_flBaseMinUp"), bone.GetFloatProperty("m_flBaseMaxUp"),
                        bone.GetFloatProperty("m_flBaseUpFriction"),
                        bone.GetFloatProperty("m_flBaseMinForward"), bone.GetFloatProperty("m_flBaseMaxForward"),
                        bone.GetFloatProperty("m_flBaseForwardFriction"),
                        bone.GetFloatProperty("m_flRadius0"), bone.GetFloatProperty("m_flRadius1"),
                        bone.GetSubCollection("m_vPoint0") is { } pt0 ? pt0.ToVector3() : default,
                        bone.GetSubCollection("m_vPoint1") is { } pt1 ? pt1.ToVector3() : default,
                        bone.GetInt32Property("m_nCollisionMask")));
            });

            BoneMergeLinks = ReadArray(data, "m_BoneMergeLinks", static o => new BoneMergeLink(
                o.GetUInt32Property("m_nParentHash"), o.GetInt32Property("m_nChildNode")));

            LockToParent = ReadArray(data, "m_LockToParent", static o => new LockToParentLink(
                o.GetSubCollection("vOffset") is { } offset ? offset.ToVector3() : default,
                o.GetInt32Property("nCtrlParent"), o.GetInt32Property("nCtrlChild")));
            LockToGoal = data.GetIntegerArray("m_LockToGoal").Select(static v => (int)v).ToArray();

            CtrlOsOffsets = ReadArray(data, "m_CtrlOsOffsets", static o => new CtrlOsOffset(
                o.GetInt32Property("nCtrlParent"), o.GetInt32Property("nCtrlChild")));

            CtrlOffsets = ReadArray(data, "m_CtrlOffsets", static o => new CtrlOffset(
                o.GetSubCollection("vOffset") is { } ctrlOffset ? ctrlOffset.ToVector3() : default,
                o.GetInt32Property("nCtrlParent"), o.GetInt32Property("nCtrlChild")));

            VertexSetNames = data.GetArray<uint>("m_VertexSetNames") ?? [];
            DynNodeVertexSet = data.GetArray<byte>("m_DynNodeVertexSet") ?? [];
            if (VertexMaps.Count == 0)
            {
                VertexMaps = BuildVertexMapsFromSets();
                vertexMapsFromSets = VertexMaps.Count > 0;
            }

            LegacyStretchForce = data.GetFloatArray("m_LegacyStretchForce");
            CollisionSpheres = data.GetArray("m_CollisionSpheres") ?? [];

            RecoveredSkinWeights = RecoverAuthoredSkinWeights(data, out var deferredOffsetWeights,
                out var unbackSolvedMeshes);
            DeferredOffsetSkinWeights = deferredOffsetWeights;
            UnbackSolvedProxyMeshes = unbackSolvedMeshes;
            RawGoalPaintNodes = BuildRawGoalPaintNodes();

            AssertAllKeysAccountedFor(data);
        }
    }
}
