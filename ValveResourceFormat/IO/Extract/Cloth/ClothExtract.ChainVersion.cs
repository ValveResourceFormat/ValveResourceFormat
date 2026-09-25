using System.Linq;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
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
    private static bool ChainLocksJointsToParent(FeModel feModel, FeModel.BoneChain chain)
        => chain.ExtrudeSides >= 1
            && chain.Joints.Exists(joint => !joint.Simulated && feModel.AllowsRotation(joint.Node)
                && (joint.RingNodes.Count > 0 || chain.Joints.Exists(child => child.ParentNode == joint.Node && child.RingNodes.Count > 0))
                && LocksToParent(feModel, joint.Node));

    private static bool LocksToParent(FeModel feModel, int node)
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
}
