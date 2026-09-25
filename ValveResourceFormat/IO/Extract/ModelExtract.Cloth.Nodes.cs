using System.Globalization;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
    // A "$cloth_node_<name>" control node is an authored free-standing ClothNode: the compiler names the
    // ctrl "$cloth_node_" + the element name, anchors it to cloth_node_root_bone via an m_CtrlOffsets
    // entry holding the authored bone-local origin, and registers the root bone as a second ctrl of its
    // own. A ClothNode whose name equals its root bone merges into ONE ctrl carrying the plain bone name
    // (static when is_static_node), which is how a plain cloth bone that no chain, proxy or shape claims
    // was authored. The rods among these nodes come from explicit ClothSprings, whose endpoints resolve
    // by ClothNode element name (or plain bone name for a merged/root ClothNode); a bone with no cloth
    // declaration of its own is not a valid endpoint ("Cannot find Fx Bone"). A rod from a node declared here to a
    // joint of chainJoints is such a spring as well: no chain and no chain-ring source spring re-declares it.
    internal static int AddFreeClothNodesAndSprings(KVObject clothChildren, KVObject softbodyChildren,
        FeModel feModel, HashSet<int> coveredNodes, Func<string, bool> emitBareStatic,
        HashSet<string> clothBones, Func<int, bool, KVObject>? folderFor = null, bool hasOtherChains = false,
        Func<string, bool>? bareStaticReparented = null, HashSet<(int, int)>? alreadyEmitted = null,
        HashSet<int>? chainJoints = null)
    {
        const string ClothNodePrefix = "$cloth_node_";
        var names = feModel.CtrlNames;

        var anchorOf = BuildCtrlAnchorMap(feModel);

        var nodeByName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var node = 0; node < names.Length; node++)
        {
            nodeByName.TryAdd(names[node], node);
        }

        var jiggleNodes = feModel.JiggleBones.Select(static j => j.Node).ToHashSet();
        var shapeParentBones = CollisionShapeParentBones(feModel);

        var rodTouched = new HashSet<int>();
        foreach (var rod in feModel.Rods)
        {
            if (rod.NodeA != rod.NodeB)
            {
                rodTouched.Add(rod.NodeA);
                rodTouched.Add(rod.NodeB);
            }
        }

        // node -> the name a ClothSpring endpoint references it by.
        var springName = new Dictionary<int, string>();
        var declared = new HashSet<int>();
        var emitted = 0;

        // A ClothNode carries no vertex_map of its own, so a lone one joins its selections through the
        // ClothVertexMap containers. A node a ClothSpring names is listed by them but left flat.
        KVObject FolderOf(int node)
            => folderFor is not null ? folderFor(node, !rodTouched.Contains(node)) : clothChildren;

        for (var node = 0; node < names.Length; node++)
        {
            var name = names[node];
            if (coveredNodes.Contains(node) || jiggleNodes.Contains(node) || shapeParentBones.Contains(name))
            {
                continue;
            }

            if (name.StartsWith(ClothNodePrefix, StringComparison.Ordinal))
            {
                var elementName = name[ClothNodePrefix.Length..];
                if (!TryResolveClothNodeAnchor(feModel, anchorOf, node, out var rootBone, out var origin, out var angles))
                {
                    continue;
                }

                FolderOf(node).Add(MakeClothNode(feModel, rootBone, node,
                    isStaticNode: feModel.IsStatic(node), elementName: elementName, origin: origin, angles: angles));
                springName[node] = elementName;
                declared.Add(node);
                clothBones.Add(rootBone);
                emitted++;

                // The root bone compiles into a registered ctrl of its own, referencable by plain name.
                if (nodeByName.TryGetValue(rootBone, out var rootNode))
                {
                    springName.TryAdd(rootNode, rootBone);
                }
            }
            else if (!feModel.IsGeneratedNodeName(name))
            {
                var isStatic = feModel.IsStatic(node);
                var bareStatic = isStatic && !rodTouched.Contains(node);
                if (!isStatic || !bareStatic || emitBareStatic(name))
                {
                    // A static node a rod names is an anchor the spring network already ties in, and a
                    // static node with no control-node ancestor compiles to a hierarchy root from a
                    // merged ClothNode already. Only a BARE, re-parented one needs the chain form.
                    var loneNode = LoneNodeIsJointChain(feModel, node, bareStatic, bareStaticReparented?.Invoke(name) ?? false)
                        && !(StrayRecordOnlyAClothNodeStates(feModel, node) && bareStaticReparented?.Invoke(name) == false);
                    (loneNode ? clothChildren : FolderOf(node)).Add(loneNode
                        ? MakeLoneJointChain(feModel, name, node, hasOtherChains)
                        : MakeClothNode(feModel, name, node, isStaticNode: isStatic));
                    springName[node] = name;
                    declared.Add(node);
                    clothBones.Add(name);
                    emitted++;
                }
            }
        }

        if (springName.Count == 0)
        {
            return emitted;
        }

        bool IsEndpoint(int node, int other) => springName.ContainsKey(node)
            || (chainJoints is not null && chainJoints.Contains(node) && declared.Contains(other));

        // One spring per rod OCCURRENCE, not per distinct pair: node mass accumulates per rod, and a model
        // can ship genuine duplicate rods. Where EVERY occurrence of a pair is an identical copy, one
        // ClothSpring's own extra_iterations reproduces them: the compiler duplicates a spring's rod once
        // per iteration, so N identical copies come from one authored spring declaration and leave one
        // m_SourceElems entry, where N separate springs would leave N. A pair whose copies are not all
        // identical keeps the per-occurrence numbering.
        var rodsByEdge = new Dictionary<(int, int), List<FeModel.Rod>>();
        foreach (var rod in feModel.Rods)
        {
            if (rod.NodeA == rod.NodeB || !IsEndpoint(rod.NodeA, rod.NodeB) || !IsEndpoint(rod.NodeB, rod.NodeA))
            {
                continue;
            }

            var edge = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (!rodsByEdge.TryGetValue(edge, out var list))
            {
                rodsByEdge[edge] = list = [];
            }

            list.Add(rod);
        }

        foreach (var (edge, rods) in rodsByEdge)
        {
            // A pair an explicit source spring already re-declared is spent: emitting it here as well
            // ships the same constraint twice and records a second source element for it.
            if (alreadyEmitted is not null && alreadyEmitted.Contains(edge))
            {
                continue;
            }

            var name0 = springName.GetValueOrDefault(edge.Item1) ?? names[edge.Item1];
            var name1 = springName.GetValueOrDefault(edge.Item2) ?? names[edge.Item2];

            // The source element keeps the two corners in the order the spring named them, which for a
            // spring reaching a chain joint is not the rod's own node order.
            if ((!declared.Contains(edge.Item1) || !declared.Contains(edge.Item2))
                && Array.IndexOf(feModel.SourceSprings, (edge.Item2, edge.Item1)) >= 0
                && Array.IndexOf(feModel.SourceSprings, (edge.Item1, edge.Item2)) < 0)
            {
                (name0, name1) = (name1, name0);
            }

            var first = rods[0];

            if (IsUnrecordedJointTie(feModel, edge, rods, springName))
            {
                var memberStiffness = MathF.Sqrt(first.RelaxationFactor);
                softbodyChildren.Add(MakeClothSelfCollisionCluster($"cluster_{edge.Item1}_{edge.Item2}", [name0, name1],
                    first.MaxDist / 2f, first.MaxDist / 2f, [memberStiffness, memberStiffness]));
                continue;
            }
            var allIdentical = rods.TrueForAll(rod => rod.MinDist == first.MinDist
                && rod.MaxDist == first.MaxDist && rod.RelaxationFactor == first.RelaxationFactor);

            if (rods.Count > 1 && allIdentical)
            {
                softbodyChildren.Add(MakeClothSpring($"rod_{edge.Item1}_{edge.Item2}", name0, name1,
                    first.MinDist, first.MaxDist, first.RelaxationFactor, extraIterations: rods.Count - 1));
                continue;
            }

            for (var copy = 0; copy < rods.Count; copy++)
            {
                var rod = rods[copy];
                var springLabel = copy == 0 ? $"rod_{edge.Item1}_{edge.Item2}" : $"rod_{edge.Item1}_{edge.Item2}_{copy}";
                softbodyChildren.Add(MakeClothSpring(springLabel, name0, name1, rod.MinDist, rod.MaxDist,
                    rod.RelaxationFactor));
            }
        }

        return emitted;
    }

    /// <summary>
    /// Whether the tie on <paramref name="edge"/> between a declared node and a chain joint is one rigid rod the original
    /// records no two-corner source element for. A <c>ClothSpring</c> registers that element and the element joins its
    /// nodes' neighbour sets, which grades a basis hint the original left ungraded; a two-member
    /// <c>ClothSelfCollisionCluster</c> compiles the same rod, its relaxation the product of the two member stiffnesses,
    /// and registers nothing.
    /// </summary>
    internal static bool IsUnrecordedJointTie(FeModel feModel, (int A, int B) edge, List<FeModel.Rod> rods,
        Dictionary<int, string> springName)
    {
        if (springName.ContainsKey(edge.A) && springName.ContainsKey(edge.B))
        {
            return false;
        }

        if (rods.Count != 1 || Array.IndexOf(feModel.SourceSprings, (edge.A, edge.B)) >= 0
            || Array.IndexOf(feModel.SourceSprings, (edge.B, edge.A)) >= 0)
        {
            return false;
        }

        var rod = rods[0];
        return MathF.Abs(rod.MinDist - rod.MaxDist) <= 1e-4f * MathF.Max(1f, MathF.Abs(rod.MaxDist));
    }

    /// <summary>
    /// Whether a lone real bone must be re-authored as a single-joint <c>ClothChain</c> instead of a
    /// merged <c>ClothNode</c>: the original records it as an <c>m_SkelParents</c> ROOT, which a chain
    /// root compiles back to while a merged ClothNode is re-parented onto its nearest control-node
    /// ancestor. The single-joint chain compiles an otherwise identical node.
    /// </summary>
    /// <summary>
    /// Builds the test for whether a bone has an ancestor that is itself a cloth control node. That
    /// ancestor is what the compiler re-parents a merged <c>ClothNode</c> declaration onto, so a bone
    /// with none already compiles to an <c>m_SkelParents</c> root without a chain declaration.
    /// </summary>
    Func<string, bool> ClothControlAncestorTest(FeModel feModel)
    {
        var controlNames = new HashSet<string>(feModel.CtrlNames, StringComparer.Ordinal);
        var boneByName = model?.Skeleton.Bones.ToDictionary(static b => b.Name, StringComparer.Ordinal);

        return name =>
        {
            if (boneByName is null || !boneByName.TryGetValue(name, out var bone))
            {
                return false;
            }

            for (var ancestor = bone.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (controlNames.Contains(ancestor.Name))
                {
                    return true;
                }
            }

            return false;
        };
    }

    /// <summary>
    /// Whether a bone's own PARENT bone is a cloth control node. A declared cloth node is claimed by
    /// its immediate parent alone, not by any control node further up the skeleton.
    /// </summary>
    Func<string, bool> ClothControlParentTest(FeModel feModel)
    {
        var controlNames = new HashSet<string>(feModel.CtrlNames, StringComparer.Ordinal);
        var boneByName = model?.Skeleton.Bones.ToDictionary(static b => b.Name, StringComparer.Ordinal);

        return name => boneByName is not null && boneByName.TryGetValue(name, out var bone)
            && bone.Parent is not null && controlNames.Contains(bone.Parent.Name);
    }

    /// <summary>
    /// Gets whether a node's stray radius record is one only a <c>ClothNode</c> can state. A chain joint writes its relaxation as
    /// <c>1 - stray_radius_stretchiness</c> and a stretchiness at or above <see cref="ChainStrayStretchinessLimit"/> cancels the
    /// radius, so a record relaxed to zero vanishes from a one-joint chain while a <c>ClothNode</c> carries the factor verbatim.
    /// </summary>
    internal static bool StrayRecordOnlyAClothNodeStates(FeModel feModel, int node)
        => feModel.GetStrayRadius(node) > 0f && feModel.GetStrayStretchiness(node) >= ChainStrayStretchinessLimit;

    const float ChainStrayStretchinessLimit = 0.99999988f;

    static bool LoneClothNodeIsOriginalRoot(FeModel feModel, int node)
        => feModel.HasCompiledSkelParents
            && node < feModel.SkelParents.Length && feModel.SkelParents[node] < 0;

    /// <summary>
    /// Whether a lone real cloth node is re-declared as a single-joint <c>ClothChain</c> rather than a merged
    /// <c>ClothNode</c>. The original must record it as an <c>m_SkelParents</c> root, and it must be dynamic, a bare static
    /// node a merged ClothNode would re-parent, or a static node the original locks to its goal: only a chain joint compiles
    /// into <c>m_LockToGoal</c>, a static ClothNode never does. A stray record only a ClothNode can state
    /// (<see cref="StrayRecordOnlyAClothNodeStates"/>) overrides all three at the call site, because a chain cannot state that
    /// record at all.
    /// </summary>
    /// <param name="feModel">The compiled cloth.</param>
    /// <param name="node">The lone node.</param>
    /// <param name="bareStatic">Whether the node is static and no rod names it.</param>
    /// <param name="bareStaticReparented">Whether a merged ClothNode on this bone would be re-parented onto a control-node ancestor.</param>
    internal static bool LoneNodeIsJointChain(FeModel feModel, int node, bool bareStatic, bool bareStaticReparented)
        => LoneClothNodeIsOriginalRoot(feModel, node)
            && (!feModel.IsStatic(node) || (bareStatic && bareStaticReparented) || feModel.IsLockedToGoal(node));

    static KVObject MakeLoneJointChain(FeModel feModel, string name, int node, bool hasOtherChains)
    {
        var chain = new FeModel.BoneChain { RootBone = name };
        chain.Joints.Add(new FeModel.BoneChainJoint
        {
            Node = node,
            Name = name,
            ParentNode = -1,
            InvMass = node < feModel.NodeInvMasses.Length ? feModel.NodeInvMasses[node] : 0f,
        });
        return MakeClothChainNode(feModel, chain, hasOtherChains);
    }

    // Emits a standalone ClothNode for a simulated real bone that is NOT part of any multi-joint
    // BoneChain and NOT back-solved by a proxy mesh: individual tie points connected only by explicit
    // ClothSpring, since a real bone with no real-bone descendants of its own never forms a BoneChain
    // (see BuildBoneChains). Mirrors MakeClothJoint's integrator recovery, which is what keeps the bone's
    // per-node cloth paint off the compiler defaults; its rods round-trip through AddClothProxySprings
    // either way, a plain skeleton bone name being a valid ClothSpring endpoint on its own.
    //
    // node_base_x0/x1/y0/y1 are read straight out of feModel.NodeBases and re-declared by NAME. A node
    // left without them registers as position-driven and is driven through a synthesized m_Ropes fallback
    // rather than simulated.
    /// <summary>
    /// The <c>transform_alignment</c> a <c>ClothNode</c> on an <c>m_Ropes</c> run compiled from. Alignment 0 gives the node
    /// class byte 1, which the rope pass excludes; 1 gives 2 and 2 gives 3, and only 3 lets a virtual element node start a
    /// run. A bone node takes 1 unless the original bases it, since class 2 is what keeps the bulk node-base pass off it.
    /// </summary>
    internal static int RopeClothNodeAlignment(FeModel feModel, int node, bool isElement, bool hasBasis)
    {
        if (!feModel.IsRopeNode(node))
        {
            return 0;
        }

        return isElement || hasBasis ? 2 : 1;
    }

    internal static KVObject MakeClothNode(FeModel feModel, string boneName, int node, bool isStaticNode = false,
        string? elementName = null, Vector3 origin = default, Vector3 angles = default,
        IReadOnlyDictionary<int, string>? proxyNodeNames = null)
    {
        var integrator = feModel.GetIntegrator(node);
        var goalStrength = feModel.GoalStrengthPaint(integrator.ForceAttraction);
        var goalDamping = feModel.GoalDampingPaint(integrator.ForceAttraction, integrator.VertexAttraction);
        var strayRadius = feModel.GetStrayRadius(node);

        // A basis reference names a node in the AUTHORED namespace, which is not the ctrl namespace: a
        // proxy vertex takes the name our own proxy split gives it and a free cloth node is declared under
        // its element name with the "$cloth_node_" prefix stripped, so echoing the ctrl name leaves a
        // reference that resolves to nothing and the compiler recomputes the basis instead.
        var hasBasis = feModel.NodeBases.TryGetValue(node, out var basis);
        string BasisName(int basisNode)
        {
            if (!hasBasis || basisNode < 0 || basisNode >= feModel.CtrlNames.Length)
            {
                return string.Empty;
            }

            return ResolveAntiTunnelNodeName(feModel, basisNode, proxyNodeNames) ?? string.Empty;
        }

        // The default alignment leaves a free cloth node, and a rotation-locked static node, with no basis at
        // all: the neighbour scan grades neither. On a node the scan can already serve an alignment changes the
        // frame instead, so one is written only where the original has a basis the default drops.
        var preset = elementName is not null || (isStaticNode && !feModel.AllowsRotation(node))
            ? feModel.ClothNodeBasisPreset(node)
            : null;
        var references = preset?.References ?? basis;

        var layers = ClothNodeCollisionLayers(feModel.GetNodeCollisionMask(node));

        return MakeNode("ClothNode",
            ("name", elementName ?? boneName),
            ("origin", ToKVArray(origin)),
            ("angles", ToKVArray(angles)),
            ("cloth_node_root_bone", boneName),
            ("has_stray_radius", strayRadius > 0f),
            ("has_world_collision", feModel.IsWorldCollisionNode(node)),
            ("cloth_collision_layer0", layers.Layer0),
            ("cloth_collision_layer1", layers.Layer1),
            ("cloth_collision_layer2", layers.Layer2),
            ("cloth_collision_layer3", layers.Layer3),
            ("transform_alignment", preset?.TransformAlignment ?? RopeClothNodeAlignment(feModel, node, elementName is not null, hasBasis)),
            ("node_base_y1", BasisName(references.NodeY1)),
            ("node_base_x1", BasisName(references.NodeX1)),
            ("node_base_y0", BasisName(references.NodeY0)),
            ("node_base_x0", BasisName(references.NodeX0)),
            ("lock_translation", feModel.LocksTranslation(node)),
            ("gravity_z", integrator.Gravity / ClothSourceBaseGravity),
            ("goal_strength", goalStrength),
            ("goal_damping", goalDamping),
            ("mass", feModel.RecoverMassMultiplier(node) ?? 1.0f),
            ("friction", feModel.GetNodeFriction(node)),
            ("stray_radius", strayRadius),
            ("stray_radius_relaxation_factor", feModel.GetStrayRelaxationFactor(node)),
            ("collision_radius", feModel.GetCollisionRadius(node)),
            ("is_static_node", isStaticNode),
            ("allow_rotation", feModel.AllowsRotation(node)),
            ("super_damping", Math.Clamp(integrator.PointDamping / ClothDragPointDampingScale, 0f, 1f)));
    }

    /// <summary>
    /// The four <c>cloth_collision_layer</c> booleans a <c>ClothNode</c> declares to compile to
    /// <paramref name="mask"/>. All four set is special-cased by the compiler to the all-layers default
    /// rather than to 15, so it is what a node with the default mask declares, and a mask the four bits
    /// cannot spell out - 15 itself, or anything above them - falls back to the same default.
    /// </summary>
    static (bool Layer0, bool Layer1, bool Layer2, bool Layer3) ClothNodeCollisionLayers(int mask)
    {
        var bits = mask is >= 0 and <= 14 ? mask : 0xF;
        return ((bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0, (bits & 8) != 0);
    }

    /// <summary>
    /// The name a <c>ClothTri</c> / <c>ClothQuad</c> corner references a control node by: the element
    /// name for a free <c>ClothNode</c> (the compiler prefixes <c>$cloth_node_</c> to it itself) and the
    /// plain bone name for everything else.
    /// </summary>
    static string ClothFaceCornerName(FeModel feModel, int node)
    {
        var name = feModel.CtrlNames[node];
        return name.StartsWith(FeModel.FreeClothNodePrefix, StringComparison.Ordinal)
            ? name[FeModel.FreeClothNodePrefix.Length..]
            : name;
    }

    /// <summary>
    /// Declares a <c>ClothStiffHinge</c> for every compiled bend whose three nodes are free cloth nodes. The class names
    /// its hinge and its two base nodes by element name, and the compiler measures both bases from the hinge:
    /// <c>9 * flHeight0^2 = |b1|^2 + |b2|^2 - 2 |b1| |b2| cos(max_angle)</c>. A bend over chain joints comes back as the
    /// joint's <c>stiff_hinge</c>, and a sheet hub's as the sheet's curvature.
    /// </summary>
    internal static void AddClothStiffHinges(KVObject softbodyChildren, FeModel feModel)
    {
        bool IsFreeNode(int node) => node >= 0 && node < feModel.CtrlNames.Length && node < feModel.InitPosePositions.Length
            && feModel.CtrlNames[node].StartsWith(FeModel.FreeClothNodePrefix, StringComparison.Ordinal);

        foreach (var bend in feModel.KelagerBends)
        {
            if (!IsFreeNode(bend.MidNode) || !IsFreeNode(bend.End0) || !IsFreeNode(bend.End1))
            {
                continue;
            }

            var hinge = feModel.InitPosePositions[bend.MidNode];
            var base1 = (feModel.InitPosePositions[bend.End0] - hinge).Length();
            var base2 = (feModel.InitPosePositions[bend.End1] - hinge).Length();
            if (base1 <= 0f || base2 <= 0f)
            {
                continue;
            }

            var cosine = ((base1 * base1) + (base2 * base2) - (9f * bend.Height * bend.Height)) / (2f * base1 * base2);
            softbodyChildren.Add(MakeNode("ClothStiffHinge",
                ("cloth_node_0", ClothFaceCornerName(feModel, bend.MidNode)),
                ("cloth_node_1", ClothFaceCornerName(feModel, bend.End0)),
                ("cloth_node_2", ClothFaceCornerName(feModel, bend.End1)),
                ("max_angle", float.RadiansToDegrees(MathF.Acos(Math.Clamp(cosine, -1f, 1f))))));
        }
    }

    /// <summary>
    /// Declares one compiled surface face whose corners are all already-declared cloth nodes as the
    /// <c>ClothTri</c> or <c>ClothQuad</c> element the original was built from, instead of inventing a
    /// proxy sheet to carry it. Repeated corners collapse, so a triangle stored in a quad slot emits as
    /// a ClothTri.
    /// </summary>
    static KVObject? MakeClothFace(FeModel feModel, int[] face)
    {
        var corners = new List<int>(4);
        foreach (var corner in face)
        {
            if (!corners.Contains(corner))
            {
                corners.Add(corner);
            }
        }

        if (corners.Count is not (3 or 4))
        {
            return null;
        }

        var node = MakeNode(corners.Count == 4 ? "ClothQuad" : "ClothTri");
        for (var i = 0; i < corners.Count; i++)
        {
            node.Add("cloth_node_" + i.ToString(CultureInfo.InvariantCulture),
                ClothFaceCornerName(feModel, corners[i]));
        }

        return node;
    }

    /// <summary>
    /// Emits every face the original built from a ClothTri / ClothQuad over declared cloth nodes, and
    /// returns the control nodes those faces name so the caller can keep them declared.
    /// </summary>
    static HashSet<int> AddClothFaces(KVObject clothChildren, FeModel feModel)
    {
        var cornered = new HashSet<int>();
        foreach (var face in feModel.GetAuthoredElementFaces())
        {
            if (MakeClothFace(feModel, face) is not { } element)
            {
                continue;
            }

            clothChildren.Add(element);
            cornered.UnionWith(face);
        }

        return cornered;
    }

    /// <summary>
    /// Whether a model with jiggle bones was authored with a <c>Softbody</c> holding a <c>ClothParams</c> even though it
    /// has no cloth node of its own. The jiggle bones compile the same either way, and the ClothParams leaves only its
    /// iteration counts behind: a model compiled without one ships them as zero.
    /// </summary>
    internal static bool HasJiggleBoneClothParams(FeModel feModel)
        => feModel.JiggleBones.Length > 0
            && (feModel.ExtraIterations != 0 || feModel.ExtraGoalIterations != 0 || feModel.ExtraPressureIterations != 0);

    bool EmitFreeNodeClothPhase(FeModel feModel, List<FeModel.BoneChain> boneChains, KVObject rootChildren)
    {
        // No sheet and no chains: cloth built purely from free-standing ClothNodes (and the
        // ClothSprings wiring them), e.g. the "$cloth_node_*" minimal rigs and lone goal-driven
        // bones. A FeModel that yields no authorable node here (jiggle-bone users, weapon-offset
        // rigs) emits nothing and falls through to the PHYS transplant placeholder below.
        var (softbody, softbodyChildren) = MakeListNode("Softbody");
        AddSoftbodyAttributes(softbody, feModel);
        softbodyChildren.Add(MakeClothParams(feModel, explicitMasses: feModel.HasExplicitMasses));
        var (clothFolder, clothFolderChildren) = MakeListNode("Folder");
        clothFolder.Add("name", "cloth");
        softbodyChildren.Add(clothFolder);

        var strip = feModel.ImportedStripNodes;
        if (strip.Count > 0)
        {
            clothFolderChildren.Add(MakeImportedCloth(feModel, strip));
        }

        var clothBones = ClothBoneNames(feModel);
        clothBones.UnionWith(ImportedStripBoneNames(feModel, strip));
        var clustered = AddClothSelfCollisionClusters(softbodyChildren, feModel, clothBones);
        clustered.UnionWith(strip);
        var freeNodes = AddFreeClothNodesAndSprings(clothFolderChildren, softbodyChildren, feModel,
            clustered, static _ => true, clothBones,
            ClothVertexMapFolders(feModel, clothFolderChildren),
            bareStaticReparented: ClothControlAncestorTest(feModel));
        AddClothFaces(clothFolderChildren, feModel);
        AddClothStiffHinges(softbodyChildren, feModel);

        // Every ctrl of a collision-shape-only model is a shape parent bone, which the loop above
        // skips, so gating on the node count alone drops the shapes with the rest of the Softbody.
        if (freeNodes > 0 || strip.Count > 0 || CollisionShapeParentBones(feModel).Count > 0 || HasJiggleBoneClothParams(feModel))
        {
            AddClothFollowBones(softbodyChildren, feModel, clothBones);
            AddClothCollisionShapes(softbodyChildren, feModel);
            AddClothEffects(softbodyChildren, feModel, AvailableVertexMaps(feModel, boneChains));
            AddShapeParentDefaultClothNodes(softbodyChildren, feModel);
            rootChildren.Add(softbody);
            AddClothAntiTunnelProbes(rootChildren, feModel, proxyNodeNames: null);
            return true;
        }

        return false;
    }
}
