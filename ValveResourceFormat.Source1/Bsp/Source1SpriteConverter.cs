using System.Globalization;
using System.Runtime.InteropServices;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Source1.Entities;
using ValveResourceFormat.Source1.Material;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// Converts the sprite entities of a map to quads, one model per material and map area: <c>env_sprite</c>,
/// <c>env_sprite_clientside</c>, <c>env_sprite_oriented</c>, <c>env_glow</c> and <c>env_lightglow</c>, and the beams
/// and halos of <c>point_spotlight</c> and <c>beam_spotlight</c>.
/// </summary>
internal static partial class Source1SpriteConverter
{
    private const string LightGlowMaterial = "sprites/light_glow02_add_noz";

    // The quads of glows grow with the distance, so their reach cannot be known up front
    private const float BoundsPadding = 256f;

    private const int StartOnFlag = 1;

    // The radius of what has to be in view for a glow to show, when the entity names none
    private const float DefaultProxySize = 2f;

    // The distance at which a glow of a scale of one is as wide as the texture, as the quad is sized per distance
    private const float GlowSizeDistance = 200f;
    private const float LightGlowSizeDistance = 100f;

    // How the sprite shader fades a quad, which it reads from the parameters of the vertex
    private enum FadeMode
    {
        None = 0,
        Glow = 1,
        LightGlow = 2,
    }

    private enum Orientation
    {
        ParallelUpright = 0,
        FacingUpright = 1,
        Parallel = 2,
        Oriented = 3,
        ParallelOriented = 4,
        FacingCamera = 5,
        Beam = 6,
        BeamHalo = 7,
    }

    private readonly record struct SpriteInfo(Vector2 Size, Orientation Orientation, Vector2 Origin);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpriteVertex
    {
        public Vector3 Position;
        public Vector2 TexCoord;
        public Vector4 Color;
        public Vector3 Billboard;
        public Vector4 Params;
        public Vector4 Fade;
    }

    // The extra attributes are the second, third and fourth texture coordinates
    private static readonly VertexField[] SpriteFields =
    [
        new(nameof(SpriteVertex.Position), "POSITION", 0, DXGI_FORMAT.R32G32B32_FLOAT),
        new(nameof(SpriteVertex.TexCoord), "TEXCOORD", 0, DXGI_FORMAT.R32G32_FLOAT),
        new(nameof(SpriteVertex.Color), "COLOR", 0, DXGI_FORMAT.R32G32B32A32_FLOAT),
        new(nameof(SpriteVertex.Billboard), "TEXCOORD", 1, DXGI_FORMAT.R32G32B32_FLOAT),
        new(nameof(SpriteVertex.Params), "TEXCOORD", 2, DXGI_FORMAT.R32G32B32A32_FLOAT),
        new(nameof(SpriteVertex.Fade), "TEXCOORD", 3, DXGI_FORMAT.R32G32B32A32_FLOAT),
    ];

    /// <summary>Gets whether entities of a class name their sprite in the <c>model</c> key.</summary>
    public static bool IsSpriteClass(string className)
        => className is "env_sprite" or "env_sprite_clientside" or "env_sprite_oriented" or "env_glow";

    /// <summary>Converts the sprite entities of a map and adds their models to the converted map.</summary>
    /// <param name="map">The converted map, which gets the models and the entities that were converted.</param>
    /// <param name="bsp">The map.</param>
    /// <param name="locator">Finds the 3D sky area.</param>
    /// <param name="getMaterialInfo">Reads the texture and the sprite settings of a material, or returns null when it has no texture.</param>
    /// <param name="worldParts">Gets the paths of the models of the map.</param>
    /// <param name="skyParts">Gets the paths of the models in the 3D sky box area.</param>
    public static void Convert(
        Source1ConvertedMap map,
        BspFile bsp,
        IBrushLightingLocator locator,
        Func<string, Source1MaterialInfo?> getMaterialInfo,
        List<string> worldParts,
        List<string> skyParts)
    {
        var meshes = new Source1BakedMeshSet<SpriteVertex>("sprite", SpriteFields);
        var trace = new Lazy<BspWorldTrace>(() => new BspWorldTrace(bsp));

        foreach (var entity in bsp.Entities)
        {
            var isDrawn = false;

            switch (entity.ClassName)
            {
                case "env_sprite":
                case "env_glow":
                    isDrawn = AddSprite(map, meshes, locator, getMaterialInfo, entity, isOrientedClass: false, isClientSideClass: false);
                    break;
                case "env_sprite_clientside":
                    isDrawn = AddSprite(map, meshes, locator, getMaterialInfo, entity, isOrientedClass: false, isClientSideClass: true);
                    break;
                case "env_sprite_oriented":
                    isDrawn = AddSprite(map, meshes, locator, getMaterialInfo, entity, isOrientedClass: true, isClientSideClass: false);
                    break;
                case "env_lightglow":
                    isDrawn = AddLightGlow(meshes, locator, getMaterialInfo, entity);
                    break;
                case "point_spotlight":
                case "beam_spotlight":
                    isDrawn = AddSpotlight(map, meshes, locator, getMaterialInfo, trace, entity);
                    break;
            }

            if (isDrawn)
            {
                map.DrawnEntities.Add(entity);
            }
        }

        meshes.AddModels(map, worldParts, skyParts);
    }

    private static bool AddSprite(
        Source1ConvertedMap map,
        Source1BakedMeshSet<SpriteVertex> meshes,
        IBrushLightingLocator locator,
        Func<string, Source1MaterialInfo?> getMaterialInfo,
        BspEntity entity,
        bool isOrientedClass,
        bool isClientSideClass)
    {
        var materialName = entity.GetString("model");

        // A sprite model is named by its sprite file, and the material of the same name is what draws
        if (materialName.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
        {
            materialName = string.Concat(materialName.AsSpan(0, materialName.Length - ".spr".Length), ".vmt");
        }

        if (!materialName.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // A named sprite only shows from the start when it is told to. A sprite of the client has no name to be turned on by.
        var isToggled = !isClientSideClass && entity.TargetName.Length > 0 && (entity.GetInt("spawnflags") & StartOnFlag) == 0;

        var isHidden = Source1EntityConverter.IsHiddenAtSpawn(entity) || isToggled;

        // A sprite with no material to draw is drawn as the error model
        if (getMaterialInfo(materialName) is not { } info)
        {
            map.SpriteModels[entity] = new Source1ConvertedMap.EntitySpriteModel(Source1Paths.ErrorModelPath, isHidden);
            return false;
        }

        var sprite = new SpriteInfo(info.TextureSize, ParseOrientation(info.SpriteOrientation), info.SpriteOrigin);
        var renderMode = (RenderMode)entity.GetInt("rendermode");
        var angles = GetSpriteAngles(entity, isOrientedClass);
        var orientation = sprite.Orientation;

        // A sprite with a roll turns in the view plane
        if (angles.Z != 0f && orientation == Orientation.Parallel)
        {
            orientation = Orientation.ParallelOriented;
        }

        var scale = entity.GetFloat("scale", 1f);

        if (scale <= 0f)
        {
            scale = 1f;
        }

        var hasColor = renderMode != RenderMode.Normal;
        var color = hasColor ? GetColor(entity) : Vector4.One;
        var distanceScale = renderMode == RenderMode.Glow ? 1f / GlowSizeDistance : 0f;
        var isGlow = renderMode is RenderMode.Glow or RenderMode.WorldGlow;
        var fadeMode = isGlow ? FadeMode.Glow : FadeMode.None;
        var framerate = MathF.Max(entity.GetFloat("framerate"), 0f);
        var parameters = new Vector4(float.DegreesToRadians(angles.Z), distanceScale, (float)fadeMode, framerate);
        var fade = new Vector4(Vector3.Zero, isGlow ? entity.GetFloat("GlowProxySize", DefaultProxySize) : 0f);

        var materialPath = GetMaterialPath(materialName, renderMode);

        // A hidden sprite is a model of its own, at the origin of its entity, which can be shown later
        if (isHidden)
        {
            var entityChunk = new Source1BakedMeshSet<SpriteVertex>.Chunk();
            var modelPath = string.Create(CultureInfo.InvariantCulture, $"{map.MapPath}/models/sprite_entity_{map.SpriteModels.Count}.vmdl");

            AddQuad(entityChunk, Vector3.Zero, sprite, orientation, angles, scale, color, parameters, fade);
            meshes.AddModel(map, modelPath, materialPath, entityChunk);
            map.SpriteModels[entity] = new Source1ConvertedMap.EntitySpriteModel(modelPath, StartsDisabled: true);

            return false;
        }

        var chunk = meshes.Get(locator.IsInSky(entity.Origin), entity.Origin, materialPath);

        AddQuad(chunk, entity.Origin, sprite, orientation, angles, scale, color, parameters, fade);

        return true;
    }

    private static bool AddLightGlow(
        Source1BakedMeshSet<SpriteVertex> meshes,
        IBrushLightingLocator locator,
        Func<string, Source1MaterialInfo?> getMaterialInfo,
        BspEntity entity)
    {
        var width = entity.GetFloat("HorizontalGlowSize");
        var height = entity.GetFloat("VerticalGlowSize");

        if (width <= 0f || height <= 0f || getMaterialInfo(LightGlowMaterial) == null)
        {
            return false;
        }

        var color = GetColor(entity);
        var fade = new Vector4(
            entity.GetFloat("MinDist"),
            entity.GetFloat("MaxDist"),
            entity.GetFloat("OuterMaxDist"),
            entity.GetFloat("GlowProxySize", DefaultProxySize));
        var parameters = new Vector4(0f, 1f / LightGlowSizeDistance, (float)FadeMode.LightGlow, 0f);

        // The glow is sized by half widths, centered on the light, as a sprite of the same size would
        var sprite = new SpriteInfo(new Vector2(width, height) * 2f, Orientation.FacingCamera, new Vector2(0.5f));

        var materialPath = GetMaterialPath(LightGlowMaterial, RenderMode.Glow);
        var chunk = meshes.Get(locator.IsInSky(entity.Origin), entity.Origin, materialPath);

        AddQuad(chunk, entity.Origin, sprite, sprite.Orientation, Vector3.Zero, 1f, color, parameters, fade);

        return true;
    }

    // Each render mode blends differently, so a material has a variant per mode
    private static string GetMaterialPath(string materialName, RenderMode renderMode)
    {
        var variant = Source1ResourceProvider.GetSpriteVariant(renderMode);

        return Source1Paths.AddVariant(Source1Paths.ToMaterialPath(materialName), variant);
    }

    // The color and the opacity of a sprite, as the vertex color of its quad. The colors are gamma space values.
    private static Vector4 GetColor(BspEntity entity)
    {
        var color = GetRenderColor(entity) / 255f;
        var opacity = Math.Clamp(entity.GetInt("renderamt", 255), 0, 255) / 255f;
        var hdrScale = entity.GetFloat("HDRColorScale", 1f);

        return new Vector4(ColorSpace.SrgbGammaToLinear(color) * hdrScale, opacity);
    }

    private static void AddQuad(
        Source1BakedMeshSet<SpriteVertex>.Chunk chunk,
        Vector3 origin,
        SpriteInfo sprite,
        Orientation orientation,
        Vector3 angles,
        float scale,
        Vector4 color,
        Vector4 parameters,
        Vector4 fade)
    {
        // The corner offsets of the sprite from its origin, which is at the center unless the material says otherwise
        var left = -sprite.Size.X * sprite.Origin.X * scale;
        var right = left + (sprite.Size.X * scale);
        var up = sprite.Size.Y * sprite.Origin.Y * scale;
        var down = up - (sprite.Size.Y * scale);

        // The texture is sampled half a texel in from the edge
        var minU = 0.5f / sprite.Size.X;
        var minV = 0.5f / sprite.Size.Y;

        var reach = MathUtils.Max(MathF.Abs(left), MathF.Abs(right), MathF.Abs(up), MathF.Abs(down)) + BoundsPadding;
        var fixedAxes = orientation == Orientation.Oriented ? new BspDetailProps.Basis(angles) : default;

        int AddCorner(float x, float y, Vector2 texCoord)
        {
            var position = origin;
            var offset = new Vector3(x, y, (float)orientation);

            // An oriented sprite is placed in the world, the others are turned toward the view by the shader
            if (orientation == Orientation.Oriented)
            {
                position += (fixedAxes.Right * x) + (fixedAxes.Up * y);
                offset = new Vector3(0f, 0f, (float)orientation);
            }

            var vertex = new SpriteVertex
            {
                Position = position,
                TexCoord = texCoord,
                Color = color,
                Billboard = offset,
                Params = parameters,
                Fade = fade,
            };

            return chunk.AddVertex(vertex, position, reach);
        }

        var bottomLeft = AddCorner(left, down, new Vector2(minU, 1f - minV));
        var topLeft = AddCorner(left, up, new Vector2(minU, minV));
        var topRight = AddCorner(right, up, new Vector2(1f - minU, minV));
        var bottomRight = AddCorner(right, down, new Vector2(1f - minU, 1f - minV));

        chunk.AddQuad(bottomLeft, topLeft, topRight, bottomRight);
    }

    // A sprite turns its yaw into a roll, and the oriented kind faces away from the way it points
    private static Vector3 GetSpriteAngles(BspEntity entity, bool isOrientedClass)
    {
        var angles = entity.Angles;

        if (isOrientedClass)
        {
            angles.Y = (angles.Y + 180f) % 360f;
            return angles;
        }

        if (angles.Y != 0f && angles.Z == 0f)
        {
            angles.Z = angles.Y;
            angles.Y = 0f;
        }

        return angles;
    }

    private static Orientation ParseOrientation(string? name)
    {
        if (name == null)
        {
            return Orientation.ParallelUpright;
        }

        if (name.Equals("facing_upright", StringComparison.OrdinalIgnoreCase))
        {
            return Orientation.FacingUpright;
        }

        if (name.Equals("vp_parallel", StringComparison.OrdinalIgnoreCase))
        {
            return Orientation.Parallel;
        }

        if (name.Equals("oriented", StringComparison.OrdinalIgnoreCase))
        {
            return Orientation.Oriented;
        }

        if (name.Equals("vp_parallel_oriented", StringComparison.OrdinalIgnoreCase))
        {
            return Orientation.ParallelOriented;
        }

        return Orientation.ParallelUpright;
    }
}
