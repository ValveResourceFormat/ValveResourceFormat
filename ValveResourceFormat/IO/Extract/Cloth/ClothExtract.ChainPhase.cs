using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    private void EmitChainClothPhase(ClothReconstruction cloth, List<BoneChain> boneChains, KVObject rootChildren)
    {
        var (softbody, softbodyChildren) = MakeSoftbody(cloth);
        softbodyChildren.Add(MakeClothParams(cloth,
            generatesBendRods: cloth.HasChainStiffnessRods(boneChains),
            generatesBendOnlyRods: cloth.HasChainBendOnlyRods(boneChains),
            addCurvature: cloth.ChainRingCurvature));
        var clothFolderChildren = AddClothFolder(softbodyChildren);
        var strip = AddImportedStrip(clothFolderChildren, cloth);

        foreach (var jointNode in ChainJointClothNodes(cloth, boneChains))
        {
            clothFolderChildren.Add(jointNode);
        }

        var declarationPlan = TryPlanClothChainDeclarations(cloth, boneChains, ClothControlParentTest(cloth));
        var declaredChains = declarationPlan?.Chains ?? boneChains;
        foreach (var (name, node) in declarationPlan?.PreDeclared ?? [])
        {
            clothFolderChildren.Add(MakeClothChainJointDeclaration(cloth, name, node));
        }

        foreach (var boneChain in declaredChains)
        {
            AddClothChainDeclarations(clothFolderChildren, cloth, boneChain,
                declarationPlan?.Walk.GetValueOrDefault(boneChain));
        }

        AddClothFaces(clothFolderChildren, cloth);
        var sourceSprings = AddClothSourceSprings(softbodyChildren, cloth, boneChains);
        sourceSprings.UnionWith(AddClothChainSurplusRods(softbodyChildren, cloth, boneChains));
        sourceSprings.UnionWith(AddClothChainCrossLinkSprings(softbodyChildren, cloth, boneChains));

        var chainCoveredNodes = boneChains.SelectMany(static chain => chain.Joints)
            .Where(joint => !cloth.SiblingSpringHubs.Contains(joint.Name))
            .Select(static joint => joint.Node)
            .ToHashSet();
        var clothBones = ClothBoneNames(cloth);
        clothBones.UnionWith(boneChains.SelectMany(static chain => chain.Joints).Select(static joint => joint.Name));
        chainCoveredNodes.UnionWith(strip);
        clothBones.UnionWith(ImportedStripBoneNames(cloth, strip));
        chainCoveredNodes.UnionWith(AddClothSelfCollisionClusters(softbodyChildren, cloth, clothBones));
        var clothControlBones = model?.Skeleton.Bones
            .Where(static b => b.IsClothControlNode)
            .Select(static b => b.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chainSurface = cloth.Index.HasSurfaceElements;
        AddFreeClothNodesAndSprings(clothFolderChildren, softbodyChildren, cloth, chainCoveredNodes,
            name => chainSurface || (clothControlBones?.Contains(name) ?? false),
            clothBones, ClothVertexMapFolders(cloth, clothFolderChildren),
            ClothControlAncestorTest(cloth), sourceSprings,
            chainJoints: ChainJointNodes(boneChains));
        AddClothStiffHinges(softbodyChildren, cloth);
        AddClothRigidCloudClusterLocks(softbodyChildren, cloth, declaredChains);
        AddClothChainVolumetricMaps(softbodyChildren, cloth, boneChains);
        AddClothPhaseTail(cloth, rootChildren, softbody, softbodyChildren, clothBones, boneChains,
            antiTunnelCloth: declaredChains.Select(static chain => chain.RootBone + chain.DeclarationSuffix));
    }
}
