using System.Linq;

namespace ValveResourceFormat.IO
{
    internal sealed partial class ClothReconstruction
    {
        /// <summary>
        /// Reads the springs, stiffnesses and iteration counts of one chain declaration's joints from the rod graph.
        /// </summary>
        private sealed class ChainReader
        {
            private readonly ClothReconstruction cloth;
            private readonly ChainSpec spec;
            private readonly BoneChain chain;
            private readonly int rootNode;
            private readonly bool ringlessRoot;
            private readonly Dictionary<int, List<int>> proxyChildrenOf;
            private readonly IReadOnlyDictionary<int, int> ropeParents;
            private readonly Dictionary<int, List<int>> jointRingOf;
            private readonly Dictionary<int, List<int>> endEffectorRingOf;
            private readonly HashSet<(int, int)> rodPairs;
            private readonly Dictionary<(int, int), List<float>> rodRelaxationsByPair;
            private readonly Dictionary<(int, int), List<float>> rigidRodRelaxationsByPair;
            private readonly Dictionary<(int, int), List<float>> repeatRodRelaxationsByPair;
            private readonly Dictionary<(int, int), List<float>> rodContractionsByPair;
            private readonly Dictionary<int, BoneChainJoint> jointByNode;
            private readonly Dictionary<int, List<BoneChainJoint>> kidsByParent = [];
            private readonly HashSet<int> chainNodes = [];
            private readonly HashSet<(int, int)> crossesRoot = [];
            private readonly IReadOnlyDictionary<int, int> ropeHinted;
            private readonly float sliderScale;
            private float? chainNaturalRf;
            private bool chainDeclaresNoStretch;

            public ChainReader(ClothReconstruction cloth, ChainSpec spec, BoneChain chain, RodGraph rodGraph,
                Dictionary<int, List<int>> proxyChildrenOf, IReadOnlyDictionary<int, int> ropeParents,
                Dictionary<int, List<int>> jointRingOf, Dictionary<int, List<int>> endEffectorRingOf)
            {
                this.cloth = cloth;
                this.spec = spec;
                this.chain = chain;
                this.proxyChildrenOf = proxyChildrenOf;
                this.ropeParents = ropeParents;
                this.jointRingOf = jointRingOf;
                this.endEffectorRingOf = endEffectorRingOf;
                (rodPairs, rodRelaxationsByPair, rigidRodRelaxationsByPair, repeatRodRelaxationsByPair, rodContractionsByPair) = rodGraph;
                rootNode = spec.Root;
                ringlessRoot = spec.RinglessRoot;
                sliderScale = cloth.Index.SurfaceStretchScale;
                ropeHinted = cloth.Index.NodeBases.Count == 0 ? cloth.RopeRunParents : new Dictionary<int, int>();
                jointByNode = chain.Joints.ToDictionary(static j => j.Node);

                foreach (var chainJoint in chain.Joints)
                {
                    GetOrAdd(kidsByParent, chainJoint.ParentNode).Add(chainJoint);
                }

                foreach (var chainJoint in chain.Joints)
                {
                    chainNodes.Add(chainJoint.Node);
                    foreach (var ringNode in Side(chainJoint.Node))
                    {
                        chainNodes.Add(ringNode);
                    }
                }

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
            }

            /// <summary>Reads every joint's springs, then returns the lowest simulated node the chain occupies.</summary>
            public int Read()
            {
                ReadNaturalRelaxation();
                chainDeclaresNoStretch = DeclaresChainNoStretch();

                // Read up front: JointCopies consults it, and nothing the loop below writes feeds it.
                foreach (var joint in chain.Joints)
                {
                    joint.ChildSiblingSpring = ChildSiblingValue(joint);
                }

                foreach (var joint in chain.Joints)
                {
                    ReadJointSprings(joint);
                }

                ReadAnimatedLengths();
                MarkRestatedJoints();
                cloth.SteerNodeBaseTies(chain);

                return FirstSimulatedNode();
            }

            private List<int>? DeclaredRing(int node) => DeclaredRingOf(spec, proxyChildrenOf, node);

            private List<BoneChainJoint> KidsOf(BoneChainJoint joint) => kidsByParent.GetValueOrDefault(joint.Node) ?? [];

            private List<int> Side(int end)
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

            private float Slider(float relaxation) => MathF.Min(1f, relaxation / sliderScale);

            private void ReadNaturalRelaxation()
            {
                chainNaturalRf = NaturalRf(null);
                if (chainNaturalRf is null && NaturalRf(true) is { } acrossRoot
                    && NaturalRf(false) is { } withoutRoot
                    && MathF.Abs(acrossRoot - withoutRoot) > ChainReadingTolerance)
                {
                    chainNaturalRf = withoutRoot;
                }
            }

            private float? NaturalRf(bool? crossingRoot)
            {
                float? natural = null;
                foreach (var (pair, relaxations) in rodRelaxationsByPair)
                {
                    if (relaxations.Count != 1
                        || !chainNodes.Contains(pair.Item1) || !chainNodes.Contains(pair.Item2)
                        || (crossingRoot is { } want && crossesRoot.Contains(pair) != want))
                    {
                        continue;
                    }

                    if (!AgreeInto(ref natural, relaxations[0]))
                    {
                        return null;
                    }
                }

                return natural;
            }

            private bool DeclaresChainNoStretch()
            {
                var clusterPairs = cloth.SelfCollisionClusterPairs;
                return chain.Joints.Count > 1
                    && chain.Joints.Exists(joint => !joint.IsRoot
                        && clusterPairs.Contains(UnorderedPair(joint.Node, joint.ParentNode)))
                    && chain.Joints.TrueForAll(joint => joint.IsRoot
                        || clusterPairs.Contains(UnorderedPair(joint.Node, joint.ParentNode))
                        || (!SpannedByRod(joint.Node, joint.ParentNode)
                            && RingInternalRelaxation(joint.Node) is null));
            }

            private float? RelaxationAcross(List<int> lhs, int other)
            {
                if (other < 0 || lhs.Count == 0)
                {
                    return null;
                }

                var otherSide = Side(other);
                return Across(lhs, otherSide, rodRelaxationsByPair) ?? Across(lhs, otherSide, rigidRodRelaxationsByPair);
            }

            private static float? Across(List<int> lhs, List<int> rhs, Dictionary<(int, int), List<float>> byPair)
            {
                float? value = null;
                foreach (var a in lhs)
                {
                    foreach (var b in rhs)
                    {
                        if (!byPair.TryGetValue(UnorderedPair(a, b), out var relaxations) || !AgreeInto(ref value, relaxations))
                        {
                            return null;
                        }
                    }
                }

                return value;
            }

            private float? RingInternalRelaxation(int node)
            {
                if (DeclaredRing(node) is not { Count: > 0 } ring)
                {
                    return null;
                }

                var extrusion = new List<int>(ring) { node };
                extrusion.Sort();
                return Inside(extrusion, rodRelaxationsByPair) ?? Inside(extrusion, rigidRodRelaxationsByPair);
            }

            private static float? Inside(List<int> extrusion, Dictionary<(int, int), List<float>> byPair)
            {
                float? value = null;
                for (var i = 0; i < extrusion.Count; i++)
                {
                    for (var j = i + 1; j < extrusion.Count; j++)
                    {
                        if (byPair.TryGetValue((extrusion[i], extrusion[j]), out var relaxations) && !AgreeInto(ref value, relaxations))
                        {
                            return null;
                        }
                    }
                }

                return value;
            }

            private float? SpanRelaxation(int node, int other) => RelaxationAcross(Side(node), other);

            private bool IsDeclaredRod((int, int) pair) => rodPairs.Contains(pair) && !cloth.SurfaceFoldOnlyPairs.Contains(pair);

            private bool AllPairs(List<int> lhs, int other, Func<(int, int), bool> holds)
            {
                if (other < 0 || lhs.Count == 0)
                {
                    return false;
                }

                var otherSide = Side(other);
                return lhs.All(a => otherSide.All(b => holds(UnorderedPair(a, b))));
            }

            private bool SpannedByDeclaredRod(int node, int other)
                => other >= 0 && (IsDeclaredRod(UnorderedPair(node, other)) || AllPairs(Side(node), other, IsDeclaredRod));

            private bool SpannedByRod(int node, int other)
                => other >= 0 && (rodPairs.Contains(UnorderedPair(node, other)) || AllPairs(Side(node), other, rodPairs.Contains));

            private List<int> EndEffectorRing(int node)
                => endEffectorRingOf.TryGetValue(node, out var far) ? far : [];

            private int SpanRodCopies(int a, int b)
                => repeatRodRelaxationsByPair.TryGetValue(UnorderedPair(a, b), out var repeats) ? repeats.Count : 0;

            private int SpanCopies(BoneChainJoint joint, int other)
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
                        var count = SpanRodCopies(a, b);
                        if (count == 0 || (copies != 0 && count != copies))
                        {
                            return 0;
                        }

                        copies = count;
                    }
                }

                return copies;
            }

            private float JointContraction(BoneChainJoint joint, int parent, int grand, int greatGrand)
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
                            if (rodContractionsByPair.TryGetValue(UnorderedPair(a, b), out var contractions)
                                && !AgreeInto(ref found, contractions))
                            {
                                return 1f;
                            }
                        }
                    }
                }

                return (found ?? SiblingContraction(joint)) is { } reading ? MathUtils.Saturate(reading) : 1f;
            }

            private float? SiblingContraction(BoneChainJoint joint)
            {
                if (joint.ChildSiblingSpring <= 0f)
                {
                    return null;
                }

                var kids = KidsOf(joint);
                List<float>? common = null;
                for (var i = 0; i < kids.Count; i++)
                {
                    for (var j = i + 1; j < kids.Count; j++)
                    {
                        foreach (var a in Side(kids[i].Node))
                        {
                            foreach (var b in Side(kids[j].Node))
                            {
                                if (!rodContractionsByPair.TryGetValue(UnorderedPair(a, b), out var contractions))
                                {
                                    continue;
                                }

                                if (common is null)
                                {
                                    common = [.. contractions];
                                }
                                else
                                {
                                    common.RemoveAll(seen => !contractions.Exists(contraction => MathF.Abs(seen - contraction) <= ChainReadingTolerance));
                                }
                            }
                        }
                    }
                }

                return common is { Count: > 0 } && common.TrueForAll(value => MathF.Abs(value - common[0]) <= ChainReadingTolerance)
                    ? common[0]
                    : null;
            }

            private int JointCopies(BoneChainJoint joint, bool floor = false)
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
                            var count = SpanRodCopies(a, b);
                            if (count == 0 && cloth.Index.IsStatic(a) && cloth.Index.IsStatic(b))
                            {
                                continue;
                            }

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
                    if (copies != 0 || joint.ChildSiblingSpring == 0f)
                    {
                        return 1;
                    }

                    return Math.Max(SiblingCopies(joint), 1);
                }

                if (copies == 0 && KidsOf(joint) is [var onlyChild] && !Repeats(onlyChild.Node))
                {
                    copies = 0;
                }

                if (copies == 0 && joint.ChildSiblingSpring != 0f)
                {
                    copies = SiblingCopies(joint);
                }

                return Math.Max(copies, 1);
            }

            private int SiblingCopies(BoneChainJoint joint)
            {
                var kids = KidsOf(joint);
                var common = 0;
                for (var i = 0; i < kids.Count; i++)
                {
                    for (var j = 0; j < i; j++)
                    {
                        foreach (var a in Side(kids[j].Node))
                        {
                            foreach (var b in Side(kids[i].Node))
                            {
                                var count = SpanRodCopies(a, b);
                                if (count == 0)
                                {
                                    continue;
                                }

                                common = common == 0 ? count : Math.Min(common, count);
                            }
                        }
                    }
                }

                return common;
            }

            private static float? SplitEvenly(List<float> relaxations, float naturalRf, int baseCopies)
            {
                var baseCount = 0;
                float? candidate = null;
                var candidateCount = 0;
                foreach (var rf in relaxations)
                {
                    if (MathF.Abs(rf - naturalRf) < ChainReadingTolerance)
                    {
                        baseCount++;
                    }
                    else if (candidate is null || MathF.Abs(rf - candidate.Value) < ChainReadingTolerance)
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
                    || (MathF.Abs(value - 1.0f) < ChainReadingTolerance && MathF.Abs(naturalRf - 1.0f) >= ChainReadingTolerance))
                {
                    return null;
                }

                return value;
            }

            private bool RootIsUpwardTarget(BoneChainJoint joint, int parentNode, int grand, int greatGrand)
                => rootNode == parentNode
                    || (joint.BendSpring && rootNode == grand)
                    || (joint.TorsionSpring && rootNode == greatGrand);

            // upwardCopies is the joint's JointCopies when the root is an upward target, else 0.
            private float? RootSuspenderValue(BoneChainJoint joint, int parentNode, int grand, int greatGrand, out int upwardCopies,
                out float? rootSpanSpring)
            {
                upwardCopies = 0;
                rootSpanSpring = null;
                if (joint.Node == rootNode)
                {
                    return null;
                }

                if (RootIsUpwardTarget(joint, parentNode, grand, greatGrand))
                {
                    return UpwardRootSuspender(joint, parentNode, grand, greatGrand, out upwardCopies, out rootSpanSpring);
                }

                return RigidRootSuspender(joint);
            }

            private float? UpwardRootSuspender(BoneChainJoint joint, int parentNode, int grand, int greatGrand, out int upwardCopies,
                out float? rootSpanSpring)
            {
                rootSpanSpring = null;
                var naturalRf = chainNaturalRf ?? 1f;
                upwardCopies = JointCopies(joint);
                var totalCopies = upwardCopies;
                if (totalCopies <= 1 || totalCopies % 2 != 0)
                {
                    return DoubledRootSpanSuspender(joint, parentNode, grand, greatGrand, out upwardCopies, out rootSpanSpring);
                }

                var baseCopies = totalCopies / 2;
                var ringCopies = jointRingOf.TryGetValue(joint.Node, out var ownRing) && ownRing.Count > 0
                    && rodRelaxationsByPair.TryGetValue(UnorderedPair(joint.Node, ownRing[0]), out var ringRods)
                    ? ringRods.Count
                    : 0;
                if (RootSpans(joint, baseCopies * 2) is not { } spans)
                {
                    return null;
                }

                float? suspender = null;
                if (spans.TrueForAll(relaxations => (SplitEvenly(relaxations, naturalRf, baseCopies)
                        ?? (ringCopies == baseCopies && relaxations.TrueForAll(rf => MathF.Abs(rf - naturalRf) < ChainReadingTolerance)
                            ? naturalRf
                            : null)) is { } value
                    && AgreeInto(ref suspender, value)))
                {
                    return suspender;
                }

                if (rootNode != parentNode || SplitRootSpans(spans, baseCopies) is not { } split || split.Spring == split.Suspender)
                {
                    return null;
                }

                rootSpanSpring = split.Spring;
                return split.Suspender;
            }

            private float? RigidRootSuspender(BoneChainJoint joint)
            {
                float? rigidSuspender = null;
                foreach (var a in Side(joint.Node))
                {
                    foreach (var b in Side(rootNode))
                    {
                        var pair = UnorderedPair(a, b);
                        if (cloth.IsSourceSpring(pair.Item1, pair.Item2)
                            || !rigidRodRelaxationsByPair.TryGetValue(pair, out var relaxations)
                            || relaxations.Count < 1
                            || relaxations.Exists(rf => MathF.Abs(rf - relaxations[0]) > ChainReadingTolerance)
                            || !AgreeInto(ref rigidSuspender, relaxations[0]))
                        {
                            return null;
                        }
                    }
                }

                return rigidSuspender;
            }

            private List<List<float>>? RootSpans(BoneChainJoint joint, int copies)
            {
                var spans = new List<List<float>>();
                foreach (var a in Side(joint.Node))
                {
                    foreach (var b in Side(rootNode))
                    {
                        if (!rodRelaxationsByPair.TryGetValue(UnorderedPair(a, b), out var relaxations) || relaxations.Count != copies)
                        {
                            return null;
                        }

                        spans.Add(relaxations);
                    }
                }

                return spans;
            }

            private static (float Spring, float Suspender)? SplitRootSpans(List<List<float>> spans, int baseCopies)
            {
                (float Spring, float Suspender)? split = null;
                foreach (var relaxations in spans)
                {
                    var low = relaxations.Min();
                    var high = relaxations.Max();
                    if (high - low >= ChainReadingTolerance && relaxations.Count(rf => rf - low < ChainReadingTolerance) != baseCopies)
                    {
                        return null;
                    }

                    if (split is { } seen
                        && (MathF.Abs(seen.Spring - high) >= ChainReadingTolerance || MathF.Abs(seen.Suspender - low) >= ChainReadingTolerance))
                    {
                        return null;
                    }

                    split = (high, low);
                }

                return split;
            }

            private float? DoubledRootSpanSuspender(BoneChainJoint joint, int parentNode, int grand, int greatGrand, out int copies,
                out float? rootSpanSpring)
            {
                copies = 0;
                rootSpanSpring = null;
                var baseCopies = SpanCopies(joint, parentNode);
                if (rootNode == parentNode || baseCopies < 2
                    || (joint.BendSpring && grand != rootNode && SpanCopies(joint, grand) != baseCopies)
                    || (joint.TorsionSpring && greatGrand != rootNode && SpanCopies(joint, greatGrand) != baseCopies)
                    || Side(joint.Node).Exists(a => Side(rootNode).Exists(b => cloth.IsSourceSpring(a, b)))
                    || RootSpans(joint, 2 * baseCopies) is not { } spans
                    || SplitRootSpans(spans, baseCopies) is not { } split)
                {
                    return null;
                }

                copies = 2 * baseCopies;
                rootSpanSpring = split.Spring;
                return split.Suspender;
            }

            private float? RootCompanionValue(BoneChainJoint joint, int parentNode, int grand, int greatGrand, out float? spanRelaxation)
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

                var baseRf = (rootTarget == grand ? joint.BendStiffness : joint.TorsionStiffness) * sliderScale;
                float? companion = null;
                var pairsAgree = true;
                (float Low, float High)? split = null;
                var splitsAgree = baseCopies == 1;
                foreach (var a in Side(joint.Node))
                {
                    foreach (var b in Side(rootTarget))
                    {
                        var pair = UnorderedPair(a, b);
                        if (cloth.IsSourceSpring(pair.Item1, pair.Item2)
                            || !rigidRodRelaxationsByPair.TryGetValue(pair, out var relaxations))
                        {
                            return null;
                        }

                        if (pairsAgree && Surplus(relaxations, baseCopies, baseRf) is { } value
                            && (companion is not { } already || MathF.Abs(already - value) <= ChainReadingTolerance))
                        {
                            companion = value;
                        }
                        else
                        {
                            pairsAgree = false;
                        }

                        if (splitsAgree && TwoSingleRods(relaxations) is { } two
                            && (split is not { } seen
                                || (MathF.Abs(seen.Low - two.Low) <= ChainReadingTolerance && MathF.Abs(seen.High - two.High) <= ChainReadingTolerance)))
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

            private static (float Low, float High)? TwoSingleRods(List<float> relaxations)
                => relaxations.Count == 2 && MathF.Abs(relaxations[0] - relaxations[1]) > ChainReadingTolerance
                    ? (MathF.Min(relaxations[0], relaxations[1]), MathF.Max(relaxations[0], relaxations[1]))
                    : null;

            private static float? Surplus(List<float> relaxations, int baseCopies, float baseRf)
            {
                var groups = new List<(float Value, int Count)>();
                foreach (var rf in relaxations)
                {
                    var at = groups.FindIndex(g => MathF.Abs(g.Value - rf) < ChainReadingTolerance);
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
                    return groups.FindIndex(g => MathF.Abs(g.Value - baseRf) < ChainReadingTolerance) is var atBase and >= 0
                        ? groups[1 - atBase].Value
                        : null;
                }

                var odd = groups.FindIndex(static g => g.Count == 1);
                return odd >= 0 && groups[1 - odd].Count == baseCopies ? groups[odd].Value : null;
            }

            private int OwnRingRodCopies(int node)
            {
                if (!jointRingOf.TryGetValue(node, out var ring) || ring.Count < 2)
                {
                    return 0;
                }

                var fewest = int.MaxValue;
                for (var i = 0; i < ring.Count; i++)
                {
                    if (i == ring.Count - 1 && ring.Count == 2)
                    {
                        break;
                    }

                    fewest = Math.Min(fewest, SpanRodCopies(ring[i], ring[(i + 1) % ring.Count]));
                }

                return fewest;
            }

            private int RingRodCopies(int node)
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

            private bool DeclaresNoStretch(BoneChainJoint joint)
            {
                if (joint.IsRoot || joint.ParentNode < 0 || !cloth.Simulates(joint.Node) || cloth.IsPositionDriven(joint.Node)
                    || !jointRingOf.TryGetValue(joint.Node, out var ring) || ring.Count == 0)
                {
                    return false;
                }

                var own = Extrusion(joint.Node);
                return !own.Exists(cloth.QuadNodes.Contains)
                    && !AnyRod(rodPairs, own, [joint.ParentNode, .. Side(joint.ParentNode)])
                    && !AnyRod(rodPairs, own, own);
            }

            private List<int> Extrusion(int node)
                => jointRingOf.TryGetValue(node, out var ring) ? [node, .. ring] : [node];

            private bool SpanOnlyAnimated(BoneChainJoint joint)
            {
                if (joint.IsRoot || joint.ParentNode < 0 || !cloth.Simulates(joint.Node)
                    || (jointRingOf.TryGetValue(joint.Node, out var ring) && ring.Count > 0))
                {
                    return false;
                }

                var parentSide = Side(joint.ParentNode);
                return parentSide.Exists(other => cloth.AnimRodPairs.Contains(UnorderedPair(joint.Node, other)))
                    && !AnyRod(rodPairs, [joint.Node], parentSide);
            }

            private float SpringStiffness(BoneChainJoint joint, List<int> endRing, int other, int endEffectorOther)
            {
                var stiffness = SpanRelaxation(joint.Node, other)
                    ?? RelaxationAcross(endRing, endEffectorOther)
                    ?? chainNaturalRf ?? 1f;
                return stiffness > 0f ? Slider(stiffness) : 1f;
            }

            private bool AuthoredSpring(BoneChainJoint joint, int other) => other >= 0
                && !ropeHinted.ContainsKey(joint.Node) && !ropeHinted.ContainsKey(other)
                && cloth.IsSourceSpring(joint.Node, other);

            private void ReadJointSprings(BoneChainJoint joint)
            {
                var parent = joint.ParentNode;
                var grandParent = parent >= 0 && jointByNode.TryGetValue(parent, out var p1) ? p1.ParentNode : -1;
                var greatGrandParent = grandParent >= 0 && jointByNode.TryGetValue(grandParent, out var p2) ? p2.ParentNode : -1;

                var endRing = EndEffectorRing(joint.Node);
                joint.BendSpring = SpannedByDeclaredRod(joint.Node, grandParent) || AllPairs(endRing, parent, IsDeclaredRod);
                joint.TorsionSpring = SpannedByDeclaredRod(joint.Node, greatGrandParent) || AllPairs(endRing, grandParent, IsDeclaredRod);

                var stretch = SpanRelaxation(joint.Node, parent) ?? RingInternalRelaxation(joint.Node)
                    ?? chainNaturalRf ?? 1f;

                var roped = !joint.IsRoot && ropeParents.GetValueOrDefault(joint.Node, -1) == parent;
                joint.StretchStiffness = (chainDeclaresNoStretch && !joint.IsRoot) || DeclaresNoStretch(joint)
                    || (roped && !SpannedByRod(joint.Node, parent) && RingInternalRelaxation(joint.Node) is null)
                    ? 0f
                    : stretch > 0f ? Slider(stretch) : 1f;

                var ringCopies = RingRodCopies(joint.Node);
                if (AuthoredSpring(joint, parent)
                    && !(ringCopies > 0 && SpanRodCopies(joint.Node, parent) > ringCopies))
                {
                    joint.StretchStiffness = 0f;
                }

                if (AuthoredSpring(joint, grandParent))
                {
                    joint.BendSpring = false;
                }

                if (AuthoredSpring(joint, greatGrandParent))
                {
                    joint.TorsionSpring = false;
                }

                joint.BendStiffness = joint.BendSpring ? SpringStiffness(joint, endRing, grandParent, parent) : 0f;
                joint.TorsionStiffness = joint.TorsionSpring ? SpringStiffness(joint, endRing, greatGrandParent, grandParent) : 0f;
                joint.Antishrink = JointContraction(joint, parent, grandParent, greatGrandParent);

                ReadJointSuspender(joint, parent, grandParent, greatGrandParent);
            }

            private void ReadJointSuspender(BoneChainJoint joint, int parent, int grandParent, int greatGrandParent)
            {
                if (RootSuspenderValue(joint, parent, grandParent, greatGrandParent, out var upwardCopies, out var rootSpanSpring) is { } suspender)
                {
                    joint.Suspender = Slider(suspender);
                    joint.ExtraIterations = upwardCopies > 0 ? upwardCopies / 2 - 1 : JointCopies(joint) - 1;
                    if (rootSpanSpring is { } spring)
                    {
                        if (rootNode == parent)
                        {
                            joint.StretchStiffness = Slider(spring);
                        }
                        else if (joint.BendSpring && rootNode == grandParent)
                        {
                            joint.BendStiffness = Slider(spring);
                        }
                        else
                        {
                            joint.TorsionStiffness = Slider(spring);
                        }
                    }
                }
                else if (RootCompanionValue(joint, parent, grandParent, greatGrandParent, out var spanReading) is { } companion)
                {
                    joint.Suspender = Slider(companion);
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
                    ReadFloorIterations(joint, parent);
                }
            }

            private void ReadFloorIterations(BoneChainJoint joint, int parent)
            {
                var copies = JointCopies(joint, floor: true);
                var own = OwnRingRodCopies(joint.Node);
                if (own > 0 && own < copies && parent >= 0 && !joint.BendSpring && !joint.TorsionSpring
                    && joint.StretchStiffness != 0f && EndEffectorRing(joint.Node).Count == 0 && joint.ChildSiblingSpring == 0f)
                {
                    joint.ExtraIterations = own - 1;
                    joint.CrossLinkSurplus = [.. Side(joint.Node).SelectMany(a => Side(parent).Select(b => (a, b, copies - own)))];
                }
                else
                {
                    joint.ExtraIterations = copies - 1;
                }
            }

            private void ReadAnimatedLengths()
            {
                var spanAnimated = new List<BoneChainJoint>();
                foreach (var joint in chain.Joints)
                {
                    if (!DeclaresNoStretch(joint))
                    {
                        joint.AnimatedLength = SpanOnlyAnimated(joint);
                        if (joint.AnimatedLength)
                        {
                            spanAnimated.Add(joint);
                        }

                        continue;
                    }

                    var own = Extrusion(joint.Node);
                    var kids = KidsOf(joint);

                    var animated = own.Exists(cloth.AnimRodNodes.Contains);

                    joint.AnimatedLength = animated && (kids.Count == 0
                        ? cloth.Index.NodeBases.ContainsKey(joint.Node)
                        : kids.TrueForAll(kid => jointRingOf.ContainsKey(kid.Node) && !AnyRod(rodPairs, Extrusion(kid.Node), own))
                            && ((cloth.Index.NodeBases.ContainsKey(joint.Node) && !cloth.SelfCollisionClusters.Any(cluster => cluster.Nodes.Contains(joint.Node)))
                                || kids.TrueForAll(kid => AnyRod(rodPairs, Extrusion(kid.Node), Extrusion(kid.Node)))));

                    if (joint.AnimatedLength)
                    {
                        joint.StretchStiffness = 1f;
                    }
                }

                cloth.ReadAnimatedSpans(spanAnimated, jointByNode, rootNode, Side, Slider);
            }

            private float ChildSiblingValue(BoneChainJoint joint)
            {
                var kids = KidsOf(joint);
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
                                if (!cloth.Simulates(a) && !cloth.Simulates(b))
                                {
                                    continue;
                                }

                                var pair = UnorderedPair(a, b);
                                if (cloth.IsSourceSpring(pair.Item1, pair.Item2)
                                    || !rodRelaxationsByPair.TryGetValue(pair, out var relaxations))
                                {
                                    return 0f;
                                }

                                if (common is null)
                                {
                                    common = [];
                                    foreach (var relaxation in relaxations)
                                    {
                                        if (!common.Exists(seen => MathF.Abs(seen - relaxation) <= ChainReadingTolerance))
                                        {
                                            common.Add(relaxation);
                                        }
                                    }
                                }
                                else
                                {
                                    common.RemoveAll(seen =>
                                        !relaxations.Exists(relaxation => MathF.Abs(seen - relaxation) <= ChainReadingTolerance));
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

            private void MarkRestatedJoints()
            {
                foreach (var joint in chain.Joints)
                {
                    if (joint.IsRoot || joint.ProxyNode < 0 || joint.ExtrudeSides < 2 || !cloth.IsPositionDriven(joint.Node)
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
            }

            private int FirstSimulatedNode()
            {
                var firstSimulated = int.MaxValue;
                foreach (var joint in chain.Joints)
                {
                    if (joint.Node >= cloth.Fe.StaticNodes)
                    {
                        firstSimulated = Math.Min(firstSimulated, joint.Node);
                    }

                    if (DeclaredRing(joint.Node) is { } declaredRing)
                    {
                        foreach (var proxy in declaredRing)
                        {
                            if (proxy >= cloth.Fe.StaticNodes)
                            {
                                firstSimulated = Math.Min(firstSimulated, proxy);
                            }
                        }
                    }
                }

                return firstSimulated;
            }
        }
    }
}
