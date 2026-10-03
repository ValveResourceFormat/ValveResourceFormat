using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>
    /// Whether a model with jiggle bones and no cloth node was authored with a <c>ClothParams</c>, which leaves non-zero
    /// iteration counts behind.
    /// </summary>
    internal static bool HasJiggleBoneClothParams(ClothReconstruction cloth)
        => cloth.Fe.JiggleBones.Length > 0
            && (cloth.Fe.ExtraIterations != 0 || cloth.Fe.ExtraGoalIterations != 0 || cloth.Fe.ExtraPressureIterations != 0);

    private bool EmitFreeNodeClothPhase(ClothReconstruction cloth, List<BoneChain> boneChains, KVObject rootChildren)
    {
        var (softbody, softbodyChildren) = MakeSoftbody(cloth);
        softbodyChildren.Add(MakeClothParams(cloth));
        var clothFolderChildren = AddClothFolder(softbodyChildren);
        var strip = AddImportedStrip(clothFolderChildren, cloth);

        var clothBones = ClothBoneNames(cloth);
        clothBones.UnionWith(ImportedStripBoneNames(cloth, strip));
        var clustered = AddClothSelfCollisionClusters(softbodyChildren, cloth, clothBones);
        clustered.UnionWith(strip);
        var freeNodes = AddFreeClothNodesAndSprings(clothFolderChildren, softbodyChildren, cloth,
            clustered, static _ => true, clothBones,
            ClothVertexMapFolders(cloth, clothFolderChildren),
            bareStaticReparented: ClothControlAncestorTest(cloth));
        AddClothFaces(clothFolderChildren, cloth);
        AddClothStiffHinges(softbodyChildren, cloth);

        if (freeNodes == 0 && strip.Count == 0 && cloth.CollisionShapes.ParentBones.Count == 0 && !HasJiggleBoneClothParams(cloth))
        {
            return false;
        }

        AddClothPhaseTail(cloth, rootChildren, softbody, softbodyChildren, clothBones, boneChains);
        return true;
    }
}
