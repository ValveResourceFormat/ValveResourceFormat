
using System.Linq;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.ModelExtract;

namespace ValveResourceFormat.ResourceTypes.SoftbodyPhysics;
public class FeModelAggregateData
{
    public FeModelAggregateData(KVObject data)
    {
        Data = data;
    }
    /* 
     * Names of all bones and procedural nodes involved in the softbody sim. 
     * Most `nNode` properties in other structures seem to be indices into this list. 
     * The fields `m_nRotLockStaticNodes` and `m_nStaticNodes`  seem to correspond to 
     * the `Simulate` and `Allow Rotation` flags you can set in ClothChains for example, 
     * each describing the number of elements from the beginning of the list.
    */
    public string[] CtrlName => Data.GetArray<string>("m_CtrlName");

    public FeModelNode[] Nodes => nodes ??= GetNodes();

    /*
     * Seems to describe ClothChains. The first `m_nRopeCount` entries 
     * are index boundaries into the list itself, each describing a chain while the ones behind
     * that are indices into `m_CtrlName`.
     * So the first rope is between `m_nRopeCount` inclusive and `m_Ropes[0]`
     * exclusive, the second between `m_Ropes[0]` and `m_Ropes[1]` and so on.
    */
    public int[][] Ropes => ropes ??= GetRopes();

    public int[] FreeNodes => Data.GetInt32Array("m_FreeNodes");

    /* 
     * Basic Colliders. `vSphere` encodes the caps in the 
     * form `[x, y, z, r]` where `[x, y, z]` are the point coordinates and `r` the sphere 
     * radius. `nNode` is the index of the parent bone. 
    */
    public SphereCollectionRigid[] SphereRigids
        => sphereRigids ??= Data.GetArray<KVObject>("m_SphereRigids")
        .Select(c => new SphereCollectionRigid(c)).ToArray();

    public SphereCollectionRigid[] TaperedCapsuleRigids
        => taperedCapsuleRigids ??= Data.GetArray<KVObject>("m_TaperedCapsuleRigids")
        .Select(c => new SphereCollectionRigid(c)).ToArray();

    public BoxRigid[] BoxRigids
        => boxRigids ??= Data.GetArray<KVObject>("m_BoxRigids")
        .Select(b => new BoxRigid(b)).ToArray();

    private int[][] GetRopes()
    {
        var nRopes = Data.GetInt32Property("m_nRopeCount");
        var rawRopes = Data.GetInt32Array("m_Ropes");
        var ropes = new int[nRopes][];

        var separators = new ArraySegment<int>(rawRopes, 0, nRopes).Prepend(nRopes).ToArray();

        for (var i = 0; i < nRopes; i++)
        {
            ropes[i] = rawRopes[separators[i]..separators[i + 1]];
        }

        return ropes;
    }

    private FeModelNode[] GetNodes()
    {
        var staticNodeCount = Data.GetInt32Property("m_nStaticNodes");
        var rotLockedNodeCount = Data.GetInt32Property("m_nRotLockStaticNodes");
        var ctrlNames = Data.GetArray<string>("m_CtrlName");
        var ctrlParents = Data.GetInt32Array("m_SkelParents");
        var nodeIntegrator = Data.GetArray("m_NodeIntegrator");

        // these arrays skip static nodes and are completely empty if there
        // are no non-default values in all nodes
        var collissionRadii = Data.GetFloatArray("m_NodeCollisionRadii");
        var nodeFrictions = Data.GetFloatArray("m_DynNodeFriction");

        // (has_stray_radius, stray_radius, stray_relaxation)
        var strayParameters = new (bool, float, float)[ctrlNames.Length];
        foreach (var strayParameter in Data.GetArray("m_AnimStrayRadii"))
        {
            var nodeIndices = strayParameter.GetInt32Array("nNode");
            // these are probably just redundant,
            // but in case they aren't or get changed not to be
            if (nodeIndices[0] != nodeIndices[1])
            {
                continue;
            }
            var i = nodeIndices[0] - staticNodeCount;
            strayParameters[i].Item1 = true;
            strayParameters[i].Item2 = strayParameter.GetFloatProperty("flMaxDist");
            strayParameters[i].Item3 = strayParameter.GetFloatProperty("flRelaxationFactor");
        }

        return Enumerable.Range(0, ctrlNames.Length).Select(i => new FeModelNode
        {
            ControlBone = ctrlNames[i],
            ControlParent = ctrlParents[i] != -1 ? ctrlNames[ctrlParents[i]] : null,
            GoalStrength = MathF.Pow(nodeIntegrator[i].GetFloatProperty("flAnimationForceAttraction"), 1f / 3f),
            GoalDamping = 0f, // TODO reverse this from flAnimationVertexAttraction
            Gravity = nodeIntegrator[i].GetFloatProperty("flGravity") / 360f,
            Mass = 1f, // TODO
            CollissionRadius = collissionRadii.ElementAtOrDefault(i - staticNodeCount),
            Friction = nodeFrictions.ElementAtOrDefault(i - staticNodeCount),
            HasStrayRadius = strayParameters[i].Item1,
            StrayRadius = strayParameters[i].Item2,
            StrayStretchiness = strayParameters[i].Item3,
            IsStatic = i < staticNodeCount,
            AllowRotation = i >= rotLockedNodeCount,
        }).ToArray();
    }

    private FeModelNode[] nodes;

    private int[][] ropes;

    private SphereCollectionRigid[] sphereRigids;

    private SphereCollectionRigid[] taperedCapsuleRigids;

    private BoxRigid[] boxRigids;

    private readonly KVObject Data;

    public readonly struct FeModelNode
    {
        public KVObject MakeClothNode()
        {
            return new KVObject(null,
                ("cloth_node_root_bone", ControlBone),
                ("has_stray_radius", HasStrayRadius),
                ("has_world_collision", false), // TODO
                ("lock_translation", false), // TODO
                ("gravity_z", Gravity),
                ("goal_strength", GoalStrength),
                ("goal_damping", GoalDamping),
                ("mass", Mass),
                ("friction", Friction),
                ("stray_radius", StrayRadius),
                ("stray_radius_relaxation_factor", StrayStretchiness),
                ("collision_radius", CollissionRadius),
                ("is_static_node", IsStatic),
                ("allow_rotation", AllowRotation),
                ("super_damping", 0f) // TODO
            );
        }
        public KVObject MakeClothChainJoint()
        {
            var node = new KVObject(null,
                ("joint_name", ControlBone),
                ("gravity_z", Gravity),
                ("goal_strength", GoalStrength),
                ("goal_damping", GoalDamping),
                ("mass", Mass),
                ("friction", Friction),
                ("stray_radius", StrayRadius),
                ("stray_radius_stretchiness", StrayStretchiness),
                ("collision_radius", CollissionRadius),
                ("simulate", !IsStatic),
                ("allow_rotation", AllowRotation)
            );
            if (ControlParent is not null)
            {
                node.AddProperty("joint_parent", ControlParent);
            }
            return node;
        }
        public string ControlBone { get; init; }
        public string? ControlParent { get; init; }
        public float GoalStrength { get; init; }
        public float GoalDamping { get; init; }
        public float CollissionRadius { get; init; }
        public float Friction { get; init; }
        public float Gravity { get; init; }
        public float Mass { get; init; }
        public bool HasStrayRadius { get; init; }
        public float StrayRadius { get; init; }
        public float StrayStretchiness { get; init; }
        public bool IsStatic { get; init; }
        public bool AllowRotation { get; init; }
    }
}
