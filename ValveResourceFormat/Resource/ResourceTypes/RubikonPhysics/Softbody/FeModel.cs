using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    /// <summary>
    /// Soft-body (cloth) model embedded in a physics aggregate (<c>m_pFeModel</c>). Every member mirrors one
    /// <c>PhysFeModelDesc_t</c> field as serialized; arrays are parsed on first access.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/PhysFeModelDesc_t">PhysFeModelDesc_t</seealso>
    public sealed class FeModel
    {
        private uint[]? ctrlHash;
        private string[]? ctrlName;
        private int[]? ropes;
        private FeTransform[]? initPose;
        private uint[]? antiTunnelBytecode;
        private int[]? antiTunnelTargetNodes;
        private float[]? nodeInvMasses;
        private float[]? legacyStretchForce;
        private float[]? nodeCollisionRadii;
        private float[]? dynNodeFriction;
        private float[]? localRotations;
        private float[]? localForces;
        private int[]? worldCollisionNodes;
        private int[]? treeParents;
        private int[]? treeCollisionMasks;
        private int[]? freeNodes;
        private int[]? sourceElems;
        private uint[]? goalDampedSpringIntegrators;
        private byte[]? dynNodeVertexSet;
        private uint[]? vertexSetNames;
        private byte[]? morphSetData;
        private byte[]? vertexMapValues;
        private int[]? lockToGoal;
        private int[]? skelParents;
        private FeNodeBase[]? nodeBases;
        private FeQuad[]? quads;
        private FeTri[]? tris;
        private FeRodConstraint[]? rods;
        private FeTwistConstraint[]? twists;
        private FeHingeLimit[]? hingeLimits;
        private FeDynKinLink[]? dynKinLinks;
        private FeBoneMergeLink[]? boneMergeLinks;
        private FeNodeStrayBox[]? nodeStrayBoxes;
        private FeAxialEdgeBend[]? axialEdges;
        private FeCtrlOffset[]? ctrlOffsets;
        private FeCtrlOsOffset[]? ctrlOsOffsets;
        private FeFollowNode[]? followNodes;
        private FeNodeIntegrator[]? nodeIntegrator;
        private FeSpringIntegrator[]? springIntegrator;
        private FeTreeChildren[]? treeChildren;
        private FeFitMatrix[]? fitMatrices;
        private FeFitWeight[]? fitWeights;
        private FeNodeReverseOffset[]? reverseOffsets;
        private FeAnimStrayRadius[]? animStrayRadii;
        private FeKelagerBend[]? kelagerBends;
        private FeCtrlSoftOffset[]? ctrlSoftOffsets;
        private FeCtrlOffset[]? lockToParent;
        private FeNodeWindBase[]? dynNodeWindBases;
        private FeSimdNodeBase[]? simdNodeBases;
        private FeSimdQuad[]? simdQuads;
        private FeSimdTri[]? simdTris;
        private FeSimdRodConstraint[]? simdRods;
        private FeSimdRodConstraintAnim[]? simdRodsAnim;
        private FeSimdSpringIntegrator[]? simdSpringIntegrator;
        private FeSimdAnimStrayRadius[]? simdAnimStrayRadii;
        private FeCollisionPlane[]? collisionPlanes;
        private FeWorldCollisionParams[]? worldCollisionParams;
        private FeTaperedCapsuleStretch[]? taperedCapsuleStretches;
        private FeTaperedCapsuleRigid[]? taperedCapsuleRigids;
        private FeSphereRigid[]? sphereRigids;
        private FeSDFRigid[]? sdfRigids;
        private FeBoxRigid[]? boxRigids;
        private FeRigidColliderIndices[]? rigidColliderPriorities;
        private FeAntiTunnelProbe[]? antiTunnelProbes;
        private FeModelSelfCollisionLayer[]? selfCollisionLayers;
        private FeEffectDesc[]? effects;
        private FeIndexedJiggleBone[]? jiggleBones;
        private FeVertexMapDesc[]? vertexMaps;
        private FeMorphLayer[]? morphLayers;

        /// <summary>
        /// Initializes a new instance of the <see cref="FeModel"/> class from a parsed <c>m_pFeModel</c> sub-object.
        /// </summary>
        public FeModel(KVObject data)
        {
            Data = data;
            StaticNodeFlags = ReadUInt32(data, "m_nStaticNodeFlags");
            DynamicNodeFlags = ReadUInt32(data, "m_nDynamicNodeFlags");
            LocalForce = data.GetFloatProperty("m_flLocalForce");
            LocalRotation = data.GetFloatProperty("m_flLocalRotation");
            NodeCount = data.GetInt32Property("m_nNodeCount");
            StaticNodes = data.GetInt32Property("m_nStaticNodes");
            RotLockStaticNodes = data.GetInt32Property("m_nRotLockStaticNodes");
            FirstPositionDrivenNode = data.ContainsKey("m_nFirstPositionDrivenNode")
                ? data.GetInt32Property("m_nFirstPositionDrivenNode")
                : null;
            SimdTriCount1 = data.GetInt32Property("m_nSimdTriCount1");
            SimdTriCount2 = data.GetInt32Property("m_nSimdTriCount2");
            SimdQuadCount1 = data.GetInt32Property("m_nSimdQuadCount1");
            SimdQuadCount2 = data.GetInt32Property("m_nSimdQuadCount2");
            QuadCount1 = data.GetInt32Property("m_nQuadCount1");
            QuadCount2 = data.GetInt32Property("m_nQuadCount2");
            TreeDepth = data.GetInt32Property("m_nTreeDepth");
            NodeBaseJiggleboneDependsCount = data.GetInt32Property("m_nNodeBaseJiggleboneDependsCount");
            RopeCount = data.GetInt32Property("m_nRopeCount");
            TriCount1 = data.GetInt32Property("m_nTriCount1");
            TriCount2 = data.GetInt32Property("m_nTriCount2");
            ReservedUint8 = data.GetInt32Property("m_nReservedUint8");
            ExtraPressureIterations = data.GetInt32Property("m_nExtraPressureIterations");
            ExtraGoalIterations = data.GetInt32Property("m_nExtraGoalIterations");
            ExtraIterations = data.GetInt32Property("m_nExtraIterations");
            InternalPressure = data.GetFloatProperty("m_flInternalPressure");
            DefaultTimeDilation = data.GetFloatProperty("m_flDefaultTimeDilation");
            Windage = data.GetFloatProperty("m_flWindage");
            WindDrag = data.GetFloatProperty("m_flWindDrag");
            DefaultSurfaceStretch = data.GetFloatProperty("m_flDefaultSurfaceStretch");
            DefaultThreadStretch = data.GetFloatProperty("m_flDefaultThreadStretch");
            DefaultGravityScale = data.ContainsKey("m_flDefaultGravityScale")
                ? data.GetFloatProperty("m_flDefaultGravityScale")
                : null;
            DefaultVelAirDrag = data.GetFloatProperty("m_flDefaultVelAirDrag");
            DefaultExpAirDrag = data.GetFloatProperty("m_flDefaultExpAirDrag");
            DefaultVelQuadAirDrag = data.GetFloatProperty("m_flDefaultVelQuadAirDrag");
            DefaultExpQuadAirDrag = data.GetFloatProperty("m_flDefaultExpQuadAirDrag");
            DefaultVelRodAirDrag = ReadOptionalFloat(data, "m_flDefaultVelRodAirDrag");
            DefaultExpRodAirDrag = ReadOptionalFloat(data, "m_flDefaultExpRodAirDrag");
            RodVelocitySmoothRate = data.GetFloatProperty("m_flRodVelocitySmoothRate");
            QuadVelocitySmoothRate = data.GetFloatProperty("m_flQuadVelocitySmoothRate");
            AddWorldCollisionRadius = data.GetFloatProperty("m_flAddWorldCollisionRadius");
            DefaultVolumetricSolveAmount = data.GetFloatProperty("m_flDefaultVolumetricSolveAmount");
            MotionSmoothCdt = data.GetFloatProperty("m_flMotionSmoothCDT");
            LocalDrag1 = data.GetFloatProperty("m_flLocalDrag1");
            RodVelocitySmoothIterations = data.GetInt32Property("m_nRodVelocitySmoothIterations");
            QuadVelocitySmoothIterations = data.GetInt32Property("m_nQuadVelocitySmoothIterations");
        }

        #region Scalars

        /// <summary>Gets the raw <c>m_pFeModel</c> data.</summary>
        public KVObject Data { get; }

        /// <summary>Gets <c>m_nStaticNodeFlags</c>.</summary>
        public uint StaticNodeFlags { get; }

        /// <summary>Gets <c>m_nDynamicNodeFlags</c>.</summary>
        public uint DynamicNodeFlags { get; }

        /// <summary>Gets <c>m_flLocalForce</c>, the local force multiplier of every node when <see cref="LocalForces"/> is empty.</summary>
        public float LocalForce { get; }

        /// <summary>
        /// Gets <c>m_flLocalRotation</c>, the local rotation multiplier of every node when <see cref="LocalRotations"/> is empty.
        /// </summary>
        public float LocalRotation { get; }

        /// <summary>Gets <c>m_nNodeCount</c>, the total number of control nodes.</summary>
        public int NodeCount { get; }

        /// <summary>Gets <c>m_nStaticNodes</c>, the number of leading static (pinned) nodes.</summary>
        public int StaticNodes { get; }

        /// <summary>Gets <c>m_nRotLockStaticNodes</c>, the number of leading static nodes whose rotation is locked.</summary>
        public int RotLockStaticNodes { get; }

        /// <summary>Gets <c>m_nFirstPositionDrivenNode</c>, or null when the file does not carry it.</summary>
        public int? FirstPositionDrivenNode { get; }

        /// <summary>Gets <c>m_nSimdTriCount1</c>.</summary>
        public int SimdTriCount1 { get; }

        /// <summary>Gets <c>m_nSimdTriCount2</c>.</summary>
        public int SimdTriCount2 { get; }

        /// <summary>Gets <c>m_nSimdQuadCount1</c>.</summary>
        public int SimdQuadCount1 { get; }

        /// <summary>Gets <c>m_nSimdQuadCount2</c>.</summary>
        public int SimdQuadCount2 { get; }

        /// <summary>Gets <c>m_nQuadCount1</c>.</summary>
        public int QuadCount1 { get; }

        /// <summary>Gets <c>m_nQuadCount2</c>.</summary>
        public int QuadCount2 { get; }

        /// <summary>Gets <c>m_nTreeDepth</c>, the depth of the collision bounding-volume tree.</summary>
        public int TreeDepth { get; }

        /// <summary>Gets <c>m_nNodeBaseJiggleboneDependsCount</c>.</summary>
        public int NodeBaseJiggleboneDependsCount { get; }

        /// <summary>Gets <c>m_nRopeCount</c>, the number of runs in <see cref="Ropes"/>.</summary>
        public int RopeCount { get; }

        /// <summary>Gets <c>m_nTriCount1</c>.</summary>
        public int TriCount1 { get; }

        /// <summary>Gets <c>m_nTriCount2</c>.</summary>
        public int TriCount2 { get; }

        /// <summary>Gets <c>m_nReservedUint8</c>.</summary>
        public int ReservedUint8 { get; }

        /// <summary>Gets <c>m_nExtraPressureIterations</c>.</summary>
        public int ExtraPressureIterations { get; }

        /// <summary>Gets <c>m_nExtraGoalIterations</c>.</summary>
        public int ExtraGoalIterations { get; }

        /// <summary>Gets <c>m_nExtraIterations</c>.</summary>
        public int ExtraIterations { get; }

        /// <summary>Gets <c>m_flInternalPressure</c>.</summary>
        public float InternalPressure { get; }

        /// <summary>Gets <c>m_flDefaultTimeDilation</c>.</summary>
        public float DefaultTimeDilation { get; }

        /// <summary>Gets <c>m_flWindage</c>.</summary>
        public float Windage { get; }

        /// <summary>Gets <c>m_flWindDrag</c>.</summary>
        public float WindDrag { get; }

        /// <summary>Gets <c>m_flDefaultSurfaceStretch</c>.</summary>
        public float DefaultSurfaceStretch { get; }

        /// <summary>Gets <c>m_flDefaultThreadStretch</c>.</summary>
        public float DefaultThreadStretch { get; }

        /// <summary>Gets <c>m_flDefaultGravityScale</c>, or null when the file does not carry it.</summary>
        public float? DefaultGravityScale { get; }

        /// <summary>Gets <c>m_flDefaultVelAirDrag</c>.</summary>
        public float DefaultVelAirDrag { get; }

        /// <summary>Gets <c>m_flDefaultExpAirDrag</c>.</summary>
        public float DefaultExpAirDrag { get; }

        /// <summary>Gets <c>m_flDefaultVelQuadAirDrag</c>.</summary>
        public float DefaultVelQuadAirDrag { get; }

        /// <summary>Gets <c>m_flDefaultExpQuadAirDrag</c>.</summary>
        public float DefaultExpQuadAirDrag { get; }

        /// <summary>Gets <c>m_flDefaultVelRodAirDrag</c>, or null when the file does not carry it (older files only).</summary>
        public float? DefaultVelRodAirDrag { get; }

        /// <summary>Gets <c>m_flDefaultExpRodAirDrag</c>, or null when the file does not carry it (older files only).</summary>
        public float? DefaultExpRodAirDrag { get; }

        /// <summary>Gets <c>m_flRodVelocitySmoothRate</c>.</summary>
        public float RodVelocitySmoothRate { get; }

        /// <summary>Gets <c>m_flQuadVelocitySmoothRate</c>.</summary>
        public float QuadVelocitySmoothRate { get; }

        /// <summary>Gets <c>m_flAddWorldCollisionRadius</c>.</summary>
        public float AddWorldCollisionRadius { get; }

        /// <summary>Gets <c>m_flDefaultVolumetricSolveAmount</c>.</summary>
        public float DefaultVolumetricSolveAmount { get; }

        /// <summary>Gets <c>m_flMotionSmoothCDT</c>.</summary>
        public float MotionSmoothCdt { get; }

        /// <summary>Gets <c>m_flLocalDrag1</c>.</summary>
        public float LocalDrag1 { get; }

        /// <summary>Gets <c>m_nRodVelocitySmoothIterations</c>.</summary>
        public int RodVelocitySmoothIterations { get; }

        /// <summary>Gets <c>m_nQuadVelocitySmoothIterations</c>.</summary>
        public int QuadVelocitySmoothIterations { get; }

        #endregion

        #region Nodes

        /// <summary>Gets <c>m_CtrlHash</c>, the hash of each <see cref="CtrlName"/>.</summary>
        public uint[] CtrlHash => ctrlHash ??= ReadUInt32Array(Data, "m_CtrlHash");

        /// <summary>
        /// Gets <c>m_CtrlName</c>, the per-node control names. Generated proxy-mesh nodes are prefixed with <c>$</c>, the
        /// rest are skeleton bone names.
        /// </summary>
        public string[] CtrlName => ctrlName ??= Data.GetArray<string>("m_CtrlName") ?? [];

        /// <summary>
        /// Gets <c>m_Ropes</c>: the exclusive end offset of each of the <see cref="RopeCount"/> runs, then the runs' nodes.
        /// </summary>
        public int[] Ropes => ropes ??= ReadInt32Array(Data, "m_Ropes");

        /// <summary>Gets <c>m_InitPose</c>, the per-node rest transform in model space.</summary>
        public FeTransform[] InitPose => initPose ??= ReadArray(Data, "m_InitPose", static o => ReadTransform(o));

        /// <summary>Gets <c>m_AntiTunnelBytecode</c>.</summary>
        public uint[] AntiTunnelBytecode => antiTunnelBytecode ??= ReadUInt32Array(Data, "m_AntiTunnelBytecode");

        /// <summary>Gets <c>m_AntiTunnelTargetNodes</c>, the nodes <see cref="AntiTunnelProbes"/> target.</summary>
        public int[] AntiTunnelTargetNodes => antiTunnelTargetNodes ??= ReadInt32Array(Data, "m_AntiTunnelTargetNodes");

        /// <summary>Gets <c>m_NodeInvMasses</c>, the per-node inverse mass. 0 marks a static (pinned) node.</summary>
        public float[] NodeInvMasses => nodeInvMasses ??= Data.GetFloatArray("m_NodeInvMasses");

        /// <summary>Gets <c>m_LegacyStretchForce</c>.</summary>
        public float[] LegacyStretchForce => legacyStretchForce ??= Data.GetFloatArray("m_LegacyStretchForce");

        /// <summary>
        /// Gets <c>m_NodeCollisionRadii</c>, the world-collision radii indexed by node minus <see cref="StaticNodes"/>.
        /// </summary>
        public float[] NodeCollisionRadii => nodeCollisionRadii ??= Data.GetFloatArray("m_NodeCollisionRadii");

        /// <summary>Gets <c>m_DynNodeFriction</c>, the friction indexed by node minus <see cref="StaticNodes"/>.</summary>
        public float[] DynNodeFriction => dynNodeFriction ??= Data.GetFloatArray("m_DynNodeFriction");

        /// <summary>Gets <c>m_LocalRotation</c>, the local rotation multipliers indexed by node minus <see cref="StaticNodes"/>.</summary>
        public float[] LocalRotations => localRotations ??= Data.GetFloatArray("m_LocalRotation");

        /// <summary>Gets <c>m_LocalForce</c>, the local force multipliers indexed by node minus <see cref="StaticNodes"/>.</summary>
        public float[] LocalForces => localForces ??= Data.GetFloatArray("m_LocalForce");

        /// <summary>Gets <c>m_WorldCollisionNodes</c>, the nodes that collide with the world.</summary>
        public int[] WorldCollisionNodes => worldCollisionNodes ??= ReadInt32Array(Data, "m_WorldCollisionNodes");

        /// <summary>Gets <c>m_TreeParents</c>.</summary>
        public int[] TreeParents => treeParents ??= ReadInt32Array(Data, "m_TreeParents");

        /// <summary>
        /// Gets <c>m_TreeCollisionMasks</c>: a collision mask per dynamic node, then the OR of each subtree.
        /// </summary>
        public int[] TreeCollisionMasks => treeCollisionMasks ??= ReadInt32Array(Data, "m_TreeCollisionMasks");

        /// <summary>Gets <c>m_FreeNodes</c>.</summary>
        public int[] FreeNodes => freeNodes ??= ReadInt32Array(Data, "m_FreeNodes");

        /// <summary>
        /// Gets <c>m_SourceElems</c>: a count per element arity from 1 to 4, then that many elements of each arity in turn.
        /// Empty when the file stores it as a blob.
        /// </summary>
        public int[] SourceElems => sourceElems ??= Data.IsNotBlobType("m_SourceElems") ? ReadInt32Array(Data, "m_SourceElems") : [];

        /// <summary>Gets <c>m_GoalDampedSpringIntegrators</c>, a bitmask with one bit per dynamic node.</summary>
        public uint[] GoalDampedSpringIntegrators => goalDampedSpringIntegrators ??= ReadUInt32Array(Data, "m_GoalDampedSpringIntegrators");

        /// <summary>Gets <c>m_DynNodeVertexSet</c>, each dynamic node's index into <see cref="VertexSetNames"/>.</summary>
        public byte[] DynNodeVertexSet => dynNodeVertexSet ??= Data.GetArray<byte>("m_DynNodeVertexSet") ?? [];

        /// <summary>Gets <c>m_VertexSetNames</c>, the vertex set name hashes.</summary>
        public uint[] VertexSetNames => vertexSetNames ??= ReadUInt32Array(Data, "m_VertexSetNames");

        /// <summary>Gets <c>m_MorphSetData</c>.</summary>
        public byte[] MorphSetData => morphSetData ??= ReadByteArray(Data, "m_MorphSetData");

        /// <summary>Gets <c>m_VertexMapValues</c>, the per-node weights of every <see cref="VertexMaps"/> entry, 0 to 255.</summary>
        public byte[] VertexMapValues => vertexMapValues ??= ReadByteArray(Data, "m_VertexMapValues");

        /// <summary>Gets <c>m_LockToGoal</c>, the nodes locked to their animated goal.</summary>
        public int[] LockToGoal => lockToGoal ??= ReadInt32Array(Data, "m_LockToGoal");

        /// <summary>Gets <c>m_SkelParents</c>, the per-node parent node index, or -1 for a root.</summary>
        public int[] SkelParents => skelParents ??= ReadInt32Array(Data, "m_SkelParents");

        #endregion

        #region Constraints

        /// <summary>Gets <c>m_NodeBases</c>, the explicit orientation basis of generated nodes.</summary>
        public FeNodeBase[] NodeBases => nodeBases ??= ReadArray(Data, "m_NodeBases", static o => new FeNodeBase(
            o.GetInt32Property("nNode"), ReadInt32Array(o, "nDummy"),
            o.GetInt32Property("nNodeX0"), o.GetInt32Property("nNodeX1"),
            o.GetInt32Property("nNodeY0"), o.GetInt32Property("nNodeY1"),
            ReadQuaternion(o.GetSubCollection("qAdjust"))));

        /// <summary>Gets <c>m_Quads</c>, the cloth surface quads.</summary>
        public FeQuad[] Quads => quads ??= ReadArray(Data, "m_Quads", static o => new FeQuad(
            ReadInt32Array(o, "nNode"), o.GetFloatProperty("flSlack"),
            ReadArray(o, "vShape", static v => v is { Count: >= 4 } ? v.ToVector4() : default)));

        /// <summary>Gets <c>m_Tris</c>, the cloth surface triangles.</summary>
        public FeTri[] Tris => tris ??= ReadArray(Data, "m_Tris", static o => new FeTri(
            ReadInt32Array(o, "nNode"), o.GetFloatProperty("w1"), o.GetFloatProperty("w2"), o.GetFloatProperty("v1x"),
            o.GetSubCollection("v2") is { Count: >= 2 } v2 ? v2.ToVector2() : default));

        /// <summary>Gets <c>m_Rods</c>, the distance constraints between nodes.</summary>
        public FeRodConstraint[] Rods => rods ??= ReadArray(Data, "m_Rods", static o => new FeRodConstraint(
            ReadInt32Array(o, "nNode"), o.GetFloatProperty("flMaxDist"), o.GetFloatProperty("flMinDist"),
            o.GetFloatProperty("flWeight0"), o.GetFloatProperty("flRelaxationFactor")));

        /// <summary>
        /// Gets <c>m_Twists</c>. The compiler emits one record per directed pair per chain declaration without
        /// de-duplicating, so a node or pair can appear several times.
        /// </summary>
        public FeTwistConstraint[] Twists => twists ??= ReadArray(Data, "m_Twists", static o => new FeTwistConstraint(
            o.GetInt32Property("nNodeOrient"), o.GetInt32Property("nNodeEnd"),
            o.GetFloatProperty("flTwistRelax"), o.GetFloatProperty("flSwingRelax")));

        /// <summary>Gets <c>m_HingeLimits</c>.</summary>
        public FeHingeLimit[] HingeLimits => hingeLimits ??= ReadArray(Data, "m_HingeLimits", static o => new FeHingeLimit(
            ReadInt32Array(o, "nNode"), ReadUInt32(o, "nFlags"), o.GetFloatProperty("flWeight4"), o.GetFloatProperty("flWeight5"),
            o.GetFloatProperty("flAngleCenter"), o.GetFloatProperty("flAngleExtents")));

        /// <summary>Gets <c>m_DynKinLinks</c>, one per authored <c>ClothFollowBone</c>.</summary>
        public FeDynKinLink[] DynKinLinks => dynKinLinks ??= ReadArray(Data, "m_DynKinLinks", static o => new FeDynKinLink(
            o.GetInt32Property("m_nParent"), o.GetInt32Property("m_nChild")));

        /// <summary>Gets <c>m_BoneMergeLinks</c>.</summary>
        public FeBoneMergeLink[] BoneMergeLinks => boneMergeLinks ??= ReadArray(Data, "m_BoneMergeLinks", static o => new FeBoneMergeLink(
            ReadUInt32(o, "m_nParentHash"), o.GetInt32Property("m_nChildNode")));

        /// <summary>Gets <c>m_NodeStrayBoxes</c>.</summary>
        public FeNodeStrayBox[] NodeStrayBoxes => nodeStrayBoxes ??= ReadArray(Data, "m_NodeStrayBoxes", static o => new FeNodeStrayBox(
            ReadVector3(o.GetSubCollection("vMin")), ReadUInt32(o, "nFlags"), ReadVector3(o.GetSubCollection("vMax")),
            ReadInt32Array(o, "nNode")));

        /// <summary>Gets <c>m_AxialEdges</c>, the axial bend edges the rigid edge hinge option produces.</summary>
        public FeAxialEdgeBend[] AxialEdges => axialEdges ??= ReadArray(Data, "m_AxialEdges", static o => new FeAxialEdgeBend(
            o.GetFloatProperty("te"), o.GetFloatProperty("tv"), o.GetFloatProperty("flDist"),
            o.GetFloatArray("flWeight"), ReadInt32Array(o, "nNode")));

        /// <summary>Gets <c>m_CtrlOffsets</c>, the generated-node anchor offsets.</summary>
        public FeCtrlOffset[] CtrlOffsets => ctrlOffsets ??= ReadArray(Data, "m_CtrlOffsets", static o => ReadCtrlOffset(o));

        /// <summary>Gets <c>m_CtrlOsOffsets</c>, the parent and child of every object-space-offset virtual node.</summary>
        public FeCtrlOsOffset[] CtrlOsOffsets => ctrlOsOffsets ??= ReadArray(Data, "m_CtrlOsOffsets", static o => new FeCtrlOsOffset(
            o.GetInt32Property("nCtrlParent"), o.GetInt32Property("nCtrlChild")));

        /// <summary>Gets <c>m_FollowNodes</c>.</summary>
        public FeFollowNode[] FollowNodes => followNodes ??= ReadArray(Data, "m_FollowNodes", static o => new FeFollowNode(
            o.GetInt32Property("nParentNode"), o.GetInt32Property("nChildNode"), o.GetFloatProperty("flWeight")));

        /// <summary>Gets <c>m_NodeIntegrator</c>, the per-node solver integrator parameters.</summary>
        public FeNodeIntegrator[] NodeIntegrator => nodeIntegrator ??= ReadArray(Data, "m_NodeIntegrator", static o => new FeNodeIntegrator(
            o.GetFloatProperty("flPointDamping"), o.GetFloatProperty("flAnimationForceAttraction"),
            o.GetFloatProperty("flAnimationVertexAttraction"), o.GetFloatProperty("flGravity")));

        /// <summary>Gets <c>m_SpringIntegrator</c>.</summary>
        public FeSpringIntegrator[] SpringIntegrator => springIntegrator ??= ReadArray(Data, "m_SpringIntegrator", static o => new FeSpringIntegrator(
            ReadInt32Array(o, "nNode"), o.GetFloatProperty("flSpringRestLength"), o.GetFloatProperty("flSpringConstant"),
            o.GetFloatProperty("flSpringDamping"), o.GetFloatProperty("flNodeWeight0")));

        /// <summary>Gets <c>m_TreeChildren</c>.</summary>
        public FeTreeChildren[] TreeChildren => treeChildren ??= ReadArray(Data, "m_TreeChildren", static o => new FeTreeChildren(
            ReadInt32Array(o, "nChild")));

        /// <summary>Gets <c>m_FitMatrices</c>. Each fit covers the <see cref="FitWeights"/> from the previous one's end to its own.</summary>
        public FeFitMatrix[] FitMatrices => fitMatrices ??= ReadArray(Data, "m_FitMatrices", static o => new FeFitMatrix(
            ReadTransform(o.GetSubCollection("bone")), ReadVector3(o.GetSubCollection("vCenter")),
            o.GetInt32Property("nEnd"), o.GetInt32Property("nNode"), o.GetInt32Property("nBeginDynamic"), ReadOptionalInt32(o, "nCtrl")));

        /// <summary>Gets <c>m_FitWeights</c>.</summary>
        public FeFitWeight[] FitWeights => fitWeights ??= ReadArray(Data, "m_FitWeights", static o => new FeFitWeight(
            o.GetFloatProperty("flWeight"), o.GetInt32Property("nNode"), o.GetInt32Property("nDummy")));

        /// <summary>Gets <c>m_ReverseOffsets</c>.</summary>
        public FeNodeReverseOffset[] ReverseOffsets => reverseOffsets ??= ReadArray(Data, "m_ReverseOffsets", static o => new FeNodeReverseOffset(
            ReadVector3(o.GetSubCollection("vOffset")), o.GetInt32Property("nBoneCtrl"), o.GetInt32Property("nTargetNode")));

        /// <summary>Gets <c>m_AnimStrayRadii</c>.</summary>
        public FeAnimStrayRadius[] AnimStrayRadii => animStrayRadii ??= ReadArray(Data, "m_AnimStrayRadii", static o => new FeAnimStrayRadius(
            ReadInt32Array(o, "nNode"), o.GetFloatProperty("flMaxDist"), o.GetFloatProperty("flRelaxationFactor")));

        /// <summary>Gets <c>m_KelagerBends</c>, the three-node bend constraints built for chain joints with a stiff hinge.</summary>
        public FeKelagerBend[] KelagerBends => kelagerBends ??= ReadArray(Data, "m_KelagerBends", static o => new FeKelagerBend(
            o.GetFloatArray("flWeight"), o.GetFloatProperty("flHeight0"), ReadInt32Array(o, "nNode"), o.GetInt32Property("nReserved")));

        /// <summary>Gets <c>m_CtrlSoftOffsets</c>.</summary>
        public FeCtrlSoftOffset[] CtrlSoftOffsets => ctrlSoftOffsets ??= ReadArray(Data, "m_CtrlSoftOffsets", static o => new FeCtrlSoftOffset(
            o.GetInt32Property("nCtrlParent"), o.GetInt32Property("nCtrlChild"), ReadVector3(o.GetSubCollection("vOffset")),
            o.GetFloatProperty("flAlpha")));

        /// <summary>Gets <c>m_LockToParent</c>, the nodes held at a fixed offset from their parent.</summary>
        public FeCtrlOffset[] LockToParent => lockToParent ??= ReadArray(Data, "m_LockToParent", static o => ReadCtrlOffset(o));

        /// <summary>Gets <c>m_DynNodeWindBases</c>, indexed by node minus <see cref="StaticNodes"/>.</summary>
        public FeNodeWindBase[] DynNodeWindBases => dynNodeWindBases ??= ReadArray(Data, "m_DynNodeWindBases", static o => new FeNodeWindBase(
            o.GetInt32Property("nNodeX0"), o.GetInt32Property("nNodeX1"), o.GetInt32Property("nNodeY0"), o.GetInt32Property("nNodeY1")));

        #endregion

        #region SIMD constraints

        /// <summary>Gets <c>m_SimdNodeBases</c>, <see cref="NodeBases"/> packed four to a block.</summary>
        public FeSimdNodeBase[] SimdNodeBases => simdNodeBases ??= ReadArray(Data, "m_SimdNodeBases", static o => new FeSimdNodeBase(
            ReadInt32Array(o, "nNode"), ReadInt32Array(o, "nNodeX0"), ReadInt32Array(o, "nNodeX1"),
            ReadInt32Array(o, "nNodeY0"), ReadInt32Array(o, "nNodeY1"), ReadInt32Array(o, "nDummy"),
            ReadFourQuaternions(o.GetSubCollection("qAdjust"))));

        /// <summary>Gets <c>m_SimdQuads</c>, <see cref="Quads"/> packed four to a block.</summary>
        public FeSimdQuad[] SimdQuads => simdQuads ??= ReadArray(Data, "m_SimdQuads", static o => new FeSimdQuad(
            ReadSimdNodes(o), o.GetFloatArray("f4Slack"),
            ReadArray(o, "vShape", static v => ReadFourVectors(v)),
            ReadArray(o, "f4Weights", static v => v.IsArray ? FlattenFloats(v) : [])));

        /// <summary>Gets <c>m_SimdTris</c>, <see cref="Tris"/> packed four to a block.</summary>
        public FeSimdTri[] SimdTris => simdTris ??= ReadArray(Data, "m_SimdTris", static o => new FeSimdTri(
            ReadSimdNodes(o), o.GetFloatArray("w1"), o.GetFloatArray("w2"), o.GetFloatArray("v1x"),
            o.GetSubCollection("v2") is { } v2 ? new FourVectors2D(v2.GetFloatArray("x"), v2.GetFloatArray("y")) : new FourVectors2D([], [])));

        /// <summary>Gets <c>m_SimdRods</c>, rods packed four to a block.</summary>
        public FeSimdRodConstraint[] SimdRods => simdRods ??= ReadArray(Data, "m_SimdRods", static o => new FeSimdRodConstraint(
            ReadSimdNodes(o), o.GetFloatArray("f4MaxDist"), o.GetFloatArray("f4MinDist"),
            o.GetFloatArray("f4Weight0"), o.GetFloatArray("f4RelaxationFactor")));

        /// <summary>
        /// Gets <c>m_SimdRodsAnim</c>, the rods of chain joints declared with <c>animated_length</c>, packed four to a block.
        /// </summary>
        public FeSimdRodConstraintAnim[] SimdRodsAnim => simdRodsAnim ??= ReadArray(Data, "m_SimdRodsAnim", static o => new FeSimdRodConstraintAnim(
            ReadSimdNodes(o), o.GetFloatArray("f4Weight0"), o.GetFloatArray("f4RelaxationFactor")));

        /// <summary>Gets <c>m_SimdSpringIntegrator</c>, <see cref="SpringIntegrator"/> packed four to a block.</summary>
        public FeSimdSpringIntegrator[] SimdSpringIntegrator => simdSpringIntegrator ??= ReadArray(Data, "m_SimdSpringIntegrator", static o => new FeSimdSpringIntegrator(
            ReadSimdNodes(o), o.GetFloatArray("flSpringRestLength"), o.GetFloatArray("flSpringConstant"),
            o.GetFloatArray("flSpringDamping"), o.GetFloatArray("flNodeWeight0")));

        /// <summary>Gets <c>m_SimdAnimStrayRadii</c>, <see cref="AnimStrayRadii"/> packed four to a block.</summary>
        public FeSimdAnimStrayRadius[] SimdAnimStrayRadii => simdAnimStrayRadii ??= ReadArray(Data, "m_SimdAnimStrayRadii", static o => new FeSimdAnimStrayRadius(
            ReadSimdNodes(o), o.GetFloatArray("flMaxDist"), o.GetFloatArray("flRelaxationFactor")));

        #endregion

        #region Collision

        /// <summary>Gets <c>m_CollisionPlanes</c>.</summary>
        public FeCollisionPlane[] CollisionPlanes => collisionPlanes ??= ReadArray(Data, "m_CollisionPlanes", static o =>
        {
            var plane = o.GetSubCollection("m_Plane");
            return new FeCollisionPlane(
                o.GetInt32Property("nCtrlParent"), o.GetInt32Property("nChildNode"),
                new RnPlane(ReadVector3(plane?.GetSubCollection("m_vNormal")), plane?.GetFloatProperty("m_flOffset") ?? 0f),
                o.GetFloatProperty("flStrength"), o.GetFloatProperty("flStickiness"));
        });

        /// <summary>
        /// Gets <c>m_WorldCollisionParams</c>, the friction of each range of <see cref="WorldCollisionNodes"/>.
        /// </summary>
        public FeWorldCollisionParams[] WorldCollisionParams => worldCollisionParams ??= ReadArray(Data, "m_WorldCollisionParams", static o => new FeWorldCollisionParams(
            o.GetFloatProperty("flWorldFriction"), o.GetFloatProperty("flGroundFriction"),
            o.GetInt32Property("nListBegin"), o.GetInt32Property("nListEnd")));

        /// <summary>Gets <c>m_TaperedCapsuleStretches</c>.</summary>
        public FeTaperedCapsuleStretch[] TaperedCapsuleStretches => taperedCapsuleStretches ??= ReadArray(Data, "m_TaperedCapsuleStretches", static o => new FeTaperedCapsuleStretch(
            ReadInt32Array(o, "nNode"), o.GetInt32Property("nCollisionMask"), o.GetInt32Property("nDummy"), o.GetFloatArray("flRadius")));

        /// <summary>
        /// Gets <c>m_TaperedCapsuleRigids</c>. Older files store each sphere as <c>vCenter</c> and <c>flRadius</c>.
        /// </summary>
        public FeTaperedCapsuleRigid[] TaperedCapsuleRigids => taperedCapsuleRigids ??= ReadArray(Data, "m_TaperedCapsuleRigids", static o =>
        {
            Vector4[] spheres = [];
            if (o.GetArray("vSphere") is { Count: >= 2 })
            {
                spheres = ReadArray(o, "vSphere", static s => s.ToVector4());
            }
            else if (o.GetArray("vCenter") is { Count: >= 2 } centres && o.GetArray<float>("flRadius") is { Length: >= 2 } radii)
            {
                spheres = [new Vector4(centres[0].ToVector3(), radii[0]), new Vector4(centres[1].ToVector3(), radii[1])];
            }

            return new FeTaperedCapsuleRigid(spheres, o.GetInt32Property("nNode"), o.GetInt32Property("nCollisionMask"),
                o.GetInt32Property("nVertexMapIndex", -1), o.GetInt32Property("nFlags"), ReadOptionalFloat(o, "flStickiness"));
        });

        /// <summary>
        /// Gets <c>m_SphereRigids</c>. Older files store the sphere as <c>m_vSphere</c>, or as <c>vCenter</c> and
        /// <c>flRadius</c>.
        /// </summary>
        public FeSphereRigid[] SphereRigids => sphereRigids ??= ReadArray(Data, "m_SphereRigids", static o =>
        {
            Vector4? sphere = null;
            if (o.GetArray<float>("vSphere") is { Length: 4 } s)
            {
                sphere = new Vector4(s[0], s[1], s[2], s[3]);
            }
            else if (o.ContainsKey("m_vSphere"))
            {
                sphere = o.GetSubCollection("m_vSphere").ToVector4();
            }
            else if (o.GetSubCollection("vCenter") is { } centre)
            {
                sphere = new Vector4(centre.ToVector3(), o.GetFloatProperty("flRadius"));
            }

            return new FeSphereRigid(sphere, o.GetInt32Property("nNode"), o.GetInt32Property("nCollisionMask"),
                o.GetInt32Property("nVertexMapIndex", -1), o.GetInt32Property("nFlags"), ReadOptionalFloat(o, "flStickiness"));
        });

        /// <summary>Gets <c>m_SDFRigids</c>.</summary>
        public FeSDFRigid[] SDFRigids => sdfRigids ??= ReadArray(Data, "m_SDFRigids", static o => new FeSDFRigid(
            ReadVector3(o.GetSubCollection("vLocalMin")), ReadVector3(o.GetSubCollection("vLocalMax")), o.GetFloatProperty("flBounciness"),
            o.GetInt32Property("nNode"), o.GetInt32Property("nCollisionMask"), o.GetInt32Property("nVertexMapIndex", -1),
            o.GetInt32Property("nFlags"), o.GetFloatArray("m_Distances"),
            o.GetInt32Property("m_nWidth"), o.GetInt32Property("m_nHeight"), o.GetInt32Property("m_nDepth")));

        /// <summary>Gets <c>m_BoxRigids</c>. Older files store the frame as a matrix in <c>tmFrame</c>.</summary>
        public FeBoxRigid[] BoxRigids => boxRigids ??= ReadArray(Data, "m_BoxRigids", static o =>
        {
            FeTransform? frame = null;
            if (o.GetSubCollection("tmFrame2") is { } frame2)
            {
                var (position, scale, orientation) = frame2.ToTransform();
                frame = new FeTransform(position, scale, orientation);
            }
            else if (o.GetSubCollection("tmFrame") is { } matrixFrame)
            {
                var matrix = matrixFrame.ToMatrix4x4();
                frame = new FeTransform(matrix.Translation, 1f, Quaternion.CreateFromRotationMatrix(matrix));
            }

            return new FeBoxRigid(frame, o.GetInt32Property("nNode"), o.GetInt32Property("nCollisionMask"),
                o.GetSubCollection("vSize") is { } size ? size.ToVector3() : null,
                o.GetInt32Property("nVertexMapIndex", -1), o.GetInt32Property("nFlags"), ReadOptionalFloat(o, "flStickiness"));
        });

        /// <summary>
        /// Gets <c>m_RigidColliderPriorities</c>. The last row holds each rigid array's length, so priority group <c>g</c>
        /// owns <c>[row[g], row[g + 1])</c> of every array.
        /// </summary>
        public FeRigidColliderIndices[] RigidColliderPriorities => rigidColliderPriorities ??= ReadArray(Data, "m_RigidColliderPriorities", static o => new FeRigidColliderIndices(
            o.GetInt32Property("m_nTaperedCapsuleRigidIndex"), o.GetInt32Property("m_nSphereRigidIndex"),
            o.GetInt32Property("m_nBoxRigidIndex"), o.GetInt32Property("m_nSDFRigidIndex"),
            o.GetInt32Property("m_nCollisionPlaneIndex"), o.ContainsKey("m_nCollisionSphereIndex") ? ReadInt32Array(o, "m_nCollisionSphereIndex") : null));

        /// <summary>Gets <c>m_AntiTunnelProbes</c>.</summary>
        public FeAntiTunnelProbe[] AntiTunnelProbes => antiTunnelProbes ??= ReadArray(Data, "m_AntiTunnelProbes", static o => new FeAntiTunnelProbe(
            o.GetFloatProperty("flWeight"), ReadUInt32(o, "nFlags"), o.GetInt32Property("nProbeNode"),
            o.GetInt32Property("nCount"), o.GetInt32Property("nBegin"),
            o.GetFloatProperty("flActivationDistance"), o.GetFloatProperty("flCurvatureRadius"), o.GetFloatProperty("flBias")));

        /// <summary>Gets <c>m_SelfCollisionLayers</c>.</summary>
        public FeModelSelfCollisionLayer[] SelfCollisionLayers => selfCollisionLayers ??= ReadArray(Data, "m_SelfCollisionLayers", static o => new FeModelSelfCollisionLayer(
            o.GetStringProperty("m_Name") ?? string.Empty, ReadInt32Array(o, "m_Nodes"), o.GetFloatProperty("m_flParentReaction"),
            ReadUInt32(o, "m_nFlags"), ReadUInt32Array(o, "m_nEndIdx")));

        #endregion

        #region Effects and vertex maps

        /// <summary>Gets <c>m_Effects</c>, the named cloth effects.</summary>
        public FeEffectDesc[] Effects => effects ??= ReadArray(Data, "m_Effects", static o => new FeEffectDesc(
            o.GetStringProperty("sName") ?? string.Empty, ReadUInt32(o, "nNameHash"),
            o.GetInt32Property("nType"), o.GetSubCollection("m_Params")));

        /// <summary>Gets <c>m_JiggleBones</c>.</summary>
        public FeIndexedJiggleBone[] JiggleBones => jiggleBones ??= ReadArray(Data, "m_JiggleBones", static o =>
        {
            var bone = o.GetSubCollection("m_jiggleBone");
            return new FeIndexedJiggleBone(unchecked((int)ReadUInt32(o, "m_nNode")), unchecked((int)ReadUInt32(o, "m_nJiggleParent")),
                bone is null ? default : new FeJiggleBone(
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

        /// <summary>Gets <c>m_VertexMaps</c>, the named vertex selections whose weights are in <see cref="VertexMapValues"/>.</summary>
        public FeVertexMapDesc[] VertexMaps => vertexMaps ??= ReadArray(Data, "m_VertexMaps", static o => new FeVertexMapDesc(
            o.GetStringProperty("sName") ?? string.Empty, ReadUInt32(o, "nNameHash"), ReadUInt32(o, "nColor"), ReadUInt32(o, "nFlags"),
            o.GetInt32Property("nVertexBase"), o.GetInt32Property("nVertexCount"),
            o.GetInt32Property("nMapOffset"), o.GetInt32Property("nNodeListOffset"),
            ReadVector3(o.GetSubCollection("vCenterOfMass")), o.GetFloatProperty("flVolumetricSolveStrength"),
            o.GetInt32Property("nScaleSourceNode", -1), o.GetInt32Property("nNodeListCount")));

        /// <summary>Gets <c>m_MorphLayers</c>, the deprecated morph layers.</summary>
        public FeMorphLayer[] MorphLayers => morphLayers ??= ReadArray(Data, "m_MorphLayers", static o => new FeMorphLayer(
            o.GetStringProperty("m_Name") ?? string.Empty, ReadUInt32(o, "m_nNameHash"),
            ReadInt32Array(o, "m_Nodes"), ReadArray(o, "m_InitPos", static p => ReadVector3(p)),
            o.GetFloatArray("m_Gravity"), o.GetFloatArray("m_GoalStrength"), o.GetFloatArray("m_GoalDamping"),
            ReadUInt32(o, "m_nFlags")));

        #endregion

        #region Readers

        private static T[] ReadArray<T>(KVObject data, string key, Func<KVObject, T> map)
        {
            var array = data.GetArray(key);
            return array is null ? [] : [.. array.Select(map)];
        }

        private static int[] ReadInt32Array(KVObject data, string key)
            => [.. data.GetIntegerArray(key).Select(static value => (int)value)];

        /// <summary>Reads an integer array as unsigned values, keeping the bits of a negative entry.</summary>
        private static uint[] ReadUInt32Array(KVObject data, string key)
            => [.. data.GetIntegerArray(key).Select(static value => unchecked((uint)value))];

        private static byte[] ReadByteArray(KVObject data, string key)
            => [.. data.GetIntegerArray(key).Select(static value => unchecked((byte)value))];

        private static float? ReadOptionalFloat(KVObject data, string key) => data.ContainsKey(key) ? data.GetFloatProperty(key) : null;

        private static int? ReadOptionalInt32(KVObject data, string key) => data.ContainsKey(key) ? data.GetInt32Property(key) : null;

        /// <summary>Reads an unsigned 32-bit field, keeping the bits of a value stored as a negative integer.</summary>
        private static uint ReadUInt32(KVObject data, string key) => unchecked((uint)data.GetIntegerProperty(key));

        /// <summary>Reads a three-component vector, or zero when <paramref name="value"/> is absent or shorter.</summary>
        private static Vector3 ReadVector3(KVObject? value) => value is { Count: >= 3 } ? value.ToVector3() : default;

        /// <summary>Reads a <c>CTransform</c>, or the identity when <paramref name="value"/> is absent or shorter.</summary>
        private static FeTransform ReadTransform(KVObject? value)
        {
            if (value is not { Count: >= 7 })
            {
                return FeTransform.Identity;
            }

            var (position, scale, orientation) = value.ToTransform();
            return new FeTransform(position, scale, orientation);
        }

        /// <summary>
        /// Reads a SIMD <c>nNode</c> block as rows of four lanes, from either rows of four or one row-major array.
        /// </summary>
        private static int[][] ReadSimdNodes(KVObject data)
        {
            if (!data.TryGetValue("nNode", out var nNode) || !nNode.IsArray)
            {
                return [];
            }

            var flat = new List<int>();
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

            return [.. flat.Chunk(4)];
        }

        /// <summary>Gets the four lanes starting at <paramref name="lane"/>, or fewer where <paramref name="values"/> ends.</summary>
        private static float[] Lanes(float[] values, int lane)
            => lane < values.Length ? values[lane..Math.Min(lane + 4, values.Length)] : [];

        private static FeCtrlOffset ReadCtrlOffset(KVObject o)
            => new(ReadVector3(o.GetSubCollection("vOffset")), o.GetInt32Property("nCtrlParent"), o.GetInt32Property("nCtrlChild"));

        private static Quaternion ReadQuaternion(KVObject? value) => value is { Count: >= 4 } ? value.ToQuaternion() : default;

        private static float[] FlattenFloats(KVObject value)
        {
            var flat = new List<float>();
            foreach (var item in value.AsArraySpan())
            {
                if (item.IsArray)
                {
                    flat.AddRange(FlattenFloats(item));
                }
                else
                {
                    flat.Add((float)item);
                }
            }

            return [.. flat];
        }

        /// <summary>
        /// Reads a <c>FourVectors</c> stored as twelve floats (four X lanes, then Y, then Z), as <c>x</c>, <c>y</c> and
        /// <c>z</c> arrays, or as one three-component vector per lane.
        /// </summary>
        private static FourVectors ReadFourVectors(KVObject? value)
        {
            if (value is { IsCollection: true })
            {
                return new(value.GetFloatArray("x"), value.GetFloatArray("y"), value.GetFloatArray("z"));
            }

            var components = ReadComponentLanes(value, 3);
            return new(components[0], components[1], components[2]);
        }

        /// <summary>
        /// Reads a <c>FourQuaternions</c> stored as sixteen floats (four X lanes, then Y, Z and W), as <c>x</c>, <c>y</c>,
        /// <c>z</c> and <c>w</c> arrays, or as one quaternion per lane.
        /// </summary>
        private static FourQuaternions ReadFourQuaternions(KVObject? value)
        {
            if (value is { IsCollection: true })
            {
                return new(value.GetFloatArray("x"), value.GetFloatArray("y"), value.GetFloatArray("z"), value.GetFloatArray("w"));
            }

            var components = ReadComponentLanes(value, 4);
            return new(components[0], components[1], components[2], components[3]);
        }

        /// <summary>
        /// Reads the per-component lanes of a structure-of-arrays SIMD value stored flat, component by component, or as one
        /// array of <paramref name="componentCount"/> components per lane.
        /// </summary>
        private static float[][] ReadComponentLanes(KVObject? value, int componentCount)
        {
            var components = new float[componentCount][];
            if (value is not { IsArray: true })
            {
                Array.Fill(components, []);
                return components;
            }

            var items = value.AsArraySpan();
            if (items.Length > 0 && items[0].IsArray)
            {
                for (var c = 0; c < componentCount; c++)
                {
                    var lanes = new List<float>(items.Length);
                    foreach (var lane in items)
                    {
                        if (c < lane.Count)
                        {
                            lanes.Add((float)lane[c]);
                        }
                    }

                    components[c] = [.. lanes];
                }

                return components;
            }

            var values = FlattenFloats(value);
            for (var c = 0; c < componentCount; c++)
            {
                components[c] = Lanes(values, c * 4);
            }

            return components;
        }

        #endregion

        #region Record types

        /// <summary>A <c>CTransform</c>: a position, uniform scale and orientation.</summary>
        /// <param name="Position">The translation.</param>
        /// <param name="Scale">The uniform scale.</param>
        /// <param name="Orientation">The rotation.</param>
        public readonly record struct FeTransform(Vector3 Position, float Scale, Quaternion Orientation)
        {
            /// <summary>Gets the identity transform.</summary>
            public static FeTransform Identity => new(Vector3.Zero, 1f, Quaternion.Identity);
        }

        /// <summary>A node's explicit orientation basis (<c>FeNodeBase_t</c>).</summary>
        /// <param name="Node">The node the basis belongs to (<c>nNode</c>).</param>
        /// <param name="Dummy">Padding (<c>nDummy</c>).</param>
        /// <param name="NodeX0">First node of the local X axis (<c>nNodeX0</c>).</param>
        /// <param name="NodeX1">Second node of the local X axis (<c>nNodeX1</c>).</param>
        /// <param name="NodeY0">First node of the local Y axis (<c>nNodeY0</c>).</param>
        /// <param name="NodeY1">Second node of the local Y axis (<c>nNodeY1</c>).</param>
        /// <param name="Adjust">Rotation applied to the derived basis (<c>qAdjust</c>).</param>
        public readonly record struct FeNodeBase(int Node, int[] Dummy, int NodeX0, int NodeX1, int NodeY0, int NodeY1, Quaternion Adjust);

        /// <summary>A surface quad (<c>FeQuad_t</c>).</summary>
        /// <param name="Nodes">The four corner nodes (<c>nNode</c>).</param>
        /// <param name="Slack"><c>flSlack</c>.</param>
        /// <param name="Shapes">The rest shape (<c>vShape</c>).</param>
        public readonly record struct FeQuad(int[] Nodes, float Slack, Vector4[] Shapes);

        /// <summary>A surface triangle (<c>FeTri_t</c>).</summary>
        /// <param name="Nodes">The three corner nodes (<c>nNode</c>).</param>
        /// <param name="W1"><c>w1</c>.</param>
        /// <param name="W2"><c>w2</c>.</param>
        /// <param name="V1x"><c>v1x</c>.</param>
        /// <param name="V2"><c>v2</c>.</param>
        public readonly record struct FeTri(int[] Nodes, float W1, float W2, float V1x, Vector2 V2);

        /// <summary>A distance constraint between two nodes (<c>FeRodConstraint_t</c>).</summary>
        /// <param name="Nodes">The two endpoint nodes (<c>nNode</c>).</param>
        /// <param name="MaxDist">Maximum allowed distance (<c>flMaxDist</c>).</param>
        /// <param name="MinDist">Minimum allowed distance (<c>flMinDist</c>).</param>
        /// <param name="Weight0">Share of the first node in the correction (<c>flWeight0</c>).</param>
        /// <param name="RelaxationFactor"><c>flRelaxationFactor</c>.</param>
        public readonly record struct FeRodConstraint(int[] Nodes, float MaxDist, float MinDist, float Weight0, float RelaxationFactor);

        /// <summary>A twist constraint (<c>FeTwistConstraint_t</c>).</summary>
        /// <param name="NodeOrient">The node whose frame the twist is measured in (<c>nNodeOrient</c>).</param>
        /// <param name="NodeEnd">The node the twist is measured toward (<c>nNodeEnd</c>).</param>
        /// <param name="TwistRelax"><c>flTwistRelax</c>.</param>
        /// <param name="SwingRelax"><c>flSwingRelax</c>.</param>
        public readonly record struct FeTwistConstraint(int NodeOrient, int NodeEnd, float TwistRelax, float SwingRelax);

        /// <summary>A hinge angle limit (<c>FeHingeLimit_t</c>).</summary>
        /// <param name="Nodes">The axis, reference and arm nodes (<c>nNode</c>).</param>
        /// <param name="Flags"><c>nFlags</c>.</param>
        /// <param name="Weight4">Blend of the reference nodes (<c>flWeight4</c>).</param>
        /// <param name="Weight5">Blend of the arm nodes (<c>flWeight5</c>).</param>
        /// <param name="AngleCenter"><c>flAngleCenter</c>, in radians.</param>
        /// <param name="AngleExtents"><c>flAngleExtents</c>, in radians.</param>
        public readonly record struct FeHingeLimit(int[] Nodes, uint Flags, float Weight4, float Weight5, float AngleCenter, float AngleExtents);

        /// <summary>A dynamic-to-kinematic node link (<c>FeDynKinLink_t</c>).</summary>
        /// <param name="Parent"><c>m_nParent</c>.</param>
        /// <param name="Child"><c>m_nChild</c>.</param>
        public readonly record struct FeDynKinLink(int Parent, int Child);

        /// <summary>A bone-merge link (<c>FeBoneMergeLink_t</c>).</summary>
        /// <param name="ParentHash"><c>m_nParentHash</c>.</param>
        /// <param name="ChildNode"><c>m_nChildNode</c>.</param>
        public readonly record struct FeBoneMergeLink(uint ParentHash, int ChildNode);

        /// <summary>A box a node pair may not stray out of (<c>FeNodeStrayBox_t</c>).</summary>
        /// <param name="Min"><c>vMin</c>.</param>
        /// <param name="Flags"><c>nFlags</c>.</param>
        /// <param name="Max"><c>vMax</c>.</param>
        /// <param name="Nodes"><c>nNode</c>.</param>
        public readonly record struct FeNodeStrayBox(Vector3 Min, uint Flags, Vector3 Max, int[] Nodes);

        /// <summary>An axial edge bend constraint (<c>FeAxialEdgeBend_t</c>).</summary>
        /// <param name="Te"><c>te</c>.</param>
        /// <param name="Tv"><c>tv</c>.</param>
        /// <param name="Dist"><c>flDist</c>.</param>
        /// <param name="Weights"><c>flWeight</c>.</param>
        /// <param name="Nodes"><c>nNode</c>.</param>
        public readonly record struct FeAxialEdgeBend(float Te, float Tv, float Dist, float[] Weights, int[] Nodes);

        /// <summary>A child node held at an offset from its parent (<c>FeCtrlOffset_t</c>).</summary>
        /// <param name="Offset"><c>vOffset</c>.</param>
        /// <param name="CtrlParent"><c>nCtrlParent</c>.</param>
        /// <param name="CtrlChild"><c>nCtrlChild</c>.</param>
        public readonly record struct FeCtrlOffset(Vector3 Offset, int CtrlParent, int CtrlChild);

        /// <summary>An object-space-offset virtual node (<c>FeCtrlOsOffset_t</c>).</summary>
        /// <param name="CtrlParent"><c>nCtrlParent</c>.</param>
        /// <param name="CtrlChild"><c>nCtrlChild</c>.</param>
        public readonly record struct FeCtrlOsOffset(int CtrlParent, int CtrlChild);

        /// <summary>A node that follows another (<c>FeFollowNode_t</c>).</summary>
        /// <param name="ParentNode"><c>nParentNode</c>.</param>
        /// <param name="ChildNode"><c>nChildNode</c>.</param>
        /// <param name="Weight"><c>flWeight</c>.</param>
        public readonly record struct FeFollowNode(int ParentNode, int ChildNode, float Weight);

        /// <summary>A node's solver integrator parameters (<c>FeNodeIntegrator_t</c>).</summary>
        /// <param name="PointDamping"><c>flPointDamping</c>.</param>
        /// <param name="AnimationForceAttraction"><c>flAnimationForceAttraction</c>.</param>
        /// <param name="AnimationVertexAttraction"><c>flAnimationVertexAttraction</c>.</param>
        /// <param name="Gravity"><c>flGravity</c>.</param>
        public readonly record struct FeNodeIntegrator(float PointDamping, float AnimationForceAttraction, float AnimationVertexAttraction, float Gravity);

        /// <summary>A damped spring between two nodes (<c>FeSpringIntegrator_t</c>).</summary>
        /// <param name="Nodes"><c>nNode</c>.</param>
        /// <param name="SpringRestLength"><c>flSpringRestLength</c>.</param>
        /// <param name="SpringConstant"><c>flSpringConstant</c>.</param>
        /// <param name="SpringDamping"><c>flSpringDamping</c>.</param>
        /// <param name="NodeWeight0"><c>flNodeWeight0</c>.</param>
        public readonly record struct FeSpringIntegrator(int[] Nodes, float SpringRestLength, float SpringConstant, float SpringDamping, float NodeWeight0);

        /// <summary>The children of a collision tree node (<c>FeTreeChildren_t</c>).</summary>
        /// <param name="Children"><c>nChild</c>.</param>
        public readonly record struct FeTreeChildren(int[] Children);

        /// <summary>A bone fit to a range of <see cref="FeModel.FitWeights"/> (<c>FeFitMatrix_t</c>).</summary>
        /// <param name="Bone"><c>bone</c>.</param>
        /// <param name="Center"><c>vCenter</c>.</param>
        /// <param name="End">The exclusive end of the fit's weight range (<c>nEnd</c>).</param>
        /// <param name="Node">The fit bone (<c>nNode</c>).</param>
        /// <param name="BeginDynamic"><c>nBeginDynamic</c>.</param>
        /// <param name="Ctrl"><c>nCtrl</c>, or null when absent (older files only).</param>
        public readonly record struct FeFitMatrix(FeTransform Bone, Vector3 Center, int End, int Node, int BeginDynamic, int? Ctrl);

        /// <summary>A node a fit is taken over (<c>FeFitWeight_t</c>).</summary>
        /// <param name="Weight"><c>flWeight</c>.</param>
        /// <param name="Node"><c>nNode</c>.</param>
        /// <param name="Dummy"><c>nDummy</c>.</param>
        public readonly record struct FeFitWeight(float Weight, int Node, int Dummy);

        /// <summary>A reverse offset from a bone to a target node (<c>FeNodeReverseOffset_t</c>).</summary>
        /// <param name="Offset"><c>vOffset</c>.</param>
        /// <param name="BoneCtrl"><c>nBoneCtrl</c>.</param>
        /// <param name="TargetNode"><c>nTargetNode</c>.</param>
        public readonly record struct FeNodeReverseOffset(Vector3 Offset, int BoneCtrl, int TargetNode);

        /// <summary>How far a node may stray from its animated position (<c>FeAnimStrayRadius_t</c>).</summary>
        /// <param name="Nodes"><c>nNode</c>.</param>
        /// <param name="MaxDist"><c>flMaxDist</c>.</param>
        /// <param name="RelaxationFactor"><c>flRelaxationFactor</c>.</param>
        public readonly record struct FeAnimStrayRadius(int[] Nodes, float MaxDist, float RelaxationFactor);

        /// <summary>A three-node bend constraint (<c>FeKelagerBend2_t</c>).</summary>
        /// <param name="Weights">Solver share of each node (<c>flWeight</c>).</param>
        /// <param name="Height0">Allowed distance from the bent node to the centroid of the three nodes (<c>flHeight0</c>).</param>
        /// <param name="Nodes">The bent node, then the two nodes the bend measures against (<c>nNode</c>).</param>
        /// <param name="Reserved"><c>nReserved</c>.</param>
        public readonly record struct FeKelagerBend(float[] Weights, float Height0, int[] Nodes, int Reserved);

        /// <summary>A child node blended toward an offset from a parent (<c>FeCtrlSoftOffset_t</c>).</summary>
        /// <param name="CtrlParent"><c>nCtrlParent</c>.</param>
        /// <param name="CtrlChild"><c>nCtrlChild</c>.</param>
        /// <param name="Offset"><c>vOffset</c>.</param>
        /// <param name="Alpha"><c>flAlpha</c>.</param>
        public readonly record struct FeCtrlSoftOffset(int CtrlParent, int CtrlChild, Vector3 Offset, float Alpha);

        /// <summary>A dynamic node's wind basis (<c>FeNodeWindBase_t</c>).</summary>
        /// <param name="NodeX0"><c>nNodeX0</c>.</param>
        /// <param name="NodeX1"><c>nNodeX1</c>.</param>
        /// <param name="NodeY0"><c>nNodeY0</c>.</param>
        /// <param name="NodeY1"><c>nNodeY1</c>.</param>
        public readonly record struct FeNodeWindBase(int NodeX0, int NodeX1, int NodeY0, int NodeY1);

        /// <summary>Four vectors in structure-of-arrays layout (<c>FourVectors</c>), one lane per vector.</summary>
        /// <param name="X">The X lanes (<c>x</c>).</param>
        /// <param name="Y">The Y lanes (<c>y</c>).</param>
        /// <param name="Z">The Z lanes (<c>z</c>).</param>
        public readonly record struct FourVectors(float[] X, float[] Y, float[] Z);

        /// <summary>Four 2D vectors in structure-of-arrays layout (<c>FourVectors2D</c>), one lane per vector.</summary>
        /// <param name="X">The X lanes (<c>x</c>).</param>
        /// <param name="Y">The Y lanes (<c>y</c>).</param>
        public readonly record struct FourVectors2D(float[] X, float[] Y);

        /// <summary>Four quaternions in structure-of-arrays layout (<c>FourQuaternions</c>), one lane per quaternion.</summary>
        /// <param name="X">The X lanes.</param>
        /// <param name="Y">The Y lanes.</param>
        /// <param name="Z">The Z lanes.</param>
        /// <param name="W">The W lanes.</param>
        public readonly record struct FourQuaternions(float[] X, float[] Y, float[] Z, float[] W);

        /// <summary>Four node bases, one per lane (<c>FeSimdNodeBase_t</c>).</summary>
        /// <param name="Nodes"><c>nNode</c>.</param>
        /// <param name="NodeX0"><c>nNodeX0</c>.</param>
        /// <param name="NodeX1"><c>nNodeX1</c>.</param>
        /// <param name="NodeY0"><c>nNodeY0</c>.</param>
        /// <param name="NodeY1"><c>nNodeY1</c>.</param>
        /// <param name="Dummy"><c>nDummy</c>.</param>
        /// <param name="Adjust"><c>qAdjust</c>.</param>
        public readonly record struct FeSimdNodeBase(int[] Nodes, int[] NodeX0, int[] NodeX1, int[] NodeY0, int[] NodeY1, int[] Dummy,
            FourQuaternions Adjust);

        /// <summary>Four quads, one per lane (<c>FeSimdQuad_t</c>).</summary>
        /// <param name="Nodes">The corner nodes as four rows of four lanes, indexed [corner][lane] (<c>nNode</c>).</param>
        /// <param name="Slack"><c>f4Slack</c>.</param>
        /// <param name="Shapes"><c>vShape</c>.</param>
        /// <param name="Weights"><c>f4Weights</c>.</param>
        public readonly record struct FeSimdQuad(int[][] Nodes, float[] Slack, FourVectors[] Shapes, float[][] Weights);

        /// <summary>Four triangles, one per lane (<c>FeSimdTri_t</c>).</summary>
        /// <param name="Nodes">The corner nodes as three rows of four lanes, indexed [corner][lane] (<c>nNode</c>).</param>
        /// <param name="W1"><c>w1</c>.</param>
        /// <param name="W2"><c>w2</c>.</param>
        /// <param name="V1x"><c>v1x</c>.</param>
        /// <param name="V2"><c>v2</c>.</param>
        public readonly record struct FeSimdTri(int[][] Nodes, float[] W1, float[] W2, float[] V1x, FourVectors2D V2);

        /// <summary>Four rods, one per lane (<c>FeSimdRodConstraint_t</c>).</summary>
        /// <param name="Nodes">The end nodes as two rows of four lanes, indexed [end][lane] (<c>nNode</c>).</param>
        /// <param name="MaxDist"><c>f4MaxDist</c>.</param>
        /// <param name="MinDist"><c>f4MinDist</c>.</param>
        /// <param name="Weight0"><c>f4Weight0</c>.</param>
        /// <param name="RelaxationFactor"><c>f4RelaxationFactor</c>.</param>
        public readonly record struct FeSimdRodConstraint(int[][] Nodes, float[] MaxDist, float[] MinDist, float[] Weight0, float[] RelaxationFactor);

        /// <summary>Four animated-length rods, one per lane (<c>FeSimdRodConstraintAnim_t</c>).</summary>
        /// <param name="Nodes">The end nodes as two rows of four lanes, indexed [end][lane] (<c>nNode</c>).</param>
        /// <param name="Weight0"><c>f4Weight0</c>.</param>
        /// <param name="RelaxationFactor"><c>f4RelaxationFactor</c>.</param>
        public readonly record struct FeSimdRodConstraintAnim(int[][] Nodes, float[] Weight0, float[] RelaxationFactor);

        /// <summary>Four damped springs, one per lane (<c>FeSimdSpringIntegrator_t</c>).</summary>
        /// <param name="Nodes">The end nodes as two rows of four lanes, indexed [end][lane] (<c>nNode</c>).</param>
        /// <param name="SpringRestLength"><c>flSpringRestLength</c>.</param>
        /// <param name="SpringConstant"><c>flSpringConstant</c>.</param>
        /// <param name="SpringDamping"><c>flSpringDamping</c>.</param>
        /// <param name="NodeWeight0"><c>flNodeWeight0</c>.</param>
        public readonly record struct FeSimdSpringIntegrator(int[][] Nodes, float[] SpringRestLength, float[] SpringConstant,
            float[] SpringDamping, float[] NodeWeight0);

        /// <summary>Four stray radii, one per lane (<c>FeSimdAnimStrayRadius_t</c>).</summary>
        /// <param name="Nodes">The node pairs as two rows of four lanes, indexed [end][lane] (<c>nNode</c>).</param>
        /// <param name="MaxDist"><c>flMaxDist</c>.</param>
        /// <param name="RelaxationFactor"><c>flRelaxationFactor</c>.</param>
        public readonly record struct FeSimdAnimStrayRadius(int[][] Nodes, float[] MaxDist, float[] RelaxationFactor);

        /// <summary>A plane (<c>RnPlane_t</c>).</summary>
        /// <param name="Normal"><c>m_vNormal</c>.</param>
        /// <param name="Offset"><c>m_flOffset</c>.</param>
        public readonly record struct RnPlane(Vector3 Normal, float Offset);

        /// <summary>
        /// A collision plane (<c>FeCollisionPlane_t</c>). Older files carry <c>flStickiness</c> instead of <c>flStrength</c>;
        /// whichever is absent reads as 0.
        /// </summary>
        /// <param name="CtrlParent"><c>nCtrlParent</c>.</param>
        /// <param name="ChildNode"><c>nChildNode</c>.</param>
        /// <param name="Plane"><c>m_Plane</c>.</param>
        /// <param name="Strength"><c>flStrength</c>.</param>
        /// <param name="Stickiness"><c>flStickiness</c>.</param>
        public readonly record struct FeCollisionPlane(int CtrlParent, int ChildNode, RnPlane Plane, float Strength, float Stickiness);

        /// <summary>The world and ground friction of a range of world-colliding nodes (<c>FeWorldCollisionParams_t</c>).</summary>
        /// <param name="WorldFriction"><c>flWorldFriction</c>.</param>
        /// <param name="GroundFriction"><c>flGroundFriction</c>.</param>
        /// <param name="ListBegin">The first index into <see cref="FeModel.WorldCollisionNodes"/> (<c>nListBegin</c>).</param>
        /// <param name="ListEnd">The exclusive end index into <see cref="FeModel.WorldCollisionNodes"/> (<c>nListEnd</c>).</param>
        public readonly record struct FeWorldCollisionParams(float WorldFriction, float GroundFriction, int ListBegin, int ListEnd);

        /// <summary>A tapered capsule stretched between two nodes (<c>FeTaperedCapsuleStretch_t</c>).</summary>
        /// <param name="Nodes"><c>nNode</c>.</param>
        /// <param name="CollisionMask"><c>nCollisionMask</c>.</param>
        /// <param name="Dummy"><c>nDummy</c>.</param>
        /// <param name="Radii"><c>flRadius</c>.</param>
        public readonly record struct FeTaperedCapsuleStretch(int[] Nodes, int CollisionMask, int Dummy, float[] Radii);

        /// <summary>A tapered capsule collider on one node (<c>FeTaperedCapsuleRigid_t</c>).</summary>
        /// <param name="Spheres">The two cap spheres, each as center and radius (<c>vSphere</c>); empty when the file carries neither form.</param>
        /// <param name="Node"><c>nNode</c>.</param>
        /// <param name="CollisionMask"><c>nCollisionMask</c>.</param>
        /// <param name="VertexMapIndex"><c>nVertexMapIndex</c>, -1 when absent.</param>
        /// <param name="Flags"><c>nFlags</c>.</param>
        /// <param name="Stickiness"><c>flStickiness</c>, or null when absent (older files only).</param>
        public readonly record struct FeTaperedCapsuleRigid(Vector4[] Spheres, int Node, int CollisionMask, int VertexMapIndex, int Flags,
            float? Stickiness);

        /// <summary>A sphere collider on one node (<c>FeSphereRigid_t</c>).</summary>
        /// <param name="Sphere">The center and radius (<c>vSphere</c>), or null when the file carries no form of it.</param>
        /// <param name="Node"><c>nNode</c>.</param>
        /// <param name="CollisionMask"><c>nCollisionMask</c>.</param>
        /// <param name="VertexMapIndex"><c>nVertexMapIndex</c>, -1 when absent.</param>
        /// <param name="Flags"><c>nFlags</c>.</param>
        /// <param name="Stickiness"><c>flStickiness</c>, or null when absent (older files only).</param>
        public readonly record struct FeSphereRigid(Vector4? Sphere, int Node, int CollisionMask, int VertexMapIndex, int Flags, float? Stickiness);

        /// <summary>A signed-distance-field collider on one node (<c>FeSDFRigid_t</c>).</summary>
        /// <param name="LocalMin"><c>vLocalMin</c>.</param>
        /// <param name="LocalMax"><c>vLocalMax</c>.</param>
        /// <param name="Bounciness"><c>flBounciness</c>.</param>
        /// <param name="Node"><c>nNode</c>.</param>
        /// <param name="CollisionMask"><c>nCollisionMask</c>.</param>
        /// <param name="VertexMapIndex"><c>nVertexMapIndex</c>, -1 when absent.</param>
        /// <param name="Flags"><c>nFlags</c>.</param>
        /// <param name="Distances"><c>m_Distances</c>.</param>
        /// <param name="Width"><c>m_nWidth</c>.</param>
        /// <param name="Height"><c>m_nHeight</c>.</param>
        /// <param name="Depth"><c>m_nDepth</c>.</param>
        public readonly record struct FeSDFRigid(Vector3 LocalMin, Vector3 LocalMax, float Bounciness, int Node, int CollisionMask,
            int VertexMapIndex, int Flags, float[] Distances, int Width, int Height, int Depth);

        /// <summary>A box collider on one node (<c>FeBoxRigid_t</c>).</summary>
        /// <param name="Frame2">The box frame (<c>tmFrame2</c>), or null when the file carries no form of it.</param>
        /// <param name="Node"><c>nNode</c>.</param>
        /// <param name="CollisionMask"><c>nCollisionMask</c>.</param>
        /// <param name="Size">The half extents (<c>vSize</c>), or null when absent.</param>
        /// <param name="VertexMapIndex"><c>nVertexMapIndex</c>, -1 when absent.</param>
        /// <param name="Flags"><c>nFlags</c>.</param>
        /// <param name="Stickiness"><c>flStickiness</c>, or null when absent (older files only).</param>
        public readonly record struct FeBoxRigid(FeTransform? Frame2, int Node, int CollisionMask, Vector3? Size, int VertexMapIndex, int Flags,
            float? Stickiness);

        /// <summary>
        /// One row of <c>m_RigidColliderPriorities</c> (<c>FeRigidColliderIndices_t</c>): the index at which a priority group
        /// starts in each rigid-collider array.
        /// </summary>
        /// <param name="TaperedCapsuleRigidIndex"><c>m_nTaperedCapsuleRigidIndex</c>.</param>
        /// <param name="SphereRigidIndex"><c>m_nSphereRigidIndex</c>.</param>
        /// <param name="BoxRigidIndex"><c>m_nBoxRigidIndex</c>.</param>
        /// <param name="SDFRigidIndex"><c>m_nSDFRigidIndex</c>.</param>
        /// <param name="CollisionPlaneIndex"><c>m_nCollisionPlaneIndex</c>.</param>
        /// <param name="CollisionSphereIndex"><c>m_nCollisionSphereIndex</c>, two indices, or null when absent (older files only).</param>
        public readonly record struct FeRigidColliderIndices(int TaperedCapsuleRigidIndex, int SphereRigidIndex,
            int BoxRigidIndex, int SDFRigidIndex, int CollisionPlaneIndex, int[]? CollisionSphereIndex);

        /// <summary>An anti-tunnelling probe (<c>FeAntiTunnelProbe_t</c>).</summary>
        /// <param name="Weight"><c>flWeight</c>.</param>
        /// <param name="Flags"><c>nFlags</c>.</param>
        /// <param name="ProbeNode"><c>nProbeNode</c>.</param>
        /// <param name="Count">How many <see cref="FeModel.AntiTunnelTargetNodes"/> the probe targets (<c>nCount</c>).</param>
        /// <param name="Begin">The first of them (<c>nBegin</c>).</param>
        /// <param name="ActivationDistance"><c>flActivationDistance</c>.</param>
        /// <param name="CurvatureRadius"><c>flCurvatureRadius</c>.</param>
        /// <param name="Bias"><c>flBias</c>.</param>
        public readonly record struct FeAntiTunnelProbe(float Weight, uint Flags, int ProbeNode, int Count, int Begin,
            float ActivationDistance, float CurvatureRadius, float Bias);

        /// <summary>A self-collision layer (<c>FeModelSelfCollisionLayer_t</c>).</summary>
        /// <param name="Name"><c>m_Name</c>.</param>
        /// <param name="Nodes"><c>m_Nodes</c>.</param>
        /// <param name="ParentReaction"><c>m_flParentReaction</c>.</param>
        /// <param name="Flags"><c>m_nFlags</c>.</param>
        /// <param name="EndIdx"><c>m_nEndIdx</c>.</param>
        public readonly record struct FeModelSelfCollisionLayer(string Name, int[] Nodes, float ParentReaction, uint Flags, uint[] EndIdx);

        /// <summary>A named cloth effect (<c>FeEffectDesc_t</c>).</summary>
        /// <param name="Name"><c>sName</c>.</param>
        /// <param name="NameHash"><c>nNameHash</c>.</param>
        /// <param name="Type"><c>nType</c>.</param>
        /// <param name="Params">The per-type parameter block (<c>m_Params</c>), or null when absent.</param>
        public readonly record struct FeEffectDesc(string Name, uint NameHash, int Type, KVObject? Params);

        /// <summary>A jiggle bone's physical parameters (<c>CFeJiggleBone</c>).</summary>
        /// <param name="Flags"><c>m_nFlags</c>.</param>
        /// <param name="Length"><c>m_flLength</c>.</param>
        /// <param name="TipMass"><c>m_flTipMass</c>.</param>
        /// <param name="YawStiffness"><c>m_flYawStiffness</c>.</param>
        /// <param name="YawDamping"><c>m_flYawDamping</c>.</param>
        /// <param name="PitchStiffness"><c>m_flPitchStiffness</c>.</param>
        /// <param name="PitchDamping"><c>m_flPitchDamping</c>.</param>
        /// <param name="AlongStiffness"><c>m_flAlongStiffness</c>.</param>
        /// <param name="AlongDamping"><c>m_flAlongDamping</c>.</param>
        /// <param name="AngleLimit"><c>m_flAngleLimit</c>.</param>
        /// <param name="MinYaw"><c>m_flMinYaw</c>.</param>
        /// <param name="MaxYaw"><c>m_flMaxYaw</c>.</param>
        /// <param name="YawFriction"><c>m_flYawFriction</c>.</param>
        /// <param name="YawBounce"><c>m_flYawBounce</c>.</param>
        /// <param name="MinPitch"><c>m_flMinPitch</c>.</param>
        /// <param name="MaxPitch"><c>m_flMaxPitch</c>.</param>
        /// <param name="PitchFriction"><c>m_flPitchFriction</c>.</param>
        /// <param name="PitchBounce"><c>m_flPitchBounce</c>.</param>
        /// <param name="BaseMass"><c>m_flBaseMass</c>.</param>
        /// <param name="BaseStiffness"><c>m_flBaseStiffness</c>.</param>
        /// <param name="BaseDamping"><c>m_flBaseDamping</c>.</param>
        /// <param name="BaseMinLeft"><c>m_flBaseMinLeft</c>.</param>
        /// <param name="BaseMaxLeft"><c>m_flBaseMaxLeft</c>.</param>
        /// <param name="BaseLeftFriction"><c>m_flBaseLeftFriction</c>.</param>
        /// <param name="BaseMinUp"><c>m_flBaseMinUp</c>.</param>
        /// <param name="BaseMaxUp"><c>m_flBaseMaxUp</c>.</param>
        /// <param name="BaseUpFriction"><c>m_flBaseUpFriction</c>.</param>
        /// <param name="BaseMinForward"><c>m_flBaseMinForward</c>.</param>
        /// <param name="BaseMaxForward"><c>m_flBaseMaxForward</c>.</param>
        /// <param name="BaseForwardFriction"><c>m_flBaseForwardFriction</c>.</param>
        /// <param name="Radius0"><c>m_flRadius0</c>.</param>
        /// <param name="Radius1"><c>m_flRadius1</c>.</param>
        /// <param name="Point0"><c>m_vPoint0</c>.</param>
        /// <param name="Point1"><c>m_vPoint1</c>.</param>
        /// <param name="CollisionMask"><c>m_nCollisionMask</c>.</param>
        public readonly record struct FeJiggleBone(
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

        /// <summary>
        /// A jiggle bone keyed to its node (<c>CFeIndexedJiggleBone</c>). Both indices are stored unsigned, so the all-ones
        /// "none" value reads as -1.
        /// </summary>
        /// <param name="Node"><c>m_nNode</c>.</param>
        /// <param name="JiggleParent"><c>m_nJiggleParent</c>.</param>
        /// <param name="JiggleBone"><c>m_jiggleBone</c>.</param>
        public readonly record struct FeIndexedJiggleBone(int Node, int JiggleParent, FeJiggleBone JiggleBone);

        /// <summary>A named vertex selection (<c>FeVertexMapDesc_t</c>).</summary>
        /// <param name="Name"><c>sName</c>.</param>
        /// <param name="NameHash"><c>nNameHash</c>.</param>
        /// <param name="Color"><c>nColor</c>.</param>
        /// <param name="Flags"><c>nFlags</c>.</param>
        /// <param name="VertexBase">The first node the selection covers (<c>nVertexBase</c>).</param>
        /// <param name="VertexCount">How many consecutive nodes it covers (<c>nVertexCount</c>).</param>
        /// <param name="MapOffset">Where its weights start in <see cref="FeModel.VertexMapValues"/> (<c>nMapOffset</c>).</param>
        /// <param name="NodeListOffset"><c>nNodeListOffset</c>.</param>
        /// <param name="CenterOfMass"><c>vCenterOfMass</c>.</param>
        /// <param name="VolumetricSolveStrength"><c>flVolumetricSolveStrength</c>.</param>
        /// <param name="ScaleSourceNode">The node whose scale the selection follows, or -1 (<c>nScaleSourceNode</c>).</param>
        /// <param name="NodeListCount"><c>nNodeListCount</c>.</param>
        public readonly record struct FeVertexMapDesc(string Name, uint NameHash, uint Color, uint Flags, int VertexBase, int VertexCount,
            int MapOffset, int NodeListOffset, Vector3 CenterOfMass, float VolumetricSolveStrength, int ScaleSourceNode, int NodeListCount);

        /// <summary>A deprecated morph layer (<c>FeMorphLayerDepr_t</c>).</summary>
        /// <param name="Name"><c>m_Name</c>.</param>
        /// <param name="NameHash"><c>m_nNameHash</c>.</param>
        /// <param name="Nodes"><c>m_Nodes</c>.</param>
        /// <param name="InitPos"><c>m_InitPos</c>.</param>
        /// <param name="Gravity"><c>m_Gravity</c>.</param>
        /// <param name="GoalStrength"><c>m_GoalStrength</c>.</param>
        /// <param name="GoalDamping"><c>m_GoalDamping</c>.</param>
        /// <param name="Flags"><c>m_nFlags</c>.</param>
        public readonly record struct FeMorphLayer(string Name, uint NameHash, int[] Nodes, Vector3[] InitPos,
            float[] Gravity, float[] GoalStrength, float[] GoalDamping, uint Flags);

        #endregion
    }
}
