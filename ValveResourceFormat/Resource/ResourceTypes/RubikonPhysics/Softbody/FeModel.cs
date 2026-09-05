using System.Diagnostics;
using System.Globalization;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    /// <summary>
    /// Soft-body (cloth) model embedded in a physics aggregate (<c>m_pFeModel</c>).
    /// </summary>
    /// <remarks>
    /// Parses the control nodes, surface and constraint arrays that editable ModelDoc cloth source is
    /// reconstructed from; the raw <see cref="Data"/> object is retained for keys surfaced lazily.
    /// <para>
    /// <c>Fe</c> is the prefix Valve puts on all ~60 structs of this family (<c>FeRodConstraint_t</c>,
    /// <c>FeQuad_t</c>, <c>FeNodeBase_t</c>, ...). Nothing in the shipped schema expands it. The family is
    /// distinct from the <c>Rn</c> types the rest of <see cref="RubikonPhysics"/> holds.
    /// </para>
    /// </remarks>
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
        /// Gets the index of the first position-driven node, i.e. the first bone the cloth back-solves
        /// rather than simulates. Equal to <see cref="NodeCount"/> when the compile has none. Compiles
        /// that predate <c>m_nFirstPositionDrivenNode</c> omit the key and the boundary is derived from
        /// the compiled arrays.
        /// </summary>
        public int FirstPositionDrivenNode { get; }

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

        /// <summary>
        /// Gets whether the cloth is built from a quad/tri surface rather than a pure rod network. A sheet
        /// exported without a surface is rebuilt into rods by the compiler; one exported with a surface
        /// keeps its faces as solve elements.
        /// </summary>
        public bool HasSurfaceElements => Quads.Length > 0 || Tris.Length > 0;

        /// <summary>
        /// Gets the structural distance constraints (<c>m_Rods</c>) between pairs of control nodes. They
        /// are not derivable from the <see cref="Quads"/>/<see cref="Tris"/> edges and diagonals, so they
        /// are re-declared directly as explicit ClothSpring nodes.
        /// </summary>
        public Rod[] Rods { get; }

        /// <summary>A single structural rod (from <c>m_Rods</c>).</summary>
        /// <param name="NodeA">First endpoint control-node index.</param>
        /// <param name="NodeB">Second endpoint control-node index.</param>
        /// <param name="MinDist">Minimum allowed distance (<c>flMinDist</c>).</param>
        /// <param name="MaxDist">Maximum allowed distance (<c>flMaxDist</c>).</param>
        /// <param name="Weight0">Blend weight (<c>flWeight0</c>): real per-rod data, but not re-authorable.
        /// <c>ClothSpring</c> registers no matching attribute, so an authored <c>weight0</c> is discarded
        /// and the rod compiles with the builder's own default of 0.5 while <c>MinDist</c>/<c>MaxDist</c>
        /// stay exact. Recorded here, not exposed as an export field.</param>
        /// <param name="RelaxationFactor">Relaxation factor (<c>flRelaxationFactor</c>): real per-rod data,
        /// not a fixed default. Not re-authorable through <c>ClothSpring</c> either.</param>
        public readonly record struct Rod(int NodeA, int NodeB, float MinDist, float MaxDist, float Weight0, float RelaxationFactor);

        /// <summary>
        /// Gets the explicit local orientation basis of certain nodes (<c>m_NodeBases</c>), keyed by
        /// control-node index. The compiler writes one for exactly the nodes with rod-graph degree &gt;= 2
        /// that are not a ClothChain joint; a chain joint derives its orientation from its own
        /// parent-child twist and bend physics instead. <c>qAdjust</c> is computed from the X0/X1/Y0/Y1
        /// references and has no authoring channel of its own, so only the four node references are
        /// re-authorable, through <c>node_base_x0/x1/y0/y1</c>.
        /// </summary>
        // TODO: a sheet node's basis is lost on re-export where the rod adjacency is identical but the
        // proxy DMX face order is not; the recompile then emits no basis at all for that node.
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
        /// Gets the world-collision radii (<c>m_NodeCollisionRadii</c>); empty for cloth that relies on
        /// goal attraction rather than per-node collision. Indexed by DYNAMIC node (control-node index
        /// minus <see cref="StaticNodeCount"/>) - static nodes carry no radius.
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

        /// <summary>
        /// Gets the control nodes driven by a back-solved fit matrix (<c>m_FitMatrices</c>) - bones whose
        /// orientation is derived from a driving/proxy mesh (<c>ClothProxyMeshFile.back_solve_joints</c>)
        /// rather than simulated directly. A model can have bone-chain cloth AND a proxy mesh that are
        /// fully INDEPENDENT of each other (proxy ships <c>back_solve_joints = false</c>): the presence of
        /// a proxy mesh does not by itself mean every bone chain is back-solved by it - check this set.
        /// </summary>
        public IReadOnlySet<int> FitMatrixNodes { get; }

        /// <summary>
        /// Gets the subset of <see cref="FitMatrixNodes"/> that a PROXY SHEET back-solves, i.e. whose
        /// <c>m_FitWeights</c> range covers a <c>$cloth_m&lt;N&gt;p&lt;S&gt;</c> vertex. A fit matrix is
        /// the compiler's orientation solve for a bone whose rotation it cannot read off a child joint,
        /// and which construct asked for it shows in what the fit is taken over: a proxy-driven bone fits
        /// over the sheet's own vertices, while a <c>ClothChain</c> joint fits over the chain's own
        /// <c>$cc</c> extrude ring and sibling joints. Only the former is driven THROUGH the proxy and
        /// must not also be emitted as a ClothChain.
        /// </summary>
        public IReadOnlySet<int> ProxyFitMatrixNodes { get; }

        /// <summary>
        /// Gets the control nodes each <c>m_FitMatrices</c> entry is fit over, from its own
        /// <c>m_FitWeights</c> range, keyed by the bone the fit drives.
        /// </summary>
        public IReadOnlyDictionary<int, int[]> FitMatrixTargets { get; }

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
        /// is assigned to the primary bone's nearest static real ancestor. Every fit matrix's vCenter is
        /// the weighted centroid of exactly these weights, so re-painting them reproduces the original fit
        /// transforms.
        /// <para>
        /// A vertex no fit matrix covers is recovered from <c>m_CtrlOffsets</c>/<c>m_CtrlSoftOffsets</c>
        /// alone, at scale 1. The compiler drops every authored influence below a fixed keep threshold
        /// and renormalizes the survivors before building the network, so the network's expansion is
        /// exactly the authored set the compiler itself acted on. Re-painting that expansion re-derives
        /// the same network: renormalizing only scales weights up, so nothing that cleared the threshold
        /// can fall back under it.
        /// </para>
        /// </summary>
        public IReadOnlyDictionary<int, (string Bone, float Weight)[]> RecoveredSkinWeights { get; private set; }

        /// <summary>
        /// Drops the recovered multi-influence skinning of pinned vertices whose primary anchor's
        /// skeleton parent never was a control node, on a model whose SHEET back-solves bones. Such a
        /// compile pulls the anchor's parent chain into the control set, so re-painting those pins
        /// registers a bone the original does not have. The affected pins keep the single-anchor
        /// skinning. A sheet that back-solves nothing registers no parent, and its pins keep every
        /// influence they ship with.
        /// </summary>
        public void PrunePinnedRecoveries(IReadOnlyDictionary<string, string?> boneParents)
        {
            if (ProxyFitMatrixNodes.Count == 0)
            {
                return;
            }

            var ctrls = new HashSet<string>(CtrlNames, StringComparer.OrdinalIgnoreCase);
            Dictionary<int, (string Bone, float Weight)[]>? updated = null;
            foreach (var (node, influences) in RecoveredSkinWeights)
            {
                if (!IsStatic(node) || influences.Length <= 1)
                {
                    continue;
                }

                var parent = boneParents.GetValueOrDefault(influences[0].Bone);
                if (parent is not null && !ctrls.Contains(parent))
                {
                    updated ??= new Dictionary<int, (string Bone, float Weight)[]>(RecoveredSkinWeights);
                    updated[node] = [influences[0] with { Weight = 1f }];
                }
            }

            if (updated is not null)
            {
                RecoveredSkinWeights = updated;
            }
        }

        /// <summary>
        /// Gets the control nodes participating in a twist constraint (<c>m_Twists</c>), i.e. whose
        /// ClothChain joint was authored with <c>twist_relax &gt; 0</c>. A chain whose joints all leave it
        /// at 0 compiles to a whole-chain <c>m_Ropes</c> fallback constraint instead, so re-declaring
        /// twist_relax as nonzero for exactly these nodes is what reproduces the twist network.
        /// </summary>
        public IReadOnlyDictionary<int, float> TwistNodes { get; }

        /// <summary>
        /// Gets the node pairs a twist constraint spans, unordered. A twist pair is generated by the
        /// authored <c>twist_relax</c> of the CHILD joint of that link alone, so a node appearing in one
        /// says nothing about its own joint - see <see cref="HasTwistToParent"/>.
        /// </summary>
        public IReadOnlySet<(int, int)> TwistLinks { get; }

        /// <summary>
        /// Gets whether a twist constraint spans <paramref name="node"/> and its chain parent
        /// <paramref name="parent"/>, which is what that joint's own <c>twist_relax</c> generates.
        /// </summary>
        public bool HasTwistToParent(int node, int parent)
            => parent >= 0 && TwistLinks.Contains(node < parent ? (node, parent) : (parent, node));

        /// <summary>
        /// Gets whether the joint at <paramref name="node"/> was authored with a non-zero
        /// <c>twist_relax</c>. A twist pair belongs to the CHILD of the link it spans, so an interior joint
        /// has to be matched against its own <paramref name="parent"/> - reading bare membership there
        /// gives every joint above a twisted one a spurious twist of its own. A chain ROOT has no parent
        /// link to match, and its authored value still steers the first link's relaxation, so membership
        /// is the right test there.
        /// </summary>
        public bool HasAuthoredTwist(int node, int parent)
            => parent >= 0 ? HasTwistToParent(node, parent) : TwistNodes.ContainsKey(node);

        /// <summary>
        /// Gets every parsed <c>m_Twists</c> entry's <c>flTwistRelax</c>, keyed by its directed
        /// (<c>nNodeOrient</c>, <c>nNodeEnd</c>) pair. The value at a directed entry is always the ORIENT
        /// node's own authored <c>twist_relax</c>, scaled by <see cref="TwistRelaxToParentFactor"/> when
        /// <c>end</c> is orient's own chain parent or by <see cref="TwistRelaxToChildFactor"/> when
        /// <c>end</c> is one of orient's own children. A joint that also generates an extrusion
        /// ring/center node carries a flat 0.5 on its OWN entry toward its parent, so
        /// <see cref="GetAuthoredTwistRelax"/> reads its entry toward the ring instead, which stays on
        /// the normal child-branch factor.
        /// </summary>
        public IReadOnlyDictionary<(int Orient, int End), float> TwistRelaxByLink { get; }

        // The two branch factors sum to 1.
        internal const float TwistRelaxToParentFactor = 0.618f;
        internal const float TwistRelaxToChildFactor = 0.382f;

        /// <summary>
        /// Recovers the joint's own authored <c>twist_relax</c> at <paramref name="node"/> from its
        /// directed <c>m_Twists</c> entries. Prefers the entry toward its own extrusion ring/center node
        /// (<paramref name="proxyNode"/>) when it has one, since a ring-generating joint's entry toward
        /// its own chain parent is overridden by the compiler to a flat 0.5 regardless of the authored
        /// value. Falls back to the entry toward <paramref name="parent"/>, and for a chain root, which
        /// has no parent-ward entry, to any entry naming it as orient.
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

        private readonly Dictionary<int, float> twistOrientFallback = [];

        // The cloth_drag paint compiles to flPointDamping = paint * 30.
        internal const float ClothDragPointDampingScale = 30f;

        // Base gravity acceleration (inches/s^2) that a source gravity_z of 1.0 maps to; used to turn the
        // compiled per-node flGravity back into the source gravity_z scale (ClothChain joints and ClothNode).
        internal const float ClothSourceBaseGravity = 360f;

        // Outside this range the compiler skips the attraction solve and writes goal_damping through
        // unchanged, so the inverse is the identity.
        internal const float GoalDampingSolveMaxAttraction = 0.9999f;
        internal const float GoalDampingSolveMinAttraction = 0.0001f;

        /// <summary>
        /// Recovers the source <c>goal_strength</c> from a node's compiled
        /// <c>flAnimationForceAttraction</c>, which the compiler writes as the cube of it.
        /// </summary>
        public static float GoalStrengthFromAttraction(float forceAttraction)
            => MathF.Cbrt(Math.Clamp(forceAttraction, 0f, 1f));

        /// <summary>
        /// Recovers the source <c>goal_damping</c> from a node's compiled attractions, inverting the
        /// builder's <c>va = 1 - ((1-fa) / (sqrt((1-fa)*fa + d*d) + d))^2 * fa</c>, where <c>fa</c> is
        /// <c>flAnimationForceAttraction</c> and <c>d</c> the source damping. Legacy nodes compiled with an
        /// out-of-range attraction clamp to the strongest damping the modern solver can express.
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

        // The cloth_animation_force_attract / cloth_animation_attract paints compile into
        // flAnimationForceAttraction / flAnimationVertexAttraction at 30x, the same scale cloth_drag uses,
        // with no clamp at 1.
        internal const float ClothRawGoalScale = 30f;

        // The m_n*NodeFlags bits a node's own goal values raise: 0x80 on the goal-damped integrator,
        // 0x200/0x400 on the raw one.
        const uint NodeFlagGoalAttraction = 0x80;
        const uint NodeFlagRawForceAttraction = 0x200;
        const uint NodeFlagRawVertexAttraction = 0x400;

        /// <summary>
        /// Gets whether <paramref name="node"/> compiled on the goal-damped spring integrator rather than
        /// the raw one. <see cref="GoalDampedSpringIntegrators"/> carries one bit per dynamic node and is
        /// populated only for a model holding both kinds; otherwise the node's own band flags name the one
        /// kind present, and a band holding both is decided from the node's values - see
        /// <see cref="GoalSolveCanProduce"/>.
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

        /// <summary>
        /// Whether the goal-damped solve can reach this attraction pair: both halves inside the 0..1 paint
        /// range, and the vertex attraction at or above the force attraction, which the solve only lifts.
        /// Outside its own solve range (<see cref="GoalDampingSolveMinAttraction"/>,
        /// <see cref="GoalDampingSolveMaxAttraction"/>) the compiler writes the damping through unchanged,
        /// so the two halves are then unrelated.
        /// </summary>
        static bool GoalSolveCanProduce(float forceAttraction, float vertexAttraction)
        {
            if (forceAttraction is < 0f or > 1f || vertexAttraction is < 0f or > 1f)
            {
                return false;
            }

            return forceAttraction is >= GoalDampingSolveMaxAttraction or < GoalDampingSolveMinAttraction
                || vertexAttraction >= forceAttraction - 1e-4f;
        }

        // Rope cloth ships no m_SkelParents. Two records of which node follows which survive: m_Ropes (its
        // first m_nRopeCount entries are the exclusive end offsets of the ordered node runs that follow)
        // and m_FollowNodes, an explicit parent/child pair per follower.
        //
        // Three other arrays look like a hierarchy and are not. m_CtrlOsOffsets pairs the two columns of a
        // strip, and m_Rods joins them as well, so following either one parents a node onto its own
        // sibling; the second column is generated by the extrude rather than being a skeleton bone, so it
        // is not nameable as a chain joint. m_CtrlOffsets maps a proxy vertex to the bone it back-solves,
        // not one node to another.
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

            return parented ? parents : [];
        }

        // Records one more expected rod on an unordered node pair, carrying the relaxation factor the
        // chain will compile onto it (NaN where no factor is known).
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
        /// The spans <paramref name="chains"/> rebuild by themselves, one entry per rod they will emit and
        /// each carrying that rod's expected relaxation factor: a joint's rod to its parent, plus a
        /// grandparent/great-grandparent rod where its bend or torsion spring is set, all repeated once per
        /// extra solver iteration and, again, doubled where the joint carries a suspender.
        /// </summary>
        /// <remarks>
        /// Each of the three sliders lands verbatim on the rod it generates, scaled by the model's own
        /// <c>default_stretch</c> (see <c>BuildBoneChains</c>), so the factor is recoverable here without
        /// re-reading the compiled rods. It is what tells a chain's own copy of a doubled pair from the
        /// other constraint sharing it.
        /// </remarks>
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
                        // A zero slider builds no rod at all: the compiler rescales every authored rod's
                        // relaxation and drops the ones that fall to 0.001 or below.
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
                    }

                    // A suspender's companion always lands on the pair (joint, chain root) - the SAME
                    // pair as the plain parent link only when the root happens to be this joint's
                    // parent (RootSuspenderValue's own remarks), otherwise a pair extra_iterations never
                    // touches at all. Its factor is the authored value, unscaled.
                    if (joint.Suspender != 0f)
                    {
                        ExpectPair(generated, rootNode, joint.Node, joint.Suspender);
                    }
                }
            }

            return generated;
        }

        /// <summary>
        /// The two-corner source elements that describe an authored <c>ClothSpring</c> nothing else in the
        /// export re-declares: a tie between two extruded chain rings that no chain itself spans.
        /// <para>
        /// A two-corner element is NOT by itself evidence of an authored spring. An old-era rope chain
        /// records its own parent-child links as two-corner elements, and a free <c>$cloth_node_</c>
        /// records its tie to the bone it hangs off. Both of those are already emitted by the chain and
        /// by the free-node writer respectively, so re-declaring one duplicates a rod and leaves every
        /// node it touches too heavy. Restricting to <c>$cc</c> ring endpoints keeps the cross-chain ties
        /// nothing else reaches; <see cref="GetUngeneratedRods"/>'s own emitter skips proxy-named nodes.
        /// </para>
        /// </summary>
        public List<(int, int)> GetAuthoredSourceSprings(List<BoneChain> chains)
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

            // A chain's own joint is a valid spring endpoint, so a two-corner element between two of them
            // is an authored spring like any other. A sheet vertex is not: naming one the compile does not
            // create fails the whole compile.
            bool IsEndpoint(int node) => IsChainRing(node) || joints.Contains(node);

            var spanned = ChainGeneratedSpans(chains);
            var authored = new List<(int, int)>(SourceSprings.Length);
            foreach (var (a, b) in SourceSprings)
            {
                if (IsEndpoint(a) && IsEndpoint(b) && !spanned.ContainsKey(a < b ? (a, b) : (b, a)))
                {
                    authored.Add((a, b));
                }
            }

            return authored;
        }

        /// <summary>
        /// Returns the rods that <paramref name="chains"/> will not regenerate on their own. A chain emits
        /// one rod per joint to its parent (plus a grandparent/great-grandparent rod where the joint's bend
        /// or torsion spring is set), but a model can carry extra copies of those spans; each surplus copy
        /// has to be re-declared as its own spring or the nodes come out too light.
        /// </summary>
        /// <remarks>
        /// Which entry of a pair the chain regenerates is decided by VALUE, not by array position. Every
        /// slider-driven chain rod pins <c>flMinDist</c> to <c>flMaxDist</c> and carries the slider it came
        /// from, so where one pair holds a chain rod AND a separate constraint - a self-collision cluster's
        /// asymmetric tie, a second declaration's own copy at a different relaxation - taking whichever
        /// came first hands the chain's own rod back as surplus and re-declares it, while the copy actually
        /// worth recovering is dropped. Rigidity outranks the relaxation match, and equally good candidates
        /// keep array order, so a pair whose copies are identical is attributed exactly as before.
        /// </remarks>
        public List<Rod> GetUngeneratedRods(List<BoneChain> chains)
        {
            var generated = ChainGeneratedSpans(chains);

            // AddClothSourceSprings re-declares these already, at a relaxation this does not model.
            foreach (var (a, b) in GetAuthoredSourceSprings(chains))
            {
                ExpectPair(generated, a, b, float.NaN);
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
        /// Gets the authored stray-radius stretchiness of <paramref name="node"/>: the complement of the
        /// compiled relaxation factor, 0 for a node with no stray radius. A fully stretchy constraint
        /// relaxes to nothing and the compiler drops it, so the two are not interchangeable - writing the
        /// relaxation factor into the authored key deletes the constraint it was recovered from.
        /// </summary>
        public float GetStrayStretchiness(int node)
            => AnimStrayRadii.TryGetValue(node, out var stray) ? 1f - stray.RelaxationFactor : 0f;

        /// <summary>
        /// Gets whether <paramref name="node"/> keeps its rotation free. Static nodes are ordered
        /// rotation-locked first, so the lock is exactly the nodes below
        /// <see cref="RotationLockedStaticNodeCount"/>.
        /// </summary>
        public bool AllowsRotation(int node) => node >= RotationLockedStaticNodeCount;

        /// <summary>
        /// Whether the node is position-driven (a bone the cloth back-solves rather than simulates).
        /// Compiles that predate <c>m_nFirstPositionDrivenNode</c> omit the key, and
        /// <see cref="FirstPositionDrivenNode"/> then carries the boundary derived from the compiled
        /// arrays instead.
        /// </summary>
        public bool IsPositionDriven(int node) => node >= FirstPositionDrivenNode;

        // The compiler sorts position-driven nodes last within each band and publishes the boundary as
        // the longest all-position-driven suffix that stops at the static count. A node is position
        // driven when the cloth back-solves it: a fit matrix target, a reverse-offset bone, or a chain
        // joint the compiler extruded a two-or-more-sided "$cc" ring around.
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
        /// Gets whether <paramref name="node"/> was authored with <c>lock_translation</c>.
        /// <para>
        /// The compiler locks a node that carries fit influences to its parent when that parent is itself
        /// simulated or free-rotating, and to its goal otherwise, but only reaches that decision for a node
        /// that is either NOT SIMULATED or carries <c>lock_translation</c>. So a locked node that is still
        /// simulated proves the key, while an unsimulated one reaches the same lock without it.
        /// </para>
        /// </summary>
        public bool LocksTranslation(int node)
            => IsLockedToParent(node) || (IsLockedToGoal(node) && !IsStatic(node));

        /// <summary>
        /// Recovers the per-vertex normal of a proxy sheet from the compiled rest poses.
        /// <para>
        /// The cloth importer derives each proxy vertex's rest ORIENTATION from that vertex's normal and
        /// nothing else: the compiled <c>m_InitPose</c> rotation is the frame whose local +Z is the vertex
        /// normal, unaffected by the skinning, the UVs or the mesh dag. So the normal a sheet ships fixes
        /// the rest orientation of every node it creates, and that axis is recoverable from the pose.
        /// </para>
        /// </summary>
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
        /// Recovers the <c>cloth_mass</c> paint of an authored-face proxy sheet, or null when the sheet
        /// carries none (or none can be recovered).
        /// <para>
        /// The compiler gives a node the mass
        /// <c>G * mass^2 + expf(paint * cloth_mass_scale)</c>, where <c>G</c> is the geometric term
        /// <see cref="GeometricNodeMasses"/> derives and <c>mass</c> is the node's authored mass
        /// multiplier, which the compiler applies once while balancing the global multipliers and once
        /// more when it combines the terms. Every cloth node this export writes carries the default
        /// multiplier 1, so the paint is read straight off what the shipped mass carries beyond the
        /// geometric term, and the exponential is there only when the mesh ships a <c>cloth_mass</c>
        /// stream at all.
        /// </para>
        /// <para>
        /// Only a sheet exported with its AUTHORED faces qualifies, and within it only a vertex the
        /// authored topology reaches. A synthesised triangulation compiles to a network of its own, so
        /// the shipped mass and the geometric term would describe different surfaces and their
        /// difference would measure that gap rather than any paint.
        /// </para>
        /// </summary>
        public float[]? RecoverMassPaint(ProxyMesh proxy)
        {
            if (!proxy.UsesAuthoredFaces)
            {
                return null;
            }

            var count = proxy.Positions.Length;
            var geometric = GeometricNodeMasses();
            var paint = new float[count];
            var painted = 0;
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

                // Outside the band the node's mass is not this surface's mass plus an exponential, so it
                // has no paint to read: below it the faces already account for more than the node weighs,
                // above it the term is past anything expf of a painted value reaches.
                if (residual <= MinRecoverableMassPaintTerm || residual > MaxRecoverableMassPaintTerm)
                {
                    clamped++;
                    continue;
                }

                paint[v] = MathF.Log(residual);
                painted++;
            }

            // The exponential is present or absent for the whole mesh, so a handful of nodes claiming it
            // against a majority that cannot is a mis-predicted geometric term, not a paint layer.
            return painted > clamped ? paint : null;
        }

        /// <summary>
        /// Recovers the authored <c>mass</c> multiplier of a cloth node, or null when it is the default 1
        /// or cannot be read. The compiler squares the multiplier into the node's mass
        /// (<c>G * mass^2</c>, see <see cref="RecoverMassPaint"/>), so it is the square root of what the
        /// shipped mass carries over the geometric term.
        /// <para>
        /// Read only where the geometric term is known exactly: a node that is an endpoint of no rod, whose
        /// whole term is its elements and the volumetric selections covering it, or any node of a cloth
        /// with no proxy sheet, where the rods that weighed are told apart from the compiler's own by
        /// <see cref="GeometricNodeMassesWithRods"/>. A rod endpoint on a sheet is not read.
        /// </para>
        /// </summary>
        public float? RecoverMassMultiplier(int node)
        {
            return MassMultiplierOf(node) is { } multiplier && MathF.Abs(multiplier - 1f) > MassMultiplierTolerance
                ? multiplier
                : null;
        }

        /// <summary>
        /// Recovers the authored <c>mass</c> of a chain joint from the joint's own node and the ring nodes
        /// the compiler extrudes from it, which take the joint's multiplier. Null when it is the default 1,
        /// when no node of the joint can be read, or when the nodes that can disagree.
        /// </summary>
        public float? RecoverJointMassMultiplier(int joint)
        {
            float? multiplier = null;
            foreach (var node in JointMassNodes(joint))
            {
                if (IsStatic(node) || NodeInvMasses[node] == 1f)
                {
                    continue;
                }

                if (MassMultiplierOf(node) is not { } nodeMultiplier)
                {
                    return null;
                }

                if (multiplier is { } first && MathF.Abs(nodeMultiplier - first) > MassMultiplierTolerance * first)
                {
                    return null;
                }

                multiplier ??= nodeMultiplier;
            }

            return multiplier is { } value && MathF.Abs(value - 1f) > MassMultiplierTolerance ? value : null;
        }

        float? MassMultiplierOf(int node)
        {
            if (node < 0 || node >= NodeInvMasses.Length)
            {
                return null;
            }

            // invMass 1.0 is the compiler's sentinel for a node that weighed nothing.
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

        // How far a recovered multiplier may sit from the default, or two readings of one joint from each
        // other, before it is written out.
        const float MassMultiplierTolerance = 1e-3f;

        /// <summary>
        /// The mass the compiler derives from the cloth's own geometry, per control node.
        /// <para>
        /// Every element credits both ends of each of its own corner pairs with 4 per unit of rest
        /// length, counted once per element and not once per pair, so a triangle - which the compiler
        /// stores as <c>(n0,n1,n2,n2)</c> - credits its <c>n0n2</c> and <c>n1n2</c> pairs twice and its
        /// <c>n0n1</c> pair once. The elements are taken as they stood before the over-bent quads were
        /// split into triangle pairs, which is the set the mass pass ran over.
        /// </para>
        /// <para>
        /// A rod credits both its ends with 8 per unit of rest length, but only the rods the cloth was
        /// built from weigh anything - the ones the compiler derives from the surface join the network
        /// after the mass pass. The shipped <c>m_Rods</c> mixes the two and cannot be used, so the
        /// authored rods are taken from the authored elements that did not survive as solve elements,
        /// which is what a <c>cloth_make_rods</c> region compiles to.
        /// </para>
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
        /// The geometric masses of a cloth with no proxy sheet, where a shipped rod weighs 8 per unit of
        /// rest length at both ends unless the compiler derived it from the surface: the bend rods it
        /// builds over the shared edges of the declared elements join the network after the mass pass, and
        /// so does an unbounded rod, whose <see cref="UnboundedRodDistance"/> maximum only that pass
        /// writes. Everything else - explicit springs, a ring's own sides, the rods along a chain -
        /// was in the network when the masses were taken, whatever distances it carries.
        /// <para>
        /// <see cref="PredictBendRods"/> over the declared elements is what names the derived pairs, and
        /// a pair is spent once, so where the surface and a chain both rod the same two nodes the chain's
        /// copy still weighs.
        /// </para>
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

            var derived = PredictBendRods(cycles, IsStatic);

            foreach (var rod in Rods)
            {
                var (a, b) = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
                if (a == b || a < 0 || b >= mass.Length
                    || rod.MaxDist >= UnboundedRodDistance || derived.Remove((a, b)))
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
        /// Credits both ends of every authored rod with 8 per unit of rest length. The authored rods are
        /// the DISTINCT corner pairs (edges and diagonals) of the authored elements the compile did not
        /// keep as solve elements, each pair counted once however many of them share it.
        /// </summary>
        void AddAuthoredRodNodeMasses(float[] mass, List<int[]> elements)
        {
            var solved = new HashSet<(int, int, int, int)>(elements.Count);
            foreach (var element in elements)
            {
                solved.Add(CornerKey(element));
            }

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
                        if (a == b || a < 0 || b >= mass.Length)
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
        /// Recovers the <c>cloth_stray_radius</c> paint of a proxy sheet from <c>m_AnimStrayRadii</c>, or
        /// null when no vertex of the sheet is stray-constrained. The compiled <c>flMaxDist</c> is the
        /// painted distance itself, the same value a ClothChain joint's <c>stray_radius</c> carries, so a
        /// sheet that ships no stream compiles with the whole array empty. A vertex whose node is covered
        /// by an <see cref="IndependentBoneChains"/> chain (the chain's own joints, or the "$cc" ring
        /// proxies the compiler auto-generates from them) is skipped: that chain's <c>MakeClothJoint</c>
        /// KV already carries the value, and the surface reconstruction can still reference the same node
        /// as a quad corner, which would otherwise double-paint it onto a second, compiler-synthesised
        /// copy of the node.
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
        /// Gets the nodes an independent <see cref="IndependentBoneChains"/> chain already owns: its own
        /// joints, plus any <c>$cc&lt;bone&gt;_&lt;n&gt;</c> ring proxy the compiler auto-generates from
        /// one of them (see <see cref="BuildProxyMeshes"/> for how those names arise).
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

        // Mass the compiler credits both ends of an element corner pair with, per unit of rest length.
        const float ElementMassPerUnitLength = 4f;

        // Mass the compiler credits both ends of an authored rod with, per unit of rest length.
        const float RodMassPerUnitLength = 8f;

        // Mass a volumetrically solved selection credits its nodes with, per unit of bounding-box extent.
        const float VolumetricMassPerUnitExtent = 12f;

        // The solve strength a selection needs before it weighs anything at all.
        const float MinVolumetricSolveStrength = 1.1920929e-7f;

        // The band a node's exponential mass term, expf(paint * scale), has to fall in to be read back as
        // paint. A mesh that ships no stream leaves a residual of a float32 ulp of its own mass, so the
        // lower bound separates that from the smallest term a stream can carry, e^0 = 1. The upper bound
        // rejects a mass no exponential explains.
        const float MinRecoverableMassPaintTerm = 0.05f;
        const float MaxRecoverableMassPaintTerm = 1e6f;

        /// <summary>Gets the friction painted on <paramref name="node"/>, or 0 when it has none.</summary>
        public float GetNodeFriction(int node) => DynamicNodeValue(DynNodeFriction, node);

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
        // Tracks m_flDefaultSurfaceStretch whatever the shear is; MakeClothParams reads the shear off the
        // rod relaxation factors instead.
        public float DefaultThreadStretch => Data.GetFloatProperty("m_flDefaultThreadStretch");
        public float DefaultSurfaceStretch => Data.GetFloatProperty("m_flDefaultSurfaceStretch");
        public float LocalDrag1 => Data.GetFloatProperty("m_flLocalDrag1");
        public int ExtraIterations => Data.GetInt32Property("m_nExtraIterations");
        public int ExtraGoalIterations => Data.GetInt32Property("m_nExtraGoalIterations");
        public int ExtraPressureIterations => Data.GetInt32Property("m_nExtraPressureIterations");
        public float VelocitySmoothRate => Data.GetFloatProperty("m_flRodVelocitySmoothRate");
        public int VelocitySmoothIterations => Data.GetInt32Property("m_nRodVelocitySmoothIterations");
        public uint DynamicNodeFlags => Data.GetUInt32Property("m_nDynamicNodeFlags");

        // Both words are derived summaries the compiler ORs over its own node population - the static half
        // over nodes [0, StaticNodeCount), the dynamic half over the rest - from one shared six-predicate
        // table over each node's own values. Neither carries an authored key of its own, so both come back
        // once the per-node values and integrator kinds do; UsesGoalDampedIntegrator reads the bits that
        // name a node's integrator, and MakeClothParams reads the dynamic-only ClothParams bits.
        public uint StaticNodeFlags => Data.GetUInt32Property("m_nStaticNodeFlags");
        public int RotationLockedStaticNodeCount => Data.GetInt32Property("m_nRotLockStaticNodes");
        public float MotionSmoothCdt => Data.GetFloatProperty("m_flMotionSmoothCDT");

        // The scalars below have no counterpart among the keys CModelDocClothParams registers, so the
        // compiler re-derives them and MakeClothParams emits none of them. Older-era compiles still carry
        // non-zero values for them.
        public float DefaultTimeDilation => Data.GetFloatProperty("m_flDefaultTimeDilation");
        public float DefaultVolumetricSolveAmount => Data.GetFloatProperty("m_flDefaultVolumetricSolveAmount");
        public float DefaultVelQuadAirDrag => Data.GetFloatProperty("m_flDefaultVelQuadAirDrag");
        public float DefaultExpQuadAirDrag => Data.GetFloatProperty("m_flDefaultExpQuadAirDrag");
        public float DefaultVelRodAirDrag => Data.GetFloatProperty("m_flDefaultVelRodAirDrag");
        public float DefaultExpRodAirDrag => Data.GetFloatProperty("m_flDefaultExpRodAirDrag");
        public float QuadVelocitySmoothRate => Data.GetFloatProperty("m_flQuadVelocitySmoothRate");
        public int QuadVelocitySmoothIterations => Data.GetInt32Property("m_nQuadVelocitySmoothIterations");
#pragma warning restore CS1591

        /// <summary>
        /// Gets whether the cloth carries a per-node local force or rotation, which is what the source's
        /// use-per-node-local-force-and-rotation switch produces. The switch leaves no flag bit; the arrays
        /// existing at all is the trace.
        /// </summary>
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
        /// Recovers the <c>stiff_hinge</c> stiffness and its <c>stiff_hinge_angle</c> in degrees authored on
        /// the joint at <paramref name="jointNode"/>, or null when it carries no bend. A joint's stiff hinge
        /// bends its PARENT: the joint (or a proxy extruded from it) is the bend's first end, its parent the
        /// bent node and its grandparent the other end. The stiffness is spread over the bend weights
        /// as <c>stiffness * 3 * [-2*mMid, mEnd0, mEnd1] / (4*mMid + mEnd0 + mEnd1)</c>; the angle becomes
        /// the distance the bent node may reach from the triple's centroid,
        /// <c>sqrt(l0^2 + l1^2 - 2*l0*l1*cos(angle)) / 3</c>, floored at the rest distance - an angle the
        /// rest pose already exceeds leaves no trace and recovers as zero, which recompiles to the same
        /// floor. The joint's <c>motion_bias</c> comes back with it: a fully biased joint replaces the
        /// mass shares with the whole stiffness on one end, leaving the bent node weightless.
        /// </summary>
        public (float Stiffness, float Angle, float MotionBias)? GetStiffHinge(int jointNode)
        {
            foreach (var bend in KelagerBends)
            {
                var owner = bend.End0 >= 0 && bend.End0 < CtrlNames.Length && IsProxyNodeName(CtrlNames[bend.End0])
                    && bend.End0 < SkelParents.Length
                        ? SkelParents[bend.End0]
                        : -1;
                if (bend.End0 != jointNode && owner != jointNode)
                {
                    continue;
                }

                var midMass = InverseMassOf(bend.MidNode);
                var end0Mass = InverseMassOf(bend.End0);
                var end1Mass = InverseMassOf(bend.End1);
                var total = (4f * midMass) + end0Mass + end1Mass;
                if (total <= 0f)
                {
                    continue;
                }

                // A fully biased joint drops the mass share entirely and puts the whole stiffness on one
                // end, which is the only way a bend leaves a simulated node weightless.
                if (midMass > 0f && MathF.Abs(bend.MidWeight) < FullMotionBiasEpsilon
                    && MathF.Abs(bend.End0Weight) > FullMotionBiasEpsilon)
                {
                    return (Math.Clamp(bend.End0Weight / 3f, 0f, 1f), BendAngle(bend), 1f);
                }

                // Read the stiffness off the largest weight: a share whose node is pinned carries none of it.
                var shares = new[] { (-2f * midMass, bend.MidWeight), (end0Mass, bend.End0Weight), (end1Mass, bend.End1Weight) };
                var stiffness = 0f;
                var strongest = 0f;
                foreach (var (share, weight) in shares)
                {
                    if (MathF.Abs(share) > 1e-9f && MathF.Abs(weight) > strongest)
                    {
                        strongest = MathF.Abs(weight);
                        stiffness = weight * total / (3f * share);
                    }
                }

                if (stiffness <= 0f)
                {
                    continue;
                }

                return (Math.Clamp(stiffness, 0f, 1f), BendAngle(bend), 0f);
            }

            return null;
        }

        // Below this a bend weight is the compiler's own signed zero rather than a small real share.
        const float FullMotionBiasEpsilon = 1e-6f;

        float InverseMassOf(int node)
            => node >= 0 && node < NodeInvMasses.Length ? NodeInvMasses[node] : 0f;

        // Inverts the bend height back into the authored maximum bend angle, in degrees.
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
            if (bend.Height <= restHeight * 1.0001f || l0 <= 0f || l1 <= 0f)
            {
                return 0f;
            }

            var cosine = ((l0 * l0) + (l1 * l1) - (9f * bend.Height * bend.Height)) / (2f * l0 * l1);
            return float.RadiansToDegrees(MathF.Acos(Math.Clamp(cosine, -1f, 1f)));
        }

        // A hinged chain joint is anchored on a static node the compiler names after the joint's bone.
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

        /// <summary>
        /// Gets the hinge authored on the joint named <paramref name="boneName"/>, or null when it carries
        /// none. The axis is recovered from where the joint's own proxy ring ended up (the hinge is what
        /// orients that ring), and the limits from the hinge limit built over that ring - see
        /// <see cref="HingeLimitsOf"/>.
        /// </summary>
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

            // Half the span across the ring: the compiler lays the pair out at +/- this vector from the
            // joint, so both the direction and the width come back from it.
            var axis = (InitPosePositions[ring[1]] - InitPosePositions[ring[0]]) * 0.5f;
            if (axis.LengthSquared() <= 0f)
            {
                return null;
            }

            axis = BreakEndEffectorQuadTie(ring, axis);

            var (cw, ccw) = limit is { } hinge ? HingeLimitsOf(hinge) : (0f, 0f);
            return new ChainHinge(axis, cw, ccw);
        }

        // The hinge limit built over a joint's own ring, or null when the joint carries none. Only the
        // CHAIN ROOT gets a "$ha_" anchor node of its own - every joint further down the chain hinges
        // against the ring above it - so the limit over the ring is what marks a hinged joint.
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

        // The authored (limit_cw, limit_ccw) pair in degrees. The compiler keeps the range the pair spans
        // as flAngleCenter +/- flAngleExtents around the rest angle the joint's own geometry gives
        // (HingeRestAngle), clamping a negative limit_cw to zero first:
        //     flAngleCenter  = rest + (max(0, cw) - ccw) / 2
        //     flAngleExtents =        (max(0, cw) + ccw) / 2
        // so both limits come straight back out. Their SUM survives the clamp either way, which is what
        // the compiler gates the whole hinge on (a pair spanning a full turn drops the limit entirely).
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

            // Only the clamp's own rounding may move it: a solution genuinely outside the span means the
            // rest angle did not come out where the compiler put it, and the pair is not recoverable.
            return MathF.Abs(cw - solved) < 0.01f ? (cw, span - cw) : (0f, span);
        }

        // The angle the hinge's four rest-pose reference points make about the constrained ring's own axis,
        // which is where the compiler centres the limit range before the authored limits shift it. The two
        // parent-side references each blend the pair the hinge names by the same weights it stores.
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

        // The end-effector quad the compiler builds across a hinged joint takes its corners from the two
        // longest of the four ring-to-ring rest distances. A hinge axis exactly perpendicular to the tip
        // ring leaves all four the same length, so which pair the scan keeps comes down to rounding in the
        // ring it extruded. Tilting the axis a hundred-thousandth of a radian towards the tip ring's first
        // node settles the tie.
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

        /// <summary>Gets how many auto-generated proxy nodes the compiler extruded from a joint.</summary>
        public int ProxyCountOf(int jointNode) => ProxyRingOf(jointNode).Count;

        /// <summary>Gets whether a chain joint carries a hinge constraint.</summary>
        public bool IsHingedJoint(int jointNode)
        {
            var ring = ProxyRingOf(jointNode);
            return ring.Count >= 2 && HingeLimitOverRing(ring) is not null;
        }

        // A generated node hanging off a hinged joint - its ring and its hinge anchor alike - is rebuilt by
        // the ClothChain that carries the hinge, so a proxy sheet must leave it alone or the two drive it
        // twice and the sheet contributes a duplicate of every one.
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

        // The auto-generated proxy nodes extruded from a joint, in the order their names number them.
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
        /// The ring ($cc) nodes <see cref="ProxyRingOf"/> cannot find a joint for on a compile that carries
        /// no <c>m_SkelParents</c> entry linking them to it: a rope-parented chain synthesizes
        /// <see cref="SkelParents"/> from <c>m_Ropes</c>/<c>m_FollowNodes</c> (<see cref="BuildRopeParents"/>),
        /// which walks the joints' own bone-to-bone links but never extends to each joint's own extruded
        /// ring, so every ring on such a compile reads back empty. Recovered instead from the source's own
        /// authored topology: <see cref="SourceFaces"/> still records a face (edge or diagonal) between two
        /// ring nodes even when nothing in the compiled file ties either one to its joint.
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
        /// Returns whether a FIXED-LENGTH rod still spans a parent-child LINK of <paramref name="chain"/>.
        /// A rigid hinge replaces that link with a quad, so a hinged chain that kept the link's rods was
        /// authored with a soft hinge link instead. Both conditions are needed to tell the two apart: the
        /// stiffness network a chain carries on top of a rigid hinge either reaches further along the chain
        /// (a joint to its grandparent) or leaves its maximum at <see cref="UnboundedRodDistance"/>.
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
        /// Returns whether the chains carry the extra bend network <c>add_bend_only_rods</c> spans (see
        /// <c>MakeClothParams</c>). Every rod a chain builds on its own is fixed-length, so a rod left
        /// unbounded between two of the nodes a chain generated can only have come from that switch - and
        /// it is the only way to get them back, since a generated name is not a valid spring endpoint.
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
        /// Returns whether the chains carry the extra bend network <c>add_stiffness_rods</c> spans (see
        /// <c>MakeClothParams</c>). A chain pins every rod it builds itself to an exact length, so a rod
        /// left free to move between a minimum and a maximum, between two of the nodes a chain generated,
        /// can only have come from that switch - and it is the only way to get them back, since a
        /// generated name is not a valid spring endpoint.
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
                && generated.Contains(rod.NodeA) && generated.Contains(rod.NodeB));
        }

        /// <summary>The maximum length a rod that is not length-limited at all is given.</summary>
        public const float UnboundedRodDistance = 16384f;

        /// <summary>Gets the named vertex selections the cloth carries, empty when it has none.</summary>
        public IReadOnlyList<VertexMap> VertexMaps { get; } = [];

        /// <summary>
        /// The name a selection recovered from <see cref="VertexSetNames"/> is exported under. Only the
        /// hash of the authored name survives compilation, so the name is made up; the export paints this
        /// name and references it from the same effects and joints, which is what pairs the two back up.
        /// </summary>
        static string SynthesizedVertexSetName(int set)
            => string.Create(CultureInfo.InvariantCulture, $"vertex_set_{set}");

        /// <summary>
        /// Rebuilds the named selections from the vertex-set registration
        /// (<see cref="VertexSetNames"/> paired with <see cref="DynNodeVertexSet"/>), which is the only
        /// form older compiles carry them in - those ship no <c>m_VertexMaps</c> at all. One set index per
        /// dynamic node, so a node belongs to exactly the one set its index names.
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

        /// <summary>A named vertex selection, used to target cloth effects and joint vertex maps.</summary>
        /// <param name="Name">The authored selection name.</param>
        /// <param name="NameHash">The hash the compiler keys the selection by.</param>
        /// <param name="VertexBase">The first control node the selection covers.</param>
        /// <param name="VertexCount">How many consecutive control nodes it covers.</param>
        /// <param name="CenterOfMass">The selection's centre of mass.</param>
        /// <param name="Weights">
        /// How strongly each covered node belongs to the selection, 0..1, indexed from
        /// <paramref name="VertexBase"/>.
        /// </param>
        /// <param name="VolumetricSolveStrength">
        /// How strongly the selection is solved as a volume rather than a surface. One that is solved
        /// volumetrically at all also weighs what its extent gives it (see
        /// <see cref="GeometricNodeMasses"/>).
        /// </param>
        /// <param name="ScaleSourceNode">
        /// The control node whose scale the selection follows, or -1 when it follows none.
        /// </param>
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
        /// Gets the vertex selections <paramref name="node"/> belongs to, in the
        /// <c>name[=weight],name[=weight]</c> form a joint's <c>vertex_map</c> takes, or null when it
        /// belongs to none. Selections overlap freely - a skirt node is typically in both
        /// <c>skirt_vm</c> and <c>skirt_l_vm</c> - so the list form is what lets a joint join all of them
        /// rather than only the strongest. A membership weight is only written out when it is not the
        /// full 1.0 the bare name already means.
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
        /// Strips the optional <c>=weight</c> suffix off one entry of a <see cref="GetVertexMapNames"/>
        /// list, leaving the bare selection name every key that merely REFERENCES a selection - a cloth
        /// effect's <c>vertex_map</c>, a collision shape's, a <c>ClothVertexMap</c> container's own name -
        /// spells it by.
        /// </summary>
        public static string VertexMapName(string entry)
        {
            var weight = entry.IndexOf('=', StringComparison.Ordinal);
            return weight < 0 ? entry : entry[..weight];
        }

        /// <summary>
        /// Gets the selection a <c>ClothVertexMap</c> around <paramref name="proxy"/> should carry: one
        /// covering exactly the sheet's SIMULATED nodes and not registered as a vertex set of its own, or
        /// null when no selection qualifies. The container restores the same <c>m_VertexMaps</c> entry the
        /// sheet's <c>cloth_vertex_set</c> paint would, without also registering the dynamic vertex set
        /// (<c>m_VertexSetNames</c>/<c>m_DynNodeVertexSet</c>) that the paint brings with it - so the caller
        /// paints every OTHER selection and lets this one come from the container.
        /// <para>
        /// A selection the original registered as a vertex set is never a candidate: that registration
        /// exists only because the sheet painted it.
        /// </para>
        /// </summary>
        public string? GetProxyVertexMapName(ProxyMesh proxy)
        {
            var simulated = new HashSet<int>();
            for (var v = 0; v < proxy.NodeIndices.Length; v++)
            {
                if (v < proxy.ClothEnable.Length && proxy.ClothEnable[v] != 0f)
                {
                    simulated.Add(proxy.NodeIndices[v]);
                }
            }

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

                var members = 0;
                var outside = false;
                for (var i = 0; i < map.Weights.Length && !outside; i++)
                {
                    if (map.Weights[i] <= 0f)
                    {
                        continue;
                    }

                    outside = !simulated.Contains(map.VertexBase + i);
                    members++;
                }

                if (!outside && members == simulated.Count)
                {
                    // Two selections over the same nodes cannot both be the sheet's parent.
                    if (found is not null)
                    {
                        return null;
                    }

                    found = map.Name;
                }
            }

            return found;
        }

        /// <summary>An anti-tunnelling probe (from <c>m_AntiTunnelProbes</c>).</summary>
        public readonly record struct AntiTunnelProbe(float Weight, uint Flags, int ProbeNode, int Count, int Begin,
            float ActivationDistance, float CurvatureRadius, float Bias);

        /// <summary>Gets the anti-tunnelling probes (<c>m_AntiTunnelProbes</c>).</summary>
        public AntiTunnelProbe[] AntiTunnelProbes { get; }

        /// <summary>Gets the control nodes targeted by <see cref="AntiTunnelProbes"/> (<c>m_AntiTunnelTargetNodes</c>).</summary>
        public int[] AntiTunnelTargetNodes { get; }

        /// <summary>Gets the anti-tunnelling probe bytecode (<c>m_AntiTunnelBytecode</c>). Empty in every known model.</summary>
        public uint[] AntiTunnelBytecode { get; }

        /// <summary>A dynamic-to-kinematic node link (from <c>m_DynKinLinks</c>).</summary>
        public readonly record struct DynKinLink(int Parent, int Child);

        /// <summary>
        /// Gets the dynamic-to-kinematic node links (<c>m_DynKinLinks</c>). Each entry is one authored
        /// <c>ClothFollowBone</c>, re-declared on export by
        /// <c>ModelExtract.AddClothFollowBones</c>.
        /// </summary>
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
        // Parsed, never emitted: ClothShapeSDF is authored from a source mesh plus bake parameters, none
        // of which the compiled voxel grid carries.
        public SDFRigid[] SDFRigids { get; }

        /// <summary>
        /// Gets the goal-damped spring integrator bitmask (<c>m_GoalDampedSpringIntegrators</c>): one bit
        /// per DYNAMIC node, word <c>i &gt;&gt; 5</c> bit <c>i &amp; 31</c> for node
        /// <c>i + <see cref="StaticNodeCount"/></c>, set for a node on the goal-damped integrator. The
        /// compiler emits it only for a model whose dynamic nodes hold both integrator kinds, so it is
        /// empty on most models. Read through <see cref="UsesGoalDampedIntegrator(int)"/>.
        /// </summary>
        public uint[] GoalDampedSpringIntegrators { get; }

        /// <summary>
        /// Gets, per control node, whether the export re-authors that node's goal values through the raw
        /// <c>cloth_animation_force_attract</c>/<c>cloth_animation_attract</c> proxy paint instead of the
        /// goal-strength pair, which is what keeps it on the raw integrator. Empty for a model with nothing
        /// to recover that way, and for one where re-authoring it would change whether the model's dynamic
        /// nodes hold both integrator kinds.
        /// </summary>
        public bool[] RawGoalPaintNodes { get; }

        /// <summary>
        /// A named cloth effect (wind, stiffen, ...) from <c>m_Effects</c>. <see cref="Params"/> is the raw
        /// per-effect-type parameter block (e.g. a wind effect carries <c>Strength</c>, <c>Choppiness</c>,
        /// <c>Vortices</c> and <c>VertexMap</c>); its schema varies with <see cref="Type"/> and is kept
        /// unparsed.
        /// </summary>
        public readonly record struct Effect(string Name, uint NameHash, int Type, KVObject Params);

        /// <summary>Gets the named cloth effects (<c>m_Effects</c>).</summary>
        public Effect[] Effects { get; }

        /// <summary>A deprecated morph layer (from <c>m_MorphLayers</c>).</summary>
        public readonly record struct MorphLayer(string Name, uint NameHash, int[] Nodes, Vector3[] InitPos,
            float[] Gravity, float[] GoalStrength, float[] GoalDamping, uint Flags);

        /// <summary>Gets the deprecated morph layers (<c>m_MorphLayers</c>).</summary>
        public MorphLayer[] MorphLayers { get; }

        /// <summary>Gets the raw morph-set data (<c>m_MorphSetData</c>). Empty in every known model.</summary>
        public byte[] MorphSetData { get; }

        /// <summary>A self-collision layer (from <c>m_SelfCollisionLayers</c>).</summary>
        public readonly record struct SelfCollisionLayer(string Name, int[] Nodes, float ParentReaction,
            uint Flags, uint[] EndIndices);

        /// <summary>Gets the self-collision layers (<c>m_SelfCollisionLayers</c>). Empty in every known model.</summary>
        public SelfCollisionLayer[] SelfCollisionLayers { get; }

        /// <summary>A node stray-box constraint (from <c>m_NodeStrayBoxes</c>).</summary>
        public readonly record struct NodeStrayBox(Vector3 Min, Vector3 Max, uint Flags, int NodeA, int NodeB);

        /// <summary>Gets the node stray-box constraints (<c>m_NodeStrayBoxes</c>). Empty in every known model.</summary>
        public NodeStrayBox[] NodeStrayBoxes { get; }

        /// <summary>A tapered-capsule stretch constraint (from <c>m_TaperedCapsuleStretches</c>).</summary>
        public readonly record struct TaperedCapsuleStretch(int NodeA, int NodeB, int CollisionMask,
            float RadiusA, float RadiusB);

        /// <summary>
        /// Gets the tapered-capsule stretch constraints (<c>m_TaperedCapsuleStretches</c>). Empty in every
        /// known model.
        /// </summary>
        public TaperedCapsuleStretch[] TaperedCapsuleStretches { get; }

        /// <summary>A per-pair spring constraint (from <c>m_SpringIntegrator</c>).</summary>
        public readonly record struct SpringIntegrator(int NodeA, int NodeB, float RestLength,
            float SpringConstant, float SpringDamping, float NodeWeight0);

        /// <summary>Gets the spring constraints (<c>m_SpringIntegrator</c>). Empty in every known model.</summary>
        public SpringIntegrator[] SpringIntegrators { get; }

        /// <summary>
        /// One row of <c>m_RigidColliderPriorities</c>: the index at which a priority group starts in each
        /// rigid-collider array. Row <c>g</c> holds group <c>g</c>'s first index in every array and the last
        /// row holds each array's element count, so group <c>g</c> owns <c>[row[g], row[g + 1])</c>.
        /// </summary>
        /// <remarks>
        /// Compiles older than Counter-Strike 2 carry a two-entry <c>m_nCollisionSphereIndex</c> in place
        /// of <c>m_nSDFRigidIndex</c>, so <see cref="SDFRigidIndex"/> reads 0 on every model that ships
        /// the array.
        /// </remarks>
        public readonly record struct RigidColliderIndices(int TaperedCapsuleRigidIndex, int SphereRigidIndex,
            int BoxRigidIndex, int SDFRigidIndex, int CollisionPlaneIndex);

        /// <summary>
        /// Gets the rigid-collider priority groups (<c>m_RigidColliderPriorities</c>), read back per
        /// collision shape by <see cref="ColliderPriority"/>. A model that gives every collision shape the
        /// same priority ships this empty.
        /// </summary>
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

        /// <summary>A Dota-only bone-merge link (from <c>m_BoneMergeLinks</c>, absent from CS2/Deadlock).</summary>
        public readonly record struct BoneMergeLink(uint ParentHash, int ChildNode);

        /// <summary>Gets the bone-merge links (<c>m_BoneMergeLinks</c>). Empty in every known model.</summary>
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
        // TODO: no authored construct reproduces these; every carrier is an imported strip.
        public CtrlOsOffset[] CtrlOsOffsets { get; }

        /// <summary>
        /// Gets whether the cloth was authored as ModelDoc's <c>ImportedCloth</c> node ("Imported PhysAuthFx
        /// Cloth", wizard <c>wizard_import_legacy_cloth</c>) rather than as ClothChains or a proxy sheet.
        /// <para>
        /// The marker is a non-empty <c>m_CtrlOsOffsets</c>: that array is the paired second column of an
        /// imported fx node table, and no ClothChain or proxy path produces one (an <c>extrude_sides</c>
        /// ring produces <c>m_CtrlOffsets</c> instead). The other tests exclude a model that mixes the two,
        /// since the import path replaces the whole cloth folder and cannot also rebuild a ring or a sheet.
        /// </para>
        /// </summary>
        public bool IsImportedCloth
            => CtrlOsOffsets.Length > 0
                && CtrlOffsets.Length == 0
                && Quads.Length == 0 && Tris.Length == 0
                && FitMatrixNodes.Count == 0
                && CtrlNames.Length > 0
                && !Array.Exists(CtrlNames, IsCompilerGeneratedNodeName);

        // The "$" namespace is not one family. An imported fx table supplies its own node names and those
        // can start with "$" too, so only the compiler's OWN generated families disqualify a model: they
        // are regenerated from a ring, sheet or element the import path does not rebuild.
        static bool IsCompilerGeneratedNodeName(string? name)
            => string.IsNullOrEmpty(name)
                || name.StartsWith("$cc", StringComparison.Ordinal)
                || name.StartsWith("$cloth_m", StringComparison.Ordinal)
                || name.StartsWith(FreeClothNodePrefix, StringComparison.Ordinal)
                || name.StartsWith("$cloth_root", StringComparison.Ordinal)
                || name.StartsWith("$ha_", StringComparison.Ordinal);

        /// <summary>
        /// Gets each node's parent along the <c>m_Ropes</c> runs alone (the first <c>m_nRopeCount</c> entries
        /// are the runs' exclusive end offsets). Unlike <see cref="BuildRopeParents"/> this does not fall back
        /// to <c>m_FollowNodes</c>, so it recovers exactly the chain parenting an imported fx node table
        /// declared - a follow link is a separate authored field on the same node.
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
        // Read by BuildVertexMapsFromSets to rebuild the named selections of a compile that ships no
        // m_VertexMaps. Not re-emitted: the ModelDoc construct that authors it is unknown.
        public byte[] DynNodeVertexSet { get; }

        /// <summary>Gets the legacy per-node stretch force (<c>m_LegacyStretchForce</c>).</summary>
        public float[] LegacyStretchForce { get; }

        /// <summary>
        /// Gets the raw <c>m_CollisionSpheres</c> entries. Absent from the reference schema and empty in
        /// every known model, so its element shape is unknown and the entries are kept uninterpreted.
        /// </summary>
        public IReadOnlyList<KVObject> CollisionSpheres { get; }

        // Keys the compiler regenerates from other data at compile time (the BVH tree, SIMD repacks,
        // free-node lists, and reserved/derived counts) - not parsed for authoring since a recompile
        // rebuilds them regardless of what an exported source declares. m_SimdQuads/m_SimdTris are the one
        // exception still read directly (see OrderFacesBySimdLanes), for face ordering rather than authoring.
        static readonly HashSet<string> DerivedKeys =
        [
            "m_CtrlHash",
            "m_TreeParents", "m_TreeChildren", "m_TreeCollisionMasks", "m_nTreeDepth",
            "m_SimdRods", "m_SimdNodeBases", "m_SimdAnimStrayRadii", "m_SimdRodsAnim", "m_SimdSpringIntegrator",
            "m_SimdQuads", "m_SimdTris",
            "m_FreeNodes",
            "m_nQuadCount1", "m_nQuadCount2", "m_nTriCount1", "m_nTriCount2",
            "m_nSimdQuadCount1", "m_nSimdQuadCount2", "m_nSimdTriCount1", "m_nSimdTriCount2",
            "m_nReservedUint8", "m_nNodeBaseJiggleboneDependsCount",
            // The pre-Counter-Strike-2 spelling of the same padding.
            "m_nReserved",
            // Absent from the reference schema, always zero or empty, and in the same partition-count and
            // SIMD-repack clusters as their m_nQuadCount1/2 and m_SimdNodeBases siblings.
            "m_nCollisionSphereInclusiveCount",
            "m_SimdFitMatrices", "m_nFitMatrixCount1", "m_nFitMatrixCount2",
            "m_nSimdFitMatrixCount1", "m_nSimdFitMatrixCount2",
            "m_DynNodeWindBases",
            // Read by DeriveFirstPositionDrivenNode for the position-driven boundary, never re-authored.
            "m_ReverseOffsets",
        ];

        // Every top-level m_pFeModel key this class reads, parsed or lazily surfaced via Data.
        static readonly HashSet<string> ParsedKeys =
        [
            "m_CtrlName", "m_SkelParents", "m_NodeInvMasses", "m_nNodeCount", "m_nStaticNodes",
            "m_nFirstPositionDrivenNode", "m_InitPose", "m_Quads", "m_Tris", "m_SourceElems",
            "m_HingeLimits", "m_KelagerBends", "m_VertexMapValues", "m_VertexMaps", "m_Rods",
            "m_NodeIntegrator", "m_NodeCollisionRadii", "m_WorldCollisionNodes", "m_WorldCollisionParams",
            "m_DynNodeFriction", "m_AnimStrayRadii", "m_FitMatrices", "m_Twists", "m_NodeBases",
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

        // Reads an array-of-struct key, or [] when the key is absent.
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
            FirstPositionDrivenNode = data.ContainsKey("m_nFirstPositionDrivenNode")
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

            // Each selection's per-node membership is a run of bytes in the shared value array, starting at
            // its own map offset and covering its node range.
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

            // TODO: the self-rods dropped below (nNode = [i, i]) come from an older compiler and are not
            // authorable on the current one, which refuses a ClothSpring with equal endpoints.
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
                    // A rod joining a node to itself constrains nothing and cannot be re-authored.
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
            var worldCollisionOrder = data.ContainsKey("m_WorldCollisionNodes")
                ? data.GetIntegerArray("m_WorldCollisionNodes").Select(static v => (int)v).ToArray()
                : [];
            WorldCollisionNodes = worldCollisionOrder.ToHashSet();

            // Each params entry covers a run of m_WorldCollisionNodes, so a node's friction is the entry
            // whose range contains its position in that list.
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

            // An entry constrains one node to its own animated position; the pair form links two different
            // nodes and is not a per-node stray radius.
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

            var twistNodes = new Dictionary<int, float>();
            var twistLinks = new HashSet<(int, int)>();
            var twistRelaxByLink = new Dictionary<(int, int), float>();
            if (data.GetArray("m_Twists") is { } twistsArray)
            {
                foreach (var entry in twistsArray)
                {
                    var relax = entry.GetFloatProperty("flTwistRelax");
                    var orient = entry.GetInt32Property("nNodeOrient");
                    var end = entry.GetInt32Property("nNodeEnd");
                    twistNodes[orient] = relax;
                    twistNodes[end] = relax;
                    twistLinks.Add(orient < end ? (orient, end) : (end, orient));
                    twistRelaxByLink[(orient, end)] = relax;
                    twistOrientFallback.TryAdd(orient, relax);
                }
            }

            TwistNodes = twistNodes;
            TwistLinks = twistLinks;
            TwistRelaxByLink = twistRelaxByLink;

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
            }

            LegacyStretchForce = data.GetFloatArray("m_LegacyStretchForce");
            CollisionSpheres = data.GetArray("m_CollisionSpheres") ?? [];

            RecoveredSkinWeights = RecoverAuthoredSkinWeights(data);
            RawGoalPaintNodes = BuildRawGoalPaintNodes();

            AssertAllKeysAccountedFor(data);
        }
    }
}
