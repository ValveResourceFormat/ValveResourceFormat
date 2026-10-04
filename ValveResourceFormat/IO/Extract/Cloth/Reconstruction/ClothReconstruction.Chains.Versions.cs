using System.Linq;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody.FeModel;

namespace ValveResourceFormat.IO
{
    internal sealed partial class ClothReconstruction
    {
        /// <summary>The numbered and <c>Ctr</c> ring nodes of each <c>$cc</c> owner name, in node order.</summary>
        private Dictionary<string, List<int>>? fitRingNodes;
        private Dictionary<int, HashSet<int>>? reverseOffsetTargets;
        private HashSet<int>? reverseOffsetBones;

        /// <summary>
        /// Gets whether <paramref name="node"/> was authored with <c>lock_translation</c>: its parent or goal lock is one
        /// that the fit-influence pass could not have written for it.
        /// </summary>
        internal bool LocksTranslation(int node, int chainVersion = 2, BoneChain? chain = null)
            => (Fe.IsLockedToParent(node) && !((chainVersion < 2 || !ChainPresetsJoint(node, chain))
                    && ReachesParentLockUnkeyed(node) && ChainStagesFitGroup(node, chainVersion, chain)))
                || (Fe.IsLockedToGoal(node) && !Fe.IsStatic(node));

        /// <summary>Gets whether <paramref name="node"/> is a hinged joint or a rigid hinge-fan joint.</summary>
        private bool IsAnyHingedJoint(int node) => IsHingedJoint(node) || RigidHingeJoints.ContainsKey(node);

        private bool ChainPresetsJoint(int node, BoneChain? chain)
        {
            var joint = chain?.Joints.Find(candidate => candidate.Node == node);
            if (chain is null || joint is null)
            {
                return true;
            }

            var child = chain.Joints.Find(candidate => candidate.ParentNode == node);
            return child is not null && ChainNodeBaseCandidates(joint, child) is not null;
        }

        private bool ChainStagesFitGroup(int node, int chainVersion, BoneChain? chain)
        {
            var joint = chain?.Joints.Find(candidate => candidate.Node == node);
            if (chain is null || joint is null || ProxyFitMatrixNodes.Contains(node))
            {
                return true;
            }

            if (joint.Simulated)
            {
                return false;
            }

            var table = FitListOf(node);
            var stagesOwnList = table.Count > 1 || !table.Contains(node);
            foreach (var child in chain.Joints)
            {
                if (child.ParentNode == node)
                {
                    table.UnionWith(FitListOf(child.Node));
                }
            }

            var hasParent = joint.ParentNode >= 0 || SkelParentOf(node) >= 0;
            return (stagesOwnList && table.Count >= 3) || (chainVersion >= 1 && table.Count is > 0 and < 3 && hasParent);
        }

        /// <summary>
        /// The nodes a chain joint stages fit influences from: its numbered ring nodes and its <c>end_effector</c> centre node, and the
        /// joint itself when its ring is narrower than two nodes. Rings are matched by name, so a compile without <c>m_SkelParents</c>
        /// reads them too.
        /// </summary>
        private HashSet<int> FitListOf(int jointNode)
        {
            if (fitRingNodes is null)
            {
                fitRingNodes = new Dictionary<string, List<int>>(StringComparer.Ordinal);
                for (var node = 0; node < Fe.CtrlNames.Length; node++)
                {
                    var name = Fe.CtrlNames[node];
                    var split = name.LastIndexOf('_');
                    if (split < RingNodePrefix.Length || !name.StartsWith(RingNodePrefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var suffix = name.AsSpan(split + 1);
                    if (suffix.SequenceEqual("Ctr") || (suffix.Length > 0 && int.TryParse(suffix, out _)))
                    {
                        GetOrAdd(fitRingNodes, name[RingNodePrefix.Length..split]).Add(node);
                    }
                }
            }

            var list = new HashSet<int>();
            var wide = false;
            foreach (var node in fitRingNodes.GetValueOrDefault(Fe.CtrlNames[jointNode]) ?? [])
            {
                list.Add(node);
                wide |= Fe.CtrlNames[node].EndsWith("_1", StringComparison.Ordinal);
            }

            if (!wide)
            {
                list.Add(jointNode);
            }

            return list;
        }

        private bool ReachesParentLockUnkeyed(int node)
        {
            if (!Fe.IsStatic(node) || !Fe.AllowsRotation(node) || (!Fe.NodeBases.ContainsKey(node) && !Fe.FitMatrixNodes.Contains(node)))
            {
                return false;
            }

            return Array.Exists(Fe.LockToParent, link => link.CtrlChild == node
                && (!Fe.IsStatic(link.CtrlParent) || Fe.AllowsRotation(link.CtrlParent))
                && !FitsOverInfluencesOf(link.CtrlParent, node));
        }

        private bool FitsOverInfluencesOf(int parent, int child)
        {
            if (!Fe.FitMatrixTargets.TryGetValue(parent, out var targets))
            {
                return false;
            }

            var influences = Fe.FitMatrixTargets.TryGetValue(child, out var own) ? own
                : Fe.NodeBases.TryGetValue(child, out var basis) ? [basis.NodeX0, basis.NodeX1, basis.NodeY0, basis.NodeY1]
                : [];
            return (influences.Length > 0 && Array.TrueForAll(influences, influence => Array.IndexOf(targets, influence) >= 0))
                || (own is not null && HoldsOneWideEntryOf(targets, child));
        }

        private bool HoldsOneWideEntryOf(int[] targets, int node)
        {
            if (Array.IndexOf(targets, node) < 0)
            {
                return false;
            }

            var children = 0;
            for (var i = 0; i < SkelParents.Length; i++)
            {
                if (SkelParents[i] != node)
                {
                    continue;
                }

                if (Array.IndexOf(targets, i) < 0)
                {
                    return false;
                }

                children++;
            }

            return children > 0;
        }

        /// <summary>Gets the bones <c>m_ReverseOffsets</c> names (<c>nBoneCtrl</c>).</summary>
        private HashSet<int> ReverseOffsetBones => reverseOffsetBones ??= [.. ReverseOffsetTargets.Keys];

        /// <summary>Gets the target nodes each <c>m_ReverseOffsets</c> bone names.</summary>
        private Dictionary<int, HashSet<int>> ReverseOffsetTargets
        {
            get
            {
                if (reverseOffsetTargets is null)
                {
                    reverseOffsetTargets = [];
                    foreach (var entry in Fe.Data.GetArray("m_ReverseOffsets") ?? [])
                    {
                        var boneTargets = GetOrAdd(reverseOffsetTargets, entry.GetInt32Property("nBoneCtrl"));
                        boneTargets.Add(entry.GetInt32Property("nTargetNode"));
                    }
                }

                return reverseOffsetTargets;
            }
        }

        /// <summary>
        /// Gets whether a proxy-sheet vertex of <paramref name="proxy"/> carries an <c>m_NodeBases</c> entry, which only a
        /// sheet imported with <c>add_bones_to_render_mesh</c> gives it.
        /// </summary>
        internal bool ProxyOwnsNodeBases(ProxyMesh proxy)
            => Array.Exists(proxy.NodeIndices, node => IsProxyMeshNode(node) && Fe.NodeBases.ContainsKey(node));

        /// <summary>
        /// Gets whether the chain's <c>m_NodeBases</c> entries are the bulk grade over each joint's neighbours (true) or the
        /// version-2 preset grade (false), or null when nothing tells them apart.
        /// </summary>
        internal bool? ChainBasesAreBulkGraded(BoneChain chain)
        {
            var bulk = 0;
            var preset = 0;
            var unmoved = new Dictionary<int, Vector3>();
            foreach (var joint in chain.Joints)
            {
                if (!Fe.NodeBases.TryGetValue(joint.Node, out var want) || ChainJointRing(joint).Count >= 2 || IsAnyHingedJoint(joint.Node))
                {
                    continue;
                }

                var child = chain.Joints.Find(other => other.ParentNode == joint.Node);
                if (child is null || ChainJointRing(joint).Count == 0 || ChainJointRing(child).Count == 0)
                {
                    continue;
                }

                var presetCandidates = ChainNodeBaseCandidates(joint, child);
                if (presetCandidates is null)
                {
                    continue;
                }

                var presetReaches = NodeBaseContains(presetCandidates, want);
                var presetHit = presetReaches
                    && NodeBaseDenotes(PredictNodeBase(presetCandidates, joint.Node, unmoved, want).Basis, want);

                var neighbours = NodeNeighbours(joint.Node);
                var bulkEligible = joint.InvMass > 0f || Fe.AllowsRotation(joint.Node);
                var bulkHit = bulkEligible && neighbours.Count >= 3 && NodeBaseContains(neighbours, want)
                    && (!presetReaches || NodeBaseDenotes(PredictNodeBase(neighbours, joint.Node, unmoved, want).Basis, want));

                if (presetHit == bulkHit)
                {
                    continue;
                }

                if (bulkHit)
                {
                    bulk++;
                }
                else
                {
                    preset++;
                }
            }

            return bulk == preset ? null : bulk > preset;
        }

        /// <summary>
        /// Whether the original fits a joint of <paramref name="chain"/> that version 2 would have given both a preset basis
        /// and a reverse offset. Version 2 skips fitting such a joint, so the chain compiled below version 2.
        /// </summary>
        internal bool ChainFitsAPresetJoint(BoneChain chain)
        {
            var unmoved = new Dictionary<int, Vector3>();
            foreach (var joint in chain.Joints)
            {
                if (!joint.Simulated || !Fe.FitMatrixNodes.Contains(joint.Node) || IsHingedJoint(joint.Node))
                {
                    continue;
                }

                var child = chain.Joints.Find(other => other.ParentNode == joint.Node);
                if (child is null || ChainNodeBaseCandidates(joint, child) is not { Count: >= 3 } candidates)
                {
                    continue;
                }

                var preset = PredictNodeBase(candidates, joint.Node, unmoved, default).Basis;
                if (preset.NodeX0 != preset.NodeX1 && preset.NodeY0 != preset.NodeY1
                    && joint.Node != preset.NodeX0 && joint.Node != preset.NodeX1
                    && joint.Node != preset.NodeY0 && joint.Node != preset.NodeY1)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the original compiled <paramref name="chain"/> with the reverse offsets a <c>ClothChain</c> of version 2
        /// or above records against its joints' preset bases: true when every read joint names the Y1 node of the preset
        /// basis graded over its own extrusion vector and its child's, false when one names none, null when no joint is read.
        /// </summary>
        internal bool? ChainReverseOffsetsArePreset(BoneChain chain)
        {
            var targets = ReverseOffsetTargets;

            var read = false;
            var unmoved = new Dictionary<int, Vector3>();
            foreach (var joint in chain.Joints)
            {
                if (joint.Node < Fe.StaticNodeCount || !targets.TryGetValue(joint.Node, out var jointTargets) || IsAnyHingedJoint(joint.Node))
                {
                    continue;
                }

                var child = chain.Joints.Find(other => other.ParentNode == joint.Node);
                var candidates = child is null ? null : ChainNodeBaseCandidates(joint, child);
                if (candidates is null)
                {
                    continue;
                }

                var scan = PredictNodeBase(candidates, joint.Node, unmoved, default);
                var preset = scan.Basis;
                if (preset.NodeY0 == preset.NodeY1)
                {
                    continue;
                }

                if (!jointTargets.Contains(preset.NodeY1) && !(jointTargets.Contains(preset.NodeY0) && NodeBaseYPairTies(joint.Node, scan)))
                {
                    return false;
                }

                read = true;
            }

            return read ? true : null;
        }

        /// <summary>
        /// Whether the original's own basis for <paramref name="node"/> is <paramref name="scan"/>'s basis with its Y pair swapped,
        /// with the handedness that orders the pair inside <see cref="NodeBaseTieMargin"/>.
        /// </summary>
        private bool NodeBaseYPairTies(int node, NodeBaseScan scan)
            => scan.Handedness < NodeBaseTieMargin && Fe.NodeBases.TryGetValue(node, out var want)
                && want == new NodeBasis(scan.Basis.NodeX0, scan.Basis.NodeX1, scan.Basis.NodeY1, scan.Basis.NodeY0);

        private static bool NodeBaseDenotes(NodeBasis basis, NodeBasis want)
            => basis == want || NodeBaseFoldReaches(basis, want);

        /// <summary>
        /// Gets whether a chain joint with a chain child carries an ungraded <c>m_DynNodeWindBases</c> hint whose X pair its
        /// twist or rope wrote, which marks a chain of version 0.
        /// </summary>
        internal bool ChainHintsAreTwistWritten(BoneChain chain)
        {
            var hints = Fe.Data.GetArray("m_DynNodeWindBases");
            if (hints is null || hints.Count == 0)
            {
                return false;
            }

            var ropePairs = RopeSourceHintPairs();
            var twistWritten = false;
            foreach (var joint in chain.Joints)
            {
                if (Fe.FitMatrixNodes.Contains(joint.Node))
                {
                    return false;
                }

                var slot = joint.Node - Fe.StaticNodeCount;
                if (slot < 0 || slot >= hints.Count)
                {
                    continue;
                }

                var hint = hints[slot];
                var x0 = hint.GetInt32Property("nNodeX0");
                var x1 = hint.GetInt32Property("nNodeX1");
                var ungraded = hint.GetInt32Property("nNodeY0") == 0 && hint.GetInt32Property("nNodeY1") == 0;
                if (ungraded && ((x0 == joint.Node && x1 != joint.Node && TwistRelaxByLink.ContainsKey((joint.Node, x1)))
                    || (ropePairs.TryGetValue(joint.Node, out var ropePair) && ropePair == (x0, x1))))
                {
                    if (chain.Joints.Exists(child => child.ParentNode == joint.Node))
                    {
                        twistWritten = true;
                    }
                }
                else if (NodeNeighbours(joint.Node).Count < 3)
                {
                    return false;
                }
            }

            return twistWritten;
        }

        /// <summary>
        /// Gets the hint X pair the rope source writes for each <c>m_Ropes</c> node; a node on two runs keeps the first.
        /// </summary>
        private Dictionary<int, (int X0, int X1)> RopeSourceHintPairs()
        {
            var pairs = new Dictionary<int, (int X0, int X1)>();
            foreach (var run in RopeRuns)
            {
                if (run.Length < 2)
                {
                    continue;
                }

                for (var i = 0; i < run.Length; i++)
                {
                    var pair = i == 0 ? (run[i], run[i + 1])
                        : i == run.Length - 1 ? (run[i], run[i - 1])
                        : (run[i - 1], run[i + 1]);
                    pairs.TryAdd(run[i], pair);
                }
            }

            return pairs;
        }

        /// <summary>What the thin joints of a set of chain joints say about the version that staged them.</summary>
        internal enum ThinJointStaging
        {
            /// <summary>No thin joint is read.</summary>
            None,
            /// <summary>Thin joints are read and none owns a group: staged at version 0.</summary>
            Unstaged,
            /// <summary>A thin joint owns a group: staged at version 1 or above.</summary>
            Staged,
        }

        /// <summary>
        /// Gets whether the chain's non-root joints read as <see cref="ThinJointStaging.Unstaged"/>, which separates version 0
        /// from version 1.
        /// </summary>
        internal bool ChainHasUnstagedThinJoint(BoneChain chain)
            => ThinJointStagingOf(chain.Joints.Skip(1)) == ThinJointStaging.Unstaged;

        /// <summary>
        /// Gets whether a two-sided chain has a simulated leaf with no node base, reverse offset, lock or fit matrix that
        /// too few neighbours reach to be graded.
        /// </summary>
        internal bool ChainHasUnbasedLeaf(BoneChain chain)
        {
            if (chain.ExtrudeSides != 2)
            {
                return false;
            }

            return chain.Joints.Exists(joint => !joint.IsRoot && joint.Simulated
                && joint.ExtrudeSides == 2
                && !chain.Joints.Exists(child => child.ParentNode == joint.Node)
                && !Fe.NodeBases.ContainsKey(joint.Node)
                && !Fe.FitMatrixNodes.Contains(joint.Node)
                && !ReverseOffsetBones.Contains(joint.Node)
                && !Fe.IsLockedToGoal(joint.Node) && !Fe.IsLockedToParent(joint.Node)
                && (joint.StretchStiffness != 0f || joint.AnimatedLength)
                && NodeNeighbours(joint.Node).Count < 3);
        }

        /// <summary>
        /// Reads the non-root joints whose fit-influence table holds one or two entries: staged when one owns a reverse
        /// offset, lock or fit matrix, unstaged when none does.
        /// </summary>
        internal ThinJointStaging ThinJointStagingOf(IEnumerable<BoneChainJoint> joints)
        {
            var list = joints as IReadOnlyList<BoneChainJoint> ?? [.. joints];
            var unstaged = false;
            foreach (var joint in list)
            {
                if (joint.Node < Fe.StaticNodeCount || IsAnyHingedJoint(joint.Node))
                {
                    continue;
                }

                var own = NumberedRingCount(joint.Node);
                var table = own >= 2 ? own : own > 0 ? 1 + own : 0;
                foreach (var child in list)
                {
                    if (child.ParentNode == joint.Node)
                    {
                        table += NumberedRingCount(child.Node);
                    }
                }

                if (table is >= 1 and <= 2)
                {
                    if (ReverseOffsetBones.Contains(joint.Node) || Fe.IsLockedToGoal(joint.Node)
                        || Fe.IsLockedToParent(joint.Node) || Fe.FitMatrixNodes.Contains(joint.Node))
                    {
                        return ThinJointStaging.Staged;
                    }

                    unstaged = true;
                }
            }

            return unstaged ? ThinJointStaging.Unstaged : ThinJointStaging.None;
        }
    }
}
