using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>
    /// Whether a model with jiggle bones was authored with a <c>Softbody</c> holding a <c>ClothParams</c> even though it
    /// has no cloth node of its own. The jiggle bones compile the same either way, and the ClothParams leaves only its
    /// iteration counts behind: a model compiled without one ships them as zero.
    /// </summary>
    internal static bool HasJiggleBoneClothParams(FeModel feModel)
        => feModel.JiggleBones.Length > 0
            && (feModel.ExtraIterations != 0 || feModel.ExtraGoalIterations != 0 || feModel.ExtraPressureIterations != 0);

    private bool EmitFreeNodeClothPhase(FeModel feModel, List<FeModel.BoneChain> boneChains, KVObject rootChildren)
    {
        // No sheet and no chains: cloth built purely from free-standing ClothNodes (and the
        // ClothSprings wiring them), e.g. the "$cloth_node_*" minimal rigs and lone goal-driven
        // bones. A FeModel that yields no authorable node here (jiggle-bone users, weapon-offset
        // rigs) emits nothing and falls through to the PHYS transplant placeholder below.
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

        // Every ctrl of a collision-shape-only model is a shape parent bone, which the loop above
        // skips, so gating on the node count alone drops the shapes with the rest of the Softbody.
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
