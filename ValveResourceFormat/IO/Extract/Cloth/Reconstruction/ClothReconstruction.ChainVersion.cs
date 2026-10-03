using System.Linq;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothReconstruction
{
    /// <summary>
    /// Whether the goal lock on <paramref name="chain"/> belongs to an algorithm-0 <c>ClothRigidCloudCluster</c> rather
    /// than to the chain's own format: a locked joint with a chain child beside preset-graded node bases.
    /// </summary>
    internal static bool IsRigidCloudClusterLock(ClothReconstruction cloth, BoneChain chain)
        => LockedJointsWithChildren(cloth, chain).Any() && cloth.ChainBasesAreBulkGraded(chain) == false;

    /// <summary>The goal-locked joints of <paramref name="chain"/> that have chain children, with those children.</summary>
    internal static IEnumerable<(BoneChainJoint Joint, List<BoneChainJoint> Children)> LockedJointsWithChildren(
        ClothReconstruction cloth, BoneChain chain)
        => chain.Joints
            .Where(joint => cloth.Fe.IsLockedToGoal(joint.Node))
            .Select(joint => (joint, chain.Joints.Where(child => child.ParentNode == joint.Node).ToList()))
            .Where(static entry => entry.Item2.Count > 0);

    /// <summary>The compiled evidence <see cref="ClothChainVersion(ChainVersionEvidence)"/> reads a chain's version off.</summary>
    internal readonly record struct ChainVersionEvidence(bool? RootAllowsRotation, bool RootHasBase, bool LockedJoint,
        bool RigidCloudClusterLock, bool LocksJoints, bool? BasesBulkGraded, bool HintsTwistWritten, bool HasUnstagedThinJoint,
        bool? ReverseOffsetsPreset = null, bool HasUnbasedLeaf = false, bool SiblingHubLock = false, bool ExtrudesNothing = false,
        bool FitsPresetJoint = false, bool LocksOnlyParentLocked = false, bool LocksOnlyToGoal = false);

    /// <summary>
    /// The <c>ClothChain</c> version a chain was authored at, read off the evidence its compiled data carries. Version 2
    /// keeps a rotation-locked root's node base and grades a preset basis on every joint with a child, version 1
    /// goal-locks every static rotation-free joint of an extruding chain, and version 0 stages no fit influences.
    /// </summary>
    internal static int ClothChainVersion(ChainVersionEvidence evidence)
    {
        var lockedInOriginal = evidence.LockedJoint && !evidence.RigidCloudClusterLock && !evidence.SiblingHubLock;
        var guardOpen = lockedInOriginal || !evidence.LocksJoints || evidence.LocksOnlyParentLocked
            || (evidence.LocksOnlyToGoal && evidence.BasesBulkGraded == true);
        var rootRotationLocked = evidence.RootAllowsRotation == false;

        var version = rootRotationLocked && guardOpen
            ? (evidence.RootHasBase || evidence.ExtrudesNothing ? 2 : 1)
            : (lockedInOriginal ? 1 : 2);

        var rootKeepsPreset = rootRotationLocked && evidence.RootHasBase;
        if (version == 2 && !rootKeepsPreset && guardOpen
            && (evidence.BasesBulkGraded == true || (evidence.BasesBulkGraded is null && evidence.ReverseOffsetsPreset == false)
                || evidence.FitsPresetJoint))
        {
            version = 1;
        }

        if (version != 0 && !rootKeepsPreset && guardOpen && evidence.HintsTwistWritten && evidence.BasesBulkGraded != false)
        {
            version = 0;
        }

        if (version != 0 && !rootKeepsPreset && guardOpen && evidence.HasUnbasedLeaf && evidence.BasesBulkGraded != false)
        {
            version = 0;
        }

        if (version == 1 && evidence.HasUnstagedThinJoint)
        {
            version = 0;
        }

        return version;
    }

    /// <summary>The <c>ClothChain</c> version <paramref name="chain"/> was authored at.</summary>
    internal static int ClothChainVersion(ClothReconstruction cloth, BoneChain chain)
        => ClothChainVersion(ChainVersionEvidenceOf(cloth, chain));

    /// <summary>The version evidence <paramref name="chain"/>'s compiled data carries.</summary>
    private static ChainVersionEvidence ChainVersionEvidenceOf(ClothReconstruction cloth, BoneChain chain)
    {
        var root = chain.Joints.Count > 0 ? chain.Joints[0] : null;
        var locksJoints = ChainLocksJoints(cloth, chain);
        return new ChainVersionEvidence(
            RootAllowsRotation: root is null ? null : cloth.Fe.AllowsRotation(root.Node),
            RootHasBase: root is not null && cloth.Fe.NodeBases.ContainsKey(root.Node),
            LockedJoint: chain.Joints.Exists(joint => cloth.Fe.IsLockedToGoal(joint.Node)),
            RigidCloudClusterLock: IsRigidCloudClusterLock(cloth, chain),
            LocksJoints: locksJoints,
            BasesBulkGraded: cloth.ChainBasesAreBulkGraded(chain),
            HintsTwistWritten: cloth.ChainHintsAreTwistWritten(chain),
            HasUnstagedThinJoint: cloth.ChainHasUnstagedThinJoint(chain),
            ReverseOffsetsPreset: cloth.ChainReverseOffsetsArePreset(chain),
            HasUnbasedLeaf: cloth.ChainHasUnbasedLeaf(chain),
            SiblingHubLock: cloth.SiblingSpringHubs.Contains(chain.RootBone)
                && chain.Joints.TrueForAll(joint => !cloth.Fe.IsLockedToGoal(joint.Node)
                    || joint.SpringsWithSiblings),
            ExtrudesNothing: chain.ExtrudeSides < 1
                && !chain.Joints.Exists(static joint => joint.RingNodes.Count > 0),
            FitsPresetJoint: cloth.ChainFitsAPresetJoint(chain),
            LocksOnlyParentLocked: ChainLocksOnlyParentLockedJoints(cloth, chain),
            LocksOnlyToGoal: locksJoints && !ChainLocksJointsToParent(cloth, chain));
    }

    /// <summary>
    /// Whether format 1 would lock a joint of <paramref name="chain"/> to its parent rather than its goal: a joint
    /// <see cref="ChainLocksJoints"/> counts whose compiled parent is simulated or rotates freely.
    /// </summary>
    private static bool ChainLocksJointsToParent(ClothReconstruction cloth, BoneChain chain)
        => chain.ExtrudeSides >= 1
            && chain.Joints.Exists(joint => !joint.Simulated && cloth.Fe.AllowsRotation(joint.Node)
                && StagesFitTable(chain, joint) && LocksToParent(cloth, joint.Node));

    private static bool LocksToParent(ClothReconstruction cloth, int node)
    {
        var parent = node < cloth.SkelParents.Length ? cloth.SkelParents[node] : -1;
        return parent >= 0 && (parent >= cloth.Fe.StaticNodeCount || cloth.Fe.AllowsRotation(parent));
    }

    /// <summary>
    /// Whether every joint <see cref="ChainLocksJoints"/> counts on <paramref name="chain"/> is locked to its parent in the
    /// original.
    /// </summary>
    private static bool ChainLocksOnlyParentLockedJoints(ClothReconstruction cloth, BoneChain chain)
        => chain.ExtrudeSides >= 1
            && chain.Joints.TrueForAll(joint => joint.Simulated || !cloth.Fe.AllowsRotation(joint.Node)
                || !StagesFitTable(chain, joint)
                || cloth.Fe.IsLockedToParent(joint.Node));

    /// <summary>
    /// Whether format 1 would goal-lock a joint of <paramref name="chain"/>: a non-simulated, rotation-free joint that has
    /// a ring of its own or a child with one.
    /// </summary>
    internal static bool ChainLocksJoints(ClothReconstruction cloth, BoneChain chain)
        => chain.ExtrudeSides >= 1
            && chain.Joints.Exists(joint => !joint.Simulated && cloth.Fe.AllowsRotation(joint.Node)
                && StagesFitTable(chain, joint));

    /// <summary>Whether a joint stages fit influences: it has a ring of its own or a chain child with one.</summary>
    private static bool StagesFitTable(BoneChain chain, BoneChainJoint joint)
        => joint.RingNodes.Count > 0 || chain.Joints.Exists(child => child.ParentNode == joint.Node && child.RingNodes.Count > 0);
}
