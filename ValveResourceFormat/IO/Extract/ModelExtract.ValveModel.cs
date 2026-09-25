using System.IO;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
    /// <summary>
    /// The list nodes a model doc is assembled from. Each is created and appended to the root the first
    /// time a section writes into it, so a list nothing wrote is absent rather than empty.
    /// </summary>
    private sealed class ModelDocLists(KVObject rootChildren)
    {
        private readonly Dictionary<string, KVObject> lists = [];

        /// <summary>The root node's own children, for the nodes that are not themselves list entries.</summary>
        public KVObject RootChildren { get; } = rootChildren;

        public KVObject MaterialGroups => Get("MaterialGroupList");
        public KVObject RenderMeshes => Get("RenderMeshList");
        public KVObject BodyGroups => Get("BodyGroupList");
        public KVObject LodGroups => Get("LODGroupList");
        public KVObject Animations => Get("AnimationList");
        public KVObject PhysicsShapes => Get("PhysicsShapeList");
        public KVObject PhysicsBodyMarkup => Get("PhysicsBodyMarkupList");
        public KVObject PhysicsJoints => Get("PhysicsJointList");
        public KVObject Attachments => Get("AttachmentList");
        public KVObject Skeleton => Get("Skeleton");
        public KVObject ModelModifiers => Get("ModelModifierList");
        public KVObject WeightLists => Get("WeightListList");
        public KVObject ScaleSets => Get("ScaleSetList");
        public KVObject HitboxSets => Get("HitboxSetList");
        public KVObject PoseParams => Get("PoseParamList");
        public KVObject NmSkeletons => Get("NmSkeletonList");
        public KVObject AnimGraph2 => Get("AnimGraph2List");
        public KVObject Vsnaps => Get("VSNAPList");
        public KVObject BreakPieces => Get("BreakPieceList");
        public KVObject GameData => Get("GameDataList");

        /// <summary>Whether a section has already written into the list node of this class.</summary>
        public bool Has(string className) => lists.ContainsKey(className);

        private KVObject Get(string className)
        {
            if (!lists.TryGetValue(className, out var children))
            {
                var list = MakeListNode(className);
                RootChildren.Add(list.Node);
                lists[className] = children = list.Children;
            }

            return children;
        }
    }

    /// <summary>
    /// Converts the model to Valve model format as a string.
    /// </summary>
    public string ToValveModel()
    {
        var kv = KVObject.Collection();

        var root = MakeListNode("RootNode");
        kv.Add("rootNode", root.Node);

        var lists = new ModelDocLists(root.Children);

        var boneMarkupList = MakeListNode("BoneMarkupList");
        root.Children.Add(boneMarkupList.Node);
        boneMarkupList.Node.Add("bone_cull_type", "None");

        AddRenderMeshNodes(lists);
        AddMaterialGroupNodes(lists);
        AddSequenceMarkupNodes(lists, Sequences);
        AddAnimationNodes(lists, Sequences);
        AddPhysicsShapeFileNodes(lists);

        if (model != null)
        {
            ExtractModelKeyValues(model, lists, root.Node);
            AddHitboxSetNodes(model, lists);

            if (model.Skeleton.Roots.Length > 0)
            {
                AddBonesRecursive(model.Skeleton.Roots, lists.Skeleton);
            }

            if (Cloth.CulledBones.Count > 0)
            {
                Cloth.AddCulledClothBones(lists.Skeleton);
            }
        }

        AddPhysicsBodyNodes(lists);

        if (physAggregateData is not null && ClothExtract.ExtractJiggleBones(physAggregateData.FeModel) is { } jiggleBoneList)
        {
            root.Children.Add(jiggleBoneList);
        }

        var clothEmitted = physAggregateData?.FeModel is { } feModel && Cloth.EmitCloth(feModel, root.Children);

        // A soft-body FeModel that yields no authorable cloth gets a minimal placeholder PhysicsShapeList,
        // which is what makes the compiler allocate a PHYS block and the CTRL embedded_physics reference.
        if (physAggregateData?.FeModel is not null
            && !clothEmitted
            && !lists.Has("PhysicsShapeList")
            && model?.Resource?.GetBlockByType(BlockType.PHYS) is not null
            && model.Skeleton.Bones.Length > 0)
        {
            lists.PhysicsShapes.Add(MakeNode("PhysicsShapeSphere",
                ("parent_bone", GetExportBoneName(model.Skeleton.Bones[0])),
                ("surface_prop", "default"),
                ("collision_tags", "solid"),
                ("radius", 1.0f),
                ("center", ToKVArray(Vector3.Zero)),
                ("name", "vrf_phys_transplant_placeholder")
            ));
        }

        if (Translation != Vector3.Zero)
        {
            lists.ModelModifiers.Add(MakeNode("ModelModifier_Translate", ("translation", ToKVArray(Translation))));
        }

        AddVsnapNodes(lists);

        return kv.ToKV3String(format: KV3IDLookup.Get("modeldoc28"));
    }
}
