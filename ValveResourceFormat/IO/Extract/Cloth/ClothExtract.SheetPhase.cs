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
        // A chain fitted over a proxy sheet's vertices is driven by the sheet; one fitted over its own ring is emitted.
        var independentChains = boneChains
            .Where(chain => !chain.Joints.Any(joint => feModel.ProxyFitMatrixNodes.Contains(joint.Node))
                && !feModel.IsSheetDrivenChain(chain))
            .ToList();

        // A proxy back-solves only where it drives a bone no independent chain covers.
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

        // A compile that does not state its position-driven boundary reads every carried bone.
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

        // add_bones_to_render_mesh shows as node bases on the sheet's own vertices, or as procedural cloth bones named by
        // them.
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
            // A sheet whose driven bones another sheet already fits was compiled with its own back-solve off.
            var proxyBackSolve = backSolveJoints && ProxyDrivesUnchainedBone(proxyFile.Proxy)
                && !feModel.IsUnbackSolvedProxyMesh(proxyFile.Proxy);
            // back_solve_joints_drive_meshes is also stated alone, on a sheet fitting a bone it leaves undriven.
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

            // A selection covering exactly the sheet's simulated nodes is a ClothVertexMap around the sheet.
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

                    AddClothVertexMapAttributes(mapNode, feModel, proxyVertexMap,
                        BuildProxyNodeNameMap(ProxyMeshes));
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

        // A selection the original does not register as a vertex set is declared as a container listing its sheet
        // vertices, since painting it would register it.
        var sheetNodeNames = BuildProxyNodeNameMap(ProxyMeshes);
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
                if (weight <= 0f || !sheetNodeNames.TryGetValue(node, out var memberName))
                {
                    continue;
                }

                if (weight >= 1f)
                {
                    members.Add(memberName, true);
                }
                else
                {
                    var member = KVObject.Collection();
                    member.Add("weight", weight);
                    members.Add(memberName, member);
                }

                listed++;
            }

            if (listed == 0)
            {
                continue;
            }

            var (unregisteredNode, _) = MakeListNode("ClothVertexMap");
            unregisteredNode.Add("name", map.Name);
            AddClothVertexMapAttributes(unregisteredNode, feModel, map.Name, sheetNodeNames);
            var unregisteredData = KVObject.Collection();
            unregisteredData.Add("nodes", members);
            unregisteredNode.Add("data", unregisteredData);
            clothProxyChildren.Add(unregisteredNode);
        }

        foreach (var clothGrid in ChainGrids)
        {
            var gridNode = MakeClothProxyMeshFile(clothGrid.Name, clothGrid.FileName, backSolveJoints: false, driveMeshes: true);
            gridNode.Add("disabled", true);
            clothProxyChildren.Add(gridNode);
        }

        rootChildren.Add(clothProxyList);

        var (softbody, softbodyChildren) = MakeListNode("Softbody");
        AddSoftbodyAttributes(softbody, feModel);
        var surfaceRods = ClothRodsFromSurface(feModel, ProxyMeshes,
            out var generatesBendRods, out var generatesBendOnlyRods, out var addCurvature, out _, out _,
            out _);
        softbodyChildren.Add(MakeClothParams(feModel, generatesBendRods, generatesBendOnlyRods,
            addCurvature > 0f ? addCurvature : feModel.ChainRingCurvature, feModel.HasExplicitMasses));

        var chainNodes = boneChains.SelectMany(static chain => chain.Joints).Select(static joint => joint.Node).ToHashSet();
        var independentChainNodes = independentChains
            .SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Node)
            .ToHashSet();
        var loneClothNodes = new List<(string Name, int Node)>();

        var boneByName = model?.Skeleton.Bones
            .GroupBy(static bone => bone.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.First(), StringComparer.OrdinalIgnoreCase);
        var shapeParentBones = CollisionShapeParentBones(feModel);
        var leftoverStaticNodes = new List<(string Name, int Node)>();

        // A generated or fitted node no emitted proxy recreates is declared on its own.
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

        // Emitted flat: a ClothVertexMap around a free ClothNode a spring names does not compile.
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
            // A bone a surviving proxy vertex is skinned to is registered by the sheet unless the original records it
            // as a root and the skinning is recovered rather than synthesised.
            else if (!shapeParentBones.Contains(name)
                && !((recoveredSkinnedBones.Contains(name)
                        || (feModel.FitMatrixNodes.Count == 0 && proxySkinnedBones.Contains(name)))
                    && node < feModel.SkelParents.Length && feModel.SkelParents[node] < 0)
                && boneByName is not null && boneByName.TryGetValue(name, out var bone) && bone.IsClothControlNode)
            {
                leftoverStaticNodes.Add((name, node));
            }
        }

        var proxyNodeNameMap = BuildProxyNodeNameMap(ProxyMeshes);

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

            foreach (var (name, node) in unregisteredNodes)
            {
                clothFolderChildren.Add(MakeClothNode(feModel, name, node,
                    isStaticNode: feModel.IsStatic(node), proxyNodeNames: proxyNodeNameMap));
            }

            foreach (var (rootBone, node, elementName, origin, angles) in unregisteredFreeNodes)
            {
                clothFolderChildren.Add(MakeClothNode(feModel, rootBone, node,
                    isStaticNode: feModel.IsStatic(node), elementName: elementName, origin: origin, angles: angles,
                    proxyNodeNames: proxyNodeNameMap));
            }

            AddClothFaces(clothFolderChildren, feModel);
            AddClothStiffHinges(softbodyChildren, feModel);
        }

        var authoredClothNodes = loneClothNodes.Concat(leftoverStaticNodes).Concat(unregisteredNodes)
            .Select(static entry => entry.Node)
            .ToHashSet();
        AddClothProxySprings(softbodyChildren, feModel, ProxyMeshes, independentChainNodes,
            authoredClothNodes, freeClothNodeNames, surfaceRods, proxyNodeNameMap);
        AddClothSourceSprings(softbodyChildren, feModel, independentChains);
        AddClothChainSurplusClusters(softbodyChildren, feModel, independentChains);
        AddClothChainVolumetricMaps(softbodyChildren, feModel, independentChains);

        var clothBones = ClothBoneNames(feModel);
        clothBones.UnionWith(proxySkinnedBones);
        clothBones.UnionWith(independentChains.SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Name));
        clothBones.UnionWith(loneClothNodes.Concat(leftoverStaticNodes).Concat(unregisteredNodes)
            .Select(static entry => entry.Name));
        clothBones.UnionWith(unregisteredFreeNodes.Select(static entry => entry.RootBone));
        AddClothSelfCollisionClusters(softbodyChildren, feModel, clothBones);
        AddClothFollowBones(softbodyChildren, feModel, clothBones);
        AddClothJointLocks(softbodyChildren, feModel, (node, name) => !independentChainNodes.Contains(node)
            && !authoredClothNodes.Contains(node) && (feModel.FitMatrixNodes.Contains(node) || proxySkinnedBones.Contains(name)));
        var shapeNames = AddClothCollisionShapes(softbodyChildren, feModel);
        AddClothAntiTunnelGroup(softbodyChildren, feModel, shapeNames,
            [.. ProxyMeshes.Select(static proxy => proxy.Name)]);
        AddClothEffects(softbodyChildren, feModel, AvailableVertexMaps(feModel, independentChains));
        AddShapeParentDefaultClothNodes(softbodyChildren, feModel);

        rootChildren.Add(softbody);
        AddClothAntiTunnelProbes(rootChildren, feModel, proxyNodeNameMap);

        return true;
    }
}
