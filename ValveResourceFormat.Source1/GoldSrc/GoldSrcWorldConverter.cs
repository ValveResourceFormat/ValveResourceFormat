using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Source1;
using ValveResourceFormat.Source1.Bsp;
using ValveResourceFormat.Source1.Entities;
using ValveResourceFormat.Source1.Material;
using ValveResourceFormat.Source1.Model;
using ValveResourceFormat.Utils;
using S2Model = ValveResourceFormat.ResourceTypes.Model;

namespace ValveResourceFormat.GoldSrc;

/// <summary>A GoldSrc map converted to Source 2 resources.</summary>
internal sealed class GoldSrcConvertedMap
{
    /// <summary>The converted files by their compiled path, such as <c>maps/crossfire/world.vwrld_c</c>.</summary>
    public Dictionary<string, Resource> Files { get; } = [];

    /// <summary>The path of the map without an extension, such as <c>maps/crossfire</c>.</summary>
    public required string MapPath { get; init; }

    /// <summary>Gets whether a file is already in the map.</summary>
    /// <param name="path">The path of the file without the compiled suffix.</param>
    public bool Contains(string path) => Files.ContainsKey(path + GameFileLoader.CompiledFileSuffix);

    /// <summary>Adds a file to the map, which must not have it yet.</summary>
    /// <param name="path">The path of the file without the compiled suffix.</param>
    /// <param name="resource">The file.</param>
    public void AddFile(string path, Resource resource)
    {
        resource.FileName = path + GameFileLoader.CompiledFileSuffix;
        Files.Add(resource.FileName, resource);
    }
}

/// <summary>Converts a GoldSrc map to an in-memory Source 2 map, world, world nodes and entity lump.</summary>
internal static class GoldSrcWorldConverter
{
    /// <summary>Converts a map.</summary>
    /// <param name="bsp">The map.</param>
    /// <param name="fileSystem">The game, which holds the WADs and models of the map, or <see langword="null"/>.</param>
    /// <param name="loadModel">Converts a model of the game by its path, such as <c>models/scientist.mdl</c>.</param>
    /// <param name="logger">Where problems with the map are reported.</param>
    /// <param name="progress">Receives the phase the conversion is in, or <see langword="null"/>.</param>
    public static GoldSrcConvertedMap Convert(GoldSrcBspFile bsp, GoldSrcFileSystem? fileSystem, Func<string, GoldSrcConvertedModel?> loadModel, ILogger logger, IProgress<string>? progress = null)
    {
        return new GoldSrcMapConversion(bsp, fileSystem, loadModel, logger, progress).Convert();
    }
}

/// <summary>The state of the conversion of one map.</summary>
internal sealed class GoldSrcMapConversion
{
    private const int NoDrawEffect = 32;
    private const int ConstantGlowEffect = 14;
    private const int DistortEffect = 15;
    private const int HologramEffect = 16;

    // The opacity the distort and hologram effects flicker about
    private const int DistortAmount = 180;
    private const string AnglesDirectionHelper = "angles_direction";

    private const int RandomTileGrid = 20;
    private const int RandomTileOrigin = 65536;

    private const int SurfaceModeLit = 0;
    private const int SurfaceModeFullbright = 1;
    private const int SurfaceModeColor = 2;

    private const int ModeNormal = 0;
    private const int ModeColor = 1;
    private const int ModeTexture = 2;
    private const int ModeGlow = 3;
    private const int ModeSolid = 4;
    private const float SolidAlphaReference = 0.25f;
    private const int ModeAdditive = 5;

    // The alpha of the background of a tools label, as the viewer draws it on shapes
    private const float ToolLabelOpacity = 0.65f;

    // The world units after which the label of a tool face repeats
    private const float ToolLabelTileSize = 128f;

    private const string ShaderName = "goldsrc_world.vfx";
    private const string SpriteShaderName = "goldsrc_sprite.vfx";
    private const string LightmapFolder = "lightmaps";

    private static readonly int[] RandomTileTable = CreateRandomTileTable();

    // The tool textures of the map compilers, and the Source 2 tools material that shows what their faces are
    private static readonly Dictionary<string, string> ToolMaterials = new(StringComparer.OrdinalIgnoreCase)
    {
        ["aaatrigger"] = ToolsMaterials.Trigger,
        ["clip"] = "materials/tools/toolsclip.vmat",
        ["cliphull1"] = "materials/tools/toolsclip.vmat",
        ["cliphull2"] = "materials/tools/toolsclip.vmat",
        ["cliphull3"] = "materials/tools/toolsclip.vmat",
        ["noclip"] = "materials/tools/toolsclip.vmat",
        ["hint"] = "materials/tools/toolsskip.vmat",
        ["skip"] = "materials/tools/toolsskip.vmat",
        ["solidhint"] = "materials/tools/toolsskip.vmat",
        ["origin"] = ToolsMaterials.Nodraw,
        ["null"] = ToolsMaterials.Nodraw,
        ["bevel"] = ToolsMaterials.Nodraw,
        ["splitface"] = ToolsMaterials.Nodraw,
        ["contentwater"] = ToolsMaterials.Nodraw,
        ["contentempty"] = ToolsMaterials.Nodraw,
        ["contentsolid"] = ToolsMaterials.Nodraw,
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct BrushVertex
    {
        public Vector3 Position;
        public Vector2 TexCoord;
        public Vector2 LightmapCoord;
    }

    private static readonly VertexField[] BrushVertexFields =
    [
        new(nameof(BrushVertex.Position), "POSITION", 0, DXGI_FORMAT.R32G32B32_FLOAT),
        new(nameof(BrushVertex.TexCoord), "TEXCOORD", 0, DXGI_FORMAT.R32G32_FLOAT),
        new(nameof(BrushVertex.LightmapCoord), "LIGHTMAPUV", 0, DXGI_FORMAT.R32G32_FLOAT, "vLightmapUV"),
    ];

    [StructLayout(LayoutKind.Sequential)]
    private struct SpriteVertex
    {
        public Vector3 Center;
        public Vector2 Corner;
        public Vector2 TexCoord;
        public uint Color;
        public Vector2 Animation;
    }

    private static readonly VertexField[] SpriteVertexFields =
    [
        new(nameof(SpriteVertex.Center), "POSITION", 0, DXGI_FORMAT.R32G32B32_FLOAT),
        new(nameof(SpriteVertex.Corner), "TEXCOORD", 1, DXGI_FORMAT.R32G32_FLOAT),
        new(nameof(SpriteVertex.TexCoord), "TEXCOORD", 0, DXGI_FORMAT.R32G32_FLOAT),
        new(nameof(SpriteVertex.Color), "COLOR", 0, DXGI_FORMAT.R8G8B8A8_UNORM),
        new(nameof(SpriteVertex.Animation), "TEXCOORD", 2, DXGI_FORMAT.R32G32_FLOAT),
    ];

    // How an entity is drawn: its render mode, opacity from 0 to 255 and render color from 0 to 255
    private readonly record struct RenderSettings(int Mode, int Amount, Vector3 Color)
    {
        public static readonly RenderSettings Default = new(ModeNormal, 255, Vector3.Zero);

        public float Alpha => Amount / 255f;

        // An entity of any mode but the normal one is drawn with its opacity, and is gone without it
        public bool IsInvisible => Mode != ModeNormal && Amount == 0;

        // Drops the amount and color that a draw does not use, so that draws that look the same share a material
        public RenderSettings WithoutUnused(bool usesAmount, bool usesColor)
        {
            return this with
            {
                Amount = usesAmount ? Amount : 255,
                Color = usesColor ? Color : Vector3.Zero,
            };
        }
    }

    private readonly record struct MaterialKey(int Texture, RenderSettings Render, string? ToolMaterial);

    private readonly record struct BrushModelKey(int ModelIndex, RenderSettings Render, bool ForceTool, bool Alternate);

    private readonly record struct SpriteMaterialKey(string SpritePath, bool IsAdditive, bool IsColorOnly, bool IsGlow);

    private readonly record struct SpriteModelKey(SpriteMaterialKey Material, RenderSettings Render, float Scale, float Framerate);

    private readonly record struct TextureInfo(string? Path, int Width, int Height, int Frames);

    private readonly record struct WorldCell(int X, int Y, int Z);

    private readonly record struct DecalTexture(string MaterialPath, int Width, int Height);

    private readonly record struct PlacedDecal(DecalTexture Texture, GoldSrcDecalPiece Piece);

    private sealed class MeshBucket
    {
        public List<BrushVertex> Vertices { get; } = [];
        public List<int> Indices { get; } = [];
        public required string Material { get; init; }

        public static MeshBucket Get(Dictionary<string, MeshBucket> buckets, string material)
        {
            if (!buckets.TryGetValue(material, out var bucket))
            {
                bucket = new MeshBucket { Material = material };
                buckets.Add(material, bucket);
            }

            return bucket;
        }

        // Triangulates the polygon whose vertices were just added, starting at the vertex index first
        public void AddTriangleFan(int first, int vertexCount)
        {
            for (var i = 1; i < vertexCount - 1; i++)
            {
                Indices.Add(first);
                Indices.Add(first + i);
                Indices.Add(first + i + 1);
            }
        }
    }

    private readonly GoldSrcBspFile bsp;
    private readonly GoldSrcFileSystem? fileSystem;
    private readonly GoldSrcTextureTypes textureTypes;
    private readonly Func<string, GoldSrcConvertedModel?> loadModel;
    private readonly ILogger logger;
    private readonly IProgress<string>? progress;
    private readonly GoldSrcConvertedMap map;
    private readonly EntityLump lump;
    private readonly EntityClassTable classes;
    private readonly string[] textureNames;
    private readonly Dictionary<string, int> textureIndices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, TextureInfo> textures = [];
    private readonly Dictionary<MaterialKey, string> materials = [];
    private readonly Dictionary<BrushModelKey, string> brushModels = [];
    private readonly Dictionary<int, int> brushModelParts = [];
    private readonly Dictionary<int, string?> brushModelPhysics = [];
    private readonly Dictionary<string, int> cycleCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SprFile?> sprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<SpriteMaterialKey, string> spriteMaterials = [];
    private readonly Dictionary<SpriteModelKey, string> spriteModels = [];
    private readonly Dictionary<string, DecalTexture?> decalTextures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, List<PlacedDecal>> decalsByFace = [];
    private readonly int[] styleScales;
    private readonly GoldSrcLightmapAtlas atlas;
    private bool hasStudioModels;

    public GoldSrcMapConversion(GoldSrcBspFile bsp, GoldSrcFileSystem? fileSystem, Func<string, GoldSrcConvertedModel?> loadModel, ILogger logger, IProgress<string>? progress)
    {
        this.bsp = bsp;
        this.fileSystem = fileSystem;
        textureTypes = GoldSrcTextureTypes.Load(fileSystem);
        this.loadModel = loadModel;
        this.logger = logger;
        this.progress = progress;

        map = new GoldSrcConvertedMap { MapPath = $"maps/{Source1Paths.Normalize(bsp.Name)}" };
        lump = Source1EntityConverter.CreateLump(bsp.Name);
        classes = Source1EntityClassCatalog.GetGoldSrc(fileSystem?.GameDirectories ?? [], logger);
        textureNames = [.. bsp.TextureNames];

        for (var i = 0; i < textureNames.Length; i++)
        {
            textureIndices.TryAdd(textureNames[i], i);
        }

        styleScales = GoldSrcLightmapAtlas.GetStyleSampleScales(bsp.Entities);
        progress?.Report("Building lightmaps…");
        atlas = new GoldSrcLightmapAtlas(bsp, styleScales, logger);
    }

    public GoldSrcConvertedMap Convert()
    {
        var worldFolder = $"{map.MapPath}/";
        var nodePath = $"{worldFolder}world_nodes/node001";
        var lumpPath = $"{worldFolder}entities/default_ents";
        var postProcessPath = $"{map.MapPath}/postprocess.vpost";

        PlaceDecals();

        progress?.Report("Converting entities…");
        var entities = ConvertEntities();

        if (hasStudioModels)
        {
            progress?.Report("Baking light probes…");
            GoldSrcLightProbeBaker.Add(map, bsp, lump, styleScales, entities, logger);
        }

        progress?.Report("Loading sky…");
        AddSky(entities);

        entities.Add(Source1WorldConverter.CreatePostProcessVolume(lump, postProcessPath));
        map.AddFile($"{map.MapPath}.vmap", Source1WorldConverter.CreateMap($"{map.MapPath}/"));
        map.AddFile(postProcessPath, Source1WorldConverter.CreatePostProcessing());

        var lightmapPaths = AddLightmaps();
        var worldParts = new List<string>();

        if (bsp.Models.Length > 0)
        {
            progress?.Report("Building world geometry…");
            AddWorldModels(worldParts);
        }

        progress?.Report("Converting world visibility…");

        var worldVisibility = GoldSrcWorldVisibilityConverter.Convert(bsp);

        if (worldVisibility != null)
        {
            map.AddFile($"{worldFolder}world_visibility.vvis", worldVisibility);
        }

        map.AddFile($"{worldFolder}world.vwrld", Source1WorldConverter.CreateWorld($"{worldFolder}world.vwrld", lumpPath, nodePath, new Source1WorldConverter.WorldLightmaps(lightmapPaths)));
        map.AddFile($"{nodePath}.vwnod", CreateWorldNode($"{nodePath}.vwnod", worldParts));
        map.AddFile($"{lumpPath}.vents", Source1WorldConverter.CreateEntityLump($"{lumpPath}.vents", bsp.Name, entities));

        foreach (var file in map.Files.Values)
        {
            Source1ResourceReferences.Update(file);
        }

        return map;
    }

    private static Resource CreateWorldNode(string path, List<string> parts)
    {
        var resource = new Resource(ResourceType.WorldNode) { FileName = path };

        var data = KVObject.Collection();
        data.Add("m_sceneObjects", Source1WorldConverter.CreateSceneObjects(parts));
        data.Add("m_layerNames", KVObject.Array());

        resource.AddBlock(new WorldNode { Resource = resource, Data = data });

        return resource;
    }

    // The surfaces read one lightmap page, which is the only page the set lists
    private List<string> AddLightmaps()
    {
        var pixels = MemoryMarshal.AsBytes(atlas.Pixels.AsSpan()).ToArray();
        var lightmap = Texture.Create((ushort)atlas.Width, (ushort)atlas.Height, VTexFormat.RGBA16161616F, 0, Vector4.One, [pixels]);
        var lightmapPath = $"{map.MapPath}/{LightmapFolder}/lightmap.vtex";

        map.AddFile(lightmapPath, lightmap);

        return [lightmapPath];
    }

    #region Entities

    private List<EntityLump.Entity> ConvertEntities()
    {
        var options = CreateEntityOptions();
        var entities = new List<EntityLump.Entity>(bsp.Entities.Count);

        foreach (var source in bsp.Entities)
        {
            var entity = Source1EntityConverter.Convert(source, lump, options);

            if (entity == null)
            {
                continue;
            }

            var modelKey = source.GetString("model");

            if (modelKey.StartsWith('*'))
            {
                ConvertBrushEntity(source, entity, modelKey);
            }
            else if (modelKey.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
            {
                ConvertSpriteEntity(source, entity, modelKey);
            }
            else
            {
                ConvertStudioEntity(source, entity, modelKey);
            }

            entities.Add(entity);
        }

        return entities;
    }

    // Lights are baked into the lightmaps, so they do not light anything by themselves
    private Source1EntityConverterOptions CreateEntityOptions()
    {
        var targetOrigins = new Dictionary<string, Vector3>();

        foreach (var entity in bsp.Entities)
        {
            var targetName = entity.TargetName;

            if (targetName.Length > 0)
            {
                targetOrigins.TryAdd(targetName, entity.Origin);
            }
        }

        return new Source1EntityConverterOptions
        {
            DynamicLights = false,
            TargetOrigins = targetOrigins,
        };
    }

    private static RenderSettings GetRenderSettings(BspEntity entity)
    {
        var mode = entity.GetInt("rendermode");
        var amount = Math.Clamp(entity.GetInt("renderamt"), 0, 255);
        var color = Vector3.Zero;

        if (entity.Properties.ContainsKey("rendercolor"))
        {
            color = Vector3.Clamp(entity.GetVector3("rendercolor"), Vector3.Zero, new Vector3(255f));
        }

        // A conveyor encodes its speed in its render color for the scrolling texture
        if (entity.ClassName == "func_conveyor")
        {
            color = GetConveyorColor(entity.GetFloat("speed"));
        }

        var effect = entity.GetInt("renderfx");

        if (effect is DistortEffect or HologramEffect)
        {
            amount = DistortAmount;
        }

        // The amount of an entity that is drawn normally is not used
        if (mode == ModeNormal)
        {
            amount = 255;
        }

        return new RenderSettings(mode, amount, color);
    }

    // The lone angle key of the editor is a yaw, and -1 and -2 name straight up and straight down
    private static Vector3 GetAngles(BspEntity entity)
    {
        if (entity.Properties.ContainsKey("angles"))
        {
            return entity.Angles;
        }

        if (!entity.Properties.ContainsKey("angle"))
        {
            return Vector3.Zero;
        }

        var angle = entity.GetFloat("angle");

        if (angle < 0f)
        {
            return new Vector3(MathF.Truncate(angle) == -1f ? -90f : 90f, 0f, 0f);
        }

        return new Vector3(0f, angle, 0f);
    }

    // The model of a brush entity is relative to its origin, which is the center of its origin brush. A brush entity turns
    // about its origin by its angles, unless its class takes them as the way it moves.
    private void ConvertBrushEntity(BspEntity source, EntityLump.Entity entity, string modelKey)
    {
        var isBrushModel = TryGetBrushModelIndex(modelKey, out var index);
        var settings = GetRenderSettings(source);

        // An entity without opacity is not drawn, whatever its mode
        var isHidden = (source.GetInt("effects") & NoDrawEffect) != 0 || settings.IsInvisible;

        if (!isBrushModel || isHidden)
        {
            entity["model"] = string.Empty;
            return;
        }

        var forceTool = source.ClassName.StartsWith("trigger_", StringComparison.Ordinal);
        var angles = Vector3.Zero;
        var entityClass = classes.FindClass(source.ClassName);

        if (entityClass != null && !HasAnglesDirection(entityClass))
        {
            angles = GetAngles(source);
        }

        var alternate = source.GetInt("frame") != 0;

        entity["model"] = Source1ResourceReferences.Reference(GetBrushModel(new BrushModelKey(index, settings, forceTool, alternate)));
        entity["angles"] = Source1EntityConverter.FormatVector(angles);
    }

    // A key of the form *N names the submodel N, and the world is not one
    private bool TryGetBrushModelIndex(string modelKey, out int index)
    {
        index = 0;

        return modelKey.StartsWith('*')
            && int.TryParse(modelKey.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out index)
            && index > 0
            && index < bsp.Models.Length;
    }

    private static bool HasAnglesDirection(EntityClass entityClass)
        => entityClass.Helpers.Any(static helper => helper.Name.Equals(AnglesDirectionHelper, StringComparison.OrdinalIgnoreCase));

    private void ConvertStudioEntity(BspEntity source, EntityLump.Entity entity, string modelKey)
    {
        var path = GetStudioPath(source, modelKey);

        if (path == null)
        {
            return;
        }

        var model = loadModel(path);

        if (model == null)
        {
            entity["model"] = string.Empty;
            return;
        }

        var settings = GetRenderSettings(source);

        // An entity without opacity is not drawn
        if (settings.IsInvisible)
        {
            entity["model"] = string.Empty;
            return;
        }

        hasStudioModels = true;

        // A studio model turns by the negated pitch, unlike a brush model
        var angles = GetAngles(source);
        angles.X = -angles.X;

        // Every mode but the normal one blends the model with its opacity
        var blend = GoldSrcStudioBlend.Translucent;

        if (settings.Mode == ModeNormal)
        {
            blend = GoldSrcStudioBlend.Opaque;
        }
        else if (settings.Mode == ModeAdditive)
        {
            blend = GoldSrcStudioBlend.Additive;
        }

        // The renderer fades the model with the opacity, and the color key of the map must not tint it
        entity["rendercolor"] = Source1EntityConverter.FormatVector(new Vector3(255f));
        entity["renderamt"] = settings.Alpha.ToString(CultureInfo.InvariantCulture);

        entity["model"] = Source1ResourceReferences.Reference(Source1Paths.ToModelPath(path));
        entity["angles"] = Source1EntityConverter.FormatVector(angles);
        entity["skin"] = GoldSrcModelConverter.GetSkinName(source.GetInt("skin"), blend);
        entity["model_state"] = EntityLump.StringifyValue(CreateModelState(model.Model, Math.Max(source.GetInt("body"), 0)));
        entity.Remove("body");

        var sequence = source.GetInt("sequence");

        if ((uint)sequence >= (uint)model.SequenceNames.Count)
        {
            return;
        }

        var sequenceName = model.SequenceNames[sequence];

        if (sequenceName != null)
        {
            entity["defaultanim"] = sequenceName;
        }
    }

    // The body value picks a sub-model of every body part, which the mesh groups of the model name as part_@index_#&name
    private static KVObject CreateModelState(S2Model model, int body)
    {
        var parts = new List<(string Name, int Count)>();

        foreach (var group in model.Data.GetArray<string>("m_meshGroups") ?? [])
        {
            var separator = group.IndexOf("_@", StringComparison.Ordinal);

            if (separator < 0)
            {
                continue;
            }

            var name = group[..separator];

            if (parts.Count > 0 && parts[^1].Name == name && !group.AsSpan(separator).StartsWith("_@0_", StringComparison.Ordinal))
            {
                parts[^1] = (name, parts[^1].Count + 1);
            }
            else
            {
                parts.Add((name, 1));
            }
        }

        var bodyGroups = KVObject.Collection();
        var addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var divisor = 1;

        foreach (var (name, count) in parts)
        {
            if (addedNames.Add(name))
            {
                bodyGroups.Add(name, (long)(body / divisor % count));
            }

            divisor *= count;
        }

        var state = KVObject.Collection();
        state.Add("body_groups", bodyGroups);

        return state;
    }

    // An entity names its model in the model key, and the editor class of one that does not can name a default
    private string? GetStudioPath(BspEntity source, string modelKey)
    {
        if (modelKey.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
        {
            return modelKey;
        }

        var entityClass = classes.FindClass(source.ClassName);

        if (entityClass == null)
        {
            return null;
        }

        foreach (var helper in entityClass.Helpers)
        {
            var path = GetHelperModel(source, helper);

            if (path != null && path.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        return null;
    }

    private string? GetHelperModel(BspEntity source, HelperDeclaration helper)
    {
        if (helper.Name.Equals("studio", StringComparison.OrdinalIgnoreCase))
        {
            return helper.Arguments.Count > 0 ? helper.Arguments[0] : null;
        }

        if (helper.Name.Equals("studio_keyed", StringComparison.OrdinalIgnoreCase))
        {
            return GetKeyedModel(source, helper.Arguments);
        }

        if (helper.Name.Equals("studio_cycle", StringComparison.OrdinalIgnoreCase))
        {
            return GetCycledModel(source.ClassName, helper.Arguments);
        }

        return null;
    }

    // The key to read, then pairs of a value and the model it picks, then the model of any other value
    private static string? GetKeyedModel(BspEntity source, IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 2)
        {
            return null;
        }

        var value = source.GetInt(arguments[0]);

        for (var i = 1; i + 1 < arguments.Count; i += 2)
        {
            if (int.TryParse(arguments[i], CultureInfo.InvariantCulture, out var candidate) && candidate == value)
            {
                return arguments[i + 1];
            }
        }

        return arguments.Count % 2 == 0 ? arguments[^1] : null;
    }

    // The entities of a class take the models in turn, in the order the map lists them
    private string? GetCycledModel(string className, IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return null;
        }

        cycleCounts.TryGetValue(className, out var count);
        cycleCounts[className] = count + 1;

        return arguments[count % arguments.Count];
    }

    #endregion

    #region Sprites

    private SprFile? LoadSprite(string path)
    {
        if (sprites.TryGetValue(path, out var cached))
        {
            return cached;
        }

        SprFile? sprite = null;

        try
        {
            var file = fileSystem?.FindFile(path);

            if (file != null)
            {
                sprite = new SprFile(File.ReadAllBytes(file));
            }
        }
        catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException)
        {
            logger.LogWarning("Failed to load sprite {Sprite}: {Message}", path, e.Message);
        }

        sprites.Add(path, sprite);

        return sprite;
    }

    // A sprite is a quad model of its own, which a brush entity draws. Its shader turns the quad to face the view.
    private void ConvertSpriteEntity(BspEntity source, EntityLump.Entity entity, string modelKey)
    {
        var spritePath = Source1Paths.Normalize(modelKey);
        var settings = GetRenderSettings(source);
        var sprite = LoadSprite(spritePath);

        if (sprite == null || settings.IsInvisible)
        {
            entity["model"] = string.Empty;
            return;
        }

        var isAdditive = settings.Mode is ModeGlow or ModeAdditive || sprite.TextureFormat == SprTextureFormat.Additive;
        var isColorOnly = settings.Mode == ModeColor;

        // A glow keeps its size on the screen and fades with distance, which its vertex shader does
        var isGlow = settings.Mode == ModeGlow && source.GetInt("renderfx") != ConstantGlowEffect;

        var materialKey = new SpriteMaterialKey(spritePath, isAdditive, isColorOnly, isGlow);
        var framerate = sprite.Frames.Length > 1 ? Math.Max(source.GetFloat("framerate"), 0f) : 0f;
        var spriteModelKey = new SpriteModelKey(materialKey, settings, GetSpriteScale(source, isGlow), framerate);

        entity["classname"] = "func_brush";
        entity["model"] = Source1ResourceReferences.Reference(GetSpriteModel(spriteModelKey, sprite));
        entity["angles"] = Source1EntityConverter.FormatVector(Vector3.Zero);
    }

    private string GetSpriteMaterial(SpriteMaterialKey key, SprFile sprite)
    {
        if (spriteMaterials.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var path = string.Create(CultureInfo.InvariantCulture, $"materials/goldsrc/sprites/{spriteMaterials.Count}.vmat");
        spriteMaterials.Add(key, path);
        map.AddFile(path, CreateSpriteMaterial(path, key.SpritePath, sprite, key.IsAdditive, key.IsColorOnly, key.IsGlow));

        return path;
    }

    private string GetSpriteModel(SpriteModelKey key, SprFile sprite)
    {
        if (spriteModels.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var materialPath = GetSpriteMaterial(key.Material, sprite);
        var path = string.Create(CultureInfo.InvariantCulture, $"{map.MapPath}/models/sprite_{spriteModels.Count}.vmdl");
        spriteModels.Add(key, path);
        map.AddFile(path, CreateSpriteModel(path, materialPath, sprite, key));

        return path;
    }

    private static float GetSpriteScale(BspEntity source, bool isGlow)
    {
        if (isGlow)
        {
            return 1f;
        }

        var requested = source.GetFloat("scale", 1f);

        return requested > 0f ? requested : 1f;
    }

    private static Resource CreateSpriteMaterial(string path, string spritePath, SprFile sprite, bool isAdditive, bool isColorOnly, bool isGlow)
    {
        var orientation = sprite.Type switch
        {
            SprType.ParallelUpright => 0,
            SprType.FacingUpright => 1,
            _ => 2,
        };

        var material = new ResourceTypes.Material
        {
            Resource = new Resource(ResourceType.Material),
            Name = path,
            ShaderName = SpriteShaderName,
        };

        material.IntParams["F_TRANSLUCENT"] = 1;
        material.IntParams["F_ADDITIVE_BLEND"] = isAdditive ? 1 : 0;
        material.IntParams["F_COLOR_ONLY"] = isColorOnly ? 1 : 0;
        material.IntParams["F_GLOW"] = isGlow ? 1 : 0;
        material.IntParams["g_nOrientation"] = orientation;
        material.IntParams["F_RENDER_BACKFACES"] = 1;
        material.IntParams["F_NO_Z_PREPASS"] = 1;
        material.TextureParams["g_tColor"] = Path.ChangeExtension(spritePath, Source1Paths.TextureExtension);

        return FinishMaterial(material, path);
    }

    private static Resource CreateSpriteModel(string path, string materialPath, SprFile sprite, SpriteModelKey key)
    {
        var settings = key.Render;
        var scale = key.Scale;
        var isAdditive = key.Material.IsAdditive;
        var frame = sprite.Frames[0];

        // A sprite without a render color is drawn white. The render color is in gamma space and the texture is
        // decoded to linear, so the vertex color is converted to linear too.
        var rgb = settings.Color == Vector3.Zero ? Vector3.One : settings.Color / 255f;
        var alpha = 1f;

        if (isAdditive)
        {
            rgb *= settings.Alpha;
        }
        else
        {
            alpha = settings.Alpha;
        }

        var linear = ColorSpace.SrgbGammaToLinear(rgb);
        var color = PackColor(linear, alpha);

        var left = frame.OriginX * scale;
        var right = (frame.OriginX + frame.Width) * scale;
        var top = frame.OriginY * scale;
        var bottom = (frame.OriginY - frame.Height) * scale;

        Vector2[] corners = [new(left, top), new(right, top), new(right, bottom), new(left, bottom)];
        var (uvMin, uvMax) = GoldSrcSpriteConverter.GetFrameRect(sprite, 0);
        Vector2[] texCoords = [uvMin, new(uvMax.X, uvMin.Y), uvMax, new(uvMin.X, uvMax.Y)];
        var vertices = new SpriteVertex[corners.Length];
        var animation = new Vector2(key.Framerate, sprite.Frames.Length);

        for (var i = 0; i < vertices.Length; i++)
        {
            vertices[i] = new SpriteVertex { Center = Vector3.Zero, Corner = corners[i], TexCoord = texCoords[i], Color = color, Animation = animation };
        }

        int[] indices = [0, 1, 2, 0, 2, 3];
        var extent = MathF.Max(MathF.Abs(right - left), MathF.Abs(top - bottom));
        var bounds = new Vector3(extent);

        var buffers = Source1VertexBuffers.Create(vertices, SpriteVertexFields, indices);
        var drawCalls = new List<MeshDrawCall> { new(materialPath, 0, vertices.Length, 0, indices.Length) };

        return CreateModel(path, buffers, drawCalls, [materialPath], -bounds, bounds);
    }

    private static uint PackColor(Vector3 color, float alpha)
    {
        var red = (uint)Source1VertexBuffers.ToUnorm8(color.X);
        var green = (uint)Source1VertexBuffers.ToUnorm8(color.Y);
        var blue = (uint)Source1VertexBuffers.ToUnorm8(color.Z);
        var opacity = (uint)Source1VertexBuffers.ToUnorm8(alpha);

        return red | (green << 8) | (blue << 16) | (opacity << 24);
    }

    #endregion

    #region Sky

    // The 2D sky is the six images of the skyname of the world, which an env_sky draws as a cube
    private void AddSky(List<EntityLump.Entity> entities)
    {
        var skyName = bsp.Entities.Count > 0 ? bsp.Entities[0].GetString("skyname") : string.Empty;

        if (fileSystem == null || skyName.Length == 0)
        {
            return;
        }

        var bitmaps = new SKBitmap[SkyCube.FaceCount];

        try
        {
            var size = 0;

            for (var i = 0; i < bitmaps.Length; i++)
            {
                var suffix = SkyCube.GetSuffix(i);
                var file = fileSystem.FindFile($"gfx/env/{skyName}{suffix}.tga");

                if (file == null)
                {
                    logger.LogWarning("Sky image gfx/env/{Name}{Face}.tga was not found", skyName, suffix);
                    return;
                }

                bitmaps[i] = TgaImage.Decode(File.ReadAllBytes(file));
                size = Math.Max(size, bitmaps[i].Width);
            }

            var materialPath = $"materials/skybox/{skyName}.vmat";
            var texturePath = $"materials/skybox/{skyName}.vtex";

            map.AddFile(texturePath, CreateSkyCube(size, bitmaps));
            map.AddFile(materialPath, Source1MaterialConverter.CreateSkyMaterial(materialPath, texturePath));

            var sky = new EntityLump.Entity { ParentLump = lump };
            sky["classname"] = "env_sky";
            sky["skyname"] = Source1ResourceReferences.Reference(materialPath);
            sky["fog_type"] = 0L;
            entities.Add(sky);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnexpectedMagicException)
        {
            logger.LogWarning("Failed to load the sky {Name}: {Message}", skyName, e.Message);
        }
        finally
        {
            foreach (var bitmap in bitmaps)
            {
                bitmap?.Dispose();
            }
        }
    }

    private static Resource CreateSkyCube(int size, SKBitmap[] bitmaps)
    {
        var faces = new ReadOnlyMemory<byte>[SkyCube.FaceCount];

        for (var glFace = 0; glFace < faces.Length; glFace++)
        {
            var texCoords = SkyCube.GetFaceTexCoords(glFace, size, out var skyFace);
            var bitmap = bitmaps[skyFace];
            var source = bitmap.Pixels;
            var pixels = new byte[texCoords.Length * 4];

            for (var i = 0; i < texCoords.Length; i++)
            {
                var sourceX = Math.Clamp((int)(texCoords[i].X * bitmap.Width), 0, bitmap.Width - 1);
                var sourceY = Math.Clamp((int)(texCoords[i].Y * bitmap.Height), 0, bitmap.Height - 1);
                var texel = source[(sourceY * bitmap.Width) + sourceX];

                pixels[i * 4] = texel.Red;
                pixels[(i * 4) + 1] = texel.Green;
                pixels[(i * 4) + 2] = texel.Blue;
                pixels[(i * 4) + 3] = 255;
            }

            faces[glFace] = pixels;
        }

        return Texture.Create((ushort)size, (ushort)size, VTexFormat.RGBA8888, VTexFlags.CUBE_TEXTURE, Vector4.One, faces);
    }

    #endregion

    #region Brush models

    // The model of a brush entity or of the world, built once for each way it is drawn
    private string GetBrushModel(BrushModelKey key)
    {
        if (brushModels.TryGetValue(key, out var cached))
        {
            return cached;
        }

        brushModelParts.TryGetValue(key.ModelIndex, out var part);
        brushModelParts[key.ModelIndex] = part + 1;

        var path = Source1WorldConverter.GetBrushModelPath(map.MapPath, key.ModelIndex, part);
        brushModels.Add(key, path);

        var resource = CreateBrushModel(key, path);
        map.AddFile(path, resource);
        AddBrushModelPhysics(key, path, resource);

        return path;
    }

    private void AddBrushModelPhysics(BrushModelKey key, string modelPath, Resource modelResource)
    {
        if (key.ForceTool)
        {
            return;
        }

        if (key.ModelIndex == 0)
        {
            var worldPhysics = GoldSrcModelPhysics.Convert(bsp, 0, textureTypes);

            if (worldPhysics != null)
            {
                var worldPhysicsPath = $"{map.MapPath}/world_physics.vphys";
                map.AddFile(worldPhysicsPath, worldPhysics);
                map.AddFile($"{map.MapPath}/world_physics.vrman", Source1WorldConverter.CreateWorldPhysicsManifest(worldPhysicsPath));
            }

            return;
        }

        if (!brushModelPhysics.TryGetValue(key.ModelIndex, out var physicsPath))
        {
            var physics = GoldSrcModelPhysics.Convert(bsp, key.ModelIndex, textureTypes);

            if (physics != null)
            {
                physicsPath = Path.ChangeExtension(modelPath, ".vphys");
                map.AddFile(physicsPath, physics);
            }

            brushModelPhysics.Add(key.ModelIndex, physicsPath);
        }

        if (physicsPath != null && modelResource.DataBlock is S2Model model)
        {
            model.Data["m_refPhysicsData"].Add(S2Model.CreateResourceReference(physicsPath));
        }
    }

    // The faces of the world model become models of their own, one for each cell of a grid, so that the frustum and
    // visibility can cull them
    private void AddWorldModels(List<string> worldParts)
    {
        var worldKey = new BrushModelKey(0, RenderSettings.Default, false, false);
        var cells = new Dictionary<WorldCell, List<int>>();

        foreach (var faceIndex in GetModelFaces(0))
        {
            var cell = GetWorldCell(faceIndex);

            if (!cells.TryGetValue(cell, out var faces))
            {
                faces = [];
                cells.Add(cell, faces);
            }

            faces.Add(faceIndex);
        }

        if (cells.Count == 0)
        {
            worldParts.Add(GetBrushModel(worldKey));
            return;
        }

        var part = 0;

        foreach (var faces in cells.Values)
        {
            var path = Source1WorldConverter.GetBrushModelPath(map.MapPath, 0, part);
            var resource = CreateBrushModel(worldKey, path, faces);

            map.AddFile(path, resource);
            worldParts.Add(path);

            if (part == 0)
            {
                brushModels.Add(worldKey, path);
                AddBrushModelPhysics(worldKey, path, resource);
            }

            part++;
        }

        brushModelParts[0] = part;
    }

    // A face is in the cell of the grid its center is in
    private WorldCell GetWorldCell(int faceIndex)
    {
        var positions = bsp.GetFaceVertices(bsp.Faces[faceIndex]);
        var center = Vector3.Zero;

        foreach (var position in positions)
        {
            center += position;
        }

        center /= Math.Max(positions.Length, 1);

        return new WorldCell(
            (int)MathF.Floor(center.X / Source1WorldConverter.WorldChunkSize),
            (int)MathF.Floor(center.Y / Source1WorldConverter.WorldChunkSize),
            (int)MathF.Floor(center.Z / Source1WorldConverter.WorldChunkSize));
    }

    private List<int> GetModelFaces(int modelIndex)
    {
        var model = bsp.Models[modelIndex];
        var faces = new List<int>(model.NumFaces);

        for (var i = 0; i < model.NumFaces; i++)
        {
            var faceIndex = model.FirstFace + i;

            if (faceIndex < 0 || faceIndex >= bsp.Faces.Length)
            {
                break;
            }

            faces.Add(faceIndex);
        }

        return faces;
    }

    private Resource CreateBrushModel(BrushModelKey key, string path, List<int>? faces = null)
    {
        var buckets = new Dictionary<string, MeshBucket>();

        foreach (var faceIndex in faces ?? GetModelFaces(key.ModelIndex))
        {
            AddFace(faceIndex, key, buckets);
        }

        var vertices = new List<BrushVertex>();
        var indices = new List<int>();
        var drawCalls = new List<MeshDrawCall>();
        var materialPaths = new List<string>();

        foreach (var bucket in buckets.Values)
        {
            drawCalls.Add(new MeshDrawCall(bucket.Material, vertices.Count, bucket.Vertices.Count, indices.Count, bucket.Indices.Count));
            materialPaths.Add(bucket.Material);
            vertices.AddRange(bucket.Vertices);
            indices.AddRange(bucket.Indices);
        }

        // A volume that draws nothing is only its collision, as in compiled Source 2 maps
        if (drawCalls.Count == 0)
        {
            return S2Model.Create(path, [], [], [], [], 0UL);
        }

        var vertexArray = vertices.ToArray();
        var (min, max) = Source1VertexBuffers.GetBounds(vertexArray, vertex => vertex.Position);
        var buffers = Source1VertexBuffers.Create(vertexArray, BrushVertexFields, indices.ToArray());

        return CreateModel(path, buffers, drawCalls, materialPaths, min, max);
    }

    private static Resource CreateModel(string path, VBIB buffers, List<MeshDrawCall> drawCalls, List<string> materialPaths, Vector3 min, Vector3 max)
    {
        var mesh = new ModelMeshSource("brush", buffers, drawCalls, min, max) { MeshGroupMask = 1UL };
        var materialGroups = new List<ModelMaterialGroup> { new(Source1ModelConverter.GetSkinName(0), materialPaths) };

        return S2Model.Create(path, [mesh], [], materialGroups, ["brush"], 1UL);
    }

    // A face without a polygon, a texture info or a texture name in the map is not drawn
    private bool TryGetFaceTexture(in GoldSrcFace face, out GoldSrcTextureInfo info, out string name)
    {
        info = default;
        name = string.Empty;

        if (face.TexInfo < 0 || face.TexInfo >= bsp.TextureInfos.Length || face.NumEdges < 3)
        {
            return false;
        }

        info = bsp.TextureInfos[face.TexInfo];

        if (info.MipTexture < 0 || info.MipTexture >= textureNames.Length)
        {
            return false;
        }

        name = textureNames[info.MipTexture];

        return true;
    }

    private void AddFace(int faceIndex, BrushModelKey model, Dictionary<string, MeshBucket> buckets)
    {
        var face = bsp.Faces[faceIndex];

        if (!TryGetFaceTexture(face, out var info, out var name))
        {
            return;
        }

        // The sky is drawn by the sky box
        if (name.StartsWith("sky", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var textureIndex = ResolveAnimationBase(ResolveRandomTile(info.MipTexture, faceIndex), model.Alternate);
        var toolMaterial = GetToolMaterial(name, model.ForceTool);
        var materialPath = GetMaterial(new MaterialKey(textureIndex, GetMaterialSettings(model.Render, textureNames[textureIndex]), toolMaterial));

        var bucket = MeshBucket.Get(buckets, materialPath);
        var positions = bsp.GetFaceVertices(face);
        var normal = bsp.Planes[face.PlaneNum].Normal;
        var hasLightmap = atlas.HasLightmap(faceIndex);
        var first = bucket.Vertices.Count;

        if (face.Side != 0)
        {
            normal = -normal;
        }

        ReverseIfBackFacing(positions, normal);

        // The coordinates are stored normalized, as the depth prepass samples the texture with them as they are
        var texture = GetTexture(textureIndex);
        var textureSize = new Vector2(texture.Width, texture.Height);

        foreach (var position in positions)
        {
            var texCoord = info.GetTextureCoordinates(position);
            var lightmapCoord = hasLightmap ? atlas.GetCoordinates(faceIndex, texCoord) : atlas.WhiteCoordinates;
            var storedCoord = texCoord / textureSize;

            if (toolMaterial != null)
            {
                storedCoord = GetToolTextureCoordinates(position, normal) / ToolLabelTileSize;
            }

            bucket.Vertices.Add(new BrushVertex { Position = position, TexCoord = storedCoord, LightmapCoord = lightmapCoord });
        }

        bucket.AddTriangleFan(first, positions.Length);

        if (!model.ForceTool)
        {
            AddDecals(faceIndex, buckets);
        }
    }

    #region Decals

    // How far from its origin an infodecal looks for a brush entity to land on
    private const float DecalTraceDistance = 5f;

    // Static decals are placed where the map says. A decal with a name waits for something to trigger it.
    private void PlaceDecals()
    {
        foreach (var source in bsp.Entities)
        {
            if (!source.ClassName.Equals("infodecal", StringComparison.OrdinalIgnoreCase) || source.TargetName.Length > 0)
            {
                continue;
            }

            if (GetDecalTexture(source.GetString("texture")) is not { } texture)
            {
                continue;
            }

            var pieces = ProjectOnBrushEntities(source.Origin, texture);

            if (pieces.Count == 0)
            {
                pieces = GoldSrcDecalProjector.Project(bsp, 0, source.Origin, texture.Width, texture.Height, CanReceiveDecal);
            }

            foreach (var piece in pieces)
            {
                if (!decalsByFace.TryGetValue(piece.Face, out var placed))
                {
                    placed = [];
                    decalsByFace.Add(piece.Face, placed);
                }

                placed.Add(new PlacedDecal(texture, piece));
            }
        }
    }

    private List<GoldSrcDecalPiece> ProjectOnBrushEntities(Vector3 origin, DecalTexture texture)
    {
        foreach (var source in bsp.Entities)
        {
            if (!TryGetBrushModelIndex(source.GetString("model"), out var index)
                || source.ClassName.StartsWith("trigger_", StringComparison.Ordinal))
            {
                continue;
            }

            var model = bsp.Models[index];
            var local = origin - source.Origin;
            var margin = new Vector3(DecalTraceDistance);
            var min = model.Min - margin;
            var max = model.Max + margin;
            var isNear = local.X >= min.X && local.Y >= min.Y && local.Z >= min.Z && local.X <= max.X && local.Y <= max.Y && local.Z <= max.Z;

            if (!isNear)
            {
                continue;
            }

            var pieces = GoldSrcDecalProjector.Project(bsp, index, local, texture.Width, texture.Height, CanReceiveDecal);

            if (pieces.Count > 0)
            {
                return pieces;
            }
        }

        return [];
    }

    // Faces that are not drawn with a lightmap and texture, such as the sky and liquids, take no decals
    private bool CanReceiveDecal(int faceIndex)
    {
        if (!TryGetFaceTexture(bsp.Faces[faceIndex], out var info, out var name) || info.IsSpecial)
        {
            return false;
        }

        return !name.StartsWith("sky", StringComparison.OrdinalIgnoreCase)
            && !IsWater(name)
            && GetToolMaterial(name, false) == null;
    }

    private void AddDecals(int faceIndex, Dictionary<string, MeshBucket> buckets)
    {
        if (!decalsByFace.TryGetValue(faceIndex, out var placed))
        {
            return;
        }

        foreach (var decal in placed)
        {
            var bucket = MeshBucket.Get(buckets, decal.Texture.MaterialPath);
            var piece = decal.Piece;
            var hasLightmap = atlas.HasLightmap(faceIndex);
            var first = bucket.Vertices.Count;

            for (var i = 0; i < piece.Positions.Length; i++)
            {
                var lightmapCoord = hasLightmap ? atlas.GetCoordinates(faceIndex, piece.TextureCoordinates[i]) : atlas.WhiteCoordinates;

                bucket.Vertices.Add(new BrushVertex { Position = piece.Positions[i], TexCoord = piece.DecalCoordinates[i], LightmapCoord = lightmapCoord });
            }

            bucket.AddTriangleFan(first, piece.Positions.Length);
        }
    }

    private DecalTexture? GetDecalTexture(string name)
    {
        if (decalTextures.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var image = fileSystem?.FindDecal(name);
        DecalTexture? decal = null;

        if (image == null)
        {
            logger.LogDebug("GoldSrc decal {Texture} was not found", name);
        }
        else
        {
            var fileName = GetFileName(name);
            var texturePath = $"materials/goldsrc/decals/{fileName}.vtex";
            var materialPath = $"materials/goldsrc/decals/{fileName}.vmat";
            var usesIndexAsAlpha = GoldSrcTextureConverter.UsesIndexAsAlpha(image);

            var texture = usesIndexAsAlpha
                ? GoldSrcTextureConverter.ConvertIndexAlpha(image)
                : GoldSrcTextureConverter.Convert(image, transparentLastColor: true);

            map.AddFile(texturePath, texture);
            map.AddFile(materialPath, CreateDecalMaterial(materialPath, texturePath, image, usesIndexAsAlpha));

            decal = new DecalTexture(materialPath, image.Width, image.Height);
        }

        decalTextures.Add(name, decal);

        return decal;
    }

    private static Resource CreateDecalMaterial(string path, string texturePath, PalettedImage image, bool usesIndexAsAlpha)
    {
        var material = new ResourceTypes.Material
        {
            Resource = new Resource(ResourceType.Material),
            Name = path,
            ShaderName = ShaderName,
        };

        material.IntParams["F_ALPHA_TEST"] = usesIndexAsAlpha ? 0 : 1;
        material.IntParams["F_TRANSLUCENT"] = usesIndexAsAlpha ? 1 : 0;
        material.IntParams["F_NO_Z_PREPASS"] = 1;
        material.IntParams["g_nSurfaceMode"] = SurfaceModeLit;
        material.IntAttributes["overlay"] = 1;
        material.VectorParams["g_vTextureSize"] = new Vector4(image.Width, image.Height, 0f, 0f);
        material.TextureParams["g_tColor"] = texturePath;

        return FinishMaterial(material, path);
    }

    #endregion

    private static string? GetToolMaterial(string textureName, bool forceTool)
    {
        if (ToolMaterials.TryGetValue(textureName, out var material))
        {
            return material;
        }

        return forceTool ? ToolsMaterials.Trigger : null;
    }

    // The edge winding of a face depends on the side of its plane the face is on, so match the plane normal
    internal static void ReverseIfBackFacing(Vector3[] positions, Vector3 normal)
    {
        var polygonNormal = Vector3.Zero;

        for (var i = 0; i < positions.Length; i++)
        {
            var current = positions[i];
            var next = positions[(i + 1) % positions.Length];

            polygonNormal.X += (current.Y - next.Y) * (current.Z + next.Z);
            polygonNormal.Y += (current.Z - next.Z) * (current.X + next.X);
            polygonNormal.Z += (current.X - next.X) * (current.Y + next.Y);
        }

        if (Vector3.Dot(polygonNormal, normal) < 0f)
        {
            Array.Reverse(positions);
        }
    }

    // Textures named -0 to -9 are variants of one texture. A face shows the variant its position in the grid of
    // texture-sized tiles picks, so neighboring tiles differ. The variant comes from a table of random numbers,
    // so any fixed table is as good.
    private int ResolveRandomTile(int textureIndex, int faceIndex)
    {
        var name = textureNames[textureIndex];

        if (!IsNumbered(name, '-'))
        {
            return textureIndex;
        }

        var variants = FindNumberedTextures('-', name.AsSpan(2));

        if (variants.Count < 2)
        {
            return textureIndex;
        }

        var texture = GetTexture(textureIndex);
        var extent = bsp.LightmapExtents[faceIndex];

        // The origin keeps the tile numbers of faces left of or below the origin of the texture positive
        var tileX = (int)MathF.Floor(extent.MinS * 16f / texture.Width) + RandomTileOrigin;
        var tileY = (int)MathF.Floor(extent.MinT * 16f / texture.Height) + RandomTileOrigin;
        var random = RandomTileTable[((tileX % RandomTileGrid) * RandomTileGrid) + (tileY % RandomTileGrid)];

        return variants[random % variants.Count];
    }

    private static int[] CreateRandomTileTable()
    {
        var random = new Random(0);
        var table = new int[RandomTileGrid * RandomTileGrid];

        for (var i = 0; i < table.Length; i++)
        {
            table[i] = random.Next(0x7FFF);
        }

        return table;
    }

    // Whether a texture is named like one of a numbered series: a prefix, a digit and the name of the series
    private static bool IsNumbered(string name, char prefix)
        => name.Length >= 3 && name[0] == prefix && char.IsAsciiDigit(name[1]);

    // Finds the textures of a series from the digit 0 up to the first one the map does not have
    private List<int> FindNumberedTextures(char prefix, ReadOnlySpan<char> seriesName)
    {
        var found = new List<int>();

        for (var digit = 0; digit < 10; digit++)
        {
            var name = string.Create(CultureInfo.InvariantCulture, $"{prefix}{digit}{seriesName}");

            if (!textureIndices.TryGetValue(name, out var index))
            {
                break;
            }

            found.Add(index);
        }

        return found;
    }

    // Textures named +0 to +9 are the frames of an animation. An entity in its alternate state shows the series named
    // +a to +j instead, as does a face that uses one of those names. The first frame stands for the whole series.
    private int ResolveAnimationBase(int textureIndex, bool alternate)
    {
        var name = textureNames[textureIndex];

        if (!IsAnimated(name))
        {
            return textureIndex;
        }

        var useAlternate = alternate || char.IsAsciiLetter(name[1]);
        var seriesName = name.AsSpan(2);

        if (useAlternate)
        {
            var alternateName = string.Concat("+a", seriesName);

            if (textureIndices.TryGetValue(alternateName, out var alternateIndex))
            {
                return alternateIndex;
            }
        }

        var baseName = string.Concat("+0", seriesName);

        return textureIndices.GetValueOrDefault(baseName, textureIndex);
    }

    private static bool IsAnimated(string name)
        => name.Length >= 3 && name[0] == '+' && (char.IsAsciiDigit(name[1]) || name[1] is >= 'a' and <= 'j');

    // Finds the frames of an animation from its first frame up to the first one the map does not have
    private List<int> FindAnimationFrames(string firstFrameName)
    {
        var found = new List<int>();
        var firstCharacter = firstFrameName[1];
        var seriesName = firstFrameName.AsSpan(2);

        for (var frame = 0; frame < 10; frame++)
        {
            var name = string.Create(CultureInfo.InvariantCulture, $"+{(char)(firstCharacter + frame)}{seriesName}");

            if (!textureIndices.TryGetValue(name, out var index))
            {
                break;
            }

            found.Add(index);
        }

        return found;
    }

    // The images of an animation, or none when a frame is missing or differs in size from the first
    private List<PalettedImage> GetAnimationImages(string firstFrameName)
    {
        var images = new List<PalettedImage>();

        foreach (var index in FindAnimationFrames(firstFrameName))
        {
            var image = bsp.ResolveTexture(index, FindTexture);

            if (image == null || (images.Count > 0 && (image.Width != images[0].Width || image.Height != images[0].Height || image.Mips.Length != images[0].Mips.Length)))
            {
                return [];
            }

            images.Add(image);
        }

        return images;
    }

    #endregion

    #region Materials

    private static bool UsesAmount(int mode) => mode is ModeColor or ModeTexture or ModeGlow or ModeAdditive;

    private static bool IsWater(string textureName)
    {
        return textureName.StartsWith('!')
            || textureName.StartsWith("laser", StringComparison.OrdinalIgnoreCase)
            || textureName.StartsWith("water", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsScrolling(string textureName) => textureName.StartsWith("scroll", StringComparison.OrdinalIgnoreCase);

    private static RenderSettings GetMaterialSettings(RenderSettings settings, string textureName)
    {
        var usesColor = settings.Mode == ModeColor || IsScrolling(textureName);

        return settings.WithoutUnused(UsesAmount(settings.Mode), usesColor);
    }

    // An entity sets the speed of a scrolling texture with its render color: the green and blue bytes are the
    // speed in sixteenths of a texel per second, and the texture moves backwards when red is 0
    private static float GetScrollSpeed(Vector3 renderColor)
    {
        var speed = ((((int)renderColor.Y) << 8) + (int)renderColor.Z) / 16f;

        return (int)renderColor.X == 0 ? -speed : speed;
    }

    private static Vector3 GetConveyorColor(float speed)
    {
        if (speed == 0f)
        {
            speed = 100f;
        }

        var speedCode = (int)(MathF.Abs(speed) * 16f);

        return new Vector3(speed < 0f ? 1f : 0f, speedCode >> 8, speedCode & 0xFF);
    }

    private TextureInfo GetTexture(int textureIndex)
    {
        if (textures.TryGetValue(textureIndex, out var cached))
        {
            return cached;
        }

        var name = textureNames[textureIndex];
        var image = bsp.ResolveTexture(textureIndex, FindTexture);
        TextureInfo info;

        if (image == null)
        {
            logger.LogDebug("GoldSrc texture {Texture} was not found", name);
            info = new TextureInfo(null, 64, 64, 1);
        }
        else
        {
            var path = $"materials/goldsrc/textures/{GetFileName(name)}.vtex";
            var isFirstFrame = IsAnimated(name) && name[1] is '0' or 'a';
            var frames = isFirstFrame ? GetAnimationImages(name) : [];

            if (frames.Count < 2)
            {
                frames = [];
            }

            if (!map.Contains(path))
            {
                var texture = frames.Count > 0
                    ? GoldSrcTextureConverter.ConvertFrames(frames, image.IsMasked)
                    : GoldSrcTextureConverter.Convert(image, image.IsMasked);

                map.AddFile(path, texture);
            }

            info = new TextureInfo(path, image.Width, image.Height, Math.Max(frames.Count, 1));
        }

        textures.Add(textureIndex, info);

        return info;
    }

    private PalettedImage? FindTexture(string name, IReadOnlyList<string> wads) => fileSystem?.FindTexture(name, wads);

    // Texture names hold characters that mark their kind, such as { and !
    private static string GetFileName(string textureName)
    {
        var name = new StringBuilder(textureName.Length);

        foreach (var character in textureName.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character) || character == '_')
            {
                name.Append(character);
            }
            else
            {
                name.Append(CultureInfo.InvariantCulture, $"-{(int)character:x}");
            }
        }

        return name.ToString();
    }

    private string GetMaterial(MaterialKey key)
    {
        if (materials.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var path = key.ToolMaterial == null ? GetTextureMaterialPath(key) : key.ToolMaterial;

        if (!materials.ContainsValue(path))
        {
            var material = key.ToolMaterial == null ? CreateTextureMaterial(key, path) : CreateToolMaterial(key.ToolMaterial, path);
            map.AddFile(path, material);
        }

        materials.Add(key, path);

        return path;
    }

    private string GetTextureMaterialPath(MaterialKey key)
    {
        var name = GetFileName(textureNames[key.Texture]);
        var render = key.Render;

        if (render == RenderSettings.Default)
        {
            return $"materials/goldsrc/{name}.vmat";
        }

        return string.Create(CultureInfo.InvariantCulture, $"materials/goldsrc/{name}_{render.Mode}_{render.Amount}_{(int)render.Color.X}_{(int)render.Color.Y}_{(int)render.Color.Z}.vmat");
    }

    private Resource CreateToolMaterial(string toolMaterial, string path)
    {
        var label = ToolsMaterials.GetToolsLabel(toolMaterial)
            ?? throw new InvalidOperationException($"{toolMaterial} has no tools label.");

        var texturePath = GetToolLabelPath(label);

        if (!map.Contains(texturePath))
        {
            map.AddFile(texturePath, CreateToolLabelTexture(label));
        }

        return CreateToolMaterial(toolMaterial, path, texturePath);
    }

    /// <summary>Gets the path of the generated texture that shows a tools label.</summary>
    /// <param name="label">The label of a tools material.</param>
    public static string GetToolLabelPath(ToolsMaterials.TextureLabel label) => $"materials/goldsrc/tools/{label.Key}.vtex";

    /// <summary>Creates the generated texture that shows a tools label.</summary>
    /// <param name="label">The label of a tools material.</param>
    public static Resource CreateToolLabelTexture(ToolsMaterials.TextureLabel label) => GoldSrcTextureConverter.ConvertLabel(label, ToolLabelOpacity);

    /// <summary>Creates a tools material, named as its Source 2 counterpart is, that shows the label of what it compiles to.</summary>
    /// <param name="toolMaterial">The path of the tools material, such as <c>materials/tools/toolsclip.vmat</c>.</param>
    /// <param name="path">The path of the material.</param>
    /// <param name="texturePath">The path of the label texture.</param>
    public static Resource CreateToolMaterial(string toolMaterial, string path, string texturePath)
    {
        var material = new ResourceTypes.Material
        {
            Resource = new Resource(ResourceType.Material),
            Name = toolMaterial,
            ShaderName = ShaderName,
        };

        // Tool faces carry world units as texture coordinates, so the label repeats after one tile
        material.IntParams["F_TRANSLUCENT"] = 1;
        material.IntParams["F_NO_Z_PREPASS"] = 1;
        material.IntParams["g_nSurfaceMode"] = SurfaceModeFullbright;
        material.VectorParams["g_vTextureSize"] = new Vector4(ToolLabelTileSize, ToolLabelTileSize, 0f, 0f);
        material.TextureParams["g_tColor"] = texturePath;
        material.IntAttributes["tools.toolsmaterial"] = 1;

        return FinishMaterial(material, path);
    }

    // Projects the label onto the plane of the face, upright on walls and facing the same way from outside
    private static Vector2 GetToolTextureCoordinates(Vector3 position, Vector3 normal)
    {
        var absolute = Vector3.Abs(normal);
        var isFloorOrCeiling = absolute.Z >= MathF.Max(absolute.X, absolute.Y);
        var up = isFloorOrCeiling ? Vector3.UnitY : Vector3.UnitZ;
        var right = Vector3.Normalize(Vector3.Cross(up, normal));
        var down = Vector3.Cross(right, normal);

        return new Vector2(Vector3.Dot(position, right), Vector3.Dot(position, down));
    }

    private Resource CreateTextureMaterial(MaterialKey key, string path)
    {
        var name = textureNames[key.Texture];
        var settings = key.Render;
        var texture = GetTexture(key.Texture);

        var isWater = IsWater(name);
        var isMasked = name.StartsWith('{') || settings.Mode == ModeSolid;
        var isAdditive = settings.Mode == ModeAdditive;
        var isTranslucent = UsesAmount(settings.Mode);

        // Only the normal and solid modes use the lightmap
        var surfaceMode = SurfaceModeLit;

        if (settings.Mode == ModeColor)
        {
            surfaceMode = SurfaceModeColor;
        }
        else if (isTranslucent || isWater)
        {
            surfaceMode = SurfaceModeFullbright;
        }

        var material = new ResourceTypes.Material
        {
            Resource = new Resource(ResourceType.Material),
            Name = path,
            ShaderName = ShaderName,
        };

        material.IntParams["F_ALPHA_TEST"] = isMasked ? 1 : 0;
        material.IntParams["F_TRANSLUCENT"] = isTranslucent ? 1 : 0;
        material.IntParams["F_ADDITIVE_BLEND"] = isAdditive ? 1 : 0;
        material.IntParams["F_WATER"] = isWater ? 1 : 0;
        material.IntParams["g_nSurfaceMode"] = surfaceMode;
        material.VectorParams["g_vTextureSize"] = new Vector4(texture.Width, texture.Height, 0f, 0f);

        // The color is in the gamma space of the frame buffer, and the shader works in linear space
        var color = settings.Mode == ModeColor ? settings.Color / 255f : Vector3.One;
        var linearColor = ColorSpace.SrgbGammaToLinear(color);
        material.VectorParams["g_vRenderColor"] = new Vector4(linearColor, settings.Alpha);

        var alphaReference = settings.Mode == ModeSolid ? SolidAlphaReference : 0f;

        // The depth prepass of the material tests with the same reference as the shader
        material.FloatParams["g_flAlphaReference"] = alphaReference;
        material.FloatParams["g_flAlphaTestReference"] = alphaReference;

        if (IsScrolling(name))
        {
            material.FloatParams["g_flTextureScroll"] = GetScrollSpeed(settings.Color);
        }

        if (isMasked || isTranslucent)
        {
            material.IntParams["F_NO_Z_PREPASS"] = 1;
        }

        if (texture.Path == null)
        {
            return FinishMaterial(material, path);
        }

        if (texture.Frames > 1)
        {
            material.IntParams["F_ANIMATED"] = 1;
            material.IntParams["g_nFrameCount"] = texture.Frames;
            material.TextureParams["g_tColorFrames"] = texture.Path;
        }
        else
        {
            material.TextureParams["g_tColor"] = texture.Path;
        }

        return FinishMaterial(material, path);
    }

    private static Resource FinishMaterial(ResourceTypes.Material material, string path)
    {
        var resource = material.Resource;
        resource.FileName = path;
        material.BuildData();
        resource.AddBlock(material);
        Source1ResourceReferences.Update(resource);

        return resource;
    }

    #endregion
}
