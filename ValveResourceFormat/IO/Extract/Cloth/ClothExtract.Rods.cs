using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    // How far a cluster band's maximum has to sit from the pair's rest length before it is read as summed stray radii
    private const float ClusterRestTolerance = 1e-3f;

    // How closely solved member radii have to reproduce every band of a clique, relative to the band
    private const float ClusterRadiusTolerance = 1e-4f;

    // Bounds on the clique search: larger components, or ones needing more steps, yield no cliques
    private const int MaxCliqueComponentNodes = 64;
    private const int MaxCliqueSearchSteps = 1 << 16;

    /// <summary>
    /// A <c>ClothSpring</c> between two named nodes with explicit lengths; its stiffness compiles to the rod's relaxation
    /// factor, and its weight is left at the default.
    /// </summary>
    private static KVObject MakeClothSpring(string name, string n0, string n1, float minLength, float maxLength,
        float stiffness, int extraIterations = 0)
    {
        var kv = MakeNode("ClothSpring",
            ("name", name),
            ("cloth_node_0", n0),
            ("cloth_node_1", n1),
            ("stiffness", stiffness),
            ("enable_advanced_parameters", true),
            ("is_length_explicit", true),
            ("min_length", minLength),
            ("max_length", maxLength));

        if (extraIterations != 0)
        {
            kv.Add("extra_iterations", extraIterations);
        }

        return kv;
    }

    /// <summary>
    /// Declares the rods on one node pair as springs: one spring carrying the extra copies as iterations where every copy is
    /// identical, and one spring per copy otherwise.
    /// </summary>
    private static void AddPairSprings(KVObject softbodyChildren, string label, string name0, string name1, List<FeModel.Rod> rods)
    {
        var first = rods[0];
        if (rods.Count > 1 && rods.TrueForAll(rod => rod.MinDist == first.MinDist && rod.MaxDist == first.MaxDist
            && rod.RelaxationFactor == first.RelaxationFactor))
        {
            softbodyChildren.Add(MakeClothSpring(label, name0, name1, first.MinDist, first.MaxDist, first.RelaxationFactor,
                extraIterations: rods.Count - 1));
            return;
        }

        for (var copy = 0; copy < rods.Count; copy++)
        {
            var rod = rods[copy];
            softbodyChildren.Add(MakeClothSpring(copy == 0 ? label : $"{label}_{copy}", name0, name1, rod.MinDist, rod.MaxDist,
                rod.RelaxationFactor));
        }
    }

    /// <summary>
    /// A <c>ClothSelfCollisionCluster</c>, which compiles one rod per member pair from the summed member radii and adds no
    /// source element. Without per-member radii the pair's length is split evenly.
    /// </summary>
    internal static KVObject MakeClothSelfCollisionCluster(string name, List<string> members, float radius,
        float strayRadius, float[]? stiffness = null, float[]? radii = null, float[]? strayRadii = null)
    {
        static KVObject MakeJoint(string jointName, float jointStiffness, float jointRadius, float jointStrayRadius)
        {
            var joint = KVObject.Collection();
            joint.Add("joint_name", jointName);
            joint.Add("collision_radius", jointRadius);
            joint.Add("stray_radius", jointStrayRadius);
            joint.Add("stiffness", jointStiffness);
            return joint;
        }

        var joints = KVObject.Array();
        for (var i = 0; i < members.Count; i++)
        {
            joints.Add(MakeJoint(members[i], stiffness is not null && i < stiffness.Length ? stiffness[i] : 1.0f,
                radii is not null && i < radii.Length ? radii[i] : radius,
                strayRadii is not null && i < strayRadii.Length ? strayRadii[i] : strayRadius));
        }

        var attrs = KVObject.Collection();
        AddColumn(attrs, "joint_name", "Joint Name", true, 0).Add("default", string.Empty);
        AddColumn(attrs, "stiffness", "Stiffness", true, 4).Add("default", 1f);
        AddColumn(attrs, "stray_radius", "Max Range/Radius", true, 20).Add("default", 2f);
        AddColumn(attrs, "collision_radius", "Collision Radius", true, 27).Add("default", 2f);

        return MakeNode("ClothSelfCollisionCluster",
            ("name", name),
            ("algorithm", 0),
            ("chain", MakeChainData(joints, attrs, version: 0)));
    }

    // TODO: The springs here, the chains and the proxy sheet can each re-declare the same span, re-exporting more rods
    // than the original had.
    private static void AddClothProxySprings(KVObject softbodyChildren, ClothReconstruction cloth,
        List<ClothProxyFile> proxies, HashSet<int> chainJointNodes,
        HashSet<int> authoredClothNodes, Dictionary<int, string> freeClothNodeNames,
        HashSet<(int, int)> derivedRods, Dictionary<int, string> proxyNodeNames)
    {
        var riskyNodes = new HashSet<int>();
        foreach (var (_, _, proxyMesh) in proxies)
        {
            if (proxyMesh.IsDropRisk)
            {
                riskyNodes.UnionWith(proxyMesh.NodeIndices);
            }
        }

        string? ResolveName(int node)
        {
            if (node < 0 || node >= cloth.Fe.CtrlNames.Length)
            {
                return null;
            }

            var name = cloth.Fe.CtrlNames[node];
            if (FeModel.IsProxyNodeName(name))
            {
                return proxyNodeNames.GetValueOrDefault(node) ?? freeClothNodeNames.GetValueOrDefault(node);
            }

            return authoredClothNodes.Contains(node) ? name : null;
        }

        foreach (var (edge, rods) in cloth.RodsByPair)
        {
            if (riskyNodes.Contains(edge.Item1) || riskyNodes.Contains(edge.Item2) || derivedRods.Contains(edge)
                || chainJointNodes.Contains(edge.Item1) || chainJointNodes.Contains(edge.Item2))
            {
                continue;
            }

            var rod = rods[0];
            if (ResolveName(rod.NodeA) is not { } name0 || ResolveName(rod.NodeB) is not { } name1)
            {
                continue;
            }

            AddPairSprings(softbodyChildren, $"rod_{edge.Item1}_{edge.Item2}", name0, name1, rods);
        }
    }

    /// <summary>
    /// Re-declares the rods the chains do not rebuild, as springs or as two-member clusters, and returns the pairs it
    /// declared.
    /// </summary>
    internal static HashSet<(int, int)> AddClothChainSurplusRods(KVObject softbodyChildren, ClothReconstruction cloth,
        List<BoneChain> chains)
    {
        var declaredPairs = new HashSet<(int, int)>();
        var controlNames = cloth.Fe.CtrlNames;

        var chainJoints = ChainJointNodes(chains);

        var chainRingNodes = chains.SelectMany(static chain => chain.Joints)
            .SelectMany(static joint => joint.RingNodes)
            .ToHashSet();

        var ringOwner = RingOwners(chains);

        bool Nameable(int node, bool tie)
            => chainJoints.Contains(node) || (tie && chainRingNodes.Contains(node));

        var occurrence = new Dictionary<(int, int), int>();
        var surplus = cloth.GetUngeneratedRods(chains, cloth.HasChainStiffnessRods(chains));
        var clusterTies = ClusterTiesBesideChainSpans(cloth, surplus);
        var ringTies = RingClusterTies(cloth, surplus, ringOwner);
        var cliquePairs = AddRingClusterCliques(softbodyChildren, cloth, ringOwner);
        declaredPairs.UnionWith(cliquePairs);
        var rodCounts = RodCountsByPair(cloth).Entries;
        foreach (var rod in surplus)
        {
            if (rod.NodeA >= controlNames.Length || rod.NodeB >= controlNames.Length)
            {
                continue;
            }

            var pair = RodPair(rod);
            if (cliquePairs.Contains(pair) && rod.IsBanded)
            {
                continue;
            }

            var tie = clusterTies.Contains(pair) || (ringTies.Contains(pair) && rod.IsBanded);
            if (!Nameable(rod.NodeA, tie) || !Nameable(rod.NodeB, tie))
            {
                continue;
            }

            var name0 = controlNames[rod.NodeA];
            var name1 = controlNames[rod.NodeB];
            if ((FeModel.IsProxyNodeName(name0) && !chainRingNodes.Contains(rod.NodeA))
                || (FeModel.IsProxyNodeName(name1) && !chainRingNodes.Contains(rod.NodeB)))
            {
                continue;
            }

            declaredPairs.Add(pair);
            if (tie)
            {
                softbodyChildren.Add(MakeClothSelfCollisionCluster(
                    NodeNameSafe($"cluster_{name0}_{name1}"), [name0, name1],
                    rod.MinDist / 2f, rod.MaxDist / 2f));
                continue;
            }

            if (IsUnrecordedClusterRod(cloth, rod, rodCounts) || IsUnrecordedSpanCopy(cloth, rod, rodCounts))
            {
                softbodyChildren.Add(MakeClothSelfCollisionCluster(
                    NodeNameSafe($"cluster_{name1}_{name0}"), [name1, name0],
                    rod.MinDist / 2f, rod.MaxDist / 2f));
                continue;
            }

            var copy = occurrence.GetValueOrDefault((rod.NodeA, rod.NodeB));
            occurrence[(rod.NodeA, rod.NodeB)] = copy + 1;
            var springLabel = NodeNameSafe(copy == 0
                ? $"rod_{name0}_{name1}" : $"rod_{name0}_{name1}_{copy}");
            softbodyChildren.Add(MakeClothSpring(springLabel, name0, name1, rod.MinDist, rod.MaxDist,
                rod.RelaxationFactor));
        }

        return declaredPairs;
    }

    /// <summary>
    /// Declares, on the ring node names, the parent cross links a chain joint's own repeat no longer copies
    /// (<see cref="BoneChainJoint.CrossLinkSurplus"/>), one spring per pair from the pair's first record, and returns
    /// the pairs.
    /// </summary>
    internal static HashSet<(int, int)> AddClothChainCrossLinkSprings(KVObject softbodyChildren, ClothReconstruction cloth,
        List<BoneChain> chains)
    {
        var declared = new HashSet<(int, int)>();
        var names = cloth.Fe.CtrlNames;
        var rodByPair = FirstRodByPair(cloth);

        foreach (var joint in chains.SelectMany(static chain => chain.Joints))
        {
            foreach (var (a, b, copies) in joint.CrossLinkSurplus)
            {
                var pair = ClothReconstruction.UnorderedPair(a, b);
                if (a < 0 || b < 0 || a >= names.Length || b >= names.Length || copies < 1
                    || !rodByPair.TryGetValue(pair, out var rod) || !declared.Add(pair))
                {
                    continue;
                }

                softbodyChildren.Add(MakeClothSpring(NodeNameSafe($"cross_{names[a]}_{names[b]}"), names[a], names[b],
                    rod.MinDist, rod.MaxDist, rod.RelaxationFactor, copies - 1));
            }
        }

        return declared;
    }

    /// <summary>
    /// Declares the rods between joints of different independent chains as two-member clusters. A rod without the
    /// cluster's fixed relaxation and weight is left out.
    /// </summary>
    private static void AddClothChainSurplusClusters(KVObject softbodyChildren, ClothReconstruction cloth,
        List<BoneChain> chains)
    {
        var controlNames = cloth.Fe.CtrlNames;
        var chainJoints = ChainJointNodes(chains);
        var rodCounts = RodCountsByPair(cloth).Entries;

        var surplus = cloth.GetUngeneratedRods(chains);
        var clusterTies = ClusterTiesBesideChainSpans(cloth, surplus);
        AddRingClusterCliques(softbodyChildren, cloth, RingOwners(chains));
        foreach (var rod in surplus)
        {
            if (rod.NodeA >= controlNames.Length || rod.NodeB >= controlNames.Length
                || !chainJoints.Contains(rod.NodeA) || !chainJoints.Contains(rod.NodeB))
            {
                continue;
            }

            var pair = RodPair(rod);
            if ((rodCounts.GetValueOrDefault(pair) > 1 && !clusterTies.Contains(pair)) || !HasClusterSignature(rod))
            {
                continue;
            }

            var name0 = controlNames[rod.NodeA];
            var name1 = controlNames[rod.NodeB];
            if (FeModel.IsProxyNodeName(name0) || FeModel.IsProxyNodeName(name1))
            {
                continue;
            }

            softbodyChildren.Add(MakeClothSelfCollisionCluster(
                NodeNameSafe($"cluster_{name0}_{name1}"), [name0, name1],
                rod.MinDist / 2f, rod.MaxDist / 2f));
        }
    }

    /// <summary>A composed node name with every <c>$</c> dropped: the compiler silently discards a node whose name has one.</summary>
    private static string NodeNameSafe(string name) => name.Replace("$", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Whether a surplus rod is a two-member <c>ClothSelfCollisionCluster</c>'s: the only rod on its pair, at the cluster's
    /// fixed relaxation and weight, with no source element on the pair and a length other than the rest distance.
    /// </summary>
    private static bool IsUnrecordedClusterRod(ClothReconstruction cloth, FeModel.Rod rod, Dictionary<(int, int), int> rodCounts)
    {
        if (!HasClusterSignature(rod) || cloth.IsSourceSpring(rod.NodeA, rod.NodeB))
        {
            return false;
        }

        var poses = cloth.Fe.InitPosePositions;
        if (rodCounts.GetValueOrDefault(RodPair(rod)) != 1 || rod.NodeA >= poses.Length || rod.NodeB >= poses.Length)
        {
            return false;
        }

        var rest = Vector3.Distance(poses[rod.NodeA], poses[rod.NodeB]);
        return rod.IsBanded || MathF.Abs(rod.MaxDist - rest) > MathF.Max(1e-3f, 1e-4f * rest);
    }

    /// <summary>
    /// Declares a <c>ClothSelfCollisionCluster</c> for every clique of three or more ring nodes of at least two joints whose
    /// every pair carries one banded cluster-signature rod off its rest length, where the bands solve as per-member radii.
    /// Returns the pairs the clusters cover.
    /// </summary>
    internal static HashSet<(int, int)> AddRingClusterCliques(KVObject softbodyChildren, ClothReconstruction cloth, Dictionary<int, int> ringOwner)
    {
        var covered = new HashSet<(int, int)>();
        var poses = cloth.Fe.InitPosePositions;
        var bandedOnPair = new Dictionary<(int, int), int>();
        foreach (var rod in cloth.Fe.Rods)
        {
            if (rod.IsBanded && !cloth.IsSurfaceFold(rod))
            {
                var key = RodPair(rod);
                bandedOnPair[key] = bandedOnPair.GetValueOrDefault(key) + 1;
            }
        }

        var band = new Dictionary<(int, int), (float Min, float Max)>();
        var neighbours = new Dictionary<int, HashSet<int>>();
        foreach (var rod in cloth.Fe.Rods)
        {
            var key = RodPair(rod);
            if (!rod.IsBanded || !HasClusterSignature(rod)
                || !ringOwner.ContainsKey(rod.NodeA) || !ringOwner.ContainsKey(rod.NodeB)
                || bandedOnPair.GetValueOrDefault(key) != 1 || rod.NodeA >= poses.Length || rod.NodeB >= poses.Length
                || MathF.Abs(rod.MaxDist - Vector3.Distance(poses[rod.NodeA], poses[rod.NodeB])) <= ClusterRestTolerance)
            {
                continue;
            }

            band[key] = (rod.MinDist, rod.MaxDist);
            ClothReconstruction.GetOrAdd(neighbours, key.Item1).Add(key.Item2);
            ClothReconstruction.GetOrAdd(neighbours, key.Item2).Add(key.Item1);
        }

        foreach (var clique in MaximalCliques(neighbours))
        {
            if (clique.Count < 3 || clique.Select(node => ringOwner[node]).Distinct().Count() < 2
                || SolveMemberRadii(clique, band) is not var (radii, strayRadii))
            {
                continue;
            }

            var names = clique.Select(node => cloth.Fe.CtrlNames[node]).ToList();
            softbodyChildren.Add(MakeClothSelfCollisionCluster(NodeNameSafe($"cluster_{string.Join("_", names)}"), names,
                radii[0], strayRadii[0], radii: radii, strayRadii: strayRadii));
            foreach (var a in clique)
            {
                foreach (var b in clique)
                {
                    if (a < b)
                    {
                        covered.Add((a, b));
                    }
                }
            }
        }

        return covered;
    }

    /// <summary>
    /// Maximal cliques of an undirected graph, each in ascending node order, in lexicographic order. Self-loops are
    /// ignored, and a connected component larger than <see cref="MaxCliqueComponentNodes"/> or whose search takes more than
    /// <see cref="MaxCliqueSearchSteps"/> steps yields none.
    /// </summary>
    private static List<List<int>> MaximalCliques(Dictionary<int, HashSet<int>> neighbours)
    {
        var cliques = new List<List<int>>();
        var seen = new HashSet<int>();
        foreach (var start in neighbours.Keys.Order())
        {
            if (!seen.Add(start))
            {
                continue;
            }

            var component = new List<int> { start };
            for (var i = 0; i < component.Count; i++)
            {
                foreach (var next in neighbours[component[i]])
                {
                    if (seen.Add(next))
                    {
                        component.Add(next);
                    }
                }
            }

            if (component.Count <= MaxCliqueComponentNodes && ComponentCliques(neighbours, component) is { } found)
            {
                cliques.AddRange(found);
            }
        }

        cliques.Sort(static (a, b) => CollectionsMarshal.AsSpan(a).SequenceCompareTo(CollectionsMarshal.AsSpan(b)));

        return cliques;
    }

    /// <summary>
    /// Maximal cliques of one connected component by Bron-Kerbosch with Tomita pivoting, or null when the search runs out
    /// of steps.
    /// </summary>
    private static List<List<int>>? ComponentCliques(Dictionary<int, HashSet<int>> neighbours, List<int> component)
    {
        var cliques = new List<List<int>>();
        var steps = 0;

        bool Adjacent(int a, int b) => a != b && neighbours[a].Contains(b);

        bool Extend(List<int> clique, HashSet<int> candidates, HashSet<int> excluded)
        {
            if (++steps > MaxCliqueSearchSteps)
            {
                return false;
            }

            if (candidates.Count == 0)
            {
                if (excluded.Count == 0)
                {
                    cliques.Add([.. clique.Order()]);
                }

                return true;
            }

            var pivot = candidates.Concat(excluded).MaxBy(u => candidates.Count(v => Adjacent(u, v)));
            foreach (var node in candidates.Where(v => !Adjacent(pivot, v)).ToList())
            {
                if (!Extend([.. clique, node], [.. candidates.Where(v => Adjacent(node, v))],
                    [.. excluded.Where(v => Adjacent(node, v))]))
                {
                    return false;
                }

                candidates.Remove(node);
                excluded.Add(node);
            }

            return true;
        }

        return Extend([], [.. component], []) ? cliques : null;
    }

    /// <summary>
    /// Per-member collision and stray radii that sum to every pair's band, read off one triangle and checked on every
    /// pair; null where no such split exists.
    /// </summary>
    private static (float[] Radii, float[] StrayRadii)? SolveMemberRadii(List<int> members,
        Dictionary<(int, int), (float Min, float Max)> band)
    {
        (float Min, float Max) Band(int a, int b) => band[ClothReconstruction.UnorderedPair(a, b)];

        var radii = new float[members.Count];
        var strayRadii = new float[members.Count];
        for (var i = 0; i < members.Count; i++)
        {
            var j = (i + 1) % members.Count;
            var k = (i + 2) % members.Count;
            var (minIj, maxIj) = Band(members[i], members[j]);
            var (minIk, maxIk) = Band(members[i], members[k]);
            var (minJk, maxJk) = Band(members[j], members[k]);
            radii[i] = (minIj + minIk - minJk) / 2f;
            strayRadii[i] = (maxIj + maxIk - maxJk) / 2f;
            if (radii[i] < 0f || strayRadii[i] < radii[i])
            {
                return null;
            }
        }

        for (var i = 0; i < members.Count; i++)
        {
            for (var j = i + 1; j < members.Count; j++)
            {
                var (min, max) = Band(members[i], members[j]);
                if (MathF.Abs(radii[i] + radii[j] - min) > ClusterRadiusTolerance * MathF.Max(1f, min)
                    || MathF.Abs(strayRadii[i] + strayRadii[j] - max) > ClusterRadiusTolerance * MathF.Max(1f, max))
                {
                    return null;
                }
            }
        }

        return (radii, strayRadii);
    }

    /// <summary>
    /// Whether a surplus rod is a second rigid copy of a span at its rest distance, with the cluster's fixed relaxation and
    /// weight and no source element on the pair, on a model that compiled <c>m_SkelParents</c>.
    /// </summary>
    internal static bool IsUnrecordedSpanCopy(ClothReconstruction cloth, FeModel.Rod rod, Dictionary<(int, int), int> rodCounts)
    {
        if (!cloth.HasCompiledSkelParents || !HasClusterSignature(rod) || rod.IsBanded
            || cloth.IsSourceSpring(rod.NodeA, rod.NodeB))
        {
            return false;
        }

        var poses = cloth.Fe.InitPosePositions;
        if (rod.NodeA >= poses.Length || rod.NodeB >= poses.Length)
        {
            return false;
        }

        var onPair = rodCounts.GetValueOrDefault(RodPair(rod));
        var rest = Vector3.Distance(poses[rod.NodeA], poses[rod.NodeB]);
        return onPair >= 2 && FeModel.Rod.IsAtRestLength(rod.MaxDist, rest);
    }

    /// <summary>
    /// The pairs whose single banded rod is a two-member cluster between ring nodes of two different chain joints, beside
    /// the chain's rigid span, with a maximum off the pair's rest length.
    /// </summary>
    private static HashSet<(int, int)> RingClusterTies(ClothReconstruction cloth, List<FeModel.Rod> surplus,
        Dictionary<int, int> ringOwner)
    {
        var ties = new HashSet<(int, int)>();
        if (ringOwner.Count == 0)
        {
            return ties;
        }

        var (entries, banded) = RodCountsByPair(cloth);
        var poses = cloth.Fe.InitPosePositions;
        foreach (var rod in surplus)
        {
            var key = RodPair(rod);
            if (!ringOwner.TryGetValue(rod.NodeA, out var ownerA)
                || !ringOwner.TryGetValue(rod.NodeB, out var ownerB) || ownerA == ownerB
                || entries.GetValueOrDefault(key) < 2 || banded.GetValueOrDefault(key) != 1
                || !rod.IsBanded || !HasClusterSignature(rod)
                || rod.NodeA >= poses.Length || rod.NodeB >= poses.Length)
            {
                continue;
            }

            if (FeModel.Rod.IsAtRestLength(rod.MaxDist, Vector3.Distance(poses[rod.NodeA], poses[rod.NodeB])))
            {
                continue;
            }

            ties.Add(key);
        }

        return ties;
    }

    /// <summary>
    /// The pairs whose one surplus rod is a banded cluster tie beside a chain span on the same pair: several rods, only
    /// that one banded, with the cluster's fixed relaxation and weight.
    /// </summary>
    private static HashSet<(int, int)> ClusterTiesBesideChainSpans(ClothReconstruction cloth, List<FeModel.Rod> surplus)
    {
        var (entries, banded) = RodCountsByPair(cloth);
        var surplusCounts = new Dictionary<(int, int), int>();
        foreach (var rod in surplus)
        {
            var key = RodPair(rod);
            surplusCounts[key] = surplusCounts.GetValueOrDefault(key) + 1;
        }

        var ties = new HashSet<(int, int)>();
        foreach (var rod in surplus)
        {
            var key = RodPair(rod);
            if (entries.GetValueOrDefault(key) > 1 && banded.GetValueOrDefault(key) == 1
                && surplusCounts[key] == 1 && rod.IsBanded && HasClusterSignature(rod))
            {
                ties.Add(key);
            }
        }

        return ties;
    }

    /// <summary>
    /// Re-declares each <see cref="SelfCollisionCluster"/> as a <c>ClothSelfCollisionCluster</c> and returns the
    /// member nodes, which the cluster registers on its own.
    /// </summary>
    private static HashSet<int> AddClothSelfCollisionClusters(KVObject softbodyChildren, ClothReconstruction cloth,
        HashSet<string> clothBones)
    {
        var covered = new HashSet<int>();
        var names = cloth.Fe.CtrlNames;
        var index = 0;
        foreach (var cluster in cloth.SelfCollisionClusters)
        {
            var members = new List<string>(cluster.Nodes.Length);
            foreach (var node in cluster.Nodes)
            {
                if (node < 0 || node >= names.Length || cloth.IsGeneratedNodeName(names[node]))
                {
                    members.Clear();
                    break;
                }

                members.Add(names[node]);
            }

            if (members.Count != cluster.Nodes.Length)
            {
                continue;
            }

            softbodyChildren.Add(MakeClothSelfCollisionCluster(
                $"cluster_{index.ToString(CultureInfo.InvariantCulture)}", members,
                cluster.MinDist / 2f, cluster.MaxDist / 2f, cluster.Stiffness));
            clothBones.UnionWith(members);
            covered.UnionWith(cluster.Nodes);
            index++;
        }

        return covered;
    }

    /// <summary>
    /// Re-declares the authored two-corner source elements (<see cref="FeModel.SourceSprings"/>) as springs named by
    /// control name, and returns their pairs.
    /// </summary>
    private static HashSet<(int, int)> AddClothSourceSprings(KVObject softbodyChildren, ClothReconstruction cloth,
        List<BoneChain> chains)
    {
        var emitted = new HashSet<(int, int)>();
        var names = cloth.Fe.CtrlNames;
        var rodByEdge = FirstRodByPair(cloth);

        foreach (var (a, b, copies) in cloth.GetAuthoredSourceSprings(chains))
        {
            var pair = ClothReconstruction.UnorderedPair(a, b);
            if (a < 0 || a >= names.Length || b < 0 || b >= names.Length || !rodByEdge.TryGetValue(pair, out var rod))
            {
                continue;
            }

            softbodyChildren.Add(MakeClothSpring($"spring_{a}_{b}", names[a], names[b], rod.MinDist,
                rod.MaxDist, rod.RelaxationFactor, copies - 1));
            emitted.Add(pair);
        }

        return emitted;
    }

    /// <summary>The node pair of <paramref name="rod"/>, lower node first.</summary>
    private static (int, int) RodPair(FeModel.Rod rod) => ClothReconstruction.UnorderedPair(rod.NodeA, rod.NodeB);

    private static Dictionary<(int, int), FeModel.Rod> FirstRodByPair(ClothReconstruction cloth) => LookupsOf(cloth).FirstRodByPair;

    /// <summary>Whether a rod carries the relaxation of 1 and weight of 0.5 every cluster rod compiles with.</summary>
    private static bool HasClusterSignature(FeModel.Rod rod) => rod.RelaxationFactor == 1f && rod.Weight0 == 0.5f;

    internal static RodPairCounts RodCountsByPair(ClothReconstruction cloth) => LookupsOf(cloth).RodCounts;

    private static HashSet<int> ChainJointNodes(IEnumerable<BoneChain> chains)
        => [.. chains.SelectMany(static chain => chain.Joints).Select(static joint => joint.Node)];

    /// <summary>The joint node that extruded each ring node of <paramref name="chains"/>.</summary>
    private static Dictionary<int, int> RingOwners(IEnumerable<BoneChain> chains)
    {
        var ringOwner = new Dictionary<int, int>();
        foreach (var joint in chains.SelectMany(static chain => chain.Joints))
        {
            foreach (var ring in joint.RingNodes)
            {
                ringOwner[ring] = joint.Node;
            }
        }

        return ringOwner;
    }
}
