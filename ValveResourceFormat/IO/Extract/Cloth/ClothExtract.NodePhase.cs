using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>
    /// Whether a model with jiggle bones and no cloth node was authored with a <c>ClothParams</c>, which leaves non-zero
    /// iteration counts behind.
    /// </summary>
    internal static bool HasJiggleBoneClothParams(FeModel feModel)
        => feModel.JiggleBones.Length > 0
            && (feModel.ExtraIterations != 0 || feModel.ExtraGoalIterations != 0 || feModel.ExtraPressureIterations != 0);

    private bool EmitFreeNodeClothPhase(FeModel feModel, List<FeModel.BoneChain> boneChains, KVObject rootChildren)
    {
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

        // A model of collision shapes alone declares no node, but its shapes still need the Softbody.
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
