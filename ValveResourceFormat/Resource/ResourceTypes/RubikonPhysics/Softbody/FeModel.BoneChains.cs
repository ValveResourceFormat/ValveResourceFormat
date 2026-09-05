using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        /// <summary>
        /// A single joint within a reconstructed bone chain.
        /// </summary>
        public sealed class BoneChainJoint
        {
            /// <summary>Gets the control-node index of this joint.</summary>
            public int Node { get; init; }
            /// <summary>Gets the bone name of this joint.</summary>
            public required string Name { get; init; }
            /// <summary>Gets the control-node index of the chain parent, or -1 if this is the chain root.</summary>
            public int ParentNode { get; init; }
            /// <summary>Gets the bone name of the chain parent, or null if this is the chain root.</summary>
            public string? ParentName { get; init; }
            /// <summary>Gets the inverse mass for this node (0 = static anchor).</summary>
            public float InvMass { get; set; }
            /// <summary>
            /// Gets the node whose compiled per-node values describe THIS declaration of the joint, or
            /// -1 to read them from the joint's own node. A bone two chains declare re-registers its own
            /// node once, so only the ring each declaration extruded still carries that declaration's
            /// goal, damping, collision radius, stray radius and simulate flag.
            /// </summary>
            public int ValueNode { get; set; } = -1;
            /// <summary>
            /// Gets the number of auto-generated <c>$cc</c> proxy nodes the compiler placed on THIS joint
            /// (its local ribbon width). Usually equal to the chain's <see cref="BoneChain.ExtrudeSides"/>,
            /// but an end-cap joint can fan wider. Used to override the chain-level extrude per joint so an
            /// end-cap fan is not lost to the uniform chain width.
            /// </summary>
            public int ExtrudeSides { get; set; }
            /// <summary>
            /// Gets one of the <c>$cc</c> proxy nodes generated from this joint, or -1 when it has none.
            /// A joint's own node is position-driven and compiles with no gravity, so the authored
            /// <c>gravity_z</c> survives only on its proxies.
            /// </summary>
            public int ProxyNode { get; set; } = -1;
            /// <summary>
            /// Gets the authored <c>extra_iterations</c> of this joint, recovered from how many copies of
            /// its parent span the compiler emitted (one per iteration, so copies minus one).
            /// </summary>
            public int ExtraIterations { get; set; }
            /// <summary>
            /// Gets the authored <c>suspender</c> of this joint: a single companion rod between this
            /// joint's own ring and its CHAIN ROOT's ring that the compiler adds, carrying this value as
            /// its own <c>flRelaxationFactor</c>. Zero when the joint carries none. Told apart from
            /// <see cref="ExtraIterations"/> by <c>RootSuspenderValue</c> in <c>BuildBoneChains</c>.
            /// </summary>
            public float Suspender { get; set; }
            /// <summary>
            /// Gets whether a rod spans this joint and its grandparent, i.e. whether the source authored a
            /// non-zero <c>bend_spring</c> here.
            /// </summary>
            public bool BendSpring { get; set; }
            /// <summary>
            /// Gets whether a rod spans this joint and its great-grandparent, i.e. whether the source
            /// authored a non-zero <c>torsion_spring</c> here.
            /// </summary>
            public bool TorsionSpring { get; set; }
            /// <summary>
            /// Gets the authored <c>stretch_spring</c> of this joint: the <c>flRelaxationFactor</c> the
            /// compiler wrote on the span between this joint and its chain parent.
            /// </summary>
            public float StretchStiffness { get; set; } = 1f;
            /// <summary>
            /// Gets the authored <c>bend_spring</c> of this joint: the <c>flRelaxationFactor</c> on the
            /// span to its grandparent. Zero when <see cref="BendSpring"/> is false, which is what keeps
            /// the compiler from generating that rod at all.
            /// </summary>
            public float BendStiffness { get; set; }
            /// <summary>
            /// Gets the authored <c>torsion_spring</c> of this joint: the <c>flRelaxationFactor</c> on the
            /// span to its great-grandparent. Zero when <see cref="TorsionSpring"/> is false.
            /// </summary>
            public float TorsionStiffness { get; set; }
            /// <summary>Gets the distance from this joint to its own proxy ring.</summary>
            public float ExtrudeRadius { get; set; }
            /// <summary>
            /// Gets the roll (degrees) added on top of <see cref="ExtrudeTwist"/> to settle the node-base
            /// axis scan of a joint whose scan is a numerical tie. Zero for every other joint.
            /// </summary>
            public float ExtrudeTwistTieNudge { get; set; }
            /// <summary>
            /// Gets the roll (degrees) of this joint's proxy ring about the forward axis, measured in the
            /// joint's rest frame. The ring's unrolled direction is the frame's +Y, so the value authored
            /// as <c>extrude_twist</c> is this angle's complement.
            /// </summary>
            public float ExtrudeTwist { get; set; }
            /// <summary>
            /// Gets the forward distance to a second proxy ring around this joint, which is what the
            /// authored <c>end_effector</c> produces (a tip that fans into two rows rather than one wider
            /// ring). Zero when the joint carries a single ring.
            /// </summary>
            public float EndEffector { get; set; }
            /// <summary>
            /// Gets the forward axis the compiler used to orient this joint's proxy ring (<c>'x'</c>,
            /// <c>'y'</c> or <c>'z'</c>), detected from the ring's own plane normal expressed in the
            /// joint's rest frame. <c>'x'</c> is the default and needs no explicit
            /// <c>extrude_forward_axis</c> authoring.
            /// </summary>
            public char ForwardAxis { get; set; } = 'x';
            /// <summary>
            /// Gets whether the source declared this joint a second time, in a plain chain after the
            /// extruding one. The plain declaration re-registers the joint node with its own values
            /// and ties it to its parent with a rod of its own, so the node and the ring extruded
            /// from it carry two different sets of values.
            /// </summary>
            public bool Restated { get; set; }
            /// <summary>Gets a value indicating whether this joint is simulated (invMass &gt; 0).</summary>
            public bool Simulated => InvMass > 0f;
            /// <summary>Gets a value indicating whether this joint is the chain root.</summary>
            public bool IsRoot => ParentNode < 0;
        }

        /// <summary>
        /// A reconstructed bone chain: a static anchor bone plus all of its simulated descendants.
        /// </summary>
        public sealed class BoneChain
        {
            /// <summary>Gets the anchor (root) bone name.</summary>
            public required string RootBone { get; init; }
            /// <summary>
            /// Gets the suffix that tells this chain apart from another declaration over the same root
            /// bone, empty for a bone only one chain declares.
            /// </summary>
            public string DeclarationSuffix { get; set; } = string.Empty;
            /// <summary>Gets the joints, root first, in pre-order (a parent always precedes its children).</summary>
            public List<BoneChainJoint> Joints { get; } = [];
            /// <summary>
            /// Gets the ribbon width the compiler baked as auto-generated <c>$cc</c> proxy nodes per joint:
            /// 0/1 = a plain 1-wide rope (no extrude), 2+ = an extruded strip or tube. Drives the
            /// ClothChain's <c>extrude_sides</c> so the recompile regenerates the same proxy count.
            /// </summary>
            public int ExtrudeSides { get; set; }
            /// <summary>Gets the mean distance from a joint bone to its <c>$cc</c> proxy nodes (the extrude half-width).</summary>
            public float ExtrudeRadius { get; set; }
            /// <summary>
            /// Gets the roll (degrees) applied to the extruded proxy ring about the chain's forward axis.
            /// Recovered from where the compiler actually placed the <c>$cc</c> proxies in the joint's rest
            /// frame; a chain whose ring is not rolled recovers 0.
            /// </summary>
            public float ExtrudeTwist { get; set; }
        }

        // One reconstructed ClothChain before its joints are walked: which bone roots it, which children
        // of a given node it keeps, and which of that node's rings belongs to it.
        sealed class ChainSpec
        {
            public int Root { get; init; }
            public bool RinglessRoot { get; init; }
            /// <summary>The children each listed node keeps in this chain; a node absent keeps all of them.</summary>
            public Dictionary<int, HashSet<int>>? ChildrenOf { get; set; }
            /// <summary>The ring each listed node extruded in THIS declaration; empty for a ringless one.</summary>
            public Dictionary<int, List<int>>? RingOf { get; set; }
            public string Suffix { get; set; } = string.Empty;
        }

        // How far apart two proxies must sit along a joint's forward axis to count as separate rings. The
        // compiler ignores an end_effector below 0.05, so anything closer than that is one ring.
        const float EndEffectorRingTolerance = 0.05f;

        // The trailing "_<n>" the compiler appends to a generated ring node's name, or -1 for a name that
        // carries none (the "_Ctr" centre node of an end_effector below two sides).
        static int RingSuffixIndex(string name)
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
        /// <remarks>
        /// The compiler numbers every ring ONE declaration extrudes continuously - an <c>end_effector</c>
        /// second row carries on the count rather than restarting.
        /// A second declaration over an already-extruded bone builds a NEW ring and numbers it from 0
        /// again, so an index at or below one already seen starts another declaration. The groups are in
        /// control-node order, which is the compiler's static-then-dynamic layout rather than declaration
        /// order, so nothing downstream may read group 0 as "the first ClothChain".
        /// </remarks>
        static List<List<int>>? SplitRingDeclarations(List<int> proxies, string[] names)
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
        /// <remarks>
        /// A second declaration over an already-extruded joint does not reuse the ring the first built; it
        /// builds its own, and each ring keeps the values ITS declaration authored. Which children belong
        /// to which declaration is read off the rods: a child's parent span lands on the ring its own
        /// declaration extruded, and on the bare joint node for a declaration that extrudes none. A child
        /// two pinned nodes leave no rod between at all is placed by what its descendants reach instead.
        /// </remarks>
        void SplitRingDeclarations(List<ChainSpec> specs, List<int>?[] children, int[] realParent,
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
                        if (rodPairs.Contains(a < b ? (a, b) : (b, a)))
                        {
                            hits++;
                        }
                    }
                }

                return hits;
            }

            // Which of a bone's ring declarations the given nodes are rodded to, or -1 for none of them.
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

            // Every node under `from` in this spec, plus the rings they extruded.
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

            // The first bone of this spec whose declarations are still unresolved.
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

                // Where the bone's own ring is already fixed - its parent's split chose it - only the
                // children still need placing, and no further declaration of this bone is spawned.
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

                    // A declaration whose span joins two PINNED nodes leaves no rod at all - the
                    // compiler drops one that constrains nothing - so a child both declarations pin
                    // has evidence for one of them only. Every ring the child carries was built by
                    // SOME declaration of its parent, so where the two carry the same number of them
                    // the leftovers pair off.
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

                    // A declaration that extrudes nothing leaves only the bare joint node behind, so it is
                    // recoverable at all only where a child hangs off that node.
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

        // extrude_forward_axis selector quaternions: a +90-degree rotation about local Z for 'y' (maps +X
        // to +Y), a -90-degree rotation about local Y for 'z' (maps +X to +Z). 'x' uses
        // Quaternion.Identity. Composed with a joint's own rest rotation (ringFrame = jointRot *
        // axisSelect), this re-labels which local axis is "forward".
        static readonly Quaternion ExtrudeAxisSelectY = new(0f, 0f, 0.70710677f, 0.70710677f);
        static readonly Quaternion ExtrudeAxisSelectZ = new(0f, -0.70710677f, 0f, 0.70710677f);

        static Quaternion ExtrudeAxisSelectQuaternion(char axis) => axis switch
        {
            'y' => ExtrudeAxisSelectY,
            'z' => ExtrudeAxisSelectZ,
            _ => Quaternion.Identity,
        };

        // Detects which forward axis the compiler used to orient a joint's proxy ring. Every ring point
        // lies in the plane perpendicular to the selected forward axis, so - expressed in the joint's own
        // rest frame - that axis' component is ~0 for every point while the other two carry the actual
        // ring geometry. 'x' is preferred whenever it qualifies: a ring reproducible through the default
        // axis needs no explicit authoring, and a ring whose twist falls on a multiple of 90 degrees ties
        // two axes at exactly 0 while remaining reproducible through 'x' with an adjusted twist. Only a
        // ring that does not lie in the default axis' plane at all needs 'y' or 'z'. The tolerance is
        // relative to the ring's own scale, so it does not depend on model units.
        const float ExtrudeForwardAxisTolerance = 0.02f;

        static char DetectExtrudeForwardAxis(Vector3 jointPos, Quaternion jointRot, List<int> ring, Vector3[] positions)
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

        // The control nodes a compiled cloth SHEET vertex hangs off: its m_CtrlOffsets primary plus every
        // m_CtrlSoftOffsets bone behind it. A chain's own extrude ring carries the same two arrays, so the
        // child is required to be a sheet vertex rather than any generated node.
        HashSet<int> SheetSkinnedNodes()
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
        /// The rod graph as the chain reconstruction reads it: which node pairs carry a rod, every one of
        /// their relaxation factors, and the same for the rigid rods alone.
        /// </summary>
        /// <remarks>
        /// A suspender companion rod can span the same pair as an ordinary one but carries a DIFFERENT
        /// relaxation factor (see RootSuspenderValue), which a rod count alone cannot distinguish from an
        /// extra_iterations repeat.
        /// </remarks>
        (HashSet<(int, int)> Pairs, Dictionary<(int, int), List<float>> RelaxationsByPair,
            Dictionary<(int, int), List<float>> RigidRelaxationsByPair) BuildRodGraph()
        {
            var pairs = new HashSet<(int, int)>();
            var relaxationsByPair = new Dictionary<(int, int), List<float>>();
            var rigidRelaxationsByPair = new Dictionary<(int, int), List<float>>();
            foreach (var rod in Rods)
            {
                var pair = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
                pairs.Add(pair);
                if (!relaxationsByPair.TryGetValue(pair, out var relaxations))
                {
                    relaxations = [];
                    relaxationsByPair[pair] = relaxations;
                }

                relaxations.Add(rod.RelaxationFactor);

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

            return (pairs, relaxationsByPair, rigidRelaxationsByPair);
        }

        /// <summary>
        /// Each real bone mapped to the auto-generated <c>$cc&lt;bone&gt;</c> proxy nodes parented straight
        /// to it (its ribbon width), and the inverse map from ring vertex to owning bone.
        /// </summary>
        /// <remarks>
        /// Restricted to the <c>$cc</c> prefix (the ClothChain extrude proxies), NOT every <c>$</c>-node:
        /// a <c>$cloth_m</c> SHEET must not be mistaken for a chain's own width. Used to keep a ribbon's
        /// position-driven TIP joint in the chain, and to recover each chain's extrude width.
        /// </remarks>
        (Dictionary<int, List<int>> ChildrenOf, Dictionary<int, int> OwnerOf) BuildProxyRings()
        {
            var childrenOf = new Dictionary<int, List<int>>();

            // Old-era compiles ship m_SkelParents empty; there the ring's anchor bone still survives in
            // m_CtrlOffsets (each generated ring vertex carries its bone-local anchor offset).
            Dictionary<int, int>? ctrlOffsetParents = null;
            if (!HasCompiledSkelParents && CtrlOffsets.Length > 0)
            {
                ctrlOffsetParents = new Dictionary<int, int>(CtrlOffsets.Length);
                foreach (var off in CtrlOffsets)
                {
                    ctrlOffsetParents[off.CtrlChild] = off.CtrlParent;
                }
            }

            for (var node = 0; node < CtrlNames.Length; node++)
            {
                // A strip's second column is generated too, but named after the bone it widens instead of
                // carrying the "$cc" prefix, so it counts as ribbon width the same way.
                if (!CtrlNames[node].StartsWith("$cc", StringComparison.Ordinal)
                    && !(!IsProxyNodeName(CtrlNames[node]) && IsGeneratedNodeName(CtrlNames[node])))
                {
                    continue;
                }

                var pp = node < SkelParents.Length ? SkelParents[node] : -1;
                if (pp < 0 && ctrlOffsetParents is not null)
                {
                    pp = ctrlOffsetParents.GetValueOrDefault(node, -1);
                }
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

        // How far apart two ring rods' readings may sit, as a fraction of the reading itself, and still
        // count as the same authored value.
        internal const float ChainRingCurvatureAgreement = 0.01f;

        /// <summary>
        /// Gets the authored <c>add_curvature</c>, read back out of the bend rods the compiler builds
        /// across a chain joint's own extrude ring. Such a rod joins the far corners of the two ring faces
        /// that meet along one strip edge, and where the ring around that edge is symmetric - both corners
        /// the same distance from it, neither sliding along it - the general minimum collapses to
        /// <c>flMaxDist * sin(add_curvature * pi / 2)</c>, capped at the rod's own rest span as everywhere
        /// else. That is what makes the value readable without the strip faces themselves.
        /// <para>
        /// The symmetry is not assumed, it is required: a tapering or unevenly rolled ring puts the two
        /// corners at different distances from the hinge and every rod then reads a different value, so
        /// the reading is taken only when they all agree, and a disagreeing ring recovers 0 rather than a
        /// number none of its rods support. A ring the value never bends also recovers 0, as does a chain
        /// with no extrude ring at all.
        /// </para>
        /// </summary>
        public float ChainRingCurvature
        {
            get
            {
                var (_children, ringOwnerOf) = BuildProxyRings();
                if (ringOwnerOf.Count == 0)
                {
                    return 0f;
                }

                // Only the "$cc" extrude proxies, not every generated node BuildProxyRings groups: a
                // strip's second column is named after the bone it widens and is no ring, so its rods sit
                // on no hinge this reading knows the geometry of.
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

        // Whether `to` is reachable from `from` by following the parent links - assigning
        // from -> ... -> to plus to -> from would close a parent cycle, which the compiler's
        // topological sort recurses into until the stack runs out.
        static bool ReachesByParents(int[] realParent, int from, int to)
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
        /// Reconstructs bone chains from the control-node topology, ignoring auto-generated cloth proxy nodes.
        /// Each chain is rooted at a real bone with no real-bone parent and contains all of its real descendants.
        /// Chains are returned ordered by the lowest SIMULATED control-node index any of their joints or
        /// extruded rings occupies, which is the order the compiler lays their simulated nodes out in.
        /// A chain with no simulated node is ordered by its lowest static node instead.
        /// </summary>
        public List<BoneChain> BuildBoneChains()
        {
            var chains = new List<BoneChain>();
            var chainFirstSimulated = new Dictionary<BoneChain, int>();
            var n = CtrlNames.Length;
            if (n == 0)
            {
                return chains;
            }

            // Mark real skeleton bones (everything that is not an auto-generated cloth node).
            var isReal = new bool[n];
            for (var i = 0; i < n; i++)
            {
                isReal[i] = !IsGeneratedNodeName(CtrlNames[i]);
            }

            // For each real node, resolve its parent among real nodes. The direct skeleton parent is used when
            // it is itself a real bone; otherwise the node is treated as a chain root. (Proxy-mesh parenting is
            // intentionally not followed here - that topology belongs to the later proxy-mesh phase.)
            //
            // m_SkelParents is indexed in CONTROL-NODE space, so it collapses through any intermediate real
            // skeleton bone that never became a control node itself: bones wired to each other only by
            // explicit ClothSpring resolve their "real parent" to a distant shared ancestor and read as one
            // chain. A link therefore needs an m_Rods entry between the node and its candidate real parent.
            // A chain compiles to a fully-connected local rod mesh among its own joints (see
            // AddClothProxySprings), which always includes the direct parent-child pair.
            var (rodPairs, rodRelaxationsByPair, rigidRodRelaxationsByPair) = BuildRodGraph();
            var (proxyChildrenOf, ringOwnerOf) = BuildProxyRings();

            // Every chain link the compiler surfaces leaves one source element behind, and a joint with no
            // extrude ring of its own contributes its bare node to it: a ringless parent joined to a ringed
            // child is recorded as that parent plus the child's whole ring. Where a model records its other
            // chain surfaces but not this one, the parent is not part of the chain and the skeleton link is
            // the artist's bone hierarchy alone. The parent also has to drive proxy-sheet vertices through
            // the offset network, which is what keeps its own node in the model independently of the chain.
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

                var rodLinked = rodPairs.Contains(p < i ? (p, i) : (i, p));

                // A $cc-proxied chain carries its rods among the auto-generated $cc PROXY nodes, never
                // between the real chain bones, so the rod test alone never links it. Two consecutive
                // position-driven SIMULATED real bones are linked directly instead. The collapsed-ancestor
                // false chain the rod test guards against cannot satisfy this: both ends are static.
                var bothDrivenSim = i >= FirstPositionDrivenNode && p >= FirstPositionDrivenNode
                    && i < NodeInvMasses.Length && NodeInvMasses[i] != 0f
                    && p < NodeInvMasses.Length && NodeInvMasses[p] != 0f;

                // A node that carries its own $cc proxies is a ribbon joint, so it links to its real parent
                // cloth node whatever that parent's role: a simulated body bone, another $cc-proxied ribbon
                // bone, or a pinned anchor with no proxies of its own. Requiring i to be $cc-proxied is
                // itself the guard against the collapsed-ancestor false chain, whose nodes carry none.
                var proxyRibbon = proxyChildrenOf.ContainsKey(i);

                // A bone the compiler built a hinge anchor for is a hinged chain's root by construction,
                // so its real children belong to that chain however few traces they leave of their own. The
                // hinge puts the whole ribbon's proxies on the ROOT, which is what makes the three tests
                // above miss these chains.
                var hingedRoot = Array.IndexOf(CtrlNames, HingeAnchorPrefix + CtrlNames[p]) >= 0
                    || RigidHingeJoints.ContainsKey(p);

                // A joint whose parent carries a stiff hinge is joined to it by the bend rather than by a
                // rod, so the rod test alone drops it and the chain ends one joint short.
                var bendLinked = false;
                foreach (var bend in KelagerBends)
                {
                    if (bend.MidNode == p && (bend.End0 == i || bend.End1 == i))
                    {
                        bendLinked = true;
                        break;
                    }
                }

                // A joint can reach its chain by the BEND rod to its grandparent rather than by one to its
                // own parent, which leaves the direct pair the rod test looks for absent and ends the chain
                // a joint short. A grandparent needs a real parent of its own, so the static-root false
                // chain the rod test guards against cannot reach this: its resolved parent is a root.
                var grandParent = p < SkelParents.Length ? SkelParents[p] : -1;
                var bendRodLinked = grandParent >= 0 && grandParent < n && isReal[grandParent]
                    && rodPairs.Contains(grandParent < i ? (grandParent, i) : (i, grandParent));

                // Where the parent extrudes, the joint's rod lands on the parent's ring instead of on the
                // parent itself.
                var ringLinked = false;
                if (proxyChildrenOf.TryGetValue(p, out var parentRing))
                {
                    foreach (var ring in parentRing)
                    {
                        if (rodPairs.Contains(ring < i ? (ring, i) : (i, ring)))
                        {
                            ringLinked = true;
                            break;
                        }
                    }
                }

                if ((rodLinked || bothDrivenSim || proxyRibbon || hingedRoot || bendLinked
                    || bendRodLinked || ringLinked) && !RinglessLinkUnrecorded(p, i))
                {
                    realParent[i] = p;
                }
            }

            // A rod between two pinned nodes constrains nothing and the compiler leaves it out, so a chain
            // whose top two joints are both static loses the one link every rule above reads and roots a
            // joint too low. The parent joint keeps only its own compiled m_SkelParents entry, which is
            // taken here, but only where it names the node's DIRECT skeleton parent: the control-space
            // collapse invents distant ancestors, which is what the rod test guards against. A bone the
            // SHEET skins to is excluded on both ends, its compiled parent coming from the skin hierarchy
            // rather than from a chain.
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

                // Only a link one end of which the rules above already placed in a chain, so a pair of
                // pinned bones no chain reaches stays the pair of ClothNodes it reconstructs as today.
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
                            || sheetSkinned.Contains(i) || sheetSkinned.Contains(p))
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

            // With m_SkelParents empty the link rules above never fire, so the chain's own compiled rod
            // mesh is the remaining parent evidence: consecutive joints are joined by rods between the
            // joints and their rings, and joints are emitted root-first, so the lower-indexed side of the
            // rod evidence is the parent. Restricted to ring-bearing bones - a rod network among bare real
            // bones is ClothNode/ClothSpring authoring, not a chain.
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
                    if (rod.NodeA == rod.NodeB)
                    {
                        continue;
                    }

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

                    var key = a > b ? (b, a) : (a, b);
                    linkCounts[key] = linkCounts.GetValueOrDefault(key) + 1;
                }

                // The skeleton orients a link where it can: rod evidence alone cannot tell parent from
                // child on a strap anchored at both ends. Only a rod-evidenced pair is linked.
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

                        // The link goes to the nearest ANCESTOR the rods evidence, not merely the
                        // nearest control-node ancestor: a strip whose rods skip a joint's own
                        // skeleton parent and land on the shared bone above it otherwise stays
                        // unoriented, and the lower-index fallback below then inverts the pair into
                        // a parent cycle.
                        var ancestor = SkeletonBoneParents.GetValueOrDefault(CtrlNames[i]);
                        var guard = 0;
                        while (ancestor is not null && guard++ < 256)
                        {
                            if (nodeByName.TryGetValue(ancestor, out var p) && p != i)
                            {
                                var key = p > i ? (i, p) : (p, i);
                                if (linkCounts.ContainsKey(key))
                                {
                                    realParent[i] = p;
                                    break;
                                }
                            }

                            ancestor = SkeletonBoneParents.GetValueOrDefault(ancestor);
                        }
                    }
                }

                // Remaining unoriented pairs: joints are emitted root-first, so the lower-indexed side is
                // the parent.
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
                        && !ReachesByParents(realParent, link.Parent, child))
                    {
                        realParent[child] = link.Parent;
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

            // A joint the compiler extruded carries its children's rods on its RING; one authored with
            // extrude_sides 0 carries them on the BONE. A reconstructed root that shows BOTH patterns
            // across its own children was authored as SEPARATE chains sharing that root, only one of
            // which extruded it.
            bool AnyRod(IEnumerable<int> a, IEnumerable<int> b)
            {
                foreach (var x in a)
                {
                    foreach (var y in b)
                    {
                        if (rodPairs.Contains(x < y ? (x, y) : (y, x)))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            // The compiler fits an orientation-locked joint over its own chain neighbourhood: its parent
            // plus the rings of the parent's children WITHIN THE SAME ClothChain. A fit that names the
            // parent therefore enumerates that joint's real siblings, and a sibling this chain has but the
            // fit does not was authored elsewhere. A fit that does NOT name the parent is taken over some
            // other neighbourhood and says nothing about siblings, so it never splits.
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
                // A real bone with no real descendants is not a cloth chain, unless it carries its own
                // extrude ring - a lone ring-bearing bone is a single-joint chain.
                if (children[rootNode] is not { } rootKids)
                {
                    if (!proxyChildrenOf.ContainsKey(rootNode))
                    {
                        continue;
                    }

                    chainSpecs.Add(new ChainSpec { Root = rootNode });
                    continue;
                }

                List<int> ringAnchored = [];
                List<int> boneAnchored = [];
                if (rootKids.Count > 1 && proxyChildrenOf.TryGetValue(rootNode, out var rootRing) && rootRing.Count > 0)
                {
                    foreach (var kid in rootKids)
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
                            // No rod either way says nothing about which chain the child was in, so the
                            // split has no evidence for it and the whole root stays merged.
                            ringAnchored.Clear();
                            boneAnchored.Clear();
                            break;
                        }
                    }
                }

                var groups = ringAnchored.Count > 0 && boneAnchored.Count > 0
                    ? [(ringAnchored, false), (boneAnchored, true)]
                    : new List<(List<int> Kids, bool RinglessRoot)> { (rootKids, false) };

                SplitGroupsByFitSet(rootNode, groups);

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
            }

            SplitRingDeclarations(chainSpecs, children, realParent, proxyChildrenOf, rodPairs);

            foreach (var spec in chainSpecs)
            {
                var rootNode = spec.Root;
                var ringlessRoot = spec.RinglessRoot;
                var chain = new BoneChain { RootBone = CtrlNames[rootNode], DeclarationSuffix = spec.Suffix };

                // The ring a joint extruded in THIS declaration. A bone two chains declare carries one
                // ring per declaration under the same name, so reading them all as one ring merges two
                // rings into a single wider one and every value the second declaration left on its own
                // ring is lost with it.
                List<int>? DeclaredRing(int node)
                    => spec.RingOf is not null && spec.RingOf.TryGetValue(node, out var ring)
                        ? ring
                        : proxyChildrenOf.GetValueOrDefault(node);

                var subtreeFirstNode = new Dictionary<int, int>();

                // The children this declaration keeps under one joint, in the order they were declared.
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

                // The lowest node of the chain's own simulated block a joint's subtree occupies: its own
                // node, the ring it extruded and everything declared under it. The compiler lays that
                // block out in declaration order, and a position-driven joint sits past the whole of it
                // whatever its place in the declaration.
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

                // The ribbon width the compiler baked into $cc proxy nodes: how many it placed per joint
                // (extrude_sides) and their mean offset (extrude_radius).
                //
                // extrude_sides forces EVERY joint to the same width, so it reproduces a uniform strip
                // exactly but cannot reproduce a ribbon whose END-CAP joint fans wider than its body. The
                // width is the MODE, the one most joints share, so the body is reproduced exactly and only
                // the tip fan is dropped. 0/1 stays a plain rope with no extrude.
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

                    // A bone this declaration shares with another leaves its own goal, damping, radius,
                    // stray radius and simulate flag on the ring it extruded; the joint node itself keeps
                    // only one declaration's.
                    if (spec.RingOf is not null && spec.RingOf.ContainsKey(joint.Node))
                    {
                        joint.ValueNode = proxies[0];
                        joint.InvMass = proxies[0] < NodeInvMasses.Length ? NodeInvMasses[proxies[0]] : joint.InvMass;
                    }

                    // A "$cc<bone>_Ctr" proxy is the single centre node the compiler emits for an
                    // end_effector with extrude_sides < 2 - it is not a ring member at all. A joint whose
                    // proxies are ALL centre nodes has no side ring: end_effector is the centre's forward
                    // displacement and the ring is left empty. Such a joint takes no part in the
                    // body-width vote below.
                    var ring = proxies;
                    List<int>? endEffectorRing = null;
                    if (proxies.TrueForAll(p => CtrlNames[p].EndsWith("_Ctr", StringComparison.Ordinal))
                        && joint.Node < InitPoseRotations.Length && joint.Node < InitPosePositions.Length
                        && proxies[0] < InitPosePositions.Length)
                    {
                        var centreOffset = Vector3.Transform(
                            InitPosePositions[proxies[0]] - InitPosePositions[joint.Node],
                            Quaternion.Conjugate(InitPoseRotations[joint.Node]));
                        if (MathF.Abs(centreOffset.X) >= EndEffectorRingTolerance)
                        {
                            joint.EndEffector = centreOffset.X;
                            joint.ExtrudeSides = 0;
                            joint.ProxyNode = proxies[0];
                            continue;
                        }
                    }

                    // A joint whose proxies sit at two different distances along its forward axis carries a
                    // second ring, which is what end_effector produces. Its ring width is half the proxy
                    // count, not the whole of it - taking the whole count instead lays them out as one
                    // wider ring and every proxy lands somewhere the original never put it.
                    if (joint.Node < InitPoseRotations.Length && joint.Node < InitPosePositions.Length)
                    {
                        var forwardOf = new Dictionary<int, float>(proxies.Count);
                        foreach (var proxy in proxies)
                        {
                            if (proxy < InitPosePositions.Length)
                            {
                                forwardOf[proxy] = Vector3.Transform(
                                    InitPosePositions[proxy] - InitPosePositions[joint.Node],
                                    Quaternion.Conjugate(InitPoseRotations[joint.Node])).X;
                            }
                        }

                        if (forwardOf.Count == proxies.Count)
                        {
                            // The joint's OWN ring sits at forward ~= 0, centred on the joint; an
                            // end_effector ring is displaced from that by a signed amount that can go
                            // either way, so the near ring is whichever cluster sits closest to 0 rather
                            // than whichever has the smaller signed value.
                            var minAbs = forwardOf.Values.Min(MathF.Abs);
                            var maxAbs = forwardOf.Values.Max(MathF.Abs);
                            if (maxAbs - minAbs > EndEffectorRingTolerance)
                            {
                                var nearRing = proxies.Where(p => MathF.Abs(forwardOf[p]) - minAbs <= EndEffectorRingTolerance).ToList();
                                if (nearRing.Count > 0 && nearRing.Count < proxies.Count)
                                {
                                    // One reference value per ring rather than an average across the
                                    // cluster: the near ring's smallest-magnitude member and the far
                                    // ring's largest-magnitude one.
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
                                InitPosePositions[joint.Node], InitPoseRotations[joint.Node], proxies, InitPosePositions);
                        }

                        // The ring is laid out around the joint's forward axis, so the roll the compiler
                        // used shows up as the angle of the first proxy in the RING's own frame - the
                        // joint's rest rotation composed with the forward-axis selector, not the joint's
                        // rest rotation alone (the two coincide only when the axis is the default 'x').
                        // A hinge re-lays the joint's own ring along its hinge vector, overriding the
                        // authored width and roll on that ring alone. The end-effector ring the same joint
                        // extrudes is left where the authored extrude put it, so it is the only place the
                        // authored values still survive on a hinged joint.
                        var measured = endEffectorRing is { Count: > 0 } && IsHingedJoint(joint.Node)
                            ? endEffectorRing
                            : proxies;

                        if (joint.Node < InitPoseRotations.Length && measured[0] < InitPosePositions.Length)
                        {
                            var ringFrame = InitPoseRotations[joint.Node] * ExtrudeAxisSelectQuaternion(joint.ForwardAxis);
                            var offset = Vector3.Transform(
                                InitPosePositions[measured[0]] - InitPosePositions[joint.Node],
                                Quaternion.Conjugate(ringFrame));
                            if (new Vector2(offset.Y, offset.Z).LengthSquared() > 1e-6f)
                            {
                                var twist = float.RadiansToDegrees(MathF.Atan2(offset.Y, offset.Z));
                                joint.ExtrudeTwist = twist;
                                twists.Add(twist);
                            }

                            joint.ExtrudeRadius = measured == proxies
                                ? Vector3.Distance(InitPosePositions[joint.Node], InitPosePositions[measured[0]])
                                : new Vector2(offset.Y, offset.Z).Length();
                        }

                        foreach (var proxy in proxies)
                        {
                            if (proxy < InitPosePositions.Length)
                            {
                                radii.Add(Vector3.Distance(InitPosePositions[joint.Node], InitPosePositions[proxy]));
                            }
                        }
                    }
                }

                // Body width = most common per-joint count; tie-break toward the SMALLER (an end cap only
                // ever ADDS proxies, so the smaller of two equally-common widths is the body, not the cap).
                var bodySides = sideFrequency
                    .OrderByDescending(static kv => kv.Value)
                    .ThenBy(static kv => kv.Key)
                    .Select(static kv => kv.Key)
                    .FirstOrDefault();

                // Extrude whenever the body carries proxies at all, not only 2-wide strips: a 1-wide body
                // with one $cc proxy per joint is not the same as a genuine 0-proxy rope. A 0-width body
                // (empty sideFrequency, so bodySides 0) gets no extrude.
                if (bodySides >= 1)
                {
                    // extrude_sides' authored range is [0,4]; a wider strip is clamped (best-effort width).
                    chain.ExtrudeSides = Math.Min(bodySides, 4);
                    chain.ExtrudeRadius = radii.Count > 0 ? radii.Average() : 0f;
                    chain.ExtrudeTwist = twists.Count > 0 ? twists.Average() : 0f;
                }

                // A joint's bend/torsion springs are what make the compiler span a rod to its grandparent
                // and great-grandparent, so the presence of those rods is what the source authored. On an
                // extruding chain that span lands between the two joints' extruded RINGS and never between
                // the joint nodes, so both have to be looked at or the spring reads as off on every joint
                // of every such chain.
                var jointByNode = chain.Joints.ToDictionary(static j => j.Node);

                // A joint that extrudes carries a span on its ring instead of on itself, so the ring stands
                // in for the joint wherever it has one. A joint that also extrudes an end_effector owns TWO
                // rings; only its own - the near one - carries its spans, so the crossing tests read that
                // one. The end-effector ring sits a joint further down and carries the spans of that
                // position instead.
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

                    return DeclaredRing(end) is { Count: > 0 } ring ? ring : [end];
                }

                // THIS chain's own natural, non-suspendered relaxation factor: what every UNDOUBLED rod
                // (a pair with exactly one recorded copy) among the chain's own nodes carries. A
                // chain-level modifier such as a non-default stretch_spring moves it off the compiler's
                // 1.0 default uniformly. Null when the chain's undoubled rods disagree or it has none, in
                // which case RootSuspenderValue reads extra_iterations alone.
                var chainNodes = new HashSet<int>();
                foreach (var chainJoint in chain.Joints)
                {
                    chainNodes.Add(chainJoint.Node);
                    foreach (var ringNode in Side(chainJoint.Node))
                    {
                        chainNodes.Add(ringNode);
                    }
                }

                float? chainNaturalRf = null;
                var chainNaturalRfConsistent = true;
                foreach (var kv in rodRelaxationsByPair)
                {
                    if (!chainNaturalRfConsistent || kv.Value.Count != 1
                        || !chainNodes.Contains(kv.Key.Item1) || !chainNodes.Contains(kv.Key.Item2))
                    {
                        continue;
                    }

                    if (chainNaturalRf is { } already && MathF.Abs(already - kv.Value[0]) > 1e-4f)
                    {
                        chainNaturalRfConsistent = false;
                        chainNaturalRf = null;
                        continue;
                    }

                    chainNaturalRf = kv.Value[0];
                }

                // The compiler writes a chain rod's flRelaxationFactor straight from the slider that
                // generated it: the span to the parent carries the joint's stretch_spring, the span to the
                // grandparent its bend_spring and the span to the great-grandparent its torsion_spring. An
                // extruding joint carries the span on its ring, so the factor is read across the whole
                // ring-to-ring crossing and returned only where every rod there agrees. A crossing a
                // suspender or an extra_iterations repeat has doubled carries two different factors and
                // reads as null, leaving the chain's own natural factor as the value. A crossing that also
                // carries a LENGTH-BANDED rod is read again over its rigid rods alone: every slider-driven
                // chain rod pins min to max, so a banded companion is some other construct and its factor
                // is not the slider's.
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
                                if (!byPair.TryGetValue(a < b ? (a, b) : (b, a), out var relaxations))
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

                // The rods WITHIN one joint's own extrusion - ring to ring, and the joint to its ring -
                // carry that joint's stretch_spring too, which is the only place a chain ROOT records it:
                // a root has no parent span for SpanRelaxation to read.
                float? RingInternalRelaxation(int node)
                {
                    if (DeclaredRing(node) is not { Count: > 0 } ring)
                    {
                        return null;
                    }

                    var extrusion = new List<int>(ring) { node };
                    extrusion.Sort();
                    float? value = null;
                    for (var i = 0; i < extrusion.Count; i++)
                    {
                        for (var j = i + 1; j < extrusion.Count; j++)
                        {
                            if (!rodRelaxationsByPair.TryGetValue((extrusion[i], extrusion[j]), out var relaxations))
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

                float? SpanRelaxation(int node, int other) => RelaxationAcross(Side(node), other);

                // The spring spans the two sides in FULL: anything short of that is some other construct
                // passing between them - a surface the sheet rebuilds, say - and turning the spring on to
                // claim it would add every pair it does not have.
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
                            if (!rodPairs.Contains(a < b ? (a, b) : (b, a)))
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

                    if (rodPairs.Contains(node < other ? (node, other) : (other, node)))
                    {
                        return true;
                    }

                    return AllSpanned(Side(node), other);
                }

                // An end_effector ring stands one joint deeper than the joint that extrudes it: its own
                // span reaches that joint's ring, its bend span the joint's PARENT and its torsion span
                // the joint's GRANDPARENT. Both are switched by the extruding joint's own bend_spring
                // and torsion_spring, so that crossing is evidence for those two flags as well.
                List<int> EndEffectorRing(int node)
                    => endEffectorRingOf.TryGetValue(node, out var far) ? far : [];

                // An extra solver iteration repeats the rods a joint generates upward - the span to its
                // parent plus its bend and torsion spans - and repeats them ALL, uniformly. Each span is
                // read across both ends in full (an extruding joint carries it on its ring), and the whole
                // ring-to-ring set has to be present: an end cap that fans wider than its parent reaches
                // only part of the tip ring, so its crossing pairs are doubled by geometry while the set
                // stays incomplete. Disagreement or a gap means no iteration.
                //
                // Three things are not evidence. A joint's own ring edge tracks the ring's shape rather
                // than the iteration count. A span running DOWN to a deeper joint belongs to that joint's
                // count. And only RIGID rods count: a span rod carries flMinDist == flMaxDist, while
                // add_curvature lands one slack rod on the index-aligned ring pairs of the bend span
                // alone, which reads one higher on those pairs than on the rest of the set.
                // The rigid rods on one joint's span to <paramref name="other"/>, counted across the whole
                // ring-to-ring set and 0 unless every pair of it agrees. -1 marks a span the joint does not
                // reach at all.
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
                            var count = rigidRodRelaxationsByPair.TryGetValue(a < b ? (a, b) : (b, a), out var rigid)
                                ? rigid.Count
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

                int JointCopies(BoneChainJoint joint)
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
                                var count = rigidRodRelaxationsByPair.TryGetValue(a < b ? (a, b) : (b, a), out var rigid)
                                    ? rigid.Count
                                    : 0;
                                if (count == 0 || (copies != 0 && count != copies))
                                {
                                    return false;
                                }

                                copies = count;
                            }
                        }

                        return true;
                    }

                    var parentNode = joint.ParentNode;
                    var grand = parentNode >= 0 && jointByNode.TryGetValue(parentNode, out var g1) ? g1.ParentNode : -1;
                    var greatGrand = grand >= 0 && jointByNode.TryGetValue(grand, out var g2) ? g2.ParentNode : -1;

                    return Repeats(parentNode)
                        && (!joint.BendSpring || Repeats(grand))
                        && (!joint.TorsionSpring || Repeats(greatGrand))
                        ? Math.Max(copies, 1)
                        : 1;
                }

                // Every upward pair this joint generates must carry EXACTLY baseCopies rods at
                // <paramref name="naturalRf"/> plus baseCopies more at ONE other shared value, or this
                // returns null.
                //
                // A candidate of 1.0 is accepted only when naturalRf is ALSO 1.0. The compiler emits a
                // flat 1.0 on every copy an extra_iterations repeat adds, whatever the chain's own natural
                // factor, so on a chain whose natural factor is something else a lone 1.0 copy is that
                // repeat rather than a suspender. Only a candidate at a third value, matching neither the
                // natural factor nor 1.0, is unambiguous.
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

                // A suspender adds exactly ONE companion rod between a joint's own ring and its CHAIN
                // ROOT's ring - never its immediate parent, unless the root happens to BE that parent.
                // extra_iterations is unrelated: it repeats a joint's own parent, grandparent (if
                // bend_spring) and great-grandparent (if torsion_spring) spans (JointCopies, untouched by
                // this method), which overlaps the root pair only when the root is one of those targets.
                float? RootSuspenderValue(BoneChainJoint joint, int parentNode, int grand, int greatGrand)
                {
                    if (joint.Node == rootNode)
                    {
                        return null;
                    }

                    var rootIsUpwardTarget = rootNode == parentNode
                        || (joint.BendSpring && rootNode == grand)
                        || (joint.TorsionSpring && rootNode == greatGrand);

                    if (rootIsUpwardTarget)
                    {
                        // The root coincides with a target JointCopies already gathers evidence from, so
                        // ITS total already counts any extra_iterations repeats AND a suspender companion
                        // together - split it the same way, against the chain's own natural factor.
                        if (chainNaturalRf is not { } naturalRf)
                        {
                            return null;
                        }

                        var totalCopies = JointCopies(joint);
                        if (totalCopies <= 1 || totalCopies % 2 != 0)
                        {
                            return null;
                        }

                        var baseCopies = totalCopies / 2;
                        float? suspender = null;
                        foreach (var a in Side(joint.Node))
                        {
                            foreach (var b in Side(rootNode))
                            {
                                var pair = a < b ? (a, b) : (b, a);
                                if (!rodRelaxationsByPair.TryGetValue(pair, out var relaxations)
                                    || relaxations.Count != baseCopies * 2
                                    || SplitEvenly(relaxations, naturalRf, baseCopies) is not { } value
                                    || (suspender is { } already && MathF.Abs(already - value) > 1e-4f))
                                {
                                    return null;
                                }

                                suspender = value;
                            }
                        }

                        return suspender;
                    }

                    // The root is not one of extra_iterations' own targets, so a chain with no suspender
                    // has no rod between this joint and the root AT ALL: nothing but a suspender reaches a
                    // joint further from the root than its own great-grandparent span. Every copy of that
                    // rod carries the suspender's own value, so a pair whose rigid rods all agree names it
                    // however many iterations repeated it. Skips a pair SourceSprings already accounts for
                    // as an explicit authored ClothSpring (a rigger's own cross-chain tie - see
                    // GetAuthoredSourceSprings) so the two mechanisms never double-claim the same rod.
                    {
                        float? suspender = null;
                        foreach (var a in Side(joint.Node))
                        {
                            foreach (var b in Side(rootNode))
                            {
                                var pair = a < b ? (a, b) : (b, a);
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

                // The suspender companion on a chain that has NO extra iterations, which
                // <see cref="RootSuspenderValue"/>'s own route cannot see: there the root pair's total is
                // split evenly between repeats and the companion, so it needs an even count and a second
                // relaxation to split on. A chain that merely carries a suspender has every span of the
                // joint at the same count and the span landing on the chain ROOT one higher, and the
                // surplus rod may sit at the chain's own factor, which no split can separate. The count
                // alone identifies it. The root pair is also what breaks JointCopies' uniformity test, so
                // this reads the iteration count off the parent span instead.
                float? RootCompanionValue(BoneChainJoint joint, int parentNode, int grand, int greatGrand)
                {
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

                    float? companion = null;
                    foreach (var a in Side(joint.Node))
                    {
                        foreach (var b in Side(rootTarget))
                        {
                            var pair = a < b ? (a, b) : (b, a);
                            if (Array.IndexOf(SourceSprings, pair) >= 0
                                || Array.IndexOf(SourceSprings, (pair.Item2, pair.Item1)) >= 0
                                || !rigidRodRelaxationsByPair.TryGetValue(pair, out var relaxations)
                                || Surplus(relaxations, baseCopies) is not { } value
                                || (companion is { } already && MathF.Abs(already - value) > 1e-4f))
                            {
                                return null;
                            }

                            companion = value;
                        }
                    }

                    return companion;
                }

                // The one relaxation left on a root pair once the joint's own baseCopies repeats are taken
                // out of it: the pair's single odd value, or its shared value when every rod agrees.
                static float? Surplus(List<float> relaxations, int baseCopies)
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

                    var odd = groups.FindIndex(static g => g.Count == 1);
                    return odd >= 0 && groups[1 - odd].Count == baseCopies ? groups[odd].Value : null;
                }

                // A chain rod compiles with flRelaxationFactor = slider * exp(-default_stretch), so the
                // slider is the compiled factor with the model's own default_stretch scale taken back out.
                var sliderScale = MathF.Exp(-DefaultSurfaceStretch);
                float Slider(float relaxation) => Math.Min(1f, relaxation / sliderScale);

                foreach (var joint in chain.Joints)
                {
                    var parent = joint.ParentNode;
                    var grandParent = parent >= 0 && jointByNode.TryGetValue(parent, out var p1) ? p1.ParentNode : -1;
                    var greatGrandParent = grandParent >= 0 && jointByNode.TryGetValue(grandParent, out var p2) ? p2.ParentNode : -1;

                    var endRing = EndEffectorRing(joint.Node);
                    joint.BendSpring = SpannedByRod(joint.Node, grandParent) || AllSpanned(endRing, parent);
                    joint.TorsionSpring = SpannedByRod(joint.Node, greatGrandParent) || AllSpanned(endRing, grandParent);

                    // A zero stiffness is the compiler's signal to leave the span out entirely, so a span
                    // that exists but reads back at zero keeps the neutral 1.0 rather than switching its
                    // own rod off.
                    float SpringStiffness(int other, int endEffectorOther)
                    {
                        var stiffness = SpanRelaxation(joint.Node, other)
                            ?? RelaxationAcross(endRing, endEffectorOther)
                            ?? chainNaturalRf ?? 1f;
                        return stiffness > 0f ? Slider(stiffness) : 1f;
                    }

                    var stretch = SpanRelaxation(joint.Node, parent) ?? RingInternalRelaxation(joint.Node)
                        ?? chainNaturalRf ?? 1f;
                    joint.StretchStiffness = stretch > 0f ? Slider(stretch) : 1f;

                    // A span the compile records as its own two-corner source element was authored as a
                    // ClothSpring rather than as this joint's stretch slider, and the spring is re-declared
                    // with the rod's own fields. The chain must not build the same span a second time: the
                    // two are created in different passes and never merge, so a zero slider is what removes
                    // the chain's copy.
                    // A compile that wrote NO node base at all had every dynamic node's basis hint pair
                    // filled by its ropes, and a roped node keeps its chain rods for that reason: a chain
                    // left generating nothing loses the rope, the hint pairs go empty and the compiler
                    // then writes a basis per node the original does not carry. The explicit spring is
                    // worth a source element, not that.
                    var ropeHinted = NodeBases.Count == 0
                        ? RopeRunParents
                        : (IReadOnlyDictionary<int, int>)new Dictionary<int, int>();
                    bool AuthoredSpring(int other) => other >= 0
                        && !ropeHinted.ContainsKey(joint.Node) && !ropeHinted.ContainsKey(other)
                        && (Array.IndexOf(SourceSprings, (joint.Node, other)) >= 0
                            || Array.IndexOf(SourceSprings, (other, joint.Node)) >= 0);

                    if (AuthoredSpring(parent))
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

                    if (RootSuspenderValue(joint, parent, grandParent, greatGrandParent) is { } suspender)
                    {
                        joint.Suspender = suspender;
                        var rootIsUpwardTarget = rootNode == parent
                            || (joint.BendSpring && rootNode == grandParent)
                            || (joint.TorsionSpring && rootNode == greatGrandParent);
                        joint.ExtraIterations = rootIsUpwardTarget ? JointCopies(joint) / 2 - 1 : JointCopies(joint) - 1;
                    }
                    else if (RootCompanionValue(joint, parent, grandParent, greatGrandParent) is { } companion)
                    {
                        joint.Suspender = companion;
                        joint.ExtraIterations = SpanCopies(joint, parent) - 1;
                    }
                    else
                    {
                        joint.Suspender = 0f;
                        joint.ExtraIterations = JointCopies(joint) - 1;
                    }
                }

                // An extruding chain ties a position-driven joint through its ring only; a rod straight
                // between that joint node and its parent node comes from a second, plain declaration
                // of the two, which also re-registers both joint nodes.
                foreach (var joint in chain.Joints)
                {
                    if (joint.IsRoot || joint.ProxyNode < 0 || !IsPositionDriven(joint.Node)
                        || !rodPairs.Contains(joint.Node < joint.ParentNode
                            ? (joint.Node, joint.ParentNode)
                            : (joint.ParentNode, joint.Node)))
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

                // A joint's ring can straddle the static boundary, and the joint itself records only its
                // first proxy, so the chain's first simulated node is read off the whole declared ring.
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
            }

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
    }
}
