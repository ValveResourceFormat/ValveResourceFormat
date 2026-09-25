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
        var (softbody, softbodyChildren) = MakeSoftbody(feModel);
        softbodyChildren.Add(MakeClothParams(feModel, explicitMasses: feModel.HasExplicitMasses));
        var clothFolderChildren = AddClothFolder(softbodyChildren);
        var strip = AddImportedStrip(clothFolderChildren, feModel);

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
        if (freeNodes == 0 && strip.Count == 0 && CollisionShapeParentBones(feModel).Count == 0 && !HasJiggleBoneClothParams(feModel))
        {
            return false;
        }

        AddClothPhaseTail(feModel, rootChildren, softbody, softbodyChildren, clothBones, boneChains);
        return true;
    }
}
