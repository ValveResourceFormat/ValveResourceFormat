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
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/PhysFeModelDesc_t">PhysFeModelDesc_t</seealso>
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

        /// <summary>Gets the per-dynamic-node friction (<c>m_DynNodeFriction</c>).</summary>
        public float[] DynNodeFriction { get; }

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
        /// over the sheet's own vertices (dark_willow's Coattail/HairStrand), while a <c>ClothChain</c>
        /// joint fits over the chain's own <c>$cc</c> extrude ring and sibling joints (hornet's
        /// <c>hat_flap_*</c>, dynamo's <c>coat_e_3</c>/<c>coat_e_end</c>). Only the former is driven
        /// THROUGH the proxy and must not also be emitted as a ClothChain.
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
        /// is assigned to the primary bone's nearest static real ancestor. Verified on dark_willow:
        /// all 39 back-solved simulated vertices reconstruct with error ~1e-7, and every fit matrix's
        /// vCenter equals the weighted centroid of exactly these weights (~1e-5), so re-painting them
        /// reproduces the original fit transforms rather than approximating them.
        /// <para>
        /// A vertex no fit matrix covers is recovered from <c>m_CtrlOffsets</c>/<c>m_CtrlSoftOffsets</c>
        /// alone, at scale 1. The compiler drops every authored influence below a fixed keep threshold
        /// and renormalizes the survivors before building the network, so the network's expansion is
        /// exactly the authored set the compiler itself acted on. Measured against the authored proxy
        /// DMX that abrams, hornet, dynamo and bebop compile from - 3035 vertices, every kept
        /// influence's authored-to-network ratio constant per vertex to 3e-5, and the keep/drop split a
        /// single global threshold bracketed to (0.02480, 0.02504). Re-painting the expansion therefore
        /// re-derives the same network: renormalizing only scales weights up, so nothing that cleared
        /// the threshold can fall back under it.
        /// </para>
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
        public IReadOnlyDictionary<int, float> TwistNodes { get; }

        /// <summary>
        /// Gets the compiled twist relaxation of <paramref name="node"/>, or 0 when untwisted. This is the
        /// solver's own per-constraint value, which decays along a chain, not the authored
        /// <c>twist_relax</c> it was derived from.
        /// </summary>
        public float GetCompiledTwistRelax(int node) => TwistNodes.GetValueOrDefault(node);

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
        /// link to match, and its authored value still steers the first link's relaxation
        /// (<c>kez_feathers</c>: 0.382 against 0.0 on the leading pair), so membership is the right test
        /// there.
        /// </summary>
        public bool HasAuthoredTwist(int node, int parent)
            => parent >= 0 ? HasTwistToParent(node, parent) : TwistNodes.ContainsKey(node);

        // The cloth_drag paint compiles to flPointDamping = paint * 30 (measured: 0.2 -> 6.0, 0.5 -> 15.0).
        internal const float ClothDragPointDampingScale = 30f;

        // Base gravity acceleration (inches/s^2) that a source gravity_z of 1.0 maps to; used to turn the
        // compiled per-node flGravity back into the source gravity_z scale (ClothChain joints and ClothNode).
        internal const float ClothSourceBaseGravity = 360f;

        // Outside this range the compiler skips the attraction solve and writes goal_damping through
        // unchanged, so the inverse is the identity.
        internal const float GoalDampingSolveMaxAttraction = 0.9999f;
        internal const float GoalDampingSolveMinAttraction = 0.0001f;

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

        // Rope cloth ships no m_SkelParents. Two records of which node follows which survive: m_Ropes (its
        // first m_nRopeCount entries are the exclusive end offsets of the ordered node runs that follow)
        // and m_FollowNodes, an explicit parent/child pair per follower.
        //
        // Three other arrays look like a hierarchy and are not. m_CtrlOsOffsets pairs the two columns of a
        // strip, and m_Rods joins them as well, so following either one parents a node onto its own
        // sibling; the second column is generated by the extrude rather than being a skeleton bone, and
        // naming it as a chain joint fails the compile ("Bone <name> not found, transform undefined").
        // m_CtrlOffsets maps a proxy vertex to the bone it back-solves, not one node to another.
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

        /// <summary>
        /// The spans <paramref name="chains"/> rebuild by themselves, with multiplicity: each joint's rod to
        /// its parent, plus a grandparent/great-grandparent rod where its bend or torsion spring is set, all
        /// repeated once per extra solver iteration and, again, doubled where the joint carries a suspender.
        /// </summary>
        static Dictionary<(int, int), int> ChainGeneratedSpans(List<BoneChain> chains)
        {
            var generated = new Dictionary<(int, int), int>();

            void Generate(int a, int b)
            {
                if (a < 0 || b < 0)
                {
                    return;
                }

                var key = a < b ? (a, b) : (b, a);
                generated[key] = generated.GetValueOrDefault(key) + 1;
            }

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
                        Generate(parent, joint.Node);

                        if (joint.BendSpring)
                        {
                            Generate(grandParent, joint.Node);
                        }

                        if (joint.TorsionSpring)
                        {
                            Generate(greatGrandParent, joint.Node);
                        }
                    }

                    // A suspender's companion always lands on the pair (joint, chain root) - the SAME
                    // pair as the plain parent link only when the root happens to be this joint's
                    // parent (RootSuspenderValue's own remarks), otherwise a pair extra_iterations never
                    // touches at all.
                    if (joint.Suspender != 0f)
                    {
                        Generate(rootNode, joint.Node);
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
        /// records its own parent-child links as two-corner elements (<c>hair_strand_3jnts</c>,
        /// <c>phoenix_ti10_immortal_back</c>, <c>undying_fall20_immortal_minion</c>), and a free
        /// <c>$cloth_node_</c> records its tie to the bone it hangs off (<c>ti10_ursa_immortalcub</c>).
        /// Both of those are already emitted by the chain and by the free-node writer respectively, so
        /// re-declaring one duplicates a rod and leaves every node it touches too heavy. Restricting to
        /// <c>$cc</c> ring endpoints keeps priest's cross-chain ties, which nothing else reaches -
        /// <see cref="GetUngeneratedRods"/>'s own emitter skips proxy-named nodes outright.
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

            var spanned = ChainGeneratedSpans(chains);
            var authored = new List<(int, int)>(SourceSprings.Length);
            foreach (var (a, b) in SourceSprings)
            {
                if (IsChainRing(a) && IsChainRing(b) && !spanned.ContainsKey(a < b ? (a, b) : (b, a)))
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
        public List<Rod> GetUngeneratedRods(List<BoneChain> chains)
        {
            var generated = ChainGeneratedSpans(chains);

            void Generate(int a, int b)
            {
                if (a < 0 || b < 0)
                {
                    return;
                }

                var key = a < b ? (a, b) : (b, a);
                generated[key] = generated.GetValueOrDefault(key) + 1;
            }

            // AddClothSourceSprings re-declares these already.
            foreach (var (a, b) in GetAuthoredSourceSprings(chains))
            {
                Generate(a, b);
            }

            var surplus = new List<Rod>();
            foreach (var rod in Rods)
            {
                var key = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
                if (generated.TryGetValue(key, out var remaining) && remaining > 0)
                {
                    generated[key] = remaining - 1;
                    continue;
                }

                surplus.Add(rod);
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

        /// <summary>Gets how far <paramref name="node"/> may stretch past its stray radius, or 0.</summary>
        public float GetStrayRelaxation(int node) => AnimStrayRadii.GetValueOrDefault(node).RelaxationFactor;

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
        /// Recovers the per-vertex normal of a proxy sheet from the compiled rest poses.
        /// <para>
        /// The cloth importer derives each proxy vertex's rest ORIENTATION from that vertex's normal and
        /// nothing else - measured on a hand-authored sheet, the compiled <c>m_InitPose</c> rotation is the
        /// frame whose local +Z is the vertex normal, and rebinding the skinning, rotating the UVs or
        /// moving the mesh dag leaves it untouched. So the normal a sheet ships fixes the rest orientation
        /// of every node it creates, and the axis the original recorded is recoverable from the pose it
        /// compiled to.
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
        /// The compiler gives a proxy-mesh node the mass
        /// <c>8 * sum over its incident surface rods of the rest length + expf(paint * cloth_mass_scale)</c>,
        /// the exponential term only when the mesh ships a <c>cloth_mass</c> stream at all. Those surface
        /// rods are the DISTINCT corner pairs (edges and diagonals) of the sheet's own faces, each pair
        /// counted once however many faces share it, and only those - the rods <c>add_stiffness_rods</c>
        /// derives join the network after the mass pass and weigh nothing, so the compiled
        /// <c>m_Rods</c> array is not the set to sum. The geometric term is therefore predictable from the
        /// very faces this sheet exports, and whatever the shipped mass carries beyond it is the
        /// exponential.
        /// </para>
        /// <para>
        /// Only a sheet exported with its AUTHORED faces qualifies. A synthesised triangulation compiles to
        /// a rod network of its own, so the shipped mass and the geometric term describe different surfaces
        /// and their difference measures that gap rather than any paint.
        /// </para>
        /// </summary>
        public float[]? RecoverMassPaint(ProxyMesh proxy)
        {
            if (!proxy.UsesAuthoredFaces)
            {
                return null;
            }

            var count = proxy.Positions.Length;
            var pairs = new Dictionary<(int A, int B), float>();
            foreach (var face in proxy.Faces)
            {
                for (var i = 0; i < face.Length; i++)
                {
                    for (var j = i + 1; j < face.Length; j++)
                    {
                        var (a, b) = face[i] < face[j] ? (face[i], face[j]) : (face[j], face[i]);
                        pairs[(a, b)] = Vector3.Distance(proxy.Positions[a], proxy.Positions[b]);
                    }
                }
            }

            var geometric = new float[count];
            foreach (var ((a, b), length) in pairs)
            {
                geometric[a] += SurfaceRodMassPerUnitLength * length;
                geometric[b] += SurfaceRodMassPerUnitLength * length;
            }

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

                var residual = 1f / invMass - geometric[v];

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
        /// Recovers the <c>cloth_stray_radius</c> paint of a proxy sheet from <c>m_AnimStrayRadii</c>, or
        /// null when no vertex of the sheet is stray-constrained. The compiled <c>flMaxDist</c> is the
        /// painted distance itself, the same value a ClothChain joint's <c>stray_radius</c> carries, so a
        /// sheet that ships no stream compiles with the whole array empty. A vertex whose node is covered
        /// by an <see cref="IndependentBoneChains"/> chain (the chain's own joints, or the "$cc" ring
        /// proxies the compiler auto-generates from them) is skipped: that chain's <c>MakeClothJoint</c>
        /// KV already carries the value, and the surface reconstruction can still reference the same node
        /// as a quad corner (mh_dragon_knight_back's skirt ring), which would otherwise double-paint it
        /// onto a second, compiler-synthesised copy of the node.
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

            Dictionary<int, int>? offsetParents = null;
            if (!HasCompiledSkelParents && CtrlOffsets.Length > 0)
            {
                offsetParents = new Dictionary<int, int>(CtrlOffsets.Length);
                foreach (var off in CtrlOffsets)
                {
                    offsetParents[off.CtrlChild] = off.CtrlParent;
                }
            }

            int ParentOf(int node)
            {
                var parent = node < SkelParents.Length ? SkelParents[node] : -1;
                return parent < 0 && offsetParents is not null
                    ? offsetParents.GetValueOrDefault(node, -1)
                    : parent;
            }

            var covered = new HashSet<int>(chainBoneNodes);
            for (var node = 0; node < CtrlNames.Length; node++)
            {
                if (CtrlNames[node].StartsWith("$cc", StringComparison.Ordinal) && chainBoneNodes.Contains(ParentOf(node)))
                {
                    covered.Add(node);
                }
            }

            return covered;
        }

        // Mass the compiler credits a node with per unit of incident surface-rod rest length.
        const float SurfaceRodMassPerUnitLength = 8f;

        // The band a node's exponential mass term, expf(paint * scale), has to fall in to be read back as
        // paint. A mesh that ships no stream leaves a residual of a float32 ulp of its own mass - 6e-5 on a
        // node weighing 1000 - so the lower bound separates "no term at all" from the smallest term a
        // stream can carry, e^0 = 1. Shipped sheets paint 0 to 10, so the upper bound sits far above the
        // largest term in the corpus and only rejects a mass no exponential explains.
        const float MinRecoverableMassPaintTerm = 0.05f;
        const float MaxRecoverableMassPaintTerm = 1e6f;

        /// <summary>Gets the friction painted on <paramref name="node"/>, or 0 when it has none.</summary>
        public float GetNodeFriction(int node)
        {
            var dynamicIndex = node - StaticNodeCount;
            return dynamicIndex >= 0 && dynamicIndex < DynNodeFriction.Length ? DynNodeFriction[dynamicIndex] : 0f;
        }

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
        public float VelocitySmoothRate => Data.GetFloatProperty("m_flRodVelocitySmoothRate");
        public int VelocitySmoothIterations => Data.GetInt32Property("m_nRodVelocitySmoothIterations");
        public uint DynamicNodeFlags => Data.GetUInt32Property("m_nDynamicNodeFlags");
        public uint StaticNodeFlags => Data.GetUInt32Property("m_nStaticNodeFlags");
        public int RotationLockedStaticNodeCount => Data.GetInt32Property("m_nRotLockStaticNodes");
        public float MotionSmoothCdt => Data.GetFloatProperty("m_flMotionSmoothCDT");
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
        /// Gets the angular limits the compiler built for joints authored with a stiff hinge, keyed by the
        /// node the hinge orients. <c>Center</c> and <c>Range</c> are radians.
        /// </summary>
        public IReadOnlyDictionary<int, (float Weight, float Center, float Range)> HingeLimits { get; } =
            new Dictionary<int, (float, float, float)>();

        /// <summary>
        /// Gets whether the cloth carries axial bend edges, which is what the source's rigid-edge-hinge
        /// switch produces (verified on a plain sheet: the switch alone turns an empty array into one entry
        /// per interior edge).
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
        // ring - every keychain - leaves all four the same length, so which pair the compiler's own scan
        // keeps comes down to rounding in the ring it extruded. Tilting the axis a hundred-
        // thousandth of a radian towards the tip ring's first node settles it the way the originals
        // compiled, three orders of magnitude below the drift the ring positions round-trip with anyway.
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

            return IsHingedJoint(parent);
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
        public readonly record struct VertexMap(string Name, uint NameHash, int VertexBase, int VertexCount,
            Vector3 CenterOfMass, float[] Weights)
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

#pragma warning disable CS1591
#pragma warning restore CS1591

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

        /// <summary>Gets the dynamic-to-kinematic node links (<c>m_DynKinLinks</c>).</summary>
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

        /// <summary>Gets the goal-damped spring integrator indices (<c>m_GoalDampedSpringIntegrators</c>).</summary>
        public uint[] GoalDampedSpringIntegrators { get; }

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

        /// <summary>Gets the deprecated morph layers (<c>m_MorphLayers</c>). Empty in every known model.</summary>
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

        /// <summary>Per-node priority order into the four rigid-collider arrays (from <c>m_RigidColliderPriorities</c>).</summary>
        public readonly record struct RigidColliderIndices(int TaperedCapsuleRigidIndex, int SphereRigidIndex,
            int BoxRigidIndex, int SDFRigidIndex, int CollisionPlaneIndex);

        /// <summary>
        /// Gets the rigid-collider priority indices (<c>m_RigidColliderPriorities</c>). Empty in every known
        /// model.
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
        public CtrlOsOffset[] CtrlOsOffsets { get; }

        /// <summary>
        /// Gets whether the cloth was authored as ModelDoc's <c>ImportedCloth</c> node ("Imported PhysAuthFx
        /// Cloth", wizard <c>wizard_import_legacy_cloth</c>) rather than as ClothChains or a proxy sheet.
        /// <para>
        /// The marker is a non-empty <c>m_CtrlOsOffsets</c>: that array is the paired second column of an
        /// imported fx node table, and no ClothChain/proxy path produces one (an <c>extrude_sides</c> ring
        /// produces <c>m_CtrlOffsets</c> instead). The other tests exclude the one Dota model that mixes the
        /// two (bristlebot: 5 os-offsets alongside 20 <c>$cc</c> ring ctrls), since the import path replaces
        /// the whole cloth folder and cannot also rebuild a ring or a sheet.
        /// </para>
        /// </summary>
        public bool IsImportedCloth
            => CtrlOsOffsets.Length > 0
                && CtrlOffsets.Length == 0
                && Quads.Length == 0 && Tris.Length == 0
                && FitMatrixNodes.Count == 0
                && CtrlNames.Length > 0
                && !Array.Exists(CtrlNames, IsCompilerGeneratedNodeName);

        // The "$" namespace is not one family. An imported fx table supplies its own node names and a
        // definition whose name starts with "$" leads them there too (brewmaster's flails ship
        // "$cloth1_flail_r0c1"), so only the compiler's OWN generated families disqualify a model - those
        // are regenerated from a ring/sheet/element the import path does not rebuild.
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
        public byte[] DynNodeVertexSet { get; }

        /// <summary>Gets the legacy per-node stretch force (<c>m_LegacyStretchForce</c>).</summary>
        public float[] LegacyStretchForce { get; }

        /// <summary>
        /// Gets the raw <c>m_CollisionSpheres</c> entries. Not in the reference schema snapshot (a newer
        /// compiler build than either static extraction it was taken from) and empty in every known model,
        /// so its element shape is unknown - kept as uninterpreted sub-objects rather than guessed.
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
            // Not in the reference schema snapshot (a newer compiler build than either static extraction it
            // was taken from) - found via AssertAllKeysAccountedFor against real corpus data, always 0/empty,
            // sitting in the same partition-count/SIMD-repack clusters as their m_nQuadCount1/2 and
            // m_SimdNodeBases siblings.
            "m_nCollisionSphereInclusiveCount",
            "m_SimdFitMatrices", "m_nFitMatrixCount1", "m_nFitMatrixCount2",
            "m_nSimdFitMatrixCount1", "m_nSimdFitMatrixCount2",
            "m_DynNodeWindBases",
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

            // Parsed this session.
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
            FirstPositionDrivenNode = data.GetInt32Property("m_nFirstPositionDrivenNode");

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

            var hingeLimits = new Dictionary<int, (float, float, float)>();
            foreach (var hinge in data.GetArray("m_HingeLimits") ?? [])
            {
                var nodes = hinge.GetIntegerArray("nNode");
                if (nodes.Length > 0)
                {
                    hingeLimits[(int)nodes[0]] = (hinge.GetFloatProperty("flWeight5"),
                        hinge.GetFloatProperty("flAngleCenter"), hinge.GetFloatProperty("flAngleExtents"));
                }
            }

            HingeLimits = hingeLimits;

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
                    weights));
            }

            VertexMaps = vertexMaps;

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
                    // A rod joining a node to itself constrains nothing and cannot be re-authored - the
                    // compiler rejects such a ClothSpring outright ("Cannot connect node to itself").
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
                }
            }

            TwistNodes = twistNodes;
            TwistLinks = twistLinks;

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
            LegacyStretchForce = data.GetFloatArray("m_LegacyStretchForce");
            CollisionSpheres = data.GetArray("m_CollisionSpheres") ?? [];

            (RecoveredSkinWeights, RecoveredBackSolveThreshold) = RecoverAuthoredSkinWeights(data);

            AssertAllKeysAccountedFor(data);
        }
    }
}
