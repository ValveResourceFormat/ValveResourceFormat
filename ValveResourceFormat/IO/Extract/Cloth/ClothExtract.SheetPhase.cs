using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>
    /// A <c>ClothProxyMeshFile</c> referencing a sheet DMX. The influence threshold defaults to the compiler's own, for
    /// the generated grids.
    /// </summary>
    private static KVObject MakeClothProxyMeshFile(string name, string fileName, bool backSolveJoints, bool driveMeshes, bool addBonesToRenderMesh = false,
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
    private static Dictionary<int, string> BuildProxyNodeNameMap(
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

    /// <summary>
    /// The vertices of an exported proxy that survive the import: faced, and not a pin whose face neighbours are all
    /// pinned.
    /// </summary>
    private static HashSet<int> SurvivingProxyVertices(FeModel.ProxyMesh proxy)
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

    private bool EmitProxySheetClothPhase(FeModel feModel, List<FeModel.BoneChain> boneChains, KVObject rootChildren)
    {
        var backSolveJoints = feModel.FitMatrixNodes.Count > 0 || feModel.DrivesRealBones;
        var independentChains = boneChains
            .Where(chain => !chain.Joints.Any(joint => feModel.ProxyFitMatrixNodes.Contains(joint.Node))
                && !feModel.IsSheetDrivenChain(chain))
            .ToList();
        var proxyNodeNames = BuildProxyNodeNameMap(ProxyMeshes);

        rootChildren.Add(MakeClothProxyMeshList(feModel, independentChains, backSolveJoints, proxyNodeNames));

        var (softbody, softbodyChildren) = MakeSoftbody(feModel);
        var surfaceRods = SurfaceRods(feModel);
        softbodyChildren.Add(MakeClothParams(feModel, surfaceRods.GeneratesBendRods, surfaceRods.GeneratesBendOnlyRods,
            surfaceRods.AddCurvature > 0f ? surfaceRods.AddCurvature : feModel.ChainRingCurvature, feModel.HasExplicitMasses));

        var nodes = ClassifySheetControlNodes(feModel, boneChains, independentChains);
        var authoredFaces = feModel.GetAuthoredElementFaces();
        if (independentChains.Count > 0 || nodes.LoneClothNodes.Count > 0 || nodes.LeftoverStaticNodes.Count > 0
            || nodes.UnregisteredNodes.Count > 0 || nodes.UnregisteredFreeNodes.Count > 0 || authoredFaces.Count > 0)
        {
            DeclareSheetClothFolder(feModel, softbodyChildren, independentChains, nodes, proxyNodeNames);
        }

        var authoredClothNodes = nodes.LoneClothNodes.Concat(nodes.LeftoverStaticNodes).Concat(nodes.UnregisteredNodes)
            .Select(static entry => entry.Node)
            .ToHashSet();
        AddClothProxySprings(softbodyChildren, feModel, ProxyMeshes, nodes.IndependentChainNodes,
            authoredClothNodes, nodes.FreeClothNodeNames, surfaceRods.Derived, proxyNodeNames);
        AddClothSourceSprings(softbodyChildren, feModel, independentChains);
        AddClothChainSurplusClusters(softbodyChildren, feModel, independentChains);
        AddClothChainVolumetricMaps(softbodyChildren, feModel, independentChains);

        var clothBones = ClothBoneNames(feModel);
        clothBones.UnionWith(nodes.ProxySkinnedBones);
        clothBones.UnionWith(independentChains.SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Name));
        clothBones.UnionWith(nodes.LoneClothNodes.Concat(nodes.LeftoverStaticNodes).Concat(nodes.UnregisteredNodes)
            .Select(static entry => entry.Name));
        clothBones.UnionWith(nodes.UnregisteredFreeNodes.Select(static entry => entry.RootBone));
        AddClothSelfCollisionClusters(softbodyChildren, feModel, clothBones);
        AddClothPhaseTail(feModel, rootChildren, softbody, softbodyChildren, clothBones, independentChains,
            antiTunnelCloth: ProxyMeshes.Select(static proxy => proxy.Name),
            jointLocks: (node, name) => !nodes.IndependentChainNodes.Contains(node) && !authoredClothNodes.Contains(node)
                && (feModel.FitMatrixNodes.Contains(node) || nodes.ProxySkinnedBones.Contains(name)),
            proxyNodeNames: proxyNodeNames);
        return true;
    }

    /// <summary>
    /// The <c>ClothProxyMeshList</c> declaring every exported sheet with its back-solve, border and render-bone keys,
    /// grouped under the <c>ClothVertexMap</c> each one stands for, then the unregistered selections and the grids.
    /// </summary>
    private KVObject MakeClothProxyMeshList(FeModel feModel, List<FeModel.BoneChain> independentChains, bool backSolveJoints,
        Dictionary<int, string> proxyNodeNames)
    {
        var chainDrivenBones = new HashSet<string>(
            independentChains.SelectMany(static chain => chain.Joints).Select(joint => feModel.CtrlNames[joint.Node]),
            StringComparer.OrdinalIgnoreCase);

        var positionDrivenBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = feModel.FirstPositionDrivenNode; i < feModel.CtrlNames.Length; i++)
        {
            if (!FeModel.IsProxyNodeName(feModel.CtrlNames[i]))
            {
                positionDrivenBones.Add(feModel.CtrlNames[i]);
            }
        }

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

        var proxyRenderBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bone in model?.Skeleton.Bones ?? [])
        {
            if (bone.IsProceduralCloth && FeModel.IsProxyNodeName(bone.Name))
            {
                proxyRenderBones.Add(bone.Name);
            }
        }

        bool ProxyAddsBonesToRenderMesh(FeModel.ProxyMesh proxy)
            => feModel.ProxyOwnsNodeBases(proxy)
            || (proxyRenderBones.Count > 0
                && Array.Exists(proxy.NodeIndices, node => feModel.IsProxyMeshNode(node)
                    && proxyRenderBones.Contains(feModel.CtrlNames[node])));

        var (clothProxyList, clothProxyChildren) = MakeListNode("ClothProxyMeshList");
        var proxyGroup = ProxyMeshes.ConvertAll(static entry => entry.Proxy);
        var vertexMapContainers = new Dictionary<string, KVObject>(StringComparer.Ordinal);
        foreach (var proxyFile in ProxyMeshes)
        {
            var proxyBackSolve = backSolveJoints && ProxyDrivesUnchainedBone(proxyFile.Proxy)
                && !feModel.IsUnbackSolvedProxyMesh(proxyFile.Proxy);
            var proxyDrivesMeshes = proxyBackSolve || feModel.ProxyFitsUndrivenBone(proxyFile.Proxy);
            var addsBonesToRenderMesh = ProxyAddsBonesToRenderMesh(proxyFile.Proxy);
            var proxyFlexes = ProxyFlexesClothBorders(feModel, proxyFile.Proxy, proxyBackSolve, addsBonesToRenderMesh);
            if (proxyFlexes)
            {
                flexedProxies.Add(proxyFile.Proxy);
            }

            var proxyNode = MakeClothProxyMeshFile(proxyFile.Name, proxyFile.FileName, proxyBackSolve,
                driveMeshes: proxyDrivesMeshes, addsBonesToRenderMesh,
                backSolveInfluenceThreshold: feModel.GetBackSolveInfluenceThreshold(proxyFile.Proxy),
                flexClothBorders: proxyFlexes);

            if (feModel.GetProxyVertexMapName(proxyFile.Proxy, proxyGroup) is { } proxyVertexMap)
            {
                if (!vertexMapContainers.TryGetValue(proxyVertexMap, out var mapChildren))
                {
                    var (mapNode, children) = MakeListNode("ClothVertexMap");
                    mapNode.Add("name", proxyVertexMap);
                    if (feModel.VertexMapAliases(proxyVertexMap) is { Count: > 1 } aliases)
                    {
                        mapNode.Add("aliases", string.Join(',', aliases));
                    }

                    AddClothVertexMapAttributes(mapNode, feModel, proxyVertexMap, proxyNodeNames);
                    if (feModel.UniformVertexMapWeight(proxyVertexMap) is { } mapWeight)
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

        foreach (var map in feModel.VertexMaps)
        {
            if (feModel.RegistersVertexSet(map.NameHash) || vertexMapContainers.ContainsKey(map.Name))
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

            var (unregisteredNode, _) = MakeListNode("ClothVertexMap");
            unregisteredNode.Add("name", map.Name);
            AddClothVertexMapAttributes(unregisteredNode, feModel, map.Name, proxyNodeNames);
            unregisteredNode.Add("data", MakeNodeTable(members));
            clothProxyChildren.Add(unregisteredNode);
        }

        AddDisabledChainGrids(clothProxyChildren);
        return clothProxyList;
    }

    /// <summary>The control nodes of a sheet phase that no exported sheet or independent chain recreates.</summary>
    private sealed record SheetControlNodes(
        HashSet<int> IndependentChainNodes,
        HashSet<string> ProxySkinnedBones,
        List<(string Name, int Node)> LoneClothNodes,
        List<(string Name, int Node)> LeftoverStaticNodes,
        List<(string Name, int Node)> UnregisteredNodes,
        List<(string RootBone, int Node, string ElementName, Vector3 Origin, Vector3 Angles)> UnregisteredFreeNodes,
        Dictionary<int, string> FreeClothNodeNames);

    /// <summary>
    /// Sorts the control nodes of a sheet phase into the simulated lone nodes, the static control bones, and the
    /// generated or fitted nodes no exported sheet or independent chain recreates.
    /// </summary>
    private SheetControlNodes ClassifySheetControlNodes(FeModel feModel, List<FeModel.BoneChain> boneChains,
        List<FeModel.BoneChain> independentChains)
    {
        var chainNodes = ChainJointNodes(boneChains);
        var independentChainNodes = ChainJointNodes(independentChains);
        var loneClothNodes = new List<(string Name, int Node)>();

        var boneByName = model?.Skeleton.Bones
            .GroupBy(static bone => bone.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.First(), StringComparer.OrdinalIgnoreCase);
        var shapeParentBones = CollisionShapeParentBones(feModel);
        var leftoverStaticNodes = new List<(string Name, int Node)>();

        var anchorOf = BuildCtrlAnchorMap(feModel);
        var jiggleNodes = feModel.JiggleBones.Select(static j => j.Node).ToHashSet();
        var proxyRegisteredNodes = new HashSet<int>();
        var proxySkinnedBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recoveredSkinnedBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, _, proxy) in ProxyMeshes)
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

        var unregisteredNodes = new List<(string Name, int Node)>();
        var unregisteredFreeNodes = new List<(string RootBone, int Node, string ElementName, Vector3 Origin, Vector3 Angles)>();
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

                if (name.StartsWith(FeModel.FreeClothNodePrefix, StringComparison.Ordinal))
                {
                    if (TryResolveClothNodeAnchor(feModel, anchorOf, node, out var rootBone, out var origin, out var angles))
                    {
                        var elementName = name[FeModel.FreeClothNodePrefix.Length..];
                        unregisteredFreeNodes.Add((rootBone, node, elementName, origin, angles));
                        freeClothNodeNames[node] = elementName;
                    }
                }
                else if (!FeModel.IsProxyNodeName(name))
                {
                    unregisteredNodes.Add((name, node));
                }

                continue;
            }

            if (IsDeclaredByItsJiggleBone(feModel, node))
            {
                continue;
            }

            if (node < feModel.NodeInvMasses.Length && feModel.NodeInvMasses[node] != 0f)
            {
                loneClothNodes.Add((name, node));
            }
            else if (!shapeParentBones.Contains(name)
                && !((recoveredSkinnedBones.Contains(name)
                        || (feModel.FitMatrixNodes.Count == 0 && proxySkinnedBones.Contains(name)))
                    && node < feModel.SkelParents.Length && feModel.SkelParents[node] < 0)
                && boneByName is not null && boneByName.TryGetValue(name, out var bone) && bone.IsClothControlNode)
            {
                leftoverStaticNodes.Add((name, node));
            }
        }

        return new SheetControlNodes(independentChainNodes, proxySkinnedBones, loneClothNodes, leftoverStaticNodes,
            unregisteredNodes, unregisteredFreeNodes, freeClothNodeNames);
    }

    /// <summary>
    /// Declares the independent chains, the classified control nodes and the authored faces in the sheet phase's cloth
    /// folder.
    /// </summary>
    private void DeclareSheetClothFolder(FeModel feModel, KVObject softbodyChildren, List<FeModel.BoneChain> independentChains,
        SheetControlNodes nodes, Dictionary<int, string> proxyNodeNames)
    {
        var clothFolderChildren = AddClothFolder(softbodyChildren);

        var loneJointChainCount = nodes.LoneClothNodes.Count(n => LoneClothNodeIsOriginalRoot(feModel, n.Node));
        var hasOtherChains = independentChains.Count + loneJointChainCount > 1;

        foreach (var boneChain in independentChains)
        {
            clothFolderChildren.Add(MakeClothChainNode(feModel, boneChain, hasOtherChains,
                relandedJoints: RelandedJoints));
            if (MakeClothChainRestatement(feModel, boneChain) is { } restated)
            {
                clothFolderChildren.Add(restated);
            }

            foreach (var second in MakeClothChainSecondDeclarations(feModel, boneChain,
                ClothChainVersion(feModel, boneChain, hasOtherChains)))
            {
                clothFolderChildren.Add(second);
            }
        }

        AddClothRigidCloudClusterLocks(softbodyChildren, feModel, independentChains);

        var folderFor = ClothVertexMapFolders(feModel, clothFolderChildren);

        foreach (var (name, node) in nodes.LoneClothNodes)
        {
            if (LoneClothNodeIsOriginalRoot(feModel, node))
            {
                clothFolderChildren.Add(MakeLoneJointChain(feModel, name, node, hasOtherChains));
            }
            else
            {
                folderFor(node, true).Add(MakeClothNode(feModel, name, node, proxyNodeNames: proxyNodeNames));
            }
        }

        foreach (var (name, node) in nodes.LeftoverStaticNodes)
        {
            folderFor(node, true).Add(MakeClothNode(feModel, name, node, isStaticNode: true,
                proxyNodeNames: proxyNodeNames));
        }

        foreach (var (name, node) in nodes.UnregisteredNodes)
        {
            clothFolderChildren.Add(MakeClothNode(feModel, name, node,
                isStaticNode: feModel.IsStatic(node), proxyNodeNames: proxyNodeNames));
        }

        foreach (var (rootBone, node, elementName, origin, angles) in nodes.UnregisteredFreeNodes)
        {
            clothFolderChildren.Add(MakeClothNode(feModel, rootBone, node,
                isStaticNode: feModel.IsStatic(node), elementName: elementName, origin: origin, angles: angles,
                proxyNodeNames: proxyNodeNames));
        }

        AddClothFaces(clothFolderChildren, feModel);
        AddClothStiffHinges(softbodyChildren, feModel);
    }
}
