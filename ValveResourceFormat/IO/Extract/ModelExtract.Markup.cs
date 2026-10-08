using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

/// <summary>
/// Rebuilds the weapon sticker and keychain markup nodes.
/// </summary>
/// <remarks>
/// The compiler hands enabled sticker and keychain markup, like <c>chicken_metadata</c>, to a <c>vgcxdata</c> child
/// compile that no public compiler provides, which fails the whole model. These nodes are therefore written disabled.
/// </remarks>
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
                var locators = group.Select(BuildKeychainLocator);
                lists.GameData.Add(MakeNode("KeychainMarkup",
                    ("render_mesh_name", GetKeychainMeshName(legacy: group.Key)),
                    ("disabled", true),
                    ("children", MakeArray(locators))
                ));
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

        if (sticker.ContainsKey("UICameraRotation"))
        {
            locator.Add("uicamerarotation", sticker["UICameraRotation"]);
        }

        var children = KVObject.Array();
        children.Add(locator);

        foreach (var polygon in sticker.GetArray("Polygons") ?? [])
        {
            foreach (var outline in GetStickerPolygonOutlines(polygon.GetFloatArray("Vertices")))
            {
                children.Add(MakeNode("StickerMarkupPolygon", ("children", MakeArray(outline.Select(point =>
                    MakeNode("StickerMarkupPolygonNode",
                        ("position", MakeArray(point.X * StickerUnitsPerUv, -point.Y * StickerUnitsPerUv, 0f))))))));
            }
        }

        return MakeNode("StickerMarkupContainer", ("children", children));
    }

    /// <summary>
    /// Recovers the polygon outlines from the triangles the compiler cut them into. A triangle set whose boundary does
    /// not form a single loop is returned as one outline per triangle.
    /// </summary>
    private static List<List<Vector2>> GetStickerPolygonOutlines(float[] vertices)
    {
        var triangles = new List<(Vector2 A, Vector2 B, Vector2 C)>();

        for (var i = 0; i + 5 < vertices.Length; i += 6)
        {
            triangles.Add((new(vertices[i], vertices[i + 1]), new(vertices[i + 2], vertices[i + 3]), new(vertices[i + 4], vertices[i + 5])));
        }

        var edgeCounts = new Dictionary<(Vector2, Vector2), int>();

        foreach (var (a, b, c) in triangles)
        {
            foreach (var (from, to) in (ReadOnlySpan<(Vector2, Vector2)>)[(a, b), (b, c), (c, a)])
            {
                var key = Compare(from, to) < 0 ? (from, to) : (to, from);
                edgeCounts[key] = edgeCounts.GetValueOrDefault(key) + 1;
            }
        }

        var next = new Dictionary<Vector2, Vector2>();
        var isLoop = true;

        foreach (var (a, b, c) in triangles)
        {
            foreach (var (from, to) in (ReadOnlySpan<(Vector2, Vector2)>)[(a, b), (b, c), (c, a)])
            {
                var key = Compare(from, to) < 0 ? (from, to) : (to, from);

                if (edgeCounts[key] == 1 && !next.TryAdd(from, to))
                {
                    isLoop = false;
                }
            }
        }

        if (isLoop && next.Count >= 3)
        {
            var outline = new List<Vector2>();
            var start = next.Keys.First();
            var current = start;

            do
            {
                outline.Add(current);

                if (!next.TryGetValue(current, out current) || outline.Count > next.Count)
                {
                    break;
                }
            }
            while (current != start);

            if (current == start && outline.Count == next.Count)
            {
                return [outline];
            }
        }

        return [.. triangles.Select(t => new List<Vector2> { t.A, t.B, t.C })];

        static int Compare(Vector2 x, Vector2 y) => x.X != y.X ? x.X.CompareTo(y.X) : x.Y.CompareTo(y.Y);
    }

    private KVObject BuildKeychainLocator(KVObject keychain)
    {
        var corners = keychain.GetFloatArray("Corners");
        Array.Resize(ref corners, 12);
        var boneName = keychain.GetStringProperty("BoneName", string.Empty);
        var boneIndex = model?.Skeleton.Bones.FirstOrDefault(bone => bone.Name == boneName)?.Index ?? -1;

        var locator = MakeNode("KeychainMarkupLocator");

        for (var corner = 0; corner < 4; corner++)
        {
            locator.Add($"corner{corner}", MakeArray(corners[corner * 3], corners[corner * 3 + 1], corners[corner * 3 + 2]));
            locator.Add($"corner{corner}_to_camera", MakeArray(0f, 0f, 0f));
            locator.Add($"bone_index{corner}", boneIndex);
        }

        locator.Add("bone_name", boneName);
        locator.Add("default_location", keychain.GetBooleanProperty("DefaultLocation"));

        return locator;
    }

    /// <summary>
    /// Gets the render mesh a keychain markup sits on. The compiler only keeps whether that mesh is a legacy one,
    /// which it reads from the mesh name.
    /// </summary>
    private string GetKeychainMeshName(bool legacy)
    {
        foreach (var renderMesh in RenderMeshesToExtract)
        {
            if (renderMesh.Name.Contains("_legacy", StringComparison.OrdinalIgnoreCase) == legacy)
            {
                return renderMesh.Name;
            }
        }

        return legacy ? "body_legacy" : "body_hd";
    }
}
