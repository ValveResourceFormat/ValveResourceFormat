using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>
    /// The chain joints declared as their own <c>ClothNode</c> ahead of the chains, and the order the chains and their
    /// joints are walked in.
    /// </summary>
    private sealed class ClothChainDeclarationPlan
    {
        public List<(string Name, int Node)> PreDeclared { get; } = [];
        public List<FeModel.BoneChain> Chains { get; } = [];
        public Dictionary<FeModel.BoneChain, List<FeModel.BoneChainJoint>> Walk { get; } = [];
    }

    /// <summary>
    /// The band of every control node: the runs the compiler's node sort keeps contiguous, by block and constraint-graph
    /// rank. Null where the compiled node order does not follow that key.
    /// </summary>
    private static int[]? ClothNodeBands(FeModel feModel)
    {
        var count = feModel.NodeCount;
        if (count <= 0 || feModel.CtrlNames.Length != count || feModel.StaticNodeCount <= 0)
        {
            return null;
        }

        var neighbours = new HashSet<int>[count];
        var named = new bool[count];

        // An all-static element or rod makes no neighbours, but still places its members at the final rank.
        void Connect(IReadOnlyList<int> group)
        {
            var simulated = false;
            foreach (var node in group)
            {
                if (node >= 0 && node < count)
                {
                    named[node] = true;
                    simulated |= node >= feModel.StaticNodeCount;
                }
            }

            if (!simulated)
            {
                return;
            }

            foreach (var a in group)
            {
                foreach (var b in group)
                {
                    if (a == b || a < 0 || b < 0 || a >= count || b >= count)
                    {
                        continue;
                    }

                    (neighbours[a] ??= []).Add(b);
                }
            }
        }

        foreach (var face in feModel.SourceFaces)
        {
            Connect(face);
        }

        foreach (var (a, b) in feModel.SourceSprings)
        {
            Connect([a, b]);
        }

        foreach (var quad in feModel.Quads)
        {
            Connect(quad);
        }

        foreach (var tri in feModel.Tris)
        {
            Connect(tri);
        }

        foreach (var rod in feModel.Rods)
        {
            Connect([rod.NodeA, rod.NodeB]);
        }

        var rank = new int[count];
        Array.Fill(rank, int.MaxValue);
        var frontier = new List<int>();
        for (var i = 0; i < feModel.StaticNodeCount; i++)
        {
            rank[i] = 0;
            frontier.Add(i);
        }

        var level = 0;
        while (frontier.Count > 0)
        {
            level++;
            var next = new List<int>();
            foreach (var node in frontier)
            {
                foreach (var other in neighbours[node] ?? [])
                {
                    if (rank[other] == int.MaxValue)
                    {
                        rank[other] = level;
                        next.Add(other);
                    }
                }
            }

            frontier = next;
        }

        // An unreached node any element or rod names takes the final rank; one nothing names keeps int.MaxValue.
        for (var i = 0; i < count; i++)
        {
            if (rank[i] == int.MaxValue && named[i])
            {
                rank[i] = level;
            }
        }

        var rotLock = Math.Clamp(feModel.RotationLockedStaticNodeCount, 0, feModel.StaticNodeCount);
        var positionDriven = Math.Clamp(feModel.FirstPositionDrivenNode, feModel.StaticNodeCount, count);
        int Block(int node) => node < rotLock ? 0 : node < feModel.StaticNodeCount ? 1 : node < positionDriven ? 2 : 3;

        var bands = new int[count];
        for (var i = 1; i < count; i++)
        {
            var stepped = Block(i) != Block(i - 1) || rank[i] != rank[i - 1];
            if (stepped && (Block(i) < Block(i - 1) || (Block(i) == Block(i - 1) && rank[i] < rank[i - 1])))
            {
                return null;
            }

            bands[i] = stepped ? bands[i - 1] + 1 : bands[i - 1];
        }

        return bands;
    }

    /// <summary>
    /// The declaration plan that reproduces the compiled control-node order, or null when the natural order already does,
    /// the bands cannot be read, or no plan does.
    /// </summary>
    private static ClothChainDeclarationPlan? TryPlanClothChainDeclarations(FeModel feModel,
        List<FeModel.BoneChain> chains, Func<string, bool> reparents)
    {
        if (chains.Count == 0 || ClothNodeBands(feModel) is not { } bands)
        {
            return null;
        }

        // One entry per joint declaration: several chains can declare the same bone.
        var joints = new List<(int Chain, FeModel.BoneChainJoint Joint, List<int> Rings)>();
        var occurrences = new Dictionary<int, List<int>>();
        var jointNodes = new HashSet<int>();
        var ringNodes = new HashSet<int>();

        // A joint two rings wide or wider creates its node after every chain's rings; a narrower one before its own ring.
        var deferred = new HashSet<int>();
        for (var c = 0; c < chains.Count; c++)
        {
            foreach (var joint in chains[c].Joints)
            {
                if (joint.Node < 0 || joint.Node >= bands.Length)
                {
                    return null;
                }

                var rings = new List<int>(joint.RingNodes.Count);
                foreach (var ring in joint.RingNodes)
                {
                    if (ring < 0 || ring >= bands.Length || !ringNodes.Add(ring))
                    {
                        return null;
                    }

                    rings.Add(ring);
                }

                jointNodes.Add(joint.Node);
                if (joint.ExtrudeSides >= 2)
                {
                    deferred.Add(joint.Node);
                }

                GetOrAdd(occurrences, joint.Node).Add(joints.Count);
                joints.Add((c, joint, rings));
            }
        }

        if (jointNodes.Overlaps(ringNodes))
        {
            return null;
        }

        var owned = new HashSet<int>(jointNodes);
        owned.UnionWith(ringNodes);

        var lanes = new List<List<int>>();
        for (var node = 0; node < bands.Length; node++)
        {
            if (!owned.Contains(node))
            {
                continue;
            }

            while (lanes.Count <= bands[node])
            {
                lanes.Add([]);
            }

            lanes[bands[node]].Add(node);
        }

        // The chains' nodes can only be reordered within a band they fill without a foreign node.
        var runStart = new Dictionary<int, int>();
        var runEnd = new Dictionary<int, int>();
        var runCount = new Dictionary<int, int>();
        var pureBands = new HashSet<int>();
        for (var node = 0; node < bands.Length; node++)
        {
            var band = bands[node];
            if (!owned.Contains(node))
            {
                pureBands.Remove(band);
                continue;
            }

            if (!runCount.ContainsKey(band))
            {
                runStart[band] = node;
                pureBands.Add(band);
            }

            runEnd[band] = node;
            runCount[band] = runCount.GetValueOrDefault(band) + 1;
        }

        foreach (var (band, count) in runCount)
        {
            if (count != runEnd[band] - runStart[band] + 1)
            {
                return null;
            }
        }

        // Within a band, narrow joints and their rings must be created in the same order.
        for (var i = 0; i < joints.Count; i++)
        {
            for (var j = i + 1; j < joints.Count; j++)
            {
                var (_, first, firstRings) = joints[i];
                var (_, second, secondRings) = joints[j];
                if (firstRings.Count == 0 || secondRings.Count == 0
                    || deferred.Contains(first.Node) || deferred.Contains(second.Node)
                    || first.Node == second.Node
                    || bands[first.Node] != bands[second.Node]
                    || bands[firstRings[0]] != bands[secondRings[0]])
                {
                    continue;
                }

                if (first.Node < second.Node != firstRings[0] < secondRings[0])
                {
                    return null;
                }
            }
        }

        var solver = new ClothChainOrderSolver(joints, occurrences, deferred, lanes, bands, pureBands);
        return solver.Solve(feModel, chains, reparents);
    }

    /// <summary>
    /// Searches the declaration orders whose compiler creation walk takes every band's nodes in their compiled order.
    /// </summary>
    private sealed class ClothChainOrderSolver
    {
        private const int ExpansionBudget = 50000;

        private readonly List<(int Chain, FeModel.BoneChainJoint Joint, List<int> Rings)> joints;
        private readonly Dictionary<int, List<int>> occurrences;
        private readonly Dictionary<int, List<int>> owners = [];
        private readonly HashSet<int> deferred;
        private readonly HashSet<int> pureBands;
        private readonly List<List<int>> lanes;
        private readonly int[] bands;
        private readonly int[] lanePos;
        private readonly bool[] walked;
        private readonly bool[] tookNode;
        private readonly bool[] nodeTaken;
        private readonly bool[] declaredNode;
        private readonly bool[] keepInChain;
        private readonly int[] chainPending;
        private readonly List<int> preDeclared = [];
        private readonly List<int> walkOrder = [];
        private int remaining;
        private int expansions;
        private bool started;
        private int currentChain = -1;

        public ClothChainOrderSolver(List<(int Chain, FeModel.BoneChainJoint Joint, List<int> Rings)> joints,
            Dictionary<int, List<int>> occurrences, HashSet<int> deferred, List<List<int>> lanes,
            int[] bands, HashSet<int> pureBands)
        {
            this.joints = joints;
            this.occurrences = occurrences;
            this.deferred = deferred;
            this.lanes = lanes;
            this.bands = bands;
            this.pureBands = pureBands;
            lanePos = new int[lanes.Count];
            walked = new bool[joints.Count];
            tookNode = new bool[joints.Count];
            nodeTaken = new bool[bands.Length];
            declaredNode = new bool[bands.Length];
            keepInChain = new bool[joints.Count];
            chainPending = new int[joints.Count == 0 ? 0 : joints[^1].Chain + 1];
            for (var index = 0; index < joints.Count; index++)
            {
                var (_, joint, rings) = joints[index];
                Own(joint.Node, index);
                foreach (var ring in rings)
                {
                    Own(ring, index);
                }
            }

            void Own(int node, int index)
            {
                var list = GetOrAdd(owners, node);
                if (!list.Contains(index))
                {
                    list.Add(index);
                }
            }
        }

        public ClothChainDeclarationPlan? Solve(FeModel feModel, List<FeModel.BoneChain> chains,
            Func<string, bool> reparents)
        {
            // A joint the original records as a hierarchy root whose parent bone is a control node stays in its chain.
            for (var i = 0; i < joints.Count; i++)
            {
                var node = joints[i].Joint.Node;
                keepInChain[i] = feModel.HasCompiledSkelParents && node < feModel.SkelParents.Length
                    && feModel.SkelParents[node] < 0 && reparents(joints[i].Joint.Name);
            }

            if (WalksNaturally())
            {
                return null;
            }

            Reset();
            if (!Search())
            {
                return null;
            }

            var plan = new ClothChainDeclarationPlan();
            foreach (var index in preDeclared)
            {
                plan.PreDeclared.Add((joints[index].Joint.Name, joints[index].Joint.Node));
            }

            foreach (var index in walkOrder)
            {
                var chain = chains[joints[index].Chain];
                if (!plan.Walk.TryGetValue(chain, out var walk))
                {
                    // A chain declares its root first.
                    if (chain.Joints.Count == 0 || joints[index].Joint.Node != chain.Joints[0].Node)
                    {
                        return null;
                    }

                    walk = [];
                    plan.Walk[chain] = walk;
                    plan.Chains.Add(chain);
                }

                walk.Add(joints[index].Joint);
            }

            return plan.Chains.Count == chains.Count ? plan : null;
        }

        // Whether the reconstructed order, with nothing declared ahead, already reproduces the node order.
        private bool WalksNaturally()
        {
            Reset();
            for (var index = 0; index < joints.Count; index++)
            {
                if (!StartJoint(index))
                {
                    return false;
                }
            }

            return TakeDeferredNodes(null) && remaining == 0;
        }

        private void Reset()
        {
            Array.Clear(lanePos);
            Array.Clear(walked);
            Array.Clear(tookNode);
            Array.Clear(nodeTaken);
            Array.Clear(declaredNode);
            preDeclared.Clear();
            walkOrder.Clear();
            started = false;
            currentChain = -1;
            expansions = 0;
            remaining = 0;
            Array.Clear(chainPending);
            foreach (var (chain, _, _) in joints)
            {
                chainPending[chain]++;
            }

            foreach (var lane in lanes)
            {
                remaining += lane.Count;
            }
        }

        private bool TakeHead(int node)
        {
            var lane = lanes[bands[node]];
            if (lanePos[bands[node]] >= lane.Count || lane[lanePos[bands[node]]] != node)
            {
                return false;
            }

            lanePos[bands[node]]++;
            remaining--;
            return true;
        }

        private void ReturnHead(int node)
        {
            lanePos[bands[node]]--;
            remaining++;
        }

        // The compiler's first pass: a narrow joint creates its node and then its ring, a wide one only its ring, and a
        // bone already created only its ring.
        private bool StartJoint(int index)
        {
            var (chain, joint, rings) = joints[index];
            if (walked[index] || (currentChain >= 0 && chain != currentChain && !ChainFinished(currentChain)))
            {
                return false;
            }

            if (joint.ParentNode >= 0 && ParentPending(chain, joint.ParentNode))
            {
                return false;
            }

            var takesNode = !deferred.Contains(joint.Node) && !nodeTaken[joint.Node]
                && !declaredNode[joint.Node];
            if (takesNode && !TakeHead(joint.Node))
            {
                return false;
            }

            var taken = 0;
            foreach (var ring in rings)
            {
                if (!TakeHead(ring))
                {
                    for (var i = taken - 1; i >= 0; i--)
                    {
                        ReturnHead(rings[i]);
                    }

                    if (takesNode)
                    {
                        ReturnHead(joint.Node);
                    }

                    return false;
                }

                taken++;
            }

            tookNode[index] = takesNode;
            nodeTaken[joint.Node] |= takesNode;
            walked[index] = true;
            chainPending[chain]--;
            walkOrder.Add(index);
            started = true;
            currentChain = chain;
            return true;
        }

        private void UndoJoint(int index)
        {
            var (_, joint, rings) = joints[index];
            for (var i = rings.Count - 1; i >= 0; i--)
            {
                ReturnHead(rings[i]);
            }

            if (tookNode[index])
            {
                ReturnHead(joint.Node);
                nodeTaken[joint.Node] = false;
                tookNode[index] = false;
            }

            walked[index] = false;
            chainPending[joints[index].Chain]++;
            walkOrder.RemoveAt(walkOrder.Count - 1);
        }

        // A joint is walked only after every declaration of its parent in the same chain.
        private bool ParentPending(int chain, int parentNode)
        {
            if (!occurrences.TryGetValue(parentNode, out var list))
            {
                return false;
            }

            foreach (var other in list)
            {
                if (joints[other].Chain == chain && !walked[other])
                {
                    return true;
                }
            }

            return false;
        }

        // The compiler's second pass: the nodes of the wide joints, in walk order.
        private bool TakeDeferredNodes(List<int>? taken)
        {
            foreach (var index in walkOrder)
            {
                var node = joints[index].Joint.Node;
                if (!deferred.Contains(node) || nodeTaken[node] || declaredNode[node])
                {
                    continue;
                }

                if (!TakeHead(node))
                {
                    return false;
                }

                nodeTaken[node] = true;
                taken?.Add(node);
            }

            return true;
        }

        private void ReturnDeferredNodes(List<int> taken)
        {
            for (var i = taken.Count - 1; i >= 0; i--)
            {
                ReturnHead(taken[i]);
                nodeTaken[taken[i]] = false;
            }
        }

        private bool ChainFinished(int chain) => chainPending[chain] == 0;

        private bool Search()
        {
            if (walkOrder.Count == joints.Count)
            {
                var taken = new List<int>();
                if (TakeDeferredNodes(taken) && remaining == 0)
                {
                    return true;
                }

                ReturnDeferredNodes(taken);
                return false;
            }

            if (++expansions > ExpansionBudget)
            {
                return false;
            }

            // The next joint of the reconstructed order is tried first.
            var natural = -1;
            for (var index = 0; index < joints.Count; index++)
            {
                if (!walked[index])
                {
                    natural = index;
                    break;
                }
            }

            if (natural >= 0 && TryStep(natural))
            {
                return true;
            }

            for (var band = 0; band < lanes.Count; band++)
            {
                if (lanePos[band] >= lanes[band].Count)
                {
                    continue;
                }

                var head = lanes[band][lanePos[band]];
                foreach (var index in owners.GetValueOrDefault(head, []))
                {
                    if (index != natural && TryStep(index))
                    {
                        return true;
                    }
                }

                if (started || !pureBands.Contains(band) || !CanPreDeclare(head) || !TakeHead(head))
                {
                    continue;
                }

                declaredNode[head] = true;
                preDeclared.Add(occurrences[head][0]);
                if (Search())
                {
                    return true;
                }

                preDeclared.RemoveAt(preDeclared.Count - 1);
                declaredNode[head] = false;
                ReturnHead(head);
            }

            // A declaration that creates no node heads no band, so it is offered directly.
            for (var index = 0; index < joints.Count; index++)
            {
                if (index == natural || walked[index] || joints[index].Rings.Count > 0
                    || (!nodeTaken[joints[index].Joint.Node] && !declaredNode[joints[index].Joint.Node]
                        && !deferred.Contains(joints[index].Joint.Node)))
                {
                    continue;
                }

                if (TryStep(index))
                {
                    return true;
                }
            }

            return false;
        }

        // Walks joint index next and searches on from there, undoing the step where that fails.
        private bool TryStep(int index)
        {
            var previousChain = currentChain;
            var wasStarted = started;
            if (!StartJoint(index))
            {
                return false;
            }

            if (Search())
            {
                return true;
            }

            UndoJoint(index);
            currentChain = previousChain;
            started = wasStarted;
            return false;
        }

        // A node can be declared ahead of the chains when every declaration of its bone allows it and its band is pure.
        private bool CanPreDeclare(int node)
        {
            if (nodeTaken[node] || declaredNode[node] || deferred.Contains(node)
                || !occurrences.TryGetValue(node, out var list))
            {
                return false;
            }

            foreach (var index in list)
            {
                if (walked[index] || keepInChain[index])
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// A chain joint's bone declared as its own <c>ClothNode</c> ahead of the chains, every other value at its neutral
    /// default, which claims only the node's creation index.
    /// </summary>
    private static KVObject MakeClothChainJointDeclaration(FeModel feModel, string boneName, int node)
    {
        return BuildClothNode(new ClothNodeFields
        {
            Name = boneName,
            RootBone = boneName,
            CollisionMask = feModel.GetNodeCollisionMask(node),
            IsStaticNode = node < feModel.StaticNodeCount,
            AllowRotation = feModel.AllowsRotation(node),
        });
    }
}
