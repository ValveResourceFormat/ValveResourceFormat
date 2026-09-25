using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    private bool EmitChainClothPhase(FeModel feModel, List<FeModel.BoneChain> boneChains, KVObject rootChildren)
    {
        var (softbody, softbodyChildren) = MakeSoftbody(feModel);
        softbodyChildren.Add(MakeClothParams(feModel,
            generatesBendRods: feModel.HasChainStiffnessRods(boneChains),
            generatesBendOnlyRods: feModel.HasChainBendOnlyRods(boneChains),
            addCurvature: feModel.ChainRingCurvature,
            explicitMasses: feModel.HasExplicitMasses));
        var clothFolderChildren = AddClothFolder(softbodyChildren);
        var strip = AddImportedStrip(clothFolderChildren, feModel);

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
            clothFolderChildren.Add(MakeClothChainNode(feModel, boneChain, walk, RelandedJoints));
            if (MakeClothChainRestatement(feModel, boneChain) is { } restated)
            {
                clothFolderChildren.Add(restated);
            }

            foreach (var second in MakeClothChainSecondDeclarations(feModel, boneChain,
                ClothChainVersion(feModel, boneChain)))
            {
                clothFolderChildren.Add(second);
            }
        }

        AddDisabledChainGrids(clothFolderChildren);
        AddClothFaces(clothFolderChildren, feModel);
        var sourceSprings = AddClothSourceSprings(softbodyChildren, feModel, boneChains);
        sourceSprings.UnionWith(AddClothChainSurplusRods(softbodyChildren, feModel, boneChains));

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
        var clothControlBones = model?.Skeleton.Bones
            .Where(static b => b.IsClothControlNode)
            .Select(static b => b.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chainSurface = feModel.HasSurfaceElements;
        AddFreeClothNodesAndSprings(clothFolderChildren, softbodyChildren, feModel, chainCoveredNodes,
            name => chainSurface || (clothControlBones?.Contains(name) ?? false),
            clothBones, ClothVertexMapFolders(feModel, clothFolderChildren),
            ClothControlAncestorTest(feModel), sourceSprings,
            chainJoints: ChainJointNodes(boneChains));
        AddClothStiffHinges(softbodyChildren, feModel);
        AddClothRigidCloudClusterLocks(softbodyChildren, feModel, declaredChains);
        AddClothChainVolumetricMaps(softbodyChildren, feModel, boneChains);
        AddClothPhaseTail(feModel, rootChildren, softbody, softbodyChildren, clothBones, boneChains,
            antiTunnelCloth: declaredChains.Select(static chain => chain.RootBone + chain.DeclarationSuffix));
        return true;
    }
}
