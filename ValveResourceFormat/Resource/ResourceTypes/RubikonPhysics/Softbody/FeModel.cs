using System.Globalization;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

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

        private HashSet<(int, int)>? animRodPairs;

        private List<AnimRod>? animRods;

        /// <summary>
        /// Gets the unordered node pairs of <c>m_SimdRodsAnim</c>, the rods of chain joints declared with
        /// <c>animated_length</c>. These rods appear nowhere else in the file.
        /// </summary>
        internal IReadOnlySet<(int, int)> AnimRodPairs => animRodPairs ??= AnimRods
            .Select(static rod => UnorderedPair(rod.NodeA, rod.NodeB))
            .ToHashSet();

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

                var flat = FlattenSimdNodes(nNodeValue, 8);
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

        /// <summary>Gets the per-node solver integrator parameters (<c>m_NodeIntegrator</c>).</summary>
        public NodeIntegrator[] NodeIntegrators { get; }

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
        private float DynamicNodeValue(float[] values, int node)
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
        internal IReadOnlySet<int> ProxyFitMatrixNodes { get; }

        /// <summary>
        /// Gets the control nodes each <c>m_FitMatrices</c> entry is fit over, from its own
        /// <c>m_FitWeights</c> range, keyed by the bone the fit drives.
        /// </summary>
        public IReadOnlyDictionary<int, int[]> FitMatrixTargets { get; }

        /// <summary>
        /// Gets the authored skin weights of back-solved proxy-sheet vertices, keyed by control node, recovered from
        /// <c>m_FitWeights</c>, <c>m_CtrlOffsets</c> and <c>m_CtrlSoftOffsets</c>.
        /// </summary>
        internal IReadOnlyDictionary<int, (string Bone, float Weight)[]> RecoveredSkinWeights { get; }

        /// <summary>
        /// Gets the offset-network skin weights of the proxy-sheet vertices <see cref="RecoveredSkinWeights"/> leaves out,
        /// keyed by control node.
        /// </summary>
        internal IReadOnlyDictionary<int, (string Bone, float Weight)[]> DeferredOffsetSkinWeights { get; }

        /// <summary>Gets the control nodes named by any twist constraint (<c>m_Twists</c>).</summary>
        public IReadOnlySet<int> TwistNodes { get; }

        /// <summary>
        /// Gets every <c>m_Twists</c> record in array order. The compiler appends one record per directed
        /// pair per chain declaration and never de-duplicates, so a node or a pair can carry several.
        /// </summary>
        public IReadOnlyList<TwistRecord> TwistRecords { get; }

        /// <summary>Gets the unordered node pairs a twist constraint spans.</summary>
        internal IReadOnlySet<(int, int)> TwistLinks { get; }

        /// <summary>
        /// Gets the <c>flTwistRelax</c> of each directed (<c>nNodeOrient</c>, <c>nNodeEnd</c>) pair; the last entry wins
        /// where a pair has several.
        /// </summary>
        internal IReadOnlyDictionary<(int Orient, int End), float> TwistRelaxByLink { get; }

        /// <summary>
        /// Gets every <c>flTwistRelax</c> of each directed pair in array order, one per chain declaration that wrote it.
        /// </summary>
        internal IReadOnlyDictionary<(int Orient, int End), IReadOnlyList<float>> TwistRelaxCopies { get; }

        private static int[] BuildRopeParents(KVObject data, IReadOnlyList<int[]> ropeRuns)
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

            foreach (var run in ropeRuns)
            {
                for (var i = 1; i < run.Length; i++)
                {
                    Adopt(run[i], run[i - 1]);
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

        /// <summary>Gets the pair with its lower node first.</summary>
        internal static (int, int) UnorderedPair(int a, int b) => a < b ? (a, b) : (b, a);

        /// <summary>Gets the stray radius for <paramref name="node"/>, or 0 when unconstrained.</summary>
        public float GetStrayRadius(int node) => AnimStrayRadii.GetValueOrDefault(node).MaxDistance;

        /// <summary>
        /// Gets whether <paramref name="node"/> keeps its rotation free. Static nodes are ordered
        /// rotation-locked first, so the lock is exactly the nodes below
        /// <see cref="RotationLockedStaticNodeCount"/>.
        /// </summary>
        public bool AllowsRotation(int node) => node >= RotationLockedStaticNodeCount;

        /// <summary>Gets whether the node is position-driven (back-solved rather than simulated).</summary>
        public bool IsPositionDriven(int node) => node >= FirstPositionDrivenNode;

        private static int DeriveFirstPositionDrivenNode(KVObject data, string[] ctrlNames, int nodeCount, int staticNodes)
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
        /// Gets the parent control node of <paramref name="node"/>, or -1 for a root: its skeleton parent,
        /// or on an original that ships no <c>m_SkelParents</c> the parent its ctrl offset names.
        /// </summary>
        private int ParentNodeOf(int node)
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

        private Dictionary<int, int>? offsetParentByNode;

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

        private float InverseMassOf(int node)
            => node >= 0 && node < NodeInvMasses.Length ? NodeInvMasses[node] : 0f;

        /// <summary>The maximum length a rod that is not length-limited at all is given.</summary>
        public const float UnboundedRodDistance = 16384f;

        /// <summary>Gets the named vertex selections the cloth carries, empty when it has none.</summary>
        public IReadOnlyList<VertexMap> VertexMaps { get; private set; } = [];

        /// <summary>Gets the anti-tunnelling probes (<c>m_AntiTunnelProbes</c>).</summary>
        public AntiTunnelProbe[] AntiTunnelProbes { get; }

        /// <summary>Gets the control nodes targeted by <see cref="AntiTunnelProbes"/> (<c>m_AntiTunnelTargetNodes</c>).</summary>
        public int[] AntiTunnelTargetNodes { get; }

        /// <summary>Gets the anti-tunnelling probe bytecode (<c>m_AntiTunnelBytecode</c>).</summary>
        public uint[] AntiTunnelBytecode { get; }

        /// <summary>Gets the dynamic-to-kinematic node links (<c>m_DynKinLinks</c>), one per authored <c>ClothFollowBone</c>.</summary>
        public DynKinLink[] DynKinLinks { get; }

        /// <summary>Gets the collision planes (<c>m_CollisionPlanes</c>).</summary>
        public CollisionPlane[] CollisionPlanes { get; }

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
        internal bool[] RawGoalPaintNodes { get; }

        /// <summary>Gets the named cloth effects (<c>m_Effects</c>).</summary>
        public Effect[] Effects { get; }

        /// <summary>Gets the deprecated morph layers (<c>m_MorphLayers</c>).</summary>
        public MorphLayer[] MorphLayers { get; }

        /// <summary>Gets the raw morph-set data (<c>m_MorphSetData</c>).</summary>
        public byte[] MorphSetData { get; }

        /// <summary>Gets the self-collision layers (<c>m_SelfCollisionLayers</c>).</summary>
        public SelfCollisionLayer[] SelfCollisionLayers { get; }

        /// <summary>Gets the node stray-box constraints (<c>m_NodeStrayBoxes</c>).</summary>
        public NodeStrayBox[] NodeStrayBoxes { get; }

        /// <summary>Gets the tapered-capsule stretch constraints (<c>m_TaperedCapsuleStretches</c>).</summary>
        public TaperedCapsuleStretch[] TaperedCapsuleStretches { get; }

        /// <summary>Gets the spring constraints (<c>m_SpringIntegrator</c>).</summary>
        public SpringIntegrator[] SpringIntegrators { get; }

        /// <summary>Gets the rigid-collider priority groups (<c>m_RigidColliderPriorities</c>).</summary>
        public RigidColliderIndices[] RigidColliderPriorities { get; }

        /// <summary>Gets the jiggle bones (<c>m_JiggleBones</c>).</summary>
        public IndexedJiggleBone[] JiggleBones { get; }

        /// <summary>Gets the bone-merge links (<c>m_BoneMergeLinks</c>).</summary>
        public BoneMergeLink[] BoneMergeLinks { get; }

        /// <summary>Gets the parent-locked node links (<c>m_LockToParent</c>).</summary>
        public LockToParentLink[] LockToParent { get; }

        /// <summary>Gets the control nodes locked to their animated goal (<c>m_LockToGoal</c>).</summary>
        public int[] LockToGoal { get; }

        /// <summary>Gets the strip column pairings (<c>m_CtrlOsOffsets</c>).</summary>
        public CtrlOsOffset[] CtrlOsOffsets { get; }

        /// <summary>
        /// Gets each node's parent along the <c>m_Ropes</c> runs alone, without the <c>m_FollowNodes</c> fallback of
        /// <see cref="BuildRopeParents"/>.
        /// </summary>
        public IReadOnlyDictionary<int, int> RopeRunParents
        {
            get
            {
                var parents = new Dictionary<int, int>();
                foreach (var run in RopeRuns)
                {
                    for (var i = 1; i < run.Length; i++)
                    {
                        parents.TryAdd(run[i], run[i - 1]);
                    }
                }

                return parents;
            }
        }

        /// <summary>
        /// Gets the node runs of <c>m_Ropes</c>, whose first <c>m_nRopeCount</c> entries are the runs' exclusive end offsets.
        /// </summary>
        private IReadOnlyList<int[]> RopeRuns => ropeRuns ??= ReadRopeRuns(Data);

        private List<int[]>? ropeRuns;

        private static List<int[]> ReadRopeRuns(KVObject data)
        {
            var runs = new List<int[]>();
            var ropeCount = data.GetInt32Property("m_nRopeCount");
            var ropes = data.GetIntegerArray("m_Ropes");
            if (ropeCount <= 0 || ropes.Length <= ropeCount)
            {
                return runs;
            }

            var begin = ropeCount;
            for (var rope = 0; rope < ropeCount; rope++)
            {
                var end = Math.Min((int)ropes[rope], ropes.Length);
                var run = new int[Math.Max(end - begin, 0)];
                for (var i = 0; i < run.Length; i++)
                {
                    run[i] = (int)ropes[begin + i];
                }

                runs.Add(run);
                begin = end;
            }

            return runs;
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

        private static T[] ReadArray<T>(KVObject data, string key, Func<KVObject, T> map)
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
                SkelParents = BuildRopeParents(data, RopeRuns);
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
                    twistLinks.Add(UnorderedPair(orient, end));
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

            RecoveredSkinWeights = RecoverAuthoredSkinWeights(out var deferredOffsetWeights, out var unbackSolvedMeshes);
            DeferredOffsetSkinWeights = deferredOffsetWeights;
            UnbackSolvedProxyMeshes = unbackSolvedMeshes;
            RawGoalPaintNodes = BuildRawGoalPaintNodes();
        }

        private static int[][] ReadNodeIndexArray(KVObject data, string key, int expectedLength)
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

        private static (int[][] Faces, (int, int)[] Springs) ReadSourceElems(KVObject data)
        {
            if (!data.ContainsKey("m_SourceElems") || !data.IsNotBlobType("m_SourceElems"))
            {
                return ([], []);
            }

            var elems = data.GetIntegerArray("m_SourceElems");
            if (elems.Length < SourceElemArities)
            {
                return ([], []);
            }

            var counted = SourceElemArities;
            for (var arity = 1; arity <= SourceElemArities; arity++)
            {
                var count = elems[arity - 1];
                if (count < 0 || count > elems.Length)
                {
                    return ([], []);
                }

                counted += arity * (int)count;
            }

            if (counted != elems.Length)
            {
                return ([], []);
            }

            var faces = new List<int[]>();
            var springs = new List<(int, int)>();
            var read = SourceElemArities;
            for (var arity = 1; arity <= SourceElemArities; arity++)
            {
                for (var remaining = (int)elems[arity - 1]; remaining > 0; remaining--, read += arity)
                {
                    if (arity == 2)
                    {
                        var a = (int)elems[read];
                        var b = (int)elems[read + 1];
                        if (a != b)
                        {
                            springs.Add((a, b));
                        }

                        continue;
                    }

                    if (arity < 3)
                    {
                        continue;
                    }

                    var corners = new List<int>(arity);
                    for (var c = 0; c < arity; c++)
                    {
                        var node = (int)elems[read + c];
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
            }

            return ([.. faces], [.. springs]);
        }

        private const int SourceElemArities = 4;

        /// <summary>
        /// Gets the authored proxy-mesh faces recovered from <c>m_SourceElems</c>, as control-node index
        /// lists in winding order (four corners for a quad, three for a triangle).
        /// </summary>
        public int[][] SourceFaces { get; } = [];

        /// <summary>
        /// Gets the two-corner elements of <c>m_SourceElems</c>, one per authored <c>ClothSpring</c>.
        /// </summary>
        public (int, int)[] SourceSprings { get; } = [];

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

        /// <summary>The prefix of a control node created for an authored free-standing <c>ClothNode</c>.</summary>
        public const string FreeClothNodePrefix = "$cloth_node_";

        /// <summary>
        /// Returns whether the node at <paramref name="node"/> is a static (pinned, invMass == 0) anchor.
        /// </summary>
        public bool IsStatic(int node)
            => node >= 0 && node < NodeInvMasses.Length && NodeInvMasses[node] == 0f;
    }
}
