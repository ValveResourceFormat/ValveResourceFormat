using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SoftbodyPhysics;

/// <summary>
/// Softbody / cloth finite-element model.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ContinuousEmitter">C_OP_ContinuousEmitter</seealso>
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

    /*
     * Names of all bones and procedural nodes involved in the softbody sim.
     * Most `nNode` properties in other structures are indices into this list.
     * `m_nStaticNodes` and `m_nRotLockStaticNodes` count, from the start of the list,
     * how many nodes are static (not simulated) and rotation-locked respectively.
    */

    /// <summary>
    /// Names of all bones and procedural nodes involved in the simulation.
    /// </summary>
    public string[] CtrlName => Data.GetArray<string>("m_CtrlName") ?? [];

    /// <summary>
    /// Per-node simulation parameters, one entry per <see cref="CtrlName"/> entry.
    /// </summary>
    public FeModelNode[] Nodes => nodes ??= GetNodes();

    /*
     * Describes ClothChains. The first `m_nRopeCount` entries of `m_Ropes` are index
     * boundaries into the list itself; entries beyond that are indices into `m_CtrlName`.
     * So the first rope spans [`m_nRopeCount`, `m_Ropes[0]`), the second [`m_Ropes[0]`, `m_Ropes[1]`), etc.
    */

    /// <summary>
    /// Cloth chains, each as an array of node indices into <see cref="CtrlName"/>.
    /// </summary>
    public int[][] Ropes => ropes ??= GetRopes();

    /// <summary>
    /// Indices of free (unparented) cloth nodes.
    /// </summary>
    public int[] FreeNodes => Data.GetInt32Array("m_FreeNodes");

    /*
     * Basic colliders. `vSphere` is packed as `[x, y, z, r]` where `[x, y, z]` is the
     * center and `r` the radius. `nNode` is the index of the parent bone.
    */

    /// <summary>
    /// Single-sphere rigid colliders.
    /// </summary>
    public SphereRigid[] SphereRigids
        => sphereRigids ??= [.. (Data.GetArray("m_SphereRigids") ?? []).Select(c => new SphereRigid(c))];

    /// <summary>
    /// Tapered-capsule rigid colliders.
    /// </summary>
    public TaperedCapsuleRigid[] TaperedCapsuleRigids
        => taperedCapsuleRigids ??= [.. (Data.GetArray("m_TaperedCapsuleRigids") ?? []).Select(c => new TaperedCapsuleRigid(c))];

    /// <summary>
    /// Box rigid colliders.
    /// </summary>
    public BoxRigid[] BoxRigids
        => boxRigids ??= [.. (Data.GetArray("m_BoxRigids") ?? []).Select(b => new BoxRigid(b))];

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

        return Enumerable.Range(0, ctrlNames.Length).Select(i => new FeModelNode
        {
            ControlBone = ctrlNames[i],
            ControlParent = i < ctrlParents.Length && ctrlParents[i] != -1 ? ctrlNames[ctrlParents[i]] : null,
            GoalStrength = i < nodeIntegrator.Count
                ? MathF.Pow(nodeIntegrator[i].GetFloatProperty("flAnimationForceAttraction"), 1f / 3f)
                : 0f,
            GoalDamping = 0f, // TODO reverse this from flAnimationVertexAttraction
            Gravity = i < nodeIntegrator.Count ? nodeIntegrator[i].GetFloatProperty("flGravity") / 360f : 0f,
            Mass = 1f, // TODO
            CollisionRadius = collisionRadii.ElementAtOrDefault(i - staticNodeCount),
            Friction = nodeFrictions.ElementAtOrDefault(i - staticNodeCount),
            HasStrayRadius = strayParameters[i].Has,
            StrayRadius = strayParameters[i].Radius,
            StrayStretchiness = strayParameters[i].Relaxation,
            IsStatic = i < staticNodeCount,
            AllowRotation = i >= rotLockedNodeCount,
        }).ToArray();
    }

    private FeModelNode[]? nodes;
    private int[][]? ropes;
    private SphereRigid[]? sphereRigids;
    private TaperedCapsuleRigid[]? taperedCapsuleRigids;
    private BoxRigid[]? boxRigids;

    private readonly KVObject Data;

    /// <summary>
    /// Per-node simulation parameters parsed from the various parallel arrays in a <see cref="PhysFeModel"/>.
    /// </summary>
    public readonly struct FeModelNode
    {
        /// <summary>Name of the bone this node controls.</summary>
        public string ControlBone { get; init; }
        /// <summary>Name of the parent bone, or <see langword="null"/> if the node has no parent.</summary>
        public string? ControlParent { get; init; }
        /// <summary>Animation goal attraction strength.</summary>
        public float GoalStrength { get; init; }
        /// <summary>Animation goal damping.</summary>
        public float GoalDamping { get; init; }
        /// <summary>Node collision radius.</summary>
        public float CollisionRadius { get; init; }
        /// <summary>Node friction.</summary>
        public float Friction { get; init; }
        /// <summary>Gravity scale applied to this node.</summary>
        public float Gravity { get; init; }
        /// <summary>Node mass.</summary>
        public float Mass { get; init; }
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
