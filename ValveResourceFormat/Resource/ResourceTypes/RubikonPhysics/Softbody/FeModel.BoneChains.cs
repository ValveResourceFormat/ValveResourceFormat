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
            public float InvMass { get; init; }
            /// <summary>
            /// Gets the number of auto-generated <c>$cc</c> proxy nodes the compiler placed on THIS joint
            /// (its local ribbon width). Usually equal to the chain's <see cref="BoneChain.ExtrudeSides"/>,
            /// but an end-cap joint can fan wider (primal_beast back_chain body 2, tip 4). Used to override
            /// the chain-level extrude per joint so an end-cap fan is not lost to the uniform chain width.
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
            /// <summary>Gets the joints, root first, in pre-order (a parent always precedes its children).</summary>
            public List<BoneChainJoint> Joints { get; } = [];
            /// <summary>
            /// Gets the ribbon width the compiler baked as auto-generated <c>$cc</c> proxy nodes per joint:
            /// 0/1 = a plain 1-wide rope (no extrude), 2+ = an extruded strip/tube (marci BackpackStrapLwr =
            /// 2, phantom_assassin hair = 3). Drives the ClothChain's <c>extrude_sides</c> so the recompile
            /// regenerates the same proxy count instead of halving a 2-wide strip to a 1-wide rope.
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

        // How far apart two proxies must sit along a joint's forward axis to count as separate rings. The
        // compiler ignores an end_effector below 0.05, so anything closer than that is one ring.
        const float EndEffectorRingTolerance = 0.05f;

        // extrude_forward_axis selector quaternions, byte-verified against the compiler's own compiled
        // ring frames: a +90-degree rotation about local Z for 'y' (maps +X to +Y), a -90-degree rotation
        // about local Y for 'z' (maps +X to +Z). 'x' uses Quaternion.Identity. Composed with a joint's own
        // rest rotation (ringFrame = jointRot * axisSelect), this re-labels which local axis is "forward".
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
        // ring geometry. 'x' is preferred whenever it qualifies (not just whichever axis is smallest):
        // a ring reproducible via the default axis needs no explicit authoring, and for a ring whose twist
        // happens to fall on a multiple of 90 degrees, two axes can tie at exactly 0 - genuine positional
        // ambiguity that authoring the non-default axis would not resolve any better, since the same
        // ring is exactly reproducible via 'x' with an adjusted twist. Only a ring that does NOT lie in the
        // default axis' plane at all needs 'y' or 'z'. Tolerance is relative to the ring's own scale so it
        // does not depend on model units.
        //
        // Deliberately NOT extended to look past a single ring at any end_effector second ring: a lone
        // ring's own centroid sits at the joint only when it has 2+ points that cancel out symmetrically
        // (sides>=2); a single-point ring's "centroid" is just that one point, off-center by construction
        // whether or not an end_effector exists, so using centroid displacement as an axis signal false-
        // positives on every plain sides=1 ring (measured: 30+ false positives across hoodwink/kez_base
        // alone). For the genuinely unrecoverable case - a sides=2 ring with no usable second-ring signal -
        // 'x' with a fitted twist reproduces the exact same compiled ring, so defaulting to it here is
        // correct, not merely a fallback.
        const float ExtrudeForwardAxisTolerance = 0.02f;

        static char DetectExtrudeForwardAxis(Vector3 jointPos, Quaternion jointRot, List<int> ring, Vector3[] positions)
        {
            float sumX = 0f, sumY = 0f, sumZ = 0f;
            foreach (var proxy in ring)
            {
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
        /// Reconstructs bone chains from the control-node topology, ignoring auto-generated cloth proxy nodes.
        /// Each chain is rooted at a real bone with no real-bone parent and contains all of its real descendants.
        /// </summary>
        public List<BoneChain> BuildBoneChains()
        {
            var chains = new List<BoneChain>();
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
            // m_SkelParents is indexed in CONTROL-NODE space, so it silently collapses through any
            // intermediate real skeleton bone that never became a control node itself - e.g.
            // meepo_naruto_set's 5 standalone "neck_nodes" bones (each a distant, otherwise-unrelated
            // descendant of "root_0" with nothing in between ever referenced by a cloth construct) all
            // resolve their "real parent" to root_0 directly, reading as one bogus 5-way chain even though
            // they are only connected to EACH OTHER (sparsely) via explicit ClothSpring, never to root_0.
            // Require an actual m_Rods entry between a node and its candidate real parent before trusting
            // the link (verified: root_0 has ZERO rods touching it at all in that model) - a genuine
            // authored chain's own joint-to-joint rods (see AddClothProxySprings remarks: a chain compiles
            // to a fully-connected local rod mesh among its own joints) always include the direct
            // parent-child pair, so this never rejects a real chain link, only a coincidental one.
            var rodPairs = new HashSet<(int, int)>();
            var rodMultiplicity = new Dictionary<(int, int), int>();
            // Every rod's own flRelaxationFactor, by pair - a suspender companion rod can be the same pair
            // as an ordinary one but carries a DIFFERENT relaxation factor (see RootSuspenderValue), which
            // rodMultiplicity alone (count only) cannot distinguish from an extra_iterations repeat.
            var rodRelaxationsByPair = new Dictionary<(int, int), List<float>>();
            foreach (var rod in Rods)
            {
                var pair = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
                rodPairs.Add(pair);
                rodMultiplicity[pair] = rodMultiplicity.GetValueOrDefault(pair) + 1;
                if (!rodRelaxationsByPair.TryGetValue(pair, out var relaxations))
                {
                    relaxations = [];
                    rodRelaxationsByPair[pair] = relaxations;
                }

                relaxations.Add(rod.RelaxationFactor);
            }

            // Each real bone -> the auto-generated "$cc<bone>" proxy nodes parented straight to it (its
            // ribbon width). Restricted to the "$cc" prefix (the ClothChain extrude proxies), NOT every
            // "$"-node: a "$cloth_m" SHEET must not be mistaken for a chain's own width (would disturb
            // meepo's jaket). Used below to keep a ribbon's position-driven TIP joint in the chain, and
            // later to recover each chain's extrude width.
            var proxyChildrenOf = new Dictionary<int, List<int>>();

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

            for (var node = 0; node < n; node++)
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
                    if (!proxyChildrenOf.TryGetValue(pp, out var list))
                    {
                        proxyChildrenOf[pp] = list = [];
                    }

                    list.Add(node);
                }
            }

            var ringOwnerOf = new Dictionary<int, int>();
            foreach (var (owner, ring) in proxyChildrenOf)
            {
                foreach (var vertex in ring)
                {
                    ringOwnerOf[vertex] = owner;
                }
            }

            // Every chain link the compiler surfaces leaves one source element behind, and a joint with no
            // extrude ring of its own contributes its bare node to it: a ringless parent joined to a ringed
            // child is recorded as that parent plus the child's whole ring (hornet's hat_base, once per
            // hat_flap). Where a model records its other chain surfaces but not this one, the parent is not
            // part of the chain and the skeleton link is the artist's bone hierarchy alone - prof_dynamo
            // hangs three coat chains per side off clavicle_L/R, and reading that as one chain has the
            // compiler draw six caps the original has none of.
            //
            // The parent also has to be a control node for a reason of its own, and driving proxy-sheet
            // vertices through the offset network is that reason: a bone the chain alone names leaves the
            // model with the chain, taking its m_SkelParents links and its own node with it. Every ringless
            // parent measured across the Dota corpus whose cap element is missing drives ZERO sheet
            // vertices; prof_dynamo's clavicles drive 20 and 2.
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

                // A $cc-proxied chain (marci's BackpackStrapLwr/GemRibbon/Ponytail/Backpack/SkirtHlp,
                // primal_beast's leg/back/neck) carries its rods among the auto-generated $cc PROXY nodes,
                // never between the real chain bones, so the rod test alone never links it and the chain
                // silently falls through to BuildProxyMeshesFromRodsOnly - where a curved 2-wide ribbon then
                // collapses in the compiler's 2D cloth-mesh import. Link two consecutive position-driven
                // SIMULATED real bones directly instead. The meepo neck_nodes false-chain the rod test guards
                // against (5 distant descendants of a static root_0 that m_SkelParents collapses onto) can't
                // satisfy this: they are STATIC (invMass 0) and their resolved parent root_0 is static too.
                var bothDrivenSim = i >= FirstPositionDrivenNode && p >= FirstPositionDrivenNode
                    && i < NodeInvMasses.Length && NodeInvMasses[i] != 0f
                    && p < NodeInvMasses.Length && NodeInvMasses[p] != 0f;

                // A node that carries its own $cc proxies is unambiguously a ribbon joint, so link it to its
                // real parent cloth node whatever that parent's role: a simulated BODY bone (extends the
                // strip inward), another $cc-proxied ribbon bone (a per-side anchor like back_chain_0), OR a
                // pinned SHARED anchor that carries no proxies of its own (ringmaster's cape_top, from which
                // both cape_L and cape_R hang - dropping it lost 1 node, firstPD 46->45). p is already
                // guaranteed a real cloth node (isReal[p] checked above), so no extra role test is needed;
                // requiring i to be $cc-proxied is itself the guard against the meepo neck_nodes false-chain
                // (those static root_0 descendants carry NO proxies, so this never links them).
                var proxyRibbon = proxyChildrenOf.ContainsKey(i);

                // A bone the compiler built a hinge anchor for is a hinged chain's root by construction,
                // so its real children belong to that chain however few traces they leave of their own. The
                // hinge puts the whole ribbon's proxies on the ROOT, which is what makes the three tests
                // above miss these chains entirely (legion_commander's earrings, tinker's cosmic back).
                var hingedRoot = Array.IndexOf(CtrlNames, HingeAnchorPrefix + CtrlNames[p]) >= 0;

                // A joint whose parent carries a stiff hinge is joined to it by the bend rather than by a
                // rod, so the rod test alone drops it and the chain ends one joint short (snotty_survivors'
                // snot_L_02, whose only rod reaches its grandparent).
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
                // a joint short (gigawatt's sleeve_cloth_R_3 rods to sleeve_cloth_R_1, ribPipe_R_7 to
                // ribPipe_R_5). A grandparent needs a real parent of its own, so the static-root false chain
                // the rod test guards against cannot reach this: its resolved parent is a root.
                var grandParent = p < SkelParents.Length ? SkelParents[p] : -1;
                var bendRodLinked = grandParent >= 0 && grandParent < n && isReal[grandParent]
                    && rodPairs.Contains(grandParent < i ? (grandParent, i) : (i, grandParent));

                // Where the parent extrudes, the joint's rod lands on the parent's ring instead of on the
                // parent itself (gigawatt's joint51, whose only rods reach $ccjoint50_0/1).
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
            // whose top two joints are both static loses the one link every rule above reads and the chain
            // reconstructs rooted a joint too low - the parent joint keeps only its own compiled
            // m_SkelParents entry (abrams' chain_0 under cuff_attach, inferno's dishcloth_front_0 under
            // dishcloth_center_0, both a joint_parent in the authored source). Take that entry, but only
            // where it names the node's DIRECT skeleton parent: the control-space collapse invents distant
            // ancestors (meepo_naruto_set's neck_nodes on root_0), which is what the rod test guards
            // against. A bone the SHEET skins to is excluded on both ends - its compiled parent comes from
            // the skin hierarchy rather than from a chain, and reading it as a link merges every one of
            // abrams' coat chains into a single chain under coat_main.
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
                // child on a strap anchored at both ends (sniper_calavera's strap_front, pinned at joints
                // 01 AND 07 - picking the wrong end hangs joints backwards off the far anchor and the
                // compiler access-violates on the resulting chain). Only a rod-evidenced pair is linked.
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
                        while (ancestor is not null)
                        {
                            if (nodeByName.TryGetValue(ancestor, out var p) && p != i)
                            {
                                var key = p > i ? (i, p) : (p, i);
                                if (linkCounts.ContainsKey(key))
                                {
                                    realParent[i] = p;
                                }

                                break;
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
                    if (realParent[child] < 0 && realParent[link.Parent] != child)
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
            // which extruded it - hornet's hat_base rods to the feather joints through $cchat_base_*
            // and to the hat_top/hat_flap joints directly. Merging them gives every child the ring
            // anchor and widens the compiler's own fit matrices over the merged sibling set.
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
            // parent therefore enumerates that joint's real siblings, and a sibling our chain has but the
            // fit does not was authored elsewhere (hornet's hat_top_0 is absent from every hat_flap fit).
            // A fit that does NOT name the parent is taken over some other neighbourhood and says nothing
            // about siblings, so it never splits.
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

            var chainSpecs = new List<(int Root, HashSet<int>? RootChildren, bool RinglessRoot)>();
            foreach (var rootNode in roots)
            {
                // A real bone with no real descendants is not a cloth chain, unless it carries its own
                // extrude ring - a lone ring-bearing bone is a single-joint chain (fv_cosmic_weapon's
                // weapon_ball with its $cc..._Ctr node, flagbearer's collar).
                if (children[rootNode] is not { } rootKids)
                {
                    if (!proxyChildrenOf.ContainsKey(rootNode))
                    {
                        continue;
                    }

                    chainSpecs.Add((rootNode, null, false));
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
                    chainSpecs.Add((rootNode, null, false));
                }
                else
                {
                    foreach (var (kids, ringless) in groups)
                    {
                        chainSpecs.Add((rootNode, [.. kids], ringless));
                    }
                }
            }

            foreach (var (rootNode, rootChildren, ringlessRoot) in chainSpecs)
            {
                var chain = new BoneChain { RootBone = CtrlNames[rootNode] };

                void Visit(int node)
                {
                    var parent = realParent[node];
                    chain.Joints.Add(new BoneChainJoint
                    {
                        Node = node,
                        Name = CtrlNames[node],
                        ParentNode = parent,
                        ParentName = parent >= 0 ? CtrlNames[parent] : null,
                        InvMass = node < NodeInvMasses.Length ? NodeInvMasses[node] : 0f,
                    });

                    if (children[node] is { } kids)
                    {
                        kids.Sort();
                        foreach (var child in kids)
                        {
                            if (node == rootNode && rootChildren is not null && !rootChildren.Contains(child))
                            {
                                continue;
                            }

                            Visit(child);
                        }
                    }
                }

                Visit(rootNode);

                // Recover the ribbon width the compiler baked into $cc proxy nodes: how many it placed per
                // joint (extrude_sides) and their mean offset (extrude_radius). Without this a ClothChain
                // regenerates only ONE proxy per joint, halving a 2-wide strip (marci BackpackStrapLwr,
                // primal_beast leg_chain) to a 1-wide rope.
                //
                // extrude_sides forces EVERY joint to the same width, so it reproduces a UNIFORM strip
                // exactly (leg_chain [2,2,2,2,2,2] -> extrude 2 -> identical) but cannot reproduce a ribbon
                // whose END-CAP joint fans wider than its body (back_chain [2,2,2,4], hoodwink tail
                // [2,2,2,2,2,2,4]). Use the MODE (the width most joints share = the true body width), NOT the
                // max: picking the tip's 4 would re-extrude the whole body 4-wide ([4,4,4,4]), inflating the
                // node count and distorting the shape (hoodwink went +52). The mode reproduces the body
                // exactly and just drops the unreproducible tip fan (a few nodes). 0/1 stays a plain rope (no
                // extrude) so genuine 1-wide chains - meepo/dark_willow/legion - are byte-identical as before.
                var sideFrequency = new Dictionary<int, int>();
                var radii = new List<float>();
                var twists = new List<float>();
                foreach (var joint in chain.Joints)
                {
                    if (ringlessRoot && joint.Node == rootNode)
                    {
                        continue;
                    }

                    if (!proxyChildrenOf.TryGetValue(joint.Node, out var proxies) || proxies.Count == 0)
                    {
                        continue;
                    }

                    // A "$cc<bone>_Ctr" proxy is the single centre node the compiler emits for an
                    // end_effector with extrude_sides < 2 - it is not a ring member at all. A joint whose
                    // proxies are ALL centre nodes has no side ring: recover end_effector as the centre's
                    // forward displacement and leave the ring empty (fv_cosmic_weapon's weapon_ball).
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
                            sideFrequency[0] = sideFrequency.GetValueOrDefault(0) + 1;
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
                            // The joint's OWN ring sits at forward ~= 0 (it is centred on the joint); an
                            // end_effector ring is displaced away from that by a signed amount that can go
                            // either way along forward, so the near ring is whichever cluster sits closest
                            // to 0, not whichever has the smaller raw (signed) value - comparing raw
                            // Min()/Max() mislabels the two when the displacement is negative.
                            var minAbs = forwardOf.Values.Min(MathF.Abs);
                            var maxAbs = forwardOf.Values.Max(MathF.Abs);
                            if (maxAbs - minAbs > EndEffectorRingTolerance)
                            {
                                var nearRing = proxies.Where(p => MathF.Abs(forwardOf[p]) - minAbs <= EndEffectorRingTolerance).ToList();
                                if (nearRing.Count > 0 && nearRing.Count < proxies.Count)
                                {
                                    // Same single-reference-value shape as before (not an average across
                                    // the cluster): the near ring's own smallest-magnitude member and the
                                    // far ring's own largest-magnitude member, which is exactly what the old
                                    // global Min()/Max() picked out on the already-correct (positive
                                    // displacement) case - so that case stays byte-identical.
                                    var farRing = proxies.Except(nearRing).ToList();
                                    var nearValue = forwardOf[nearRing.MinBy(p => MathF.Abs(forwardOf[p]))];
                                    var farValue = forwardOf[farRing.MaxBy(p => MathF.Abs(forwardOf[p]))];
                                    joint.EndEffector = farValue - nearValue;
                                    ring = nearRing;
                                    endEffectorRing = farRing;
                                }
                            }
                        }
                    }

                    joint.ExtrudeSides = Math.Min(ring.Count, 4);
                    joint.ProxyNode = ring[0];
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

                // Extrude whenever the body carries proxies at all (>= 1), not only 2-wide strips. A 1-wide
                // body (hoodwink face_tuft/cape_back, one $cc proxy per joint) is NOT the same as a genuine
                // 0-proxy rope (meepo/dark_willow): dropping its extrude would leave the joints as bare chain
                // nodes and lose that per-joint proxy (hoodwink lost ~24 nodes this way). A 0-width body
                // (empty sideFrequency -> bodySides 0) still gets no extrude, keeping those models byte-exact.
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
                // in for the joint wherever it has one.
                List<int> Side(int end)
                {
                    if (ringlessRoot && end == rootNode)
                    {
                        return [end];
                    }

                    return proxyChildrenOf.TryGetValue(end, out var ring) && ring.Count > 0 ? ring : [end];
                }

                // THIS chain's own natural, non-suspendered relaxation factor - what every UNDOUBLED rod
                // (a pair with exactly one recorded copy) among the chain's own nodes carries. A chain-
                // level modifier (e.g. a non-default stretch_spring) can move this off the compiler's
                // 1.0 default uniformly (axe_dressed_to_cull_head's "beard" chain: every undoubled rod
                // carries 0.9), so a hardcoded 1.0 reference is wrong there. Null when the chain's
                // undoubled rods disagree, or when it has none - no reliable reference, so
                // RootSuspenderValue keeps today's extra_iterations-only reading rather than guess.
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

                    // The spring spans the two sides in FULL: anything short of that is some other construct
                    // passing between them - a surface the sheet rebuilds, say - and turning the spring on to
                    // claim it would add every pair it does not have.
                    foreach (var a in Side(node))
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

                // An extra solver iteration repeats the rods a joint generates upward - the span to its
                // parent plus its bend and torsion spans - and repeats them ALL, uniformly. Each span is
                // read across both ends in full (an extruding joint carries it on its ring), and the whole
                // ring-to-ring set has to be present: an end cap that fans wider than its parent reaches
                // only part of the tip ring, so its crossing pairs are doubled by geometry while the set
                // stays incomplete (baron_of_the_minotaur_belt: 4 of 8). Disagreement or a gap means no
                // iteration.
                //
                // Two things are deliberately NOT evidence. A joint's own ring edge tracks the ring's
                // shape, not the iteration count (wandering_poet_mount repeats its spans twice while that
                // edge stays single). And a span running DOWN to a deeper joint belongs to that joint's
                // count, not this one's (inferno_v4's flame_hair_2 sits at x2 while the span it shares
                // with its child flame_hair_4 is x1).
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
                                var count = rodMultiplicity.GetValueOrDefault(a < b ? (a, b) : (b, a));
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
                // flat, hardcoded 1.0 on every copy an extra_iterations repeat adds, REGARDLESS of the
                // chain's own natural factor - so on a chain whose natural factor is something else, a
                // lone 1.0 copy is that iteration artifact, not an authored suspender (verified directly:
                // setting suspender explicitly on spring2021_bristleback_paganism_pope_golem's
                // skirt_back_2, whose chain-natural factor is 0.8, produced WRONG topology entirely - see
                // RootSuspenderValue's own remarks). Where the natural factor is itself something other
                // than 1.0 (axe_dressed_to_cull_head's "beard" chain: natural 0.9), a companion at 1.0
                // would be equally ambiguous and is rejected the same way; only a candidate at some THIRD
                // value, matching neither the natural factor nor 1.0, is unambiguous either way.
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
                // Probe-verified on two independent multi-joint chains (w14_suspender.md): brewmaster's
                // 2-joint probe, where root and parent coincide, and a controlled compile of
                // axe_dressed_to_cull_head's real 3-joint "beard" chain (root beard_0, then beard_1, then
                // beard_end) with suspender authored on the LEAF joint alone, which put the companion on
                // (beard_end, beard_0) - two hops past its own parent beard_1 - never on
                // (beard_end, beard_1). extra_iterations is unrelated: it repeats a joint's own parent,
                // grandparent (if bend_spring) and great-grandparent (if torsion_spring) spans
                // (JointCopies, untouched by this method), which only overlaps the root pair when the
                // root itself happens to be one of those targets.
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

                    // The root is not one of extra_iterations' own targets, so in a chain with no
                    // suspender there is no rod between this joint and the root AT ALL - a uniform,
                    // brand-new single rod there, shared across the whole joint-ring/root-ring crossing,
                    // is unambiguous suspender evidence with no pre-existing "natural" copy to guard
                    // against. Skips a pair SourceSprings already accounts for as an explicit authored
                    // ClothSpring (a rigger's own cross-chain tie - see GetAuthoredSourceSprings) so the
                    // two mechanisms never double-claim the same rod.
                    {
                        float? suspender = null;
                        foreach (var a in Side(joint.Node))
                        {
                            foreach (var b in Side(rootNode))
                            {
                                var pair = a < b ? (a, b) : (b, a);
                                if (Array.IndexOf(SourceSprings, pair) >= 0
                                    || Array.IndexOf(SourceSprings, (pair.Item2, pair.Item1)) >= 0
                                    || !rodRelaxationsByPair.TryGetValue(pair, out var relaxations)
                                    || relaxations.Count != 1
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

                foreach (var joint in chain.Joints)
                {
                    var parent = joint.ParentNode;
                    var grandParent = parent >= 0 && jointByNode.TryGetValue(parent, out var p1) ? p1.ParentNode : -1;
                    var greatGrandParent = grandParent >= 0 && jointByNode.TryGetValue(grandParent, out var p2) ? p2.ParentNode : -1;

                    joint.BendSpring = SpannedByRod(joint.Node, grandParent);
                    joint.TorsionSpring = SpannedByRod(joint.Node, greatGrandParent);

                    if (RootSuspenderValue(joint, parent, grandParent, greatGrandParent) is { } suspender)
                    {
                        joint.Suspender = suspender;
                        var rootIsUpwardTarget = rootNode == parent
                            || (joint.BendSpring && rootNode == grandParent)
                            || (joint.TorsionSpring && rootNode == greatGrandParent);
                        joint.ExtraIterations = rootIsUpwardTarget ? JointCopies(joint) / 2 - 1 : JointCopies(joint) - 1;
                    }
                    else
                    {
                        joint.Suspender = 0f;
                        joint.ExtraIterations = JointCopies(joint) - 1;
                    }
                }

                SteerNodeBaseTies(chain);
                chains.Add(chain);
            }

            return chains;
        }
    }
}
