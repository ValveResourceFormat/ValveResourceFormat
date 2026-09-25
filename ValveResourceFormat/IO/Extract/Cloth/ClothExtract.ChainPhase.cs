using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    private bool EmitChainClothPhase(FeModel feModel, List<FeModel.BoneChain> boneChains, KVObject rootChildren)
    {
        // Phase 1 fallback (no recoverable sheet): bone-chain cloth, plus a GENERATED sheet grid
        // over each group of neighbouring chains (skirts/capes). The grid mirrors hand-authored
        // item proxies: with back_solve_joints=false the chains keep simulating the bones while
        // the sheet simulates the surface between them and drives the render mesh directly.
        var (softbody, softbodyChildren) = MakeListNode("Softbody");
        AddSoftbodyAttributes(softbody, feModel);
        softbodyChildren.Add(MakeClothParams(feModel,
            generatesBendRods: feModel.HasChainStiffnessRods(boneChains),
            generatesBendOnlyRods: feModel.HasChainBendOnlyRods(boneChains),
            addCurvature: feModel.ChainRingCurvature,
            explicitMasses: feModel.HasExplicitMasses));
        var (clothFolder, clothFolderChildren) = MakeListNode("Folder");
        clothFolder.Add("name", "cloth");
        softbodyChildren.Add(clothFolder);

        var strip = feModel.ImportedStripNodes;
        if (strip.Count > 0)
        {
            clothFolderChildren.Add(MakeImportedCloth(feModel, strip));
        }

        var hasOtherChains = boneChains.Count > 1;

        // The compiled node order is (block, constraint rank, creation index), so inside a band it IS the
        // order the control nodes were created in. A chain creates each joint immediately followed by its
        // own ring nodes, so a joint the band order separates from its rings was created before the chain
        // ran - by an earlier declaration of the same bone name, which the chain then reuses.
        foreach (var jointNode in ChainJointClothNodes(feModel, boneChains))
        {
            clothFolderChildren.Add(jointNode);
        }

        var declarationPlan = TryPlanClothChainDeclarations(feModel, boneChains,
            ClothControlParentTest(feModel));
        var declaredChains = declarationPlan?.Chains ?? boneChains;
        foreach (var (name, node) in declarationPlan?.PreDeclared ?? [])
        {
            clothFolderChildren.Add(MakeClothChainJointDeclaration(feModel, name, node));
        }

        foreach (var boneChain in declaredChains)
        {
            var walk = declarationPlan is not null
                && declarationPlan.Walk.TryGetValue(boneChain, out var found)
                ? found
                : null;
            clothFolderChildren.Add(MakeClothChainNode(feModel, boneChain, hasOtherChains, walk, RelandedJoints));
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

        foreach (var clothGrid in ChainGrids)
        {
            // The grid ships DISABLED: the chains alone reproduce the original physics, and with
            // drive_meshes the sheet would fight the chain-driven skinning of the same region.
            // It is a ready-made starting sheet the author can enable/retarget in ModelDoc
            // (like hand-authored cape proxies that drive otherwise boneless render regions).
            var gridNode = MakeClothProxyMeshFile(clothGrid.Name, clothGrid.FileName, backSolveJoints: false, driveMeshes: true);
            gridNode.Add("disabled", true);
            clothFolderChildren.Add(gridNode);
        }

        AddClothFaces(clothFolderChildren, feModel);
        var sourceSprings = AddClothSourceSprings(softbodyChildren, feModel, boneChains);
        sourceSprings.UnionWith(AddClothChainSurplusRods(softbodyChildren, feModel, boneChains));

        // A sibling hub is declared for its spring alone and anchors no chain of its own, so it keeps the
        // bare ClothNode every static control node the chains do not claim is declared as - which is what
        // the cloth nodes parented to it resolve through.
        var chainCoveredNodes = boneChains.SelectMany(static chain => chain.Joints)
            .Where(joint => !feModel.SiblingSpringHubs.Contains(joint.Name))
            .Select(static joint => joint.Node)
            .ToHashSet();
        var clothBones = ClothBoneNames(feModel);
        clothBones.UnionWith(boneChains.SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Name));
        chainCoveredNodes.UnionWith(strip);
        clothBones.UnionWith(ImportedStripBoneNames(feModel, strip));
        chainCoveredNodes.UnionWith(AddClothSelfCollisionClusters(softbodyChildren, feModel, clothBones));
        // A static control node no chain, shape or jiggle bone claims is recreated by nothing else in
        // this phase, so it is declared as a bare ClothNode wherever the compiled skeleton records the
        // bone as a cloth control node - the same evidence the proxy-sheet phase reads.
        var clothControlBones = model?.Skeleton.Bones
            .Where(static b => b.IsClothControlNode)
            .Select(static b => b.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chainSurface = feModel.Quads.Length > 0 || feModel.Tris.Length > 0;
        AddFreeClothNodesAndSprings(clothFolderChildren, softbodyChildren, feModel, chainCoveredNodes,
            name => chainSurface || (clothControlBones?.Contains(name) ?? false),
            clothBones, ClothVertexMapFolders(feModel, clothFolderChildren), hasOtherChains: true,
            ClothControlAncestorTest(feModel), sourceSprings,
            chainJoints: [.. boneChains.SelectMany(static chain => chain.Joints).Select(static joint => joint.Node)]);
        AddClothStiffHinges(softbodyChildren, feModel);
        AddClothRigidCloudClusterLocks(softbodyChildren, feModel, declaredChains);
        AddClothChainVolumetricMaps(softbodyChildren, feModel, boneChains);

        AddClothFollowBones(softbodyChildren, feModel, clothBones);
        var shapeNames = AddClothCollisionShapes(softbodyChildren, feModel);
        AddClothAntiTunnelGroup(softbodyChildren, feModel, shapeNames,
            [.. declaredChains.Select(static chain => chain.RootBone + chain.DeclarationSuffix)]);
        AddClothEffects(softbodyChildren, feModel, AvailableVertexMaps(feModel, boneChains));
        AddShapeParentDefaultClothNodes(softbodyChildren, feModel);
        rootChildren.Add(softbody);
        AddClothAntiTunnelProbes(rootChildren, feModel, proxyNodeNames: null);
        return true;
    }
}
