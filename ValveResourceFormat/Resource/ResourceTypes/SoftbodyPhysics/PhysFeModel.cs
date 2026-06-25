using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SoftbodyPhysics;

/// <summary>
/// Softbody / cloth finite-element model.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/PhysFeModelDesc_t">PhysFeModelDesc_t</seealso>
public class PhysFeModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PhysFeModel"/> class.
    /// </summary>
    /// <param name="data">The <c>m_pFeModel</c> key-value object.</param>
    public PhysFeModel(KVObject data)
    {
        Data = data;
    }

    private readonly KVObject Data;

    #region Scalars

    /// <summary>Bitmask of which nodes are static.</summary>
    public uint StaticNodeFlags => Data.GetUInt32Property("m_nStaticNodeFlags");

    /// <summary>Bitmask of which nodes are dynamic.</summary>
    public uint DynamicNodeFlags => Data.GetUInt32Property("m_nDynamicNodeFlags");

    /// <summary>Local force applied to the simulation.</summary>
    public float LocalForce => Data.GetFloatProperty("m_flLocalForce");

    /// <summary>Local rotation applied to the simulation.</summary>
    public float LocalRotation => Data.GetFloatProperty("m_flLocalRotation");

    /// <summary>Total number of nodes.</summary>
    public int NodeCount => Data.GetInt32Property("m_nNodeCount");

    /// <summary>Number of static (non-simulated) nodes at the start of the node list.</summary>
    public int StaticNodes => Data.GetInt32Property("m_nStaticNodes");

    /// <summary>Number of rotation-locked static nodes at the start of the node list.</summary>
    public int RotLockStaticNodes => Data.GetInt32Property("m_nRotLockStaticNodes");

    /// <summary>Index of the first position-driven node.</summary>
    public int FirstPositionDrivenNode => Data.GetInt32Property("m_nFirstPositionDrivenNode");

    /// <summary>Depth of the collision bounding-volume tree.</summary>
    public int TreeDepth => Data.GetInt32Property("m_nTreeDepth");

    /// <summary>Windage applied to the simulation.</summary>
    public float Windage => Data.GetFloatProperty("m_flWindage");

    /// <summary>Wind drag applied to the simulation.</summary>
    public float WindDrag => Data.GetFloatProperty("m_flWindDrag");

    /// <summary>Default internal (volumetric) pressure.</summary>
    public float InternalPressure => Data.GetFloatProperty("m_flInternalPressure");

    /// <summary>Default surface stretch.</summary>
    public float DefaultSurfaceStretch => Data.GetFloatProperty("m_flDefaultSurfaceStretch");

    /// <summary>Default thread stretch.</summary>
    public float DefaultThreadStretch => Data.GetFloatProperty("m_flDefaultThreadStretch");

    /// <summary>Default gravity scale.</summary>
    public float DefaultGravityScale => Data.GetFloatProperty("m_flDefaultGravityScale");

    /// <summary>Default linear velocity air drag.</summary>
    public float DefaultVelAirDrag => Data.GetFloatProperty("m_flDefaultVelAirDrag");

    /// <summary>Default exponential air drag.</summary>
    public float DefaultExpAirDrag => Data.GetFloatProperty("m_flDefaultExpAirDrag");

    /// <summary>Default quad linear velocity air drag.</summary>
    public float DefaultVelQuadAirDrag => Data.GetFloatProperty("m_flDefaultVelQuadAirDrag");

    /// <summary>Default quad exponential air drag.</summary>
    public float DefaultExpQuadAirDrag => Data.GetFloatProperty("m_flDefaultExpQuadAirDrag");

    /// <summary>Radius added to nodes when testing world collision.</summary>
    public float AddWorldCollisionRadius => Data.GetFloatProperty("m_flAddWorldCollisionRadius");

    /// <summary>Default volumetric solve amount.</summary>
    public float DefaultVolumetricSolveAmount => Data.GetFloatProperty("m_flDefaultVolumetricSolveAmount");

    #endregion

    /*
     * Names of all bones and procedural nodes involved in the softbody sim.
     * Most `nNode` properties in other structures are indices into this list.
     * `m_nStaticNodes` and `m_nRotLockStaticNodes` count, from the start of the list,
     * how many nodes are static (not simulated) and rotation-locked respectively.
    */

    /// <summary>Names of all bones and procedural nodes involved in the simulation.</summary>
    public string[] CtrlName => Data.GetArray<string>("m_CtrlName") ?? [];

    /// <summary>Murmur hashes of <see cref="CtrlName"/>.</summary>
    public uint[] CtrlHash => ctrlHash ??= [.. (Data.GetArray<object>("m_CtrlHash") ?? []).Select(Convert.ToUInt32)];

    /// <summary>Per-node simulation parameters, one entry per <see cref="CtrlName"/> entry.</summary>
    public FeModelNode[] Nodes => nodes ??= GetNodes();

    /// <summary>Inverse mass for each node (0 means immovable).</summary>
    public float[] NodeInvMasses => Data.GetFloatArray("m_NodeInvMasses");

    /*
     * Describes ClothChains. The first `m_nRopeCount` entries of `m_Ropes` are index
     * boundaries into the list itself; entries beyond that are indices into `m_CtrlName`.
     * So the first rope spans [`m_nRopeCount`, `m_Ropes[0]`), the second [`m_Ropes[0]`, `m_Ropes[1]`), etc.
    */

    /// <summary>Cloth chains, each as an array of node indices into <see cref="CtrlName"/>.</summary>
    public int[][] Ropes => ropes ??= GetRopes();

    /// <summary>Indices of free (unparented) cloth nodes.</summary>
    public int[] FreeNodes => Data.GetInt32Array("m_FreeNodes");

    /// <summary>Basis frames for procedurally generated cloth nodes.</summary>
    public FeNodeBase[] NodeBases => nodeBases ??= Build("m_NodeBases", d => new FeNodeBase(d));

    /// <summary>Distance constraints (springs) between nodes.</summary>
    public FeRod[] Rods => rods ??= Build("m_Rods", d => new FeRod(d));

    /// <summary>Spring constraints with explicit constants.</summary>
    public FeSpringIntegrator[] SpringIntegrators
        => springIntegrators ??= Build("m_SpringIntegrator", d => new FeSpringIntegrator(d));

    /// <summary>Axial edge bend constraints.</summary>
    public FeAxialEdgeBend[] AxialEdges => axialEdges ??= Build("m_AxialEdges", d => new FeAxialEdgeBend(d));

    /// <summary>Follow constraints making child nodes follow parents.</summary>
    public FeFollowNode[] FollowNodes => followNodes ??= Build("m_FollowNodes", d => new FeFollowNode(d));

    /// <summary>Positional offsets of child controls relative to parents.</summary>
    public FeCtrlOffset[] CtrlOffsets => ctrlOffsets ??= Build("m_CtrlOffsets", d => new FeCtrlOffset(d));

    /// <summary>Object-space parent/child control relationships.</summary>
    public FeCtrlOsOffset[] CtrlOsOffsets => ctrlOsOffsets ??= Build("m_CtrlOsOffsets", d => new FeCtrlOsOffset(d));

    /// <summary>Boxes limiting how far nodes may stray.</summary>
    public FeNodeStrayBox[] NodeStrayBoxes => nodeStrayBoxes ??= Build("m_NodeStrayBoxes", d => new FeNodeStrayBox(d));

    /// <summary>Per-span world collision friction parameters.</summary>
    public FeWorldCollisionParams[] WorldCollisionParams
        => worldCollisionParams ??= Build("m_WorldCollisionParams", d => new FeWorldCollisionParams(d));

    /// <summary>Node indices that participate in world collision.</summary>
    public int[] WorldCollisionNodes => Data.GetInt32Array("m_WorldCollisionNodes");

    /// <summary>Parent index of each node in the collision bounding-volume tree.</summary>
    public int[] TreeParents => Data.GetInt32Array("m_TreeParents");

    /// <summary>Collision mask of each node in the collision bounding-volume tree.</summary>
    public int[] TreeCollisionMasks => Data.GetInt32Array("m_TreeCollisionMasks");

    /// <summary>Children of each internal node of the collision bounding-volume tree.</summary>
    public FeTreeChildren[] TreeChildren => treeChildren ??= Build("m_TreeChildren", d => new FeTreeChildren(d));

    /*
     * Basic colliders. `vSphere` is packed as `[x, y, z, r]` where `[x, y, z]` is the
     * center and `r` the radius. `nNode` is the index of the parent bone.
    */

    /// <summary>Single-sphere rigid colliders.</summary>
    public SphereRigid[] SphereRigids => sphereRigids ??= Build("m_SphereRigids", d => new SphereRigid(d));

    /// <summary>Tapered-capsule rigid colliders.</summary>
    public TaperedCapsuleRigid[] TaperedCapsuleRigids
        => taperedCapsuleRigids ??= Build("m_TaperedCapsuleRigids", d => new TaperedCapsuleRigid(d));

    /// <summary>Box rigid colliders.</summary>
    public BoxRigid[] BoxRigids => boxRigids ??= Build("m_BoxRigids", d => new BoxRigid(d));

    private T[] Build<T>(string key, Func<KVObject, T> factory)
        => [.. (Data.GetArray(key) ?? []).Select(factory)];

    private int[][] GetRopes()
    {
        var nRopes = Data.GetInt32Property("m_nRopeCount");
        if (nRopes == 0)
        {
            return [];
        }

        var rawRopes = Data.GetInt32Array("m_Ropes");
        var ropeList = new int[nRopes][];

        var separators = new ArraySegment<int>(rawRopes, 0, nRopes).Prepend(nRopes).ToArray();

        for (var i = 0; i < nRopes; i++)
        {
            ropeList[i] = rawRopes[separators[i]..separators[i + 1]];
        }

        return ropeList;
    }

    private FeModelNode[] GetNodes()
    {
        var staticNodeCount = Data.GetInt32Property("m_nStaticNodes");
        var rotLockedNodeCount = Data.GetInt32Property("m_nRotLockStaticNodes");
        var ctrlNames = Data.GetArray<string>("m_CtrlName") ?? [];
        var ctrlParents = Data.GetInt32Array("m_SkelParents");
        var nodeIntegrator = Data.GetArray("m_NodeIntegrator") ?? [];
        var nodeInvMasses = Data.GetFloatArray("m_NodeInvMasses");

        // These arrays skip static nodes and are completely empty when every node uses default values.
        var collisionRadii = Data.GetFloatArray("m_NodeCollisionRadii");
        var nodeFrictions = Data.GetFloatArray("m_DynNodeFriction");

        // (has_stray_radius, stray_radius, stray_relaxation), indexed by full node index.
        var strayParameters = new (bool Has, float Radius, float Relaxation)[ctrlNames.Length];
        foreach (var strayParameter in Data.GetArray("m_AnimStrayRadii") ?? [])
        {
            var nodeIndices = strayParameter.GetInt32Array("nNode");
            // The pair is expected to reference a single node; skip if it spans two different nodes.
            if (nodeIndices.Length < 2 || nodeIndices[0] != nodeIndices[1])
            {
                continue;
            }

            var n = nodeIndices[0];
            if (n < 0 || n >= strayParameters.Length)
            {
                continue;
            }

            strayParameters[n] = (true,
                strayParameter.GetFloatProperty("flMaxDist"),
                strayParameter.GetFloatProperty("flRelaxationFactor"));
        }

        return [.. Enumerable.Range(0, ctrlNames.Length).Select(i => new FeModelNode
        {
            ControlBone = ctrlNames[i],
            ControlParent = i < ctrlParents.Length && ctrlParents[i] != -1 ? ctrlNames[ctrlParents[i]] : null,
            PointDamping = i < nodeIntegrator.Count ? nodeIntegrator[i].GetFloatProperty("flPointDamping") : 0f,
            GoalStrength = i < nodeIntegrator.Count
                ? MathF.Pow(nodeIntegrator[i].GetFloatProperty("flAnimationForceAttraction"), 1f / 3f)
                : 0f,
            AnimationVertexAttraction = i < nodeIntegrator.Count
                ? nodeIntegrator[i].GetFloatProperty("flAnimationVertexAttraction")
                : 0f,
            GoalDamping = 0f, // TODO reverse this from flAnimationVertexAttraction
            Gravity = i < nodeIntegrator.Count ? nodeIntegrator[i].GetFloatProperty("flGravity") / 360f : 0f,
            Mass = 1f, // TODO
            InvMass = nodeInvMasses.ElementAtOrDefault(i),
            CollisionRadius = collisionRadii.ElementAtOrDefault(i - staticNodeCount),
            Friction = nodeFrictions.ElementAtOrDefault(i - staticNodeCount),
            HasStrayRadius = strayParameters[i].Has,
            StrayRadius = strayParameters[i].Radius,
            StrayStretchiness = strayParameters[i].Relaxation,
            IsStatic = i < staticNodeCount,
            AllowRotation = i >= rotLockedNodeCount,
        })];
    }

    private uint[]? ctrlHash;
    private FeModelNode[]? nodes;
    private int[][]? ropes;
    private FeNodeBase[]? nodeBases;
    private FeRod[]? rods;
    private FeSpringIntegrator[]? springIntegrators;
    private FeAxialEdgeBend[]? axialEdges;
    private FeFollowNode[]? followNodes;
    private FeCtrlOffset[]? ctrlOffsets;
    private FeCtrlOsOffset[]? ctrlOsOffsets;
    private FeNodeStrayBox[]? nodeStrayBoxes;
    private FeWorldCollisionParams[]? worldCollisionParams;
    private FeTreeChildren[]? treeChildren;
    private SphereRigid[]? sphereRigids;
    private TaperedCapsuleRigid[]? taperedCapsuleRigids;
    private BoxRigid[]? boxRigids;

    /// <summary>
    /// Per-node simulation parameters parsed from the various parallel arrays in a <see cref="PhysFeModel"/>.
    /// </summary>
    public readonly struct FeModelNode
    {
        /// <summary>Name of the bone this node controls.</summary>
        public string ControlBone { get; init; }
        /// <summary>Name of the parent bone, or <see langword="null"/> if the node has no parent.</summary>
        public string? ControlParent { get; init; }
        /// <summary>Point damping applied to this node.</summary>
        public float PointDamping { get; init; }
        /// <summary>Animation goal attraction strength.</summary>
        public float GoalStrength { get; init; }
        /// <summary>Animation goal damping.</summary>
        public float GoalDamping { get; init; }
        /// <summary>Animation vertex attraction.</summary>
        public float AnimationVertexAttraction { get; init; }
        /// <summary>Node collision radius.</summary>
        public float CollisionRadius { get; init; }
        /// <summary>Node friction.</summary>
        public float Friction { get; init; }
        /// <summary>Gravity scale applied to this node.</summary>
        public float Gravity { get; init; }
        /// <summary>Node mass.</summary>
        public float Mass { get; init; }
        /// <summary>Inverse node mass (0 means immovable).</summary>
        public float InvMass { get; init; }
        /// <summary>Whether this node has a stray radius constraint.</summary>
        public bool HasStrayRadius { get; init; }
        /// <summary>Stray radius maximum distance.</summary>
        public float StrayRadius { get; init; }
        /// <summary>Stray radius relaxation / stretchiness factor.</summary>
        public float StrayStretchiness { get; init; }
        /// <summary>Whether this node is static (not simulated).</summary>
        public bool IsStatic { get; init; }
        /// <summary>Whether this node is allowed to rotate.</summary>
        public bool AllowRotation { get; init; }
    }
}
