using System.Linq;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>
    /// Whether the goal lock on <paramref name="chain"/> belongs to an algorithm-0 <c>ClothRigidCloudCluster</c> rather
    /// than to the chain's own format: a locked joint with a chain child beside preset-graded node bases.
    /// </summary>
    internal static bool IsRigidCloudClusterLock(FeModel feModel, FeModel.BoneChain chain)
        => LockedJointsWithChildren(feModel, chain).Any() && feModel.ChainBasesAreBulkGraded(chain) == false;

    internal static IEnumerable<(FeModel.BoneChainJoint Joint, List<FeModel.BoneChainJoint> Children)> LockedJointsWithChildren(
        FeModel feModel, FeModel.BoneChain chain)
        => chain.Joints
            .Where(joint => feModel.IsLockedToGoal(joint.Node))
            .Select(joint => (joint, chain.Joints.Where(child => child.ParentNode == joint.Node).ToList()))
            .Where(static entry => entry.Item2.Count > 0);

    /// <summary>The <c>ClothChain</c> version a chain was authored at, read off the evidence its compiled data carries.</summary>
    internal static int ClothChainVersion(int jointCount, bool hasOtherChains, bool? rootAllowsRotation, bool rootHasBase,
        bool lockedJoint, bool rigidCloudClusterLock, bool locksJoints, bool? basesBulkGraded, bool hintsTwistWritten,
        bool hasUnstagedThinJoint, bool? reverseOffsetsPreset = null, bool hasUnbasedLeaf = false,
        bool siblingHubLock = false, bool extrudesNothing = false, bool fitsPresetJoint = false, bool locksOnlyParentLocked = false,
        bool locksOnlyToGoal = false)
    {
        // Format 1 goal-locks every non-simulated, rotation-free joint of an extruding chain and format 2 does not, so an
        // original that locks none of them rules format 1 out unless the lock could only land on the goal.
        var lockedInOriginal = lockedJoint && !rigidCloudClusterLock && !siblingHubLock;
        var guardOpen = lockedInOriginal || !locksJoints || locksOnlyParentLocked || (locksOnlyToGoal && basesBulkGraded == true);
        var rootRotationLocked = rootAllowsRotation == false;

        // A rotation-locked root keeps its m_NodeBases entry only at format 2, but a missing entry is evidence of format 1
        // only on a chain that extrudes.
        var version = rootRotationLocked && guardOpen
            ? (rootHasBase || extrudesNothing ? 2 : 1)
            : (lockedInOriginal ? 1 : 2);

        // Format 2 grades a preset basis on every joint with a child, so bulk-graded bases, reverse offsets that do not
        // name the preset Y1 nodes, or a fit matrix on a preset joint mean an earlier format.
        var rootKeepsPreset = rootRotationLocked && rootHasBase;
        if (version == 2 && !rootKeepsPreset && guardOpen
            && (basesBulkGraded == true || (basesBulkGraded is null && reverseOffsetsPreset == false) || fitsPresetJoint))
        {
            version = 1;
        }

        // A basis hint the twist or rope source wrote and nothing graded was compiled without fit influences: version 0.
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

    /// <summary>The <c>ClothChain</c> version <paramref name="chain"/> was authored at.</summary>
    internal static int ClothChainVersion(FeModel feModel, FeModel.BoneChain chain, bool hasOtherChains)
    {
        var root = chain.Joints.Count > 0 ? chain.Joints[0] : null;
        var locksJoints = ChainLocksJoints(feModel, chain);
        return ClothChainVersion(chain.Joints.Count, hasOtherChains,
            rootAllowsRotation: root is null ? null : feModel.AllowsRotation(root.Node),
            rootHasBase: root is not null && feModel.NodeBases.ContainsKey(root.Node),
            lockedJoint: chain.Joints.Exists(joint => feModel.IsLockedToGoal(joint.Node)),
            rigidCloudClusterLock: IsRigidCloudClusterLock(feModel, chain),
            locksJoints: locksJoints,
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
            locksOnlyToGoal: locksJoints && !ChainLocksJointsToParent(feModel, chain));
    }

    /// <summary>
    /// Whether format 1 would lock a joint of <paramref name="chain"/> to its parent rather than its goal: a joint
    /// <see cref="ChainLocksJoints"/> counts whose compiled parent is simulated or rotates freely.
    /// </summary>
    private static bool ChainLocksJointsToParent(FeModel feModel, FeModel.BoneChain chain)
        => chain.ExtrudeSides >= 1
            && chain.Joints.Exists(joint => !joint.Simulated && feModel.AllowsRotation(joint.Node)
                && StagesFitTable(chain, joint) && LocksToParent(feModel, joint.Node));

    private static bool LocksToParent(FeModel feModel, int node)
    {
        var parent = node < feModel.SkelParents.Length ? feModel.SkelParents[node] : -1;
        return parent >= 0 && (parent >= feModel.StaticNodeCount || feModel.AllowsRotation(parent));
    }

    /// <summary>
    /// Whether every joint <see cref="ChainLocksJoints"/> counts on <paramref name="chain"/> is locked to its parent in the
    /// original.
    /// </summary>
    internal static bool ChainLocksOnlyParentLockedJoints(FeModel feModel, FeModel.BoneChain chain)
        => chain.ExtrudeSides >= 1
            && chain.Joints.TrueForAll(joint => joint.Simulated || !feModel.AllowsRotation(joint.Node)
                || !StagesFitTable(chain, joint)
                || feModel.IsLockedToParent(joint.Node));

    /// <summary>
    /// Whether format 1 would goal-lock a joint of <paramref name="chain"/>: a non-simulated, rotation-free joint that has
    /// a ring of its own or a child with one.
    /// </summary>
    internal static bool ChainLocksJoints(FeModel feModel, FeModel.BoneChain chain)
        => chain.ExtrudeSides >= 1
            && chain.Joints.Exists(joint => !joint.Simulated && feModel.AllowsRotation(joint.Node)
                && StagesFitTable(chain, joint));

    // A joint stages fit influences where it has a ring of its own or a chain child with one.
    private static bool StagesFitTable(FeModel.BoneChain chain, FeModel.BoneChainJoint joint)
        => joint.RingNodes.Count > 0 || chain.Joints.Exists(child => child.ParentNode == joint.Node && child.RingNodes.Count > 0);
}
