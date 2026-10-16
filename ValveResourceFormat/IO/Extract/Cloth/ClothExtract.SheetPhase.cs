using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>A free <c>$cloth_node_</c> control node, its element name and where it is re-authored.</summary>
    private readonly record struct FreeClothNode(int Node, string ElementName, ClothNodeAnchor Anchor);

    /// <summary>The control nodes of a sheet phase that no exported sheet or independent chain recreates.</summary>
    private sealed record SheetControlNodes(
        HashSet<int> IndependentChainNodes,
        HashSet<string> ProxySkinnedBones,
        List<NodeRef> LoneClothNodes,
        List<NodeRef> LeftoverStaticNodes,
        List<NodeRef> UnregisteredNodes,
        List<FreeClothNode> UnregisteredFreeNodes,
        Dictionary<int, string> FreeClothNodeNames);

    private static KVObject MakeClothProxyMeshFile(string name, string fileName, bool backSolveJoints, bool driveMeshes,
        bool addBonesToRenderMesh, float backSolveInfluenceThreshold, bool flexClothBorders)
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

        var importFilter = KVObject.Collection();
        importFilter.Add("exclude_by_default", false);
        importFilter.Add("exception_list", KVObject.Array());
        node.Add("import_filter", importFilter);
        return node;
    }

    /// <summary>
    /// Maps the control node of every faced vertex of the exported proxies to the <c>$cloth_m{N}p{L}</c> name the
    /// recompile gives it.
    /// </summary>
    private static Dictionary<int, string> BuildProxyNodeNameMap(List<ClothProxyFile> proxies)
    {
        var proxyNodeNames = new Dictionary<int, string>();
        for (var proxyIndex = 0; proxyIndex < proxies.Count; proxyIndex++)
        {
            var proxy = proxies[proxyIndex].Proxy;
            var nodeIndices = proxy.NodeIndices;
            var faced = proxy.Faces.SelectMany(static face => face).ToHashSet();

            for (var localIndex = 0; localIndex < nodeIndices.Length; localIndex++)
            {
                if (faced.Contains(localIndex))
                {
                    proxyNodeNames[nodeIndices[localIndex]] = $"{ClothReconstruction.ProxyNamePrefix}{proxyIndex}p{localIndex}";
                }
            }
        }

        return proxyNodeNames;
    }

    /// <summary>The selection an exported sheet stands for among all of <see cref="ProxyMeshes"/>, or null where none does.</summary>
    private string? ProxyVertexMapName(ClothReconstruction cloth, ProxyMesh proxy)
    {
        if (!proxyVertexMapNames.TryGetValue(proxy, out var mapName))
        {
            proxyGroup ??= ProxyMeshes.ConvertAll(static entry => entry.Proxy);
            mapName = cloth.GetProxyVertexMapName(proxy, proxyGroup);
            proxyVertexMapNames[proxy] = mapName;
        }

        return mapName;
    }

    /// <summary>
    /// The vertices of an exported proxy that survive the import: faced, and not a pin whose face neighbours are all
    /// pinned.
    /// </summary>
    private static HashSet<int> SurvivingProxyVertices(ProxyMesh proxy)
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

    private void EmitProxySheetClothPhase(ClothReconstruction cloth, List<BoneChain> boneChains, KVObject rootChildren)
    {
        var backSolveJoints = cloth.Index.FitMatrixNodes.Count > 0 || cloth.DrivesRealBones;
        var independentChains = boneChains.Where(cloth.IsIndependentChain).ToList();
        var proxyNodeNames = BuildProxyNodeNameMap(ProxyMeshes);

        rootChildren.Add(MakeClothProxyMeshList(cloth, independentChains, backSolveJoints, proxyNodeNames));

        var (softbody, softbodyChildren) = MakeSoftbody(cloth);
        var surfaceRods = SurfaceRods(cloth);
        softbodyChildren.Add(MakeClothParams(cloth, surfaceRods.GeneratesBendRods, surfaceRods.GeneratesBendOnlyRods,
            surfaceRods.AddCurvature > 0f ? surfaceRods.AddCurvature : cloth.ChainRingCurvature));

        var nodes = ClassifySheetControlNodes(cloth, boneChains, independentChains);
        var authoredFaces = cloth.GetAuthoredElementFaces();
        if (independentChains.Count > 0 || nodes.LoneClothNodes.Count > 0 || nodes.LeftoverStaticNodes.Count > 0
            || nodes.UnregisteredNodes.Count > 0 || nodes.UnregisteredFreeNodes.Count > 0 || authoredFaces.Count > 0)
        {
            DeclareSheetClothFolder(cloth, softbodyChildren, independentChains, nodes, proxyNodeNames);
        }

        List<NodeRef> authoredNodes = [.. nodes.LoneClothNodes, .. nodes.LeftoverStaticNodes, .. nodes.UnregisteredNodes];
        var authoredClothNodes = authoredNodes.Select(static entry => entry.Node).ToHashSet();
        AddClothProxySprings(softbodyChildren, cloth, ProxyMeshes, nodes.IndependentChainNodes,
            authoredClothNodes, nodes.FreeClothNodeNames, surfaceRods.Derived, proxyNodeNames);
        AddClothSourceSprings(softbodyChildren, cloth, independentChains);
        AddClothChainSurplusClusters(softbodyChildren, cloth, independentChains);
        AddClothChainCrossLinkSprings(softbodyChildren, cloth, independentChains);
        AddClothChainVolumetricMaps(softbodyChildren, cloth, independentChains);

        var clothBones = ClothBoneNames(cloth);
        clothBones.UnionWith(nodes.ProxySkinnedBones);
        clothBones.UnionWith(independentChains.SelectMany(static chain => chain.Joints).Select(static joint => joint.Name));
        clothBones.UnionWith(authoredNodes.Select(static entry => entry.Name));
        clothBones.UnionWith(nodes.UnregisteredFreeNodes.Select(static entry => entry.Anchor.RootBone));
        AddClothSelfCollisionClusters(softbodyChildren, cloth, clothBones);
        AddClothPhaseTail(cloth, rootChildren, softbody, softbodyChildren, clothBones, independentChains,
            antiTunnelCloth: ProxyMeshes.Select(static proxy => proxy.Name),
            jointLocks: (node, name) => !nodes.IndependentChainNodes.Contains(node) && !authoredClothNodes.Contains(node)
                && (cloth.Index.FitMatrixNodes.Contains(node) || nodes.ProxySkinnedBones.Contains(name)),
            proxyNodeNames: proxyNodeNames);
    }

    /// <summary>
    /// The <c>ClothProxyMeshList</c> declaring every exported sheet with its back-solve, border and render-bone keys,
    /// grouped under the <c>ClothVertexMap</c> each one stands for, then the unregistered selections and the grids.
    /// </summary>
    private KVObject MakeClothProxyMeshList(ClothReconstruction cloth, List<BoneChain> independentChains, bool backSolveJoints,
        Dictionary<int, string> proxyNodeNames)
    {
        var chainDrivenBones = new HashSet<string>(
            independentChains.SelectMany(static chain => chain.Joints).Select(joint => cloth.Fe.CtrlName[joint.Node]),
            StringComparer.OrdinalIgnoreCase);

        var positionDrivenBones = new HashSet<string>(
            cloth.Fe.CtrlName.Skip(cloth.FirstPositionDrivenNode).Where(static name => !FeModelIndex.IsProxyNodeName(name)),
            StringComparer.OrdinalIgnoreCase);

        bool ProxyDrivesUnchainedBone(ProxyMesh proxy, float threshold)
        {
            IEnumerable<string> CarriedBones(int vertex)
            {
                if (cloth.HasCompiledFirstPositionDrivenNode)
                {
                    return proxy.SkinInfluences[vertex].Where(i => i.Weight >= threshold).Select(static i => i.Bone);
                }

                if (cloth.Index.FitMatrixNodes.Count == 0)
                {
                    return [cloth.ResolveSkinBone(proxy.NodeIndices[vertex]) ?? string.Empty];
                }

                return proxy.SkinInfluences[vertex].Select(static i => i.Bone);
            }

            for (var v = 0; v < proxy.ClothEnable.Length; v++)
            {
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

        var proxyRenderBones = new HashSet<string>(
            (model?.Skeleton.Bones ?? []).Where(static bone => bone.IsProceduralCloth && FeModelIndex.IsProxyNodeName(bone.Name))
                .Select(static bone => bone.Name),
            StringComparer.OrdinalIgnoreCase);

        bool ProxyAddsBonesToRenderMesh(ProxyMesh proxy)
            => cloth.ProxyOwnsNodeBases(proxy)
            || (proxyRenderBones.Count > 0
                && Array.Exists(proxy.NodeIndices, node => cloth.IsProxyMeshNode(node)
                    && proxyRenderBones.Contains(cloth.Fe.CtrlName[node])));

        var (clothProxyList, clothProxyChildren) = MakeListNode("ClothProxyMeshList");
        var vertexMapContainers = new Dictionary<string, KVObject>(StringComparer.Ordinal);
        foreach (var proxyFile in ProxyMeshes)
        {
            var threshold = cloth.GetBackSolveInfluenceThreshold(proxyFile.Proxy);
            var proxyBackSolve = backSolveJoints && ProxyDrivesUnchainedBone(proxyFile.Proxy, threshold)
                && !cloth.IsUnbackSolvedProxyMesh(proxyFile.Proxy);
            var proxyDrivesMeshes = proxyBackSolve || cloth.ProxyFitsUndrivenBone(proxyFile.Proxy);
            var addsBonesToRenderMesh = ProxyAddsBonesToRenderMesh(proxyFile.Proxy);
            var proxyFlexes = ProxyFlexesClothBorders(cloth, proxyFile.Proxy, proxyBackSolve, addsBonesToRenderMesh);
            if (proxyFlexes)
            {
                flexedProxies.Add(proxyFile.Proxy);
            }

            var proxyNode = MakeClothProxyMeshFile(proxyFile.Name, proxyFile.FileName, proxyBackSolve, proxyDrivesMeshes,
                addsBonesToRenderMesh, threshold, proxyFlexes);

            if (ProxyVertexMapName(cloth, proxyFile.Proxy) is { } proxyVertexMap)
            {
                if (!vertexMapContainers.TryGetValue(proxyVertexMap, out var mapChildren))
                {
                    var (mapNode, children) = MakeListNode("ClothVertexMap");
                    mapNode.Add("name", proxyVertexMap);
                    if (cloth.VertexMapAliases(proxyVertexMap) is { Count: > 1 } aliases)
                    {
                        mapNode.Add("aliases", string.Join(',', aliases));
                    }

                    AddClothVertexMapAttributes(mapNode, cloth, proxyVertexMap, proxyNodeNames);
                    if (cloth.UniformVertexMapWeight(proxyVertexMap) is { } mapWeight)
                    {
                        mapNode.Add("weight", mapWeight);
                    }

                    clothProxyChildren.Add(mapNode);
                    vertexMapContainers[proxyVertexMap] = mapChildren = children;
                }

                mapChildren.Add(proxyNode);
                continue;
            }

            clothProxyChildren.Add(proxyNode);
        }

        foreach (var map in cloth.VertexMaps)
        {
            if (cloth.RegistersVertexSet(map.NameHash) || vertexMapContainers.ContainsKey(map.Name))
            {
                continue;
            }

            var members = KVObject.Collection();
            var listed = 0;
            for (var node = map.VertexBase; node < map.VertexBase + map.VertexCount; node++)
            {
                var weight = map.WeightOf(node);
                if (weight <= 0f || !proxyNodeNames.TryGetValue(node, out var memberName))
                {
                    continue;
                }

                AddNodeTableMember(members, memberName, weight);
                listed++;
            }

            if (listed == 0)
            {
                continue;
            }

            clothProxyChildren.Add(MakeClothVertexMap(cloth, map.Name, members, proxyNodeNames).Node);
        }

        return clothProxyList;
    }

    /// <summary>
    /// Sorts the control nodes of a sheet phase into the simulated lone nodes, the static control bones, and the
    /// generated or fitted nodes no exported sheet or independent chain recreates.
    /// </summary>
    private SheetControlNodes ClassifySheetControlNodes(ClothReconstruction cloth, List<BoneChain> boneChains,
        List<BoneChain> independentChains)
    {
        var chainNodes = ChainJointNodes(boneChains);
        var independentChainNodes = ChainJointNodes(independentChains);
        var boneByName = model?.Skeleton.Bones
            .GroupBy(static bone => bone.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.First(), StringComparer.OrdinalIgnoreCase);
        var shapeParentBones = cloth.CollisionShapes.ParentBones;
        var anchorOf = BuildCtrlAnchorMap(cloth);
        var proxyRegisteredNodes = new HashSet<int>();
        var proxySkinnedBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recoveredSkinnedBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, _, proxy) in ProxyMeshes)
        {
            foreach (var vertex in SurvivingProxyVertices(proxy))
            {
                var vertexNode = proxy.NodeIndices[vertex];
                proxyRegisteredNodes.Add(vertexNode);
                var recoveredVertex = cloth.RecoveredSkinWeights.ContainsKey(vertexNode);
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
                || independentChainNodes.Contains(node) || IsDeclaredByItsJiggleBone(cloth, node)
                || shapeParentBones.Contains(name);

        bool IsSkinnedRoot(int node, string name)
            => (recoveredSkinnedBones.Contains(name) || (cloth.Index.FitMatrixNodes.Count == 0 && proxySkinnedBones.Contains(name)))
                && node < cloth.SkelParents.Length && cloth.SkelParents[node] < 0;

        var loneClothNodes = new List<NodeRef>();
        var leftoverStaticNodes = new List<NodeRef>();
        var unregisteredNodes = new List<NodeRef>();
        var unregisteredFreeNodes = new List<FreeClothNode>();
        var freeClothNodeNames = new Dictionary<int, string>();

        for (var node = 0; node < cloth.Fe.CtrlName.Length; node++)
        {
            var name = cloth.Fe.CtrlName[node];
            if (FeModelIndex.IsProxyNodeName(name) || cloth.Index.FitMatrixNodes.Contains(node) || chainNodes.Contains(node))
            {
                if (IsRecreated(node, name))
                {
                    continue;
                }

                if (cloth.IsFreeClothNode(node))
                {
                    if (TryResolveClothNodeAnchor(cloth, anchorOf, node, out var anchor))
                    {
                        var elementName = ClothFaceCornerName(cloth, node);
                        unregisteredFreeNodes.Add(new FreeClothNode(node, elementName, anchor));
                        freeClothNodeNames[node] = elementName;
                    }
                }
                else if (!FeModelIndex.IsProxyNodeName(name))
                {
                    unregisteredNodes.Add(new NodeRef(name, node));
                }

                continue;
            }

            if (IsDeclaredByItsJiggleBone(cloth, node))
            {
                continue;
            }

            if (node < cloth.Fe.NodeInvMasses.Length && cloth.Fe.NodeInvMasses[node] != 0f)
            {
                loneClothNodes.Add(new NodeRef(name, node));
            }
            else if (!shapeParentBones.Contains(name) && !IsSkinnedRoot(node, name)
                && boneByName is not null && boneByName.TryGetValue(name, out var bone) && bone.IsClothControlNode)
            {
                leftoverStaticNodes.Add(new NodeRef(name, node));
            }
        }

        return new SheetControlNodes(independentChainNodes, proxySkinnedBones, loneClothNodes, leftoverStaticNodes,
            unregisteredNodes, unregisteredFreeNodes, freeClothNodeNames);
    }

    /// <summary>
    /// Declares the independent chains, the classified control nodes and the authored faces in the sheet phase's cloth
    /// folder.
    /// </summary>
    private static void DeclareSheetClothFolder(ClothReconstruction cloth, KVObject softbodyChildren, List<BoneChain> independentChains,
        SheetControlNodes nodes, Dictionary<int, string> proxyNodeNames)
    {
        var clothFolderChildren = AddClothFolder(softbodyChildren);

        foreach (var boneChain in independentChains)
        {
            AddClothChainDeclarations(clothFolderChildren, cloth, boneChain);
        }

        AddClothRigidCloudClusterLocks(softbodyChildren, cloth, independentChains);

        var folderFor = ClothVertexMapFolders(cloth, clothFolderChildren);

        foreach (var (name, node) in nodes.LoneClothNodes)
        {
            if (LoneClothNodeIsOriginalRoot(cloth, node))
            {
                clothFolderChildren.Add(MakeLoneJointChain(cloth, name, node));
            }
            else
            {
                folderFor(node, true).Add(MakeClothNode(cloth, name, node, proxyNodeNames: proxyNodeNames));
            }
        }

        foreach (var (name, node) in nodes.LeftoverStaticNodes)
        {
            folderFor(node, true).Add(MakeClothNode(cloth, name, node, isStaticNode: true,
                proxyNodeNames: proxyNodeNames));
        }

        foreach (var (name, node) in nodes.UnregisteredNodes)
        {
            clothFolderChildren.Add(MakeClothNode(cloth, name, node,
                isStaticNode: cloth.Index.IsStatic(node), proxyNodeNames: proxyNodeNames));
        }

        foreach (var (node, elementName, anchor) in nodes.UnregisteredFreeNodes)
        {
            clothFolderChildren.Add(MakeClothNode(cloth, anchor.RootBone, node,
                isStaticNode: cloth.Index.IsStatic(node), elementName: elementName, origin: anchor.Origin, angles: anchor.Angles,
                proxyNodeNames: proxyNodeNames));
        }

        AddClothFaces(clothFolderChildren, cloth);
        AddClothStiffHinges(softbodyChildren, cloth);
    }
}
