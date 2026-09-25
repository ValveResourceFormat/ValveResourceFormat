using System.Globalization;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    // Explicitly declares a two-node distance constraint (a "rod") by NODE NAME: the ClothSpring node, the
    // analogue of ClothQuad for edges instead of faces. is_length_explicit=false, the default, pins
    // min_length = max_length = the rest distance, a fully rigid edge. Both is_length_explicit and
    // enable_advanced_parameters are needed together for min_length/max_length to take effect.
    //
    // weight0 and relaxation_factor are not ClothSpring inputs: it registers no attribute for either, so
    // an authored weight0 compiles to the builder's default of 0.5 while min_length/max_length stay exact
    // (see FeModel.Rod.Weight0). "stiffness" is the attribute a rod's flRelaxationFactor comes back on.
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

    // A ClothSelfCollisionCluster's member pair compiles to exactly one m_Rods entry (flMinDist/flMaxDist
    // the summed member radii, flWeight0 the builder's own default) and leaves no other trace:
    // m_SelfCollisionLayers, m_NodeCollisionRadii and m_AnimStrayRadii are all unaffected. Unlike a
    // ClothSpring it registers no m_SourceElems entry, so it is the node to re-emit for a rod between two
    // chain joints that a chain does not itself regenerate. The per-member radius split the compiled rod
    // does not preserve (only the sum reaches m_Rods) is recovered as an even split.
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

        // The member table's own schema, which the compiler falls back to for any member row that omits
        // a key, exactly as a ClothChain's attrs table does. Its four defaults are the dense-KV3
        // schema's own (CAuthClothDataTable::ctor_dtor_1).
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
        // Islands the cloth importer is expected to prune vertices from (see FeModel.ComputeDropRisk):
        // emitting explicit rods into them would orphan a ClothSpring on a vertex the compiler never creates
        // ("Cannot find node $cloth_mXpY", a hard failure). Skip their explicit rods entirely and let the
        // importer auto-derive the network from the surface instead - guaranteed to compile, at the cost of
        // exact rod topology for that one island. Clean islands keep their exact reconstructed rods.
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

        // A real bone anchors a spring only when this export also declares it as a ClothNode. A bone the
        // compile knows solely through a chain's joint list or a proxy back-solve is not a valid endpoint,
        // and naming one fails the whole compile with "Cannot find Fx Bone"/"Cannot find node". A
        // "$cloth_node_" ctrl re-authored as a free ClothNode is named by its element name instead.
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

            // A rod inside a drop-risk island is skipped (the whole island falls back to compiler-derived
            // rods) - see the riskyNodes remarks above.
            if (riskyNodes.Contains(edge.Item1) || riskyNodes.Contains(edge.Item2))
            {
                continue;
            }

            if (derivedRods.Contains(edge))
            {
                continue;
            }

            // A ClothChain's own joint hierarchy compiles to a fully-connected local rod mesh among ITS
            // OWN joints, not just parent-child pairs, so re-declaring one of these as an explicit
            // ClothSpring is redundant. It is also rejected: a bone that is only a ClothChain joint_name,
            // with no fit-matrix back-solve or ClothNode registration of its own, is not a valid
            // ClothSpring endpoint.
            if (chainJointNodes.Contains(edge.Item1) || chainJointNodes.Contains(edge.Item2))
            {
                continue;
            }

            var name0 = ResolveName(rod.NodeA);
            var name1 = ResolveName(rod.NodeB);
            if (name0 is null || name1 is null)
            {
                // A rod-only proxy node dropped by BuildProxyMeshesFromRodsOnly's 3-member minimum (see
                // its own remarks) has no corresponding exported vertex to reference at all - skip rather
                // than author a dangling reference the compiler would reject outright.
                continue;
            }

            softbodyChildren.Add(MakeClothSpring($"rod_{edge.Item1}_{edge.Item2}", name0, name1, rod.MinDist,
                rod.MaxDist, rod.RelaxationFactor));
        }
    }

    // Rods the chains do not rebuild themselves (extra copies of a parent span) are re-declared here, and a
    // cluster's tie beside a chain span as its two-member cluster.
    internal static HashSet<(int, int)> AddClothChainSurplusRods(KVObject softbodyChildren, FeModel feModel,
        List<FeModel.BoneChain> chains)
    {
        // The pairs declared here, so the free-node pass does not re-declare one of them and ship the
        // same constraint twice. A pair reaches both only where one endpoint is a chain joint the other
        // passes still declare a bare node for, which is what a sibling hub is.
        var declaredPairs = new HashSet<(int, int)>();
        var controlNames = feModel.CtrlNames;

        // Only a bone some emitted chain actually claims as a joint is registered as a cloth node, and so
        // only such a bone can anchor a spring. A cloth-flagged bone that no chain covers (a chain's own
        // parent one hop above its root, say) resolves to nothing and fails the whole compile with
        // "Cannot find Fx Bone".
        var chainJoints = chains.SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Node)
            .ToHashSet();

        // Ring nodes the emitted chain regenerates, and which joint extruded each: a cluster tie may name
        // one, a spring may not, and a tie spans the rings of two DIFFERENT joints.
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

        // One spring per surplus rod OCCURRENCE, numbered like AddFreeClothNodesAndSprings' copies.
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

    // The proxy-sheet phase's own AddClothProxySprings skips every rod touching an independent chain
    // joint (that pairing is a chain's job), but a chain's own generated spans (see
    // FeModel.ChainGeneratedSpans) only ever cover ITS OWN joints - a rod between two joints of two
    // DIFFERENT chains is never regenerated by anything in that phase and was dropped outright before
    // this. Unlike AddClothChainSurplusRods' plain ClothSpring, this emits a ClothSelfCollisionCluster
    // (see MakeClothSelfCollisionCluster), which adds no m_SourceElems entry.
    //
    // A cluster's compiled rod always carries the builder's own fixed relax and weight of 1.0 and 0.5,
    // neither an authorable cluster input (same as ClothSpring's, see MakeClothSpring). A rod without that
    // signature is left unemitted rather than re-declared as a ClothSpring, which would compile the
    // m_SourceElems entry a cluster-derived rod never has.
    private static void AddClothChainSurplusClusters(KVObject softbodyChildren, FeModel feModel,
        List<FeModel.BoneChain> chains)
    {
        var controlNames = feModel.CtrlNames;
        var chainJoints = chains.SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Node)
            .ToHashSet();

        // A pair with more than one raw entry is skipped unless it is a cluster tie beside a chain span.
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

    /// <summary>
    /// A composed ModelDoc node name the compiler will keep. A softbody child whose <c>name</c> carries a
    /// <c>$</c> ANYWHERE is discarded silently - no error, and the compile still reports success - so a name
    /// built out of control names, every generated one of which carries one, has to drop it. The members a
    /// cluster or spring NAMES are unaffected: <c>joint_name</c> resolves a <c>$cc</c> ring node fine.
    /// <para>
    /// Measured on VRF's own emitted document for synth row <c>w37wt_probe_ring2_cluster_span</c>: five arms
    /// differing in this field alone, compiled into one namespace. The two with no <c>$</c> compiled the
    /// cluster's rod (16 rods, the banded 12/48 on the ring pair); the three with one, leading, middle or
    /// trailing, compiled 15 and dropped it. A 43-character name with no <c>$</c> compiled, so it is the
    /// character and not the length.
    /// </para>
    /// </summary>
    private static string NodeNameSafe(string name)
        => name.Contains('$', StringComparison.Ordinal)
            ? name.Replace("$", string.Empty, StringComparison.Ordinal)
            : name;

    /// <summary>
    /// Whether a surplus rod is a two-member <c>ClothSelfCollisionCluster</c>'s own rod rather than a spring's: the only
    /// rod on its pair, at the cluster's fixed relaxation of 1.0 and weight of 0.5, with no two-corner source element
    /// on the pair in the original, and a length that is not the pair's rest distance (the summed member radii,
    /// banded or not). A <c>ClothSpring</c> always records that source element, so its absence rules the spring out.
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
    /// Emits one <c>ClothSelfCollisionCluster</c> per clique of three or more extruded ring nodes, owned by at least two
    /// different chain joints, whose every pair carries exactly one banded rod at relaxation 1 and weight 0.5 off its rest
    /// length, where those bands solve as per-member radii: a cluster compiles a rod on every member pair, its minimum the
    /// two members' collision radii summed and its maximum their stray radii summed. No chain span is banded off its rest
    /// length, so the clique is read off every shipped rod; a fold the compiler built across the pair beside the cluster's
    /// band is not counted against it. Returns the pairs the clusters cover.
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
    /// Whether a surplus rod is a second rigid copy of a span at the pair's rest distance, at the cluster's fixed relaxation of
    /// 1.0 and weight of 0.5, with no two-corner source element on the pair in the original: a copy no <c>ClothSpring</c> made,
    /// which a two-member cluster at half the length per member reproduces without the element. Only a model that compiled
    /// <c>m_SkelParents</c> is read this way.
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
    /// Returns the node pairs whose one banded rod is a two-member <c>ClothSelfCollisionCluster</c> over the
    /// extruded ring nodes of two DIFFERENT chain joints, beside the chain's rigid span on the same pair.
    /// <para>
    /// <see cref="ClusterTiesBesideChainSpans"/> cannot see these: it requires the pair to hold exactly ONE
    /// surplus rod, which assumes <see cref="FeModel.GetUngeneratedRods"/> gave the chain the rigid entry,
    /// and a chain whose spans that model does not predict leaves BOTH on the pair. Ring-ring spans are the
    /// population where that happens, so two other conditions have to do the work instead.
    /// </para>
    /// <para>
    /// FIRST, the rings belong to different joints. A banded rod between two rings of ONE joint is that
    /// joint's own ring rod under an <c>antishrink</c> below one, which the emitted chain regenerates:
    /// declaring a cluster there duplicates it. Measured on the 30 dota documents a band test alone reached,
    /// 21 of which were EXACT or EQUIVALENT and every one of which gained <c>m_Rods</c> without this
    /// condition. SECOND, the band's maximum is not the pair's rest length, which is
    /// <see cref="FeModel.IsRadiusBandTriangle"/>'s test applied to a pair: a cluster's maximum is its
    /// members' summed stray radii and has nothing to do with how far apart they sit.
    /// </para>
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
    /// Returns the node pairs whose one surplus rod is a two-member self-collision cluster's tie beside the
    /// chain span on the same pair: the pair carries several entries, every one but that rod is rigid, and
    /// that rod is banded with the cluster's fixed relaxation of 1.0 and weight of 0.5.
    /// <see cref="FeModel.GetUngeneratedRods"/> gives the chain the rigid entry, so the banded one left over
    /// is the cluster's own and never the chain's.
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
    /// Re-declares each recovered <see cref="FeModel.SelfCollisionCluster"/> as one
    /// <c>ClothSelfCollisionCluster</c> listing every member. The compiler rebuilds the whole pairwise rod
    /// set from it and records no source element for any of them, which one spring per pair would.
    /// <para>
    /// Returns the member nodes, which the caller must keep out of the lone-node and lone-chain emitters:
    /// the cluster registers them on its own.
    /// </para>
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
    /// Re-declares the authored two-corner source elements (<see cref="FeModel.SourceSprings"/>) as
    /// explicit springs. Neither the surface nor a chain regenerates these, and the compiler records one
    /// source element per spring, so a model exported without them comes back short both a rod and a
    /// source element per pair. Endpoints are named verbatim, <c>$cc</c> proxies included - those are
    /// valid ClothSpring endpoints even though they are not chain joints.
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

            // The source element keeps the two corners in the order the spring named them, which the rod's
            // own endpoint order does not.
            softbodyChildren.Add(MakeClothSpring($"spring_{a}_{b}", names[a], names[b], rod.MinDist,
                rod.MaxDist, rod.RelaxationFactor, copies - 1));
            emitted.Add(a < b ? (a, b) : (b, a));
        }

        return emitted;
    }
}
