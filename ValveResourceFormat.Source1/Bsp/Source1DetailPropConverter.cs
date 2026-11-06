using System.Linq;
using System.Runtime.InteropServices;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// Converts the detail props of a map: the sprites become one model per map area and sky area, and the detail models
/// are listed to be placed like static props.
/// </summary>
internal static class Source1DetailPropConverter
{
    // The distances of the cl_detaildist and cl_detailfade settings of the engine
    private const float DefaultDetailDistance = 1200f;
    private const float DefaultFadeWidth = 400f;

    private const int VerticesPerQuad = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct SpriteVertex
    {
        public Vector3 Position;
        public Vector2 TexCoord;
        public Vector4 Color;
        public Vector3 Billboard;
    }

    // The billboard reads as the second texture coordinate
    private static readonly VertexField[] SpriteFields =
    [
        new(nameof(SpriteVertex.Position), "POSITION", 0, DXGI_FORMAT.R32G32B32_FLOAT),
        new(nameof(SpriteVertex.TexCoord), "TEXCOORD", 0, DXGI_FORMAT.R32G32_FLOAT),
        new(nameof(SpriteVertex.Color), "COLOR", 0, DXGI_FORMAT.R32G32B32A32_FLOAT),
        new(nameof(SpriteVertex.Billboard), "TEXCOORD", 1, DXGI_FORMAT.R32G32B32_FLOAT),
    ];

    /// <summary>A detail model and where it stands.</summary>
    /// <param name="ModelName">The path of the model.</param>
    /// <param name="Transform">The transform of the model.</param>
    internal readonly record struct DetailModel(string ModelName, Matrix4x4 Transform);

    /// <summary>What the detail props of a map add to the world and to the 3D sky box area.</summary>
    internal sealed class Result
    {
        /// <summary>Gets the paths of the sprite models of the world.</summary>
        public List<string> WorldParts { get; } = [];

        /// <summary>Gets the paths of the sprite models in the 3D sky box area.</summary>
        public List<string> SkyParts { get; } = [];

        /// <summary>Gets the detail models of the world.</summary>
        public List<DetailModel> WorldModels { get; } = [];

        /// <summary>Gets the detail models in the 3D sky box area.</summary>
        public List<DetailModel> SkyModels { get; } = [];
    }

    /// <summary>Converts the detail props of a map and adds their sprite models to the converted map.</summary>
    /// <param name="map">The converted map, which gets the sprite models and the fade distances of the sprites.</param>
    /// <param name="bsp">The map.</param>
    /// <param name="locator">Finds the 3D sky area.</param>
    public static Result Convert(Source1ConvertedMap map, BspFile bsp, IBrushLightingLocator locator)
    {
        var result = new Result();
        var detailProps = bsp.DetailProps;

        if (detailProps.Props.Count == 0)
        {
            return result;
        }

        var detailDistance = DefaultDetailDistance;
        var fadeWidth = DefaultFadeWidth;

        // The map's detail controller can only shorten the distances, and a missing key counts as 0, which hides all detail props
        var controller = bsp.FindEntity("env_detail_controller");

        if (controller != null)
        {
            detailDistance = MathF.Min(detailDistance, controller.GetFloat("fademaxdist", 0f));
            fadeWidth = MathF.Min(fadeWidth, controller.GetFloat("fademindist", 0f));
        }

        if (detailDistance <= 0f)
        {
            return result;
        }

        var fadeStart = MathF.Max(detailDistance - fadeWidth, 0f);
        var endSquared = detailDistance * detailDistance;
        map.DetailFadeSquared = new Vector2(MathF.Min(fadeStart * fadeStart, endSquared - 1f), endSquared);

        AddSprites(map, bsp, locator, result);
        AddModels(detailProps, locator, result);

        return result;
    }

    private static void AddSprites(Source1ConvertedMap map, BspFile bsp, IBrushLightingLocator locator, Result result)
    {
        var spriteMesh = bsp.DetailProps.BuildSpriteMesh();

        if (spriteMesh.Indices.Length == 0)
        {
            return;
        }

        var materialName = bsp.FindEntity("worldspawn")?.GetString("detailmaterial") ?? string.Empty;

        if (materialName.Length == 0)
        {
            materialName = BspDetailProps.SpriteMaterial;
        }

        var materialPath = Source1Paths.AddVariant(Source1Paths.ToMaterialPath(materialName), Source1ResourceProvider.DetailSpriteVariant);
        var meshes = new Source1BakedMeshSet<SpriteVertex>("detail", SpriteFields);
        var vertices = spriteMesh.Vertices;

        for (var firstVertex = 0; firstVertex + VerticesPerQuad <= vertices.Length; firstVertex += VerticesPerQuad)
        {
            var origin = vertices[firstVertex].PropOrigin;
            var inSky = locator.IsInSky(origin);
            var chunk = meshes.Get(inSky, origin, materialPath);

            AddQuad(chunk, vertices.AsSpan(firstVertex, VerticesPerQuad));
        }

        meshes.AddModels(map, result.WorldParts, result.SkyParts);
    }

    private static void AddQuad(Source1BakedMeshSet<SpriteVertex>.Chunk chunk, ReadOnlySpan<BspDetailVertex> corners)
    {
        Span<int> indices = stackalloc int[VerticesPerQuad];

        for (var i = 0; i < corners.Length; i++)
        {
            var corner = corners[i];
            var isBillboard = corner.Orientation != BspDetailPropOrientation.Normal;

            // A billboard is placed from the origin of its prop, the other sprites are already in the world
            var position = isBillboard ? corner.PropOrigin : corner.Position;
            var billboardOffset = isBillboard ? corner.LocalOffset : Vector2.Zero;

            var vertex = new SpriteVertex
            {
                Position = position,
                TexCoord = corner.TexCoord,
                Color = new Vector4(corner.Color, 1f),
                Billboard = new Vector3(billboardOffset, (float)corner.Orientation),
            };

            indices[i] = chunk.AddVertex(vertex, position, corner.LocalOffset.Length());
        }

        chunk.AddQuad(indices[0], indices[1], indices[2], indices[3]);
    }

    private static void AddModels(BspDetailProps detailProps, IBrushLightingLocator locator, Result result)
    {
        foreach (var prop in detailProps.Props)
        {
            if (prop.Type != BspDetailPropType.Model || prop.DetailModel >= detailProps.ModelDictionary.Count)
            {
                continue;
            }

            var transform = EntityTransformHelper.ToRigidTransformationMatrix(prop.Angles, prop.Origin);
            var model = new DetailModel(detailProps.ModelDictionary[prop.DetailModel], transform);

            if (locator.IsInSky(prop.Origin))
            {
                result.SkyModels.Add(model);
            }
            else
            {
                result.WorldModels.Add(model);
            }
        }
    }
}
