using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>
    /// The declaration order that reproduces a chain-phase model's compiled control-node order: the
    /// chain joints declared as their own <c>ClothNode</c> ahead of the chains, and the order the chains
    /// and their joints are walked in afterwards.
    /// </summary>
    private sealed class ClothChainDeclarationPlan
    {
        public List<(string Name, int Node)> PreDeclared { get; } = [];
        public List<FeModel.BoneChain> Chains { get; } = [];
        public Dictionary<FeModel.BoneChain, List<FeModel.BoneChainJoint>> Walk { get; } = [];
    }

    /// <summary>
    /// Groups the control nodes into the bands the compiler's node sort leaves contiguous: the static /
    /// rotation-locked / position-driven blocks, subdivided by the constraint-graph rank. Returns null
    /// when the recomputed key is not ordered the way the compiled node list is, which means the shipped
    /// arrays no longer describe the graph the sort ranked over.
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

        // An element or rod every one of whose members is static is dropped before the walk, so it
        // makes no neighbours - but its members are still placed, at the rank the walk ended on.
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

        // The two passes after the walk place what it did not reach: a node any element or rod names
        // takes the rank the walk ended on, and only a node nothing names at all keeps its seeded
        // int.MaxValue - which is one real band of its own, ordered by creation index like any other.
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
    /// Reproduces the compiled control-node order by choosing which chain joints are declared ahead of
    /// the chains and in what order the chains are then walked. Returns null when today's declaration
    /// order already reproduces it, when the bands cannot be read, or when no declaration order does.
    /// </summary>
    private static ClothChainDeclarationPlan? TryPlanClothChainDeclarations(FeModel feModel,
        List<FeModel.BoneChain> chains, Func<string, bool> reparents)
    {
        if (chains.Count == 0 || ClothNodeBands(feModel) is not { } bands)
        {
            return null;
        }

        // One entry per joint DECLARATION, not per joint node: several chains can declare the same bone,
        // and each of them extrudes a ring of its own that the shared node is created only once for.
        var joints = new List<(int Chain, FeModel.BoneChainJoint Joint, List<int> Rings)>();
        var occurrences = new Dictionary<int, List<int>>();
        var jointNodes = new HashSet<int>();
        var ringNodes = new HashSet<int>();

        // A joint two rings wide or wider creates its node in the compiler's second pass, after every
        // chain's rings; anything narrower creates it before its own ring.
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

                if (!occurrences.TryGetValue(joint.Node, out var list))
                {
                    occurrences[joint.Node] = list = [];
                }

                list.Add(joints.Count);
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

        // Nodes some other declaration creates hold the positions they are in, so the chains can only be
        // reordered where their own nodes fill an unbroken run of a band: a run is permuted within itself
        // and nothing crosses it. A band a foreign node breaks carries an interleaving this cannot see.
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

        // A joint narrower than two rings creates its node immediately before its own rings, so within a
        // band the order of two such joints and the order of their rings have to agree. Where they
        // disagree the joint nodes came from somewhere else - a back-solved proxy sheet promotes the same
        // bones and numbers them its own way - and none of the creation order modelled here describes it.
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

    // Walks the creation order the compiler would build from a candidate declaration order, one node at
    // a time, and only ever places the next unplaced node of a band - which is what keeps every band in
    // the order the compiled file has it, so a completed walk reproduces the compiled node order.
    private sealed class ClothChainOrderSolver
    {
        const int ExpansionBudget = 50000;

        readonly List<(int Chain, FeModel.BoneChainJoint Joint, List<int> Rings)> joints;
        readonly Dictionary<int, List<int>> occurrences;
        readonly Dictionary<int, List<int>> owners = [];
        readonly HashSet<int> deferred;
        readonly HashSet<int> pureBands;
        readonly List<List<int>> lanes;
        readonly int[] bands;
        readonly int[] lanePos;
        readonly bool[] walked;
        readonly bool[] tookNode;
        readonly bool[] nodeTaken;
        readonly bool[] declaredNode;
        readonly bool[] keepInChain;
        readonly int[] chainPending;
        readonly List<int> preDeclared = [];
        readonly List<int> walkOrder = [];
        int remaining;
        int expansions;
        bool started;
        int currentChain = -1;

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

            foreach (var lane in lanes)
            {
                remaining += lane.Count;
            }

            void Own(int node, int index)
            {
                if (!owners.TryGetValue(node, out var list))
                {
                    owners[node] = list = [];
                }

                if (!list.Contains(index))
                {
                    list.Add(index);
                }
            }
        }

        public ClothChainDeclarationPlan? Solve(FeModel feModel, List<FeModel.BoneChain> chains,
            Func<string, bool> reparents)
        {
            // A chain never writes a parent onto its joint node, but a ClothNode over a bone whose own
            // PARENT bone is a control node is parented to it, so a joint the original records as a
            // hierarchy ROOT cannot be declared ahead of its chain without inventing an m_SkelParents
            // entry. The search works around those rather than giving up on the model.
            for (var i = 0; i < joints.Count; i++)
            {
                var node = joints[i].Joint.Node;
                keepInChain[i] = feModel.HasCompiledSkelParents && node < feModel.SkelParents.Length
                    && feModel.SkelParents[node] < 0 && reparents(joints[i].Joint.Name);
            }

            if (WalksNaturally(chains))
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
                    // A chain declares its root first: every other joint names a joint_parent that has to
                    // resolve to a joint already declared above it.
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

        // The order the exporter emits today: no joint declared ahead of its chain, chains and joints in
        // the order the chain reconstruction built them, which is the order this list was built in.
        bool WalksNaturally(List<FeModel.BoneChain> chains)
        {
            Reset();
            for (var index = 0; index < joints.Count; index++)
            {
                if (!StartJoint(index))
                {
                    return false;
                }
            }

            return TakeDeferredNodes([]) && remaining == 0;
        }

        void Reset()
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

        bool TakeHead(int node)
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

        void ReturnHead(int node)
        {
            lanePos[bands[node]]--;
            remaining++;
        }

        // The compiler's first pass over the chains: a joint narrower than two rings creates its own node
        // and then its ring, a joint two rings wide or wider creates only its ring, and a bone another
        // declaration already created the node for creates only the ring it extruded itself.
        bool StartJoint(int index)
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

        void UndoJoint(int index)
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

        // A joint can only be walked once every declaration of its chain parent in the same chain has
        // been, which is what makes the emitted joint_parent resolve to a joint already above it.
        bool ParentPending(int chain, int parentNode)
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

        // The compiler's second pass: the node of every joint two rings wide or wider, in the order the
        // walk reached that joint. Nothing branches here - the second pass follows the same walk the
        // first did - so this is a check on a completed walk, not a step of the search.
        bool TakeDeferredNodes(List<int> taken)
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
                taken.Add(node);
            }

            return true;
        }

        void ReturnDeferredNodes(List<int> taken)
        {
            for (var i = taken.Count - 1; i >= 0; i--)
            {
                ReturnHead(taken[i]);
                nodeTaken[taken[i]] = false;
            }
        }

        bool ChainFinished(int chain) => chainPending[chain] == 0;

        bool Search()
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

            // Several walks can reproduce one node order, and the one closest to the order the chain
            // reconstruction built is the one every other emitted value is already keyed to - so the
            // next joint of that order is tried before anything else is.
            var natural = -1;
            for (var index = 0; index < joints.Count; index++)
            {
                if (!walked[index])
                {
                    natural = index;
                    break;
                }
            }

            if (natural >= 0)
            {
                var previousChain = currentChain;
                var wasStarted = started;
                if (StartJoint(natural))
                {
                    if (Search())
                    {
                        return true;
                    }

                    UndoJoint(natural);
                    currentChain = previousChain;
                    started = wasStarted;
                }
            }

            for (var band = 0; band < lanes.Count; band++)
            {
                if (lanePos[band] >= lanes[band].Count)
                {
                    continue;
                }

                // The next node of a band is the first node of whichever declaration creates it, so every
                // declaration that node belongs to is a candidate for the next step of the walk.
                var head = lanes[band][lanePos[band]];
                foreach (var index in owners.GetValueOrDefault(head, []))
                {
                    if (index == natural)
                    {
                        continue;
                    }

                    var previousChain = currentChain;
                    var wasStarted = started;
                    if (!StartJoint(index))
                    {
                        continue;
                    }

                    if (Search())
                    {
                        return true;
                    }

                    UndoJoint(index);
                    currentChain = previousChain;
                    started = wasStarted;
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

            // A declaration that creates no node of its own - a joint declared ahead of the chains, or a
            // bone whose node and ring another declaration already made - is not the head of any band, so
            // the walk has to be offered it directly.
            for (var index = 0; index < joints.Count; index++)
            {
                if (index == natural || walked[index] || joints[index].Rings.Count > 0
                    || (!nodeTaken[joints[index].Joint.Node] && !declaredNode[joints[index].Joint.Node]
                        && !deferred.Contains(joints[index].Joint.Node)))
                {
                    continue;
                }

                var previousChain = currentChain;
                var wasStarted = started;
                if (!StartJoint(index))
                {
                    continue;
                }

                if (Search())
                {
                    return true;
                }

                UndoJoint(index);
                currentChain = previousChain;
                started = wasStarted;
            }

            return false;
        }

        // A joint whose node the chains create can be declared ahead of them instead, which claims the
        // creation index for its name. Every declaration of that bone has to allow it, and the band has
        // to be the chains' own: a foreign node in it is created wherever ITS declaration sits, which a
        // node moved to the head of the cloth folder would be reordered against.
        bool CanPreDeclare(int node)
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
    /// Declares a chain joint's bone as its own cloth node ahead of the <c>ClothChain</c>, which claims
    /// the control-node creation index for that name; the chain then reuses the node and only appends
    /// the joint's ring nodes. Every value the chain joint itself writes is left at its neutral default
    /// so that only the creation index changes.
    /// </summary>
    private static KVObject MakeClothChainJointDeclaration(FeModel feModel, string boneName, int node)
    {
        var layers = ClothNodeCollisionLayers(feModel.GetNodeCollisionMask(node));

        return MakeNode("ClothNode",
            ("name", boneName),
            ("origin", ToKVArray(Vector3.Zero)),
            ("angles", ToKVArray(Vector3.Zero)),
            ("cloth_node_root_bone", boneName),
            ("has_stray_radius", false),
            ("has_world_collision", false),
            ("cloth_collision_layer0", layers.Layer0),
            ("cloth_collision_layer1", layers.Layer1),
            ("cloth_collision_layer2", layers.Layer2),
            ("cloth_collision_layer3", layers.Layer3),
            ("transform_alignment", 0),
            ("node_base_y1", string.Empty),
            ("node_base_x1", string.Empty),
            ("node_base_y0", string.Empty),
            ("node_base_x0", string.Empty),
            ("lock_translation", false),
            ("gravity_z", 1.0f),
            ("goal_strength", 0.0f),
            ("goal_damping", 0.0f),
            ("mass", 1.0f),
            ("friction", 0.0f),
            ("stray_radius", 0.0f),
            ("stray_radius_relaxation_factor", 1.0f),
            ("collision_radius", 0.0f),
            ("is_static_node", node < feModel.StaticNodeCount),
            ("allow_rotation", feModel.AllowsRotation(node)));
    }
}
