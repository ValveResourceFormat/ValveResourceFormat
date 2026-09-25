using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
    // An unrolled proxy ring sits on the joint frame's +Y, so an authored twist counts down from 90 degrees.
    const float ClothExtrudeTwistBase = 90f;

    /// <summary>
    /// The <c>extrude_twist</c> a joint row states for a ring measured at <paramref name="measuredTwist"/>
    /// degrees of roll.
    /// </summary>
    internal static float ClothExtrudeTwistKey(float measuredTwist) => ClothExtrudeTwistBase - measuredTwist;

    /// <summary>
    /// The <c>extrude_twist</c> default a chain's <c>attrs</c> table states. It is the key's schema
    /// default, which every authored chain carries whatever its ring measures, and the compiler reads
    /// it for a joint row that omits the key.
    /// </summary>
    const float ClothExtrudeTwistAttrDefault = 0f;

    // A twist a static chain root authored compiles to the same pair of relaxation-free entries at every
    // value above zero, so the re-declaration names the top of the key's range.
    const float ClothStaticRootTwistRelax = 1f;

    /// <summary>
    /// Whether the lock the original carries on <paramref name="chain"/> belongs to a <c>ClothRigidCloudCluster</c> of
    /// algorithm 0 rather than to the chain's own format. Algorithm 0 compiles its <c>parent_node</c> into
    /// <c>m_LockToGoal</c> and nothing else, while a chain below version 2 that locks a joint leaves its joints' node bases
    /// bulk graded, so a lock beside preset-graded bases is the cluster's. The cluster needs members, so only a locked
    /// joint with a chain child qualifies.
    /// </summary>
    internal static bool IsRigidCloudClusterLock(FeModel feModel, FeModel.BoneChain chain)
        => LockedJointsWithChildren(feModel, chain).Any() && feModel.ChainBasesAreBulkGraded(chain) == false;

    internal static IEnumerable<(FeModel.BoneChainJoint Joint, List<FeModel.BoneChainJoint> Children)> LockedJointsWithChildren(
        FeModel feModel, FeModel.BoneChain chain)
        => chain.Joints
            .Where(joint => feModel.IsLockedToGoal(joint.Node))
            .Select(joint => (joint, chain.Joints.Where(child => child.ParentNode == joint.Node).ToList()))
            .Where(static entry => entry.Item2.Count > 0);

    /// <summary>
    /// Declares the <c>ClothRigidCloudCluster</c> of algorithm 0 behind every chain lock
    /// <see cref="IsRigidCloudClusterLock"/> attributes to one.
    /// </summary>
    internal static void AddClothRigidCloudClusterLocks(KVObject softbodyChildren, FeModel feModel,
        IEnumerable<FeModel.BoneChain> chains)
    {
        foreach (var chain in chains.Where(chain => IsRigidCloudClusterLock(feModel, chain)))
        {
            foreach (var (joint, _) in LockedJointsWithChildren(feModel, chain))
            {
                softbodyChildren.Add(MakeClothRigidCloudCluster(joint.Name, RigidCloudClusterMembers(chain, joint)));
            }
        }
    }

    /// <summary>
    /// The members declared for the <c>ClothRigidCloudCluster</c> locking <paramref name="joint"/>. Algorithm 0 compiles no
    /// member into the file and refuses a cluster of fewer than two, while any two members compile to the same lock, so the
    /// joint's chain descendants are named a generation at a time until there are two, and the joint itself completes a pair
    /// its subtree cannot.
    /// </summary>
    internal static List<string> RigidCloudClusterMembers(FeModel.BoneChain chain, FeModel.BoneChainJoint joint)
    {
        List<string> members = [];
        List<int> generation = [joint.Node];
        while (members.Count < 2 && generation.Count > 0)
        {
            List<FeModel.BoneChainJoint> next = [.. chain.Joints.Where(child => generation.Contains(child.ParentNode))];
            members.AddRange(next.Select(static child => child.Name));
            generation = [.. next.Select(static child => child.Node)];
        }

        if (members.Count < 2)
        {
            members.Insert(0, joint.Name);
        }

        return members;
    }

    /// <summary>
    /// A <c>ClothRigidCloudCluster</c> of algorithm 0 locking <paramref name="parentNode"/>, with
    /// <paramref name="members"/> as its joints at the default stiffness.
    /// </summary>
    internal static KVObject MakeClothRigidCloudCluster(string parentNode, IEnumerable<string> members)
    {
        var joints = KVObject.Array();
        foreach (var member in members)
        {
            var joint = KVObject.Collection();
            joint.Add("joint_name", member);
            joint.Add("stiffness", 1f);
            joints.Add(joint);
        }

        var jointName = KVObject.Collection();
        jointName.Add("display", "Joint Name");
        jointName.Add("show", true);
        jointName.Add("ui_order", 0);
        jointName.Add("default", string.Empty);
        var stiffness = KVObject.Collection();
        stiffness.Add("display", "Stiffness");
        stiffness.Add("show", true);
        stiffness.Add("ui_order", 1);
        stiffness.Add("default", 1f);
        var attrs = KVObject.Collection();
        attrs.Add("joint_name", jointName);
        attrs.Add("stiffness", stiffness);

        var chainData = KVObject.Collection();
        chainData.Add("joints", joints);
        chainData.Add("attrs", attrs);
        chainData.Add("selection", KVObject.Array());
        chainData.Add("version", 0);

        return MakeNode("ClothRigidCloudCluster",
            ("name", parentNode + "_rigid_cloud"),
            ("algorithm", 0),
            ("parent_node", parentNode),
            ("chain", chainData));
    }

    /// <summary>
    /// The <c>ClothChain</c> version a chain was authored at, read off the evidence its compiled data carries.
    /// </summary>
    /// <param name="jointCount">The chain's joint count.</param>
    /// <param name="hasOtherChains">Whether the model declares another chain.</param>
    /// <param name="rootAllowsRotation">Whether the root joint rotates freely, null without a root.</param>
    /// <param name="rootHasBase">Whether the root joint carries an <c>m_NodeBases</c> entry.</param>
    /// <param name="lockedJoint">Whether any joint is in <c>m_LockToGoal</c>.</param>
    /// <param name="rigidCloudClusterLock">Whether that lock belongs to a rigid cloud cluster (<see cref="IsRigidCloudClusterLock"/>).</param>
    /// <param name="locksJoints">Whether format 1 would lock a joint of this extruding chain.</param>
    /// <param name="basesBulkGraded">Whether the joints' node bases are the bulk grade, null when they do not say.</param>
    /// <param name="hintsTwistWritten">Whether a joint's basis hint was written by the twist or rope source and never graded.</param>
    /// <param name="hasUnstagedThinJoint">Whether a joint only the version-1 fit top-up would group carries no group.</param>
    /// <param name="reverseOffsetsPreset">Whether the joints' reverse offsets name their preset bases' Y1 nodes, null when they do not say.</param>
    /// <param name="hasUnbasedLeaf">Whether a simulated leaf of a two-sided chain carries no node base (<see cref="FeModel.ChainHasUnbasedLeaf"/>).</param>
    /// <param name="siblingHubLock">Whether the chain's locks are its sibling hub's, which states nothing about the format.</param>
    /// <param name="extrudesNothing">Whether the chain extrudes no ring at all, so the version-2 preset grade raises no candidates and cannot be read.</param>
    /// <param name="fitsPresetJoint">Whether the original fits a joint the version-2 preset would base and offset (<see cref="FeModel.ChainFitsAPresetJoint"/>).</param>
    /// <param name="locksOnlyParentLocked">Whether every joint format 1 would lock is already locked to its parent in the original.</param>
    /// <param name="locksOnlyToGoal">Whether every joint format 1 would lock has no parent it could lock to, so the lock lands on its goal.</param>
    internal static int ClothChainVersion(int jointCount, bool hasOtherChains, bool? rootAllowsRotation, bool rootHasBase,
        bool lockedJoint, bool rigidCloudClusterLock, bool locksJoints, bool? basesBulkGraded, bool hintsTwistWritten,
        bool hasUnstagedThinJoint, bool? reverseOffsetsPreset = null, bool hasUnbasedLeaf = false,
        bool siblingHubLock = false, bool extrudesNothing = false, bool fitsPresetJoint = false, bool locksOnlyParentLocked = false,
        bool locksOnlyToGoal = false)
    {
        // The two chain formats are not interchangeable: format 1 registers a non-simulated joint that has
        // no parent to be offset from into m_LockToGoal, format 2 leaves it out. Both are in live use, so
        // the original's own m_LockToGoal membership is what says which one a chain was authored in, unless a
        // rigid cloud cluster declared the lock, which states nothing about the format.
        // A rotation-locked root carries a second, sharper signal: format 1 suppresses that root's
        // m_NodeBases entry and format 2 keeps it, so the original's own m_NodeBases decides those chains.
        // The node-base signal reads an ABSENT root entry as format 1, which is equally what an anchor bone
        // several sub-chains were merged under looks like: it roots no chain in the original, so nothing
        // ever gave it a base. Format 1 also locks every non-simulated, rotation-free joint of an extruding
        // chain to its goal, so an original that locks none of them rules format 1 out directly - unless that lock can only
        // land on the goal: a goal lock copies the animated goal into a static node that already holds it, so where the
        // chain's own bases are the bulk grade it does not hold the chain at version 2.
        var lockedInOriginal = lockedJoint && !rigidCloudClusterLock && !siblingHubLock;
        var guardOpen = lockedInOriginal || !locksJoints || locksOnlyParentLocked || (locksOnlyToGoal && basesBulkGraded == true);
        var rootRotationLocked = rootAllowsRotation == false;

        // A one-joint chain carries no version of its own: MEASURED 2026-09-20 on dl `haze` (30 chains,
        // six of them one joint) and `unicorn_celeste` (eleven chains, four), every chain forced to
        // version 1 and then to version 2 - both compile, no access violation. The wave-26 crash this
        // once guarded against was a LONE one-joint chain, which this condition never covered anyway,
        // and the authored sources ship one-joint chains at version 2 beside eighteen others.
        //
        // A rotation-locked root whose m_NodeBases entry is PRESENT is format 2 outright, since format 1
        // suppresses it. An ABSENT entry states nothing on its own: it is equally what a root that never
        // rooted a chain in the original looks like, and reading it as format 1 was wrong on every
        // authored chain of that shape (MEASURED 2026-09-20: haze's six hub chains, its Flame_Head and
        // bookworm's Hair are all authored version 2 and all read version 1).
        //
        // What decides it is whether the chain EXTRUDES. The version-2 preset grade runs over a joint's
        // own extrusion vector and its child's, so a RINGLESS chain raises no candidates and compiles
        // identically at either version - PROBED on dl `bookworm2`'s ringless `hair` chain, whose own
        // authored version is 2: forced from 1 to 2 it emits the same 6 node bases on the same owners,
        // the same 6 reverse offsets and the same 1092 rods. An EXTRUDING chain does differ, and its
        // absent bases are then real evidence of format 1 - PROBED on `hornet_new_default`, where forcing
        // version 2 bases `hat_base` and three `hat_flap_*` joints the original bases not at all.
        var version = rootRotationLocked && guardOpen
            ? (rootHasBase || extrudesNothing ? 2 : 1)
            : (lockedInOriginal ? 1 : 2);

        // Format 2 also grades a preset basis for every joint that has a child, over the joint's own
        // extrusion vector and its child's, where format 1 leaves those joints to the bulk pass and its
        // neighbour set. The joints' own entries say which grade the original carries, and a chain whose
        // entries are the bulk grade was authored below version 2 wherever format 1 does not also lock a
        // joint the original leaves free or drop the basis of a rotation-locked root. Where the entries do not say,
        // the reverse offsets do: format 2 records a simulated joint's offset against its preset basis' Y1 node. A joint
        // format 2 would both base and offset loses its fit group, so a fit matrix on one rules format 2 out.
        var rootKeepsPreset = rootRotationLocked && rootHasBase;
        if (version == 2 && !rootKeepsPreset && guardOpen
            && (basesBulkGraded == true || (basesBulkGraded is null && reverseOffsetsPreset == false) || fitsPresetJoint))
        {
            version = 1;
        }

        // From version 1 on the chain stages fit influences for its joints, and the hint pass then grades
        // every joint's basis hint over them. A hint the twist or rope source wrote and nothing graded is one
        // the original compiled without any influence at all, which is version 0. Version 0 locks the same
        // joints format 1 does, so an original that locks none of them rules it out too.
        if (version != 0 && !rootKeepsPreset && guardOpen && hintsTwistWritten && basesBulkGraded != false)
        {
            version = 0;
        }

        // A two-sided leaf with no node base is one only the chain itself could have based, which it does from version 1 on.
        if (version != 0 && !rootKeepsPreset && guardOpen && hasUnbasedLeaf && basesBulkGraded != false)
        {
            version = 0;
        }

        // A joint only the version-1 fit top-up gives a group to, carrying none, was staged at version 0.
        if (version == 1 && hasUnstagedThinJoint)
        {
            version = 0;
        }

        return version;
    }

    /// <summary>
    /// The <c>ClothChain</c> version <paramref name="chain"/> was authored at, read off the evidence <paramref name="feModel"/> carries.
    /// </summary>
    /// <param name="feModel">The compiled cloth.</param>
    /// <param name="chain">The reconstructed chain.</param>
    /// <param name="hasOtherChains">Whether the model declares another chain.</param>
    internal static int ClothChainVersion(FeModel feModel, FeModel.BoneChain chain, bool hasOtherChains)
    {
        var root = chain.Joints.Count > 0 ? chain.Joints[0] : null;
        return ClothChainVersion(chain.Joints.Count, hasOtherChains,
            rootAllowsRotation: root is null ? null : feModel.AllowsRotation(root.Node),
            rootHasBase: root is not null && feModel.NodeBases.ContainsKey(root.Node),
            lockedJoint: chain.Joints.Exists(joint => feModel.IsLockedToGoal(joint.Node)),
            rigidCloudClusterLock: IsRigidCloudClusterLock(feModel, chain),
            locksJoints: ChainLocksJoints(feModel, chain),
            basesBulkGraded: feModel.ChainBasesAreBulkGraded(chain),
            hintsTwistWritten: feModel.ChainHintsAreTwistWritten(chain),
            hasUnstagedThinJoint: feModel.ChainHasUnstagedThinJoint(chain),
            reverseOffsetsPreset: feModel.ChainReverseOffsetsArePreset(chain),
            hasUnbasedLeaf: feModel.ChainHasUnbasedLeaf(chain),
            siblingHubLock: feModel.SiblingSpringHubs.Contains(chain.RootBone)
                && chain.Joints.TrueForAll(joint => !feModel.IsLockedToGoal(joint.Node)
                    || joint.SpringsWithSiblings),
            extrudesNothing: chain.ExtrudeSides < 1
                && !chain.Joints.Exists(static joint => joint.RingNodes.Count > 0),
            fitsPresetJoint: feModel.ChainFitsAPresetJoint(chain),
            locksOnlyParentLocked: ChainLocksOnlyParentLockedJoints(feModel, chain),
            locksOnlyToGoal: ChainLocksJoints(feModel, chain) && !ChainLocksJointsToParent(feModel, chain));
    }

    /// <summary>
    /// Whether format 1 would lock a joint of <paramref name="chain"/> to its PARENT rather than its goal: a joint
    /// <see cref="ChainLocksJoints"/> counts whose compiled parent is simulated or rotates freely.
    /// </summary>
    /// <param name="feModel">The compiled cloth.</param>
    /// <param name="chain">The reconstructed chain.</param>
    static bool ChainLocksJointsToParent(FeModel feModel, FeModel.BoneChain chain)
        => chain.ExtrudeSides >= 1
            && chain.Joints.Exists(joint => !joint.Simulated && feModel.AllowsRotation(joint.Node)
                && (joint.RingNodes.Count > 0 || chain.Joints.Exists(child => child.ParentNode == joint.Node && child.RingNodes.Count > 0))
                && LocksToParent(feModel, joint.Node));

    static bool LocksToParent(FeModel feModel, int node)
    {
        var parent = node < feModel.SkelParents.Length ? feModel.SkelParents[node] : -1;
        return parent >= 0 && (parent >= feModel.StaticNodeCount || feModel.AllowsRotation(parent));
    }

    /// <summary>
    /// Whether every joint <see cref="ChainLocksJoints"/> counts on <paramref name="chain"/> carries an <c>m_LockToParent</c> entry in the original.
    /// </summary>
    /// <param name="feModel">The compiled cloth.</param>
    /// <param name="chain">The reconstructed chain.</param>
    internal static bool ChainLocksOnlyParentLockedJoints(FeModel feModel, FeModel.BoneChain chain)
        => chain.ExtrudeSides >= 1
            && chain.Joints.TrueForAll(joint => joint.Simulated || !feModel.AllowsRotation(joint.Node)
                || !(joint.RingNodes.Count > 0 || chain.Joints.Exists(child => child.ParentNode == joint.Node && child.RingNodes.Count > 0))
                || feModel.IsLockedToParent(joint.Node));

    /// <summary>
    /// Whether format 1 would lock a joint of <paramref name="chain"/> to its goal: a non-simulated, rotation-free joint whose
    /// fit table holds any member. The table takes the joint's own ring and every ring its chain children extruded, so a joint
    /// with neither stages no fit influence and no lock reaches it.
    /// </summary>
    /// <param name="feModel">The compiled cloth.</param>
    /// <param name="chain">The reconstructed chain.</param>
    internal static bool ChainLocksJoints(FeModel feModel, FeModel.BoneChain chain)
        => chain.ExtrudeSides >= 1
            && chain.Joints.Exists(joint => !joint.Simulated && feModel.AllowsRotation(joint.Node)
                && (joint.RingNodes.Count > 0 || chain.Joints.Exists(child => child.ParentNode == joint.Node && child.RingNodes.Count > 0)));

    static KVObject MakeClothChainNode(FeModel feModel, FeModel.BoneChain chain, bool hasOtherChains,
        IReadOnlyList<FeModel.BoneChainJoint>? walk = null, HashSet<string>? relandedJoints = null)
    {
        // A rigid hinge takes the chain's rod network over, so a hinged chain that still carries rods was
        // authored with a soft link instead, unless the hinged link itself compiled to a quad.
        var softHinge = feModel.HasChainRods(chain) && !feModel.HasRigidHingeLink(chain);

        var version = ClothChainVersion(feModel, chain, hasOtherChains);

        var chainMass = feModel.RecoverChainMassDefault(chain);

        var joints = KVObject.Array();
        foreach (var joint in walk ?? chain.Joints)
        {
            var jointNode = MakeClothJoint(feModel, joint, chainExtrudes: chain.ExtrudeSides >= 1, softHinge, version,
                rollTies: relandedJoints?.Contains(joint.Name) != true, chain: chain, chainMass: chainMass);

            // A rigid hinge is the one shape whose sibling set the chain reconstruction cannot read a
            // value off, so it keeps the flat 1.0 it has always been given.
            var childSibling = joint.ChildSiblingSpring > 0f
                ? joint.ChildSiblingSpring
                : feModel.SpringsHingeChildren(chain, joint.Node) ? 1.0f : 0f;
            if (childSibling > 0f)
            {
                jointNode.Add("child_sibling_spring", childSibling);
            }

            joints.Add(jointNode);
        }

        var chainData = KVObject.Collection();
        chainData.Add("joints", joints);
        chainData.Add("attrs", MakeClothChainAttrs(chain.ExtrudeSides, chain.ExtrudeRadius, chain.ExtrudeTwist,
            chainMass));
        chainData.Add("selection", KVObject.Array());

        chainData.Add("version", version);

        var chainNode = MakeNode("ClothChain",
            ("name", chain.RootBone + chain.DeclarationSuffix),
            ("root_bone", chain.RootBone),
            ("chain", chainData));

        // A rigid ClothChainHinge is a child node of the chain, constraining one joint by name.
        var hinges = KVObject.Array();
        foreach (var joint in chain.Joints)
        {
            if (feModel.RigidHingeJoints.TryGetValue(joint.Node, out var hingeVector))
            {
                hinges.Add(MakeNode("ClothChainHinge",
                    ("constrained_bone", joint.Name),
                    ("hinge_vector", ToKVArray(hingeVector)),
                    ("soft_hinge_link", false),
                    ("limits_enabled", false)));
            }
        }

        if (hinges.Count > 0)
        {
            chainNode.Add("children", hinges);
        }

        return chainNode;
    }

    /// <summary>
    /// The plain second declaration of the joints <c>BuildBoneChains</c> marked restated, or null when
    /// the chain has none. Emitted right after the extruding chain, so the compiler re-registers those
    /// joint nodes with these values and adds the plain parent rod, as the source's own second
    /// declaration did. Every value is read from the joint node itself, which is where the second
    /// declaration left it.
    /// </summary>
    static KVObject? MakeClothChainRestatement(FeModel feModel, FeModel.BoneChain chain)
    {
        var restated = chain.Joints.FindAll(static joint => joint.Restated);
        if (restated.Count == 0)
        {
            return null;
        }

        var members = restated.Select(static joint => joint.Node).ToHashSet();
        var joints = KVObject.Array();
        string? rootBone = null;
        foreach (var joint in restated)
        {
            var kv = KVObject.Collection();
            kv.Add("joint_name", joint.Name);

            var parented = members.Contains(joint.ParentNode);
            if (parented && joint.ParentName is { } parentName)
            {
                kv.Add("joint_parent", parentName);
            }
            else
            {
                rootBone ??= joint.Name;
            }

            kv.Add("simulate", joint.Simulated);

            var integrator = feModel.GetIntegrator(joint.Node);
            kv.Add("goal_strength", feModel.GoalStrengthPaint(integrator.ForceAttraction));
            kv.Add("goal_damping", feModel.GoalDampingPaint(integrator.ForceAttraction, integrator.VertexAttraction));
            kv.Add("gravity_z", integrator.Gravity / ClothSourceBaseGravity);

            if (joint.Simulated)
            {
                kv.Add("collision_radius", feModel.GetCollisionRadius(joint.Node));
            }

            if (parented)
            {
                foreach (var rod in feModel.Rods)
                {
                    if ((rod.NodeA == joint.Node && rod.NodeB == joint.ParentNode)
                        || (rod.NodeA == joint.ParentNode && rod.NodeB == joint.Node))
                    {
                        if (rod.RelaxationFactor != 1f)
                        {
                            kv.Add("stretch_spring", rod.RelaxationFactor);
                        }

                        break;
                    }
                }
            }

            joints.Add(kv);
        }

        var chainData = KVObject.Collection();
        chainData.Add("joints", joints);
        chainData.Add("attrs", MakeClothChainAttrs());
        chainData.Add("selection", KVObject.Array());

        rootBone ??= restated[0].Name;
        return MakeNode("ClothChain",
            ("name", rootBone + "_restated"),
            ("root_bone", rootBone),
            ("chain", chainData));
    }

    /// <summary>
    /// The SECOND declaration of the sub-chain <c>BuildBoneChains</c> marked, or null where the chain has
    /// none. The source states such a run twice - once inside the parent chain at <c>simulate = false</c>
    /// and once in a chain of its own - and the later declaration wins the node, so this one carries the
    /// members' whole attribute set. It states neither <c>stiff_hinge</c> nor <c>child_sibling_spring</c>:
    /// each is written once per DECLARATION, so restating one doubles the bends or the sibling rods the
    /// first declaration already made.
    /// </summary>
    static List<KVObject> MakeClothChainSecondDeclarations(FeModel feModel, FeModel.BoneChain chain, int version)
    {
        var runs = new List<string>();
        foreach (var joint in chain.Joints)
        {
            if (joint.SecondDeclarationRoot is { } root && !runs.Contains(root))
            {
                runs.Add(root);
            }
        }

        var declarations = new List<KVObject>(runs.Count);
        foreach (var rootBone in runs)
        {
            var joints = KVObject.Array();
            foreach (var joint in chain.Joints)
            {
                if (string.Equals(joint.SecondDeclarationRoot, rootBone, StringComparison.OrdinalIgnoreCase))
                {
                    joints.Add(MakeClothJoint(feModel, joint, chainExtrudes: false, softHinge: false, version,
                        rollTies: true, chain, secondDeclaration: true));
                }
            }

            var chainData = KVObject.Collection();
            chainData.Add("joints", joints);
            chainData.Add("attrs", MakeClothChainAttrs());
            chainData.Add("selection", KVObject.Array());
            chainData.Add("version", version);

            declarations.Add(MakeNode("ClothChain",
                ("name", rootBone + "_second"),
                ("root_bone", rootBone),
                ("chain", chainData)));
        }

        return declarations;
    }

    internal static KVObject MakeClothJoint(FeModel feModel, FeModel.BoneChainJoint joint, bool chainExtrudes = false,
        bool softHinge = false, int chainVersion = 2, bool rollTies = true, FeModel.BoneChain? chain = null,
        bool secondDeclaration = false, float chainMass = 1f)
    {
        var kv = KVObject.Collection();
        kv.Add("joint_name", joint.Name);

        var secondRoot = joint.SecondDeclarationRoot;
        if (joint.ParentName is not null
            && !(secondDeclaration && string.Equals(joint.Name, secondRoot, StringComparison.OrdinalIgnoreCase)))
        {
            kv.Add("joint_parent", joint.ParentName);
        }

        // The compiler CUBES the joint goal_strength into flAnimationForceAttraction, the same way it
        // treats the painted cloth_goal_strength_v2 on a proxy mesh, so the emitted value is the cube root
        // of the recovered attraction.
        //
        // It is recovered regardless of joint.Simulated: a chain ROOT is routinely authored
        // `simulate = false` with a nonzero goal_strength, so gating on the flag would zero goal_strength
        // on every chain root.
        //
        // A joint the source declared twice keeps the second declaration's values on its own node;
        // the first declaration's values survive on the ring it extruded (MakeClothChainRestatement
        // emits the second declaration from the node). Where the two declarations each extruded a ring
        // of their own, BoneChainJoint.ValueNode names this declaration's.
        var valueNode = joint.ValueNode >= 0 ? joint.ValueNode
            : joint.Restated && joint.ProxyNode >= 0 ? joint.ProxyNode : joint.Node;
        var integrator = feModel.GetIntegrator(valueNode);
        var goalStrength = feModel.GoalStrengthPaint(integrator.ForceAttraction);

        var twistRelax = feModel.GetAuthoredTwistRelax(joint.Node, joint.ParentNode, joint.ProxyNode);

        // The compiler scales a twist entry by the ORIENT joint's own twist_relax only where that
        // joint simulates, and writes a flat 0.0 where it merely allows rotation. So a chain root
        // the original gives a non-zero entry of its own was authored as a SIMULATED joint, and it
        // is pinned into the static block by lock_translation rather than by simulate = false.
        var pinnedSimulatedRoot = (joint.IsRoot && !joint.Simulated && twistRelax > 0f) || joint.SpringsWithSiblings;

        // A joint two chains declare simulates only in the second declaration; the first states
        // `simulate = false`, which is what zeroes the twist entry that declaration writes.
        var firstOfTwo = secondRoot is not null && !secondDeclaration
            && !string.Equals(joint.Name, secondRoot, StringComparison.OrdinalIgnoreCase);
        kv.Add("simulate", !firstOfTwo && (joint.Simulated || pinnedSimulatedRoot));

        // Each declaration of a doubled run states the twist of its OWN rank: rank 0 is the first
        // declaration's and rank 1 the second's. Where the pair carries a single copy the second
        // declaration wrote none and states 0, which is the silent case.
        if (secondRoot is not null)
        {
            twistRelax = feModel.TwistRelaxDeclaredAt(joint.Node, joint.ParentNode,
                secondDeclaration ? 1 : 0) ?? 0f;
        }

        // A STATIC joint's own entries carry no relaxation at all, so its authored twist_relax survives
        // only as the twist link it made. The magnitude is gone with it: every value above zero compiles
        // the same pair of entries, so the largest one stands for the key being set. The evidence is the
        // joint's own entries and is read per joint, so a static joint reads the same way wherever it
        // sits: a chain we root one bone higher than the source did leaves such a joint interior, and
        // dropping its twist there costs it the goal lock the compiler writes for a node whose parent has
        // neither simulation nor rotation to offset from.
        if (twistRelax == 0f && !joint.Simulated && !secondDeclaration
            && (feModel.HasRelaxlessTwistLink(joint.Node) || feModel.OrientsRelaxlessTwist(joint.Node)))
        {
            twistRelax = ClothStaticRootTwistRelax;
        }

        // Only a static node carries a rotation lock.
        if (joint.Node < feModel.StaticNodeCount)
        {
            kv.Add("allow_rotation", feModel.AllowsRotation(joint.Node));
        }

        if (feModel.LocksTranslation(joint.Node, chainVersion, chain) || pinnedSimulatedRoot)
        {
            kv.Add("lock_translation", true);
        }

        kv.Add("goal_strength", goalStrength);
        kv.Add("goal_damping", feModel.GoalDampingPaint(integrator.ForceAttraction, integrator.VertexAttraction));

        // The same flPointDamping channel the proxy sheets carry as cloth_drag.
        var drag = Math.Clamp(integrator.PointDamping / ClothDragPointDampingScale, 0f, 1f);
        if (drag != 0f)
        {
            kv.Add("drag", drag);
        }

        var gravityNode = joint.ProxyNode >= 0 ? joint.ProxyNode : joint.Node;
        kv.Add("gravity_z", feModel.GetIntegrator(gravityNode).Gravity / ClothSourceBaseGravity);

        // A non-zero twist_relax, stiff_hinge or motion_bias makes the compiler build a Twist or
        // KelagerBend constraint network in place of the plain ropes a chain otherwise compiles to, so
        // each is recovered per joint, magnitude included, from the original's own m_Twists participation
        // (FeModel.GetAuthoredTwistRelax) rather than defaulted.
        kv.Add("twist_relax", twistRelax);

        // World collision membership and radius (m_WorldCollisionNodes / m_NodeCollisionRadii).
        kv.Add("world_collision", feModel.IsWorldCollisionNode(joint.Node));

        // A chain joint's node mask is exactly these four bits, with no all-set special case and no
        // absent-key escape, so the four attr defaults of MakeClothChainAttrs already spell out 15.
        // Only a joint whose mask says something else needs its own keys; a mask above the four bits
        // is not expressible from a chain at all.
        var collisionMask = feModel.GetNodeCollisionMask(joint.Node);
        if (collisionMask is >= 0 and < 0xF)
        {
            kv.Add("collision_layer_0", (collisionMask & 1) != 0);
            kv.Add("collision_layer_1", (collisionMask & 2) != 0);
            kv.Add("collision_layer_2", (collisionMask & 4) != 0);
            kv.Add("collision_layer_3", (collisionMask & 8) != 0);
        }

        var (worldFriction, groundFriction) = feModel.GetWorldFriction(joint.Node);
        kv.Add("world_friction", worldFriction);
        kv.Add("ground_friction", groundFriction);
        kv.Add("collision_radius", feModel.GetCollisionRadius(valueNode));

        // Stray radius (m_AnimStrayRadii): the max distance the node may stray from its animated position.
        // A joint whose own node is pinned records it on its ring alone, which is also the only place a
        // shared joint's second declaration keeps its own.
        var strayNode = joint.ValueNode >= 0
            ? joint.ValueNode
            : feModel.StrayRadiusNode(joint.Node, joint.Name);
        kv.Add("stray_radius", feModel.GetStrayRadius(strayNode));
        kv.Add("stray_radius_stretchiness", feModel.GetStrayStretchiness(strayNode));
        kv.Add("friction", feModel.GetNodeFriction(joint.Node));

        if (feModel.RecoverJointMass(joint.Node, chainMass) is { } massMultiplier)
        {
            kv.Add("mass", massMultiplier);
        }

        // The named vertex selections this joint belongs to, comma separated. Naming them here is what
        // puts the joint and the proxies extruded from it back into the selections cloth effects target.
        // A joint that does not simulate stays out of the selection itself while its proxies join it, so
        // when the joint's own node belongs to none the proxies it extruded carry the membership.
        if ((feModel.GetVertexMapNames(joint.Node)
            ?? (joint.ProxyNode >= 0 ? feModel.GetVertexMapNames(joint.ProxyNode) : null))
            is { } vertexMaps)
        {
            kv.Add("vertex_map", vertexMaps);
        }

        // The hinge constraint the ClothChainHinge node writes onto the joint it constrains. It both
        // orients that joint's proxy ring and adds the compiler's own static anchor node, so a joint that
        // shipped one loses a control node without it - and a joint that did not gains one.
        var hinge = feModel.GetChainHinge(joint.Name, joint.Node);

        // Per-joint extrude width. The chain-level extrude_sides (MakeClothChainAttrs) is one uniform
        // value, so it cannot reproduce a ribbon whose END-CAP joint fans wider than its body; overriding
        // it per joint recovers that fan. A chain that extrudes at all emits every joint's own width,
        // including an explicit 0 for a joint that carries no proxies, which would otherwise inherit the
        // chain-level default. A chain that does not extrude emits nothing.
        if (chainExtrudes)
        {
            kv.Add("extrude_sides", joint.ExtrudeSides);

            // Ring geometry varies along a chain, so the chain-level defaults only fit one joint. Emit each
            // joint's own measured ring instead.
            if (joint.ExtrudeSides > 0)
            {
                kv.Add("extrude_radius", joint.ExtrudeRadius);
                kv.Add("extrude_twist", ClothExtrudeTwistKey(joint.ExtrudeTwist) + (rollTies ? joint.ExtrudeTwistTieNudge : 0f));

                // 'x' is the compiler's own default and needs no explicit key.
                if (joint.ForwardAxis != 'x')
                {
                    kv.Add("extrude_forward_axis", joint.ForwardAxis.ToString());
                }
            }
        }

        // A tip that fans into two rows is a second ring this far along the joint's forward axis, not
        // one ring of twice the width - the wider ring puts every proxy somewhere else entirely. A
        // hinged joint that carries only the hinge's own two proxies has no second ring to recover:
        // that pair straddles the hinge axis, which reads as two rings a ring apart. Emitted outside the
        // extrude block: a joint whose only generated node is the "$cc<bone>_Ctr" centre has an
        // end_effector but no ring at all, so its chain never extrudes.
        if (joint.EndEffector != 0f && (hinge is null || feModel.ProxyCountOf(joint.Node) > 2))
        {
            kv.Add("end_effector", joint.EndEffector);
        }

        // Each of the three sliders lands verbatim on the flRelaxationFactor of the rod it generates, so
        // they carry the recovered per-joint stiffness rather than a 1.0/0.0 on-off (see
        // FeModel.BuildBoneChains). Zero still means "no rod at all" on the bend and torsion spans.
        // 1.0 is stretch_spring's own attr default and needs no explicit key.
        if (joint.StretchStiffness != 1.0f)
        {
            kv.Add("stretch_spring", joint.StretchStiffness);
        }

        if (joint.AnimatedLength)
        {
            kv.Add("animated_length", true);
        }

        kv.Add("bend_spring", joint.BendStiffness);
        kv.Add("torsion_spring", joint.TorsionStiffness);
        kv.Add("extra_iterations", joint.ExtraIterations);
        kv.Add("suspender", joint.Suspender);

        // How far the rods this joint's spans generate may contract: the compiler copies the value
        // straight into each one's flMinDist/flMaxDist (see FeModel.BuildBoneChains). 1.0 is the attr
        // default and needs no explicit key, which is also what a joint whose spans disagree keeps.
        if (joint.Antishrink != 1.0f)
        {
            kv.Add("antishrink", joint.Antishrink);
        }

        // A stiff hinge compiles to a three-node bend rather than a rod, so it is recovered from the bend
        // centred on this joint (see FeModel.GetStiffHinge). The record is written once per DECLARATION,
        // so a declaration states the bend of its own rank and states nothing where the original carries
        // no bend for that rank.
        if (feModel.GetStiffHinge(joint.Node) is { } stiffHinge)
        {
            if (feModel.GetStiffHinge(joint.Node, secondDeclaration ? 1 : 0) is { } declared)
            {
                kv.Add("stiff_hinge", declared.Stiffness);
                kv.Add("stiff_hinge_angle", declared.Angle);
            }

            var stiffBias = stiffHinge.MotionBias != 0f ? stiffHinge.MotionBias : feModel.GetMotionBias(joint) ?? 0f;
            if (stiffBias != 0f)
            {
                kv.Add("motion_bias", stiffBias);
            }
        }
        else if (feModel.GetMotionBias(joint) is { } motionBias)
        {
            // Without a stiff hinge the bias leaves its only trace in the weights of the joint's own
            // span rods, which is where FeModel.GetMotionBias reads it back from.
            kv.Add("motion_bias", motionBias);
        }

        if (hinge is { } chainHinge)
        {
            kv.Add("hinge_constraint_vector_worldspace", ToKVArray(chainHinge.Vector));
            kv.Add("hinge_constraint_soft", softHinge);
            kv.Add("hinge_constraint_limit_cw", chainHinge.LimitCw);
            kv.Add("hinge_constraint_limit_ccw", chainHinge.LimitCcw);
        }

        return kv;
    }

    // The cloth-chain joint datatable schema: per-column UI metadata and defaults, matching the editable
    // ModelDoc source the tools produce. The compiler takes the "default" value of any joint field the
    // joint rows above do not write.
    internal static KVObject MakeClothChainAttrs(int extrudeSides = 0, float extrudeRadius = 0f,
        float extrudeTwist = 0f, float mass = 1f)
    {
        var attrs = KVObject.Collection();

        KVObject AddAttr(string key, string display, bool show, int uiOrder)
        {
            var attr = KVObject.Collection();
            attr.Add("display", display);
            attr.Add("show", show);
            attr.Add("ui_order", uiOrder);
            attrs.Add(key, attr);
            return attr;
        }

        KVObject FloatAttr(string key, string display, bool show, int uiOrder, float def, float? min = null, float? max = null)
        {
            var attr = AddAttr(key, display, show, uiOrder);
            attr.Add("default", def);
            if (min.HasValue) { attr.Add("min", min.Value); }
            if (max.HasValue) { attr.Add("max", max.Value); }
            return attr;
        }

        KVObject IntAttr(string key, string display, bool show, int uiOrder, int def, int? min = null, int? max = null)
        {
            var attr = AddAttr(key, display, show, uiOrder);
            attr.Add("default", def);
            if (min.HasValue) { attr.Add("min", min.Value); }
            if (max.HasValue) { attr.Add("max", max.Value); }
            return attr;
        }

        KVObject BoolAttr(string key, string display, bool show, int uiOrder, bool def)
        {
            var attr = AddAttr(key, display, show, uiOrder);
            attr.Add("default", def);
            return attr;
        }

        KVObject StringAttr(string key, string display, bool show, int uiOrder)
        {
            var attr = AddAttr(key, display, show, uiOrder);
            attr.Add("default", "");
            return attr;
        }

        // The complete version-2 attr set. An incomplete v1-era key list makes the v2 joint grid ignore
        // the table and fall back to default columns. Attrs with values recovered from the compiled
        // FeModel are shown; the rest keep stock visibility.
        StringAttr("joint_name", "Joint Name", true, 1).Add("lock", true);
        StringAttr("joint_parent", "Parent Joint", false, 2);
        BoolAttr("simulate", "Simulate", true, 3, true);
        BoolAttr("allow_rotation", "Allow Rotation", false, 4, true);
        // The display names match the ClothChainAttrEditor schema and are ModelDoc UI labels only.
        FloatAttr("stretch_spring", "Stretch Stiffness", false, 5, 1.0f, 0.0f, 1.0f);
        FloatAttr("child_sibling_spring", "Spring Between Children", false, 6, 0.0f, 0.0f, 1.0f);
        FloatAttr("bend_spring", "Bend Stiffness", false, 7, 1.0f, 0.0f, 1.0f);
        FloatAttr("torsion_spring", "Torsion Stiffness", false, 8, 0.0f, 0.0f, 1.0f);
        FloatAttr("explicit_length", "Explicit Length", false, 9, 0.0f, 0.0f);
        BoolAttr("world_collision", "World Ground Collision", true, 10, false);
        BoolAttr("animated_length", "Animated Length", false, 11, false);
        FloatAttr("goal_strength", "Goal Strength", true, 12, 0.0f, 0.0f, 1.0f);
        FloatAttr("goal_damping", "Goal Damping", true, 13, 0.0f, 0.0f, 1.0f);
        FloatAttr("drag", "Extra Drag", false, 14, 0.0f, 0.0f, 1.0f);
        // The compiler takes this default for every joint row that omits the key, and 02_IMPORT 3.3 zeroes
        // flMassMultiplier on a node that does not simulate, so it is read on exactly the chain's
        // simulating joints. It states the multiplier most of them carry; the rest state their own.
        FloatAttr("mass", "Mass", false, 15, mass, 0.0f);
        FloatAttr("gravity_z", "Gravity", true, 16, 1.0f);
        FloatAttr("collision_radius", "Collision Radius", true, 17, 0.0f, 0.0f);
        BoolAttr("lock_translation", "Lock Translation", false, 18, false);
        FloatAttr("suspender", "Suspender Spring", false, 19, 0.0f);
        FloatAttr("antishrink", "Antishrink Strength", false, 20, 1.0f, 0.0f, 1.0f);
        FloatAttr("stray_radius", "Stray Radius", true, 21, 0.0f, 0.0f);
        FloatAttr("stray_radius_stretchiness", "Stray Radius Stretchiness", false, 22, 0.0f, 0.0f);
        FloatAttr("friction", "Friction", false, 23, 0.0f, 0.0f, 1.0f);
        StringAttr("vertex_map", "Vertex Map", false, 24).Add("verify", "vertex_map");
        FloatAttr("end_effector", "End Effector", false, 25, 0.0f).Add("lock_default_value", true);
        FloatAttr("stiff_hinge", "Stiff Hinge", true, 26, 0.0f, 0.0f, 1.0f).Add("lock_root2", true);
        FloatAttr("stiff_hinge_angle", "Stiff Hinge Angle", true, 27, 0.0f, 0.0f, 180.0f).Add("lock_root2", true);
        FloatAttr("motion_bias", "Motion Bias", true, 28, 0.0f, -1.0f, 1.0f).Add("lock_root", true);
        IntAttr("extra_iterations", "Extra Iterations", true, 29, 0, 0, 1000);
        FloatAttr("twist_relax", "Twist Relax", true, 30, 0.0f, 0.0f, 1.0f);
        // Recovered per chain from the compiled $cc proxy width (see FeModel.BuildBoneChains): a 2-wide
        // strip or N-sided tube regenerates its proxies only when the ClothChain re-declares the extrude.
        // extrudeSides 0 keeps the stock default, a plain rope.
        IntAttr("extrude_sides", "Extrude Sides", false, 31, extrudeSides, 0, 4);
        FloatAttr("extrude_radius", "Extrude Radius", false, 32, extrudeSides >= 1 ? extrudeRadius : 5.0f, 0.0f);
        FloatAttr("extrude_twist", "Extrude Twist", false, 33, ClothExtrudeTwistAttrDefault);
        StringAttr("extrude_forward_axis", "Extrude Forward Axis", false, 34).Add("verify", "extrude_forward_axis");
        FloatAttr("world_friction", "Ground Softness (\"world friction\" in Source1)", false, 35, 0.0f, 0.0f, 1.0f);
        FloatAttr("ground_friction", "Ground Friction", false, 36, 0.0f, 0.0f, 1.0f);
        StringAttr("stray_box", "Stray Box", false, 37).Add("verify", "stray_box");
        BoolAttr("collision_layer_0", "Collision Layer 0", false, 38, true);
        BoolAttr("collision_layer_1", "Collision Layer 1", false, 39, true);
        BoolAttr("collision_layer_2", "Collision Layer 2", false, 40, true);
        BoolAttr("collision_layer_3", "Collision Layer 3", false, 41, true);

        return attrs;
    }

    bool EmitChainClothPhase(FeModel feModel, List<FeModel.BoneChain> boneChains, KVObject rootChildren)
    {
        // Phase 1 fallback (no recoverable sheet): bone-chain cloth, plus a GENERATED sheet grid
        // over each group of neighbouring chains (skirts/capes). The grid mirrors hand-authored
        // item proxies: with back_solve_joints=false the chains keep simulating the bones while
        // the sheet simulates the surface between them and drives the render mesh directly.
        var (softbody, softbodyChildren) = MakeListNode("Softbody");
        AddSoftbodyAttributes(softbody, feModel);
        softbodyChildren.Add(MakeClothParams(feModel,
            generatesBendRods: feModel.HasChainStiffnessRods(boneChains),
            generatesBendOnlyRods: feModel.HasChainBendOnlyRods(boneChains),
            addCurvature: feModel.ChainRingCurvature,
            explicitMasses: feModel.HasExplicitMasses));
        var (clothFolder, clothFolderChildren) = MakeListNode("Folder");
        clothFolder.Add("name", "cloth");
        softbodyChildren.Add(clothFolder);

        var strip = feModel.ImportedStripNodes;
        if (strip.Count > 0)
        {
            clothFolderChildren.Add(MakeImportedCloth(feModel, strip));
        }

        var hasOtherChains = boneChains.Count > 1;

        // The compiled node order is (block, constraint rank, creation index), so inside a band it IS the
        // order the control nodes were created in. A chain creates each joint immediately followed by its
        // own ring nodes, so a joint the band order separates from its rings was created before the chain
        // ran - by an earlier declaration of the same bone name, which the chain then reuses.
        var declarationPlan = TryPlanClothChainDeclarations(feModel, boneChains,
            ClothControlParentTest(feModel));
        var declaredChains = declarationPlan?.Chains ?? boneChains;
        foreach (var (name, node) in declarationPlan?.PreDeclared ?? [])
        {
            clothFolderChildren.Add(MakeClothChainJointDeclaration(feModel, name, node));
        }

        foreach (var boneChain in declaredChains)
        {
            var walk = declarationPlan is not null
                && declarationPlan.Walk.TryGetValue(boneChain, out var found)
                ? found
                : null;
            clothFolderChildren.Add(MakeClothChainNode(feModel, boneChain, hasOtherChains, walk, ClothChainRelandedJoints));
            if (MakeClothChainRestatement(feModel, boneChain) is { } restated)
            {
                clothFolderChildren.Add(restated);
            }

            foreach (var second in MakeClothChainSecondDeclarations(feModel, boneChain,
                ClothChainVersion(feModel, boneChain, hasOtherChains)))
            {
                clothFolderChildren.Add(second);
            }
        }

        foreach (var clothGrid in ClothChainGridsToExtract)
        {
            // The grid ships DISABLED: the chains alone reproduce the original physics, and with
            // drive_meshes the sheet would fight the chain-driven skinning of the same region.
            // It is a ready-made starting sheet the author can enable/retarget in ModelDoc
            // (like hand-authored cape proxies that drive otherwise boneless render regions).
            var gridNode = MakeClothProxyMeshFile(clothGrid.Name, clothGrid.FileName, backSolveJoints: false, driveMeshes: true);
            gridNode.Add("disabled", true);
            clothFolderChildren.Add(gridNode);
        }

        AddClothFaces(clothFolderChildren, feModel);
        var sourceSprings = AddClothSourceSprings(softbodyChildren, feModel, boneChains);
        sourceSprings.UnionWith(AddClothChainSurplusRods(softbodyChildren, feModel, boneChains));

        // A sibling hub is declared for its spring alone and anchors no chain of its own, so it keeps the
        // bare ClothNode every static control node the chains do not claim is declared as - which is what
        // the cloth nodes parented to it resolve through.
        var chainCoveredNodes = boneChains.SelectMany(static chain => chain.Joints)
            .Where(joint => !feModel.SiblingSpringHubs.Contains(joint.Name))
            .Select(static joint => joint.Node)
            .ToHashSet();
        var clothBones = ClothBoneNames(feModel);
        clothBones.UnionWith(boneChains.SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Name));
        chainCoveredNodes.UnionWith(strip);
        clothBones.UnionWith(ImportedStripBoneNames(feModel, strip));
        chainCoveredNodes.UnionWith(AddClothSelfCollisionClusters(softbodyChildren, feModel, clothBones));
        // A static control node no chain, shape or jiggle bone claims is recreated by nothing else in
        // this phase, so it is declared as a bare ClothNode wherever the compiled skeleton records the
        // bone as a cloth control node - the same evidence the proxy-sheet phase reads.
        var clothControlBones = model?.Skeleton.Bones
            .Where(static b => b.IsClothControlNode)
            .Select(static b => b.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chainSurface = feModel.Quads.Length > 0 || feModel.Tris.Length > 0;
        AddFreeClothNodesAndSprings(clothFolderChildren, softbodyChildren, feModel, chainCoveredNodes,
            name => chainSurface || (clothControlBones?.Contains(name) ?? false),
            clothBones, ClothVertexMapFolders(feModel, clothFolderChildren), hasOtherChains: true,
            ClothControlAncestorTest(feModel), sourceSprings,
            chainJoints: [.. boneChains.SelectMany(static chain => chain.Joints).Select(static joint => joint.Node)]);
        AddClothStiffHinges(softbodyChildren, feModel);
        AddClothRigidCloudClusterLocks(softbodyChildren, feModel, declaredChains);
        AddClothChainVolumetricMaps(softbodyChildren, feModel, boneChains);

        AddClothFollowBones(softbodyChildren, feModel, clothBones);
        var shapeNames = AddClothCollisionShapes(softbodyChildren, feModel);
        AddClothAntiTunnelGroup(softbodyChildren, feModel, shapeNames,
            [.. declaredChains.Select(static chain => chain.RootBone + chain.DeclarationSuffix)]);
        AddClothEffects(softbodyChildren, feModel, AvailableVertexMaps(feModel, boneChains));
        AddShapeParentDefaultClothNodes(softbodyChildren, feModel);
        rootChildren.Add(softbody);
        AddClothAntiTunnelProbes(rootChildren, feModel, proxyNodeNames: null);
        return true;
    }
}
