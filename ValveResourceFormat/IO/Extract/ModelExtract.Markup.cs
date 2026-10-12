using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

/// <summary>
/// Rebuilds the weapon sticker and keychain markup nodes.
/// </summary>
partial class ModelExtract
{
    /// <summary>
    /// Compiled sticker offsets and polygon vertices are the authored positions divided by this, and the compiled
    /// scale is this divided by the authored size.
    /// </summary>
    private const float StickerUnitsPerUv = 32f;

    private void AddStickerAndKeychainNodes(KVObject keyvalues, ModelDocLists lists)
    {
        if (keyvalues.GetArray("StickerMarkup") is { Count: > 0 } stickers)
        {
            foreach (var group in stickers.GroupBy(sticker => sticker.GetStringProperty("Mesh", string.Empty)))
            {
                var containers = group.Select(BuildStickerContainer);
                lists.GameData.Add(MakeNode("StickerMarkup",
                    ("render_mesh_name", group.Key),
                    ("disabled", true),
                    ("children", MakeArray(containers))
                ));
            }
        }

        if (keyvalues.GetArray("KeychainMarkup") is { Count: > 0 } keychains)
        {
            foreach (var group in keychains.GroupBy(keychain => keychain.GetBooleanProperty("LegacyModel")))
            {
                var node = MakeNode("KeychainMarkup");

                if (GetKeychainMeshName(legacy: group.Key) is { } meshName)
                {
                    node.Add("render_mesh_name", meshName);
                }

                node.Add("disabled", true);
                node.Add("children", MakeArray(group.Select(BuildKeychainLocator)));
                lists.GameData.Add(node);
            }
        }
    }

    private static KVObject BuildStickerContainer(KVObject sticker)
    {
        var offset = sticker.GetFloatArray("Offset") is [var x, var y, ..] ? new Vector2(x, y) : Vector2.Zero;
        var scale = sticker.GetFloatProperty("Scale", 1f);
        var size = scale != 0f ? StickerUnitsPerUv / scale : 0f;

        var locator = MakeNode("StickerMarkupLocator",
            ("position", MakeArray(offset.X * StickerUnitsPerUv, -offset.Y * StickerUnitsPerUv, 0f)),
            ("dimensions", MakeArray(size, size, size)),
            ("angles", MakeArray(0f, sticker.GetFloatProperty("Rotation") * 360f, 0f)),
            ("sticker_index", sticker.GetInt32Property("Index")),
            ("deprecated", sticker.GetBooleanProperty("Deprecated")),
            ("special_identifier", sticker.GetStringProperty("SpecialIdentifier", string.Empty))
        );

        AddIfPresent(locator, "uicamerarotation", sticker, "UICameraRotation");

        var children = KVObject.Array();
        children.Add(locator);

        foreach (var polygon in sticker.GetArray("Polygons") ?? [])
        {
            var vertices = polygon.GetFloatArray("Vertices");

            for (var i = 0; i + 5 < vertices.Length; i += 6)
            {
                children.Add(MakeNode("StickerMarkupPolygon", ("children", MakeArray(Enumerable.Range(0, 3).Select(corner =>
                    MakeNode("StickerMarkupPolygonNode",
                        ("position", MakeArray(vertices[i + corner * 2] * StickerUnitsPerUv, -vertices[i + corner * 2 + 1] * StickerUnitsPerUv, 0f))))))));
            }
        }

        return MakeNode("StickerMarkupContainer", ("children", children));
    }

    private KVObject BuildKeychainLocator(KVObject keychain)
    {
        var corners = keychain.GetFloatArray("Corners");
        var boneName = keychain.GetStringProperty("BoneName", string.Empty);
        var boneIndex = model?.Skeleton.GetBoneIndex(boneName) ?? -1;

        var locator = MakeNode("KeychainMarkupLocator");

        for (var corner = 0; corner < 4; corner++)
        {
            locator.Add(FormattableString.Invariant($"corner{corner}"), MakeArray(corners[corner * 3], corners[corner * 3 + 1], corners[corner * 3 + 2]));
            locator.Add(FormattableString.Invariant($"bone_index{corner}"), boneIndex);
        }

        locator.Add("bone_name", boneName);
        locator.Add("default_location", keychain.GetBooleanProperty("DefaultLocation"));

        return locator;
    }

    /// <summary>
    /// Gets the render mesh a keychain markup sits on. The compiler only keeps whether that mesh is a legacy one,
    /// which it reads from the mesh name.
    /// </summary>
    private string? GetKeychainMeshName(bool legacy)
    {
        foreach (var renderMesh in RenderMeshesToExtract)
        {
            if (renderMesh.Name.Contains("_legacy", StringComparison.OrdinalIgnoreCase) == legacy)
            {
                return renderMesh.Name;
            }
        }

        return null;
    }
}
