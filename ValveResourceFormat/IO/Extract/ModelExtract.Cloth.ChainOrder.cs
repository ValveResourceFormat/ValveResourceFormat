using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
    /// <summary>
    /// The declaration order that reproduces a chain-phase model's compiled control-node order: the
    /// chain joints declared as their own <c>ClothNode</c> ahead of the chains, and the order the chains
    /// and their joints are walked in afterwards.
    /// </summary>
    sealed class ClothChainDeclarationPlan
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
    static int[]? ClothNodeBands(FeModel feModel)
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
    /// The nodes a <c>ClothChain</c> creates for one joint after the joint node itself, in creation
    /// order: the <c>$cc&lt;joint&gt;_Ctr</c> centre node and then the numbered ring nodes.
    /// </summary>
    static List<int> ClothChainRingNodes(FeModel feModel, string jointName)
    {
        var prefix = "$cc" + jointName + "_";
        var centre = -1;
        var rings = new List<(int Index, int Node)>();
        for (var node = 0; node < feModel.CtrlNames.Length; node++)
        {
            var name = feModel.CtrlNames[node];
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var suffix = name[prefix.Length..];
            if (suffix == "Ctr")
            {
                centre = node;
            }
            else if (int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                rings.Add((index, node));
            }
        }

        rings.Sort(static (a, b) => a.Index.CompareTo(b.Index));
        var ordered = new List<int>(rings.Count + 1);
        if (centre >= 0)
        {
            ordered.Add(centre);
        }

        foreach (var (_, node) in rings)
        {
            ordered.Add(node);
        }

        return ordered;
    }

    /// <summary>
    /// Reproduces the compiled control-node order by choosing which chain joints are declared ahead of
    /// the chains and in what order the chains are then walked. Returns null when today's declaration
    /// order already reproduces it, when the bands cannot be read, or when no declaration order does.
    /// </summary>
    static ClothChainDeclarationPlan? TryPlanClothChainDeclarations(FeModel feModel,
        List<FeModel.BoneChain> chains, Func<string, bool> reparents)
    {
        if (chains.Count == 0 || ClothNodeBands(feModel) is not { } bands)
        {
            return null;
        }

        var joints = new List<(int Chain, FeModel.BoneChainJoint Joint, List<int> Rings)>();
        var jointOfNode = new Dictionary<int, int>();
        for (var c = 0; c < chains.Count; c++)
        {
            foreach (var joint in chains[c].Joints)
            {
                if (joint.Node < 0 || joint.Node >= bands.Length || jointOfNode.ContainsKey(joint.Node))
                {
                    return null;
                }

                jointOfNode[joint.Node] = joints.Count;
                joints.Add((c, joint, ClothChainRingNodes(feModel, joint.Name)));
            }
        }

        var owned = new HashSet<int>(jointOfNode.Keys);
        foreach (var (_, _, rings) in joints)
        {
            foreach (var ring in rings)
            {
                if (!owned.Add(ring))
                {
                    return null;
                }
            }
        }

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

        // Only the chains' own nodes are placed here. A model that also carries nodes some other
        // declaration creates has an interleaving this cannot see, so the reordering would be a guess.
        if (owned.Count != feModel.NodeCount)
        {
            return null;
        }

        // A chain creates a joint immediately followed by its own rings, so within a band the order of
        // two joints and the order of their rings have to agree. Where they disagree the joint nodes
        // came from somewhere else - a back-solved proxy sheet promotes the same bones and numbers them
        // its own way - and none of the creation order modelled here describes that model.
        for (var i = 0; i < joints.Count; i++)
        {
            for (var j = i + 1; j < joints.Count; j++)
            {
                var (_, first, firstRings) = joints[i];
                var (_, second, secondRings) = joints[j];
                if (firstRings.Count == 0 || secondRings.Count == 0
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

        var solver = new ClothChainOrderSolver(joints, jointOfNode, lanes, bands);
        return solver.Solve(feModel, chains, reparents);
    }

    // Walks the creation order the compiler would build from a candidate declaration order, one node at
    // a time, and only ever places the next unplaced node of a band - which is what keeps every band in
    // the order the compiled file has it, so a completed walk reproduces the compiled node order.
    sealed class ClothChainOrderSolver
    {
        const int ExpansionBudget = 50000;

        readonly List<(int Chain, FeModel.BoneChainJoint Joint, List<int> Rings)> joints;
        readonly Dictionary<int, int> jointOfNode;
        readonly List<List<int>> lanes;
        readonly int[] bands;
        readonly int[] lanePos;
        readonly bool[] walked;
        readonly bool[] declared;
        readonly bool[] keepInChain;
        readonly int[] chainPending;
        readonly List<int> preDeclared = [];
        readonly List<int> walkOrder = [];
        int remaining;
        int expansions;
        bool started;
        int currentChain = -1;

        public ClothChainOrderSolver(List<(int Chain, FeModel.BoneChainJoint Joint, List<int> Rings)> joints,
            Dictionary<int, int> jointOfNode, List<List<int>> lanes, int[] bands)
        {
            this.joints = joints;
            this.jointOfNode = jointOfNode;
            this.lanes = lanes;
            this.bands = bands;
            lanePos = new int[lanes.Count];
            walked = new bool[joints.Count];
            declared = new bool[joints.Count];
            keepInChain = new bool[joints.Count];
            chainPending = new int[joints.Count == 0 ? 0 : joints[^1].Chain + 1];
            foreach (var lane in lanes)
            {
                remaining += lane.Count;
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
        // the order the chain reconstruction built them.
        bool WalksNaturally(List<FeModel.BoneChain> chains)
        {
            Reset();
            for (var c = 0; c < chains.Count; c++)
            {
                foreach (var joint in chains[c].Joints)
                {
                    if (!jointOfNode.TryGetValue(joint.Node, out var index) || !StartJoint(index))
                    {
                        return false;
                    }
                }
            }

            return remaining == 0;
        }

        void Reset()
        {
            Array.Clear(lanePos);
            Array.Clear(walked);
            Array.Clear(declared);
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

        // Consumes a joint and all of its ring nodes, which the chain creates as one block.
        bool StartJoint(int index)
        {
            var (chain, joint, rings) = joints[index];
            if (walked[index] || (currentChain >= 0 && chain != currentChain && !ChainFinished(currentChain)))
            {
                return false;
            }

            if (joint.ParentNode >= 0 && jointOfNode.TryGetValue(joint.ParentNode, out var parent)
                && joints[parent].Chain == chain && !walked[parent])
            {
                return false;
            }

            var takenJoint = false;
            if (!declared[index])
            {
                if (!TakeHead(joint.Node))
                {
                    return false;
                }

                takenJoint = true;
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

                    if (takenJoint)
                    {
                        ReturnHead(joint.Node);
                    }

                    return false;
                }

                taken++;
            }

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

            if (!declared[index])
            {
                ReturnHead(joint.Node);
            }

            walked[index] = false;
            chainPending[joints[index].Chain]++;
            walkOrder.RemoveAt(walkOrder.Count - 1);
        }

        bool ChainFinished(int chain) => chainPending[chain] == 0;

        bool Search()
        {
            if (remaining == 0 && walkOrder.Count == joints.Count)
            {
                return true;
            }

            if (++expansions > ExpansionBudget)
            {
                return false;
            }

            for (var band = 0; band < lanes.Count; band++)
            {
                if (lanePos[band] >= lanes[band].Count)
                {
                    continue;
                }

                var head = lanes[band][lanePos[band]];
                if (!jointOfNode.TryGetValue(head, out var index))
                {
                    continue;
                }

                var previousChain = currentChain;
                var wasStarted = started;
                if (StartJoint(index))
                {
                    if (Search())
                    {
                        return true;
                    }

                    UndoJoint(index);
                    currentChain = previousChain;
                    started = wasStarted;
                }

                if (started || walked[index] || declared[index] || keepInChain[index] || !TakeHead(head))
                {
                    continue;
                }

                declared[index] = true;
                preDeclared.Add(index);
                if (Search())
                {
                    return true;
                }

                preDeclared.RemoveAt(preDeclared.Count - 1);
                declared[index] = false;
                ReturnHead(head);
            }

            // A joint declared ahead of the chains contributes no node of its own when the walk reaches
            // it, so its ring block is not the head of any band until the walk gets there.
            for (var index = 0; index < joints.Count; index++)
            {
                if (!declared[index] || walked[index])
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
    }

    /// <summary>
    /// Declares a chain joint's bone as its own cloth node ahead of the <c>ClothChain</c>, which claims
    /// the control-node creation index for that name; the chain then reuses the node and only appends
    /// the joint's ring nodes. Every value the chain joint itself writes is left at its neutral default
    /// so that only the creation index changes.
    /// </summary>
    static KVObject MakeClothChainJointDeclaration(FeModel feModel, string boneName, int node)
        => MakeNode("ClothNode",
            ("name", boneName),
            ("origin", ToKVArray(Vector3.Zero)),
            ("angles", ToKVArray(Vector3.Zero)),
            ("cloth_node_root_bone", boneName),
            ("has_stray_radius", false),
            ("has_world_collision", false),
            ("cloth_collision_layer0", true),
            ("cloth_collision_layer1", true),
            ("cloth_collision_layer2", true),
            ("cloth_collision_layer3", true),
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
