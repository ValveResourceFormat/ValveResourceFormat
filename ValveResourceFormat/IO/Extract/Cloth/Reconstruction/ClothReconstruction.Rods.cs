using System.Linq;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.FeModelIndex;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// A recovered <c>ClothSelfCollisionCluster</c>: its member nodes, the length band of its pairwise rods, and each
    /// member's stiffness, whose product over a pair is that pair's relaxation.
    /// </summary>
    internal readonly record struct SelfCollisionCluster(int[] Nodes, float MinDist, float MaxDist, float[] Stiffness);

    internal sealed partial class ClothReconstruction
    {
        private const float SurfaceFanBandTolerance = 1e-4f;
        private const float SurfaceFanWeightTolerance = 2e-4f;

        /// <summary>The smallest rod clique read as a cluster without <see cref="IsRadiusBandTriangle"/>.</summary>
        private const int SelfCollisionClusterMinMembers = 4;

        private bool? hasSurfaceFolds;
        private bool? surfaceFoldsAbsent;
        private HashSet<(int, int)>? surfaceFanPairs;
        private HashSet<(int, int)>? surfaceFoldOnlyPairs;
        private List<SelfCollisionCluster>? selfCollisionClusters;
        private HashSet<int>? selfCollisionClusterRods;
        private HashSet<(int, int)>? selfCollisionClusterPairs;

        private static void ExpectPair(Dictionary<(int, int), List<float>> expectations, int a, int b, float relaxation)
        {
            if (a < 0 || b < 0)
            {
                return;
            }

            GetOrAdd(expectations, UnorderedPair(a, b)).Add(relaxation);
        }

        /// <summary>
        /// Gets the rods <paramref name="chains"/> regenerate by themselves, keyed by unordered pair, each entry the rod's
        /// expected relaxation factor.
        /// </summary>
        private Dictionary<(int, int), List<float>> ChainGeneratedSpans(List<BoneChain> chains)
        {
            var generated = new Dictionary<(int, int), List<float>>();
            var sliderScale = MathF.Exp(-Fe.DefaultSurfaceStretch);

            void Generate(int a, int b, float slider) => ExpectPair(generated, a, b, slider * sliderScale);

            foreach (var chain in chains)
            {
                var byNode = new Dictionary<int, BoneChainJoint>(chain.Joints.Count);
                foreach (var joint in chain.Joints)
                {
                    byNode.TryAdd(joint.Node, joint);
                }

                var rootNode = chain.Joints.Find(static j => j.IsRoot)?.Node ?? -1;
                foreach (var joint in chain.Joints)
                {
                    var parent = joint.ParentNode;
                    var grandParent = parent >= 0 && byNode.TryGetValue(parent, out var p1) ? p1.ParentNode : -1;
                    var greatGrandParent = grandParent >= 0 && byNode.TryGetValue(grandParent, out var p2)
                        ? p2.ParentNode
                        : -1;

                    var kids = joint.ChildSiblingSpring != 0f ? chain.Joints.FindAll(other => other.ParentNode == joint.Node) : [];
                    for (var copy = 0; copy <= joint.ExtraIterations; copy++)
                    {
                        if (joint.StretchStiffness != 0f)
                        {
                            Generate(parent, joint.Node, joint.StretchStiffness);
                        }

                        if (joint.BendSpring)
                        {
                            Generate(grandParent, joint.Node, joint.BendStiffness);
                        }

                        if (joint.TorsionSpring)
                        {
                            Generate(greatGrandParent, joint.Node, joint.TorsionStiffness);
                        }

                        if (joint.Suspender != 0f)
                        {
                            Generate(rootNode, joint.Node, joint.Suspender);
                        }

                        if (joint.ChildSiblingSpring != 0f)
                        {
                            for (var i = 0; i < kids.Count; i++)
                            {
                                for (var j = i + 1; j < kids.Count; j++)
                                {
                                    Generate(kids[i].Node, kids[j].Node, joint.ChildSiblingSpring);
                                }
                            }
                        }
                    }
                }
            }

            return generated;
        }

        /// <summary>
        /// Gets the two-corner source elements between chain joints or <c>$cc</c> ring nodes that the chains do not span,
        /// with how many rod copies each one has to declare.
        /// </summary>
        /// <param name="chains">The chains the export emits.</param>
        /// <param name="spanned">The <see cref="ChainGeneratedSpans"/> of <paramref name="chains"/>, when already built.</param>
        internal List<(int A, int B, int Copies)> GetAuthoredSourceSprings(List<BoneChain> chains,
            Dictionary<(int, int), List<float>>? spanned = null)
        {
            if (Index.SourceSprings.Length == 0)
            {
                return [];
            }

            var joints = new HashSet<int>();
            foreach (var chain in chains)
            {
                foreach (var joint in chain.Joints)
                {
                    joints.Add(joint.Node);
                }
            }

            bool IsEndpoint(int node) => IsRingNode(node) || joints.Contains(node);

            spanned ??= ChainGeneratedSpans(chains);
            var authored = new List<(int, int)>(Index.SourceSprings.Length);
            var occurrences = new Dictionary<(int, int), int>();
            foreach (var (a, b) in Index.SourceSprings)
            {
                if (IsEndpoint(a) && IsEndpoint(b))
                {
                    authored.Add((a, b));
                    var key = UnorderedPair(a, b);
                    occurrences[key] = occurrences.GetValueOrDefault(key) + 1;
                }
            }

            var copies = new Dictionary<(int, int), int>();
            var clusterRods = SelfCollisionClusterRods;
            for (var i = 0; i < Index.Rods.Length; i++)
            {
                var key = UnorderedPair(Index.Rods[i].NodeA, Index.Rods[i].NodeB);
                if (!clusterRods.Contains(i) && occurrences.ContainsKey(key))
                {
                    copies[key] = copies.GetValueOrDefault(key) + 1;
                }
            }

            var springs = new List<(int, int, int)>(authored.Count);
            foreach (var (a, b) in authored)
            {
                var key = UnorderedPair(a, b);

                var generated = spanned.TryGetValue(key, out var spans) ? spans.Count : 0;
                if (occurrences[key] > 1)
                {
                    if (generated == 0)
                    {
                        springs.Add((a, b, 1));
                    }

                    continue;
                }

                var surplus = Math.Max(1, copies.GetValueOrDefault(key)) - generated;
                if (surplus > 0)
                {
                    springs.Add((a, b, surplus));
                }
            }

            return springs;
        }

        /// <summary>Returns the rods that <paramref name="chains"/> and the authored source springs do not regenerate.</summary>
        /// <param name="chains">The chains the export emits.</param>
        /// <param name="surfaceFansRegenerate">Whether the rods folded across shared face edges are regenerated.</param>
        internal List<Rod> GetUngeneratedRods(List<BoneChain> chains, bool surfaceFansRegenerate = false)
        {
            var generated = ChainGeneratedSpans(chains);

            foreach (var (a, b, copies) in GetAuthoredSourceSprings(chains, generated))
            {
                for (var copy = 0; copy < copies; copy++)
                {
                    ExpectPair(generated, a, b, float.NaN);
                }
            }

            var entriesByPair = new Dictionary<(int, int), List<int>>();
            for (var i = 0; i < Index.Rods.Length; i++)
            {
                GetOrAdd(entriesByPair, UnorderedPair(Index.Rods[i].NodeA, Index.Rods[i].NodeB)).Add(i);
            }

            var claimed = new bool[Index.Rods.Length];

            foreach (var index in SelfCollisionClusterRods)
            {
                claimed[index] = true;
            }

            if (surfaceFansRegenerate)
            {
                for (var i = 0; i < Index.Rods.Length; i++)
                {
                    claimed[i] |= IsSurfaceFanRod(Index.Rods[i], banded: false);
                }
            }

            foreach (var (key, expectations) in generated)
            {
                if (!entriesByPair.TryGetValue(key, out var entries))
                {
                    continue;
                }

                foreach (var expected in expectations)
                {
                    var best = -1;
                    var bestScore = float.MaxValue;
                    foreach (var index in entries)
                    {
                        if (claimed[index])
                        {
                            continue;
                        }

                        var rod = Index.Rods[index];
                        var score = (rod.IsBanded ? 1f : 0f) + (float.IsNaN(expected)
                            ? 0f
                            : 0.5f * MathF.Min(1f, MathF.Abs(rod.RelaxationFactor - expected)));
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = index;
                        }
                    }

                    if (best >= 0)
                    {
                        claimed[best] = true;
                    }
                }
            }

            var surplus = new List<Rod>();
            for (var i = 0; i < Index.Rods.Length; i++)
            {
                if (!claimed[i])
                {
                    surplus.Add(Index.Rods[i]);
                }
            }

            return surplus;
        }

        /// <summary>
        /// Gets the <c>$cc</c> nodes joined to another <c>$cc</c> node by a source face edge or diagonal.
        /// </summary>
        private HashSet<int> SourceFaceRingNodes()
        {
            var nodes = new HashSet<int>();
            foreach (var (a, b) in DeriveRodsFromFaces(Index.SourceFaces))
            {
                if (IsRingNode(a) && IsRingNode(b))
                {
                    nodes.Add(a);
                    nodes.Add(b);
                }
            }

            return nodes;
        }

        /// <summary>
        /// Gets whether a bounded rod spans a parent-child link of <paramref name="chain"/>.
        /// </summary>
        internal bool HasChainRods(BoneChain chain)
        {
            var groupOf = ChainNodeGroups(chain);

            var links = chain.Joints
                .Where(static joint => !joint.IsRoot)
                .Select(static joint => (joint.ParentNode, joint.Node))
                .ToHashSet();

            foreach (var rod in Index.Rods)
            {
                if (rod.MaxDist < UnboundedRodDistance
                    && groupOf.TryGetValue(rod.NodeA, out var a) && groupOf.TryGetValue(rod.NodeB, out var b)
                    && (links.Contains((a, b)) || links.Contains((b, a))))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets whether an unbounded rod joins two of the chains' generated nodes, which only <c>add_bend_only_rods</c> builds.
        /// </summary>
        internal bool HasChainBendOnlyRods(List<BoneChain> chains)
        {
            var generated = ChainGeneratedNodes(chains);

            return Index.Rods.Any(rod => rod.MaxDist >= UnboundedRodDistance
                && generated.Contains(rod.NodeA) && generated.Contains(rod.NodeB));
        }

        /// <summary>
        /// Gets whether a banded rod joins two of the chains' generated nodes or a surface fold exists, which only
        /// <c>add_stiffness_rods</c> builds.
        /// </summary>
        internal bool HasChainStiffnessRods(List<BoneChain> chains)
        {
            var generated = ChainGeneratedNodes(chains);

            return Index.Rods.Any(rod => rod.MaxDist < UnboundedRodDistance && rod.MinDist < rod.MaxDist
                && generated.Contains(rod.NodeA) && generated.Contains(rod.NodeB))
                || HasSurfaceFolds;
        }

        /// <summary>Maps each joint of <paramref name="chain"/> and each of its ring nodes to the joint's node.</summary>
        private Dictionary<int, int> ChainNodeGroups(BoneChain chain)
        {
            var groupOf = new Dictionary<int, int>();
            foreach (var joint in chain.Joints)
            {
                groupOf[joint.Node] = joint.Node;
                foreach (var proxy in ProxyRingOf(joint.Node))
                {
                    groupOf[proxy] = joint.Node;
                }
            }

            return groupOf;
        }

        /// <summary>
        /// Gets the ring nodes of the chains' joints, or the <see cref="SourceFaceRingNodes"/> when they have none.
        /// </summary>
        private HashSet<int> ChainGeneratedNodes(List<BoneChain> chains)
        {
            var generated = chains
                .SelectMany(static chain => chain.Joints)
                .SelectMany(joint => ProxyRingOf(joint.Node))
                .ToHashSet();

            return generated.Count == 0 ? SourceFaceRingNodes() : generated;
        }

        /// <summary>Gets whether the compiler folded any rod across this model's own faces.</summary>
        internal bool HasSurfaceFolds => hasSurfaceFolds ??= Index.Rods.Any(rod => IsSurfaceFanRod(rod, banded: true));

        /// <summary>
        /// Gets whether <paramref name="rod"/> is a banded rod the compiler folded across a face edge on its own: it carries
        /// its endpoints' final inverse-mass ratio as its weight, which no declaration does.
        /// </summary>
        internal bool IsSurfaceFold(Rod rod) => IsSurfaceFanRod(rod, banded: true);

        /// <summary>
        /// Gets the pairs <c>add_stiffness_rods</c> makes the compiler fold across the edges this model's own faces share,
        /// walking the solve elements first and then the faces built into rods instead.
        /// </summary>
        private HashSet<(int, int)> SurfaceFanPairs => surfaceFanPairs ??= PredictBendRods(
            [.. FoldWalkSolveElements(), .. SourceElementWalk()], Index.IsStatic);

        /// <summary>Gets the node pairs whose every rod is a surface fold, so no declaration put a rod there.</summary>
        private HashSet<(int, int)> SurfaceFoldOnlyPairs => surfaceFoldOnlyPairs ??= [.. Index.Rods
            .GroupBy(static rod => UnorderedPair(rod.NodeA, rod.NodeB))
            .Where(group => group.All(rod => IsSurfaceFanRod(rod, banded: true)))
            .Select(static group => group.Key)];

        /// <summary>
        /// Gets whether <paramref name="rod"/> lies on a surface fold pair and carries its endpoints' final inverse-mass
        /// ratio as its weight. With <paramref name="banded"/>, the rod must also be banded.
        /// </summary>
        private bool IsSurfaceFanRod(Rod rod, bool banded)
        {
            if (rod.MaxDist >= UnboundedRodDistance
                || (banded && rod.MinDist >= rod.MaxDist - SurfaceFanBandTolerance * MathF.Max(1f, rod.MaxDist))
                || rod.NodeA >= Fe.NodeInvMasses.Length || rod.NodeB >= Fe.NodeInvMasses.Length)
            {
                return false;
            }

            var sum = Fe.NodeInvMasses[rod.NodeA] + Fe.NodeInvMasses[rod.NodeB];
            if (sum <= 0f)
            {
                return false;
            }

            var ratio = Fe.NodeInvMasses[rod.NodeA] / sum;
            if (MathF.Abs(ratio - 0.5f) <= SurfaceFanWeightTolerance
                || MathF.Abs(rod.Weight0 - ratio) > SurfaceFanWeightTolerance)
            {
                return false;
            }

            return SurfaceFanPairs.Contains(UnorderedPair(rod.NodeA, rod.NodeB));
        }

        /// <summary>
        /// Gets whether a rod on a surface fold pair joined the network after the mass pass.
        /// </summary>
        private bool FoldedAfterMass(Rod rod)
        {
            if (HasUnequalInverseMasses(rod))
            {
                return IsSurfaceFanRod(rod, banded: false);
            }

            return !(surfaceFoldsAbsent ??= Index.Rods.Any(IsUnequalFoldedPair) && !Index.Rods.Any(other => IsSurfaceFanRod(other, banded: false)));
        }

        private bool IsUnequalFoldedPair(Rod rod)
            => HasUnequalInverseMasses(rod) && SurfaceFanPairs.Contains(UnorderedPair(rod.NodeA, rod.NodeB));

        /// <summary>Gets whether the inverse-mass ratio of <paramref name="rod"/>'s endpoints is off an even split.</summary>
        private bool HasUnequalInverseMasses(Rod rod)
        {
            if (rod.NodeA >= Fe.NodeInvMasses.Length || rod.NodeB >= Fe.NodeInvMasses.Length)
            {
                return false;
            }

            var sum = Fe.NodeInvMasses[rod.NodeA] + Fe.NodeInvMasses[rod.NodeB];
            return sum > 0f && MathF.Abs(Fe.NodeInvMasses[rod.NodeA] / sum - 0.5f) > SurfaceFanWeightTolerance;
        }

        /// <summary>Gets the node pairs a recovered cluster puts one of its own rods on.</summary>
        private IReadOnlySet<(int, int)> SelfCollisionClusterPairs
        {
            get
            {
                if (selfCollisionClusterPairs is null)
                {
                    selfCollisionClusterPairs = [];
                    foreach (var cluster in SelfCollisionClusters)
                    {
                        for (var i = 0; i < cluster.Nodes.Length; i++)
                        {
                            for (var j = i + 1; j < cluster.Nodes.Length; j++)
                            {
                                selfCollisionClusterPairs.Add(UnorderedPair(cluster.Nodes[i], cluster.Nodes[j]));
                            }
                        }
                    }
                }

                return selfCollisionClusterPairs;
            }
        }

        /// <summary>
        /// Gets the self-collision clusters: cliques whose rods share one length band, weight 0.5 and pairwise-product
        /// relaxations, and register no source element.
        /// </summary>
        internal IReadOnlyList<SelfCollisionCluster> SelfCollisionClusters
            => selfCollisionClusters ??= BuildSelfCollisionClusters();

        /// <summary>Gets the index into <see cref="FeModelIndex.Rods"/> of every rod a <see cref="SelfCollisionClusters"/> entry accounts for.</summary>
        internal IReadOnlySet<int> SelfCollisionClusterRods
            => selfCollisionClusterRods ??= BuildSelfCollisionClusterRods();

        private List<SelfCollisionCluster> BuildSelfCollisionClusters()
        {
            var found = new List<SelfCollisionCluster>();
            const int minMembers = 3;
            if (IsImportedCloth || Index.Rods.Length < minMembers)
            {
                return found;
            }

            var sprung = new HashSet<(int, int)>();
            foreach (var (a, b) in Index.SourceSprings)
            {
                sprung.Add(UnorderedPair(a, b));
            }

            var byBand = new Dictionary<(float, float), Dictionary<(int, int), (int Copies, float Relaxation)>>();
            foreach (var rod in Index.Rods)
            {
                var pair = UnorderedPair(rod.NodeA, rod.NodeB);
                if (rod.MaxDist <= rod.MinDist || rod.RelaxationFactor <= 0f
                    || rod.Weight0 != 0.5f || sprung.Contains(pair)
                    || ImportedStripNodes.Contains(rod.NodeA) || ImportedStripNodes.Contains(rod.NodeB))
                {
                    continue;
                }

                var band = (rod.MinDist, rod.MaxDist);
                var counts = GetOrAdd(byBand, band);
                counts[pair] = (counts.GetValueOrDefault(pair).Copies + 1, rod.RelaxationFactor);
            }

            var taken = new HashSet<int>();
            foreach (var (band, counts) in byBand.OrderBy(static entry => entry.Key.Item1)
                .ThenBy(static entry => entry.Key.Item2))
            {
                var neighbours = new Dictionary<int, HashSet<int>>();
                foreach (var ((a, b), (copies, _)) in counts)
                {
                    if (copies != 1)
                    {
                        continue;
                    }

                    GetOrAdd(neighbours, a).Add(b);
                    GetOrAdd(neighbours, b).Add(a);
                }

                var seen = new HashSet<int>();
                foreach (var start in neighbours.Keys.Order())
                {
                    if (!seen.Add(start))
                    {
                        continue;
                    }

                    var members = new List<int> { start };
                    for (var read = 0; read < members.Count; read++)
                    {
                        foreach (var next in neighbours[members[read]])
                        {
                            if (seen.Add(next))
                            {
                                members.Add(next);
                            }
                        }
                    }

                    members.Sort();
                    if (members.Count < minMembers || members.Exists(taken.Contains)
                        || members.Exists(node => neighbours[node].Count != members.Count - 1)
                        || (members.Count < SelfCollisionClusterMinMembers && !IsRadiusBandTriangle(members, band.Item2)))
                    {
                        continue;
                    }

                    if (MemberStiffness(members, counts) is not { } stiffness)
                    {
                        continue;
                    }

                    taken.UnionWith(members);
                    found.Add(new SelfCollisionCluster([.. members], band.Item1, band.Item2, stiffness));
                }
            }

            return found;

            static float[]? MemberStiffness(List<int> members, Dictionary<(int, int), (int Copies, float Relaxation)> counts)
            {
                float RelaxationOf(int a, int b) => counts[UnorderedPair(a, b)].Relaxation;

                var stiffness = new float[members.Count];
                for (var i = 0; i < members.Count; i++)
                {
                    var j = members[(i + 1) % members.Count];
                    var k = members[(i + 2) % members.Count];
                    stiffness[i] = MathF.Sqrt(RelaxationOf(members[i], j) * RelaxationOf(members[i], k) / RelaxationOf(j, k));
                }

                for (var i = 0; i < members.Count; i++)
                {
                    for (var j = i + 1; j < members.Count; j++)
                    {
                        if (MathF.Abs(RelaxationOf(members[i], members[j]) - stiffness[i] * stiffness[j]) > 1e-4f)
                        {
                            return null;
                        }
                    }
                }

                return stiffness;
            }
        }

        /// <summary>
        /// Gets whether a three-node rod triangle is a self-collision cluster: every member is authored and the band's
        /// maximum is none of the pairs' rest distances.
        /// </summary>
        private bool IsRadiusBandTriangle(List<int> members, float bandMax)
        {
            foreach (var node in members)
            {
                if (node >= Fe.CtrlName.Length || node >= Index.InitPosePositions.Length || IsGeneratedNodeName(Fe.CtrlName[node]))
                {
                    return false;
                }
            }

            for (var i = 0; i < members.Count; i++)
            {
                for (var j = i + 1; j < members.Count; j++)
                {
                    var rest = Vector3.Distance(Index.InitPosePositions[members[i]], Index.InitPosePositions[members[j]]);
                    if (Rod.IsAtRestLength(bandMax, rest))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private HashSet<int> BuildSelfCollisionClusterRods()
        {
            var claimed = new HashSet<int>();
            if (SelfCollisionClusters.Count == 0)
            {
                return claimed;
            }

            var wanted = new Dictionary<(int, int), (float Min, float Max, float Relaxation)>();
            foreach (var cluster in SelfCollisionClusters)
            {
                for (var i = 0; i < cluster.Nodes.Length; i++)
                {
                    for (var j = i + 1; j < cluster.Nodes.Length; j++)
                    {
                        var relaxation = cluster.Stiffness[i] * cluster.Stiffness[j];
                        wanted[UnorderedPair(cluster.Nodes[i], cluster.Nodes[j])] = (cluster.MinDist, cluster.MaxDist, relaxation);
                    }
                }
            }

            for (var i = 0; i < Index.Rods.Length; i++)
            {
                var rod = Index.Rods[i];
                var pair = UnorderedPair(rod.NodeA, rod.NodeB);
                if (wanted.TryGetValue(pair, out var band) && rod.MinDist == band.Min
                    && rod.MaxDist == band.Max && MathF.Abs(rod.RelaxationFactor - band.Relaxation) <= 1e-4f
                    && rod.Weight0 == 0.5f)
                {
                    claimed.Add(i);
                    wanted.Remove(pair);
                }
            }

            return claimed;
        }

        /// <summary>
        /// Returns the node pairs the compiler regenerates as <c>m_Rods</c> from <paramref name="faces"/>:
        /// every face edge plus every face diagonal, deduplicated.
        /// </summary>
        internal static HashSet<(int, int)> DeriveRodsFromFaces(IEnumerable<int[]> faces)
        {
            var derived = new HashSet<(int, int)>();
            foreach (var face in faces)
            {
                for (var a = 0; a < face.Length; a++)
                {
                    for (var b = a + 1; b < face.Length; b++)
                    {
                        derived.Add(UnorderedPair(face[a], face[b]));
                    }
                }
            }

            return derived;
        }
    }
}
