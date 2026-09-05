using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
    // A ClothProxyMeshFile referencing the cloth-sheet DMX. With backSolveJoints=true the compiler
    // back-solves the skinned bone-chain joints from the simulated sheet, regenerating the bone-chain
    // FeModel nodes (so the proxy covers the WHOLE cloth and ClothChain is not needed - and must not be
    // emitted, or the bones would be driven twice). Generated chain grids use backSolveJoints=false:
    // there the ClothChains simulate the bones and the sheet only drives the render mesh between them.
    //
    // back_solve_joints_drive_meshes tracks backSolveJoints rather than being a blanket true or false: the
    // flag is not scoped to this proxy's own nodes when it disagrees with back_solve_joints, and the
    // compiler then back-solves fit matrices for unrelated bones elsewhere in the model. The disabled
    // ready-made grid is the exception: it passes backSolveJoints=false but driveMeshes=true, so a
    // re-author can enable it to drive the mesh directly.
    //
    // back_solve_influence_threshold is the minimum skin weight at which a vertex contributes to a joint's
    // back-solved fit. The value is derived per proxy from the original's own compiled fit data (see
    // FeModel.GetBackSolveInfluenceThreshold); the parameter default is the compiler's own, for the
    // generated grids that carry no proxy to derive from.
    static KVObject MakeClothProxyMeshFile(string name, string fileName, bool backSolveJoints, bool driveMeshes, bool addBonesToRenderMesh = false,
        float backSolveInfluenceThreshold = FeModel.DefaultBackSolveInfluenceThreshold, bool flexClothBorders = false)
    {
        var node = MakeNode("ClothProxyMeshFile",
            ("name", name),
            ("filename", fileName),
            ("import_scale", 1.0f),
            ("back_solve_joints", backSolveJoints),
            ("back_solve_joints_drive_meshes", driveMeshes),
            ("flex_cloth_borders", flexClothBorders),
            ("add_bones_to_render_mesh", addBonesToRenderMesh),
            ("back_solve_influence_threshold", backSolveInfluenceThreshold),
            ("cloth_friction_bias", 0.0f),
            ("cloth_friction_scale", 1.0f),
            ("lock_friction_0", false),
            ("lock_friction_1", false),
            ("cloth_goal_strength_bias", 0.0f),
            ("cloth_goal_strength_scale", 1.0f),
            ("lock_goal_strength_0", false),
            ("lock_goal_strength_1", false),
            ("cloth_drag_scale", 1.0f),
            ("cloth_mass_scale", 1.0f),
            ("cloth_gravity_scale", 1.0f),
            ("cloth_collision_radius_scale", 1.0f),
            ("cloth_ground_collision_scale", 1.0f),
            ("cloth_ground_friction_scale", 1.0f),
            ("cloth_use_rods_scale", 1.0f),
            ("cloth_make_rods_scale", 1.0f),
            ("cloth_anchor_free_rotate_scale", 1.0f),
            ("cloth_volumetric_scale", 1.0f),
            ("cloth_suspenders_scale", 1.0f),
            ("cloth_bend_stiffness_scale", 1.0f),
            ("cloth_stray_radius_inv_scale", 1.0f),
            ("cloth_stray_radius_scale", 1.0f),
            ("cloth_stray_radius_stretchiness_scale", 1.0f));

        // envelope_inches (how far the sheet reaches when DRIVING render meshes) is not emitted, matching
        // how hand-authored proxies ship. A large value drive-binds essentially the whole render mesh to
        // the sheet, and those bindings live in the compiled vmesh rather than in the PHYS block.

        var importFilter = KVObject.Collection();
        importFilter.Add("exclude_by_default", false);
        importFilter.Add("exception_list", KVObject.Array());
        node.Add("import_filter", importFilter);
        return node;
    }

    // Maps each global control-node index covered by an exported proxy mesh to the "$cloth_m{N}p{local}"
    // name the compiler will create for it in OUR export (declaration order; kept aligned with the
    // compiler's own name-sorted numbering by the padded proxy names, see EnqueueClothProxyMesh). Only
    // faced vertices are mapped: an unfaced vertex is silently dropped by the importer, so a reference to
    // it is a hard compile failure ("Cannot find node") - see TriangulateDominantPlane remarks.
    static Dictionary<int, string> BuildProxyNodeNameMap(
        List<(string FileName, string Name, FeModel.ProxyMesh Proxy)> proxies)
    {
        var proxyNodeNames = new Dictionary<int, string>();
        for (var proxyIndex = 0; proxyIndex < proxies.Count; proxyIndex++)
        {
            var proxy = proxies[proxyIndex].Proxy;
            var nodeIndices = proxy.NodeIndices;

            var faced = new HashSet<int>();
            foreach (var face in proxy.Faces)
            {
                foreach (var localIndex in face)
                {
                    faced.Add(localIndex);
                }
            }

            for (var localIndex = 0; localIndex < nodeIndices.Length; localIndex++)
            {
                if (faced.Contains(localIndex))
                {
                    proxyNodeNames[nodeIndices[localIndex]] = $"$cloth_m{proxyIndex}p{localIndex}";
                }
            }
        }

        return proxyNodeNames;
    }

    // The vertices of an exported proxy mesh that survive into the compiled node set. Two importer rules
    // remove the rest, and a removed vertex registers neither itself nor the bones it is skinned to:
    // an unfaced vertex is dropped outright (the same rule BuildProxyNodeNameMap maps around), and a
    // pinned vertex whose face-neighbours are all pinned belongs to a fully-static region the solver
    // discards (the first of the two conditions behind FeModel.ProxyMesh.IsDropRisk).
    static HashSet<int> SurvivingProxyVertices(FeModel.ProxyMesh proxy)
    {
        var hasSimulatedNeighbour = new bool[proxy.Positions.Length];
        var surviving = new HashSet<int>();

        foreach (var face in proxy.Faces)
        {
            foreach (var a in face)
            {
                surviving.Add(a);
                foreach (var b in face)
                {
                    if (a != b && proxy.ClothEnable[b] != 0f)
                    {
                        hasSimulatedNeighbour[a] = true;
                    }
                }
            }
        }

        surviving.RemoveWhere(v => proxy.ClothEnable[v] == 0f && !hasSimulatedNeighbour[v]);
        return surviving;
    }

    bool EmitProxySheetClothPhase(FeModel feModel, List<FeModel.BoneChain> boneChains, KVObject rootChildren)
    {
        // Phase 2 (preferred): the cloth sheet ships as a proxy mesh. With back_solve_joints the
        // compiler regenerates the $cloth_* sheet nodes and back-solves the bone-chain follower
        // nodes the sheet is skinned to, so a chain whose joints appear in the FeModel's own
        // m_FitMatrices is driven by the proxy and must not also get an explicit ClothChain. A
        // proxy mesh does not by itself mean every bone chain is back-solved, though: a model can
        // ship independent cloth alongside an unrelated proxy-mesh panel with back_solve_joints
        // off and no m_FitMatrices entries, and such chains still need the explicit ClothChain
        // emission Phase 1 uses below, or their joints are never registered as cloth nodes at all.
        // back_solve_joints goes on whenever the cloth drives real bones, both the fit-matrix case
        // and the CtrlOffsets-only case with m_FitMatrices empty; DrivesRealBones is a superset of
        // FitMatrixNodes being non-empty.
        var backSolveJoints = feModel.FitMatrixNodes.Count > 0 || feModel.DrivesRealBones;
        // A fit matrix only drives a bone THROUGH a proxy sheet when the fit is taken over that
        // sheet's vertices (see FeModel.ProxyFitMatrixNodes). A chain whose fit matrices are taken
        // over its own $cc extrude ring is the compiler's chain-internal orientation solve, so that
        // chain still has to be emitted or its whole ring goes with it.
        var independentChains = boneChains
            .Where(chain => !chain.Joints.Any(joint => feModel.ProxyFitMatrixNodes.Contains(joint.Node))
                && !feModel.IsSheetDrivenChain(chain))
            .ToList();

        // The bones an independent ClothChain already simulates and drives on its own. The compiler
        // regenerates those bones' proxy nodes from the chain, so a reconstructed proxy mesh that
        // ONLY re-drives such bones is redundant, and a tiny one is degenerate for a back-solved fit:
        // a strip with one "most-bound" vertex per joint makes the compiler's most-bound-joint search
        // access-violate. back_solve is therefore decided PER PROXY below, on only when the proxy
        // drives a real bone no ClothChain covers. Names are compared case-insensitively, compiled
        // control-node and skeleton casing being free to disagree (see BuildClothProxyMeshDmx).
        var chainDrivenBones = new HashSet<string>(
            independentChains.SelectMany(static chain => chain.Joints).Select(joint => feModel.CtrlNames[joint.Node]),
            StringComparer.OrdinalIgnoreCase);

        // A sheet is also skinned to the body bones it merely hangs off, which the cloth uses only
        // as collision anchors. The bones a proxy back-solves are the ones the original compile left
        // position-driven, so only those count as evidence below.
        var positionDrivenBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = feModel.FirstPositionDrivenNode; i < feModel.CtrlNames.Length; i++)
        {
            if (!FeModel.IsProxyNodeName(feModel.CtrlNames[i]))
            {
                positionDrivenBones.Add(feModel.CtrlNames[i]);
            }
        }

        // The bones a sheet back-solves are the ones a SIMULATED vertex carries at or above
        // back_solve_influence_threshold; the proxy DMX carries a slot for every one of them. A
        // compile that does not state its own position-driven boundary gives no evidence of which
        // bones it drove, and keeps the reading that predates this rule.
        bool ProxyDrivesUnchainedBone(FeModel.ProxyMesh proxy)
        {
            var threshold = feModel.GetBackSolveInfluenceThreshold(proxy);

            IEnumerable<string> CarriedBones(int vertex)
                => feModel.HasCompiledFirstPositionDrivenNode
                    ? proxy.SkinInfluences[vertex]
                        .Where(i => i.Weight >= threshold)
                        .Select(static i => i.Bone)
                    : feModel.FitMatrixNodes.Count == 0
                        ? [feModel.ResolveSkinBone(proxy.NodeIndices[vertex]) ?? string.Empty]
                        : proxy.SkinInfluences[vertex].Select(static i => i.Bone);

            for (var v = 0; v < proxy.ClothEnable.Length; v++)
            {
                // Only simulated vertices back-solve a bone; a pinned vertex just follows its anchor.
                if (proxy.ClothEnable[v] == 0f)
                {
                    continue;
                }

                foreach (var bone in CarriedBones(v))
                {
                    if (positionDrivenBones.Contains(bone) && !chainDrivenBones.Contains(bone))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // add_bones_to_render_mesh is recoverable from the skeleton: with the flag on, the compiler
        // adds a model-space skeleton "Bone" per cloth PROXY vertex, carrying the raw
        // "$cloth_m{proxy}p{vertex}" control-node name (GetExportBoneName sanitizes '$' to '_' for
        // this exporter's own vmdl text only; the compiled skeleton keeps the literal '$').
        //
        // Bone.IsProceduralCloth alone does not identify those bones. It is Cloth | Procedural, a
        // combination any procedurally-driven cloth bone carries, real back-solved bones with real
        // names included. The narrow signal is a bone that is both flagged procedural-cloth and
        // named by the synthetic proxy convention, which is FeModel.IsProxyNodeName's '$' check.
        var addBonesToRenderMesh = model?.Skeleton.Bones.Any(static b =>
            b.IsProceduralCloth && FeModel.IsProxyNodeName(b.Name)) ?? false;

        // A pinned border vertex keeps its rotation locked unless the sheet was imported with
        // flex_cloth_borders on, so the flag is re-emitted wherever that reproduces the
        // original. On a non-back-solving sheet the flag reaches exactly the pins a face joins
        // to two or more simulated corners: those it frees and gives a node base, which no
        // per-vertex paint does, while a pin every face leaves with fewer simulated corners
        // stays rotation-locked either way and carries no evidence. The flag is taken when every
        // reached pin is recorded rotation-free and every unreached one rotation-locked, so the
        // paint the flag replaces has nothing left to say. A back-solving sheet instead
        // frees exactly the pins with a skin influence on a registered control and its fit
        // machinery pulls anchor parent chains in, so each pin's influence registration has to
        // match its rot-lock class, no gap slot's influences may register it (a new node the
        // original does not have), and every freed pin's static anchor needs its skeleton
        // parent already position-driven (the compiler otherwise registers or reclassifies
        // the parent).
        var ctrlIndexByName = new Dictionary<string, int>(feModel.CtrlNames.Length, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < feModel.CtrlNames.Length; i++)
        {
            ctrlIndexByName.TryAdd(feModel.CtrlNames[i], i);
        }

        bool ProxyFlexesClothBorders(FeModel.ProxyMesh proxy, bool proxyBackSolves)
        {
            var faced = new HashSet<int>();
            var flexReaches = new HashSet<int>();
            foreach (var face in proxy.Faces)
            {
                faced.UnionWith(face);
                if (face.Distinct().Count(corner => proxy.ClothEnable[corner] != 0f) >= 2)
                {
                    flexReaches.UnionWith(face.Where(corner => proxy.ClothEnable[corner] == 0f));
                }
            }

            var freesAny = false;
            for (var v = 0; v < proxy.ClothEnable.Length; v++)
            {
                if (proxy.ClothEnable[v] != 0f)
                {
                    continue;
                }

                var node = proxy.NodeIndices[v];

                // A non-back-solving sheet never registers a padded gap slot, so only the
                // recorded rot-lock classes have to agree with the flag's reach.
                if (!proxyBackSolves)
                {
                    if (!faced.Contains(v) || node >= feModel.StaticNodeCount)
                    {
                        continue;
                    }

                    if (flexReaches.Contains(v) != feModel.AllowsRotation(node))
                    {
                        return false;
                    }

                    freesAny |= flexReaches.Contains(v);
                    continue;
                }

                (string Bone, float Weight)[] influences = feModel.RecoveredSkinWeights.TryGetValue(node, out var recovered) && recovered.Length > 0
                    ? [.. recovered]
                    : feModel.ResolveSkinBone(node) is { } skinBone ? [(skinBone, 1f)] : [];

                var registeredAnchors = influences
                    .Where(i => i.Weight > 0f && ctrlIndexByName.ContainsKey(i.Bone))
                    .Select(i => ctrlIndexByName[i.Bone])
                    .ToArray();

                if (!faced.Contains(v))
                {
                    if (registeredAnchors.Length > 0)
                    {
                        return false;
                    }

                    continue;
                }

                if (node >= feModel.StaticNodeCount)
                {
                    continue;
                }

                if ((registeredAnchors.Length > 0) != feModel.AllowsRotation(node))
                {
                    return false;
                }

                if (!feModel.AllowsRotation(node))
                {
                    continue;
                }

                freesAny = true;

                foreach (var anchorNode in registeredAnchors)
                {
                    if (!feModel.IsStatic(anchorNode))
                    {
                        continue;
                    }

                    if (feModel.SkeletonBoneParents?.GetValueOrDefault(feModel.CtrlNames[anchorNode]) is { } parent
                        && (!ctrlIndexByName.TryGetValue(parent, out var parentNode)
                            || !feModel.IsPositionDriven(parentNode)))
                    {
                        return false;
                    }
                }
            }

            return freesAny;
        }

        var (clothProxyList, clothProxyChildren) = MakeListNode("ClothProxyMeshList");
        foreach (var proxyFile in ClothProxyMeshesToExtract)
        {
            // The threshold is derived from the ORIGINAL's own compiled fit data (see
            // FeModel.GetBackSolveInfluenceThreshold): the sub-threshold weights the original's
            // compile dropped from its fit ranges have to be dropped by ours too, or the extra
            // influences carry bones over the fit-matrix minimum the original left below it.
            // Per-vertex gravity rides the cloth_gravity$0 paint in the proxy DMX instead of any
            // KV field here (the cloth_gravity_scale KV was tested and does not reach flGravity).
            var proxyBackSolve = backSolveJoints && ProxyDrivesUnchainedBone(proxyFile.Proxy);
            var proxyFlexes = ProxyFlexesClothBorders(proxyFile.Proxy, proxyBackSolve);
            if (proxyFlexes)
            {
                clothProxiesFlexed.Add(proxyFile.Proxy);
            }

            var proxyNode = MakeClothProxyMeshFile(proxyFile.Name, proxyFile.FileName, proxyBackSolve, driveMeshes: proxyBackSolve, addBonesToRenderMesh,
                backSolveInfluenceThreshold: feModel.GetBackSolveInfluenceThreshold(proxyFile.Proxy),
                flexClothBorders: proxyFlexes);

            // A selection covering exactly the sheet's simulated nodes is the m_VertexMaps entry a
            // ClothVertexMap around the sheet compiles to - the grouping the "Add Cloth Vertex Map"
            // wizard builds. Only a PROXY MESH may be wrapped this way: the same container around
            // free ClothNodes that a ClothSpring references makes the compiler access-violate.
            if (feModel.GetProxyVertexMapName(proxyFile.Proxy) is { } proxyVertexMap)
            {
                var (mapNode, mapChildren) = MakeListNode("ClothVertexMap");
                mapNode.Add("name", proxyVertexMap);
                AddClothVertexMapAttributes(mapNode, feModel, proxyVertexMap,
                    BuildProxyNodeNameMap(ClothProxyMeshesToExtract));
                mapChildren.Add(proxyNode);
                clothProxyChildren.Add(mapNode);
                continue;
            }

            clothProxyChildren.Add(proxyNode);
        }

        // Clean regular grids generated over the bone chains, shipped DISABLED next to the
        // recovered surface: a ready-made editable sheet for re-authoring the cloth.
        foreach (var clothGrid in ClothChainGridsToExtract)
        {
            var gridNode = MakeClothProxyMeshFile(clothGrid.Name, clothGrid.FileName, backSolveJoints: false, driveMeshes: true);
            gridNode.Add("disabled", true);
            clothProxyChildren.Add(gridNode);
        }

        rootChildren.Add(clothProxyList);

        // The proxy mesh ships the global solver scalars + any collision shapes via a Softbody.
        // Independent (non-back-solved) chains, if any, are emitted alongside it - see above.
        var (softbody, softbodyChildren) = MakeListNode("Softbody");
        AddSoftbodyAttributes(softbody, feModel);
        var surfaceRods = ClothRodsFromSurface(feModel, ClothProxyMeshesToExtract,
            out var generatesBendRods, out var generatesBendOnlyRods, out var addCurvature, out _);
        softbodyChildren.Add(MakeClothParams(feModel, generatesBendRods, generatesBendOnlyRods,
            addCurvature > 0f ? addCurvature : feModel.ChainRingCurvature));

        // Simulated real bones that are NEITHER back-solved NOR part of any multi-joint BoneChain:
        // standalone goal-attraction points wired together only by ClothSpring (see MakeClothNode).
        // A real bone with no real-bone descendants of its own never forms a BoneChain, so without
        // this these carry correct rods and connectivity but compiler-default paint.
        var chainNodes = boneChains.SelectMany(static chain => chain.Joints).Select(static joint => joint.Node).ToHashSet();
        var independentChainNodes = independentChains
            .SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Node)
            .ToHashSet();
        var loneClothNodes = new List<(string Name, int Node)>();

        // Real, STATIC (invMass == 0) bones the compiler still registers as FeModel control nodes
        // purely for orientation bookkeeping: no rods, no integrator role, no fit matrix, and no
        // ClothChain, capsule or sphere authoring of their own. The compiled skeleton's plain Cloth
        // bone flag (Bone.IsClothControlNode, not the stricter IsProceduralCloth) marks exactly
        // these, and each is emitted as a static ClothNode. A capsule or sphere parent bone is
        // excluded: the compiler walks a collision bone's ancestor chain and registers them itself.
        // A control name carries the case the cloth was AUTHORED in, which need not be the skeleton's;
        // the compiler resolves both by a case-insensitive compare, so this has to as well.
        var boneByName = model?.Skeleton.Bones
            .GroupBy(static bone => bone.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.First(), StringComparer.OrdinalIgnoreCase);
        var shapeParentBones = CollisionShapeParentBones(feModel);
        var leftoverStaticNodes = new List<(string Name, int Node)>();

        // The three constructs skipped above each rely on something else recreating the node:
        // a generated name on the proxy sheet or chain that regenerates it, a fit-matrix or
        // back-solved chain joint on the sheet that drives it. What actually registers a bone as a
        // control node is a proxy VERTEX skinned to it - so a fit-matrix/chain bone no emitted
        // proxy is skinned to, and a generated node no emitted proxy contains, is recreated by
        // nothing and vanishes from the compiled node set along with its rods and its share of
        // every neighbour's mass. Reconstructed sheets are smaller than the originals whenever a
        // vertex ends up unfaced (BuildProxyMeshesFromRodsOnly's 3-member minimum drops the rest),
        // which is exactly when this bites.
        var anchorOf = BuildCtrlAnchorMap(feModel);
        var jiggleNodes = feModel.JiggleBones.Select(static j => j.Node).ToHashSet();
        var proxyRegisteredNodes = new HashSet<int>();
        var proxySkinnedBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recoveredSkinnedBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, _, proxy) in ClothProxyMeshesToExtract)
        {
            foreach (var vertex in SurvivingProxyVertices(proxy))
            {
                var vertexNode = proxy.NodeIndices[vertex];
                proxyRegisteredNodes.Add(vertexNode);
                var recoveredVertex = feModel.RecoveredSkinWeights.ContainsKey(vertexNode);
                foreach (var (bone, weight) in proxy.SkinInfluences[vertex])
                {
                    proxySkinnedBones.Add(bone);
                    if (recoveredVertex && weight > 0f)
                    {
                        recoveredSkinnedBones.Add(bone);
                    }
                }
            }
        }

        bool IsRecreated(int node, string name)
            => proxyRegisteredNodes.Contains(node) || proxySkinnedBones.Contains(name)
                || independentChainNodes.Contains(node) || jiggleNodes.Contains(node)
                || shapeParentBones.Contains(name);

        // Emitted flat, never under a ClothVertexMap: that container around a free ClothNode a
        // ClothSpring names makes the compiler access-violate.
        var unregisteredNodes = new List<(string Name, int Node)>();
        var unregisteredFreeNodes = new List<(string RootBone, int Node, string ElementName, Vector3 Origin)>();
        var freeClothNodeNames = new Dictionary<int, string>();

        for (var node = 0; node < feModel.CtrlNames.Length; node++)
        {
            var name = feModel.CtrlNames[node];
            if (FeModel.IsProxyNodeName(name) || feModel.FitMatrixNodes.Contains(node) || chainNodes.Contains(node))
            {
                if (IsRecreated(node, name))
                {
                    continue;
                }

                const string ClothNodePrefix = "$cloth_node_";
                if (name.StartsWith(ClothNodePrefix, StringComparison.Ordinal))
                {
                    if (TryResolveClothNodeAnchor(feModel, anchorOf, node, out var rootBone, out var origin))
                    {
                        var elementName = name[ClothNodePrefix.Length..];
                        unregisteredFreeNodes.Add((rootBone, node, elementName, origin));
                        freeClothNodeNames[node] = elementName;
                    }
                }
                else if (!FeModel.IsProxyNodeName(name))
                {
                    unregisteredNodes.Add((name, node));
                }

                continue;
            }

            if (node < feModel.NodeInvMasses.Length && feModel.NodeInvMasses[node] != 0f)
            {
                loneClothNodes.Add((name, node));
            }
            // A bone an emitted proxy vertex is skinned to is registered by the sheet, whose
            // hierarchy spans only the skinned bones; an extra ClothNode on it instead parents it
            // onto its nearest control-node ancestor, whatever registered that. Skipped only where
            // the original records the bone as a hierarchy ROOT, and only where the skinning
            // evidence is real: on a no-fit model every exported influence is recovered, and on a
            // fit model a bone counts only when a RECOVERED influence names it, a synthesised one
            // being no evidence of how the compiler registers that bone.
            else if (!shapeParentBones.Contains(name)
                && !((recoveredSkinnedBones.Contains(name)
                        || (feModel.FitMatrixNodes.Count == 0 && proxySkinnedBones.Contains(name)))
                    && node < feModel.SkelParents.Length && feModel.SkelParents[node] < 0)
                && boneByName is not null && boneByName.TryGetValue(name, out var bone) && bone.IsClothControlNode)
            {
                leftoverStaticNodes.Add((name, node));
            }
        }

        var proxyNodeNameMap = BuildProxyNodeNameMap(ClothProxyMeshesToExtract);

        var authoredFaces = feModel.GetAuthoredElementFaces();
        if (independentChains.Count > 0 || loneClothNodes.Count > 0 || leftoverStaticNodes.Count > 0
            || unregisteredNodes.Count > 0 || unregisteredFreeNodes.Count > 0 || authoredFaces.Count > 0)
        {
            var (clothFolder, clothFolderChildren) = MakeListNode("Folder");
            clothFolder.Add("name", "cloth");
            softbodyChildren.Add(clothFolder);

            var loneJointChainCount = loneClothNodes.Count(n => LoneClothNodeIsOriginalRoot(feModel, n.Node));
            var hasOtherChains = independentChains.Count + loneJointChainCount > 1;

            foreach (var boneChain in independentChains)
            {
                clothFolderChildren.Add(MakeClothChainNode(feModel, boneChain, hasOtherChains));
                if (MakeClothChainRestatement(feModel, boneChain) is { } restated)
                {
                    clothFolderChildren.Add(restated);
                }
            }

            // A ClothNode carries no vertex_map of its own, so the selections covering one are
            // restored by parenting it under a ClothVertexMap instead.
            var folderFor = ClothVertexMapFolders(feModel, clothFolderChildren);

            foreach (var (name, node) in loneClothNodes)
            {
                if (LoneClothNodeIsOriginalRoot(feModel, node))
                {
                    clothFolderChildren.Add(MakeLoneJointChain(feModel, name, node, hasOtherChains));
                }
                else
                {
                    folderFor(node, true).Add(MakeClothNode(feModel, name, node, proxyNodeNames: proxyNodeNameMap));
                }
            }

            foreach (var (name, node) in leftoverStaticNodes)
            {
                folderFor(node, true).Add(MakeClothNode(feModel, name, node, isStaticNode: true,
                    proxyNodeNames: proxyNodeNameMap));
            }

            // is_static_node reproduces the node's own compiled class: a free node with no
            // coverage left compiles to invMass exactly 1.0 when dynamic and 0.0 when static.
            foreach (var (name, node) in unregisteredNodes)
            {
                clothFolderChildren.Add(MakeClothNode(feModel, name, node,
                    isStaticNode: feModel.IsStatic(node), proxyNodeNames: proxyNodeNameMap));
            }

            foreach (var (rootBone, node, elementName, origin) in unregisteredFreeNodes)
            {
                clothFolderChildren.Add(MakeClothNode(feModel, rootBone, node,
                    isStaticNode: feModel.IsStatic(node), elementName: elementName, origin: origin,
                    proxyNodeNames: proxyNodeNameMap));
            }

            AddClothFaces(clothFolderChildren, feModel);
        }

        var authoredClothNodes = loneClothNodes.Concat(leftoverStaticNodes).Concat(unregisteredNodes)
            .Select(static entry => entry.Node)
            .ToHashSet();
        AddClothProxySprings(softbodyChildren, feModel, ClothProxyMeshesToExtract, independentChainNodes,
            authoredClothNodes, freeClothNodeNames, surfaceRods, proxyNodeNameMap);
        AddClothChainSurplusClusters(softbodyChildren, feModel, independentChains);

        // A bone an emitted proxy vertex is skinned to is registered by the sheet itself; the rest
        // come from the chains and cloth nodes emitted above.
        var clothBones = ClothBoneNames(feModel);
        clothBones.UnionWith(proxySkinnedBones);
        clothBones.UnionWith(independentChains.SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Name));
        clothBones.UnionWith(loneClothNodes.Concat(leftoverStaticNodes).Concat(unregisteredNodes)
            .Select(static entry => entry.Name));
        clothBones.UnionWith(unregisteredFreeNodes.Select(static entry => entry.RootBone));
        AddClothFollowBones(softbodyChildren, feModel, clothBones);
        var shapeNames = AddClothCollisionShapes(softbodyChildren, feModel);
        AddClothAntiTunnelGroup(softbodyChildren, feModel, shapeNames,
            [.. ClothProxyMeshesToExtract.Select(static proxy => proxy.Name)]);
        AddClothEffects(softbodyChildren, feModel, AvailableVertexMaps(feModel, independentChains));

        rootChildren.Add(softbody);
        AddClothAntiTunnelProbes(rootChildren, feModel, proxyNodeNameMap);

        return true;
    }
}
