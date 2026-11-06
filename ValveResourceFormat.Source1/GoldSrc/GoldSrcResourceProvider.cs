using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Source1;
using ValveResourceFormat.Source1.Audio;
using ValveResourceFormat.Source1.Entities;
using ValveResourceFormat.Source1.Material;
using ValveResourceFormat.Source1.Physics;
using S2Model = ValveResourceFormat.ResourceTypes.Model;

namespace ValveResourceFormat.GoldSrc;

/// <summary>
/// Serves the files of a GoldSrc game as Source 2 resources: a <c>.mdl</c> as a model, a <c>.spr</c> as a texture, the
/// <c>.wav</c> of <c>sound/name</c> as the sound <c>sounds/name.vsnd</c>, and the converted files of a loaded map and of every converted model by their paths. The paths may carry the <c>_c</c>
/// suffix of compiled files.
/// </summary>
/// <param name="fileSystem">The game, or <see langword="null"/> when the file is not in a known game.</param>
/// <param name="logger">Where failures to load a file are reported.</param>
internal sealed class GoldSrcResourceProvider(GoldSrcFileSystem? fileSystem, ILogger logger) : IResourceProvider
{
    private const string ModelExtension = ".mdl";
    private const string SpriteExtension = ".spr";
    private const string Source2ModelExtension = ".vmdl";

    private readonly Dictionary<string, Resource> files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GoldSrcConvertedModel?> models = new(StringComparer.OrdinalIgnoreCase);
    private readonly Source1SoundFiles sounds = new(null, fileSystem == null ? null : fileSystem.EnumerateWaves, logger, GoldSrcSurfaceProperties.CreateStepEvents(fileSystem));

    /// <summary>The game, or <see langword="null"/> when the file is not in a known game.</summary>
    public GoldSrcFileSystem? FileSystem => fileSystem;

    /// <summary>The game that materials are written for when they are extracted.</summary>
    public Game MaterialTargetGame { get; set; } = Game.Csgo;

    /// <inheritdoc/>
    public ContentFile? ExtractMaterial(ResourceTypes.Material material)
    {
        if (Source1MaterialConverter.IsSkyMaterial(material))
        {
            return MaterialTranslator.Translate(material, Source1MaterialConverter.DescribeSkyMaterial(material), MaterialTargetGame, LoadFile, logger);
        }

        if (!GoldSrcMaterialReader.CanRead(material))
        {
            return null;
        }

        return MaterialTranslator.Translate(material, GoldSrcMaterialReader.Read(material), MaterialTargetGame, LoadFile, logger);
    }

    /// <summary>Converts a map and serves its map, world, world node, entity lump, brush models and materials by path.</summary>
    /// <param name="bsp">The map.</param>
    /// <returns>The converted map.</returns>
    /// <param name="progress">Receives the phase the conversion is in, or <see langword="null"/>.</param>
    public GoldSrcConvertedMap LoadMap(GoldSrcBspFile bsp, IProgress<string>? progress = null)
    {
        var map = GoldSrcWorldConverter.Convert(bsp, fileSystem, LoadModel, logger, progress);
        AddFiles(map.Files);

        return map;
    }

    /// <summary>Converts a model once, from the data of its file, and serves it and its materials by path.</summary>
    /// <param name="source">The model, with the files it keeps its textures and sequences in loaded.</param>
    /// <param name="path">The path of the model in the game, such as <c>models/scientist.mdl</c>.</param>
    /// <returns>The converted model.</returns>
    public S2Model AddModel(GoldSrcMdlFile source, string path)
    {
        var converted = GoldSrcModelConverter.Convert(source, path, logger);

        lock (models)
        {
            models[path] = converted;
            AddFiles(converted.Files);
        }

        return converted.Model;
    }

    /// <summary>Converts a model of the game once, for everything that draws it.</summary>
    /// <param name="path">The path of the model in the game, such as <c>models/scientist.mdl</c>.</param>
    /// <returns>The converted model, or <see langword="null"/> when the game has none of that name or it does not load.</returns>
    public GoldSrcConvertedModel? LoadModel(string path)
    {
        lock (models)
        {
            if (models.TryGetValue(path, out var cached))
            {
                return cached;
            }

            var model = ConvertModel(path);
            models[path] = model;

            return model;
        }
    }

    private GoldSrcConvertedModel? ConvertModel(string path)
    {
        var diskPath = fileSystem?.FindFile(path);

        if (diskPath == null)
        {
            return null;
        }

        try
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var source = GoldSrcMdlFile.Load(File.ReadAllBytes(diskPath), name, companion => GoldSrcFileSystem.ReadCompanion(diskPath, companion, fileSystem));
            var converted = GoldSrcModelConverter.Convert(source, path, logger);
            AddFiles(converted.Files);

            return converted;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException or UnexpectedMagicException)
        {
            logger.LogWarning("Failed to load model {Model}: {Message}", path, e.Message);
            return null;
        }
    }

    private Resource? LoadSprite(string path)
    {
        var diskPath = fileSystem?.FindFile(path);

        if (diskPath == null)
        {
            return null;
        }

        try
        {
            return GoldSrcSpriteConverter.Convert(new SprFile(File.ReadAllBytes(diskPath)));
        }
        catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException)
        {
            logger.LogWarning("Failed to load sprite {Sprite}: {Message}", path, e.Message);
            return null;
        }
    }

    private Resource? LoadSound(string name)
    {
        var diskPath = fileSystem?.FindFile(Source1Paths.ToWavePath(name));

        if (diskPath == null)
        {
            return null;
        }

        try
        {
            return SoundConverter.ConvertWave(File.ReadAllBytes(diskPath));
        }
        catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException)
        {
            logger.LogWarning("Failed to load sound {Sound}: {Message}", name, e.Message);
            return null;
        }
    }

    // A sprite is converted once, for everything that draws it
    private Resource? AddSprite(string sourcePath, string gamePath)
    {
        var sprite = LoadSprite(gamePath);

        if (sprite == null)
        {
            return null;
        }

        sprite.FileName = sourcePath + GameFileLoader.CompiledFileSuffix;

        lock (files)
        {
            files[sourcePath] = sprite;
        }

        return sprite;
    }

    // A tools material that no map converted shows its label like the ones the maps convert
    private Resource AddToolMaterial(string path, ToolsMaterials.TextureLabel label)
    {
        var texturePath = GoldSrcMapConversion.GetToolLabelPath(label);
        var material = GoldSrcMapConversion.CreateToolMaterial(path, path, texturePath);
        material.FileName = path + GameFileLoader.CompiledFileSuffix;

        lock (files)
        {
            if (!files.ContainsKey(texturePath))
            {
                var texture = GoldSrcMapConversion.CreateToolLabelTexture(label);
                texture.FileName = texturePath + GameFileLoader.CompiledFileSuffix;
                files[texturePath] = texture;
            }

            files[path] = material;
        }

        return material;
    }

    private void AddFiles(IReadOnlyDictionary<string, Resource> converted)
    {
        lock (files)
        {
            foreach (var (path, resource) in converted)
            {
                files[Source1Paths.NormalizeCompiled(path)] = resource;
            }
        }
    }

    // Entities name their models by the path of the game's file, and materials name a sprite's texture as a texture
    private static string GetGameFile(string path)
    {
        if (path.EndsWith(Source2ModelExtension, StringComparison.Ordinal))
        {
            return Source1Paths.ToGameModelPath(path);
        }

        return Source1Paths.ToGameSpritePath(path);
    }

    /// <inheritdoc/>
    public Stream? GetFileStream(string file)
    {
        if (Source1Paths.Normalize(file) != Source1SurfaceProperties.FootstepsPath)
        {
            return null;
        }

        return new MemoryStream(Encoding.UTF8.GetBytes(GoldSrcSurfaceProperties.CreateFootstepScript()));
    }

    /// <inheritdoc/>
    public EntityClassTable GetEntityClasses(ILogger logger)
    {
        var table = Source1EntityClassCatalog.GetGoldSrc(fileSystem?.GameDirectories ?? [], logger);

        return Source1EntityClassCatalog.WithoutUndrawnHelpers(table);
    }

    /// <inheritdoc/>
    public List<ResourceTypes.EntityLump.Entity> GetExportEntities(List<ResourceTypes.EntityLump.Entity> entities) => Source1MapExport.Regroup(entities);

    /// <inheritdoc/>
    public void AddExportProperties(string className, EditGameClassProps properties)
        => Source1MapExport.AddRequiredProperties(className, properties);

    /// <inheritdoc/>
    public IEnumerable<string> GetExportResources(Resource entityLump) => Source1MapExport.GetResources(entityLump);

    /// <inheritdoc/>
    public bool Exists(string file)
    {
        var path = Source1Paths.NormalizeCompiled(file);

        lock (files)
        {
            if (files.ContainsKey(path))
            {
                return true;
            }
        }

        if (path is Source1SurfaceProperties.ResourcePath or Source1SurfaceProperties.FootstepsPath)
        {
            return true;
        }

        if (Source1Paths.TryGetSoundName(path, out var soundName))
        {
            return fileSystem?.FindFile(Source1Paths.ToWavePath(soundName)) != null;
        }

        if (ToolsMaterials.GetToolsLabel(path) != null)
        {
            return true;
        }

        var gameFile = GetGameFile(path);

        if (gameFile.EndsWith(ModelExtension, StringComparison.Ordinal) || gameFile.EndsWith(SpriteExtension, StringComparison.Ordinal))
        {
            return fileSystem?.FindFile(gameFile) != null;
        }

        return sounds.Exists(path);
    }

    /// <inheritdoc/>
    public Resource? LoadFile(string file)
    {
        var sourcePath = Source1Paths.NormalizeCompiled(file);

        lock (files)
        {
            if (files.TryGetValue(sourcePath, out var converted))
            {
                return converted;
            }
        }

        if (sourcePath == Source1SurfaceProperties.ResourcePath)
        {
            var surfaces = GoldSrcSurfaceProperties.CreateResource();
            surfaces.FileName = file;

            return surfaces;
        }

        if (Source1Paths.TryGetSoundName(sourcePath, out var soundName))
        {
            return LoadSound(soundName);
        }

        if (ToolsMaterials.GetToolsLabel(sourcePath) is { } label)
        {
            return AddToolMaterial(sourcePath, label);
        }

        var gameFile = GetGameFile(sourcePath);

        if (gameFile.EndsWith(ModelExtension, StringComparison.Ordinal))
        {
            return LoadModel(gameFile)?.Model.Resource;
        }

        if (gameFile.EndsWith(SpriteExtension, StringComparison.Ordinal))
        {
            return AddSprite(sourcePath, gameFile);
        }

        return sounds.Load(sourcePath);
    }
}
