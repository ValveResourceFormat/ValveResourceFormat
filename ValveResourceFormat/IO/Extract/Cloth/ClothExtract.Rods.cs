using System.Globalization;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>
    /// A <c>ClothSpring</c> between two named nodes with explicit lengths; its stiffness compiles to the rod's relaxation
    /// factor, and its weight is always the builder's default.
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
    /// A <c>ClothSelfCollisionCluster</c>, which compiles one rod per member pair from the summed member radii and adds no
    /// source element. Without per-member radii the pair's length is split evenly.
    /// </summary>
    internal static KVObject MakeClothSelfCollisionCluster(string name, List<string> members, float radius,
        float strayRadius, float[]? stiffness = null, float[]? radii = null, float[]? strayRadii = null)
    {
        KVObject MakeJoint(string jointName, float jointStiffness, float jointRadius, float jointStrayRadius)
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

        // The member table's schema defaults, read for any member row that omits a key.
        var attrs = KVObject.Collection();

        KVObject Attr(string key, string display, int uiOrder)
        {
            var attr = KVObject.Collection();
            attr.Add("display", display);
            attr.Add("show", true);
            attr.Add("ui_order", uiOrder);
            attrs.Add(key, attr);
            return attr;
        }

        Attr("joint_name", "Joint Name", 0).Add("default", string.Empty);
        Attr("stiffness", "Stiffness", 4).Add("default", 1f);
        Attr("stray_radius", "Max Range/Radius", 20).Add("default", 2f);
        Attr("collision_radius", "Collision Radius", 27).Add("default", 2f);

        var chainData = KVObject.Collection();
        chainData.Add("joints", joints);
        chainData.Add("attrs", attrs);
        chainData.Add("selection", KVObject.Array());
        chainData.Add("version", 0);

        return MakeNode("ClothSelfCollisionCluster",
            ("name", name),
            ("algorithm", 0),
            ("chain", chainData));
    }

    // TODO: some models re-export more rods than the original, from overlap between the springs emitted
    // here, the chains, and the proxy sheet all re-declaring the same span.
    private static void AddClothProxySprings(KVObject softbodyChildren, FeModel feModel,
        List<(string FileName, string Name, FeModel.ProxyMesh Proxy)> proxies, HashSet<int> chainJointNodes,
        HashSet<int> authoredClothNodes, Dictionary<int, string> freeClothNodeNames,
        HashSet<(int, int)> derivedRods, Dictionary<int, string> proxyNodeNames)
    {
        // A drop-risk island keeps no explicit rods and lets the importer derive its network from the surface.
        var riskyNodes = new HashSet<int>();

        foreach (var (_, _, proxyMesh) in proxies)
        {
            if (proxyMesh.IsDropRisk)
            {
                foreach (var node in proxyMesh.NodeIndices)
                {
                    riskyNodes.Add(node);
                }
            }
        }

        // A spring endpoint is an exported proxy vertex, a free ClothNode's element name, or a declared ClothNode.
        string? ResolveName(int node)
            => FeModel.IsProxyNodeName(feModel.CtrlNames[node])
                ? proxyNodeNames.GetValueOrDefault(node) ?? freeClothNodeNames.GetValueOrDefault(node)
                : authoredClothNodes.Contains(node) ? feModel.CtrlNames[node] : null;

        var seen = new HashSet<(int, int)>();
        foreach (var rod in feModel.Rods)
        {
            var edge = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (!seen.Add(edge))
            {
                continue;
            }

            if (riskyNodes.Contains(edge.Item1) || riskyNodes.Contains(edge.Item2))
            {
                continue;
            }

            if (derivedRods.Contains(edge))
            {
                continue;
            }

            // A chain joint's rods are the chain's own.
            if (chainJointNodes.Contains(edge.Item1) || chainJointNodes.Contains(edge.Item2))
            {
                continue;
            }

            var name0 = ResolveName(rod.NodeA);
            var name1 = ResolveName(rod.NodeB);
            if (name0 is null || name1 is null)
            {
                continue;
            }

            softbodyChildren.Add(MakeClothSpring($"rod_{edge.Item1}_{edge.Item2}", name0, name1, rod.MinDist,
                rod.MaxDist, rod.RelaxationFactor));
        }
    }

    /// <summary>
    /// Re-declares the rods the chains do not rebuild, as springs or as two-member clusters, and returns the pairs it
    /// declared.
    /// </summary>
    internal static HashSet<(int, int)> AddClothChainSurplusRods(KVObject softbodyChildren, FeModel feModel,
        List<FeModel.BoneChain> chains)
    {
        var declaredPairs = new HashSet<(int, int)>();
        var controlNames = feModel.CtrlNames;

        // Only a bone some chain claims as a joint can anchor a spring.
        var chainJoints = chains.SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Node)
            .ToHashSet();

        // A cluster tie may name a ring node, a spring may not.
        var chainRingNodes = chains.SelectMany(static chain => chain.Joints)
            .SelectMany(static joint => joint.RingNodes)
            .ToHashSet();

        var ringOwner = new Dictionary<int, int>();
        foreach (var joint in chains.SelectMany(static chain => chain.Joints))
        {
            foreach (var ring in joint.RingNodes)
            {
                ringOwner[ring] = joint.Node;
            }
        }

        bool Nameable(int node, bool tie)
            => chainJoints.Contains(node) || (tie && chainRingNodes.Contains(node));

        var occurrence = new Dictionary<(int, int), int>();
        var surplus = feModel.GetUngeneratedRods(chains, feModel.HasChainStiffnessRods(chains));
        var clusterTies = ClusterTiesBesideChainSpans(feModel, surplus);
        var ringTies = RingClusterTies(feModel, surplus, ringOwner);
        var cliquePairs = AddRingClusterCliques(softbodyChildren, feModel, ringOwner);
        declaredPairs.UnionWith(cliquePairs);
        foreach (var rod in surplus)
        {
            if (rod.NodeA < 0 || rod.NodeA >= controlNames.Length
            || rod.NodeB < 0 || rod.NodeB >= controlNames.Length)
            {
                continue;
            }

            var pair = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (cliquePairs.Contains(pair) && IsBandedRod(rod))
            {
                continue;
            }

            var tie = clusterTies.Contains(pair) || (ringTies.Contains(pair) && IsBandedRod(rod));
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

            // A two-member cluster compiles its rod with the members reversed, so the rod's second node is listed first.
            if (IsUnrecordedClusterRod(feModel, rod) || IsUnrecordedSpanCopy(feModel, rod))
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
    /// Declares the rods between joints of different independent chains as two-member clusters. A rod without the
    /// cluster's fixed relaxation and weight is left out.
    /// </summary>
    private static void AddClothChainSurplusClusters(KVObject softbodyChildren, FeModel feModel,
        List<FeModel.BoneChain> chains)
    {
        var controlNames = feModel.CtrlNames;
        var chainJoints = chains.SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Node)
            .ToHashSet();

        // A pair with more than one rod is skipped unless it is a cluster tie beside a chain span.
        var rodCounts = new Dictionary<(int, int), int>();
        foreach (var rod in feModel.Rods)
        {
            var key = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            rodCounts[key] = rodCounts.GetValueOrDefault(key) + 1;
        }

        var surplus = feModel.GetUngeneratedRods(chains);
        var clusterTies = ClusterTiesBesideChainSpans(feModel, surplus);
        var ringOwner = new Dictionary<int, int>();
        foreach (var joint in chains.SelectMany(static chain => chain.Joints))
        {
            foreach (var ring in joint.RingNodes)
            {
                ringOwner[ring] = joint.Node;
            }
        }

        AddRingClusterCliques(softbodyChildren, feModel, ringOwner);
        foreach (var rod in surplus)
        {
            if (rod.NodeA < 0 || rod.NodeA >= controlNames.Length
            || rod.NodeB < 0 || rod.NodeB >= controlNames.Length)
            {
                continue;
            }

            if (!chainJoints.Contains(rod.NodeA) || !chainJoints.Contains(rod.NodeB))
            {
                continue;
            }

            var pairKey = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (rodCounts.GetValueOrDefault(pairKey) > 1 && !clusterTies.Contains(pairKey))
            {
                continue;
            }

            if (rod.RelaxationFactor != 1f || rod.Weight0 != 0.5f)
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
    private static string NodeNameSafe(string name)
        => name.Contains('$', StringComparison.Ordinal)
            ? name.Replace("$", string.Empty, StringComparison.Ordinal)
            : name;

    /// <summary>
    /// Whether a surplus rod is a two-member <c>ClothSelfCollisionCluster</c>'s: the only rod on its pair, at the cluster's
    /// fixed relaxation and weight, with no source element on the pair and a length other than the rest distance.
    /// </summary>
    internal static bool IsUnrecordedClusterRod(FeModel feModel, FeModel.Rod rod)
    {
        if (rod.RelaxationFactor != 1f || rod.Weight0 != 0.5f
            || Array.IndexOf(feModel.SourceSprings, (rod.NodeA, rod.NodeB)) >= 0
            || Array.IndexOf(feModel.SourceSprings, (rod.NodeB, rod.NodeA)) >= 0)
        {
            return false;
        }

        var onPair = 0;
        foreach (var other in feModel.Rods)
        {
            if ((other.NodeA == rod.NodeA && other.NodeB == rod.NodeB) || (other.NodeA == rod.NodeB && other.NodeB == rod.NodeA))
            {
                onPair++;
            }
        }

        var poses = feModel.InitPosePositions;
        if (onPair != 1 || rod.NodeA >= poses.Length || rod.NodeB >= poses.Length)
        {
            return false;
        }

        var rest = Vector3.Distance(poses[rod.NodeA], poses[rod.NodeB]);
        return IsBandedRod(rod) || MathF.Abs(rod.MaxDist - rest) > MathF.Max(1e-3f, 1e-4f * rest);
    }

    /// <summary>
    /// Declares a <c>ClothSelfCollisionCluster</c> for every clique of three or more ring nodes of at least two joints whose
    /// every pair carries one banded cluster-signature rod off its rest length, where the bands solve as per-member radii.
    /// Returns the pairs the clusters cover.
    /// </summary>
    internal static HashSet<(int, int)> AddRingClusterCliques(KVObject softbodyChildren, FeModel feModel, Dictionary<int, int> ringOwner)
    {
        var covered = new HashSet<(int, int)>();
        var poses = feModel.InitPosePositions;
        var bandedOnPair = new Dictionary<(int, int), int>();
        foreach (var rod in feModel.Rods)
        {
            if (IsBandedRod(rod) && !feModel.IsSurfaceFold(rod))
            {
                var key = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
                bandedOnPair[key] = bandedOnPair.GetValueOrDefault(key) + 1;
            }
        }

        var band = new Dictionary<(int, int), (float Min, float Max)>();
        var neighbours = new Dictionary<int, HashSet<int>>();
        foreach (var rod in feModel.Rods)
        {
            var key = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (rod.NodeA == rod.NodeB || !IsBandedRod(rod) || rod.RelaxationFactor != 1f || rod.Weight0 != 0.5f
                || !ringOwner.ContainsKey(rod.NodeA) || !ringOwner.ContainsKey(rod.NodeB)
                || bandedOnPair.GetValueOrDefault(key) != 1 || rod.NodeA >= poses.Length || rod.NodeB >= poses.Length
                || MathF.Abs(rod.MaxDist - Vector3.Distance(poses[rod.NodeA], poses[rod.NodeB])) <= ClusterRestTolerance)
            {
                continue;
            }

            band[key] = (rod.MinDist, rod.MaxDist);
            (neighbours.TryGetValue(key.Item1, out var na) ? na : neighbours[key.Item1] = []).Add(key.Item2);
            (neighbours.TryGetValue(key.Item2, out var nb) ? nb : neighbours[key.Item2] = []).Add(key.Item1);
        }

        foreach (var clique in MaximalCliques(neighbours))
        {
            if (clique.Count < 3 || clique.Select(node => ringOwner[node]).Distinct().Count() < 2
                || SolveMemberRadii(clique, band) is not var (radii, strayRadii))
            {
                continue;
            }

            var names = clique.Select(node => feModel.CtrlNames[node]).ToList();
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

    // How far a cluster band's maximum has to sit from the pair's rest length before it is read as summed stray radii.
    private const float ClusterRestTolerance = 1e-3f;

    // Maximal cliques of an undirected graph, each in ascending node order.
    private static List<List<int>> MaximalCliques(Dictionary<int, HashSet<int>> neighbours)
    {
        var cliques = new List<List<int>>();

        void Extend(List<int> clique, HashSet<int> candidates, HashSet<int> excluded)
        {
            if (candidates.Count == 0 && excluded.Count == 0)
            {
                cliques.Add([.. clique.Order()]);
                return;
            }

            foreach (var node in candidates.Order().ToList())
            {
                var around = neighbours[node];
                Extend([.. clique, node], [.. candidates.Where(around.Contains)], [.. excluded.Where(around.Contains)]);
                candidates.Remove(node);
                excluded.Add(node);
            }
        }

        Extend([], [.. neighbours.Keys], []);
        return cliques;
    }

    // Per-member collision and stray radii that sum to every pair's band, read off one triangle and checked on every
    // pair; null where no such split exists.
    private static (float[] Radii, float[] StrayRadii)? SolveMemberRadii(List<int> members,
        Dictionary<(int, int), (float Min, float Max)> band)
    {
        (float Min, float Max) Band(int a, int b) => band[a < b ? (a, b) : (b, a)];

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

    // How closely the solved radii have to reproduce every band of the clique, relative to the band.
    private const float ClusterRadiusTolerance = 1e-4f;

    /// <summary>
    /// Whether a surplus rod is a second rigid copy of a span at its rest distance, with the cluster's fixed relaxation and
    /// weight and no source element on the pair, on a model that compiled <c>m_SkelParents</c>.
    /// </summary>
    internal static bool IsUnrecordedSpanCopy(FeModel feModel, FeModel.Rod rod)
    {
        if (!feModel.HasCompiledSkelParents || rod.RelaxationFactor != 1f || rod.Weight0 != 0.5f || IsBandedRod(rod)
            || Array.IndexOf(feModel.SourceSprings, (rod.NodeA, rod.NodeB)) >= 0
            || Array.IndexOf(feModel.SourceSprings, (rod.NodeB, rod.NodeA)) >= 0)
        {
            return false;
        }

        var poses = feModel.InitPosePositions;
        if (rod.NodeA >= poses.Length || rod.NodeB >= poses.Length)
        {
            return false;
        }

        var onPair = 0;
        foreach (var other in feModel.Rods)
        {
            if ((other.NodeA == rod.NodeA && other.NodeB == rod.NodeB) || (other.NodeA == rod.NodeB && other.NodeB == rod.NodeA))
            {
                onPair++;
            }
        }

        var rest = Vector3.Distance(poses[rod.NodeA], poses[rod.NodeB]);
        return onPair >= 2 && MathF.Abs(rod.MaxDist - rest) <= MathF.Max(1e-3f, 1e-4f * rest);
    }

    /// <summary>Whether a rod's length band is open: a cluster's separation constraint rather than a span.</summary>
    private static bool IsBandedRod(FeModel.Rod rod)
        => MathF.Abs(rod.MinDist - rod.MaxDist) > 1e-4f * MathF.Max(1f, MathF.Abs(rod.MaxDist));

    /// <summary>
    /// The pairs whose single banded rod is a two-member cluster between ring nodes of two different chain joints, beside
    /// the chain's rigid span, with a maximum off the pair's rest length.
    /// </summary>
    private static HashSet<(int, int)> RingClusterTies(FeModel feModel, List<FeModel.Rod> surplus,
        Dictionary<int, int> ringOwner)
    {
        var ties = new HashSet<(int, int)>();
        if (ringOwner.Count == 0)
        {
            return ties;
        }

        var entries = new Dictionary<(int, int), int>();
        var banded = new Dictionary<(int, int), int>();
        foreach (var rod in feModel.Rods)
        {
            var key = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            entries[key] = entries.GetValueOrDefault(key) + 1;
            if (IsBandedRod(rod))
            {
                banded[key] = banded.GetValueOrDefault(key) + 1;
            }
        }

        var poses = feModel.InitPosePositions;
        foreach (var rod in surplus)
        {
            var key = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (!ringOwner.TryGetValue(rod.NodeA, out var ownerA)
                || !ringOwner.TryGetValue(rod.NodeB, out var ownerB) || ownerA == ownerB
                || entries.GetValueOrDefault(key) < 2 || banded.GetValueOrDefault(key) != 1
                || !IsBandedRod(rod) || rod.RelaxationFactor != 1f || rod.Weight0 != 0.5f
                || rod.NodeA >= poses.Length || rod.NodeB >= poses.Length)
            {
                continue;
            }

            var rest = Vector3.Distance(poses[rod.NodeA], poses[rod.NodeB]);
            if (MathF.Abs(rod.MaxDist - rest) <= MathF.Max(1e-3f, 1e-4f * rest))
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
    private static HashSet<(int, int)> ClusterTiesBesideChainSpans(FeModel feModel, List<FeModel.Rod> surplus)
    {
        static (int, int) PairOf(FeModel.Rod rod)
            => rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);

        var entries = new Dictionary<(int, int), int>();
        var banded = new Dictionary<(int, int), int>();
        foreach (var rod in feModel.Rods)
        {
            var key = PairOf(rod);
            entries[key] = entries.GetValueOrDefault(key) + 1;
            if (IsBandedRod(rod))
            {
                banded[key] = banded.GetValueOrDefault(key) + 1;
            }
        }

        var surplusCounts = new Dictionary<(int, int), int>();
        foreach (var rod in surplus)
        {
            var key = PairOf(rod);
            surplusCounts[key] = surplusCounts.GetValueOrDefault(key) + 1;
        }

        var ties = new HashSet<(int, int)>();
        foreach (var rod in surplus)
        {
            var key = PairOf(rod);
            if (entries.GetValueOrDefault(key) > 1 && banded.GetValueOrDefault(key) == 1
                && surplusCounts[key] == 1 && IsBandedRod(rod)
                && rod.RelaxationFactor == 1f && rod.Weight0 == 0.5f)
            {
                ties.Add(key);
            }
        }

        return ties;
    }

    /// <summary>
    /// Re-declares each <see cref="FeModel.SelfCollisionCluster"/> as a <c>ClothSelfCollisionCluster</c> and returns the
    /// member nodes, which the cluster registers on its own.
    /// </summary>
    private static HashSet<int> AddClothSelfCollisionClusters(KVObject softbodyChildren, FeModel feModel,
        HashSet<string> clothBones)
    {
        var covered = new HashSet<int>();
        var names = feModel.CtrlNames;
        var index = 0;
        foreach (var cluster in feModel.SelfCollisionClusters)
        {
            var members = new List<string>(cluster.Nodes.Length);
            foreach (var node in cluster.Nodes)
            {
                if (node < 0 || node >= names.Length || feModel.IsGeneratedNodeName(names[node]))
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
    private static HashSet<(int, int)> AddClothSourceSprings(KVObject softbodyChildren, FeModel feModel,
        List<FeModel.BoneChain> chains)
    {
        var emitted = new HashSet<(int, int)>();
        var names = feModel.CtrlNames;
        var rodByEdge = new Dictionary<(int, int), FeModel.Rod>();
        foreach (var rod in feModel.Rods)
        {
            rodByEdge.TryAdd(rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA), rod);
        }

        foreach (var (a, b, copies) in feModel.GetAuthoredSourceSprings(chains))
        {
            if (a < 0 || a >= names.Length || b < 0 || b >= names.Length)
            {
                continue;
            }

            if (!rodByEdge.TryGetValue(a < b ? (a, b) : (b, a), out var rod))
            {
                continue;
            }

            // A spring keeps its source element's corner order.
            softbodyChildren.Add(MakeClothSpring($"spring_{a}_{b}", names[a], names[b], rod.MinDist,
                rod.MaxDist, rod.RelaxationFactor, copies - 1));
            emitted.Add(a < b ? (a, b) : (b, a));
        }

        return emitted;
    }
}
