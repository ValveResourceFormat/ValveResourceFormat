using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Source1.Entities;
using ValveResourceFormat.Source1.Material;
using ValveResourceFormat.Source1.Model;
using ValveResourceFormat.Utils;
using S2Model = ValveResourceFormat.ResourceTypes.Model;
using VrfTexture = ValveResourceFormat.ResourceTypes.Texture;

namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// The Source 2 resources of a converted Source 1 map, by the path they are served at.
/// </summary>
internal sealed class Source1ConvertedMap
{
    /// <summary>The resources by normalized path, with the <c>_c</c> suffix of compiled files.</summary>
    public Dictionary<string, Resource> Files { get; } = [];

    /// <summary>The path of the map resource, which names the world beside it.</summary>
    public required string MapPath { get; init; }

    /// <summary>The entities as they were converted, in the order of the entity lump, followed by the cubemaps of the map.</summary>
    public required List<EntityLump.Entity> Entities { get; init; }

    /// <summary>The Source 1 entities, each at the index of the converted entity made from it in <see cref="Entities"/>.</summary>
    public required List<BspEntity> SourceEntities { get; init; }

    /// <summary>The Source 1 entities whose ropes and sprites the world draws, which need no stand-in.</summary>
    public HashSet<BspEntity> DrawnEntities { get; } = [];

    /// <summary>The model that stands for the sprite of an entity, which the entity draws itself.</summary>
    /// <param name="Path">The path of the model.</param>
    /// <param name="StartsDisabled">Whether the sprite is hidden when the map starts.</param>
    public readonly record struct EntitySpriteModel(string Path, bool StartsDisabled);

    /// <summary>The Source 1 entities whose sprite is a model of its own, so that it can be shown and hidden.</summary>
    public Dictionary<BspEntity, EntitySpriteModel> SpriteModels { get; } = [];

    /// <summary>A dynamic light at the end of the beam of a spotlight.</summary>
    /// <param name="Position">Where the light is.</param>
    /// <param name="Color">The color of the light, in 0-255 units.</param>
    /// <param name="Radius">How far the light reaches.</param>
    public readonly record struct SpotlightLight(Vector3 Position, Vector3 Color, float Radius);

    /// <summary>The lights of the spotlights whose beams the world draws.</summary>
    public List<SpotlightLight> SpotlightLights { get; } = [];

    /// <summary>The squared distances where the detail sprites start to fade and where they are gone, or null when the map has no detail props.</summary>
    public Vector2? DetailFadeSquared { get; set; }

    /// <summary>The paths of the materials of the surfaces that have a bumped lightmap.</summary>
    public required HashSet<string> BumpedMaterials { get; init; }

    /// <summary>The path of the brush model of a model of the map, <c>*N</c> in the entities.</summary>
    /// <param name="modelIndex">The index of the model in the map.</param>
    public string GetBrushModelPath(int modelIndex) => Source1WorldConverter.GetBrushModelPath(MapPath, modelIndex);
}

/// <summary>
/// Converts a Source 1 map to the resources a Source 2 world is made of: a map, a world with one world node, an entity lump,
/// and one model per brush model of the map.
/// </summary>
internal static class Source1WorldConverter
{
    private const string CompiledSuffix = "_c";
    private const string DefaultCubemapPath = "materials/engine/defaultcubemap.vtex";
    private const string SkyWorldGroup = "skybox";

    // The scale of a sky_camera that sets none, which the Source 2 sky camera would read as 1
    private const float DefaultSkyScale = 16f;

    private const int BloomBlurBuffers = 5;

    // The lightmap version whose light store the Source 1 lights use
    private const long LightmapVersion = 8;
    private const long LightmapGameVersion = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct BrushVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector4 Tangent;
        public Vector2 TexCoord;
        public Vector4 LightmapCoord;
        public uint Color;
        public uint BlendValues;
    }

    private static uint PackUnorm8x4(Vector4 value)
    {
        var x = (uint)Source1VertexBuffers.ToUnorm8(value.X);
        var y = (uint)Source1VertexBuffers.ToUnorm8(value.Y);
        var z = (uint)Source1VertexBuffers.ToUnorm8(value.Z);
        var w = (uint)Source1VertexBuffers.ToUnorm8(value.W);

        return x | (y << 8) | (z << 16) | (w << 24);
    }

    // No semantic names the lightmap coordinates, so the field names the shader input directly. The lightmap coordinates
    // hold the UV in xy.
    private static readonly VertexField[] BrushVertexFields =
    [
        new(nameof(BrushVertex.Position), "POSITION", 0, DXGI_FORMAT.R32G32B32_FLOAT),
        new(nameof(BrushVertex.Normal), "NORMAL", 0, DXGI_FORMAT.R32G32B32_FLOAT),
        new(nameof(BrushVertex.Tangent), "TANGENT", 0, DXGI_FORMAT.R32G32B32A32_FLOAT),
        new(nameof(BrushVertex.TexCoord), "TEXCOORD", 0, DXGI_FORMAT.R32G32_FLOAT),
        new(nameof(BrushVertex.LightmapCoord), "LIGHTMAPUV", 0, DXGI_FORMAT.R32G32B32A32_FLOAT, "vLightmapUV"),
        new(nameof(BrushVertex.Color), "COLOR", 0, DXGI_FORMAT.R8G8B8A8_UNORM),
        new(nameof(BrushVertex.BlendValues), "TEXCOORD", 4, DXGI_FORMAT.R8G8B8A8_UNORM),
    ];

    /// <summary>The lightmap textures of a world.</summary>
    internal readonly record struct WorldLightmaps(List<string> Paths);

    // Faces outside any cluster are drawn in chunks of this size, so that the frustum can cull the ones out of sight
    internal const float WorldChunkSize = 1024f;

    // The layer that the aggregate scene objects of a compiled world are in
    private const string AggregateLayerName = "world_layer_base";

    // A chunk is the faces of one brush, or of one cluster, or of one cell of the grid when its cluster is negative
    private readonly record struct WorldChunk(bool InSky, int Brush, int Cluster, int X, int Y, int Z);

    private sealed class WorldChunkContents
    {
        public List<MaterialPiece> Pieces { get; } = [];
        public SortedSet<int> Clusters { get; } = [];
    }

    private readonly record struct MaterialPiece(string Material, BspVertex[] Vertices, int[] Indices);

    private readonly record struct AggregatePiece(MaterialPiece Piece, IReadOnlyCollection<int> Clusters);

    /// <summary>Converts a map.</summary>
    /// <param name="bsp">The map.</param>
    /// <param name="getMaterialInfo">Reads the texture, the sprite settings and the decal scale of a material, or returns null when it has no texture. Sprites, ropes and decals of such materials are left out.</param>
    /// <param name="locator">Finds the 3D sky area of the faces of the world.</param>
    /// <param name="loadModel">Loads a model of the game by path, or returns null when it has none. Props whose model is missing are left out.</param>
    /// <param name="openFile">Opens a file of the game or of the pakfile of the map, such as the baked lighting of a static prop.</param>
    /// <param name="logger">Receives a warning for each part of the map that is left out.</param>
    /// <param name="sky">The 2D sky, which the map draws with an <c>env_sky</c>, or null when it has none.</param>
    /// <param name="progress">Receives the phase the conversion is in, or null.</param>
    public static Source1ConvertedMap Convert(
        BspFile bsp,
        Func<string, Source1Model?> loadModel,
        Func<string, Stream?> openFile,
        Func<string, Source1MaterialInfo?> getMaterialInfo,
        IBrushLightingLocator locator,
        ILogger logger,
        Source1Sky? sky = null,
        IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(bsp);
        ArgumentNullException.ThrowIfNull(loadModel);
        ArgumentNullException.ThrowIfNull(openFile);

        var mapPath = $"maps/{Source1Paths.Normalize(bsp.Name)}";
        var worldFolder = $"{mapPath}/";
        var sourceEntities = new List<BspEntity>();
        var entityLump = Source1EntityConverter.CreateLump(bsp.Name);
        var skyEntities = new List<EntityLump.Entity>();

        progress?.Report("Converting entities…");
        var entities = ConvertEntities(bsp, mapPath, entityLump, sourceEntities, locator, skyEntities, loadModel, openFile);

        progress?.Report("Building world geometry…");
        var modelMeshes = BspMeshBuilder.BuildAll(bsp, getMaterialInfo);

        if (sky != null)
        {
            entities.Add(CreateSkyEntity(entityLump, sky));
        }

        var postProcessPath = $"{mapPath}/postprocess.vpost";
        entities.Add(CreatePostProcessVolume(entityLump, postProcessPath));

        var map = new Source1ConvertedMap
        {
            MapPath = mapPath,
            Entities = entities,
            SourceEntities = sourceEntities,
            BumpedMaterials = GetBumpedMaterials(modelMeshes),
        };

        AddFile(map, $"{mapPath}.vmap", CreateMap(worldFolder));
        AddFile(map, postProcessPath, CreatePostProcessing());

        if (sky != null)
        {
            AddFile(map, sky.MaterialPath, sky.Material);
            AddFile(map, sky.TexturePath, sky.Texture);
        }

        progress?.Report("Building lightmaps…");
        var lightmaps = AddLightmaps(map, bsp);

        progress?.Report("Baking light probes…");
        var skyProbeVolumes = new List<EntityLump.Entity>();
        Source1LightProbeBaker.Add(map, bsp, entityLump, locator, skyProbeVolumes);

        progress?.Report("Converting world models…");
        var worldParts = new List<string>();
        var skyParts = new List<string>();
        var worldPartClusters = new WorldPartClusters();
        var skyPartClusters = new WorldPartClusters();

        if (modelMeshes.Length > 0)
        {
            AddWorldModels(map, bsp, new BspBrushSet(bsp), modelMeshes[0], locator, entityLump, skyEntities, worldParts, skyParts, worldPartClusters, skyPartClusters, getMaterialInfo);
        }

        for (var i = 1; i < modelMeshes.Length; i++)
        {
            var path = GetBrushModelPath(mapPath, i);
            var pieces = GetPieces(modelMeshes[i]);

            AddFile(map, path, CreateBrushModel(pieces, path));
        }

        progress?.Report("Converting world physics…");
        Source1WorldPhysicsConverter.Convert(map, bsp, getMaterialInfo, logger);

        progress?.Report("Converting world visibility…");

        var worldVisibility = Source1WorldVisibilityConverter.Convert(bsp);

        if (worldVisibility != null)
        {
            AddFile(map, $"{worldFolder}world_visibility.vvis", worldVisibility);
        }

        progress?.Report("Converting detail props…");
        var detail = Source1DetailPropConverter.Convert(map, bsp, locator);
        worldParts.AddRange(detail.WorldParts);
        skyParts.AddRange(detail.SkyParts);

        progress?.Report("Converting ropes and sprites…");
        Source1RopeConverter.Convert(map, bsp, locator, getMaterialInfo, worldParts, skyParts);
        Source1SpriteConverter.Convert(map, bsp, locator, getMaterialInfo, worldParts, skyParts);
        MarkDrawnByWorld(map, entities);
        AddSpotlightLights(map, bsp, entityLump, entities);

        var nodePath = $"{worldFolder}world_nodes/node001";
        var lumpPath = $"{worldFolder}entities/default_ents";

        AddFile(map, $"{worldFolder}world.vwrld", CreateWorld($"{worldFolder}world.vwrld", lumpPath, nodePath, lightmaps));
        var worldProps = new List<int>();
        var skyProps = new List<int>();

        for (var i = 0; i < bsp.StaticProps.Props.Count; i++)
        {
            var prop = bsp.StaticProps.Props[i];

            if (prop.ModelName.Length == 0)
            {
                continue;
            }

            if (locator.IsInSky(prop.Origin))
            {
                skyProps.Add(i);
            }
            else
            {
                worldProps.Add(i);
            }
        }

        var skyCamera = FindSkyCamera(entities, locator);

        if (skyCamera != null)
        {
            skyCamera["scale"] = MathF.Max(skyCamera.GetFloatProperty("scale", DefaultSkyScale), 1f);
            skyEntities.Add(skyCamera);
        }

        var worldEntities = new List<EntityLump.Entity>(entities.Count - skyEntities.Count);

        foreach (var entity in entities)
        {
            if (!skyEntities.Contains(entity))
            {
                worldEntities.Add(entity);
            }
        }

        progress?.Report("Converting static props…");
        AddFile(map, $"{nodePath}.vwnod", CreateWorldNode($"{nodePath}.vwnod", worldParts, worldPartClusters, bsp, worldProps, detail.WorldModels, loadModel, openFile));

        if (skyParts.Count > 0 || skyPartClusters.HasAggregates || skyProps.Count > 0 || detail.SkyModels.Count > 0 || skyEntities.Count > 0)
        {
            var skyLumpEntities = GetSkyLumpEntities(entityLump, skyProbeVolumes, skyEntities, skyCamera);

            AddSkyWorld(map, bsp, skyParts, skyPartClusters, skyProps, detail.SkyModels, skyLumpEntities, lightmaps, loadModel, openFile);
            worldEntities.Add(CreateSkyboxReference(entityLump, map.MapPath));
        }

        OrderLightsBySpawn(worldEntities);
        AddFile(map, $"{lumpPath}.vents", CreateEntityLump($"{lumpPath}.vents", bsp.Name, worldEntities));

        foreach (var file in map.Files.Values)
        {
            Source1ResourceReferences.Update(file);
        }

        return map;
    }

    // A scene stores the lights in the order they spawn, and drops the ones past what the store holds, so the lights
    // nearest to the first player spawn come first. The lights swap places and the other entities keep theirs.
    private static void OrderLightsBySpawn(List<EntityLump.Entity> entities)
    {
        var spawn = entities.Find(static entity => entity.GetStringProperty("classname") is { } name
            && (name == "info_team_spawn" || name.StartsWith("info_player_", StringComparison.Ordinal)));

        if (spawn == null)
        {
            return;
        }

        var spawnOrigin = spawn.GetVector3Property("origin");
        var slots = new List<int>();
        var lights = new List<EntityLump.Entity>();

        for (var i = 0; i < entities.Count; i++)
        {
            if (entities[i].GetStringProperty("classname") is { } name && name.StartsWith("light_", StringComparison.Ordinal))
            {
                slots.Add(i);
                lights.Add(entities[i]);
            }
        }

        var ordered = lights.OrderBy(light => Vector3.DistanceSquared(light.GetVector3Property("origin"), spawnOrigin)).ToList();

        for (var i = 0; i < slots.Count; i++)
        {
            entities[slots[i]] = ordered[i];
        }
    }

    // The first sky_camera is the one the engine draws the 3D sky from, when it is in an area of its own
    private static EntityLump.Entity? FindSkyCamera(List<EntityLump.Entity> entities, IBrushLightingLocator locator)
    {
        foreach (var entity in entities)
        {
            if (entity.GetStringProperty("classname") != "sky_camera")
            {
                continue;
            }

            return locator.IsInSky(entity.GetVector3Property("origin")) ? entity : null;
        }

        return null;
    }

    // The sky world is lit by the probes of its own area and fogged by the fog of the sky_camera, which the lump of the sky
    // holds as the entities of a Source 2 sky map do
    private static List<EntityLump.Entity> GetSkyLumpEntities(
        EntityLump lump,
        List<EntityLump.Entity> skyProbeVolumes,
        List<EntityLump.Entity> skyEntities,
        EntityLump.Entity? skyCamera)
    {
        var skyLumpEntities = new List<EntityLump.Entity>(skyEntities);
        skyLumpEntities.AddRange(skyProbeVolumes);

        if (skyCamera != null && skyCamera.GetBooleanProperty("fogenable"))
        {
            skyLumpEntities.Add(CreateSkyFog(lump, skyCamera));
        }

        return skyLumpEntities;
    }

    private static EntityLump.Entity CreateSkyFog(EntityLump lump, EntityLump.Entity skyCamera)
    {
        var fog = new EntityLump.Entity { ParentLump = lump };
        fog["classname"] = "env_fog_controller";
        fog["fogcolor"] = "255 255 255";
        fog["fogcolor2"] = "255 255 255";
        fog["fogstart"] = 500f;
        fog["fogend"] = 2000f;

        string[] keys = ["fogenable", "fogcolor", "fogcolor2", "fogstart", "fogend", "fogmaxdensity", "fogblend", "fogdir", "use_angles", "angles"];

        foreach (var key in keys)
        {
            if (skyCamera.TryGetValue(key, out var value))
            {
                fog[key] = value;
            }
        }

        SetFogsObjects(fog);

        return fog;
    }

    // The map the reference places is the sky world, in a world group of its own that is drawn as the 3D sky. The sky world
    // keeps the coordinates of the map, so the reference sits at the origin and the sky_camera is seen from its own position.
    private static EntityLump.Entity CreateSkyboxReference(EntityLump lump, string mapPath)
    {
        var reference = new EntityLump.Entity { ParentLump = lump };
        reference["classname"] = "skybox_reference";
        reference["origin"] = "0 0 0";
        reference["targetmapname"] = $"{mapPath}/sky.vmap";
        reference["worldgroupid"] = SkyWorldGroup;

        return reference;
    }

    // The 3D sky box area is a world of its own, which the map does not list, that the sky reference places
    private static void AddSkyWorld(
        Source1ConvertedMap map,
        BspFile bsp,
        List<string> parts,
        WorldPartClusters partClusters,
        List<int> propIndices,
        List<Source1DetailPropConverter.DetailModel> detailModels,
        List<EntityLump.Entity> entities,
        WorldLightmaps lightmaps,
        Func<string, Source1Model?> loadModel,
        Func<string, Stream?> openFile)
    {
        var skyFolder = $"{map.MapPath}/sky/";
        var worldPath = $"{skyFolder}world.vwrld";
        var nodePath = $"{skyFolder}world_nodes/node001";
        var lumpPath = $"{skyFolder}entities/default_ents";

        AddFile(map, $"{map.MapPath}/sky.vmap", CreateMap(skyFolder));
        Source1ResourceReferences.Add(map.Files[$"{map.MapPath}.vmap{GameFileLoader.CompiledFileSuffix}"], $"{map.MapPath}/sky.vmap");
        AddFile(map, worldPath, CreateWorld(worldPath, lumpPath, nodePath, lightmaps));
        AddFile(map, $"{nodePath}.vwnod", CreateWorldNode($"{nodePath}.vwnod", parts, partClusters, bsp, propIndices, detailModels, loadModel, openFile));
        AddFile(map, $"{lumpPath}.vents", CreateEntityLump($"{lumpPath}.vents", $"{bsp.Name}_sky", entities));
    }

    private static HashSet<string> GetBumpedMaterials(BspModelMesh[] modelMeshes)
    {
        var materials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var model in modelMeshes)
        {
            foreach (var (name, mesh) in model.Materials)
            {
                if ((mesh.Flags & BspMeshFlags.Bumped) != 0)
                {
                    materials.Add(Source1Paths.ToMaterialPath(name));
                }
            }
        }

        return materials;
    }

    // The flat lightmap and the three bump basis lightmaps are textures of their own, named for the samplers that read them
    private static WorldLightmaps AddLightmaps(Source1ConvertedMap map, BspFile bsp)
    {
        var layout = bsp.LightmapLayout;
        var atlas = layout.BuildAtlas();
        var paths = new List<string>(LightmapAtlas.PageCount);

        for (var page = 0; page < atlas.Pages.Length; page++)
        {
            var name = page == 0
                ? "lightmap"
                : string.Create(CultureInfo.InvariantCulture, $"lightmap_bump{page}");
            var path = $"{map.MapPath}/lightmaps/{name}.vtex";
            var pixels = MemoryMarshal.AsBytes(atlas.Pages[page].AsSpan()).ToArray();

            var texture = VrfTexture.Create((ushort)atlas.Width, (ushort)atlas.Height, VTexFormat.RGBA16161616F, 0, Vector4.One, [pixels]);

            AddFile(map, path, texture);
            paths.Add(path);
        }

        return new WorldLightmaps(paths);
    }

    internal static string GetBrushModelPath(string mapPath, int modelIndex, int part = 0)
    {
        var name = mapPath[(mapPath.LastIndexOf('/') + 1)..];

        if (part == 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{mapPath}/models/{name}_c0_{modelIndex}.vmdl");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{mapPath}/models/{name}_c0_{modelIndex}_{part}.vmdl");
    }

    internal static void AddFile(Source1ConvertedMap map, string path, Resource resource)
    {
        resource.FileName = path + CompiledSuffix;
        map.Files.Add(resource.FileName, resource);
    }

    private static List<MaterialPiece> GetPieces(BspModelMesh model)
    {
        var pieces = new List<MaterialPiece>();

        foreach (var (materialName, materialMesh) in model.Materials)
        {
            if (materialMesh.Indices.Length == 0)
            {
                continue;
            }

            pieces.Add(new MaterialPiece(materialName, materialMesh.Vertices, materialMesh.Indices));
        }

        return pieces;
    }

    // The faces of the world model become models of their own, so that visibility and the frustum can cull them.
    // A face goes with the lowest cluster that lists it, and the model keeps every cluster of its faces. The faces
    // inside the area of the 3D sky box have no visibility and form a model of their own.
    private static void AddWorldModels(
        Source1ConvertedMap map,
        BspFile bsp,
        BspBrushSet brushes,
        BspModelMesh world,
        IBrushLightingLocator locator,
        EntityLump entityLump,
        List<EntityLump.Entity> skyEntities,
        List<string> worldParts,
        List<string> skyParts,
        WorldPartClusters partClusters,
        WorldPartClusters skyPartClusters,
        Func<string, Source1MaterialInfo?> getMaterialInfo)
    {
        var faceClusters = bsp.Visibility.ClusterCount > 0 ? new BspFaceClusters(bsp) : null;
        var faceBrushes = new BspFaceBrushes(bsp, brushes, bsp.GetLightmappedFaces(preferHdr: true));
        var groups = new Dictionary<WorldChunk, WorldChunkContents>();
        var waterGroups = new Dictionary<WorldChunk, WorldChunkContents>();
        var worldFragments = new List<AggregatePiece>();
        var skyFragments = new List<AggregatePiece>();

        foreach (var (materialName, mesh) in world.Materials)
        {
            var piece = new MaterialPiece(materialName, mesh.Vertices, mesh.Indices);
            var isBlended = getMaterialInfo(materialName)?.IsBlended ?? true;
            var isWater = (mesh.Flags & BspMeshFlags.Water) != 0;
            var triangles = new Dictionary<WorldChunk, List<int>>();
            var chunkClusters = new Dictionary<WorldChunk, SortedSet<int>>();

            foreach (var range in mesh.FaceRanges)
            {
                var clusters = faceClusters?.Get(range.Face) ?? [];
                var cluster = faceClusters?.GetPrimaryCluster(range.Face) ?? -1;
                var center = GetFaceCenter(piece, range);
                var chunk = GetWorldChunk(center, cluster, locator);
                var brush = faceBrushes.Find(range.Face, center);

                if (brush >= 0)
                {
                    chunk = new WorldChunk(chunk.InSky, brush, -1, 0, 0, 0);
                }

                if (!triangles.TryGetValue(chunk, out var starts))
                {
                    starts = [];
                    triangles.Add(chunk, starts);
                }

                var end = range.FirstIndex + range.IndexCount;

                for (var i = range.FirstIndex; i + 2 < end; i += 3)
                {
                    starts.Add(i);
                }

                if (chunk.Cluster >= 0 || chunk.Brush >= 0)
                {
                    if (!chunkClusters.TryGetValue(chunk, out var union))
                    {
                        union = [];
                        chunkClusters.Add(chunk, union);
                    }

                    union.UnionWith(clusters);
                }
            }

            foreach (var (chunk, starts) in triangles)
            {
                var subPiece = CreateSubPiece(piece, starts);
                var fragmentClusters = chunkClusters.GetValueOrDefault(chunk) ?? [];

                if (isWater)
                {
                    GetChunkContents(waterGroups, chunk).Pieces.Add(subPiece);
                }
                else if (isBlended)
                {
                    var contents = GetChunkContents(groups, chunk);
                    contents.Pieces.Add(subPiece);
                    contents.Clusters.UnionWith(fragmentClusters);
                }
                else
                {
                    var fragments = chunk.InSky ? skyFragments : worldFragments;
                    fragments.Add(new AggregatePiece(subPiece, fragmentClusters));
                }
            }
        }

        var part = 0;
        AddAggregateModels(map, worldFragments, partClusters, ref part);
        AddAggregateModels(map, skyFragments, skyPartClusters, ref part);

        foreach (var overlay in world.Overlays)
        {
            if (overlay.Vertices.Length == 0)
            {
                continue;
            }

            var clusters = new SortedSet<int>();

            foreach (var face in overlay.OverlayFaces)
            {
                clusters.UnionWith(faceClusters?.Get(face) ?? []);
            }

            var cluster = clusters.Count > 0 ? clusters.Min : -1;
            var chunk = GetWorldChunk(overlay.Vertices[0].Position, cluster, locator);
            var materialPath = Source1Paths.ToMaterialPath(overlay.Material);
            var overlayMaterial = Source1Paths.AddVariant(materialPath, Source1ResourceProvider.OverlayVariant);
            var contents = GetChunkContents(groups, chunk);

            contents.Pieces.Add(new MaterialPiece(overlayMaterial, overlay.Vertices, overlay.Indices));

            if (chunk.Cluster >= 0)
            {
                contents.Clusters.UnionWith(clusters);
            }
        }

        foreach (var (chunk, contents) in groups)
        {
            var path = GetBrushModelPath(map.MapPath, 0, part);

            AddFile(map, path, CreateBrushModel(contents.Pieces, path));

            if (chunk.InSky)
            {
                skyParts.Add(path);
            }
            else
            {
                worldParts.Add(path);
                partClusters.Set(path, contents.Clusters);
            }

            part++;
        }

        AddWaterEntities(map, entityLump, skyEntities, waterGroups, ref part);
    }

    // The water surfaces are brush entities of their own, as Source 2 maps have them, which leave the world model
    private static void AddWaterEntities(
        Source1ConvertedMap map,
        EntityLump entityLump,
        List<EntityLump.Entity> skyEntities,
        Dictionary<WorldChunk, WorldChunkContents> waterGroups,
        ref int part)
    {
        foreach (var (chunk, contents) in waterGroups)
        {
            var path = GetBrushModelPath(map.MapPath, 0, part++);

            AddFile(map, path, CreateBrushModel(contents.Pieces, path));

            var water = new EntityLump.Entity { ParentLump = entityLump };
            water["classname"] = "func_water";
            water["model"] = Source1ResourceReferences.Reference(path);
            water["origin"] = "0 0 0";

            map.Entities.Add(water);

            if (chunk.InSky)
            {
                skyEntities.Add(water);
            }
        }
    }

    private static WorldChunkContents GetChunkContents(Dictionary<WorldChunk, WorldChunkContents> groups, WorldChunk chunk)
    {
        if (!groups.TryGetValue(chunk, out var contents))
        {
            contents = new WorldChunkContents();
            groups.Add(chunk, contents);
        }

        return contents;
    }

    // Faces that no leaf with a cluster lists are grouped by the grid instead
    private static WorldChunk GetWorldChunk(Vector3 position, int cluster, IBrushLightingLocator locator)
    {
        if (locator.IsInSky(position))
        {
            return new WorldChunk(true, -1, -1, 0, 0, 0);
        }

        if (cluster >= 0)
        {
            return new WorldChunk(false, -1, cluster, 0, 0, 0);
        }

        return new WorldChunk(
            false,
            -1,
            -1,
            (int)MathF.Floor(position.X / WorldChunkSize),
            (int)MathF.Floor(position.Y / WorldChunkSize),
            (int)MathF.Floor(position.Z / WorldChunkSize));
    }

    // The average of the corners of the triangles of a face, which is inside it since faces are convex
    private static Vector3 GetFaceCenter(MaterialPiece piece, BspFaceRange range)
    {
        var sum = Vector3.Zero;
        var count = 0;
        var end = range.FirstIndex + range.IndexCount;

        for (var i = range.FirstIndex; i < end; i++)
        {
            sum += piece.Vertices[piece.Indices[i]].Position;
            count++;
        }

        return count > 0 ? sum / count : Vector3.Zero;
    }

    // The opaque faces of a world are the fragments of aggregates, so each brush can be picked and selected on its own.
    // An aggregate is drawn with the material of its first draw call, so each material has an aggregate of its own.
    private static void AddAggregateModels(Source1ConvertedMap map, List<AggregatePiece> pieces, WorldPartClusters partClusters, ref int part)
    {
        var piecesByMaterial = new Dictionary<string, List<AggregatePiece>>(StringComparer.OrdinalIgnoreCase);

        foreach (var aggregatePiece in pieces)
        {
            if (!piecesByMaterial.TryGetValue(aggregatePiece.Piece.Material, out var materialPieces))
            {
                materialPieces = [];
                piecesByMaterial.Add(aggregatePiece.Piece.Material, materialPieces);
            }

            materialPieces.Add(aggregatePiece);
        }

        foreach (var materialPieces in piecesByMaterial.Values)
        {
            AddAggregateModel(map, materialPieces, partClusters, ref part);
        }
    }

    private static void AddAggregateModel(Source1ConvertedMap map, List<AggregatePiece> pieces, WorldPartClusters partClusters, ref int part)
    {
        var path = GetBrushModelPath(map.MapPath, 0, part++);
        var materialPieces = new List<MaterialPiece>(pieces.Count);
        var fragments = new List<AggregateFragment>(pieces.Count);
        var drawBounds = new List<AABB>(pieces.Count);

        foreach (var aggregatePiece in pieces)
        {
            var bounds = GetBounds(aggregatePiece.Piece);

            materialPieces.Add(aggregatePiece.Piece);
            fragments.Add(new AggregateFragment(bounds, aggregatePiece.Clusters));
            drawBounds.Add(bounds);
        }

        AddFile(map, path, CreateBrushModel(materialPieces, path, drawBounds));
        partClusters.AddAggregate(path, fragments);
    }

    private static AABB GetBounds(MaterialPiece piece)
    {
        var (min, max) = Source1VertexBuffers.GetBounds(piece.Vertices, vertex => vertex.Position);

        return new AABB(min, max);
    }

    private static MaterialPiece CreateSubPiece(MaterialPiece source, List<int> triangleStarts)
    {
        var vertices = new List<BspVertex>();
        var indices = new List<int>(triangleStarts.Count * 3);
        var remap = new Dictionary<int, int>();

        foreach (var start in triangleStarts)
        {
            for (var corner = 0; corner < 3; corner++)
            {
                var original = source.Indices[start + corner];

                if (!remap.TryGetValue(original, out var mapped))
                {
                    mapped = vertices.Count;
                    remap.Add(original, mapped);
                    vertices.Add(source.Vertices[original]);
                }

                indices.Add(mapped);
            }
        }

        return new MaterialPiece(source.Material, [.. vertices], [.. indices]);
    }

    private static List<EntityLump.Entity> ConvertEntities(
        BspFile bsp,
        string mapPath,
        EntityLump lump,
        List<BspEntity> sources,
        IBrushLightingLocator locator,
        List<EntityLump.Entity> skyEntities,
        Func<string, Source1Model?> loadModel,
        Func<string, Stream?> openFile)
    {
        var options = Source1EntityConverterOptions.ForMap(bsp);
        var entities = new List<EntityLump.Entity>(bsp.Entities.Count);
        var entityModels = new Source1EntityModels(openFile, bsp.Name, bsp.Entities);

        foreach (var source in bsp.Entities)
        {
            if (Source1EntityConverter.Convert(source, lump, options) is not { } entity)
            {
                continue;
            }

            Source1PhysicsPropExport.Mark(source.ClassName, entity);

            var brushModelIndex = ConvertModel(bsp, mapPath, source, entity);
            var isInSky = false;

            if (brushModelIndex > 0)
            {
                isInSky = IsBrushEntityInSky(bsp, source, brushModelIndex, locator);
            }
            else if (ConvertEntityModel(entityModels, source, entity, loadModel))
            {
                isInSky = locator.IsInSky(source.Origin);
            }

            if (isInSky)
            {
                skyEntities.Add(entity);
            }

            SetTonemapDefaults(entity);
            SetFogsObjects(entity);
            SetParticlePath(source, entity);

            sources.Add(source);
            entities.Add(entity);
        }

        AddCubemaps(bsp, lump, entities);

        return entities;
    }

    // The map compiler keeps no env_cubemap entities, only the samples it baked. A model takes the nearest sample, so the
    // influence of each reaches every point of the world, and the nearest one wins among the overlapping probes. A map
    // without samples reflects the default cube map of the engine everywhere.
    private static void AddCubemaps(BspFile bsp, EntityLump lump, List<EntityLump.Entity> entities)
    {
        if (bsp.Models.Length == 0)
        {
            return;
        }

        var world = bsp.Models[0];
        var radius = (world.Max - world.Min).Length();

        if (bsp.Cubemaps.Length == 0)
        {
            var center = (world.Min + world.Max) * 0.5f;
            entities.Add(CreateCubemap(lump, center, DefaultCubemapPath, radius));
            return;
        }

        var extension = bsp.HasHdrLighting ? Source1Paths.HdrTextureExtension : Source1Paths.TextureExtension;

        foreach (var sample in bsp.Cubemaps)
        {
            var name = string.Create(CultureInfo.InvariantCulture, $"c{sample.OriginX}_{sample.OriginY}_{sample.OriginZ}");
            var texture = $"materials/maps/{bsp.Name}/{name}{extension}";

            entities.Add(CreateCubemap(lump, sample.Origin, texture, radius));
        }
    }

    private static EntityLump.Entity CreateCubemap(EntityLump lump, Vector3 origin, string texture, float radius)
    {
        var entity = new EntityLump.Entity { ParentLump = lump };
        entity["classname"] = "env_cubemap";
        entity["origin"] = Source1EntityConverter.FormatVector(origin);
        entity["cubemaptexture"] = Source1ResourceReferences.Reference(texture);
        entity["influenceradius"] = radius;
        entity[Source1MapExport.RegroupKey] = 1L;

        return entity;
    }

    private static EntityLump.Entity CreateSkyEntity(EntityLump lump, Source1Sky sky)
    {
        var entity = new EntityLump.Entity { ParentLump = lump };
        entity["classname"] = "env_sky";
        entity["skyname"] = Source1ResourceReferences.Reference(sky.MaterialPath);
        entity["fog_type"] = 0L;

        // The tint color is a gamma color of at most one, so a brighter tint moves its scale into the brightness
        var brightness = MathF.Max(MathF.Max(sky.Tint.X, sky.Tint.Y), sky.Tint.Z);

        if (sky.Tint != Vector3.One && brightness > 0f)
        {
            var tintColor = ColorSpace.SrgbLinearToGamma(sky.Tint / brightness) * 255f;
            entity["tint_color"] = Source1EntityConverter.FormatVector(tintColor);
            entity["brightnessscale"] = brightness;
        }

        return entity;
    }

    // Brush entities draw the brush model of the map, which is hidden when the entity starts hidden. The sprite and
    // material of other entities are drawn by their own builders, and their model is set by ConvertEntityModel.
    // Returns the index of the brush model the entity draws, or 0 when it draws none.
    private static int ConvertModel(BspFile bsp, string mapPath, BspEntity source, EntityLump.Entity entity)
    {
        var model = source.GetString("model");

        if (model.Length == 0)
        {
            return 0;
        }

        if (model[0] == '*')
        {
            var isBrushModel = int.TryParse(model.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                && index > 0
                && index < bsp.Models.Length;

            if (!isBrushModel)
            {
                entity["model"] = string.Empty;
                return 0;
            }

            SetStartDisabled(source, entity);
            entity["model"] = Source1ResourceReferences.Reference(GetBrushModelPath(mapPath, index));
            return index;
        }

        var isSource1Model = model.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)
            || model.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase)
            || model.EndsWith(".spr", StringComparison.OrdinalIgnoreCase);

        if (isSource1Model)
        {
            entity["model"] = string.Empty;
        }

        return 0;
    }

    // A brush entity is placed by its entity transform, so it is in the 3D sky box area when the center of its model is in it
    private static bool IsBrushEntityInSky(BspFile bsp, BspEntity source, int modelIndex, IBrushLightingLocator locator)
    {
        var bounds = bsp.Models[modelIndex];
        var transform = EntityTransformHelper.ToRigidTransformationMatrix(source.Angles, source.Origin);
        var center = Vector3.Transform((bounds.Min + bounds.Max) * 0.5f, transform);

        return locator.IsInSky(center);
    }

    // The entity classes load an effect by the path in its key, and the provider serves a particle system by that path
    private static void SetParticlePath(BspEntity source, EntityLump.Entity entity)
    {
        if (!source.ClassName.Equals("info_particle_system", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var name = source.GetString("effect_name");

        if (!string.IsNullOrEmpty(name))
        {
            entity["effect_name"] = Source1ResourceReferences.Reference(Source1ResourceProvider.GetParticlePath(name));
        }
    }

    // A Source 1 tonemap controller sets its exposure range through inputs, so without keys it would clamp the exposure to zero
    private static void SetTonemapDefaults(EntityLump.Entity entity)
    {
        if (entity.GetStringProperty("classname") != "env_tonemap_controller" || entity.ContainsKey("maxexposure"))
        {
            return;
        }

        entity["minexposure"] = 0.5f;
        entity["maxexposure"] = 2f;
        entity["rate"] = 1f;
    }

    // The fog of a fog controller covers everything the game draws, not only the 2D sky
    private static void SetFogsObjects(EntityLump.Entity entity)
    {
        if (entity.GetStringProperty("classname") != "env_fog_controller")
        {
            return;
        }

        var start = entity.GetFloatProperty("fogstart");
        var end = entity.GetFloatProperty("fogend");

        if (end > start)
        {
            entity["fogobjects"] = true;
        }

        SetFogCurve(entity, start, end);
    }

    // Source 1 fogs a pixel by the square of min(maxdensity, saturate((distance - start) / (end - start))). That is
    // the Source 2 fog curve raised to the power 2, once the density cap moves into the end distance and the maximum
    // density is squared.
    private static void SetFogCurve(EntityLump.Entity entity, float start, float end)
    {
        var maxDensity = Math.Clamp(entity.GetFloatProperty("fogmaxdensity", 1f), 0f, 1f);

        if (end > start && maxDensity > 0f)
        {
            entity["fogend"] = start + (end - start) * maxDensity;
        }

        entity["fogmaxdensity"] = maxDensity * maxDensity;
        entity["fogexponent"] = 2f;
    }

    // The lights at the ends of spotlight beams are not entities of the map, so they follow its entities like the cubemaps
    private static void AddSpotlightLights(Source1ConvertedMap map, BspFile bsp, EntityLump lump, List<EntityLump.Entity> entities)
    {
        if (map.SpotlightLights.Count == 0)
        {
            return;
        }

        var options = Source1EntityConverterOptions.ForMap(bsp);

        foreach (var light in map.SpotlightLights)
        {
            var entity = new EntityLump.Entity { ParentLump = lump };
            entity["origin"] = Source1EntityConverter.FormatVector(light.Position);
            entity["classname"] = Source1LightConverter.ApplyDynamicLight(entity, light.Color, light.Radius, options);

            entities.Add(entity);
        }
    }

    // The world draws the ropes and sprites of these entities, so a tool draws no stand-in for them
    private static void MarkDrawnByWorld(Source1ConvertedMap map, List<EntityLump.Entity> entities)
    {
        for (var i = 0; i < map.SourceEntities.Count; i++)
        {
            var entity = entities[i];
            var source = map.SourceEntities[i];

            if (map.DrawnEntities.Contains(source))
            {
                entity["drawnbyworld"] = true;
            }

            if (map.SpriteModels.TryGetValue(source, out var spriteModel))
            {
                entity["classname"] = "func_brush";
                entity["model"] = Source1ResourceReferences.Reference(spriteModel.Path);

                // The angles of a sprite are part of its quad
                entity["angles"] = Source1EntityConverter.FormatVector(Vector3.Zero);

                if (spriteModel.StartsDisabled)
                {
                    entity["startdisabled"] = 1L;
                }
            }
            else if (Source1SpriteConverter.IsSpriteClass(source.ClassName))
            {
                // The key of a sprite names its sprite file, which the entity cannot load as a model
                entity.Remove("model");
            }
        }
    }

    // An entity that does not draw its model at spawn keeps it, and starts disabled so its inputs can show it
    private static void SetStartDisabled(BspEntity source, EntityLump.Entity entity)
    {
        if (Source1EntityConverter.IsHiddenAtSpawn(source))
        {
            entity["startdisabled"] = 1L;
        }
    }

    // The game code sets the model of characters, pickups and weapons, and props take it from their model key. The class of
    // the entity draws it, in the pose of the sequence the entity starts with. Returns whether the entity has a model.
    private static bool ConvertEntityModel(Source1EntityModels entityModels, BspEntity source, EntityLump.Entity entity, Func<string, Source1Model?> loadModel)
    {
        if (entityModels.IsSpawnedLater(source))
        {
            return false;
        }

        if (entityModels.GetModel(source) is not { } entityModel)
        {
            return false;
        }

        SetStartDisabled(source, entity);
        entity["model"] = Source1ResourceReferences.Reference(Source1Paths.ToModelPath(entityModel.Path));

        // A model that does not load is drawn as the error model by the entity
        var model = loadModel(entityModel.Path);

        if (model == null)
        {
            return true;
        }

        entity["skin"] = Source1ModelConverter.GetSkinName(model, entityModel.Skin);
        entity["model_state"] = EntityLump.StringifyValue(CreateModelState(model, entityModel));

        // The body value picks a sub-model of every body group, which the model state holds by name
        entity.Remove("body");

        var sequence = FindStartSequence(model, source, entityModel);

        if (sequence != null)
        {
            entity["defaultanim"] = sequence;
        }

        return true;
    }

    private static KVObject CreateModelState(Source1Model model, Source1EntityModel entityModel)
    {
        var choices = new int[model.BodyParts.Length];

        for (var i = 0; i < model.BodyParts.Length; i++)
        {
            var part = model.BodyParts[i];

            if (part.Base > 0 && part.Models.Length > 0)
            {
                choices[i] = entityModel.Body / part.Base % part.Models.Length;
            }
        }

        foreach (var (group, subModel) in entityModel.BodyGroups)
        {
            if ((uint)group < (uint)choices.Length && (uint)subModel < (uint)model.BodyParts[group].Models.Length)
            {
                choices[group] = subModel;
            }
        }

        var bodyGroups = KVObject.Collection();
        var addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The game finds a body group by name and takes the first one that matches
        for (var i = 0; i < choices.Length; i++)
        {
            var name = model.BodyParts[i].Name;

            if (addedNames.Add(name))
            {
                bodyGroups.Add(name, (long)choices[i]);
            }
        }

        var state = KVObject.Collection();
        state.Add("body_groups", bodyGroups);

        return state;
    }

    // The sequence the entity names, then the one its game code starts with, then the sequence of its activity, and an
    // entity that names no animation keeps the sequence index of its sequence key, which is 0 when it has none
    private static string? FindStartSequence(Source1Model model, BspEntity source, Source1EntityModel entityModel)
    {
        var named = FindSequence(model, source.GetString("DefaultAnim")) ?? FindSequence(model, entityModel.Sequence);

        if (named != null)
        {
            return named;
        }

        if (entityModel.Activity != null && FindSequenceByActivity(model, entityModel.Activity) is { } activitySequence)
        {
            return activitySequence;
        }

        var index = source.GetInt("sequence");

        if ((uint)index >= (uint)model.Sequences.Length)
        {
            return null;
        }

        return model.GetExportName(model.Sequences[index]);
    }

    private static string? FindSequence(Source1Model model, string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        foreach (var sequence in model.Sequences)
        {
            if (sequence.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return model.GetExportName(sequence);
            }
        }

        return null;
    }

    // The game picks the sequence of an activity with the highest weight most often
    private static string? FindSequenceByActivity(Source1Model model, string activityName)
    {
        Source1Sequence? best = null;

        foreach (var sequence in model.Sequences)
        {
            var isActivity = sequence.ActivityName.Equals(activityName, StringComparison.OrdinalIgnoreCase);

            if (sequence.IsDelta || !isActivity || !sequence.IsPlayable)
            {
                continue;
            }

            if (best == null || sequence.ActivityWeight > best.ActivityWeight)
            {
                best = sequence;
            }
        }

        return best == null ? null : model.GetExportName(best);
    }

    // The game draws a map with bloom and its own tonemap, which a master volume of the map sets
    internal static EntityLump.Entity CreatePostProcessVolume(EntityLump lump, string postProcessPath)
    {
        var entity = new EntityLump.Entity { ParentLump = lump };
        entity["classname"] = "post_processing_volume";
        entity["origin"] = "0 0 0";
        entity["master"] = true;
        entity["postprocessing"] = Source1ResourceReferences.Reference(postProcessPath);

        return entity;
    }

    /// <summary>Creates the map file, which names the folder of its world through the world manifest it references.</summary>
    /// <param name="worldFolder">The folder of the world, ending with a slash.</param>
    internal static Resource CreateMap(string worldFolder)
    {
        var resource = new Resource(ResourceType.Map);
        Source1ResourceReferences.Add(resource, $"{worldFolder}world.vrman");
        Source1ResourceReferences.Add(resource, $"{worldFolder}world.vwrld");

        return resource;
    }

    /// <summary>Creates the manifest that names the physics resource of the world.</summary>
    /// <param name="physicsPath">The path of the world physics resource without the compiled suffix.</param>
    internal static Resource CreateWorldPhysicsManifest(string physicsPath)
    {
        var resource = new Resource(ResourceType.ResourceManifest, 1);
        Source1ResourceReferences.Add(resource, physicsPath);

        var manifest = new ResourceManifest { Resource = resource };
        manifest.Resources.Add([physicsPath]);
        resource.AddBlock(manifest);

        return resource;
    }

    // The game scales the scene by its exposure and clamps it, with no curve, which the nearly straight curve of these
    // tonemap parameters stands in for.
    internal static Resource CreatePostProcessing()
    {
        var tonemap = KVObject.Collection();
        tonemap.Add("m_flExposureBias", 0f);
        tonemap.Add("m_flShoulderStrength", 0f);
        tonemap.Add("m_flLinearStrength", 0.001f);
        tonemap.Add("m_flLinearAngle", 0.001f);
        tonemap.Add("m_flToeStrength", 1f);
        tonemap.Add("m_flToeNum", 1f);
        tonemap.Add("m_flToeDenom", 1f);
        tonemap.Add("m_flWhitePoint", 1.5f);

        var data = KVObject.Collection();
        data.Add("m_bHasTonemapParams", true);
        data.Add("m_toneMapParams", tonemap);
        data.Add("m_bHasColorCorrection", false);
        data.Add("m_bHasBloomParams", true);
        data.Add("m_bloomParams", CreateBloomParams());

        var resource = new Resource(ResourceType.PostProcessing);
        resource.AddBlock(new PostProcessing { Resource = resource, Data = data });

        return resource;
    }

    private static KVObject CreateBloomParams()
    {
        var blurWeight = KVObject.Array();
        var blurTint = KVObject.Array();

        for (var i = 0; i < BloomBlurBuffers; i++)
        {
            blurWeight.Add(0.2f);
            blurTint.Add(KVHelpers.ToKVArray(Vector3.One));
        }

        var bloom = KVObject.Collection();
        bloom.Add("m_blendMode", "BLOOM_BLEND_ADD");
        bloom.Add("m_flBloomStrength", 1f);
        bloom.Add("m_flScreenBloomStrength", 0f);
        bloom.Add("m_flBlurBloomStrength", 0f);
        bloom.Add("m_flBloomThreshold", 1.05f);
        bloom.Add("m_flBloomThresholdWidth", 1.661f);
        bloom.Add("m_flSkyboxBloomStrength", 1f);
        bloom.Add("m_flBloomStartValue", 1f);
        bloom.Add("m_flBlurWeight", blurWeight);
        bloom.Add("m_vBlurTint", blurTint);

        return bloom;
    }

    internal static Resource CreateWorld(string path, string lumpPath, string nodePath, WorldLightmaps worldLightmaps)
    {
        var resource = new Resource(ResourceType.World) { FileName = path };
        var lumps = KVObject.Array();
        lumps.Add(Source1ResourceReferences.Reference($"{lumpPath}.vents"));

        var node = KVObject.Collection();
        node.Add("m_worldNodePrefix", nodePath);

        var nodes = KVObject.Array();
        nodes.Add(node);

        var lightmaps = KVObject.Array();

        foreach (var lightmapPath in worldLightmaps.Paths)
        {
            lightmaps.Add(Source1ResourceReferences.Reference(lightmapPath));
        }

        var lighting = KVObject.Collection();
        lighting.Add("m_nLightmapVersionNumber", LightmapVersion);
        lighting.Add("m_nLightmapGameVersionNumber", LightmapGameVersion);
        lighting.Add("m_lightMaps", lightmaps);

        var data = KVObject.Collection();
        data.Add("m_entityLumps", lumps);
        data.Add("m_worldNodes", nodes);
        data.Add("m_worldLightingInfo", lighting);

        resource.AddBlock(new World { Resource = resource, Data = data });
        Source1ResourceReferences.Add(resource, $"{nodePath}.vwnod");

        return resource;
    }

    // A scene object places each model of the world at the origin
    internal static KVObject CreateSceneObjects(List<string> parts) => new WorldPartClusters().CreateSceneObjects(parts, out _);

    private static Resource CreateWorldNode(
        string path,
        List<string> parts,
        WorldPartClusters partClusters,
        BspFile bsp,
        List<int> propIndices,
        List<Source1DetailPropConverter.DetailModel> detailModels,
        Func<string, Source1Model?> loadModel,
        Func<string, Stream?> openFile)
    {
        var resource = new Resource(ResourceType.WorldNode) { FileName = path };
        var sceneObjects = partClusters.CreateSceneObjects(parts, out var visClusterMembership);
        var props = new Source1StaticPropConverter(bsp, loadModel, openFile, sceneObjects);

        foreach (var propIndex in propIndices)
        {
            props.Add(propIndex);
        }

        foreach (var detailModel in detailModels)
        {
            props.AddDetailModel(detailModel.ModelName, detailModel.Transform);
        }

        var data = KVObject.Collection();
        data.Add("m_sceneObjects", sceneObjects);
        data.Add("m_visClusterMembership", visClusterMembership);

        var layerNames = KVObject.Array();

        if (partClusters.HasAggregates)
        {
            data.Add("m_aggregateSceneObjects", partClusters.CreateAggregateSceneObjects(visClusterMembership));
            layerNames.Add(AggregateLayerName);
        }

        data.Add("m_layerNames", layerNames);

        if (props.ExtraVertexStreams.Count > 0)
        {
            data.Add("m_extraVertexStreams", props.ExtraVertexStreams);
            data.Add("m_extraVertexStreamOverrides", props.ExtraVertexStreamOverrides);
        }

        resource.AddBlock(new WorldNode { Resource = resource, Data = data });

        return resource;
    }

    // The transform of a scene object is four columns of four floats each
    internal static KVObject ToColumns(Matrix4x4 value) => KVHelpers.MakeArray(
        KVHelpers.MakeArray(value.M11, value.M21, value.M31, value.M41),
        KVHelpers.MakeArray(value.M12, value.M22, value.M32, value.M42),
        KVHelpers.MakeArray(value.M13, value.M23, value.M33, value.M43),
        KVHelpers.MakeArray(value.M14, value.M24, value.M34, value.M44));

    internal static Resource CreateEntityLump(string path, string name, List<EntityLump.Entity> entities)
    {
        var lump = Source1EntityConverter.CreateLump(name);
        var resource = lump.Resource!;
        var keyValues = KVObject.Array();

        foreach (var entity in entities)
        {
            var values = KVObject.Collection();
            values.Add("version", 1L);
            values.Add("values", entity);

            var connections = KVObject.Array();

            foreach (var connection in entity.Connections ?? [])
            {
                var converted = KVObject.Collection();
                converted.Add("m_targetName", connection.TargetName);
                converted.Add("m_outputName", connection.OutputName);
                converted.Add("m_inputName", connection.InputName);
                converted.Add("m_overrideParam", connection.OverrideParam);
                converted.Add("m_flDelay", connection.Delay);
                converted.Add("m_nTimesToFire", (long)connection.TimesToFire);
                converted.Add("m_targetType", (long)connection.TargetType);
                connections.Add(converted);
            }

            var item = KVObject.Collection();
            item.Add("keyValues3Data", values);
            item.Add("m_connections", connections);
            keyValues.Add(item);
        }

        lump.Data.Remove("m_entityKeyValues");
        lump.Data.Add("m_entityKeyValues", keyValues);
        resource.FileName = path;
        resource.AddBlock(lump);

        return resource;
    }

    private static Resource CreateBrushModel(List<MaterialPiece> pieces, string path, List<AABB>? drawBounds = null)
    {
        // A volume that draws nothing is only its collision, as in compiled Source 2 maps
        if (pieces.Count == 0)
        {
            return S2Model.Create(path, [], [], [], [], 0UL);
        }

        var vertices = new List<BrushVertex>();
        var indices = new List<int>();
        var drawCalls = new List<MeshDrawCall>();
        var materials = new List<string>();

        foreach (var piece in pieces)
        {
            var material = Source1Paths.ToMaterialPath(piece.Material);
            var drawCall = new MeshDrawCall(material, vertices.Count, piece.Vertices.Length, indices.Count, piece.Indices.Length);

            if (!materials.Contains(material, StringComparer.OrdinalIgnoreCase))
            {
                materials.Add(material);
            }

            foreach (var vertex in piece.Vertices)
            {
                vertices.Add(CreateVertex(vertex));
            }

            indices.AddRange(piece.Indices);
            drawCalls.Add(drawCall);
        }

        var vertexArray = vertices.ToArray();
        var (min, max) = Source1VertexBuffers.GetBounds(vertexArray, vertex => vertex.Position);
        var buffers = Source1VertexBuffers.Create(vertexArray, BrushVertexFields, indices.ToArray());
        var mesh = new ModelMeshSource("brush", buffers, drawCalls, min, max) { MeshGroupMask = 1UL, DrawBounds = drawBounds };
        var materialGroups = new List<ModelMaterialGroup> { new(Source1ModelConverter.GetSkinName(0), materials) };

        return S2Model.Create(path, [mesh], [], materialGroups, ["brush"], 1UL);
    }

    private static BrushVertex CreateVertex(in BspVertex vertex)
    {
        var alpha = Source1VertexBuffers.ToUnorm8(vertex.DisplacementAlpha);

        return new BrushVertex
        {
            Position = vertex.Position,
            Normal = vertex.Normal,
            Tangent = new Vector4(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z, -vertex.Tangent.W),
            TexCoord = vertex.TexCoord,
            LightmapCoord = new Vector4(vertex.LightmapCoord, 0f, 0f),
            Color = 0x00FFFFFFu | ((uint)alpha << 24),
            BlendValues = PackUnorm8x4(vertex.BlendValues),
        };
    }
}
