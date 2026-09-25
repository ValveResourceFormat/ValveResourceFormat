using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        /// <summary>The margin a node-base scan decision has to clear to count as settled.</summary>
        const float NodeBaseTieMargin = 1e-4f;

        /// <summary>The ring rolls in degrees tried to settle a node-base tie, widest first.</summary>
        static readonly float[] NodeBaseNudgeLadder =
        [
            0.016f, -0.016f, 0.012f, -0.012f, 0.008f, -0.008f, 0.004f, -0.004f,
            0.002f, -0.002f, 0.001f, -0.001f, 0.0005f, -0.0005f,
        ];

        /// <summary>The furthest a roll may move a node or change a rod's rest length.</summary>
        const float NodeBaseCostBudget = 5e-4f;

        /// <summary>
        /// A joint's compiled node base with the candidate list scanned for it and the two joints the list is drawn from.
        /// </summary>
        readonly record struct NodeBaseTarget(int Node, List<int> Candidates, NodeBasis Want,
            BoneChainJoint First, BoneChainJoint Second);

        /// <summary>
        /// Gets whether a proxy-sheet vertex of <paramref name="proxy"/> carries an <c>m_NodeBases</c> entry, which only a
        /// sheet imported with <c>add_bones_to_render_mesh</c> gives it.
        /// </summary>
        public bool ProxyOwnsNodeBases(ProxyMesh proxy)
            => Array.Exists(proxy.NodeIndices, node => IsProxyMeshNode(node) && NodeBases.ContainsKey(node));

        /// <summary>
        /// Gets whether the chain's <c>m_NodeBases</c> entries are the bulk grade over each joint's neighbours (true) or the
        /// version-2 preset grade (false), or null when nothing tells them apart.
        /// </summary>
        public bool? ChainBasesAreBulkGraded(BoneChain chain)
        {
            var bulk = 0;
            var preset = 0;
            var unmoved = new Dictionary<int, Vector3>();
            foreach (var joint in chain.Joints)
            {
                if (!NodeBases.TryGetValue(joint.Node, out var want) || ChainJointRing(joint).Count >= 2
                    || IsHingedJoint(joint.Node) || RigidHingeJoints.ContainsKey(joint.Node))
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
                var bulkEligible = joint.InvMass > 0f || AllowsRotation(joint.Node);
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
        /// Whether the original fits a joint of <paramref name="chain"/> that a <c>ClothChain</c> of version 2 would have given
        /// both a preset basis and a reverse offset, which puts the joint in both of the fit pass's skip sets and discards its
        /// group, so the chain compiled below version 2.
        /// </summary>
        public bool ChainFitsAPresetJoint(BoneChain chain)
        {
            var unmoved = new Dictionary<int, Vector3>();
            foreach (var joint in chain.Joints)
            {
                if (!joint.Simulated || !FitMatrixNodes.Contains(joint.Node) || IsHingedJoint(joint.Node))
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
        public bool? ChainReverseOffsetsArePreset(BoneChain chain)
        {
            var targets = new Dictionary<int, HashSet<int>>();
            foreach (var entry in Data.GetArray("m_ReverseOffsets") ?? [])
            {
                var bone = entry.GetInt32Property("nBoneCtrl");
                if (!targets.TryGetValue(bone, out var boneTargets))
                {
                    targets[bone] = boneTargets = [];
                }

                boneTargets.Add(entry.GetInt32Property("nTargetNode"));
            }

            var read = false;
            var unmoved = new Dictionary<int, Vector3>();
            foreach (var joint in chain.Joints)
            {
                if (joint.Node < StaticNodeCount || !targets.TryGetValue(joint.Node, out var jointTargets)
                    || IsHingedJoint(joint.Node) || RigidHingeJoints.ContainsKey(joint.Node))
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
        bool NodeBaseYPairTies(int node, NodeBaseScan scan)
            => scan.Handedness < NodeBaseTieMargin && NodeBases.TryGetValue(node, out var want)
                && want == new NodeBasis(scan.Basis.NodeX0, scan.Basis.NodeX1, scan.Basis.NodeY1, scan.Basis.NodeY0);

        static bool NodeBaseDenotes(NodeBasis basis, NodeBasis want)
            => basis == want || NodeBaseFoldReaches(basis, want);

        /// <summary>
        /// Gets whether a chain joint with a chain child carries an ungraded <c>m_DynNodeWindBases</c> hint whose X pair its
        /// twist or rope wrote, which marks a chain of version 0.
        /// </summary>
        public bool ChainHintsAreTwistWritten(BoneChain chain)
        {
            var hints = Data.GetArray("m_DynNodeWindBases");
            if (hints is null || hints.Count == 0)
            {
                return false;
            }

            var ropePairs = RopeSourceHintPairs();
            var twistWritten = false;
            foreach (var joint in chain.Joints)
            {
                if (FitMatrixNodes.Contains(joint.Node))
                {
                    return false;
                }

                var slot = joint.Node - StaticNodeCount;
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
        Dictionary<int, (int X0, int X1)> RopeSourceHintPairs()
        {
            var pairs = new Dictionary<int, (int X0, int X1)>();
            foreach (var run in RopeRuns)
            {
                for (var i = 0; i < run.Length && run.Length >= 2; i++)
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
        public bool ChainHasUnstagedThinJoint(BoneChain chain)
            => ThinJointStagingOf(chain.Joints.Skip(1)) == ThinJointStaging.Unstaged;

        /// <summary>
        /// Gets whether a two-sided chain has a simulated leaf with no node base, reverse offset, lock or fit matrix that
        /// too few neighbours reach to be graded.
        /// </summary>
        public bool ChainHasUnbasedLeaf(BoneChain chain)
        {
            if (chain.ExtrudeSides != 2)
            {
                return false;
            }

            return chain.Joints.Exists(joint => !joint.IsRoot && joint.Simulated
                && joint.ExtrudeSides == 2
                && !chain.Joints.Exists(child => child.ParentNode == joint.Node)
                && !NodeBases.ContainsKey(joint.Node)
                && !FitMatrixNodes.Contains(joint.Node)
                && !ReverseOffsetBones.Contains(joint.Node)
                && !IsLockedToGoal(joint.Node) && !IsLockedToParent(joint.Node)
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
                if (joint.Node < StaticNodeCount || IsHingedJoint(joint.Node) || RigidHingeJoints.ContainsKey(joint.Node))
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
                    if (ReverseOffsetBones.Contains(joint.Node) || IsLockedToGoal(joint.Node)
                        || IsLockedToParent(joint.Node) || FitMatrixNodes.Contains(joint.Node))
                    {
                        return ThinJointStaging.Staged;
                    }

                    unstaged = true;
                }
            }

            return unstaged ? ThinJointStaging.Unstaged : ThinJointStaging.None;
        }

        /// <summary>
        /// The root bones of <paramref name="merged"/> whose one reconstructed declaration was compiled as two,
        /// each mapped to the children the second, ringless declaration keeps.
        /// </summary>
        Dictionary<int, HashSet<int>> VersionSplitRoots(List<BoneChain> merged, Func<BoneChain, bool, int> chainVersion,
            bool hasOtherChains)
        {
            var splits = new Dictionary<int, HashSet<int>>();
            foreach (var chain in merged)
            {
                if (chain.Joints.Count < 3 || chain.Joints[0].RingNodes.Count == 0)
                {
                    continue;
                }

                var root = chain.Joints[0].Node;
                var ringEnd = chain.Joints[0].RingNodes.Max();
                if (ringEnd >= StaticNodeCount)
                {
                    continue;
                }

                var staged = new List<int>();
                var unstaged = new List<int>();
                (int AfterRing, int Any) stagedFirst = (int.MaxValue, int.MaxValue);
                (int AfterRing, int Any) unstagedFirst = (int.MaxValue, int.MaxValue);
                var sharesRingBlock = false;
                var readable = true;
                foreach (var kid in chain.Joints)
                {
                    if (kid.ParentNode != root)
                    {
                        continue;
                    }

                    var subtree = new List<BoneChainJoint> { kid };
                    var members = new HashSet<int> { kid.Node };
                    foreach (var joint in chain.Joints)
                    {
                        if (joint != kid && members.Contains(joint.ParentNode))
                        {
                            members.Add(joint.Node);
                            subtree.Add(joint);
                        }
                    }

                    var rings = subtree.SelectMany(static joint => joint.RingNodes).Where(node => node < StaticNodeCount).ToList();
                    var ringBlock = rings.FindAll(node => AllowsRotation(node) == AllowsRotation(ringEnd));
                    sharesRingBlock |= ringBlock.Count > 0;
                    var first = (AfterRing: ringBlock.Where(node => node > ringEnd).DefaultIfEmpty(int.MaxValue).Min(),
                        Any: rings.DefaultIfEmpty(int.MaxValue).Min());

                    switch (ThinJointStagingOf(subtree))
                    {
                        case ThinJointStaging.Staged:
                            staged.Add(kid.Node);
                            stagedFirst = (Math.Min(stagedFirst.AfterRing, first.AfterRing), Math.Min(stagedFirst.Any, first.Any));
                            break;
                        case ThinJointStaging.Unstaged:
                            unstaged.Add(kid.Node);
                            unstagedFirst = (Math.Min(unstagedFirst.AfterRing, first.AfterRing), Math.Min(unstagedFirst.Any, first.Any));
                            break;
                        default:
                            readable = false;
                            break;
                    }
                }

                var (stagedAt, unstagedAt) = sharesRingBlock
                    ? (stagedFirst.AfterRing, unstagedFirst.AfterRing)
                    : (stagedFirst.Any, unstagedFirst.Any);

                if (!readable || staged.Count == 0 || unstaged.Count == 0 || stagedAt == unstagedAt
                    || chainVersion(chain, hasOtherChains) != 1)
                {
                    continue;
                }

                splits[root] = [.. stagedAt < unstagedAt ? unstaged : staged];
            }

            return splits;
        }

        int NumberedRingCount(int jointNode)
            => ProxyRingOf(jointNode).Count(node => RingSuffixIndex(CtrlNames[node]) >= 0);

        /// <summary>
        /// Rolls the extruded ring of a chain joint whose <c>m_NodeBases</c> axis scan is a numerical tie
        /// onto the axis pair the original kept, recording the roll in
        /// <see cref="BoneChainJoint.ExtrudeTwistTieNudge"/>.
        /// </summary>
        void SteerNodeBaseTies(BoneChain chain)
        {
            if (NodeBases.Count == 0)
            {
                return;
            }

            var targets = new List<NodeBaseTarget>();
            for (var i = 0; i < chain.Joints.Count; i++)
            {
                var joint = chain.Joints[i];
                if (!NodeBases.TryGetValue(joint.Node, out var want))
                {
                    continue;
                }

                var child = i + 1 < chain.Joints.Count && chain.Joints[i + 1].ParentNode == joint.Node
                    ? chain.Joints[i + 1]
                    : null;
                var first = child is not null ? joint : chain.Joints.Find(other => other.Node == joint.ParentNode);
                if (first is null)
                {
                    continue;
                }

                var second = child ?? joint;

                var neighbours = NodeNeighbours(joint.Node);
                var candidates = neighbours.Count >= 3 && NodeBaseContains(neighbours, want)
                    ? neighbours
                    : NodeBaseCandidates(first, second);

                if (candidates is null || !NodeBaseContains(candidates, want))
                {
                    var parent = chain.Joints.Find(other => other.Node == first.ParentNode);
                    candidates = parent is not null ? NodeBaseCandidates(parent, first, second) : null;
                    if (candidates is null || !NodeBaseContains(candidates, want))
                    {
                        continue;
                    }
                }

                targets.Add(new NodeBaseTarget(joint.Node, candidates, want, first, second));
            }

            var moved = new Dictionary<int, Vector3>();
            foreach (var target in targets)
            {
                var scan = PredictNodeBase(target.Candidates, target.Node, moved, target.Want);

                if ((scan.Basis == target.Want && scan.Decided == NodeBaseScan.Decisions) || scan.DecidedAgainst
                    || target.Want.NodeX1 > target.Want.NodeX0)
                {
                    continue;
                }

                if (!TryNudgeNodeBase(target.Second, target, targets, moved, scan))
                {
                    TryNudgeNodeBase(target.First, target, targets, moved, scan);
                }
            }
        }

        bool TryNudgeNodeBase(BoneChainJoint joint, NodeBaseTarget target, List<NodeBaseTarget> targets,
            Dictionary<int, Vector3> moved, NodeBaseScan before)
        {
            var ring = ProxyRingOf(joint.Node);
            if (ring.Count == 0 || IsHingedJoint(joint.Node) || joint.Node >= InitPoseRotations.Length
                || ring.Exists(node => node >= InitPosePositions.Length)
                || NodeBaseRingIsReadElsewhere(ring, targets))
            {
                return false;
            }

            var axis = Vector3.Transform(Vector3.UnitX,
                InitPoseRotations[joint.Node] * ExtrudeAxisSelectQuaternion(joint.ForwardAxis));
            if (axis.LengthSquared() <= 0f)
            {
                return false;
            }

            axis = Vector3.Normalize(axis);
            var pivot = InitPosePositions[joint.Node];

            foreach (var nudge in NodeBaseNudgeLadder)
            {
                var probe = new Dictionary<int, Vector3>(moved);
                var rotation = Quaternion.CreateFromAxisAngle(axis,
                    float.DegreesToRadians(joint.ExtrudeTwistTieNudge + nudge));
                foreach (var node in ring)
                {
                    probe[node] = pivot + Vector3.Transform(InitPosePositions[node] - pivot, rotation);
                }

                var after = PredictNodeBase(target.Candidates, target.Node, probe, target.Want);
                if (after.Basis != target.Want || !after.NoWorseThan(before) || after.Decided <= before.Decided
                    || !NodeBaseRollAffordable(probe) || NodeBaseRollRegresses(targets, moved, probe))
                {
                    continue;
                }

                joint.ExtrudeTwistTieNudge += nudge;
                foreach (var (node, position) in probe)
                {
                    moved[node] = position;
                }

                return true;
            }

            return false;
        }

        bool NodeBaseRollAffordable(Dictionary<int, Vector3> probe)
        {
            foreach (var (node, position) in probe)
            {
                if (Vector3.Distance(InitPosePositions[node], position) > NodeBaseCostBudget)
                {
                    return false;
                }
            }

            foreach (var rod in Rods)
            {
                if (!probe.ContainsKey(rod.NodeA) && !probe.ContainsKey(rod.NodeB))
                {
                    continue;
                }

                if (rod.NodeA >= InitPosePositions.Length || rod.NodeB >= InitPosePositions.Length)
                {
                    continue;
                }

                var rest = Vector3.Distance(InitPosePositions[rod.NodeA], InitPosePositions[rod.NodeB]);
                var rolled = Vector3.Distance(RestPosition(rod.NodeA, probe), RestPosition(rod.NodeB, probe));
                if (MathF.Abs(rolled - rest) > NodeBaseCostBudget * MathF.Max(1f, rest))
                {
                    return false;
                }
            }

            return true;
        }

        bool NodeBaseRingIsReadElsewhere(List<int> ring, List<NodeBaseTarget> targets)
        {
            foreach (var (node, basis) in NodeBases)
            {
                if (targets.Exists(target => target.Node == node))
                {
                    continue;
                }

                if (ring.Contains(basis.NodeX0) || ring.Contains(basis.NodeX1)
                    || ring.Contains(basis.NodeY0) || ring.Contains(basis.NodeY1))
                {
                    return true;
                }
            }

            return false;
        }

        bool NodeBaseRollRegresses(List<NodeBaseTarget> targets,
            Dictionary<int, Vector3> moved, Dictionary<int, Vector3> probe)
        {
            foreach (var target in targets)
            {
                var before = PredictNodeBase(target.Candidates, target.Node, moved, target.Want);
                var after = PredictNodeBase(target.Candidates, target.Node, probe, target.Want);
                if (!after.NoWorseThan(before)
                    || (before.Basis == target.Want && after.Basis != target.Want))
                {
                    return true;
                }
            }

            return false;
        }

        static bool NodeBaseContains(List<int> candidates, NodeBasis want)
            => candidates.Contains(want.NodeX0) && candidates.Contains(want.NodeX1)
            && candidates.Contains(want.NodeY0) && candidates.Contains(want.NodeY1);

        /// <summary>
        /// Gets the node vector the extrusion pushes for a joint: its ring when two or more wide, else its node and ring.
        /// </summary>
        List<int>? NodeBaseVector(BoneChainJoint joint)
        {
            var ring = ProxyRingOf(joint.Node);
            if (joint.ExtrudeSides >= 2)
            {
                return ring.Count >= joint.ExtrudeSides ? ring.GetRange(0, joint.ExtrudeSides) : null;
            }

            List<int> vector = [joint.Node];
            vector.AddRange(ring.Take(joint.ExtrudeSides));
            return vector;
        }

        /// <summary>
        /// Gets the sorted neighbour set a node's basis is graded against: the node and every corner of each source
        /// element it belongs to.
        /// </summary>
        List<int> NodeNeighbours(int node)
        {
            nodeNeighbours ??= BuildNodeNeighbours();
            return nodeNeighbours.TryGetValue(node, out var neighbours) ? neighbours : [];
        }

        Dictionary<int, List<int>>? nodeNeighbours;

        Dictionary<int, List<int>> BuildNodeNeighbours()
        {
            var sets = new Dictionary<int, SortedSet<int>>();

            void Join(IReadOnlyList<int> corners)
            {
                foreach (var a in corners)
                {
                    if (a < 0 || a >= InitPosePositions.Length)
                    {
                        continue;
                    }

                    if (!sets.TryGetValue(a, out var set))
                    {
                        sets[a] = set = [a];
                    }

                    foreach (var b in corners)
                    {
                        if (b >= 0 && b < InitPosePositions.Length)
                        {
                            set.Add(b);
                        }
                    }
                }
            }

            foreach (var face in SourceFaces)
            {
                Join(face);
            }

            foreach (var (a, b) in SourceSprings)
            {
                Join([a, b]);
            }

            var built = new Dictionary<int, List<int>>(sets.Count);
            foreach (var (node, set) in sets)
            {
                built[node] = [.. set];
            }

            return built;
        }

        /// <summary>
        /// The node list <see cref="ScanNodeBasePair"/> scans for a joint's graded or fit-arm basis, ascending by node
        /// index, which puts the higher node index of the winning pair in X0 and settles which pair an exact tie keeps.
        /// The chain preset scans its own list unsorted (<see cref="ChainNodeBaseCandidates"/>).
        /// </summary>
        List<int>? NodeBaseCandidates(params BoneChainJoint[] joints)
        {
            var candidates = new List<int>();
            foreach (var joint in joints)
            {
                if (NodeBaseVector(joint) is not { } vector)
                {
                    return null;
                }

                candidates.AddRange(vector);
            }

            if (candidates.Count < 3)
            {
                return null;
            }

            candidates.Sort();
            return candidates.TrueForAll(node => node < InitPosePositions.Length) ? candidates : null;
        }

        /// <summary>
        /// A joint's ring by its skeleton parents, or, where the compiled data parents none of the joint's
        /// <c>$cc</c> nodes to it, the ring the chain reconstruction assigned to the joint's declaration.
        /// </summary>
        List<int> ChainJointRing(BoneChainJoint joint)
            => ProxyRingOf(joint.Node) is { Count: > 0 } ring ? ring : [.. joint.RingNodes];

        /// <summary>
        /// The preset scan's node list for <paramref name="joint"/> and its child over <see cref="ChainJointRing"/>, in the order
        /// the chain importer hands it to the scan: the joint's own vector, then the child's, each as the extrusion pushes it and
        /// neither sorted. Where two pairs tie, the scan keeps the later one in this order and writes its later node as X0.
        /// </summary>
        List<int>? ChainNodeBaseCandidates(BoneChainJoint joint, BoneChainJoint child)
        {
            var candidates = new List<int>();
            foreach (var member in (ReadOnlySpan<BoneChainJoint>)[joint, child])
            {
                var ring = ChainJointRing(member);
                if (member.ExtrudeSides >= 2)
                {
                    if (ring.Count < member.ExtrudeSides)
                    {
                        return null;
                    }

                    candidates.AddRange(ring.GetRange(0, member.ExtrudeSides));
                    continue;
                }

                candidates.Add(member.Node);
                candidates.AddRange(ring.Take(member.ExtrudeSides));
            }

            if (candidates.Count < 3)
            {
                return null;
            }

            return candidates.TrueForAll(node => node < InitPosePositions.Length) ? candidates : null;
        }

        Vector3 RestPosition(int node, Dictionary<int, Vector3> moved)
            => moved.TryGetValue(node, out var position) ? position : InitPosePositions[node];

        /// <summary>
        /// The basis the compiler's scans write for one joint, with the signed margin of each of the four decisions behind
        /// it (X pair, Y pair, handedness, fold), positive towards the compiled basis.
        /// </summary>
        readonly record struct NodeBaseScan(NodeBasis Basis, float XMargin, float YMargin, float Handedness, float Fold)
        {
            public const int Decisions = 4;
            public int Decided => State(XMargin) + State(YMargin) + State(Handedness) + State(Fold);
            public bool NoWorseThan(NodeBaseScan other)
                => State(XMargin) >= State(other.XMargin) && State(YMargin) >= State(other.YMargin)
                && State(Handedness) >= State(other.Handedness) && State(Fold) >= State(other.Fold);
            public bool DecidedAgainst
                => State(XMargin) < 0 || State(YMargin) < 0 || State(Handedness) < 0 || State(Fold) < 0;
            static int State(float margin) => margin >= NodeBaseTieMargin ? 1 : margin <= -NodeBaseTieMargin ? -1 : 0;
        }

        const float NodeBaseDegenerateAxis = 0.05f;
        const float NodeBaseFoldResidual = 1e-4f;

        /// <summary>
        /// Runs the compiler's own two axis scans, the handedness flip that follows them and the pair swaps
        /// it folds a near-half-turn residual into over <paramref name="candidates"/>, scoring the result
        /// against the basis <paramref name="want"/> the original wrote for <paramref name="node"/>.
        /// </summary>
        NodeBaseScan PredictNodeBase(List<int> candidates, int node, Dictionary<int, Vector3> moved, NodeBasis want)
        {
            var (xOuter, xInner, xMargin) = ScanNodeBasePair(candidates, moved,
                static (a, b) => NodeBaseSpan(a, b), want.NodeX1, want.NodeX0);
            var xAxis = RestPosition(xOuter, moved) - RestPosition(xInner, moved);

            var (yOuter, yInner, yMargin) = ScanNodeBasePair(candidates, moved,
                (a, b) => NodeBasePerpendicular(xAxis, a - b), want.NodeY1, want.NodeY0);
            var yAxis = RestPosition(yOuter, moved) - RestPosition(yInner, moved);

            var scalar = NodeBaseHandedness(xAxis, yAxis, node);
            var basis = scalar < 0f
                ? new NodeBasis(xInner, xOuter, yOuter, yInner)
                : new NodeBasis(xInner, xOuter, yInner, yOuter);

            var (folded, residual) = FoldNodeBase(basis, xAxis, yAxis, scalar < 0f, node);
            var handedness = MathF.Abs(scalar) / MathF.Max(xAxis.Length(), 1e-12f);
            var fold = NodeBaseFoldReaches(basis, want)
                ? (folded == want ? 1f : -1f) * MathF.Abs(residual - NodeBaseFoldResidual)
                : 0f;
            return new NodeBaseScan(folded, xMargin, yMargin, handedness, fold);
        }

        /// <summary>
        /// Applies Gram-Schmidt and the half-turn folds to a scanned basis. Returns the basis written and the smallest
        /// residual tested.
        /// </summary>
        (NodeBasis Basis, float Residual) FoldNodeBase(NodeBasis basis, Vector3 xAxis, Vector3 yAxis,
            bool swapped, int node)
        {
            var up = node < InitPoseRotations.Length
                ? Vector3.Transform(Vector3.UnitZ, InitPoseRotations[node])
                : Vector3.UnitZ;
            var y = yAxis.Length() > 0f ? Vector3.Normalize(yAxis) : new Vector3(0f, 0f, -1f);
            if (swapped)
            {
                y = -y;
            }

            var x = xAxis - (y * Vector3.Dot(y, xAxis));
            if (x.Length() <= NodeBaseDegenerateAxis)
            {
                var alt = Vector3.Cross(up, y);
                x = alt.Length() < 1e-3f
                    ? Vector3.Cross(y, MathF.Abs(y.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY)
                    : alt;
            }

            if (x.LengthSquared() <= 0f)
            {
                return (basis, float.PositiveInfinity);
            }

            x = Vector3.Normalize(x);
            var z = Vector3.Cross(x, y);
            var predicted = Quaternion.CreateFromRotationMatrix(new Matrix4x4(
                x.X, x.Y, x.Z, 0f, y.X, y.Y, y.Z, 0f, z.X, z.Y, z.Z, 0f, 0f, 0f, 0f, 1f));
            var orientation = node < InitPoseRotations.Length ? InitPoseRotations[node] : Quaternion.Identity;
            var adjust = Quaternion.Normalize(Quaternion.Conjugate(predicted) * orientation);

            var straight = NodeBaseResidual(adjust);
            var both = NodeBaseResidual(adjust * new Quaternion(0f, 0f, 1f, 0f));
            var acrossX = NodeBaseResidual(adjust * new Quaternion(0f, 1f, 0f, 0f));
            var acrossY = NodeBaseResidual(adjust * new Quaternion(1f, 0f, 0f, 0f));
            var residual = MathF.Min(MathF.Min(straight, both), MathF.Min(acrossX, acrossY));

            if (straight < NodeBaseFoldResidual)
            {
                return (basis, residual);
            }

            if (both < NodeBaseFoldResidual)
            {
                return (new NodeBasis(basis.NodeX1, basis.NodeX0, basis.NodeY1, basis.NodeY0), residual);
            }

            if (acrossX < NodeBaseFoldResidual)
            {
                return (new NodeBasis(basis.NodeX1, basis.NodeX0, basis.NodeY0, basis.NodeY1), residual);
            }

            if (acrossY < NodeBaseFoldResidual)
            {
                return (new NodeBasis(basis.NodeX0, basis.NodeX1, basis.NodeY1, basis.NodeY0), residual);
            }

            return (basis, residual);
        }

        static float NodeBaseResidual(Quaternion q)
            => MathF.Sqrt((q.X * q.X) + (q.Y * q.Y) + (q.Z * q.Z));

        static bool NodeBaseFoldReaches(NodeBasis basis, NodeBasis want)
            => want == basis
            || want == new NodeBasis(basis.NodeX1, basis.NodeX0, basis.NodeY1, basis.NodeY0)
            || want == new NodeBasis(basis.NodeX1, basis.NodeX0, basis.NodeY0, basis.NodeY1)
            || want == new NodeBasis(basis.NodeX0, basis.NodeX1, basis.NodeY1, basis.NodeY0);

        /// <summary>
        /// Scans every pair in list order keeping the last maximum, and returns it with the margin by which the wanted pair
        /// beats the pairs that could take the scan from it.
        /// </summary>
        (int Outer, int Inner, float Margin) ScanNodeBasePair(List<int> candidates, Dictionary<int, Vector3> moved,
            Func<Vector3, Vector3, float> score, int wantOuter, int wantInner)
        {
            var lowWanted = Math.Min(wantOuter, wantInner);
            var highWanted = Math.Max(wantOuter, wantInner);
            var wanted = candidates.Contains(lowWanted) && candidates.Contains(highWanted)
                ? score(RestPosition(lowWanted, moved), RestPosition(highWanted, moved))
                : float.NegativeInfinity;

            var best = float.NegativeInfinity;
            var other = float.NegativeInfinity;
            var passedWanted = false;
            int outer = candidates[0], inner = candidates[0];
            for (var i = 0; i < candidates.Count; i++)
            {
                var a = RestPosition(candidates[i], moved);
                for (var j = i + 1; j < candidates.Count; j++)
                {
                    var value = score(a, RestPosition(candidates[j], moved));
                    if (value >= best)
                    {
                        best = value;
                        outer = candidates[i];
                        inner = candidates[j];
                    }

                    if ((candidates[i] == lowWanted && candidates[j] == highWanted)
                        || (candidates[i] == highWanted && candidates[j] == lowWanted))
                    {
                        passedWanted = true;
                    }
                    else if (passedWanted || value != wanted)
                    {
                        other = MathF.Max(other, value);
                    }
                }
            }

            return (outer, inner, wanted - other);
        }

        /// <summary>Gets the distance between two nodes, summed in the compiler's term order.</summary>
        static float NodeBaseSpan(Vector3 a, Vector3 b)
        {
            var d = a - b;
            return MathF.Sqrt((d.X * d.X) + (d.Y * d.Y) + (d.Z * d.Z));
        }

        /// <summary>Gets the length of <c>axis x d</c>, summed in the compiler's term order.</summary>
        static float NodeBasePerpendicular(Vector3 axis, Vector3 d)
        {
            var y = (axis.X * d.Z) - (axis.Z * d.X);
            var z = (axis.Y * d.X) - (axis.X * d.Y);
            var x = (axis.Z * d.Y) - (axis.Y * d.Z);
            return MathF.Sqrt((y * y) + (z * z) + (x * x));
        }

        /// <summary>Gets the scalar triple product of X, unit Y and the node's up axis, summed in the compiler's term order.</summary>
        float NodeBaseHandedness(Vector3 xAxis, Vector3 yAxis, int node)
        {
            var length = MathF.Sqrt((yAxis.Y * yAxis.Y) + (yAxis.Z * yAxis.Z) + (yAxis.X * yAxis.X));
            var y = length > 0f ? yAxis * (1f / length) : new Vector3(0f, 0f, -1f);
            var z = node < InitPoseRotations.Length
                ? Vector3.Transform(Vector3.UnitZ, InitPoseRotations[node])
                : Vector3.UnitZ;

            return (((y.X * xAxis.Z) - (xAxis.X * y.Z)) * z.Y)
                + (((xAxis.X * y.Y) - (y.X * xAxis.Y)) * z.Z)
                + (((y.Z * xAxis.Y) - (xAxis.Z * y.Y)) * z.X);
        }
    }
}
