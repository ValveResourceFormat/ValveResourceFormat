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
        /// <summary>The collision mask of a node that declares none: all sixteen layers.</summary>
        internal const int DefaultNodeCollisionMask = 0xFFFF;

        /// <summary>The number of element arities <c>m_SourceElems</c> leads with a count for.</summary>
        private const int SourceElemArities = 4;

        private readonly HashSet<int> lockedToParent;
        private readonly HashSet<int> lockedToGoal;
        private List<AnimRod>? animRods;
        private HashSet<int>? twistNodes;

        /// <summary>
        /// Initializes a new instance of the <see cref="FeModel"/> class from a parsed <c>m_pFeModel</c> sub-object.
        /// </summary>
        public FeModel(KVObject data)
        {
            Data = data;
            CtrlNames = data.GetArray<string>("m_CtrlName") ?? [];
            SkelParents = ReadInt32Array(data, "m_SkelParents");
            NodeInvMasses = data.GetFloatArray("m_NodeInvMasses");
            NodeCount = Math.Max(data.GetInt32Property("m_nNodeCount"), 0);
            StaticNodeCount = data.GetInt32Property("m_nStaticNodes");
            FirstPositionDrivenNode = data.ContainsKey("m_nFirstPositionDrivenNode")
                ? data.GetInt32Property("m_nFirstPositionDrivenNode")
                : NodeCount;

            var initPose = ReadArray(data, "m_InitPose", static p => p.Count >= 7 ? p.ToTransform() : (Position: Vector3.Zero, Scale: 1f, Rotation: Quaternion.Identity));
            InitPosePositions = [.. initPose.Select(static p => p.Position)];
            InitPoseRotations = [.. initPose.Select(static p => p.Rotation)];

            Quads = ReadNodeIndexArray(data, "m_Quads", 4);
            Tris = ReadNodeIndexArray(data, "m_Tris", 3);
            (SourceFaces, SourceSprings, SourceTriangleCount) = ReadSourceElems(data);

            KelagerBends = ReadKelagerBends(data);
            VertexMaps = ReadVertexMaps(data);
            Rods = ReadRods(data);

            NodeIntegrators = ReadArray(data, "m_NodeIntegrator", static o => new NodeIntegrator(
                o.GetFloatProperty("flPointDamping"),
                o.GetFloatProperty("flAnimationForceAttraction"),
                o.GetFloatProperty("flAnimationVertexAttraction"),
                o.GetFloatProperty("flGravity")));

            NodeCollisionRadii = data.GetFloatArray("m_NodeCollisionRadii");
            (NodeCollisionMasks, WorldCollisionNodes, WorldCollisionFriction) = ReadWorldCollision(data, NodeCount - StaticNodeCount);
            DynNodeFriction = data.GetFloatArray("m_DynNodeFriction");
            AnimStrayRadii = ReadAnimStrayRadii(data);
            (FitMatrixNodes, FitMatrixTargets) = ReadFitMatrices(data);

            TwistRecords = ReadArray(data, "m_Twists", static o => new TwistRecord(o.GetInt32Property("nNodeOrient"),
                o.GetInt32Property("nNodeEnd"), o.GetFloatProperty("flTwistRelax"), o.GetFloatProperty("flSwingRelax")));

            NodeBaseRecords = ReadArray(data, "m_NodeBases", static o => (o.GetInt32Property("nNode"), new NodeBasis(
                o.GetInt32Property("nNodeX0"), o.GetInt32Property("nNodeX1"),
                o.GetInt32Property("nNodeY0"), o.GetInt32Property("nNodeY1"))));
            var nodeBases = new Dictionary<int, NodeBasis>();
            foreach (var (node, basis) in NodeBaseRecords)
            {
                nodeBases[node] = basis;
            }

            NodeBases = nodeBases;

            AntiTunnelProbes = ReadArray(data, "m_AntiTunnelProbes", static o => new AntiTunnelProbe(
                o.GetFloatProperty("flWeight"), ReadUInt32(o, "nFlags"), o.GetInt32Property("nProbeNode"),
                o.GetInt32Property("nCount"), o.GetInt32Property("nBegin"),
                o.GetFloatProperty("flActivationDistance"), o.GetFloatProperty("flCurvatureRadius"),
                o.GetFloatProperty("flBias")));
            AntiTunnelTargetNodes = ReadInt32Array(data, "m_AntiTunnelTargetNodes");
            AntiTunnelBytecode = ReadUInt32Array(data, "m_AntiTunnelBytecode");

            DynKinLinks = ReadArray(data, "m_DynKinLinks", static o => new DynKinLink(
                o.GetInt32Property("m_nParent"), o.GetInt32Property("m_nChild")));

            CollisionPlanes = ReadArray(data, "m_CollisionPlanes", static o =>
            {
                var plane = o.GetSubCollection("m_Plane");
                return new CollisionPlane(
                    o.GetInt32Property("nCtrlParent"), o.GetInt32Property("nChildNode"),
                    ReadVector3(plane?.GetSubCollection("m_vNormal")),
                    plane?.GetFloatProperty("m_flOffset") ?? 0f,
                    o.GetFloatProperty("flStickiness"), o.GetFloatProperty("flStrength"));
            });

            GoalDampedSpringIntegrators = ReadUInt32Array(data, "m_GoalDampedSpringIntegrators");

            Effects = ReadArray(data, "m_Effects", static o => new Effect(
                o.GetStringProperty("sName") ?? string.Empty, ReadUInt32(o, "nNameHash"),
                o.GetInt32Property("nType"), o.GetSubCollection("m_Params")));

            MorphLayers = ReadArray(data, "m_MorphLayers", static o => new MorphLayer(
                o.GetStringProperty("m_Name") ?? string.Empty, ReadUInt32(o, "m_nNameHash"),
                ReadInt32Array(o, "m_Nodes"),
                [.. (o.GetArray("m_InitPos") ?? []).Select(static p => ReadVector3(p))],
                o.GetFloatArray("m_Gravity"), o.GetFloatArray("m_GoalStrength"), o.GetFloatArray("m_GoalDamping"),
                ReadUInt32(o, "m_nFlags")));
            RigidColliderPriorities = ReadArray(data, "m_RigidColliderPriorities", static o => new RigidColliderIndices(
                o.GetInt32Property("m_nTaperedCapsuleRigidIndex"), o.GetInt32Property("m_nSphereRigidIndex"),
                o.GetInt32Property("m_nBoxRigidIndex"), o.GetInt32Property("m_nSDFRigidIndex"),
                o.GetInt32Property("m_nCollisionPlaneIndex")));

            JiggleBones = ReadArray(data, "m_JiggleBones", static o =>
            {
                var bone = o.GetSubCollection("m_jiggleBone");
                return new IndexedJiggleBone(unchecked((int)ReadUInt32(o, "m_nNode")), unchecked((int)ReadUInt32(o, "m_nJiggleParent")),
                    bone is null ? default : new JiggleBone(
                        ReadUInt32(bone, "m_nFlags"), bone.GetFloatProperty("m_flLength"),
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
                        ReadVector3(bone.GetSubCollection("m_vPoint0")),
                        ReadVector3(bone.GetSubCollection("m_vPoint1")),
                        bone.GetInt32Property("m_nCollisionMask")));
            });

            BoneMergeLinks = ReadArray(data, "m_BoneMergeLinks", static o => new BoneMergeLink(
                ReadUInt32(o, "m_nParentHash"), o.GetInt32Property("m_nChildNode")));

            LockToParent = ReadArray(data, "m_LockToParent", static o => new LockToParentLink(
                ReadVector3(o.GetSubCollection("vOffset")),
                o.GetInt32Property("nCtrlParent"), o.GetInt32Property("nCtrlChild")));
            LockToGoal = ReadInt32Array(data, "m_LockToGoal");
            lockedToParent = [.. LockToParent.Select(static link => link.CtrlChild)];
            lockedToGoal = [.. LockToGoal];

            CtrlOsOffsets = ReadArray(data, "m_CtrlOsOffsets", static o => new CtrlOsOffset(
                o.GetInt32Property("nCtrlParent"), o.GetInt32Property("nCtrlChild")));

            CtrlOffsets = ReadArray(data, "m_CtrlOffsets", static o => new CtrlOffset(
                ReadVector3(o.GetSubCollection("vOffset")),
                o.GetInt32Property("nCtrlParent"), o.GetInt32Property("nCtrlChild")));

            VertexSetNames = ReadUInt32Array(data, "m_VertexSetNames");
            DynNodeVertexSet = data.GetArray<byte>("m_DynNodeVertexSet") ?? [];
            LegacyStretchForce = data.GetFloatArray("m_LegacyStretchForce");

            var followNodeLinks = new Dictionary<int, (int Parent, float Weight)>();
            foreach (var follow in data.GetArray("m_FollowNodes") ?? [])
            {
                followNodeLinks.TryAdd(follow.GetInt32Property("nChildNode"),
                    (follow.GetInt32Property("nParentNode"), follow.GetFloatProperty("flWeight")));
            }

            FollowNodeLinks = followNodeLinks;
            LocalForceValues = data.GetFloatArray("m_LocalForce");
            LocalRotationValues = data.GetFloatArray("m_LocalRotation");
            HasAxialEdges = data.GetArray("m_AxialEdges") is { Count: > 0 };

            InternalPressure = data.GetFloatProperty("m_flInternalPressure");
            Windage = data.GetFloatProperty("m_flWindage");
            WindDrag = data.GetFloatProperty("m_flWindDrag");
            LocalForce = data.GetFloatProperty("m_flLocalForce");
            LocalRotation = data.GetFloatProperty("m_flLocalRotation");
            AddWorldCollisionRadius = data.GetFloatProperty("m_flAddWorldCollisionRadius");
            DefaultGravityScale = data.GetFloatProperty("m_flDefaultGravityScale", 1.0f);
            DefaultVelAirDrag = data.GetFloatProperty("m_flDefaultVelAirDrag");
            DefaultExpAirDrag = data.GetFloatProperty("m_flDefaultExpAirDrag");
            DefaultThreadStretch = data.GetFloatProperty("m_flDefaultThreadStretch");
            DefaultSurfaceStretch = data.GetFloatProperty("m_flDefaultSurfaceStretch");
            LocalDrag1 = data.GetFloatProperty("m_flLocalDrag1");
            ExtraIterations = data.GetInt32Property("m_nExtraIterations");
            ExtraGoalIterations = data.GetInt32Property("m_nExtraGoalIterations");
            ExtraPressureIterations = data.GetInt32Property("m_nExtraPressureIterations");
            VelocitySmoothRate = data.GetFloatProperty("m_flRodVelocitySmoothRate");
            VelocitySmoothIterations = data.GetInt32Property("m_nRodVelocitySmoothIterations");
            DynamicNodeFlags = ReadUInt32(data, "m_nDynamicNodeFlags");
            StaticNodeFlags = ReadUInt32(data, "m_nStaticNodeFlags");
            RotationLockedStaticNodeCount = data.GetInt32Property("m_nRotLockStaticNodes");
            MotionSmoothCdt = data.GetFloatProperty("m_flMotionSmoothCDT");
        }

        /// <summary>Gets the raw <c>m_pFeModel</c> data.</summary>
        internal KVObject Data { get; }

        /// <summary>Gets whether this model has any control nodes.</summary>
        public bool HasData => CtrlNames.Length > 0;

        /// <summary>
        /// Gets the per-node control names. Generated proxy-mesh nodes are prefixed with <c>$</c>, the rest are skeleton
        /// bone names.
        /// </summary>
        public string[] CtrlNames { get; }

        /// <summary>Gets the per-node parent node index, or -1 for a root. Empty when the model carries none.</summary>
        public int[] SkelParents { get; }

        /// <summary>Gets the per-node inverse mass. 0 marks a static (pinned) node.</summary>
        public float[] NodeInvMasses { get; }

        /// <summary>Gets the total number of control nodes.</summary>
        public int NodeCount { get; }

        /// <summary>Gets the number of leading static (pinned) nodes.</summary>
        public int StaticNodeCount { get; }

        /// <summary>
        /// Gets the index of the first position-driven node, or <see cref="NodeCount"/> when the model does not carry one.
        /// </summary>
        public int FirstPositionDrivenNode { get; }

        /// <summary>Gets the per-node rest positions in model space (from <c>m_InitPose</c>).</summary>
        public Vector3[] InitPosePositions { get; }

        /// <summary>Gets the per-node rest orientations (from <c>m_InitPose</c>).</summary>
        public Quaternion[] InitPoseRotations { get; }

        /// <summary>Gets the cloth surface quads, each as four control-node indices.</summary>
        public int[][] Quads { get; }

        /// <summary>Gets the cloth surface triangles, each as three control-node indices.</summary>
        public int[][] Tris { get; }

        /// <summary>Gets whether the cloth has quad or triangle solve elements.</summary>
        public bool HasSurfaceElements => Quads.Length > 0 || Tris.Length > 0;

        /// <summary>
        /// Gets the authored proxy-mesh faces (from <c>m_SourceElems</c>), as control-node indices in winding order.
        /// </summary>
        public int[][] SourceFaces { get; }

        /// <summary>Gets how many of <see cref="SourceFaces"/> are triangles.</summary>
        internal int SourceTriangleCount { get; }

        /// <summary>Gets the two-node elements of <c>m_SourceElems</c>, one per authored <c>ClothSpring</c>.</summary>
        public (int, int)[] SourceSprings { get; }

        /// <summary>
        /// Gets the distance constraints between control nodes (<c>m_Rods</c>). Rods with a missing end or both ends on
        /// one node are left out.
        /// </summary>
        public Rod[] Rods { get; }

        /// <summary>
        /// Gets the distinct rods of <c>m_SimdRodsAnim</c>. Lanes repeated to fill a SIMD block collapse into one, and
        /// lanes with both ends on one node are left out.
        /// </summary>
        public IReadOnlyList<AnimRod> AnimRods => animRods ??= BuildAnimRods();

        /// <summary>
        /// Gets the explicit orientation basis of nodes that have one (<c>m_NodeBases</c>), keyed by control-node index.
        /// Where a node has several records, the last one is kept.
        /// </summary>
        public IReadOnlyDictionary<int, NodeBasis> NodeBases { get; }

        /// <summary>
        /// Gets every <c>m_NodeBases</c> record in array order. Older models can carry several records for one node.
        /// </summary>
        public IReadOnlyList<(int Node, NodeBasis Basis)> NodeBaseRecords { get; }

        /// <summary>Gets the per-node solver integrator parameters (<c>m_NodeIntegrator</c>).</summary>
        public NodeIntegrator[] NodeIntegrators { get; }

        /// <summary>
        /// Gets the world-collision radii (<c>m_NodeCollisionRadii</c>), indexed by control-node index minus
        /// <see cref="StaticNodeCount"/>.
        /// </summary>
        public float[] NodeCollisionRadii { get; }

        /// <summary>Gets the per-dynamic-node friction (<c>m_DynNodeFriction</c>).</summary>
        public float[] DynNodeFriction { get; }

        /// <summary>
        /// Gets the per-dynamic-node collision masks: the leading entries of <c>m_TreeCollisionMasks</c>, whose remainder
        /// holds the OR of each subtree. Empty when that array is absent or not <c>2 * dynamicNodes - 1</c> long.
        /// </summary>
        public int[] NodeCollisionMasks { get; }

        /// <summary>Gets the control nodes that collide with the world (<c>m_WorldCollisionNodes</c>).</summary>
        public IReadOnlySet<int> WorldCollisionNodes { get; }

        /// <summary>Gets the world and ground friction of each world-colliding node (<c>m_WorldCollisionParams</c>).</summary>
        public IReadOnlyDictionary<int, (float World, float Ground)> WorldCollisionFriction { get; }

        /// <summary>
        /// Gets the maximum distance each constrained node may stray from its animated position, with its relaxation
        /// factor (<c>m_AnimStrayRadii</c>).
        /// </summary>
        public IReadOnlyDictionary<int, (float MaxDistance, float RelaxationFactor)> AnimStrayRadii { get; }

        /// <summary>Gets the control nodes driven by a fit matrix (<c>m_FitMatrices</c>).</summary>
        public IReadOnlySet<int> FitMatrixNodes { get; }

        /// <summary>Gets the control nodes each fit matrix is fit over, keyed by the bone it drives.</summary>
        public IReadOnlyDictionary<int, int[]> FitMatrixTargets { get; }

        /// <summary>Gets the control nodes named by any twist constraint (<c>m_Twists</c>).</summary>
        public IReadOnlySet<int> TwistNodes => twistNodes ??= TwistRecords.SelectMany(static twist => (int[])[twist.Orient, twist.End]).ToHashSet();

        /// <summary>
        /// Gets every <c>m_Twists</c> record in array order. The compiler emits one record per directed pair per chain
        /// declaration without de-duplicating, so a node or pair can appear several times.
        /// </summary>
        public IReadOnlyList<TwistRecord> TwistRecords { get; }

        /// <summary>Gets <c>m_flInternalPressure</c>.</summary>
        public float InternalPressure { get; }

        /// <summary>Gets <c>m_flWindage</c>.</summary>
        public float Windage { get; }

        /// <summary>Gets <c>m_flWindDrag</c>.</summary>
        public float WindDrag { get; }

        /// <summary>Gets <c>m_flLocalForce</c>.</summary>
        public float LocalForce { get; }

        /// <summary>Gets <c>m_flLocalRotation</c>.</summary>
        public float LocalRotation { get; }

        /// <summary>Gets <c>m_flAddWorldCollisionRadius</c>.</summary>
        public float AddWorldCollisionRadius { get; }

        /// <summary>Gets <c>m_flDefaultGravityScale</c>, 1 when absent.</summary>
        public float DefaultGravityScale { get; }

        /// <summary>Gets <c>m_flDefaultVelAirDrag</c>.</summary>
        public float DefaultVelAirDrag { get; }

        /// <summary>Gets <c>m_flDefaultExpAirDrag</c>.</summary>
        public float DefaultExpAirDrag { get; }

        /// <summary>Gets <c>m_flDefaultThreadStretch</c>.</summary>
        public float DefaultThreadStretch { get; }

        /// <summary>Gets <c>m_flDefaultSurfaceStretch</c>.</summary>
        public float DefaultSurfaceStretch { get; }

        /// <summary>Gets <c>m_flLocalDrag1</c>.</summary>
        public float LocalDrag1 { get; }

        /// <summary>Gets <c>m_nExtraIterations</c>.</summary>
        public int ExtraIterations { get; }

        /// <summary>Gets <c>m_nExtraGoalIterations</c>.</summary>
        public int ExtraGoalIterations { get; }

        /// <summary>Gets <c>m_nExtraPressureIterations</c>.</summary>
        public int ExtraPressureIterations { get; }

        /// <summary>Gets <c>m_flRodVelocitySmoothRate</c>.</summary>
        public float VelocitySmoothRate { get; }

        /// <summary>Gets <c>m_nRodVelocitySmoothIterations</c>.</summary>
        public int VelocitySmoothIterations { get; }

        /// <summary>Gets <c>m_nDynamicNodeFlags</c>.</summary>
        public uint DynamicNodeFlags { get; }

        /// <summary>Gets <c>m_nStaticNodeFlags</c>.</summary>
        public uint StaticNodeFlags { get; }

        /// <summary>Gets <c>m_nRotLockStaticNodes</c>, the number of leading static nodes whose rotation is locked.</summary>
        public int RotationLockedStaticNodeCount { get; }

        /// <summary>Gets <c>m_flMotionSmoothCDT</c>.</summary>
        public float MotionSmoothCdt { get; }

        /// <summary>Gets whether the cloth carries per-node local force or rotation values.</summary>
        public bool HasPerNodeLocalForce => LocalForceValues.Length > 0 || LocalRotationValues.Length > 0;

        /// <summary>
        /// Gets the per-dynamic-node local force multipliers (<c>m_LocalForce</c>), empty when the cloth uses
        /// <see cref="LocalForce"/>.
        /// </summary>
        public float[] LocalForceValues { get; }

        /// <summary>
        /// Gets the per-dynamic-node local rotation multipliers (<c>m_LocalRotation</c>), empty when the cloth uses
        /// <see cref="LocalRotation"/>.
        /// </summary>
        public float[] LocalRotationValues { get; }

        /// <summary>Gets whether the cloth carries axial bend edges, which the rigid edge hinge option produces.</summary>
        public bool HasAxialEdges { get; }

        /// <summary>Gets the three-node bend constraints (<c>m_KelagerBends</c>), built for chain joints with a stiff hinge.</summary>
        public IReadOnlyList<KelagerBend> KelagerBends { get; }

        /// <summary>Gets the named vertex selections (<c>m_VertexMaps</c>).</summary>
        public IReadOnlyList<VertexMap> VertexMaps { get; }

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

        /// <summary>
        /// Gets the goal-damped spring integrator bitmask (<c>m_GoalDampedSpringIntegrators</c>), one bit per dynamic node.
        /// </summary>
        public uint[] GoalDampedSpringIntegrators { get; }

        /// <summary>Gets the named cloth effects (<c>m_Effects</c>).</summary>
        public Effect[] Effects { get; }

        /// <summary>Gets the deprecated morph layers (<c>m_MorphLayers</c>).</summary>
        public MorphLayer[] MorphLayers { get; }

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

        /// <summary>Gets the parent and child of every object-space-offset virtual node (<c>m_CtrlOsOffsets</c>).</summary>
        public CtrlOsOffset[] CtrlOsOffsets { get; }

        /// <summary>Gets each follower node's leader and follow weight (<c>m_FollowNodes</c>).</summary>
        public IReadOnlyDictionary<int, (int Parent, float Weight)> FollowNodeLinks { get; }

        /// <summary>Gets the generated-node anchor offsets (<c>m_CtrlOffsets</c>).</summary>
        public CtrlOffset[] CtrlOffsets { get; }

        /// <summary>Gets the vertex set name hashes (<c>m_VertexSetNames</c>), indexed by <see cref="DynNodeVertexSet"/>.</summary>
        public uint[] VertexSetNames { get; }

        /// <summary>Gets each dynamic node's index into <see cref="VertexSetNames"/> (<c>m_DynNodeVertexSet</c>).</summary>
        public byte[] DynNodeVertexSet { get; }

        /// <summary>Gets the legacy per-node stretch force (<c>m_LegacyStretchForce</c>).</summary>
        public float[] LegacyStretchForce { get; }

        /// <summary>Returns whether a control-node name is a generated cloth proxy node rather than a skeleton bone.</summary>
        public static bool IsProxyNodeName(string? name)
            => string.IsNullOrEmpty(name) || name.StartsWith('$');

        /// <summary>Returns whether <paramref name="node"/> is a static (pinned) node, with zero inverse mass.</summary>
        public bool IsStatic(int node)
            => node >= 0 && node < NodeInvMasses.Length && NodeInvMasses[node] == 0f;

        /// <summary>Gets the integrator parameters for <paramref name="node"/>, or a zeroed struct when absent.</summary>
        public NodeIntegrator GetIntegrator(int node)
            => node >= 0 && node < NodeIntegrators.Length ? NodeIntegrators[node] : default;

        /// <summary>Gets the world-collision radius for control node <paramref name="node"/>, or 0 when absent.</summary>
        public float GetCollisionRadius(int node) => DynamicNodeValue(NodeCollisionRadii, node, 0f);

        /// <summary>
        /// Gets the collision mask of control node <paramref name="node"/>, or <see cref="DefaultNodeCollisionMask"/>
        /// when the model records none.
        /// </summary>
        public int GetNodeCollisionMask(int node) => DynamicNodeValue(NodeCollisionMasks, node, DefaultNodeCollisionMask);

        /// <summary>Gets the world and ground friction for <paramref name="node"/>, or zero for both.</summary>
        public (float World, float Ground) GetWorldFriction(int node)
            => WorldCollisionFriction.GetValueOrDefault(node);

        /// <summary>Returns whether <paramref name="node"/> collides with the world.</summary>
        public bool IsWorldCollisionNode(int node) => WorldCollisionNodes.Contains(node);

        /// <summary>Gets the stray radius for <paramref name="node"/>, or 0 when unconstrained.</summary>
        public float GetStrayRadius(int node) => AnimStrayRadii.GetValueOrDefault(node).MaxDistance;

        /// <summary>
        /// Gets whether <paramref name="node"/> keeps its rotation free. Static nodes are ordered rotation-locked first,
        /// so only the nodes below <see cref="RotationLockedStaticNodeCount"/> are locked.
        /// </summary>
        public bool AllowsRotation(int node) => node >= RotationLockedStaticNodeCount;

        /// <summary>Gets whether <paramref name="node"/> is held at a fixed offset from its parent (<c>m_LockToParent</c>).</summary>
        public bool IsLockedToParent(int node) => lockedToParent.Contains(node);

        /// <summary>
        /// Gets whether <paramref name="node"/> is held at its animated goal (<c>m_LockToGoal</c>), the lock a
        /// non-simulated node without a parent takes.
        /// </summary>
        public bool IsLockedToGoal(int node) => lockedToGoal.Contains(node);

        /// <summary>Gets the friction painted on <paramref name="node"/>, or 0 when it has none.</summary>
        public float GetNodeFriction(int node) => DynamicNodeValue(DynNodeFriction, node, 0f);

        /// <summary>Gets the local force multiplier of control node <paramref name="node"/>, or 0 when it has none.</summary>
        internal float GetLocalForce(int node) => DynamicNodeValue(LocalForceValues, node, 0f);

        /// <summary>Gets the local rotation multiplier of control node <paramref name="node"/>, or 0 when it has none.</summary>
        internal float GetLocalRotation(int node) => DynamicNodeValue(LocalRotationValues, node, 0f);

        /// <summary>Flattens a SIMD <c>nNode</c> block, stored either as rows of four lanes or as one row-major array.</summary>
        internal static List<int> FlattenSimdNodes(KVObject nNode, int capacity)
        {
            var flat = new List<int>(capacity);
            foreach (var row in nNode.AsArraySpan())
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

            return flat;
        }

        /// <summary>Reads a per-dynamic-node array, which starts past the static nodes, at control node <paramref name="node"/>.</summary>
        private T DynamicNodeValue<T>(T[] values, int node, T fallback)
        {
            var dynamicIndex = node - StaticNodeCount;
            return dynamicIndex >= 0 && dynamicIndex < values.Length ? values[dynamicIndex] : fallback;
        }

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

        private static List<KelagerBend> ReadKelagerBends(KVObject data)
        {
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

            return kelagerBends;
        }

        /// <summary>Reads <c>m_VertexMaps</c> with their weights from <c>m_VertexMapValues</c>.</summary>
        private static List<VertexMap> ReadVertexMaps(KVObject data)
        {
            var mapValues = data.GetIntegerArray("m_VertexMapValues");
            var vertexMaps = new List<VertexMap>();
            foreach (var map in data.GetArray("m_VertexMaps") ?? [])
            {
                var count = Math.Max(map.GetInt32Property("nVertexCount"), 0);
                var offset = map.GetInt32Property("nMapOffset");
                var weights = new float[count];
                for (var i = 0; i < count && offset + i < mapValues.Length; i++)
                {
                    weights[i] = mapValues[offset + i] / 255f;
                }

                vertexMaps.Add(new VertexMap(
                    map.GetStringProperty("sName") ?? string.Empty,
                    ReadUInt32(map, "nNameHash"),
                    map.GetInt32Property("nVertexBase"),
                    count,
                    ReadVector3(map.GetSubCollection("vCenterOfMass")),
                    weights,
                    map.GetFloatProperty("flVolumetricSolveStrength"),
                    map.GetInt32Property("nScaleSourceNode", -1)));
            }

            return vertexMaps;
        }

        private static Rod[] ReadRods(KVObject data)
            => [.. ReadArray(data, "m_Rods", static o =>
            {
                var nodes = o.GetIntegerArray("nNode");
                return new Rod(
                    nodes.Length > 0 ? (int)nodes[0] : -1,
                    nodes.Length > 1 ? (int)nodes[1] : -1,
                    o.GetFloatProperty("flMinDist"),
                    o.GetFloatProperty("flMaxDist"),
                    o.GetFloatProperty("flWeight0"),
                    o.GetFloatProperty("flRelaxationFactor"));
            }).Where(static r => r.NodeA >= 0 && r.NodeB >= 0 && r.NodeA != r.NodeB)];

        private static (int[] Masks, HashSet<int> Nodes, Dictionary<int, (float World, float Ground)> Friction) ReadWorldCollision(
            KVObject data, int dynamicNodeCount)
        {
            var treeMasks = data.GetIntegerArray("m_TreeCollisionMasks");
            int[] nodeCollisionMasks = dynamicNodeCount > 0 && treeMasks.Length == (2 * dynamicNodeCount) - 1
                ? [.. treeMasks.Take(dynamicNodeCount).Select(static v => (int)v)]
                : [];
            var worldCollisionOrder = ReadInt32Array(data, "m_WorldCollisionNodes");

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

            return (nodeCollisionMasks, worldCollisionOrder.ToHashSet(), worldFriction);
        }

        /// <summary>Reads the single-node records of <c>m_AnimStrayRadii</c>, keyed by node.</summary>
        private static Dictionary<int, (float MaxDistance, float RelaxationFactor)> ReadAnimStrayRadii(KVObject data)
        {
            var strayRadii = new Dictionary<int, (float MaxDistance, float RelaxationFactor)>();
            foreach (var entry in data.GetArray("m_AnimStrayRadii") ?? [])
            {
                var nodes = entry.GetIntegerArray("nNode");
                if (nodes.Length >= 2 && nodes[0] == nodes[1])
                {
                    strayRadii[(int)nodes[0]] = (entry.GetFloatProperty("flMaxDist"), entry.GetFloatProperty("flRelaxationFactor"));
                }
            }

            return strayRadii;
        }

        /// <summary>Reads the fit-matrix bones and the nodes each fit is taken over, from its <c>m_FitWeights</c> range.</summary>
        private static (HashSet<int> Nodes, Dictionary<int, int[]> Targets) ReadFitMatrices(KVObject data)
        {
            var fitMatrixNodes = new HashSet<int>();
            var fitTargets = new Dictionary<int, int[]>();
            var fitMatrices = data.GetArray("m_FitMatrices");
            if (fitMatrices is null)
            {
                return (fitMatrixNodes, fitTargets);
            }

            var fitWeights = data.GetArray("m_FitWeights") ?? [];
            var rangeBegin = 0;
            foreach (var fit in fitMatrices)
            {
                var bone = fit.GetInt32Property("nNode");
                var rangeEnd = fit.GetInt32Property("nEnd");
                var targets = new List<int>();
                for (var i = rangeBegin; i < rangeEnd && i < fitWeights.Count; i++)
                {
                    targets.Add(fitWeights[i].GetInt32Property("nNode"));
                }

                fitMatrixNodes.Add(bone);
                fitTargets[bone] = [.. targets];
                rangeBegin = rangeEnd;
            }

            return (fitMatrixNodes, fitTargets);
        }

        private static int[][] ReadNodeIndexArray(KVObject data, string key, int expectedLength)
        {
            var faces = new List<int[]>();
            foreach (var face in data.GetArray(key) ?? [])
            {
                var nodes = face.GetIntegerArray("nNode");
                if (nodes.Length >= expectedLength)
                {
                    faces.Add([.. nodes.Take(expectedLength).Select(static v => (int)v)]);
                }
            }

            return [.. faces];
        }

        /// <summary>
        /// Reads <c>m_SourceElems</c>: a count per arity, then that many elements of each arity in turn. Returns nothing
        /// when the counts do not add up to the array length.
        /// </summary>
        private static (int[][] Faces, (int, int)[] Springs, int Triangles) ReadSourceElems(KVObject data)
        {
            if (!data.IsNotBlobType("m_SourceElems"))
            {
                return ([], [], 0);
            }

            var elems = data.GetIntegerArray("m_SourceElems");
            if (elems.Length < SourceElemArities)
            {
                return ([], [], 0);
            }

            var counted = SourceElemArities;
            for (var arity = 1; arity <= SourceElemArities; arity++)
            {
                var count = elems[arity - 1];
                if (count < 0 || count > elems.Length)
                {
                    return ([], [], 0);
                }

                counted += arity * (int)count;
            }

            if (counted != elems.Length)
            {
                return ([], [], 0);
            }

            var faces = new List<int[]>();
            var springs = new List<(int, int)>();
            var triangles = 0;
            var read = SourceElemArities;
            for (var arity = 1; arity <= SourceElemArities; arity++)
            {
                for (var remaining = (int)elems[arity - 1]; remaining > 0; remaining--, read += arity)
                {
                    if (arity == 1)
                    {
                        continue;
                    }

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

                    // Collapse repeated corners, so a degenerate quad reads as a triangle
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

                        if (arity == 3)
                        {
                            triangles++;
                        }
                    }
                }
            }

            return ([.. faces], [.. springs], triangles);
        }

        /// <summary>Reads a three-component vector, or zero when <paramref name="value"/> is absent or shorter.</summary>
        private static Vector3 ReadVector3(KVObject? value) => value is { Count: >= 3 } ? value.ToVector3() : default;

        /// <summary>Reads an unsigned 32-bit field, keeping the bits of a value stored as a negative integer.</summary>
        private static uint ReadUInt32(KVObject data, string key) => unchecked((uint)data.GetIntegerProperty(key));

        /// <summary>Reads an integer array as unsigned values, keeping the bits of a negative entry.</summary>
        private static uint[] ReadUInt32Array(KVObject data, string key)
            => [.. data.GetIntegerArray(key).Select(static value => unchecked((uint)value))];

        private static int[] ReadInt32Array(KVObject data, string key)
            => [.. data.GetIntegerArray(key).Select(static value => (int)value)];

        private static T[] ReadArray<T>(KVObject data, string key, Func<KVObject, T> map)
        {
            var array = data.GetArray(key);
            return array is null ? [] : [.. array.Select(map)];
        }
    }
}
