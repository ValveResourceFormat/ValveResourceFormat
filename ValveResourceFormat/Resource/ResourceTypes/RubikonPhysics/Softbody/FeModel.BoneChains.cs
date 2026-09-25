using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        /// <summary>
        /// A single joint within a reconstructed bone chain.
        /// </summary>
        internal sealed class BoneChainJoint
        {
            /// <summary>Gets the control-node index of this joint.</summary>
            public int Node { get; init; }
            /// <summary>Gets the bone name of this joint.</summary>
            public required string Name { get; init; }
            /// <summary>Gets the control-node index of the chain parent, or -1 if this is the chain root.</summary>
            public int ParentNode { get; set; }
            /// <summary>Gets the bone name of the chain parent, or null if this is the chain root.</summary>
            public string? ParentName { get; set; }
            /// <summary>Gets the inverse mass for this node (0 = static anchor).</summary>
            public float InvMass { get; set; }
            /// <summary>
            /// Gets the node whose compiled per-node values describe this declaration of the joint, or -1 for the joint's
            /// own node.
            /// </summary>
            public int ValueNode { get; set; } = -1;
            /// <summary>Gets the number of <c>$cc</c> proxy nodes on this joint's own ring.</summary>
            public int ExtrudeSides { get; set; }
            /// <summary>Gets the <c>$cc</c> proxy nodes this declaration extruded on the joint, in node order.</summary>
            public IReadOnlyList<int> RingNodes { get; set; } = [];
            /// <summary>Gets one of the <c>$cc</c> proxy nodes generated from this joint, or -1 when it has none.</summary>
            public int ProxyNode { get; set; } = -1;
            /// <summary>Gets the authored <c>extra_iterations</c>: the copies of the joint's parent span, less one.</summary>
            public int ExtraIterations { get; set; }
            /// <summary>
            /// Gets the authored <c>antishrink</c>, the <c>flMinDist / flMaxDist</c> the joint's spans share, or 1 when they
            /// disagree or carry no rod.
            /// </summary>
            public float Antishrink { get; set; } = 1f;
            /// <summary>
            /// Gets the authored <c>suspender</c>, the relaxation of the companion rod between this joint's ring and the chain
            /// root's ring, or 0 when it carries none.
            /// </summary>
            public float Suspender { get; set; }
            /// <summary>
            /// Gets the authored <c>child_sibling_spring</c>, the relaxation every rod between this joint's children shares,
            /// or 0 where that rod set is incomplete or disagrees.
            /// </summary>
            public float ChildSiblingSpring { get; set; }
            /// <summary>Gets whether the joint was authored with a non-zero <c>bend_spring</c>.</summary>
            public bool BendSpring { get; set; }
            /// <summary>Gets whether the joint was authored with a non-zero <c>torsion_spring</c>.</summary>
            public bool TorsionSpring { get; set; }
            /// <summary>Gets the authored <c>stretch_spring</c>, read off the span to the chain parent.</summary>
            public float StretchStiffness { get; set; } = 1f;
            /// <summary>Gets whether the joint was authored with <c>animated_length</c>.</summary>
            public bool AnimatedLength { get; set; }
            /// <summary>Gets the authored <c>bend_spring</c>, read off the span to the grandparent; 0 without <see cref="BendSpring"/>.</summary>
            public float BendStiffness { get; set; }
            /// <summary>
            /// Gets the authored <c>torsion_spring</c>, read off the span to the great-grandparent; 0 without
            /// <see cref="TorsionSpring"/>.
            /// </summary>
            public float TorsionStiffness { get; set; }
            /// <summary>Gets the distance from this joint to its own proxy ring.</summary>
            public float ExtrudeRadius { get; set; }
            /// <summary>
            /// Gets the roll in degrees added to <see cref="ExtrudeTwist"/> to settle a node-base scan tie, 0 otherwise.
            /// </summary>
            public float ExtrudeTwistTieNudge { get; set; }
            /// <summary>
            /// Gets the roll in degrees of the joint's proxy ring about the forward axis, from the rest frame's +Y.
            /// </summary>
            public float ExtrudeTwist { get; set; }
            /// <summary>
            /// Gets the forward distance to the joint's second proxy ring (<c>end_effector</c>), or 0 for a single ring.
            /// </summary>
            public float EndEffector { get; set; }
            /// <summary>
            /// Gets the <c>extrude_forward_axis</c> of the joint's proxy ring (<c>'x'</c>, <c>'y'</c> or <c>'z'</c>).
            /// </summary>
            public char ForwardAxis { get; set; } = 'x';
            /// <summary>
            /// Gets whether the joint is declared a second time in a plain chain after the extruding one.
            /// </summary>
            public bool Restated { get; set; }
            /// <summary>
            /// Gets whether the joint is a static, goal-locked sibling that a hub's <c>child_sibling_spring</c> gathers.
            /// </summary>
            public bool SpringsWithSiblings { get; set; }
            /// <summary>
            /// Gets the root bone of the second <c>ClothChain</c> that re-declares this joint, or null when only one declares it.
            /// </summary>
            public string? SecondDeclarationRoot { get; set; }
            /// <summary>Gets a value indicating whether this joint is simulated (invMass &gt; 0).</summary>
            public bool Simulated => InvMass > 0f;
            /// <summary>Gets a value indicating whether this joint is the chain root.</summary>
            public bool IsRoot => ParentNode < 0;
        }

        /// <summary>
        /// A reconstructed bone chain: a static anchor bone plus all of its simulated descendants.
        /// </summary>
        internal sealed class BoneChain
        {
            /// <summary>Gets the anchor (root) bone name.</summary>
            public required string RootBone { get; set; }
            /// <summary>
            /// Gets the suffix telling this chain apart from other declarations over the same root bone, or empty.
            /// </summary>
            public string DeclarationSuffix { get; set; } = string.Empty;
            /// <summary>Gets the joints, root first, in pre-order (a parent always precedes its children).</summary>
            public List<BoneChainJoint> Joints { get; } = [];
            /// <summary>Gets the chain's <c>extrude_sides</c>: the <c>$cc</c> proxy count most joints share.</summary>
            public int ExtrudeSides { get; set; }
            /// <summary>Gets the mean distance from a joint bone to its <c>$cc</c> proxy nodes (the extrude half-width).</summary>
            public float ExtrudeRadius { get; set; }
            /// <summary>Gets the mean roll in degrees of the chain's proxy rings.</summary>
            public float ExtrudeTwist { get; set; }
        }

        /// <summary>One chain declaration before its joints are walked.</summary>
        private sealed class ChainSpec
        {
            public int Root { get; init; }
            public bool RinglessRoot { get; init; }
            /// <summary>The children each listed node keeps in this chain; a node absent keeps all of them.</summary>
            public Dictionary<int, HashSet<int>>? ChildrenOf { get; set; }
            /// <summary>The ring each listed node extruded in THIS declaration; empty for a ringless one.</summary>
            public Dictionary<int, List<int>>? RingOf { get; set; }
            public string Suffix { get; set; } = string.Empty;
        }

        /// <summary>How far apart along a joint's forward axis two proxies must be to lie on separate rings.</summary>
        private const float EndEffectorRingTolerance = 0.05f;

        /// <summary>Gets the trailing <c>_&lt;n&gt;</c> index of a ring node's name, or -1 when it has none.</summary>
        private static int RingSuffixIndex(string name)
        {
            var underscore = name.LastIndexOf('_');
            return underscore >= 0 && underscore + 1 < name.Length
                && int.TryParse(name.AsSpan(underscore + 1), out var index)
                ? index
                : -1;
        }

        /// <summary>
        /// Splits one bone's generated ring nodes into the declarations that built them, or null when a
        /// single ClothChain declared the bone.
        /// </summary>
        private static List<List<int>>? SplitRingDeclarations(List<int> proxies, string[] names)
        {
            var groups = new List<List<int>>();
            var current = new List<int>();
            var highest = -1;
            foreach (var proxy in proxies)
            {
                var index = proxy < names.Length ? RingSuffixIndex(names[proxy]) : -1;
                if (index >= 0 && index <= highest && current.Count > 0)
                {
                    groups.Add(current);
                    current = [];
                    highest = -1;
                }

                current.Add(proxy);
                if (index > highest)
                {
                    highest = index;
                }
            }

            groups.Add(current);
            return groups.Count > 1 ? groups : null;
        }

        /// <summary>
        /// Splits every chain spec whose bones were declared by more than one ClothChain into one spec per
        /// declaration, each carrying that declaration's own ring and the children hanging off it.
        /// </summary>
        private void SplitRingDeclarations(List<ChainSpec> specs, List<int>?[] children, int[] realParent,
            Dictionary<int, List<int>> proxyChildrenOf, HashSet<(int, int)> rodPairs)
        {
            var declarations = new Dictionary<int, List<List<int>>>();
            foreach (var (bone, proxies) in proxyChildrenOf)
            {
                if (SplitRingDeclarations(proxies, CtrlNames) is { } groups)
                {
                    declarations[bone] = groups;
                }
            }

            if (declarations.Count == 0)
            {
                return;
            }

            List<int> SideOf(int node)
                => proxyChildrenOf.TryGetValue(node, out var ring) && ring.Count > 0 ? ring : [node];

            int RodCount(List<int> lhs, List<int> rhs)
            {
                var hits = 0;
                foreach (var a in lhs)
                {
                    foreach (var b in rhs)
                    {
                        if (rodPairs.Contains(UnorderedPair(a, b)))
                        {
                            hits++;
                        }
                    }
                }

                return hits;
            }

            int BestGroup(List<List<int>> groups, List<int> side)
            {
                var best = -1;
                var bestHits = 0;
                for (var g = 0; g < groups.Count; g++)
                {
                    var hits = RodCount(groups[g], side);
                    if (hits > bestHits)
                    {
                        bestHits = hits;
                        best = g;
                    }
                }

                return best;
            }

            List<int> Reach(ChainSpec spec, int from)
            {
                var result = new List<int>();
                var stack = new Stack<int>();
                stack.Push(from);
                while (stack.Count > 0 && result.Count < 4096)
                {
                    var node = stack.Pop();
                    result.Add(node);
                    if (proxyChildrenOf.TryGetValue(node, out var ring))
                    {
                        result.AddRange(ring);
                    }

                    foreach (var kid in KeptChildren(spec, node))
                    {
                        stack.Push(kid);
                    }
                }

                return result;
            }

            List<int> KeptChildren(ChainSpec spec, int node)
            {
                var kept = new List<int>();
                if (children[node] is not { } all)
                {
                    return kept;
                }

                foreach (var kid in all)
                {
                    if (spec.ChildrenOf is null || !spec.ChildrenOf.TryGetValue(node, out var allowed)
                        || allowed.Contains(kid))
                    {
                        kept.Add(kid);
                    }
                }

                return kept;
            }

            int NextSplit(ChainSpec spec, HashSet<int> done)
            {
                var stack = new Stack<int>();
                stack.Push(spec.Root);
                var guard = 0;
                while (stack.Count > 0 && guard++ < 4096)
                {
                    var node = stack.Pop();
                    if (declarations.ContainsKey(node) && !done.Contains(node))
                    {
                        return node;
                    }

                    foreach (var kid in KeptChildren(spec, node))
                    {
                        stack.Push(kid);
                    }
                }

                return -1;
            }

            var splitDone = new Dictionary<ChainSpec, HashSet<int>>();
            var declarationIndex = 1;
            for (var i = 0; i < specs.Count; i++)
            {
                var spec = specs[i];
                if (!splitDone.TryGetValue(spec, out var done))
                {
                    splitDone[spec] = done = [];
                }

                var bone = NextSplit(spec, done);
                if (bone < 0)
                {
                    continue;
                }

                done.Add(bone);
                var groups = declarations[bone];
                spec.RingOf ??= [];
                spec.ChildrenOf ??= [];

                var resolved = spec.RingOf.ContainsKey(bone);
                var keep = resolved
                    ? groups.FindIndex(group => ReferenceEquals(group, spec.RingOf[bone]))
                    : bone == spec.Root ? 0 : BestGroup(groups, SideOf(realParent[bone]));

                var kids = KeptChildren(spec, bone);
                var membership = new List<Dictionary<int, List<int>>>(kids.Count);
                foreach (var kid in kids)
                {
                    var kidGroups = declarations.TryGetValue(kid, out var kg) ? kg : [SideOf(kid)];
                    var byGroup = new Dictionary<int, List<int>>();
                    for (var g = 0; g < groups.Count; g++)
                    {
                        var best = -1;
                        var bestHits = 0;
                        for (var h = 0; h < kidGroups.Count; h++)
                        {
                            var hits = RodCount(groups[g], kidGroups[h]);
                            if (hits > bestHits)
                            {
                                bestHits = hits;
                                best = h;
                            }
                        }

                        if (best >= 0)
                        {
                            byGroup[g] = kidGroups[best];
                        }
                    }

                    if (byGroup.Count == 0)
                    {
                        var reach = Reach(spec, kid);
                        for (var g = 0; g < groups.Count; g++)
                        {
                            if (RodCount(groups[g], reach) > 0)
                            {
                                byGroup[g] = kidGroups[0];
                            }
                        }
                    }

                    if (byGroup.Count < groups.Count && kidGroups.Count == groups.Count)
                    {
                        var free = kidGroups.FindAll(ring => !byGroup.ContainsValue(ring));
                        var next = 0;
                        for (var g = 0; g < groups.Count && next < free.Count; g++)
                        {
                            if (!byGroup.ContainsKey(g))
                            {
                                byGroup[g] = free[next++];
                            }
                        }
                    }

                    membership.Add(byGroup);
                }

                HashSet<int> KidsOf(int group)
                {
                    var kept = new HashSet<int>();
                    for (var k = 0; k < kids.Count; k++)
                    {
                        if (group < 0 ? membership[k].Count == 0 : membership[k].ContainsKey(group))
                        {
                            kept.Add(kids[k]);
                        }
                    }

                    return kept;
                }

                void PlaceKidRings(ChainSpec target, int group)
                {
                    for (var k = 0; k < kids.Count; k++)
                    {
                        if (declarations.ContainsKey(kids[k]) && membership[k].TryGetValue(group, out var ring))
                        {
                            target.RingOf![kids[k]] = ring;
                        }
                    }
                }

                if (!resolved)
                {
                    for (var g = 0; g < groups.Count; g++)
                    {
                        if (g == keep)
                        {
                            continue;
                        }

                        var extra = new ChainSpec
                        {
                            Root = bone,
                            RingOf = new Dictionary<int, List<int>>(spec.RingOf),
                            ChildrenOf = new Dictionary<int, HashSet<int>>(spec.ChildrenOf),
                            Suffix = "_decl" + ++declarationIndex,
                        };
                        extra.RingOf[bone] = groups[g];
                        extra.ChildrenOf[bone] = KidsOf(g);
                        PlaceKidRings(extra, g);
                        splitDone[extra] = [.. done];
                        specs.Add(extra);
                    }

                    var bare = KidsOf(-1);
                    if (keep >= 0 && bare.Count > 0)
                    {
                        var extra = new ChainSpec
                        {
                            Root = bone,
                            RinglessRoot = true,
                            RingOf = new Dictionary<int, List<int>>(spec.RingOf),
                            ChildrenOf = new Dictionary<int, HashSet<int>>(spec.ChildrenOf),
                            Suffix = "_decl" + ++declarationIndex,
                        };
                        extra.RingOf[bone] = [];
                        extra.ChildrenOf[bone] = bare;
                        splitDone[extra] = [.. done];
                        specs.Add(extra);
                    }

                    spec.RingOf[bone] = keep >= 0 ? groups[keep] : [];
                }

                spec.ChildrenOf[bone] = KidsOf(keep);
                PlaceKidRings(spec, keep);
                i--;
            }
        }

        private static readonly Quaternion ExtrudeAxisSelectY = new(0f, 0f, 0.70710677f, 0.70710677f);
        private static readonly Quaternion ExtrudeAxisSelectZ = new(0f, -0.70710677f, 0f, 0.70710677f);

        private static Quaternion ExtrudeAxisSelectQuaternion(char axis) => axis switch
        {
            'y' => ExtrudeAxisSelectY,
            'z' => ExtrudeAxisSelectZ,
            _ => Quaternion.Identity,
        };

        private const float ExtrudeForwardAxisTolerance = 0.02f;

        /// <summary>
        /// Detects the forward axis a joint's ring was laid out around: the local axis its points have no extent along,
        /// preferring <c>'x'</c>.
        /// </summary>
        private static char DetectExtrudeForwardAxis(Vector3 jointPos, Quaternion jointRot, List<int> ring, Vector3[] positions)
        {
            float sumX = 0f, sumY = 0f, sumZ = 0f;
            foreach (var proxy in ring)
            {
                if (proxy < 0 || proxy >= positions.Length)
                {
                    continue;
                }

                var local = Vector3.Transform(positions[proxy] - jointPos, Quaternion.Conjugate(jointRot));
                sumX += MathF.Abs(local.X);
                sumY += MathF.Abs(local.Y);
                sumZ += MathF.Abs(local.Z);
            }

            var scale = MathF.Max(sumX, MathF.Max(sumY, sumZ));
            if (scale <= 1e-6f)
            {
                return 'x';
            }

            var threshold = scale * ExtrudeForwardAxisTolerance;
            if (sumX <= threshold)
            {
                return 'x';
            }

            if (sumY <= threshold)
            {
                return 'y';
            }

            return sumZ <= threshold ? 'z' : 'x';
        }

        /// <summary>Gets the control nodes a proxy-sheet vertex hangs off through its ctrl offsets or soft offsets.</summary>
        private HashSet<int> SheetSkinnedNodes()
        {
            var result = new HashSet<int>();
            foreach (var offset in CtrlOffsets)
            {
                if (IsProxyMeshNode(offset.CtrlChild))
                {
                    result.Add(offset.CtrlParent);
                }
            }

            if (Data.GetArray("m_CtrlSoftOffsets") is { } softOffsets)
            {
                foreach (var entry in softOffsets)
                {
                    if (IsProxyMeshNode(entry.GetInt32Property("nCtrlChild")))
                    {
                        result.Add(entry.GetInt32Property("nCtrlParent"));
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// A recovered <c>ClothSelfCollisionCluster</c>: its member nodes, the length band of its pairwise rods, and each
        /// member's stiffness, whose product over a pair is that pair's relaxation.
        /// </summary>
        internal readonly record struct SelfCollisionCluster(int[] Nodes, float MinDist, float MaxDist, float[]? Stiffness = null);

        /// <summary>
        /// The smallest rod clique read as a cluster without <see cref="IsRadiusBandTriangle"/>.
        /// </summary>
        private const int SelfCollisionClusterMinMembers = 4;

        private List<SelfCollisionCluster>? selfCollisionClusters;

        private HashSet<int>? selfCollisionClusterRods;

        private HashSet<(int, int)>? selfCollisionClusterPairs;

        /// <summary>Gets the node pairs a recovered cluster puts one of its own rods on.</summary>
        internal IReadOnlySet<(int, int)> SelfCollisionClusterPairs
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
                                var a = cluster.Nodes[i];
                                var b = cluster.Nodes[j];
                                selfCollisionClusterPairs.Add(UnorderedPair(a, b));
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

        /// <summary>Gets the index into <see cref="Rods"/> of every rod a <see cref="SelfCollisionClusters"/> entry accounts for.</summary>
        internal IReadOnlySet<int> SelfCollisionClusterRods
            => selfCollisionClusterRods ??= BuildSelfCollisionClusterRods();

        private List<SelfCollisionCluster> BuildSelfCollisionClusters()
        {
            var found = new List<SelfCollisionCluster>();
            const int minMembers = 3;
            if (IsImportedCloth || Rods.Length < minMembers)
            {
                return found;
            }

            var sprung = new HashSet<(int, int)>();
            foreach (var (a, b) in SourceSprings)
            {
                sprung.Add(UnorderedPair(a, b));
            }

            var byBand = new Dictionary<(float, float), Dictionary<(int, int), (int Copies, float Relaxation)>>();
            foreach (var rod in Rods)
            {
                var pair = UnorderedPair(rod.NodeA, rod.NodeB);
                if (rod.MaxDist <= rod.MinDist || rod.RelaxationFactor <= 0f
                    || rod.Weight0 != 0.5f || sprung.Contains(pair)
                    || ImportedStripNodes.Contains(rod.NodeA) || ImportedStripNodes.Contains(rod.NodeB))
                {
                    continue;
                }

                var band = (rod.MinDist, rod.MaxDist);
                if (!byBand.TryGetValue(band, out var counts))
                {
                    byBand[band] = counts = [];
                }

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

                    Neighbours(neighbours, a).Add(b);
                    Neighbours(neighbours, b).Add(a);
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

            static HashSet<int> Neighbours(Dictionary<int, HashSet<int>> map, int node)
            {
                if (!map.TryGetValue(node, out var set))
                {
                    map[node] = set = [];
                }

                return set;
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
                if (node >= CtrlNames.Length || node >= InitPosePositions.Length || IsGeneratedNodeName(CtrlNames[node]))
                {
                    return false;
                }
            }

            for (var i = 0; i < members.Count; i++)
            {
                for (var j = i + 1; j < members.Count; j++)
                {
                    var rest = Vector3.Distance(InitPosePositions[members[i]], InitPosePositions[members[j]]);
                    if (MathF.Abs(bandMax - rest) <= MathF.Max(1e-3f, 1e-4f * rest))
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
                        var a = cluster.Nodes[i];
                        var b = cluster.Nodes[j];
                        var relaxation = cluster.Stiffness is { } stiffness ? stiffness[i] * stiffness[j] : 1f;
                        wanted[UnorderedPair(a, b)] = (cluster.MinDist, cluster.MaxDist, relaxation);
                    }
                }
            }

            for (var i = 0; i < Rods.Length; i++)
            {
                var rod = Rods[i];
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
        /// Gets the rod graph without cluster rods: the rodded pairs, every rod's relaxation per pair, the same for rigid
        /// rods alone, the relaxations a repeat could have written, and each rod's <c>flMinDist / flMaxDist</c>.
        /// </summary>
        private (HashSet<(int, int)> Pairs, Dictionary<(int, int), List<float>> RelaxationsByPair,
            Dictionary<(int, int), List<float>> RigidRelaxationsByPair,
            Dictionary<(int, int), List<float>> RepeatRelaxationsByPair,
            Dictionary<(int, int), List<float>> ContractionsByPair) BuildRodGraph()
        {
            static bool SameRecord(Rod x, Rod y)
                => MathF.Abs(x.MaxDist - y.MaxDist) <= 1e-4f * MathF.Max(1f, MathF.Abs(x.MaxDist))
                    && MathF.Abs(x.MinDist - y.MinDist) <= 1e-4f * MathF.Max(1f, MathF.Abs(x.MaxDist))
                    && MathF.Abs(x.RelaxationFactor - y.RelaxationFactor) <= 1e-4f
                    && MathF.Abs(x.Weight0 - y.Weight0) <= 1e-4f;

            var pairs = new HashSet<(int, int)>();
            var relaxationsByPair = new Dictionary<(int, int), List<float>>();
            var rigidRelaxationsByPair = new Dictionary<(int, int), List<float>>();
            var contractionsByPair = new Dictionary<(int, int), List<float>>();
            var rodsByPair = new Dictionary<(int, int), List<Rod>>();
            var clusterRods = SelfCollisionClusterRods;
            for (var index = 0; index < Rods.Length; index++)
            {
                if (clusterRods.Contains(index))
                {
                    continue;
                }

                var rod = Rods[index];
                var pair = UnorderedPair(rod.NodeA, rod.NodeB);
                pairs.Add(pair);
                if (!relaxationsByPair.TryGetValue(pair, out var relaxations))
                {
                    relaxations = [];
                    relaxationsByPair[pair] = relaxations;
                }

                relaxations.Add(rod.RelaxationFactor);

                if (rod.MaxDist > 0f)
                {
                    if (!contractionsByPair.TryGetValue(pair, out var contractions))
                    {
                        contractions = [];
                        contractionsByPair[pair] = contractions;
                    }

                    contractions.Add(rod.MinDist / rod.MaxDist);
                }

                if (!rodsByPair.TryGetValue(pair, out var all))
                {
                    all = [];
                    rodsByPair[pair] = all;
                }

                all.Add(rod);

                if (MathF.Abs(rod.MinDist - rod.MaxDist) <= 1e-4f * MathF.Max(1f, MathF.Abs(rod.MaxDist)))
                {
                    if (!rigidRelaxationsByPair.TryGetValue(pair, out var rigid))
                    {
                        rigid = [];
                        rigidRelaxationsByPair[pair] = rigid;
                    }

                    rigid.Add(rod.RelaxationFactor);
                }
            }

            var repeatRelaxationsByPair = new Dictionary<(int, int), List<float>>(rigidRelaxationsByPair);
            foreach (var (pair, rods) in rodsByPair)
            {
                if (repeatRelaxationsByPair.ContainsKey(pair) || rods.Exists(rod => !SameRecord(rod, rods[0])))
                {
                    continue;
                }

                repeatRelaxationsByPair[pair] = rods.ConvertAll(static rod => rod.RelaxationFactor);
            }

            return (pairs, relaxationsByPair, rigidRelaxationsByPair, repeatRelaxationsByPair,
                contractionsByPair);
        }

        /// <summary>
        /// Gets each real bone's <c>$cc</c> proxy nodes, and the inverse map from ring node to owning bone.
        /// </summary>
        private (Dictionary<int, List<int>> ChildrenOf, Dictionary<int, int> OwnerOf) BuildProxyRings()
        {
            var childrenOf = new Dictionary<int, List<int>>();

            for (var node = 0; node < CtrlNames.Length; node++)
            {
                if ((!CtrlNames[node].StartsWith("$cc", StringComparison.Ordinal)
                    && !(!IsProxyNodeName(CtrlNames[node]) && IsGeneratedNodeName(CtrlNames[node])))
                    || ImportedStripNodes.Contains(node))
                {
                    continue;
                }

                var pp = ParentNodeOf(node);
                if (pp >= 0)
                {
                    if (!childrenOf.TryGetValue(pp, out var list))
                    {
                        childrenOf[pp] = list = [];
                    }

                    list.Add(node);
                }
            }

            var ownerOf = new Dictionary<int, int>();
            foreach (var (owner, ring) in childrenOf)
            {
                foreach (var vertex in ring)
                {
                    ownerOf[vertex] = owner;
                }
            }

            return (childrenOf, ownerOf);
        }

        internal const float ChainRingCurvatureAgreement = 0.01f;

        /// <summary>
        /// Gets the authored <c>add_curvature</c> from the bend rods across each chain ring, whose minimum is
        /// <c>flMaxDist * sin(add_curvature * pi / 2)</c>; 0 when the rods disagree or there are none.
        /// </summary>
        internal float ChainRingCurvature
        {
            get
            {
                var (_children, ringOwnerOf) = BuildProxyRings();
                if (ringOwnerOf.Count == 0)
                {
                    return 0f;
                }

                var lowest = float.MaxValue;
                var highest = 0f;
                foreach (var rod in Rods)
                {
                    if (!ringOwnerOf.TryGetValue(rod.NodeA, out var ownerA)
                        || !ringOwnerOf.TryGetValue(rod.NodeB, out var ownerB)
                        || ownerA != ownerB
                        || rod.NodeA >= InitPosePositions.Length || rod.NodeB >= InitPosePositions.Length
                        || !CtrlNames[rod.NodeA].StartsWith("$cc", StringComparison.Ordinal)
                        || !CtrlNames[rod.NodeB].StartsWith("$cc", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var rest = Vector3.Distance(InitPosePositions[rod.NodeA], InitPosePositions[rod.NodeB]);
                    if (rod.MaxDist <= rest * 1.001f || rod.MaxDist <= 0f)
                    {
                        continue;
                    }

                    var reading = 2f / MathF.PI * MathF.Asin(Math.Clamp(rod.MinDist / rod.MaxDist, 0f, 1f));
                    lowest = MathF.Min(lowest, reading);
                    highest = MathF.Max(highest, reading);
                }

                if (lowest is float.MaxValue || highest - lowest > ChainRingCurvatureAgreement * highest)
                {
                    return 0f;
                }

                return highest;
            }
        }

        private static bool ReachesByParents(int[] realParent, int from, int to)
        {
            var guard = 0;
            for (var node = from; node >= 0 && guard++ < 256; node = realParent[node])
            {
                if (node == to)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reconstructs the bone chains from the control-node topology, ordered by the lowest simulated node each one
        /// occupies (or its lowest static node when it has none).
        /// </summary>
        internal List<BoneChain> BuildBoneChains() => BuildBoneChains(null, null);

        /// <summary>
        /// Reconstructs the bone chains as <see cref="BuildBoneChains()"/> does, and declares a merged root twice where
        /// its sub-chains were staged at two versions and <paramref name="chainVersion"/> reads the merged declaration as
        /// version 1.
        /// </summary>
        /// <param name="chainVersion">The <c>ClothChain</c> version a chain reads as, given whether the model declares another chain.</param>
        internal List<BoneChain> BuildBoneChains(Func<BoneChain, bool, int> chainVersion) => BuildBoneChains(chainVersion, null);

        /// <summary>
        /// Builds the chains with each root bone <paramref name="ringlessKids"/> names declared twice: once
        /// extruding it over its other children, and once restating it ringless over the children listed.
        /// </summary>
        private List<BoneChain> BuildBoneChains(Func<BoneChain, bool, int>? chainVersion, Dictionary<int, HashSet<int>>? ringlessKids)
        {
            var chains = new List<BoneChain>();
            var mergedChains = new List<BoneChain>();

            Vector3 ExtrudeOrigin(int node)
                => ChainExtrudeOrigins is { } origins && node < CtrlNames.Length
                    && origins.TryGetValue(CtrlNames[node], out var origin)
                    ? origin
                    : InitPosePositions[node];
            var chainFirstSimulated = new Dictionary<BoneChain, int>();
            var n = CtrlNames.Length;
            if (n == 0)
            {
                return chains;
            }

            var isReal = new bool[n];
            for (var i = 0; i < n; i++)
            {
                isReal[i] = !IsGeneratedNodeName(CtrlNames[i]) && !ImportedStripNodes.Contains(i);
            }

            var (rodPairs, rodRelaxationsByPair, rigidRodRelaxationsByPair, repeatRodRelaxationsByPair,
                rodContractionsByPair) = BuildRodGraph();
            var (proxyChildrenOf, ringOwnerOf) = BuildProxyRings();

            HashSet<string>? surfaceElements = null;
            bool RinglessLinkUnrecorded(int parent, int child)
            {
                if (SourceFaces.Length == 0 || proxyChildrenOf.ContainsKey(parent)
                    || !proxyChildrenOf.TryGetValue(child, out var ring) || ring.Count < 2
                    || !DrivesProxySheetVertex(parent))
                {
                    return false;
                }

                if (surfaceElements is null)
                {
                    surfaceElements = [];
                    var recordsChainSurfaces = false;
                    foreach (var face in SourceFaces)
                    {
                        surfaceElements.Add(SurfaceElementKey(face));
                        recordsChainSurfaces |= Array.Exists(face, ringOwnerOf.ContainsKey);
                    }

                    if (!recordsChainSurfaces)
                    {
                        surfaceElements.Clear();
                    }
                }

                return surfaceElements.Count > 0 && !surfaceElements.Contains(SurfaceElementKey([parent, .. ring]));
            }

            bool IsCentreRing(List<int> ring)
                => ring.TrueForAll(node => CtrlNames[node].EndsWith("_Ctr", StringComparison.Ordinal));

            bool RingsShareFace(List<int> a, List<int> b)
                => Array.Exists(SourceFaces, face => Array.Exists(face, a.Contains) && Array.Exists(face, b.Contains));

            bool? chainSurfacesRecorded = null;
            var unlinkedRingChildren = new HashSet<int>();

            bool RingLinkUnrecorded(int parent, int child)
            {
                if (HasCompiledSkelParents || SourceFaces.Length == 0
                    || !proxyChildrenOf.TryGetValue(parent, out var parentRing) || !proxyChildrenOf.TryGetValue(child, out var childRing)
                    || IsCentreRing(parentRing) || IsCentreRing(childRing))
                {
                    return false;
                }

                chainSurfacesRecorded ??= Array.Exists(SourceFaces, face => Array.Exists(face, ringOwnerOf.ContainsKey));
                if (chainSurfacesRecorded == false || RingsShareFace(parentRing, childRing))
                {
                    return false;
                }

                unlinkedRingChildren.Add(child);
                return true;
            }

            bool SkeletonReaches(int from, int to)
            {
                var guard = 0;
                for (var node = from < SkelParents.Length ? SkelParents[from] : -1; node >= 0 && guard++ < 256;
                    node = node < SkelParents.Length ? SkelParents[node] : -1)
                {
                    if (node == to)
                    {
                        return true;
                    }
                }

                if (SkeletonBoneParents is not { } boneParents)
                {
                    return false;
                }

                for (var name = boneParents.GetValueOrDefault(CtrlNames[from]); name is not null && guard++ < 512;
                    name = boneParents.GetValueOrDefault(name))
                {
                    if (string.Equals(name, CtrlNames[to], StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }

            var ropeParents = HasCompiledSkelParents ? RopeRunParents : new Dictionary<int, int>();
            bool EndsItsChain(int node) => Array.IndexOf(CtrlNames, "$cc" + CtrlNames[node] + "_Ctr") >= 0;

            var realParent = new int[n];
            var children = new List<int>?[n];
            var roots = new List<int>();

            for (var i = 0; i < n; i++)
            {
                realParent[i] = -1;

                if (!isReal[i])
                {
                    continue;
                }

                var p = i < SkelParents.Length ? SkelParents[i] : -1;
                if (p < 0 || p >= n || !isReal[p])
                {
                    continue;
                }

                var rodLinked = rodPairs.Contains(UnorderedPair(p, i));

                var bothDrivenSim = i >= FirstPositionDrivenNode && p >= FirstPositionDrivenNode
                    && i < NodeInvMasses.Length && NodeInvMasses[i] != 0f
                    && p < NodeInvMasses.Length && NodeInvMasses[p] != 0f;

                var proxyRibbon = proxyChildrenOf.ContainsKey(i);

                var hingedRoot = Array.IndexOf(CtrlNames, HingeAnchorPrefix + CtrlNames[p]) >= 0
                    || RigidHingeJoints.ContainsKey(p);

                var bendLinked = false;
                foreach (var bend in KelagerBends)
                {
                    if (bend.MidNode == p && (bend.End0 == i || bend.End1 == i))
                    {
                        bendLinked = true;
                        break;
                    }
                }

                var grandParent = p < SkelParents.Length ? SkelParents[p] : -1;
                var bendRodLinked = grandParent >= 0 && grandParent < n && isReal[grandParent]
                    && rodPairs.Contains(UnorderedPair(grandParent, i));

                var ringLinked = false;
                if (proxyChildrenOf.TryGetValue(p, out var parentRing))
                {
                    foreach (var ring in parentRing)
                    {
                        if (rodPairs.Contains(UnorderedPair(ring, i)))
                        {
                            ringLinked = true;
                            break;
                        }
                    }
                }

                var ropeLinked = ropeParents.TryGetValue(i, out var ropeParent) && ropeParent == p && !EndsItsChain(p);

                var twistLinked = TwistLinks.Contains(UnorderedPair(p, i));

                if ((rodLinked || bothDrivenSim || proxyRibbon || hingedRoot || bendLinked
                    || bendRodLinked || ringLinked || ropeLinked || twistLinked) && !RinglessLinkUnrecorded(p, i) && !RingLinkUnrecorded(p, i))
                {
                    realParent[i] = p;
                }
            }

            for (var linked = true; linked;)
            {
                linked = false;
                for (var i = 0; i < n; i++)
                {
                    if (!isReal[i] || realParent[i] >= 0)
                    {
                        continue;
                    }

                    var p = i < SkelParents.Length ? SkelParents[i] : -1;
                    if (p < 0 || p >= n || !isReal[p] || RinglessLinkUnrecorded(p, i) || RingLinkUnrecorded(p, i))
                    {
                        continue;
                    }

                    for (var child = 0; child < n && realParent[i] < 0; child++)
                    {
                        if (child != p && realParent[child] == i
                            && rodPairs.Contains(UnorderedPair(p, child)))
                        {
                            realParent[i] = p;
                            linked = true;
                        }
                    }
                }
            }

            if (SkeletonBoneParents is not null)
            {
                var sheetSkinned = SheetSkinnedNodes();
                var linkedChildren = new int[n];
                foreach (var parent in realParent)
                {
                    if (parent >= 0)
                    {
                        linkedChildren[parent]++;
                    }
                }

                bool InChain(int node) => realParent[node] >= 0 || linkedChildren[node] > 0
                    || proxyChildrenOf.ContainsKey(node);

                for (var linked = true; linked;)
                {
                    linked = false;
                    for (var i = 0; i < n; i++)
                    {
                        if (!isReal[i] || realParent[i] >= 0)
                        {
                            continue;
                        }

                        var p = i < SkelParents.Length ? SkelParents[i] : -1;
                        if (p < 0 || p >= n || !isReal[p] || (!InChain(i) && !InChain(p))
                            || sheetSkinned.Contains(i) || sheetSkinned.Contains(p) || RingLinkUnrecorded(p, i))
                        {
                            continue;
                        }

                        if ((i < NodeInvMasses.Length && NodeInvMasses[i] != 0f)
                            || (p < NodeInvMasses.Length && NodeInvMasses[p] != 0f))
                        {
                            continue;
                        }

                        if (string.Equals(SkeletonBoneParents.GetValueOrDefault(CtrlNames[i]), CtrlNames[p],
                            StringComparison.OrdinalIgnoreCase))
                        {
                            realParent[i] = p;
                            linkedChildren[p]++;
                            linked = true;
                        }
                    }
                }
            }

            if (!HasCompiledSkelParents && Rods.Length > 0)
            {
                var ringOwner = new Dictionary<int, int>();
                foreach (var (owner, ring) in proxyChildrenOf)
                {
                    foreach (var vertex in ring)
                    {
                        ringOwner[vertex] = owner;
                    }
                }

                int OwnerOf(int node) => ringOwner.TryGetValue(node, out var owner)
                    ? owner
                    : node < n && isReal[node] ? node : -1;

                var linkCounts = new Dictionary<(int, int), int>();
                foreach (var rod in Rods)
                {
                    var a = OwnerOf(rod.NodeA);
                    var b = OwnerOf(rod.NodeB);
                    if (a < 0 || b < 0 || a == b)
                    {
                        continue;
                    }

                    if (!proxyChildrenOf.ContainsKey(a) && !proxyChildrenOf.ContainsKey(b))
                    {
                        continue;
                    }

                    var key = UnorderedPair(a, b);
                    linkCounts[key] = linkCounts.GetValueOrDefault(key) + 1;
                }

                var nodeByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < n; i++)
                {
                    if (isReal[i])
                    {
                        nodeByName.TryAdd(CtrlNames[i], i);
                    }
                }

                if (SkeletonBoneParents is not null)
                {
                    for (var i = 0; i < n; i++)
                    {
                        if (!isReal[i] || realParent[i] >= 0)
                        {
                            continue;
                        }

                        var ancestor = SkeletonBoneParents.GetValueOrDefault(CtrlNames[i]);
                        var guard = 0;
                        while (ancestor is not null && guard++ < 256)
                        {
                            if (nodeByName.TryGetValue(ancestor, out var p) && p != i)
                            {
                                var key = UnorderedPair(p, i);
                                if (linkCounts.ContainsKey(key))
                                {
                                    if (!RingLinkUnrecorded(p, i))
                                    {
                                        realParent[i] = p;
                                    }

                                    break;
                                }
                            }

                            ancestor = SkeletonBoneParents.GetValueOrDefault(ancestor);
                        }
                    }
                }

                var bestParent = new Dictionary<int, (int Parent, int Count)>();
                foreach (var ((low, high), count) in linkCounts)
                {
                    if (!bestParent.TryGetValue(high, out var current) || count > current.Count
                        || (count == current.Count && low < current.Parent))
                    {
                        bestParent[high] = (low, count);
                    }
                }

                foreach (var (child, link) in bestParent)
                {
                    if (realParent[child] < 0 && realParent[link.Parent] != child
                        && !ReachesByParents(realParent, link.Parent, child)
                        && !unlinkedRingChildren.Contains(child) && !RingLinkUnrecorded(link.Parent, child))
                    {
                        realParent[child] = link.Parent;
                    }
                }
            }

            for (var resolved = true; resolved;)
            {
                resolved = false;
                foreach (var child in unlinkedRingChildren)
                {
                    if (realParent[child] >= 0)
                    {
                        continue;
                    }

                    var ring = proxyChildrenOf[child];
                    var candidate = -1;
                    var candidates = 0;
                    foreach (var (owner, ownerRing) in proxyChildrenOf)
                    {
                        if (owner == child || owner >= n || !isReal[owner] || IsCentreRing(ownerRing)
                            || SkeletonReaches(owner, child) || ReachesByParents(realParent, owner, child)
                            || !RingsShareFace(ring, ownerRing))
                        {
                            continue;
                        }

                        candidate = owner;
                        candidates++;
                    }

                    if (candidates == 1)
                    {
                        realParent[child] = candidate;
                        resolved = true;
                    }
                }
            }

            for (var i = 0; i < n; i++)
            {
                if (!isReal[i])
                {
                    continue;
                }

                var p = realParent[i];
                if (p < 0)
                {
                    roots.Add(i);
                }
                else
                {
                    (children[p] ??= []).Add(i);
                }
            }

            bool AnyRod(IEnumerable<int> a, IEnumerable<int> b)
            {
                foreach (var x in a)
                {
                    foreach (var y in b)
                    {
                        if (rodPairs.Contains(UnorderedPair(x, y)))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            void SplitGroupsByFitSet(int rootNode, List<(List<int> Kids, bool RinglessRoot)> groups)
            {
                for (var g = 0; g < groups.Count; g++)
                {
                    var (kids, ringless) = groups[g];
                    foreach (var kid in kids)
                    {
                        if (!FitMatrixTargets.TryGetValue(kid, out var targets) || targets.Length == 0
                            || ProxyFitMatrixNodes.Contains(kid))
                        {
                            continue;
                        }

                        var owners = targets.Select(t => ringOwnerOf.GetValueOrDefault(t, t)).ToHashSet();
                        if (!owners.Contains(rootNode) || !owners.Contains(kid))
                        {
                            continue;
                        }

                        var outsiders = kids.FindAll(k => !owners.Contains(k));
                        if (outsiders.Count == 0 || outsiders.Count == kids.Count)
                        {
                            continue;
                        }

                        groups[g] = (kids.FindAll(k => owners.Contains(k)), ringless);
                        groups.Add((outsiders, ringless));
                        g--;
                        break;
                    }
                }
            }

            var chainSpecs = new List<ChainSpec>();
            foreach (var rootNode in roots)
            {
                if (children[rootNode] is not { } rootKids)
                {
                    if (!proxyChildrenOf.ContainsKey(rootNode))
                    {
                        continue;
                    }

                    chainSpecs.Add(new ChainSpec { Root = rootNode });
                    continue;
                }

                var looseKids = rootKids.Count > 1 && proxyChildrenOf.TryGetValue(rootNode, out var ownRing) && ownRing.Count > 0
                    ? rootKids.FindAll(kid => IsStatic(kid) && children[kid] is null
                        && proxyChildrenOf.TryGetValue(kid, out var kidRing) && kidRing.Count > 0
                        && !AnyRod([rootNode, .. ownRing], [kid, .. kidRing]))
                    : [];
                if (looseKids.Count == rootKids.Count)
                {
                    looseKids.Clear();
                }

                var keptKids = looseKids.Count > 0 ? rootKids.FindAll(kid => !looseKids.Contains(kid)) : rootKids;

                List<int> ringAnchored = [];
                List<int> boneAnchored = [];
                if (keptKids.Count > 1 && proxyChildrenOf.TryGetValue(rootNode, out var rootRing) && rootRing.Count > 0)
                {
                    foreach (var kid in keptKids)
                    {
                        var kidSide = proxyChildrenOf.TryGetValue(kid, out var kr) && kr.Count > 0 ? kr : [kid];
                        if (AnyRod(rootRing, kidSide))
                        {
                            ringAnchored.Add(kid);
                        }
                        else if (AnyRod([rootNode], kidSide))
                        {
                            boneAnchored.Add(kid);
                        }
                        else
                        {
                            ringAnchored.Clear();
                            boneAnchored.Clear();
                            break;
                        }
                    }
                }

                var groups = ringAnchored.Count > 0 && boneAnchored.Count > 0
                    ? [(ringAnchored, false), (boneAnchored, true)]
                    : new List<(List<int> Kids, bool RinglessRoot)> { (keptKids, false) };

                SplitGroupsByFitSet(rootNode, groups);

                if (ringlessKids is not null && ringlessKids.TryGetValue(rootNode, out var versionRingless)
                    && groups.Count == 1 && groups[0].Kids.Count == rootKids.Count)
                {
                    groups = [(rootKids.FindAll(kid => !versionRingless.Contains(kid)), false),
                        (rootKids.FindAll(versionRingless.Contains), true)];
                }

                if (groups.Count == 1 && groups[0].Kids.Count == rootKids.Count)
                {
                    chainSpecs.Add(new ChainSpec { Root = rootNode });
                }
                else
                {
                    foreach (var (kids, ringless) in groups)
                    {
                        chainSpecs.Add(new ChainSpec
                        {
                            Root = rootNode,
                            RinglessRoot = ringless,
                            ChildrenOf = new Dictionary<int, HashSet<int>> { [rootNode] = [.. kids] },
                        });
                    }
                }

                foreach (var kid in looseKids)
                {
                    chainSpecs.Add(new ChainSpec { Root = kid });
                }
            }

            SplitRingDeclarations(chainSpecs, children, realParent, proxyChildrenOf, rodPairs);

            foreach (var spec in chainSpecs)
            {
                var rootNode = spec.Root;
                var ringlessRoot = spec.RinglessRoot;
                var chain = new BoneChain { RootBone = CtrlNames[rootNode], DeclarationSuffix = spec.Suffix };

                List<int>? DeclaredRing(int node)
                    => spec.RingOf is not null && spec.RingOf.TryGetValue(node, out var ring)
                        ? ring
                        : proxyChildrenOf.GetValueOrDefault(node);

                var subtreeFirstNode = new Dictionary<int, int>();

                List<int> DeclaredChildren(int node)
                {
                    if (children[node] is not { } all)
                    {
                        return [];
                    }

                    var kids = spec.ChildrenOf is not null && spec.ChildrenOf.TryGetValue(node, out var kept)
                        ? all.FindAll(kept.Contains)
                        : [.. all];

                    kids.Sort((a, b) =>
                    {
                        var order = SubtreeFirstNode(a).CompareTo(SubtreeFirstNode(b));
                        return order != 0 ? order : a.CompareTo(b);
                    });

                    return kids;
                }

                int SubtreeFirstNode(int start)
                {
                    if (subtreeFirstNode.TryGetValue(start, out var cached))
                    {
                        return cached;
                    }

                    var first = int.MaxValue;
                    var firstSimulated = int.MaxValue;
                    var stack = new Stack<int>();
                    stack.Push(start);
                    for (var guard = 0; stack.Count > 0 && guard < 4096; guard++)
                    {
                        var node = stack.Pop();
                        foreach (var member in (int[])[node, .. DeclaredRing(node) ?? []])
                        {
                            if (member < 0)
                            {
                                continue;
                            }

                            first = Math.Min(first, member);
                            if (member >= StaticNodeCount && member < FirstPositionDrivenNode)
                            {
                                firstSimulated = Math.Min(firstSimulated, member);
                            }
                        }

                        if (children[node] is not { } all)
                        {
                            continue;
                        }

                        foreach (var kid in all)
                        {
                            if (spec.ChildrenOf is null || !spec.ChildrenOf.TryGetValue(node, out var kept)
                                || kept.Contains(kid))
                            {
                                stack.Push(kid);
                            }
                        }
                    }

                    return subtreeFirstNode[start] = firstSimulated < int.MaxValue ? firstSimulated : first;
                }

                void Visit(int node)
                {
                    var parent = node == rootNode ? -1 : realParent[node];
                    chain.Joints.Add(new BoneChainJoint
                    {
                        Node = node,
                        Name = CtrlNames[node],
                        ParentNode = parent,
                        ParentName = parent >= 0 ? CtrlNames[parent] : null,
                        InvMass = node < NodeInvMasses.Length ? NodeInvMasses[node] : 0f,
                    });

                    foreach (var child in DeclaredChildren(node))
                    {
                        Visit(child);
                    }
                }

                Visit(rootNode);

                var sideFrequency = new Dictionary<int, int>();
                var radii = new List<float>();
                var twists = new List<float>();
                var jointRingOf = new Dictionary<int, List<int>>();
                var endEffectorRingOf = new Dictionary<int, List<int>>();
                foreach (var joint in chain.Joints)
                {
                    if (ringlessRoot && joint.Node == rootNode)
                    {
                        continue;
                    }

                    if (DeclaredRing(joint.Node) is not { Count: > 0 } proxies)
                    {
                        continue;
                    }

                    joint.RingNodes = [.. proxies.Order()];

                    if (spec.RingOf is not null && spec.RingOf.ContainsKey(joint.Node))
                    {
                        joint.ValueNode = proxies[0];
                        joint.InvMass = proxies[0] < NodeInvMasses.Length ? NodeInvMasses[proxies[0]] : joint.InvMass;
                    }

                    var ring = proxies;
                    List<int>? endEffectorRing = null;
                    if (proxies.TrueForAll(p => CtrlNames[p].EndsWith("_Ctr", StringComparison.Ordinal))
                        && joint.Node < InitPoseRotations.Length && joint.Node < InitPosePositions.Length
                        && proxies[0] < InitPosePositions.Length)
                    {
                        var centreOffset = Vector3.Transform(
                            InitPosePositions[proxies[0]] - ExtrudeOrigin(joint.Node),
                            Quaternion.Conjugate(InitPoseRotations[joint.Node]));
                        if (MathF.Abs(centreOffset.X) >= EndEffectorRingTolerance)
                        {
                            joint.EndEffector = centreOffset.X;
                            joint.ExtrudeSides = 0;
                            joint.ProxyNode = proxies[0];
                            endEffectorRingOf[joint.Node] = proxies;
                            continue;
                        }
                    }

                    if (joint.Node < InitPoseRotations.Length && joint.Node < InitPosePositions.Length)
                    {
                        var forwardOf = new Dictionary<int, float>(proxies.Count);
                        foreach (var proxy in proxies)
                        {
                            if (proxy < InitPosePositions.Length)
                            {
                                forwardOf[proxy] = Vector3.Transform(
                                    InitPosePositions[proxy] - ExtrudeOrigin(joint.Node),
                                    Quaternion.Conjugate(InitPoseRotations[joint.Node])).X;
                            }
                        }

                        if (forwardOf.Count == proxies.Count)
                        {
                            var minAbs = forwardOf.Values.Min(MathF.Abs);
                            var maxAbs = forwardOf.Values.Max(MathF.Abs);
                            if (maxAbs - minAbs > EndEffectorRingTolerance)
                            {
                                var nearRing = proxies.Where(p => MathF.Abs(forwardOf[p]) - minAbs <= EndEffectorRingTolerance).ToList();
                                if (nearRing.Count > 0 && nearRing.Count < proxies.Count)
                                {
                                    var farRing = proxies.Except(nearRing).ToList();
                                    var nearValue = forwardOf[nearRing.MinBy(p => MathF.Abs(forwardOf[p]))];
                                    var farValue = forwardOf[farRing.MaxBy(p => MathF.Abs(forwardOf[p]))];
                                    joint.EndEffector = farValue - nearValue;
                                    ring = nearRing;
                                    endEffectorRing = farRing;
                                    endEffectorRingOf[joint.Node] = farRing;
                                }
                            }
                        }
                    }

                    joint.ExtrudeSides = Math.Min(ring.Count, 4);
                    joint.ProxyNode = ring[0];
                    jointRingOf[joint.Node] = ring;
                    sideFrequency[ring.Count] = sideFrequency.GetValueOrDefault(ring.Count) + 1;
                    proxies = ring;
                    if (joint.Node < InitPosePositions.Length)
                    {
                        if (joint.Node < InitPoseRotations.Length)
                        {
                            joint.ForwardAxis = DetectExtrudeForwardAxis(
                                ExtrudeOrigin(joint.Node), InitPoseRotations[joint.Node], proxies, InitPosePositions);
                        }

                        var measured = endEffectorRing is { Count: > 0 } && IsHingedJoint(joint.Node)
                            ? endEffectorRing
                            : proxies;

                        if (joint.Node < InitPoseRotations.Length && measured[0] < InitPosePositions.Length)
                        {
                            var ringFrame = InitPoseRotations[joint.Node] * ExtrudeAxisSelectQuaternion(joint.ForwardAxis);
                            var offset = Vector3.Transform(
                                InitPosePositions[measured[0]] - ExtrudeOrigin(joint.Node),
                                Quaternion.Conjugate(ringFrame));
                            if (new Vector2(offset.Y, offset.Z).LengthSquared() > 1e-6f)
                            {
                                var twist = float.RadiansToDegrees(MathF.Atan2(offset.Y, offset.Z));
                                joint.ExtrudeTwist = twist;
                                twists.Add(twist);
                            }

                            joint.ExtrudeRadius = measured == proxies
                                ? Vector3.Distance(ExtrudeOrigin(joint.Node), InitPosePositions[measured[0]])
                                : new Vector2(offset.Y, offset.Z).Length();
                        }

                        foreach (var proxy in proxies)
                        {
                            if (proxy < InitPosePositions.Length)
                            {
                                radii.Add(Vector3.Distance(ExtrudeOrigin(joint.Node), InitPosePositions[proxy]));
                            }
                        }
                    }
                }

                var bodySides = sideFrequency
                    .OrderByDescending(static kv => kv.Value)
                    .ThenBy(static kv => kv.Key)
                    .Select(static kv => kv.Key)
                    .FirstOrDefault();

                if (bodySides >= 1)
                {
                    chain.ExtrudeSides = Math.Min(bodySides, 4);
                    chain.ExtrudeRadius = radii.Count > 0 ? radii.Average() : 0f;
                    chain.ExtrudeTwist = twists.Count > 0 ? twists.Average() : 0f;
                }

                var jointByNode = chain.Joints.ToDictionary(static j => j.Node);

                List<int> Side(int end)
                {
                    if (ringlessRoot && end == rootNode)
                    {
                        return [end];
                    }

                    if (jointRingOf.TryGetValue(end, out var jointRing) && jointRing.Count > 0)
                    {
                        return jointRing;
                    }

                    if (endEffectorRingOf.ContainsKey(end))
                    {
                        return [end];
                    }

                    return DeclaredRing(end) is { Count: > 0 } ring ? ring : [end];
                }

                var chainNodes = new HashSet<int>();
                foreach (var chainJoint in chain.Joints)
                {
                    chainNodes.Add(chainJoint.Node);
                    foreach (var ringNode in Side(chainJoint.Node))
                    {
                        chainNodes.Add(ringNode);
                    }
                }

                var crossesRoot = new HashSet<(int, int)>();
                foreach (var chainJoint in chain.Joints)
                {
                    if (chainJoint.Node == rootNode)
                    {
                        continue;
                    }

                    foreach (var a in (int[])[chainJoint.Node, .. Side(chainJoint.Node)])
                    {
                        foreach (var b in (int[])[rootNode, .. Side(rootNode)])
                        {
                            crossesRoot.Add(UnorderedPair(a, b));
                        }
                    }
                }

                float? NaturalRf(bool? crossingRoot)
                {
                    float? natural = null;
                    foreach (var kv in rodRelaxationsByPair)
                    {
                        if (kv.Value.Count != 1
                            || !chainNodes.Contains(kv.Key.Item1) || !chainNodes.Contains(kv.Key.Item2)
                            || (crossingRoot is { } want && crossesRoot.Contains(kv.Key) != want))
                        {
                            continue;
                        }

                        if (natural is { } already && MathF.Abs(already - kv.Value[0]) > 1e-4f)
                        {
                            return null;
                        }

                        natural = kv.Value[0];
                    }

                    return natural;
                }

                var chainNaturalRf = NaturalRf(null);
                if (chainNaturalRf is null && NaturalRf(true) is { } acrossRoot
                    && NaturalRf(false) is { } withoutRoot
                    && MathF.Abs(acrossRoot - withoutRoot) > 1e-4f)
                {
                    chainNaturalRf = withoutRoot;
                }

                float? RelaxationAcross(List<int> lhs, int other)
                {
                    if (other < 0 || lhs.Count == 0)
                    {
                        return null;
                    }

                    return Across(rodRelaxationsByPair) ?? Across(rigidRodRelaxationsByPair);

                    float? Across(Dictionary<(int, int), List<float>> byPair)
                    {
                        float? value = null;
                        foreach (var a in lhs)
                        {
                            foreach (var b in Side(other))
                            {
                                if (!byPair.TryGetValue(UnorderedPair(a, b), out var relaxations))
                                {
                                    return null;
                                }

                                foreach (var relaxation in relaxations)
                                {
                                    if (value is { } already && MathF.Abs(already - relaxation) > 1e-4f)
                                    {
                                        return null;
                                    }

                                    value = relaxation;
                                }
                            }
                        }

                        return value;
                    }
                }

                float? RingInternalRelaxation(int node)
                {
                    if (DeclaredRing(node) is not { Count: > 0 } ring)
                    {
                        return null;
                    }

                    var extrusion = new List<int>(ring) { node };
                    extrusion.Sort();
                    return Inside(rodRelaxationsByPair) ?? Inside(rigidRodRelaxationsByPair);

                    float? Inside(Dictionary<(int, int), List<float>> byPair)
                    {
                        float? value = null;
                        for (var i = 0; i < extrusion.Count; i++)
                        {
                            for (var j = i + 1; j < extrusion.Count; j++)
                            {
                                if (!byPair.TryGetValue((extrusion[i], extrusion[j]), out var relaxations))
                                {
                                    continue;
                                }

                                foreach (var relaxation in relaxations)
                                {
                                    if (value is { } already && MathF.Abs(already - relaxation) > 1e-4f)
                                    {
                                        return null;
                                    }

                                    value = relaxation;
                                }
                            }
                        }

                        return value;
                    }
                }

                float? SpanRelaxation(int node, int other) => RelaxationAcross(Side(node), other);

                bool Declared((int, int) pair) => rodPairs.Contains(pair) && !SurfaceFoldOnlyPairs.Contains(pair);

                bool SpannedByDeclaredRod(int node, int other)
                    => other >= 0 && (Declared(UnorderedPair(node, other)) || AllDeclared(Side(node), other));

                bool AllDeclared(List<int> lhs, int other)
                    => other >= 0 && lhs.Count > 0 && lhs.All(a => Side(other).All(b => Declared(UnorderedPair(a, b))));

                bool AllSpanned(List<int> lhs, int other)
                {
                    if (other < 0 || lhs.Count == 0)
                    {
                        return false;
                    }

                    foreach (var a in lhs)
                    {
                        foreach (var b in Side(other))
                        {
                            if (!rodPairs.Contains(UnorderedPair(a, b)))
                            {
                                return false;
                            }
                        }
                    }

                    return true;
                }

                bool SpannedByRod(int node, int other)
                {
                    if (other < 0)
                    {
                        return false;
                    }

                    if (rodPairs.Contains(UnorderedPair(node, other)))
                    {
                        return true;
                    }

                    return AllSpanned(Side(node), other);
                }

                List<int> EndEffectorRing(int node)
                    => endEffectorRingOf.TryGetValue(node, out var far) ? far : [];

                int SpanCopies(BoneChainJoint joint, int other)
                {
                    if (other < 0)
                    {
                        return -1;
                    }

                    var copies = 0;
                    foreach (var a in Side(joint.Node))
                    {
                        foreach (var b in Side(other))
                        {
                            var count = repeatRodRelaxationsByPair.TryGetValue(UnorderedPair(a, b), out var repeat)
                                ? repeat.Count
                                : 0;
                            if (count == 0 || (copies != 0 && count != copies))
                            {
                                return 0;
                            }

                            copies = count;
                        }
                    }

                    return copies;
                }

                float JointContraction(BoneChainJoint joint, int parent, int grand, int greatGrand)
                {
                    float? found = null;
                    foreach (var other in (int[])[parent, joint.BendSpring ? grand : -1,
                        joint.TorsionSpring ? greatGrand : -1])
                    {
                        if (other < 0)
                        {
                            continue;
                        }

                        foreach (var a in Side(joint.Node))
                        {
                            foreach (var b in Side(other))
                            {
                                if (!rodContractionsByPair.TryGetValue(UnorderedPair(a, b),
                                    out var contractions))
                                {
                                    continue;
                                }

                                foreach (var contraction in contractions)
                                {
                                    if (found is { } already && MathF.Abs(already - contraction) > 1e-4f)
                                    {
                                        return 1f;
                                    }

                                    found = contraction;
                                }
                            }
                        }
                    }

                    return found is { } reading ? Math.Clamp(reading, 0f, 1f) : 1f;
                }

                int JointCopies(BoneChainJoint joint, bool floor = false)
                {
                    var copies = 0;

                    bool Repeats(int other)
                    {
                        if (other < 0)
                        {
                            return true;
                        }

                        foreach (var a in Side(joint.Node))
                        {
                            foreach (var b in Side(other))
                            {
                                var count = repeatRodRelaxationsByPair.TryGetValue(UnorderedPair(a, b),
                                    out var repeat)
                                    ? repeat.Count
                                    : 0;
                                if (count == 0 || (!floor && copies != 0 && count != copies))
                                {
                                    return false;
                                }

                                copies = floor && copies != 0 ? Math.Min(copies, count) : count;
                            }
                        }

                        return true;
                    }

                    var parentNode = joint.ParentNode;
                    var grand = parentNode >= 0 && jointByNode.TryGetValue(parentNode, out var g1) ? g1.ParentNode : -1;
                    var greatGrand = grand >= 0 && jointByNode.TryGetValue(grand, out var g2) ? g2.ParentNode : -1;

                    if (!Repeats(parentNode)
                        || (joint.BendSpring && !Repeats(grand))
                        || (joint.TorsionSpring && !Repeats(greatGrand)))
                    {
                        if (copies != 0 || ChildSiblingValue(joint) == 0f)
                        {
                            return 1;
                        }

                        return Math.Max(SiblingCopies(joint), 1);
                    }

                    if (copies == 0)
                    {
                        var onlyChild = -1;
                        foreach (var other in chain.Joints)
                        {
                            if (other.ParentNode != joint.Node)
                            {
                                continue;
                            }

                            if (onlyChild >= 0)
                            {
                                onlyChild = -1;
                                break;
                            }

                            onlyChild = other.Node;
                        }

                        if (onlyChild >= 0 && !Repeats(onlyChild))
                        {
                            copies = 0;
                        }
                    }

                    if (copies == 0 && ChildSiblingValue(joint) != 0f)
                    {
                        copies = SiblingCopies(joint);
                    }

                    return Math.Max(copies, 1);
                }

                int SiblingCopies(BoneChainJoint joint)
                {
                    var kids = chain.Joints.FindAll(kid => kid.ParentNode == joint.Node);
                    var common = 0;
                    for (var i = 0; i < kids.Count; i++)
                    {
                        for (var j = 0; j < i; j++)
                        {
                            foreach (var a in Side(kids[j].Node))
                            {
                                foreach (var b in Side(kids[i].Node))
                                {
                                    if (!repeatRodRelaxationsByPair.TryGetValue(UnorderedPair(a, b),
                                        out var repeat))
                                    {
                                        continue;
                                    }

                                    common = common == 0 ? repeat.Count : Math.Min(common, repeat.Count);
                                }
                            }
                        }
                    }

                    return common;
                }

                float? SplitEvenly(List<float> relaxations, float naturalRf, int baseCopies)
                {
                    var baseCount = 0;
                    float? candidate = null;
                    var candidateCount = 0;
                    foreach (var rf in relaxations)
                    {
                        if (MathF.Abs(rf - naturalRf) < 1e-4f)
                        {
                            baseCount++;
                        }
                        else if (candidate is null || MathF.Abs(rf - candidate.Value) < 1e-4f)
                        {
                            candidate = rf;
                            candidateCount++;
                        }
                        else
                        {
                            return null;
                        }
                    }

                    if (baseCount != baseCopies || candidateCount != baseCopies || candidate is not { } value
                        || (MathF.Abs(value - 1.0f) < 1e-4f && MathF.Abs(naturalRf - 1.0f) >= 1e-4f))
                    {
                        return null;
                    }

                    return value;
                }

                bool RootIsUpwardTarget(BoneChainJoint joint, int parentNode, int grand, int greatGrand)
                    => rootNode == parentNode
                        || (joint.BendSpring && rootNode == grand)
                        || (joint.TorsionSpring && rootNode == greatGrand);

                float? RootSuspenderValue(BoneChainJoint joint, int parentNode, int grand, int greatGrand)
                {
                    if (joint.Node == rootNode)
                    {
                        return null;
                    }

                    if (RootIsUpwardTarget(joint, parentNode, grand, greatGrand))
                    {
                        var naturalRf = chainNaturalRf ?? 1f;
                        var totalCopies = JointCopies(joint);
                        if (totalCopies <= 1 || totalCopies % 2 != 0)
                        {
                            return null;
                        }

                        var baseCopies = totalCopies / 2;
                        var ringCopies = jointRingOf.TryGetValue(joint.Node, out var ownRing) && ownRing.Count > 0
                            && rodRelaxationsByPair.TryGetValue(UnorderedPair(joint.Node, ownRing[0]), out var ringRods)
                            ? ringRods.Count
                            : 0;
                        float? suspender = null;
                        foreach (var a in Side(joint.Node))
                        {
                            foreach (var b in Side(rootNode))
                            {
                                var pair = UnorderedPair(a, b);
                                if (!rodRelaxationsByPair.TryGetValue(pair, out var relaxations)
                                    || relaxations.Count != baseCopies * 2
                                    || (SplitEvenly(relaxations, naturalRf, baseCopies)
                                        ?? (ringCopies == baseCopies && relaxations.TrueForAll(rf => MathF.Abs(rf - naturalRf) < 1e-4f)
                                            ? naturalRf
                                            : null)) is not { } value
                                    || (suspender is { } already && MathF.Abs(already - value) > 1e-4f))
                                {
                                    return null;
                                }

                                suspender = value;
                            }
                        }

                        return suspender;
                    }

                    {
                        float? suspender = null;
                        foreach (var a in Side(joint.Node))
                        {
                            foreach (var b in Side(rootNode))
                            {
                                var pair = UnorderedPair(a, b);
                                if (Array.IndexOf(SourceSprings, pair) >= 0
                                    || Array.IndexOf(SourceSprings, (pair.Item2, pair.Item1)) >= 0
                                    || !rigidRodRelaxationsByPair.TryGetValue(pair, out var relaxations)
                                    || relaxations.Count < 1
                                    || relaxations.Exists(rf => MathF.Abs(rf - relaxations[0]) > 1e-4f)
                                    || (suspender is { } already && MathF.Abs(already - relaxations[0]) > 1e-4f))
                                {
                                    return null;
                                }

                                suspender = relaxations[0];
                            }
                        }

                        return suspender;
                    }
                }

                float? RootCompanionValue(BoneChainJoint joint, int parentNode, int grand, int greatGrand, out float? spanRelaxation)
                {
                    spanRelaxation = null;
                    if (joint.Node == rootNode || rootNode == parentNode)
                    {
                        return null;
                    }

                    var rootTarget = joint.BendSpring && rootNode == grand ? grand
                        : joint.TorsionSpring && rootNode == greatGrand ? greatGrand
                        : -1;
                    var baseCopies = SpanCopies(joint, parentNode);
                    if (rootTarget < 0 || baseCopies <= 0 || SpanCopies(joint, rootTarget) != baseCopies + 1)
                    {
                        return null;
                    }

                    if (joint.BendSpring && grand >= 0 && grand != rootTarget
                        && SpanCopies(joint, grand) != baseCopies)
                    {
                        return null;
                    }

                    if (joint.TorsionSpring && greatGrand >= 0 && greatGrand != rootTarget
                        && SpanCopies(joint, greatGrand) != baseCopies)
                    {
                        return null;
                    }

                    var baseRf = (rootTarget == grand ? joint.BendStiffness : joint.TorsionStiffness) * MathF.Exp(-DefaultSurfaceStretch);
                    float? companion = null;
                    var pairsAgree = true;
                    (float Low, float High)? split = null;
                    var splitsAgree = baseCopies == 1;
                    foreach (var a in Side(joint.Node))
                    {
                        foreach (var b in Side(rootTarget))
                        {
                            var pair = UnorderedPair(a, b);
                            if (Array.IndexOf(SourceSprings, pair) >= 0
                                || Array.IndexOf(SourceSprings, (pair.Item2, pair.Item1)) >= 0
                                || !rigidRodRelaxationsByPair.TryGetValue(pair, out var relaxations))
                            {
                                return null;
                            }

                            if (pairsAgree && Surplus(relaxations, baseCopies, baseRf) is { } value
                                && (companion is not { } already || MathF.Abs(already - value) <= 1e-4f))
                            {
                                companion = value;
                            }
                            else
                            {
                                pairsAgree = false;
                            }

                            if (splitsAgree && TwoSingleRods(relaxations) is { } two
                                && (split is not { } seen
                                    || (MathF.Abs(seen.Low - two.Low) <= 1e-4f && MathF.Abs(seen.High - two.High) <= 1e-4f)))
                            {
                                split = two;
                            }
                            else
                            {
                                splitsAgree = false;
                            }
                        }
                    }

                    if (pairsAgree)
                    {
                        return companion;
                    }

                    if (!splitsAgree || split is not { } values)
                    {
                        return null;
                    }

                    spanRelaxation = values.High;
                    return values.Low;
                }

                static (float Low, float High)? TwoSingleRods(List<float> relaxations)
                    => relaxations.Count == 2 && MathF.Abs(relaxations[0] - relaxations[1]) > 1e-4f
                        ? (MathF.Min(relaxations[0], relaxations[1]), MathF.Max(relaxations[0], relaxations[1]))
                        : null;

                static float? Surplus(List<float> relaxations, int baseCopies, float baseRf)
                {
                    var groups = new List<(float Value, int Count)>();
                    foreach (var rf in relaxations)
                    {
                        var at = groups.FindIndex(g => MathF.Abs(g.Value - rf) < 1e-4f);
                        if (at < 0)
                        {
                            groups.Add((rf, 1));
                        }
                        else
                        {
                            groups[at] = (groups[at].Value, groups[at].Count + 1);
                        }
                    }

                    if (groups.Count == 1)
                    {
                        return groups[0].Count == baseCopies + 1 ? groups[0].Value : null;
                    }

                    if (groups.Count != 2)
                    {
                        return null;
                    }

                    if (groups[0].Count == 1 && groups[1].Count == 1)
                    {
                        return groups.FindIndex(g => MathF.Abs(g.Value - baseRf) < 1e-4f) is var atBase and >= 0
                            ? groups[1 - atBase].Value
                            : null;
                    }

                    var odd = groups.FindIndex(static g => g.Count == 1);
                    return odd >= 0 && groups[1 - odd].Count == baseCopies ? groups[odd].Value : null;
                }

                var sliderScale = MathF.Exp(-DefaultSurfaceStretch);
                float Slider(float relaxation) => Math.Min(1f, relaxation / sliderScale);

                var clusterPairs = SelfCollisionClusterPairs;
                var chainDeclaresNoStretch = chain.Joints.Count > 1
                    && chain.Joints.Exists(joint => !joint.IsRoot
                        && clusterPairs.Contains(UnorderedPair(joint.Node, joint.ParentNode)))
                    && chain.Joints.TrueForAll(joint => joint.IsRoot
                        || clusterPairs.Contains(UnorderedPair(joint.Node, joint.ParentNode))
                        || (!SpannedByRod(joint.Node, joint.ParentNode)
                            && RingInternalRelaxation(joint.Node) is null));

                int SpanRodCopies(int a, int b)
                    => repeatRodRelaxationsByPair.TryGetValue(UnorderedPair(a, b), out var repeats) ? repeats.Count : 0;

                int RingRodCopies(int node)
                {
                    var most = 0;
                    if (jointRingOf.TryGetValue(node, out var ring))
                    {
                        foreach (var member in ring)
                        {
                            most = Math.Max(most, SpanRodCopies(node, member));
                        }
                    }

                    return most;
                }

                bool DeclaresNoStretch(BoneChainJoint joint)
                {
                    if (joint.IsRoot || joint.ParentNode < 0 || !Simulates(joint.Node) || IsPositionDriven(joint.Node)
                        || !jointRingOf.TryGetValue(joint.Node, out var ring) || ring.Count == 0)
                    {
                        return false;
                    }

                    var own = Extrusion(joint.Node);
                    return !Array.Exists(Quads, quad => Array.Exists(quad, own.Contains))
                        && !AnyRodBetween(own, [joint.ParentNode, .. Side(joint.ParentNode)])
                        && !AnyRodBetween(own, own);
                }

                List<int> Extrusion(int node)
                    => jointRingOf.TryGetValue(node, out var ring) ? [node, .. ring] : [node];

                bool AnyRodBetween(List<int> lhs, List<int> rhs)
                {
                    foreach (var a in lhs)
                    {
                        foreach (var b in rhs)
                        {
                            if (a != b && rodPairs.Contains(UnorderedPair(a, b)))
                            {
                                return true;
                            }
                        }
                    }

                    return false;
                }

                foreach (var joint in chain.Joints)
                {
                    var parent = joint.ParentNode;
                    var grandParent = parent >= 0 && jointByNode.TryGetValue(parent, out var p1) ? p1.ParentNode : -1;
                    var greatGrandParent = grandParent >= 0 && jointByNode.TryGetValue(grandParent, out var p2) ? p2.ParentNode : -1;

                    var endRing = EndEffectorRing(joint.Node);
                    joint.BendSpring = SpannedByDeclaredRod(joint.Node, grandParent) || AllDeclared(endRing, parent);
                    joint.TorsionSpring = SpannedByDeclaredRod(joint.Node, greatGrandParent) || AllDeclared(endRing, grandParent);

                    float SpringStiffness(int other, int endEffectorOther)
                    {
                        var stiffness = SpanRelaxation(joint.Node, other)
                            ?? RelaxationAcross(endRing, endEffectorOther)
                            ?? chainNaturalRf ?? 1f;
                        return stiffness > 0f ? Slider(stiffness) : 1f;
                    }

                    var stretch = SpanRelaxation(joint.Node, parent) ?? RingInternalRelaxation(joint.Node)
                        ?? chainNaturalRf ?? 1f;

                    var roped = !joint.IsRoot && ropeParents.GetValueOrDefault(joint.Node, -1) == parent;
                    joint.StretchStiffness = (chainDeclaresNoStretch && !joint.IsRoot) || DeclaresNoStretch(joint)
                        || (roped && !SpannedByRod(joint.Node, parent) && RingInternalRelaxation(joint.Node) is null)
                        ? 0f
                        : stretch > 0f ? Slider(stretch) : 1f;

                    var ropeHinted = NodeBases.Count == 0
                        ? RopeRunParents
                        : (IReadOnlyDictionary<int, int>)new Dictionary<int, int>();
                    bool AuthoredSpring(int other) => other >= 0
                        && !ropeHinted.ContainsKey(joint.Node) && !ropeHinted.ContainsKey(other)
                        && (Array.IndexOf(SourceSprings, (joint.Node, other)) >= 0
                            || Array.IndexOf(SourceSprings, (other, joint.Node)) >= 0);

                    var ringCopies = RingRodCopies(joint.Node);
                    if (AuthoredSpring(parent)
                        && !(ringCopies > 0 && SpanRodCopies(joint.Node, parent) > ringCopies))
                    {
                        joint.StretchStiffness = 0f;
                    }

                    if (AuthoredSpring(grandParent))
                    {
                        joint.BendSpring = false;
                        joint.BendStiffness = 0f;
                    }

                    if (AuthoredSpring(greatGrandParent))
                    {
                        joint.TorsionSpring = false;
                        joint.TorsionStiffness = 0f;
                    }
                    joint.BendStiffness = joint.BendSpring ? SpringStiffness(grandParent, parent) : 0f;
                    joint.TorsionStiffness = joint.TorsionSpring ? SpringStiffness(greatGrandParent, grandParent) : 0f;
                    joint.Antishrink = JointContraction(joint, parent, grandParent, greatGrandParent);

                    if (RootSuspenderValue(joint, parent, grandParent, greatGrandParent) is { } suspender)
                    {
                        joint.Suspender = suspender;
                        joint.ExtraIterations = RootIsUpwardTarget(joint, parent, grandParent, greatGrandParent) ? JointCopies(joint) / 2 - 1 : JointCopies(joint) - 1;
                    }
                    else if (RootCompanionValue(joint, parent, grandParent, greatGrandParent, out var spanReading) is { } companion)
                    {
                        joint.Suspender = companion;
                        joint.ExtraIterations = SpanCopies(joint, parent) - 1;
                        if (spanReading is { } span && joint.BendSpring && rootNode == grandParent)
                        {
                            joint.BendStiffness = Slider(span);
                        }
                        else if (spanReading is { } torsionSpan)
                        {
                            joint.TorsionStiffness = Slider(torsionSpan);
                        }
                    }
                    else
                    {
                        joint.Suspender = 0f;
                        joint.ExtraIterations = JointCopies(joint, floor: true) - 1;
                    }
                }

                foreach (var joint in chain.Joints)
                {
                    if (!DeclaresNoStretch(joint))
                    {
                        continue;
                    }

                    var own = Extrusion(joint.Node);
                    var kids = chain.Joints.FindAll(other => other.ParentNode == joint.Node);

                    var animated = AnimRodPairs.Count > 0
                        && own.Exists(node => AnimRodPairs.Any(pair => pair.Item1 == node || pair.Item2 == node));

                    joint.AnimatedLength = animated && (kids.Count == 0
                        ? NodeBases.ContainsKey(joint.Node)
                        : kids.TrueForAll(kid => jointRingOf.ContainsKey(kid.Node) && !AnyRodBetween(Extrusion(kid.Node), own))
                            && ((NodeBases.ContainsKey(joint.Node) && !SelfCollisionClusters.Any(cluster => cluster.Nodes.Contains(joint.Node)))
                                || kids.TrueForAll(kid => AnyRodBetween(Extrusion(kid.Node), Extrusion(kid.Node)))));

                    if (joint.AnimatedLength)
                    {
                        joint.StretchStiffness = 1f;
                    }
                }

                bool Simulates(int node) => node < NodeInvMasses.Length && NodeInvMasses[node] != 0f;

                float ChildSiblingValue(BoneChainJoint joint)
                {
                    var kids = chain.Joints.FindAll(other => other.ParentNode == joint.Node);
                    if (kids.Count < 2)
                    {
                        return 0f;
                    }

                    List<float>? common = null;
                    for (var i = 0; i < kids.Count; i++)
                    {
                        for (var j = i + 1; j < kids.Count; j++)
                        {
                            foreach (var a in Side(kids[i].Node))
                            {
                                foreach (var b in Side(kids[j].Node))
                                {
                                    if (!Simulates(a) && !Simulates(b))
                                    {
                                        continue;
                                    }

                                    var pair = UnorderedPair(a, b);
                                    if (Array.IndexOf(SourceSprings, pair) >= 0
                                        || Array.IndexOf(SourceSprings, (pair.Item2, pair.Item1)) >= 0
                                        || !rodRelaxationsByPair.TryGetValue(pair, out var relaxations))
                                    {
                                        return 0f;
                                    }

                                    if (common is null)
                                    {
                                        common = [];
                                        foreach (var relaxation in relaxations)
                                        {
                                            if (!common.Exists(seen => MathF.Abs(seen - relaxation) <= 1e-4f))
                                            {
                                                common.Add(relaxation);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        common.RemoveAll(seen =>
                                            !relaxations.Exists(relaxation => MathF.Abs(seen - relaxation) <= 1e-4f));
                                    }

                                    if (common.Count == 0)
                                    {
                                        return 0f;
                                    }
                                }
                            }
                        }
                    }

                    return common is [var reading] && reading > 0f ? Slider(reading) : 0f;
                }

                foreach (var joint in chain.Joints)
                {
                    joint.ChildSiblingSpring = ChildSiblingValue(joint);
                }

                foreach (var joint in chain.Joints)
                {
                    if (joint.IsRoot || joint.ProxyNode < 0 || joint.ExtrudeSides < 2 || !IsPositionDriven(joint.Node)
                        || !rodPairs.Contains(UnorderedPair(joint.Node, joint.ParentNode)))
                    {
                        continue;
                    }

                    joint.Restated = true;
                    if (jointByNode.TryGetValue(joint.ParentNode, out var restatedParent))
                    {
                        restatedParent.Restated = true;
                    }
                }

                SteerNodeBaseTies(chain);

                var firstSimulated = int.MaxValue;
                foreach (var joint in chain.Joints)
                {
                    if (joint.Node >= StaticNodeCount)
                    {
                        firstSimulated = Math.Min(firstSimulated, joint.Node);
                    }

                    if (DeclaredRing(joint.Node) is { } declaredRing)
                    {
                        foreach (var proxy in declaredRing)
                        {
                            if (proxy >= StaticNodeCount)
                            {
                                firstSimulated = Math.Min(firstSimulated, proxy);
                            }
                        }
                    }
                }

                chainFirstSimulated[chain] = firstSimulated;
                chains.Add(chain);
                if (spec.ChildrenOf is null && spec.RingOf is null && !ringlessRoot)
                {
                    mergedChains.Add(chain);
                }
            }

            if (ringlessKids is null && chainVersion is not null
                && VersionSplitRoots(mergedChains, chainVersion, chains.Count > 1) is { Count: > 0 } splits)
            {
                return BuildBoneChains(chainVersion, splits);
            }

            MergeSiblingHubs(chains);
            MarkSecondDeclarations(chains);

            return [.. chains.OrderBy(ChainFirstNode)];

            int ChainFirstNode(BoneChain chain)
            {
                if (chainFirstSimulated.TryGetValue(chain, out var firstSimulated)
                    && firstSimulated < int.MaxValue)
                {
                    return firstSimulated;
                }

                var first = int.MaxValue;
                foreach (var joint in chain.Joints)
                {
                    foreach (var node in (int[])[joint.Node, joint.ProxyNode])
                    {
                        if (node >= 0)
                        {
                            first = Math.Min(first, node);
                        }
                    }
                }

                return first;
            }
        }

        /// <summary>
        /// Marks the joints a SECOND <c>ClothChain</c> re-declares, and the bone that chain is rooted at.
        /// </summary>
        /// <param name="chains">The reconstructed chains, edited in place.</param>
        private void MarkSecondDeclarations(List<BoneChain> chains)
        {
            foreach (var chain in chains)
            {
                if (chain.ExtrudeSides >= 1)
                {
                    continue;
                }

                var byNode = new Dictionary<int, BoneChainJoint>();
                var children = new Dictionary<int, List<BoneChainJoint>>();
                foreach (var joint in chain.Joints)
                {
                    byNode[joint.Node] = joint;
                    if (joint.ParentNode >= 0)
                    {
                        if (!children.TryGetValue(joint.ParentNode, out var siblings))
                        {
                            siblings = [];
                            children[joint.ParentNode] = siblings;
                        }

                        siblings.Add(joint);
                    }
                }

                var roots = new List<BoneChainJoint>();
                foreach (var joint in chain.Joints)
                {
                    if (!joint.Simulated || joint.ParentNode < 0
                        || !TwistRelaxCopies.TryGetValue((joint.Node, joint.ParentNode), out var toParent)
                        || toParent.Count != 1 || toParent[0] <= 0f
                        || !children.TryGetValue(joint.Node, out var kids)
                        || !kids.Exists(kid => TwistRelaxCopies.TryGetValue((joint.Node, kid.Node), out var toChild)
                            && toChild.Count == 1 && toChild[0] == 0f))
                    {
                        continue;
                    }

                    var root = joint;
                    while (root.Simulated && root.ParentNode >= 0 && byNode.TryGetValue(root.ParentNode, out var above))
                    {
                        root = above;
                    }

                    if (!root.Simulated && !roots.Contains(root))
                    {
                        roots.Add(root);
                    }
                }

                foreach (var root in roots)
                {
                    var pending = new Queue<BoneChainJoint>();
                    pending.Enqueue(root);
                    while (pending.Count > 0)
                    {
                        var joint = pending.Dequeue();
                        joint.SecondDeclarationRoot = root.Name;
                        if (children.TryGetValue(joint.Node, out var kids))
                        {
                            foreach (var kid in kids)
                            {
                                pending.Enqueue(kid);
                            }
                        }
                    }
                }

                MarkVoicedSecondDeclarations(chain, byNode);
            }
        }

        /// <summary>
        /// Marks the runs whose second declaration stated its own <c>twist_relax</c>, whose pairs carry two twist copies.
        /// </summary>
        private void MarkVoicedSecondDeclarations(BoneChain chain, Dictionary<int, BoneChainJoint> byNode)
        {
            var doubled = new HashSet<int>();
            foreach (var (link, copies) in TwistRelaxCopies)
            {
                if (copies.Count == 2)
                {
                    doubled.Add(link.Orient);
                    doubled.Add(link.End);
                }
            }

            if (doubled.Count == 0)
            {
                return;
            }

            var members = chain.Joints.FindAll(joint => joint.SecondDeclarationRoot is null
                && doubled.Contains(joint.Node));
            var memberNodes = members.Select(static joint => joint.Node).ToHashSet();

            foreach (var root in members)
            {
                if (memberNodes.Contains(root.ParentNode) || root.Simulated)
                {
                    continue;
                }

                var run = new List<BoneChainJoint>();
                var pending = new Queue<BoneChainJoint>();
                pending.Enqueue(root);
                while (pending.Count > 0)
                {
                    var joint = pending.Dequeue();
                    run.Add(joint);
                    foreach (var kid in members)
                    {
                        if (kid.ParentNode == joint.Node)
                        {
                            pending.Enqueue(kid);
                        }
                    }
                }

                if (!run.Exists(static joint => joint.Simulated) || !FirstDeclarationIsStatic(run, byNode))
                {
                    continue;
                }

                foreach (var joint in run)
                {
                    joint.SecondDeclarationRoot = root.Name;
                }
            }
        }

        /// <summary>
        /// Gets whether the first of a doubled run's declarations left it unsimulated: a child-ward copy of 0 at rank 0.
        /// </summary>
        private bool FirstDeclarationIsStatic(List<BoneChainJoint> run, Dictionary<int, BoneChainJoint> byNode)
        {
            foreach (var joint in run)
            {
                if (!joint.Simulated || !byNode.ContainsKey(joint.ParentNode))
                {
                    continue;
                }

                foreach (var kid in run)
                {
                    if (kid.ParentNode == joint.Node
                        && TwistRelaxCopies.TryGetValue((joint.Node, kid.Node), out var toChild)
                        && toChild.Count == 2)
                    {
                        return toChild[0] == 0f;
                    }
                }
            }

            return false;
        }

        private readonly HashSet<string> siblingSpringHubs = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets the bones a chain declares only to spring its siblings together. Such a bone anchors no
        /// chain of its own, so a cloth node parented to it still needs its own static declaration.
        /// </summary>
        internal IReadOnlySet<string> SiblingSpringHubs => siblingSpringHubs;

        /// <summary>
        /// Gathers the chains of a ringless sibling group under the bone that parents them, and marks the
        /// hub as springing its children together.
        /// </summary>
        /// <param name="chains">The reconstructed chains, edited in place.</param>
        private void MergeSiblingHubs(List<BoneChain> chains)
        {
            siblingSpringHubs.Clear();
            if (SkeletonBoneParents is null)
            {
                return;
            }

            var nodeOf = new Dictionary<string, int>(CtrlNames.Length, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < CtrlNames.Length; i++)
            {
                nodeOf.TryAdd(CtrlNames[i], i);
            }

            var groups = new Dictionary<string, List<BoneChain>>(StringComparer.OrdinalIgnoreCase);
            foreach (var chain in chains)
            {
                if (chain.ExtrudeSides >= 1 || chain.Joints.Exists(static joint => joint.RingNodes.Count > 0))
                {
                    continue;
                }

                if (chain.Joints.Find(static joint => joint.IsRoot) is not { Simulated: false } root
                    || !IsLockedToGoal(root.Node))
                {
                    continue;
                }

                if (SkeletonBoneParents.GetValueOrDefault(root.Name) is not { } hub
                    || !nodeOf.TryGetValue(hub, out var hubNode)
                    || chain.Joints.Exists(joint => joint.Node == hubNode))
                {
                    continue;
                }

                if (!groups.TryGetValue(hub, out var members))
                {
                    groups[hub] = members = [];
                }

                members.Add(chain);
            }

            foreach (var (hub, members) in groups)
            {
                if (members.Count < 2)
                {
                    continue;
                }

                var hubNode = nodeOf[hub];
                var host = chains.Find(chain => chain.Joints.Exists(joint => joint.Node == hubNode));
                if (host is null)
                {
                    host = members[0];
                    host.RootBone = hub;
                    host.Joints.Insert(0, new BoneChainJoint
                    {
                        Node = hubNode,
                        Name = CtrlNames[hubNode],
                        ParentNode = -1,
                        InvMass = hubNode < NodeInvMasses.Length ? NodeInvMasses[hubNode] : 0f,
                    });
                }

                host.Joints.Find(joint => joint.Node == hubNode)!.ChildSiblingSpring = 1f;
                siblingSpringHubs.Add(CtrlNames[hubNode]);

                foreach (var member in members)
                {
                    foreach (var joint in member.Joints)
                    {
                        if (joint.IsRoot && joint.Node != hubNode)
                        {
                            joint.ParentNode = hubNode;
                            joint.ParentName = hub;
                            joint.SpringsWithSiblings = true;
                        }

                        if (member != host)
                        {
                            host.Joints.Add(joint);
                        }
                    }

                    if (member != host)
                    {
                        chains.Remove(member);
                    }
                }
            }
        }
    }
}
