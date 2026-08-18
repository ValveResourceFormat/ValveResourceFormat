using System.Diagnostics;
using System.Globalization;
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
        public IReadOnlyDictionary<int, float> TwistNodes { get; }

        /// <summary>
        /// Gets the compiled twist relaxation of <paramref name="node"/>, or 0 when untwisted. This is the
        /// solver's own per-constraint value, which decays along a chain, not the authored
        /// <c>twist_relax</c> it was derived from.
        /// </summary>
        public float GetCompiledTwistRelax(int node) => TwistNodes.GetValueOrDefault(node);

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
        /// Returns the rods that <paramref name="chains"/> will not regenerate on their own. A chain emits
        /// one rod per joint to its parent (plus a grandparent/great-grandparent rod where the joint's bend
        /// or torsion spring is set), but a model can carry extra copies of those spans; each surplus copy
        /// has to be re-declared as its own spring or the nodes come out too light.
        /// </summary>
        public List<Rod> GetUngeneratedRods(List<BoneChain> chains)
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
                foreach (var joint in chain.Joints)
                {
                    var parent = joint.ParentNode;
                    Generate(parent, joint.Node);

                    var grandParent = parent >= 0 && byNode.TryGetValue(parent, out var p1) ? p1.ParentNode : -1;
                    if (joint.BendSpring)
                    {
                        Generate(grandParent, joint.Node);
                    }

                    if (joint.TorsionSpring)
                    {
                        var greatGrandParent = grandParent >= 0 && byNode.TryGetValue(grandParent, out var p2)
                            ? p2.ParentNode
                            : -1;
                        Generate(greatGrandParent, joint.Node);
                    }
                }
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
        /// none. The compiler anchors a hinge on a static node of its own making named after the joint it
        /// constrains, and builds exactly one such anchor per hinge, so that node's presence is what marks
        /// the constrained joint. The axis is recovered from where the joint's own proxy ring ended up (the
        /// hinge is what orients that ring), and the limits from the angular extents of the hinge limit
        /// built over it.
        /// </summary>
        public ChainHinge? GetChainHinge(string boneName, int jointNode)
        {
            if (Array.IndexOf(CtrlNames, HingeAnchorPrefix + boneName) < 0)
            {
                return null;
            }

            var ring = ProxyRingOf(jointNode);
            if (ring.Count < 2 || ring[0] >= InitPosePositions.Length || ring[1] >= InitPosePositions.Length)
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

            // The extents cover half of the counter-clockwise limit; the clockwise one leaves no trace, so
            // it recovers as the mirror of what did.
            var extents = 0f;
            foreach (var hinge in Data.GetArray("m_HingeLimits") ?? [])
            {
                var nodes = hinge.GetIntegerArray("nNode");
                if (nodes.Length >= 2 && (int)nodes[0] == ring[0] && (int)nodes[1] == ring[1])
                {
                    extents = float.RadiansToDegrees(hinge.GetFloatProperty("flAngleExtents")) * 2f;
                    break;
                }
            }

            return new ChainHinge(axis, -extents, extents);
        }

        /// <summary>Gets how many auto-generated proxy nodes the compiler extruded from a joint.</summary>
        public int ProxyCountOf(int jointNode) => ProxyRingOf(jointNode).Count;

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
            return parent >= 0 && parent < CtrlNames.Length
                && Array.IndexOf(CtrlNames, HingeAnchorPrefix + CtrlNames[parent]) >= 0;
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
        /// Returns whether any rod still joins <paramref name="chain"/>'s own nodes. A rigid hinge replaces
        /// the chain's rod network with the hinge itself, so a hinged chain that kept its rods was authored
        /// with a soft hinge link instead.
        /// </summary>
        public bool HasChainRods(BoneChain chain)
        {
            var nodes = new HashSet<int>();
            foreach (var joint in chain.Joints)
            {
                nodes.Add(joint.Node);
                nodes.UnionWith(ProxyRingOf(joint.Node));
            }

            foreach (var rod in Rods)
            {
                if (nodes.Contains(rod.NodeA) && nodes.Contains(rod.NodeB))
                {
                    return true;
                }
            }

            return false;
        }

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
            SourceFaces = ReadSourceFaces(data);

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

            FitMatrixNodes = data.GetArray("m_FitMatrices") is { } fitMatrices
                ? fitMatrices.Select(static o => o.GetInt32Property("nNode")).ToHashSet()
                : new HashSet<int>();

            var twistNodes = new Dictionary<int, float>();
            if (data.GetArray("m_Twists") is { } twistsArray)
            {
                foreach (var entry in twistsArray)
                {
                    var relax = entry.GetFloatProperty("flTwistRelax");
                    twistNodes[entry.GetInt32Property("nNodeOrient")] = relax;
                    twistNodes[entry.GetInt32Property("nNodeEnd")] = relax;
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

        // m_SourceElems is the authored proxy mesh's own face list: flat groups of four control-node
        // indices in cyclic winding order, a repeated index marking a triangle. The leading group is a
        // degenerate placeholder. Unlike m_Quads/m_Tris it survives even when the compiler collapses the
        // whole surface into rods, which is the only record of the authored topology for such models.
        static int[][] ReadSourceFaces(KVObject data)
        {
            if (!data.ContainsKey("m_SourceElems") || !data.IsNotBlobType("m_SourceElems"))
            {
                return [];
            }

            var elems = data.GetIntegerArray("m_SourceElems");

            var faces = new List<int[]>(elems.Length / SourceElemStride);
            for (var i = 0; i + SourceElemStride <= elems.Length; i += SourceElemStride)
            {
                var corners = new List<int>(SourceElemStride);
                for (var c = 0; c < SourceElemStride; c++)
                {
                    var node = (int)elems[i + c];
                    if (!corners.Contains(node))
                    {
                        corners.Add(node);
                    }
                }

                if (corners.Count >= 3)
                {
                    faces.Add([.. corners]);
                }
            }

            return [.. faces];
        }

        const int SourceElemStride = 4;

        /// <summary>
        /// Gets the authored proxy-mesh faces recovered from <c>m_SourceElems</c>, as control-node index
        /// lists in winding order (four corners for a quad, three for a triangle).
        /// </summary>
        public int[][] SourceFaces { get; } = [];

        /// <summary>
        /// Gets whether the compiler anchored this cloth to a static root node of its own making, which is
        /// what it does for a proxy mesh that arrives with no skinning. Its absence means every sheet was
        /// skinned, so exporting one unskinned would add a node the original never had.
        /// </summary>
        public bool HasGeneratedClothRoot => Array.Exists(CtrlNames, static n => n == ClothRootNodeName);

        const string ClothRootNodeName = "$cloth_root";

        /// <summary>
        /// Returns the node pairs the compiler regenerates as <c>m_Rods</c> from <paramref name="faces"/>:
        /// every face edge plus every face diagonal, deduplicated. Verified set-equal to the shipped
        /// <c>m_Rods</c> of a pure proxy-mesh cloth (a 256-quad sheet yields 544 edges + 512 diagonals).
        /// </summary>
        public static HashSet<(int, int)> DeriveRodsFromFaces(IEnumerable<int[]> faces)
        {
            var derived = new HashSet<(int, int)>();
            foreach (var face in faces)
            {
                for (var a = 0; a < face.Length; a++)
                {
                    for (var b = a + 1; b < face.Length; b++)
                    {
                        var (x, y) = face[a] < face[b] ? (face[a], face[b]) : (face[b], face[a]);
                        derived.Add((x, y));
                    }
                }
            }

            return derived;
        }

        /// <summary>
        /// Gets the authored <c>additional_shear_stretch</c>. A rod's <c>flRelaxationFactor</c> is
        /// <c>exp(-stretch)</c>, where a face edge uses the surface stretch and a face diagonal uses the
        /// surface stretch plus this value, so the slackest rod recovers it. The compiler clamps the
        /// authored value at zero, which is why a negative original is indistinguishable from zero.
        /// </summary>
        public float AdditionalShearStretch
        {
            get
            {
                var slackest = float.MaxValue;
                foreach (var rod in Rods)
                {
                    if (rod.RelaxationFactor > 0f && rod.RelaxationFactor < slackest)
                    {
                        slackest = rod.RelaxationFactor;
                    }
                }

                if (slackest is float.MaxValue or >= 1f)
                {
                    return 0f;
                }

                return Math.Max(0f, -MathF.Log(slackest) - DefaultSurfaceStretch);
            }
        }

        /// <summary>
        /// Gets a value indicating whether this FeModel carries any control nodes.
        /// </summary>
        public bool HasData => CtrlNames.Length > 0;

        /// <summary>
        /// Gets a value indicating whether <c>m_SkelParents</c> was present in the compiled data. False on
        /// old-era compiles (and rope cloth), where <see cref="SkelParents"/> is synthesized from
        /// <c>m_Ropes</c>/<c>m_FollowNodes</c> or the skeleton instead.
        /// </summary>
        public bool HasCompiledSkelParents { get; }

        /// <summary>
        /// Returns whether a control-node name is an auto-generated cloth proxy node (not a real skeleton bone).
        /// </summary>
        public static bool IsProxyNodeName(string? name)
            => string.IsNullOrEmpty(name) || name.StartsWith('$');

        /// <summary>
        /// Gets or sets the names of the skeleton's real bones. Cloth extrusion does not always mark what it
        /// generates with the <c>$</c> prefix - a two-column strip names its second column after the bone it
        /// widens - so without the skeleton to compare against, a generated node is indistinguishable from a
        /// real one and gets authored as a chain joint the compiler then cannot resolve.
        /// </summary>
        public IReadOnlySet<string>? SkeletonBoneNames { get; set; }

        /// <summary>
        /// Gets or sets each skeleton bone's parent bone name. Used to orient chain links recovered from
        /// the rod mesh on compiles that ship no <c>m_SkelParents</c>: the rod evidence alone cannot tell
        /// parent from child on a strap anchored at both ends.
        /// </summary>
        public IReadOnlyDictionary<string, string?>? SkeletonBoneParents { get; set; }

        /// <summary>
        /// Rebuilds <see cref="SkelParents"/> from the model's own bone hierarchy, for cloth that ships
        /// neither <c>m_SkelParents</c> nor the <c>m_Ropes</c>/<c>m_FollowNodes</c> trail
        /// <see cref="BuildRopeParents"/> reads. A control node takes the nearest ancestor bone that is
        /// itself a control node. Does nothing once either of those two sources has produced a hierarchy.
        /// </summary>
        public void SetSkeletonParents(IReadOnlyDictionary<string, string?> boneParents)
        {
            if (SkelParents.Length > 0 || CtrlNames.Length == 0 || NodeCount <= 0)
            {
                return;
            }

            // Only for cloth built purely out of real bones. Once the compiler has generated nodes of its
            // own, they carry the hierarchy the skeleton cannot express, and imposing the bone tree on top
            // re-parents the surrounding network instead of completing it.
            foreach (var name in CtrlNames)
            {
                if (IsProxyNodeName(name))
                {
                    return;
                }
            }

            var nodeByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var node = 0; node < CtrlNames.Length && node < NodeCount; node++)
            {
                nodeByName.TryAdd(CtrlNames[node], node);
            }

            var parents = new int[NodeCount];
            Array.Fill(parents, -1);
            var parented = false;

            foreach (var (name, node) in nodeByName)
            {
                var ancestor = boneParents.GetValueOrDefault(name);
                while (ancestor is not null)
                {
                    if (nodeByName.TryGetValue(ancestor, out var parentNode) && parentNode != node)
                    {
                        parents[node] = parentNode;
                        parented = true;
                        break;
                    }

                    ancestor = boneParents.GetValueOrDefault(ancestor);
                }
            }

            if (parented)
            {
                SkelParents = parents;
            }
        }

        /// <summary>
        /// Returns whether a control node is generated by the cloth compiler rather than being a skeleton
        /// bone the chain can name as a joint.
        /// </summary>
        public bool IsGeneratedNodeName(string? name)
            => IsProxyNodeName(name)
                || (SkeletonBoneNames is not null && !SkeletonBoneNames.Contains(name!));

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
            /// <summary>
            /// Gets the named vertex selections covering this sheet, as a per-vertex membership weight
            /// each. Painted back onto the sheet as one <c>cloth_vertex_set_&lt;name&gt;</c> stream per
            /// selection, which is how a proxy vertex joins one.
            /// </summary>
            public (string Name, float[] Weights)[] VertexMaps { get; init; } = [];
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
            /// <summary>
            /// Gets whether <see cref="Faces"/> came from the authored <c>m_SourceElems</c> topology rather
            /// than a synthesised triangulation. The compiler rebuilds the shipped rods from such a sheet on
            /// its own, so it is exported without rod-suppressing paints and without explicit springs.
            /// </summary>
            public bool UsesAuthoredFaces { get; init; }
            /// <summary>
            /// Gets whether no vertex of this sheet is driven by a real skeleton bone. Hand-authored
            /// proxies of this kind ship unskinned, and the compiler answers by generating its own static
            /// root node plus one <c>m_CtrlOffsets</c> entry per vertex. Skinning such a sheet to the
            /// synthetic per-vertex bones instead binds each node directly and loses both.
            /// </summary>
            public bool IsFreeFloating { get; init; }
        }

        /// <summary>
        /// Restores a proxy's original vertex NUMBERING when it has gaps. The compiler names control nodes
        /// "$cloth_m{N}p{SLOT}" by DMX vertex slot BEFORE dropping unfaced vertices, so an original whose
        /// source mesh had culled vertices ships non-contiguous p numbers (135 corpus models - mars,
        /// death_prophet, undying, warlock). Re-exporting only the survivors contiguously shifts every name
        /// after each gap, and the whole node set mis-pairs against the original. A pinned, unfaced dummy
        /// copy of the nearest real vertex fills each gap slot: the compiler drops it again (unfaced), and
        /// every real vertex keeps its original number.
        /// </summary>
        ProxyMesh PadToAuthoredSlots(ProxyMesh mesh)
        {
            var n = mesh.NodeIndices.Length;
            if (n == 0)
            {
                return mesh;
            }

            var slots = new int[n];
            var meshIndex = -1;
            for (var i = 0; i < n; i++)
            {
                var node = mesh.NodeIndices[i];
                if (node < 0 || node >= CtrlNames.Length)
                {
                    return mesh;
                }

                var m = ParseProxyMeshIndex(CtrlNames[node]);
                var p = ParseProxyVertexIndex(CtrlNames[node]);
                if (m < 0 || p < 0 || (i > 0 && (m != meshIndex || p <= slots[i - 1])))
                {
                    return mesh;
                }

                meshIndex = m;
                slots[i] = p;
            }

            var total = slots[n - 1] + 1;
            if (total == n && slots[0] == 0)
            {
                return mesh;
            }

            // Padded slot -> source vertex; a gap copies the nearest real vertex at or before it (a gap
            // below the first real slot copies the first).
            var srcOf = new int[total];
            var dummy = new bool[total];
            var src = 0;
            for (var slot = 0; slot < total; slot++)
            {
                if (src < n && slots[src] == slot)
                {
                    srcOf[slot] = src;
                    src++;
                }
                else
                {
                    srcOf[slot] = src > 0 ? src - 1 : 0;
                    dummy[slot] = true;
                }
            }

            T[] Pad<T>(T[] source) => [.. Enumerable.Range(0, total).Select(slot => source[srcOf[slot]])];

            var clothEnable = Pad(mesh.ClothEnable);
            for (var slot = 0; slot < total; slot++)
            {
                if (dummy[slot])
                {
                    clothEnable[slot] = 0f;
                }
            }

            var localToSlot = new int[n];
            for (var slot = 0; slot < total; slot++)
            {
                if (!dummy[slot])
                {
                    localToSlot[srcOf[slot]] = slot;
                }
            }

            // A dummy carries its copied neighbour's control-node index: every consumer that walks
            // NodeIndices stays valid, and the name map only covers faced vertices so the duplicate
            // never claims the real vertex's name.
            return new ProxyMesh
            {
                NodeIndices = Pad(mesh.NodeIndices),
                Positions = Pad(mesh.Positions),
                ClothEnable = clothEnable,
                GoalStrength = Pad(mesh.GoalStrength),
                GoalDamping = Pad(mesh.GoalDamping),
                CollisionRadius = Pad(mesh.CollisionRadius),
                Friction = Pad(mesh.Friction),
                Drag = Pad(mesh.Drag),
                GroundCollision = Pad(mesh.GroundCollision),
                Gravity = Pad(mesh.Gravity),
                VertexAttraction = Pad(mesh.VertexAttraction),
                SkinInfluences = Pad(mesh.SkinInfluences),
                VertexMaps = [.. mesh.VertexMaps.Select(m => (m.Name, Pad(m.Weights)))],
                Faces = [.. mesh.Faces.Select(f => f.Select(v => localToSlot[v]).ToArray())],
                SimulatedCount = mesh.SimulatedCount,
                PinnedCount = mesh.PinnedCount + (total - n),
                IsDropRisk = mesh.IsDropRisk,
                UsesAuthoredFaces = mesh.UsesAuthoredFaces,
                IsFreeFloating = mesh.IsFreeFloating,
            };
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
                    result.Add(PadToAuthoredSlots(merged));
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

                        result.Add(PadToAuthoredSlots(new ProxyMesh
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
                            VertexMaps = [.. merged.VertexMaps.Select(m => (m.Name, Take(m.Weights)))],
                            Faces = [.. merged.Faces.Where(f => remap.ContainsKey(f[0])).Select(f => f.Select(v => remap[v]).ToArray())],
                            SimulatedCount = vertices.Count(v => merged.ClothEnable[v] != 0f),
                            PinnedCount = vertices.Count(v => merged.ClothEnable[v] == 0f),
                        }));
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
                .Where(chain => !HasProxyMeshNodes
                    || !chain.Joints.Any(joint => FitMatrixNodes.Contains(joint.Node)))
                .SelectMany(static c => c.Joints).Select(static j => j.Node).ToHashSet();
            if (chainBoneNodes.Count > 0)
            {
                // Old-era compiles ship m_SkelParents empty; a ring vertex's anchor bone then comes from
                // its m_CtrlOffsets entry instead (the same fallback BuildBoneChains uses).
                Dictionary<int, int>? offsetParents = null;
                if (!HasCompiledSkelParents && CtrlOffsets.Length > 0)
                {
                    offsetParents = new Dictionary<int, int>(CtrlOffsets.Length);
                    foreach (var off in CtrlOffsets)
                    {
                        offsetParents[off.CtrlChild] = off.CtrlParent;
                    }
                }

                for (var node = 0; node < CtrlNames.Length; node++)
                {
                    if (!IsProxyNodeName(CtrlNames[node]))
                    {
                        continue;
                    }

                    var parent = node < SkelParents.Length ? SkelParents[node] : -1;
                    if (parent < 0 && offsetParents is not null)
                    {
                        parent = offsetParents.GetValueOrDefault(node, -1);
                    }

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
                        if (n >= 0 && n < InitPosePositions.Length && !IsHingeRegeneratedProxy(n))
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
            // A face reaching into a corner the sheet gave up (one a hinged chain rebuilds itself) is not
            // this sheet's to draw.
            var faces = new List<int[]>(Quads.Length + Tris.Length);
            bool Kept(int[] face) => Array.TrueForAll(face, corner => remap.ContainsKey(corner));

            foreach (var q in OrderFacesBySimdLanes(Quads, "m_SimdQuads"))
            {
                if (Kept(q))
                {
                    faces.Add([remap[q[0]], remap[q[1]], remap[q[2]], remap[q[3]]]);
                }
            }

            foreach (var t in OrderFacesBySimdLanes(Tris, "m_SimdTris"))
            {
                if (Kept(t))
                {
                    faces.Add([remap[t[0]], remap[t[1]], remap[t[2]]]);
                }
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
                VertexMaps = BuildVertexMapWeights(nodeIndices),
                Faces = faces,
                SimulatedCount = simulated,
                PinnedCount = pinned,
            };
        }

        // The vertex selections that reach any of the given nodes, as a membership weight per node.
        (string Name, float[] Weights)[] BuildVertexMapWeights(IReadOnlyList<int> nodeIndices)
        {
            var maps = new List<(string, float[])>();
            foreach (var map in VertexMaps)
            {
                var weights = new float[nodeIndices.Count];
                var covers = false;
                for (var i = 0; i < nodeIndices.Count; i++)
                {
                    weights[i] = map.WeightOf(nodeIndices[i]);
                    covers |= weights[i] > 0f;
                }

                if (covers)
                {
                    maps.Add((map.Name, weights));
                }
            }

            return [.. maps];
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
            var goalDamping = GoalDampingFromAttraction(integrator.ForceAttraction, integrator.VertexAttraction);

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
        /// <summary>
        /// Whether the compiler named any control node as a proxy-mesh vertex, i.e. whether the original
        /// carries a cloth SHEET at all. Without one there is nothing for a fit matrix to drive a bone
        /// THROUGH, so a chain keeps its own ClothChain however many of its joints are back-solved.
        /// </summary>
        public bool HasProxyMeshNodes => Array.Exists(CtrlNames, static name => ParseProxyMeshIndex(name) >= 0);

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
                    && !coveredNodes.Contains(node) && !IsHingeRegeneratedProxy(node);
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

            // The corners of an authored face are one surface by definition. Splitting them across islands
            // would drop that face from both (a face is only kept where all of its corners are), leaving
            // its vertices unfaced - and an unfaced vertex is never registered as a control node at all.
            // Only faces confined to a single compiled mesh count: a face spanning two of them describes
            // authored geometry the compiler itself chose to split, and merging on it would fuse two
            // separate sheets into one.
            foreach (var face in SourceFaces)
            {
                if (SpansProxyMeshes(face))
                {
                    continue;
                }

                var first = -1;
                foreach (var corner in face)
                {
                    if (corner < 0 || corner >= n || !isProxy[corner])
                    {
                        continue;
                    }

                    if (first < 0)
                    {
                        first = corner;
                    }
                    else
                    {
                        parent[Find(corner)] = Find(first);
                    }
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
                    result.Add(PadToAuthoredSlots(mesh));
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

            var localOf = new Dictionary<int, int>(count);
            for (var i = 0; i < count; i++)
            {
                localOf[nodeIndices[i]] = i;
            }

            var faces = TakeAuthoredFaces(localOf, nodeIndices);
            var usesAuthoredFaces = faces.Count > 0;
            if (!usesAuthoredFaces)
            {
                faces = TriangulateDominantPlane(positions);
                EnsureAllVerticesFaced(positions, faces);
            }

            if (faces.Count == 0)
            {
                return null;
            }

            // Authored faces are the original topology, so the compiler's own rod derivation from them
            // reproduces the shipped m_Rods - the island needs no drop-risk fallback and no explicit rods.
            var isDropRisk = !usesAuthoredFaces && ComputeDropRisk(positions, clothEnable, faces);

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
                VertexMaps = BuildVertexMapWeights(nodeIndices),
                Faces = faces,
                SimulatedCount = simulated,
                PinnedCount = pinned,
                IsDropRisk = isDropRisk,
                UsesAuthoredFaces = usesAuthoredFaces,
                IsFreeFloating = usesAuthoredFaces && HasGeneratedClothRoot
                    && skinInfluences.All(static v => v.All(static i => IsProxyNodeName(i.Bone))),
            };
        }

        // The authored faces wholly contained in one island, remapped to that island's local vertex
        // indices. A face straddling islands belongs to neither.
        //
        // The set is kept only when every rod the compiler would derive from it is a rod the model
        // actually ships. A surface that would invent constraints is rejected outright and the island
        // falls back to a synthesised triangulation with its rods declared explicitly, which is exact even
        // though the compiled quad/tri surface then differs (the mixed chain-plus-proxy heroes land here).
        List<int[]> TakeAuthoredFaces(Dictionary<int, int> localOf, List<int> nodeIndices)
        {
            var faces = new List<int[]>();
            foreach (var face in SourceFaces)
            {
                if (SpansProxyMeshes(face))
                {
                    continue;
                }

                var complete = true;
                foreach (var corner in face)
                {
                    if (!localOf.ContainsKey(corner))
                    {
                        complete = false;
                        break;
                    }
                }

                if (complete)
                {
                    faces.Add(face);
                }
            }

            if (faces.Count == 0)
            {
                return [];
            }

            var shipped = new HashSet<(int, int)>();
            foreach (var rod in Rods)
            {
                shipped.Add(rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA));
            }

            // A rod between two pinned nodes constrains nothing, and the compiler leaves it out - so an
            // authored edge joining two of them is absent from m_Rods without the surface being wrong.
            foreach (var (a, b) in DeriveRodsFromFaces(faces))
            {
                if (!shipped.Contains((a, b)) && !(a < StaticNodeCount && b < StaticNodeCount))
                {
                    return [];
                }
            }

            // Every vertex has to land on a face: an unfaced one is never registered as a control node, so
            // a partial cover would silently lose nodes that the synthesised triangulation keeps.
            var covered = new HashSet<int>();
            foreach (var face in faces)
            {
                covered.UnionWith(face);
            }

            if (!nodeIndices.All(covered.Contains))
            {
                return [];
            }

            return [.. OrderFacesByAllocation(faces, nodeIndices).Select(face => face.Select(corner => localOf[corner]).ToArray())];
        }

        // Whether a face's corners come from more than one compiled proxy mesh. Such a face cannot have
        // been authored in any single proxy DMX, and the rods it would imply are not the ones the model
        // ships, so it is left out of the surface entirely.
        bool SpansProxyMeshes(int[] face)
        {
            var meshIndex = int.MinValue;
            foreach (var corner in face)
            {
                if (corner < 0 || corner >= CtrlNames.Length || !IsProxyNodeName(CtrlNames[corner]))
                {
                    continue;
                }

                var cornerMesh = ParseProxyMeshIndex(CtrlNames[corner]);
                if (meshIndex == int.MinValue)
                {
                    meshIndex = cornerMesh;
                }
                else if (cornerMesh != meshIndex)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reorders faces so that walking them corner by corner meets the control nodes in the model's own
        /// index order. The compiler numbers a proxy's nodes by first encounter over the imported face list,
        /// so exporting the faces in this order reproduces the original numbering instead of a permutation
        /// of it. Faces are only rotated, never reversed, so the authored winding survives.
        /// </summary>
        static List<int[]> OrderFacesByAllocation(List<int[]> faces, List<int> nodeIndices)
        {
            var facesAt = new Dictionary<int, List<int>>();
            for (var f = 0; f < faces.Count; f++)
            {
                foreach (var corner in faces[f])
                {
                    if (!facesAt.TryGetValue(corner, out var list))
                    {
                        facesAt[corner] = list = [];
                    }

                    list.Add(f);
                }
            }

            var ordered = new List<int[]>(faces.Count);
            var used = new bool[faces.Count];
            var seen = new HashSet<int>();

            foreach (var node in nodeIndices.Order())
            {
                if (seen.Contains(node) || !facesAt.TryGetValue(node, out var candidates))
                {
                    continue;
                }

                int[]? bestRotation = null;
                var bestFace = -1;
                List<int>? bestFresh = null;

                foreach (var f in candidates)
                {
                    if (used[f])
                    {
                        continue;
                    }

                    var face = faces[f];
                    for (var start = 0; start < face.Length; start++)
                    {
                        var rotation = new int[face.Length];
                        for (var i = 0; i < face.Length; i++)
                        {
                            rotation[i] = face[(start + i) % face.Length];
                        }

                        var fresh = rotation.Where(c => !seen.Contains(c)).ToList();
                        if (fresh.Count == 0 || !IsAscending(fresh))
                        {
                            continue;
                        }

                        if (bestFresh is null || IsLexicographicallySmaller(fresh, bestFresh))
                        {
                            bestFresh = fresh;
                            bestRotation = rotation;
                            bestFace = f;
                        }
                    }
                }

                if (bestRotation is null || bestFresh is null)
                {
                    continue;
                }

                used[bestFace] = true;
                ordered.Add(bestRotation);
                foreach (var corner in bestFresh)
                {
                    seen.Add(corner);
                }
            }

            // Faces that introduce no node of their own play no part in the numbering; they keep their
            // original relative order behind the ones that do.
            for (var f = 0; f < faces.Count; f++)
            {
                if (!used[f])
                {
                    ordered.Add(faces[f]);
                }
            }

            return ordered;
        }

        static bool IsAscending(List<int> values)
        {
            for (var i = 1; i < values.Count; i++)
            {
                if (values[i] < values[i - 1])
                {
                    return false;
                }
            }

            return true;
        }

        static bool IsLexicographicallySmaller(List<int> candidate, List<int> current)
        {
            var shared = Math.Min(candidate.Count, current.Count);
            for (var i = 0; i < shared; i++)
            {
                if (candidate[i] != current[i])
                {
                    return candidate[i] < current[i];
                }
            }

            return candidate.Count < current.Count;
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

        /// <summary>A cloth collision box recovered from <c>m_BoxRigids</c>.</summary>
        public sealed class CollisionBox
        {
            /// <summary>Gets the bone the box is attached to.</summary>
            public required string? ParentBone { get; init; }
            /// <summary>Gets the box centre (bone-local).</summary>
            public required Vector3 Origin { get; init; }
            /// <summary>Gets the box orientation (bone-local).</summary>
            public required Quaternion Rotation { get; init; }
            /// <summary>Gets the box half-extents.</summary>
            public required Vector3 Size { get; init; }
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
        /// Reconstructs the cloth collision boxes (<c>m_BoxRigids</c>). Returns an empty list when the
        /// model has no box rigids.
        /// </summary>
        public List<CollisionBox> BuildCollisionBoxes()
        {
            var result = new List<CollisionBox>();
            var rigids = Data.GetArray("m_BoxRigids");
            if (rigids is null)
            {
                return result;
            }

            foreach (var rigid in rigids)
            {
                var frame = rigid.GetSubCollection("tmFrame2");
                if (frame is null)
                {
                    continue;
                }

                var (origin, _, rotation) = frame.ToTransform();
                var node = rigid.GetInt32Property("nNode");

                result.Add(new CollisionBox
                {
                    ParentBone = ResolveRigidBone(node),
                    Origin = origin,
                    Rotation = rotation,
                    Size = rigid.GetSubCollection("vSize").ToVector3(),
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
            /// <summary>
            /// Gets one of the <c>$cc</c> proxy nodes generated from this joint, or -1 when it has none.
            /// A joint's own node is position-driven and compiles with no gravity, so the authored
            /// <c>gravity_z</c> survives only on its proxies.
            /// </summary>
            public int ProxyNode { get; set; } = -1;
            /// <summary>
            /// Gets whether a rod spans this joint and its grandparent, i.e. whether the source authored a
            /// non-zero <c>bend_spring</c> here.
            /// </summary>
            public bool BendSpring { get; set; }
            /// <summary>
            /// Gets whether a rod spans this joint and its great-grandparent, i.e. whether the source
            /// authored a non-zero <c>torsion_spring</c> here.
            /// </summary>
            public bool TorsionSpring { get; set; }
            /// <summary>Gets the distance from this joint to its own proxy ring.</summary>
            public float ExtrudeRadius { get; set; }
            /// <summary>
            /// Gets the roll (degrees) of this joint's proxy ring about the forward axis, measured in the
            /// joint's rest frame. The ring's unrolled direction is the frame's +Y, so the value authored
            /// as <c>extrude_twist</c> is this angle's complement.
            /// </summary>
            public float ExtrudeTwist { get; set; }
            /// <summary>
            /// Gets the forward distance to a second proxy ring around this joint, which is what the
            /// authored <c>end_effector</c> produces (a tip that fans into two rows rather than one wider
            /// ring). Zero when the joint carries a single ring.
            /// </summary>
            public float EndEffector { get; set; }
            /// <summary>
            /// Gets the forward axis the compiler used to orient this joint's proxy ring (<c>'x'</c>,
            /// <c>'y'</c> or <c>'z'</c>), detected from the ring's own plane normal expressed in the
            /// joint's rest frame. <c>'x'</c> is the default and needs no explicit
            /// <c>extrude_forward_axis</c> authoring.
            /// </summary>
            public char ForwardAxis { get; set; } = 'x';
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
            /// <summary>
            /// Gets the roll (degrees) applied to the extruded proxy ring about the chain's forward axis.
            /// Recovered from where the compiler actually placed the <c>$cc</c> proxies in the joint's rest
            /// frame; a chain whose ring is not rolled recovers 0.
            /// </summary>
            public float ExtrudeTwist { get; set; }
        }

        // How far apart two proxies must sit along a joint's forward axis to count as separate rings. The
        // compiler ignores an end_effector below 0.05, so anything closer than that is one ring.
        const float EndEffectorRingTolerance = 0.05f;

        // extrude_forward_axis selector quaternions, byte-verified against the compiler's own compiled
        // ring frames: a +90-degree rotation about local Z for 'y' (maps +X to +Y), a -90-degree rotation
        // about local Y for 'z' (maps +X to +Z). 'x' uses Quaternion.Identity. Composed with a joint's own
        // rest rotation (ringFrame = jointRot * axisSelect), this re-labels which local axis is "forward".
        static readonly Quaternion ExtrudeAxisSelectY = new(0f, 0f, 0.70710677f, 0.70710677f);
        static readonly Quaternion ExtrudeAxisSelectZ = new(0f, -0.70710677f, 0f, 0.70710677f);

        static Quaternion ExtrudeAxisSelectQuaternion(char axis) => axis switch
        {
            'y' => ExtrudeAxisSelectY,
            'z' => ExtrudeAxisSelectZ,
            _ => Quaternion.Identity,
        };

        // Detects which forward axis the compiler used to orient a joint's proxy ring. Every ring point
        // lies in the plane perpendicular to the selected forward axis, so - expressed in the joint's own
        // rest frame - that axis' component is ~0 for every point while the other two carry the actual
        // ring geometry. 'x' is preferred whenever it qualifies (not just whichever axis is smallest):
        // a ring reproducible via the default axis needs no explicit authoring, and for a ring whose twist
        // happens to fall on a multiple of 90 degrees, two axes can tie at exactly 0 - genuine positional
        // ambiguity that authoring the non-default axis would not resolve any better, since the same
        // ring is exactly reproducible via 'x' with an adjusted twist. Only a ring that does NOT lie in the
        // default axis' plane at all needs 'y' or 'z'. Tolerance is relative to the ring's own scale so it
        // does not depend on model units.
        //
        // Deliberately NOT extended to look past a single ring at any end_effector second ring: a lone
        // ring's own centroid sits at the joint only when it has 2+ points that cancel out symmetrically
        // (sides>=2); a single-point ring's "centroid" is just that one point, off-center by construction
        // whether or not an end_effector exists, so using centroid displacement as an axis signal false-
        // positives on every plain sides=1 ring (measured: 30+ false positives across hoodwink/kez_base
        // alone). For the genuinely unrecoverable case - a sides=2 ring with no usable second-ring signal -
        // 'x' with a fitted twist reproduces the exact same compiled ring, so defaulting to it here is
        // correct, not merely a fallback.
        const float ExtrudeForwardAxisTolerance = 0.02f;

        static char DetectExtrudeForwardAxis(Vector3 jointPos, Quaternion jointRot, List<int> ring, Vector3[] positions)
        {
            float sumX = 0f, sumY = 0f, sumZ = 0f;
            foreach (var proxy in ring)
            {
                var local = Vector3.Transform(positions[proxy] - jointPos, Quaternion.Conjugate(jointRot));
                sumX += MathF.Abs(local.X);
                sumY += MathF.Abs(local.Y);
                sumZ += MathF.Abs(local.Z);
            }

            var scale = MathF.Max(sumX, MathF.Max(sumY, sumZ));
            if (scale <= 1e-6f)
            {
                return 'x';
            }

            var threshold = scale * ExtrudeForwardAxisTolerance;
            if (sumX <= threshold)
            {
                return 'x';
            }

            if (sumY <= threshold)
            {
                return 'y';
            }

            return sumZ <= threshold ? 'z' : 'x';
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

            // Mark real skeleton bones (everything that is not an auto-generated cloth node).
            var isReal = new bool[n];
            for (var i = 0; i < n; i++)
            {
                isReal[i] = !IsGeneratedNodeName(CtrlNames[i]);
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

            // Old-era compiles ship m_SkelParents empty; there the ring's anchor bone still survives in
            // m_CtrlOffsets (each generated ring vertex carries its bone-local anchor offset).
            Dictionary<int, int>? ctrlOffsetParents = null;
            if (!HasCompiledSkelParents && CtrlOffsets.Length > 0)
            {
                ctrlOffsetParents = new Dictionary<int, int>(CtrlOffsets.Length);
                foreach (var off in CtrlOffsets)
                {
                    ctrlOffsetParents[off.CtrlChild] = off.CtrlParent;
                }
            }

            for (var node = 0; node < n; node++)
            {
                // A strip's second column is generated too, but named after the bone it widens instead of
                // carrying the "$cc" prefix, so it counts as ribbon width the same way.
                if (!CtrlNames[node].StartsWith("$cc", StringComparison.Ordinal)
                    && !(!IsProxyNodeName(CtrlNames[node]) && IsGeneratedNodeName(CtrlNames[node])))
                {
                    continue;
                }

                var pp = node < SkelParents.Length ? SkelParents[node] : -1;
                if (pp < 0 && ctrlOffsetParents is not null)
                {
                    pp = ctrlOffsetParents.GetValueOrDefault(node, -1);
                }
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

                // A bone the compiler built a hinge anchor for is a hinged chain's root by construction,
                // so its real children belong to that chain however few traces they leave of their own. The
                // hinge puts the whole ribbon's proxies on the ROOT, which is what makes the three tests
                // above miss these chains entirely (legion_commander's earrings, tinker's cosmic back).
                var hingedRoot = Array.IndexOf(CtrlNames, HingeAnchorPrefix + CtrlNames[p]) >= 0;

                // A joint whose parent carries a stiff hinge is joined to it by the bend rather than by a
                // rod, so the rod test alone drops it and the chain ends one joint short (snotty_survivors'
                // snot_L_02, whose only rod reaches its grandparent).
                var bendLinked = false;
                foreach (var bend in KelagerBends)
                {
                    if (bend.MidNode == p && (bend.End0 == i || bend.End1 == i))
                    {
                        bendLinked = true;
                        break;
                    }
                }

                // A joint can reach its chain by the BEND rod to its grandparent rather than by one to its
                // own parent, which leaves the direct pair the rod test looks for absent and ends the chain
                // a joint short (gigawatt's sleeve_cloth_R_3 rods to sleeve_cloth_R_1, ribPipe_R_7 to
                // ribPipe_R_5). A grandparent needs a real parent of its own, so the static-root false chain
                // the rod test guards against cannot reach this: its resolved parent is a root.
                var grandParent = p < SkelParents.Length ? SkelParents[p] : -1;
                var bendRodLinked = grandParent >= 0 && grandParent < n && isReal[grandParent]
                    && rodPairs.Contains(grandParent < i ? (grandParent, i) : (i, grandParent));

                // Where the parent extrudes, the joint's rod lands on the parent's ring instead of on the
                // parent itself (gigawatt's joint51, whose only rods reach $ccjoint50_0/1).
                var ringLinked = false;
                if (proxyChildrenOf.TryGetValue(p, out var parentRing))
                {
                    foreach (var ring in parentRing)
                    {
                        if (rodPairs.Contains(ring < i ? (ring, i) : (i, ring)))
                        {
                            ringLinked = true;
                            break;
                        }
                    }
                }

                if (rodLinked || bothDrivenSim || proxyRibbon || hingedRoot || bendLinked
                    || bendRodLinked || ringLinked)
                {
                    realParent[i] = p;
                }
            }

            // With m_SkelParents empty the link rules above never fire, so the chain's own compiled rod
            // mesh is the remaining parent evidence: consecutive joints are joined by rods between the
            // joints and their rings, and joints are emitted root-first, so the lower-indexed side of the
            // rod evidence is the parent. Restricted to ring-bearing bones - a rod network among bare real
            // bones is ClothNode/ClothSpring authoring, not a chain.
            if (!HasCompiledSkelParents && Rods.Length > 0)
            {
                var ringOwner = new Dictionary<int, int>();
                foreach (var (owner, ring) in proxyChildrenOf)
                {
                    foreach (var vertex in ring)
                    {
                        ringOwner[vertex] = owner;
                    }
                }

                int OwnerOf(int node) => ringOwner.TryGetValue(node, out var owner)
                    ? owner
                    : node < n && isReal[node] ? node : -1;

                var linkCounts = new Dictionary<(int, int), int>();
                foreach (var rod in Rods)
                {
                    if (rod.NodeA == rod.NodeB)
                    {
                        continue;
                    }

                    var a = OwnerOf(rod.NodeA);
                    var b = OwnerOf(rod.NodeB);
                    if (a < 0 || b < 0 || a == b)
                    {
                        continue;
                    }

                    if (!proxyChildrenOf.ContainsKey(a) && !proxyChildrenOf.ContainsKey(b))
                    {
                        continue;
                    }

                    var key = a > b ? (b, a) : (a, b);
                    linkCounts[key] = linkCounts.GetValueOrDefault(key) + 1;
                }

                // The skeleton orients a link where it can: rod evidence alone cannot tell parent from
                // child on a strap anchored at both ends (sniper_calavera's strap_front, pinned at joints
                // 01 AND 07 - picking the wrong end hangs joints backwards off the far anchor and the
                // compiler access-violates on the resulting chain). Only a rod-evidenced pair is linked.
                var nodeByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < n; i++)
                {
                    if (isReal[i])
                    {
                        nodeByName.TryAdd(CtrlNames[i], i);
                    }
                }

                if (SkeletonBoneParents is not null)
                {
                    for (var i = 0; i < n; i++)
                    {
                        if (!isReal[i] || realParent[i] >= 0)
                        {
                            continue;
                        }

                        var ancestor = SkeletonBoneParents.GetValueOrDefault(CtrlNames[i]);
                        while (ancestor is not null)
                        {
                            if (nodeByName.TryGetValue(ancestor, out var p) && p != i)
                            {
                                var key = p > i ? (i, p) : (p, i);
                                if (linkCounts.ContainsKey(key))
                                {
                                    realParent[i] = p;
                                }

                                break;
                            }

                            ancestor = SkeletonBoneParents.GetValueOrDefault(ancestor);
                        }
                    }
                }

                // Remaining unoriented pairs: joints are emitted root-first, so the lower-indexed side is
                // the parent.
                var bestParent = new Dictionary<int, (int Parent, int Count)>();
                foreach (var ((low, high), count) in linkCounts)
                {
                    if (!bestParent.TryGetValue(high, out var current) || count > current.Count
                        || (count == current.Count && low < current.Parent))
                    {
                        bestParent[high] = (low, count);
                    }
                }

                foreach (var (child, link) in bestParent)
                {
                    if (realParent[child] < 0 && realParent[link.Parent] != child)
                    {
                        realParent[child] = link.Parent;
                    }
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
                // A real bone with no real descendants is not a cloth chain, unless it carries its own
                // extrude ring - a lone ring-bearing bone is a single-joint chain (fv_cosmic_weapon's
                // weapon_ball with its $cc..._Ctr node, flagbearer's collar).
                if (children[rootNode] is null && !proxyChildrenOf.ContainsKey(rootNode))
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
                var twists = new List<float>();
                foreach (var joint in chain.Joints)
                {
                    if (!proxyChildrenOf.TryGetValue(joint.Node, out var proxies) || proxies.Count == 0)
                    {
                        continue;
                    }

                    // A "$cc<bone>_Ctr" proxy is the single centre node the compiler emits for an
                    // end_effector with extrude_sides < 2 - it is not a ring member at all. A joint whose
                    // proxies are ALL centre nodes has no side ring: recover end_effector as the centre's
                    // forward displacement and leave the ring empty (fv_cosmic_weapon's weapon_ball).
                    var ring = proxies;
                    if (proxies.TrueForAll(p => CtrlNames[p].EndsWith("_Ctr", StringComparison.Ordinal))
                        && joint.Node < InitPoseRotations.Length && joint.Node < InitPosePositions.Length
                        && proxies[0] < InitPosePositions.Length)
                    {
                        var centreOffset = Vector3.Transform(
                            InitPosePositions[proxies[0]] - InitPosePositions[joint.Node],
                            Quaternion.Conjugate(InitPoseRotations[joint.Node]));
                        if (MathF.Abs(centreOffset.X) >= EndEffectorRingTolerance)
                        {
                            joint.EndEffector = centreOffset.X;
                            joint.ExtrudeSides = 0;
                            joint.ProxyNode = proxies[0];
                            sideFrequency[0] = sideFrequency.GetValueOrDefault(0) + 1;
                            continue;
                        }
                    }

                    // A joint whose proxies sit at two different distances along its forward axis carries a
                    // second ring, which is what end_effector produces. Its ring width is half the proxy
                    // count, not the whole of it - taking the whole count instead lays them out as one
                    // wider ring and every proxy lands somewhere the original never put it.
                    if (joint.Node < InitPoseRotations.Length && joint.Node < InitPosePositions.Length)
                    {
                        var forwardOf = new Dictionary<int, float>(proxies.Count);
                        foreach (var proxy in proxies)
                        {
                            if (proxy < InitPosePositions.Length)
                            {
                                forwardOf[proxy] = Vector3.Transform(
                                    InitPosePositions[proxy] - InitPosePositions[joint.Node],
                                    Quaternion.Conjugate(InitPoseRotations[joint.Node])).X;
                            }
                        }

                        if (forwardOf.Count == proxies.Count)
                        {
                            // The joint's OWN ring sits at forward ~= 0 (it is centred on the joint); an
                            // end_effector ring is displaced away from that by a signed amount that can go
                            // either way along forward, so the near ring is whichever cluster sits closest
                            // to 0, not whichever has the smaller raw (signed) value - comparing raw
                            // Min()/Max() mislabels the two when the displacement is negative.
                            var minAbs = forwardOf.Values.Min(MathF.Abs);
                            var maxAbs = forwardOf.Values.Max(MathF.Abs);
                            if (maxAbs - minAbs > EndEffectorRingTolerance)
                            {
                                var nearRing = proxies.Where(p => MathF.Abs(forwardOf[p]) - minAbs <= EndEffectorRingTolerance).ToList();
                                if (nearRing.Count > 0 && nearRing.Count < proxies.Count)
                                {
                                    // Same single-reference-value shape as before (not an average across
                                    // the cluster): the near ring's own smallest-magnitude member and the
                                    // far ring's own largest-magnitude member, which is exactly what the old
                                    // global Min()/Max() picked out on the already-correct (positive
                                    // displacement) case - so that case stays byte-identical.
                                    var nearValue = forwardOf[nearRing.MinBy(p => MathF.Abs(forwardOf[p]))];
                                    var farValue = forwardOf[proxies.Except(nearRing).MaxBy(p => MathF.Abs(forwardOf[p]))];
                                    joint.EndEffector = farValue - nearValue;
                                    ring = nearRing;
                                }
                            }
                        }
                    }

                    joint.ExtrudeSides = Math.Min(ring.Count, 4);
                    joint.ProxyNode = ring[0];
                    sideFrequency[ring.Count] = sideFrequency.GetValueOrDefault(ring.Count) + 1;
                    proxies = ring;
                    if (joint.Node < InitPosePositions.Length)
                    {
                        if (joint.Node < InitPoseRotations.Length)
                        {
                            joint.ForwardAxis = DetectExtrudeForwardAxis(
                                InitPosePositions[joint.Node], InitPoseRotations[joint.Node], proxies, InitPosePositions);
                        }

                        // The ring is laid out around the joint's forward axis, so the roll the compiler
                        // used shows up as the angle of the first proxy in the RING's own frame - the
                        // joint's rest rotation composed with the forward-axis selector, not the joint's
                        // rest rotation alone (the two coincide only when the axis is the default 'x').
                        if (joint.Node < InitPoseRotations.Length && proxies[0] < InitPosePositions.Length)
                        {
                            var ringFrame = InitPoseRotations[joint.Node] * ExtrudeAxisSelectQuaternion(joint.ForwardAxis);
                            var offset = Vector3.Transform(
                                InitPosePositions[proxies[0]] - InitPosePositions[joint.Node],
                                Quaternion.Conjugate(ringFrame));
                            if (new Vector2(offset.Y, offset.Z).LengthSquared() > 1e-6f)
                            {
                                var twist = float.RadiansToDegrees(MathF.Atan2(offset.Y, offset.Z));
                                joint.ExtrudeTwist = twist;
                                twists.Add(twist);
                            }

                            joint.ExtrudeRadius = Vector3.Distance(
                                InitPosePositions[joint.Node], InitPosePositions[proxies[0]]);
                        }

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
                    chain.ExtrudeTwist = twists.Count > 0 ? twists.Average() : 0f;
                }

                // A joint's bend/torsion springs are what make the compiler span a rod to its grandparent
                // and great-grandparent, so the presence of those rods is what the source authored. On an
                // extruding chain that span lands between the two joints' extruded RINGS and never between
                // the joint nodes, so both have to be looked at or the spring reads as off on every joint
                // of every such chain.
                var jointByNode = chain.Joints.ToDictionary(static j => j.Node);

                bool SpannedByRod(int node, int other)
                {
                    if (other < 0)
                    {
                        return false;
                    }

                    if (rodPairs.Contains(node < other ? (node, other) : (other, node)))
                    {
                        return true;
                    }

                    // A joint that extrudes carries the span on its ring instead of on itself, so the ring
                    // stands in for the joint wherever it has one. The spring then spans the two sides in
                    // FULL: anything short of that is some other construct passing between them - a surface
                    // the sheet rebuilds, say - and turning the spring on to claim it would add every pair
                    // it does not have.
                    List<int> Side(int end)
                        => proxyChildrenOf.TryGetValue(end, out var ring) && ring.Count > 0 ? ring : [end];

                    foreach (var a in Side(node))
                    {
                        foreach (var b in Side(other))
                        {
                            if (!rodPairs.Contains(a < b ? (a, b) : (b, a)))
                            {
                                return false;
                            }
                        }
                    }

                    return true;
                }

                foreach (var joint in chain.Joints)
                {
                    var parent = joint.ParentNode;
                    var grandParent = parent >= 0 && jointByNode.TryGetValue(parent, out var p1) ? p1.ParentNode : -1;
                    var greatGrandParent = grandParent >= 0 && jointByNode.TryGetValue(grandParent, out var p2) ? p2.ParentNode : -1;

                    joint.BendSpring = SpannedByRod(joint.Node, grandParent);
                    joint.TorsionSpring = SpannedByRod(joint.Node, greatGrandParent);
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
                    var forceAttraction = ia.ForceAttraction + (ib.ForceAttraction - ia.ForceAttraction) * t;
                    var vertexAttraction = ia.VertexAttraction + (ib.VertexAttraction - ia.VertexAttraction) * t;
                    var damping = GoalDampingFromAttraction(forceAttraction, vertexAttraction);
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
