using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

/// <summary>
/// Rebuilds the model doc nodes for the hit detection markup: the hitbox sets a model was authored with
/// and the shapes in them.
/// </summary>
partial class ModelExtract
{

    private static KVObject GetHitboxNode(Hitbox hitbox)
    {
        var node = hitbox.ShapeType switch
        {
            Hitbox.HitboxShape.Box => MakeNode("Hitbox",
                ("hitbox_mins", ToKVArray(hitbox.MinBounds)),
                ("hitbox_maxs", ToKVArray(hitbox.MaxBounds))
            ),
            Hitbox.HitboxShape.Capsule => MakeNode("HitboxCapsule",
                ("radius", hitbox.ShapeRadius),
                ("point0", ToKVArray(hitbox.MinBounds)),
                ("point1", ToKVArray(hitbox.MaxBounds))
            ),
            Hitbox.HitboxShape.Sphere => MakeNode("HitboxSphere",
                ("center", ToKVArray(hitbox.MinBounds)),
                ("radius", hitbox.ShapeRadius)
            ),
            _ => throw new NotImplementedException($"Unknown hitbox shape type: {hitbox.ShapeType}")
        };

        node.Add("name", hitbox.Name);
        node.Add("parent_bone", hitbox.BoneName);
        node.Add("surface_property", hitbox.SurfaceProperty);
        node.Add("translation_only", hitbox.TranslationOnly);
        node.Add("group_id", hitbox.GroupId);

        return node;
    }

    private static void AddHitboxSetNodes(Model model, ModelDocLists lists)
    {
        if (model.HitboxSets == null)
        {
            return;
        }

        foreach (var pair in model.HitboxSets)
        {
            var children = KVObject.Array();
            var hitboxSet = MakeNode("HitboxSet", ("name", pair.Key), ("children", children));

            foreach (var hitbox in pair.Value)
            {
                var hitboxNode = GetHitboxNode(hitbox);
                children.Add(hitboxNode);
            }

            lists.HitboxSets.Add(hitboxSet);
        }
    }
}
