using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.GoldSrc;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Source1.Audio;
using ValveResourceFormat.Source1.Bsp;
using ValveResourceFormat.Source1.Choreo;
using ValveResourceFormat.Source1.Entities;
using ValveResourceFormat.Source1.FileSystem;
using ValveResourceFormat.Source1.Material;
using ValveResourceFormat.Source1.Model;
using ValveResourceFormat.Source1.Navigation;
using ValveResourceFormat.Source1.Particles;
using ValveResourceFormat.Source1.Physics;
using ValveResourceFormat.Source1.Texture;
using VrfTexture = ValveResourceFormat.ResourceTypes.Texture;

namespace ValveResourceFormat.Source1;

/// <summary>
/// Serves the files of a Source 1 game as Source 2 resources: a <c>.vmt</c> as the material of the same name with <c>.vmat</c>,
/// a <c>.vtf</c> as the texture of the same name with <c>.vtex</c> and the <c>.wav</c> or <c>.mp3</c> of <c>sound/name</c> as the
/// sound <c>sounds/name.vsnd</c>. The paths may carry the <c>_c</c> suffix of compiled files.
/// </summary>
internal sealed class Source1ResourceProvider(Source1FileSystem fileSystem, ILogger logger) : IResourceProvider
{
    private const string MaterialExtension = ".vmat";
    private const string ModelExtension = ".mdl";
    private const string NavMeshExtension = ".nav";
    private const string ParticleFolder = "particles/";
    private const string ParticleExtension = ".vpcf";
    private const string Source2ModelExtension = ".vmdl";

    // The model of the game that is drawn for a model that is missing
    private const string GameErrorModelPath = "models/error.mdl";

    private readonly Source1ParticleLibrary particles = new(fileSystem, logger);
    private readonly Source1SoundFiles sounds = new(fileSystem.OpenFile, fileSystem.EnumerateWaves, logger);
    private readonly Source1SoundscapeScripts soundscapes = new(fileSystem.OpenFile, logger);

    /// <summary>The materials of the eyes and teeth of the models this provider converts.</summary>
    internal Source1ModelMaterials ModelMaterials { get; } = new();

    /// <summary>The variant name of a texture that holds every frame of an animated texture in a grid.</summary>
    public const string TextureSheetVariant = "sheet";

    private const string AmbientBoostParameter = "g_flAmbientBoost";
    private const string SecondTexCoordFeature = "F_FORCE_UV2";

    /// <summary>The variant name of a material that is drawn on top of the surface under it.</summary>
    public const string OverlayVariant = "overlay";

    /// <summary>The variant name of a material that is drawn as a detail sprite, which fades with the distance.</summary>
    public const string DetailSpriteVariant = "detailsprite";

    /// <summary>The variant name of a material that is drawn as the ribbon of a rope.</summary>
    public const string CableVariant = "cable";

    /// <summary>The variant name of a texture that is stored as BC7.</summary>
    public const string Bc7TextureVariant = "bc7";

    /// <summary>The variant name of a material that is drawn as a decal projected onto the world.</summary>
    public const string ProjectedDecalVariant = "projecteddecal";

    /// <summary>The variant name of a material of the game that is drawn as the icon of an entity in the editor.</summary>
    public const string IconVariant = "icon";

    private const string SpriteVariantPrefix = "sprite";
    private const string CookieVariantPrefix = "cookie";

    /// <summary>Gets the variant name of a material that is drawn as the sprite of an entity of a render mode.</summary>
    /// <param name="renderMode">The render mode of the entity.</param>
    internal static string GetSpriteVariant(RenderMode renderMode)
        => string.Create(CultureInfo.InvariantCulture, $"{SpriteVariantPrefix}{(int)renderMode}");

    /// <summary>Gets the variant name of a texture that holds a light cookie, which is stored uncompressed.</summary>
    /// <param name="frame">The frame of an animated texture.</param>
    internal static string GetCookieVariant(int frame)
        => string.Create(CultureInfo.InvariantCulture, $"{CookieVariantPrefix}{frame}");

    /// <summary>
    /// Whether materials are loaded in high dynamic range. It has to be set before the materials of a map are loaded.
    /// </summary>
    public bool HighDynamicRange { get; set; } = true;

    /// <summary>The game that materials are written for when they are extracted.</summary>
    public Game MaterialTargetGame { get; set; } = Game.Csgo;

    private Source1ConvertedMap? map;

    // The map each converted entity lump was made from
    private readonly ConditionalWeakTable<ResourceTypes.EntityLump, BspFile> lumpMaps = new();

    /// <inheritdoc/>
    public ContentFile? ExtractMaterial(ResourceTypes.Material material)
    {
        if (Source1MaterialConverter.IsSkyMaterial(material))
        {
            return MaterialTranslator.Translate(material, Source1MaterialConverter.DescribeSkyMaterial(material), MaterialTargetGame, LoadFile, logger);
        }

        if (Source1MaterialConverter.IsCableMaterial(material))
        {
            return MaterialTranslator.Translate(material, Source1MaterialConverter.DescribeCableMaterial(material), MaterialTargetGame, LoadFile, logger);
        }

        if (!Source1MaterialReader.CanRead(material))
        {
            return null;
        }

        var reader = new Source1MaterialReader(fileSystem, () => HighDynamicRange);

        return MaterialTranslator.Translate(material, reader.Read(material), MaterialTargetGame, LoadFile, logger);
    }

    /// <summary>Gets the map loaded by <see cref="LoadMap"/>, or null when none is.</summary>
    public Source1ConvertedMap? Map => map;

    /// <summary>
    /// Mounts the pakfile of a map, so its files win over the game's, then converts the map and serves its map, world,
    /// world node, entity lump and brush models by path. Materials load in the dynamic range the map was lit in.
    /// </summary>
    /// <param name="bsp">The map.</param>
    /// <returns>The converted map.</returns>
    /// <param name="progress">Receives the phase the conversion is in, or <see langword="null"/>.</param>
    public Source1ConvertedMap LoadMap(BspFile bsp, IProgress<string>? progress = null)
    {
        progress?.Report("Loading map files…");

#pragma warning disable CA2000 // Disposed with the file system that owns the archive
        if (BspPakFileReader.Open(bsp) is { } pakFile)
#pragma warning restore CA2000
        {
            fileSystem.AddArchive(pakFile, ownsArchive: true);
        }

        // Lump files may come from the pakfile, so they are read after it is mounted
        bsp.ApplyLumpFiles(fileSystem);

        HighDynamicRange = bsp.HasHdrLighting;
        particles.SetMap(bsp.Name);

        var skyName = bsp.FindEntity("worldspawn")?.GetString("skyname") ?? string.Empty;
        var sky = Source1SkyConverter.Create(fileSystem, skyName, bsp.HasHdrLighting, logger);
        var locator = new Source1MapLighting(bsp);

        map = Source1WorldConverter.Convert(bsp, path => LoadModel(path)?.Source, fileSystem.OpenFile, GetMaterialInfo, locator, logger, sky, progress);

        foreach (var file in map.Files.Values)
        {
            if (file.DataBlock is ResourceTypes.EntityLump lump)
            {
                lumpMaps.AddOrUpdate(lump, bsp);
            }
        }

        // Missing archive chunks are only found when the map's files are looked up
        foreach (var diagnostic in fileSystem.Diagnostics)
        {
            logger.LogWarning("{Diagnostic}", diagnostic);
        }

        return map;
    }

    /// <summary>A model of the game and its conversion.</summary>
    /// <param name="Source">The model as the game has it.</param>
    /// <param name="Model">The converted model, which is also served by the path of the model.</param>
    internal sealed record ConvertedModel(Source1Model Source, ResourceTypes.Model Model);

    private readonly Dictionary<string, ConvertedModel?> models = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Converts a model of the game to a Source 2 model once, for everything that draws it.</summary>
    /// <param name="path">The path of the model, such as <c>models/props/crate.mdl</c>. It names the converted model, and so its meshes.</param>
    /// <returns>The converted model, or <see langword="null"/> when the game has none of that name or it does not load.</returns>
    internal ConvertedModel? LoadModel(string path)
    {
        lock (models)
        {
            if (models.TryGetValue(path, out var cached))
            {
                return cached;
            }

            var converted = ConvertModel(path);
            models[path] = converted;

            return converted;
        }
    }

    private ConvertedModel? ConvertModel(string path)
    {
        if (!fileSystem.Exists(path))
        {
            return null;
        }

        try
        {
            var source = Source1Model.Load(path, fileSystem.OpenFile);
            var model = Source1ModelConverter.Convert(source, path, fileSystem, ModelMaterials);

            return new ConvertedModel(source, model);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException or UnexpectedMagicException)
        {
            logger.LogWarning("Failed to load model {Model}: {Message}", path, e.Message);
            return null;
        }
    }

    private readonly Lazy<SceneImage?> sceneImage = new(() => LoadSceneImage(fileSystem, logger));

    /// <summary>Gets the compiled scenes of the game.</summary>
    /// <returns>The scene image, or <see langword="null"/> when the game has none or it does not load.</returns>
    public SceneImage? GetSceneImage() => sceneImage.Value;

    private static SceneImage? LoadSceneImage(Source1FileSystem fileSystem, ILogger logger)
    {
        try
        {
            using var stream = fileSystem.OpenFile(SceneImage.Path);

            if (stream == null)
            {
                return null;
            }

            return new SceneImage(stream);
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            logger.LogWarning("Failed to load scenes: {Message}", e.Message);
            return null;
        }
    }

    /// <summary>Reads what the sprites, ropes and decals drawn with a material need to know about it.</summary>
    /// <param name="name">The name of the material.</param>
    /// <returns>The info, or <see langword="null"/> when the material or its base texture is unknown.</returns>
    internal Source1MaterialInfo? GetMaterialInfo(string name)
    {
        try
        {
            var materialPath = Source1Paths.ToMaterialPath(name);
            var vmt = VmtFile.Load(Source1Paths.ToGameMaterialPath(materialPath), fileSystem.OpenFile);

            if (Source1TextureHeaders.TryReadBaseTexture(fileSystem, vmt) is not { } header)
            {
                return null;
            }

            var size = new Vector2(header.Width, header.Height);
            var origin = vmt.Values.Pair("$spriteorigin", new Vector2(0.5f));

            var decalScale = vmt.Values.Float("$decalscale", 1f);

            var surfaceProperty = vmt.Values.String("$surfaceprop");

            var isBlended = LoadMaterialData(materialPath) is { IntParams: var parameters }
                && parameters.GetValueOrDefault("F_TRANSLUCENT") != 0;

            return new Source1MaterialInfo(size, vmt.Values.String("$spriteorientation"), origin, decalScale, surfaceProperty, isBlended);
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the path a particle system is served by. Source 1 has no <c>.vpcf</c> files, so the path cannot name a file of the game.
    /// The name is escaped, as it may hold characters a path cannot.
    /// </summary>
    /// <param name="name">The name of the system.</param>
    /// <returns>The path, such as <c>particles/teleporter_blue.vpcf</c>.</returns>
    internal static string GetParticlePath(string name)
    {
        return string.Concat(ParticleFolder, Uri.EscapeDataString(name), ParticleExtension);
    }

    private static bool TryGetParticleName(string path, out string name)
    {
        name = string.Empty;

        if (!path.StartsWith(ParticleFolder, StringComparison.Ordinal) || !path.EndsWith(ParticleExtension, StringComparison.Ordinal))
        {
            return false;
        }

        var escaped = path[ParticleFolder.Length..^ParticleExtension.Length];
        name = Uri.UnescapeDataString(escaped);

        return true;
    }

    private enum GameFileKind
    {
        Model,
        Material,
        Texture,
        NavMesh,
    }

    // Maps the path a resource is served by to the file of the game it is made from, which is how every lookup finds it.
    // A variant of a material or texture is made from the file of the material or texture it varies.
    private bool TryGetGameFile(string path, out GameFileKind kind, out string gameFile)
    {
        if (path == Source1Paths.ErrorModelPath)
        {
            kind = GameFileKind.Model;
            gameFile = GameErrorModelPath;
            return true;
        }

        if (path.EndsWith(Source2ModelExtension, StringComparison.Ordinal))
        {
            kind = GameFileKind.Model;
            gameFile = Source1Paths.ToGameModelPath(path);
            return true;
        }

        if (path.EndsWith(ModelExtension, StringComparison.Ordinal))
        {
            kind = GameFileKind.Model;
            gameFile = path;
            return true;
        }

        if (path.EndsWith(NavMeshExtension, StringComparison.Ordinal))
        {
            kind = GameFileKind.NavMesh;
            gameFile = path;
            return true;
        }

        if (path.EndsWith(MaterialExtension, StringComparison.Ordinal))
        {
            kind = GameFileKind.Material;
            gameFile = Source1Paths.ToGameMaterialPath(Source1Paths.RemoveVariant(path, out _));
            return true;
        }

        if (path.EndsWith(Source1Paths.TextureExtension, StringComparison.Ordinal))
        {
            kind = GameFileKind.Texture;
            gameFile = GetGameTextureFile(Source1Paths.RemoveVariant(path, out _));
            return true;
        }

        kind = default;
        gameFile = string.Empty;
        return false;
    }

    // A high dynamic range variant of a texture is optional
    private string GetGameTextureFile(string texturePath)
    {
        var gameFile = Source1Paths.ToGameTexturePath(texturePath);

        if (!texturePath.EndsWith(Source1Paths.HdrTextureExtension, StringComparison.Ordinal) || fileSystem.Exists(gameFile))
        {
            return gameFile;
        }

        return Source1Paths.ToGameTexturePath(Source1Paths.ToPlainTexturePath(texturePath));
    }

    // Textures the provider generates instead of reading from the game
    private static bool IsGeneratedTexture(string path)
        => path == Source1Paths.BlackTexturePath || Source1Paths.TryGetToolLabel(path, out _);

    private bool IsModelMaterial(string path)
    {
        var materialPath = Source1Paths.RemoveVariant(path, out _);

        return ModelMaterials.Contains(materialPath);
    }

    private readonly Lazy<Source1SurfaceProperties?> surfaceProperties = new(() => Source1SurfaceProperties.Load(fileSystem.OpenFile));

    private readonly Lazy<Source1ImpactDecals?> impactDecals = new(() => Source1ImpactDecals.Load(fileSystem));

    private bool IsSurfacePropertiesPath(string path)
        => path is Source1SurfaceProperties.ResourcePath or Source1SurfaceProperties.FootstepsPath && surfaceProperties.Value != null;

    private bool IsImpactDecalsPath(string path)
    {
        if (path == Source1ImpactDecals.EffectsPath)
        {
            return surfaceProperties.Value != null && impactDecals.Value != null;
        }

        return path == Source1ImpactDecals.GroupsPath && impactDecals.Value != null;
    }

    /// <inheritdoc/>
    public Stream? GetFileStream(string file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var path = Source1Paths.Normalize(file);

        if (path.EndsWith(NavMeshExtension, StringComparison.Ordinal))
        {
            using var source = fileSystem.OpenFile(path);
            return source == null ? null : Source1NavMeshConverter.Convert(source);
        }

        if (soundscapes.Exists(path))
        {
            return soundscapes.Open(path);
        }

        if (path == Source1SurfaceProperties.FootstepsPath && surfaceProperties.Value is { } properties)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(properties.CreateFootstepScript()));
        }

        if (path == Source1ImpactDecals.EffectsPath && surfaceProperties.Value is { } surfaces && impactDecals.Value is { } decals)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(decals.CreateEffectsScript(surfaces)));
        }

        return null;
    }

    /// <inheritdoc/>
    public EntityClassTable GetEntityClasses(ILogger logger) => Source1EntityClassCatalog.Get(fileSystem, logger);

    /// <inheritdoc/>
    public List<ResourceTypes.EntityLump.Entity> GetExportEntities(List<ResourceTypes.EntityLump.Entity> entities)
    {
        var targetClasses = EntityClassCatalog.GetTable(MaterialTargetGame);
        var hasCombinedVolume = targetClasses.FindClass("env_combined_light_probe_volume") != null;

        var converted = Source1EntityExport.Convert(entities, MaterialTargetGame);
        BspFile? volumeMap = null;

        if (hasCombinedVolume && entities.Count > 0)
        {
            lumpMaps.TryGetValue(entities[0].ParentLump, out volumeMap);
        }

        var regrouped = Source1MapExport.Regroup(converted, volumeMap);

        return Source1PhysicsPropExport.Apply(regrouped, ModelHasPhysics);
    }

    private bool ModelHasPhysics(string modelPath)
    {
        if (LoadFile(modelPath + GameFileLoader.CompiledFileSuffix)?.DataBlock is not ResourceTypes.Model model)
        {
            return false;
        }

        return model.GetEmbeddedPhys() is { Parts.Length: > 0 } || model.GetReferencedPhysNames().Any();
    }

    /// <inheritdoc/>
    public void AddExportProperties(string className, EditGameClassProps properties)
        => Source1MapExport.AddRequiredProperties(className, properties);

    /// <inheritdoc/>
    public IEnumerable<string> GetExportResources(Resource entityLump) => Source1MapExport.GetResources(entityLump);

    /// <inheritdoc/>
    public bool Exists(string file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var path = Source1Paths.NormalizeCompiled(file);

        if (map != null && map.Files.ContainsKey(path + GameFileLoader.CompiledFileSuffix))
        {
            return true;
        }

        if (IsGeneratedTexture(path))
        {
            return true;
        }

        if (path.EndsWith(MaterialExtension, StringComparison.Ordinal) && IsModelMaterial(path))
        {
            return true;
        }

        if (TryGetGameFile(path, out _, out var gameFile))
        {
            return fileSystem.Exists(gameFile);
        }

        if (Source1Paths.TryGetSoundName(path, out var soundName))
        {
            return fileSystem.Exists(Source1Paths.ToWavePath(soundName)) || fileSystem.Exists(Source1Paths.ToMp3Path(soundName));
        }

        if (TryGetParticleName(path, out var particleName))
        {
            return particles.GetParticleSystem(particleName) != null;
        }

        return IsSurfacePropertiesPath(path) || IsImpactDecalsPath(path) || sounds.Exists(path) || soundscapes.Exists(path);
    }

    /// <inheritdoc/>
    public Resource? LoadFile(string file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var path = Source1Paths.NormalizeCompiled(file);

        if (map != null && map.Files.TryGetValue(path + GameFileLoader.CompiledFileSuffix, out var mapResource))
        {
            return mapResource;
        }

        Resource? resource;

        if (path == Source1Paths.BlackTexturePath)
        {
            resource = CreateBlackTexture();
        }
        else if (Source1Paths.TryGetToolLabel(path, out var toolLabel))
        {
            resource = GoldSrcMapConversion.CreateToolLabelTexture(toolLabel);
        }
        else if (TryGetGameFile(path, out var kind, out var gameFile))
        {
            if (kind == GameFileKind.Model)
            {
                return LoadModel(gameFile)?.Model.Resource;
            }

            resource = kind switch
            {
                GameFileKind.Material => LoadMaterialResource(path),
                GameFileKind.Texture => LoadTextureFrame(path, gameFile),
                _ => null,
            };
        }
        else if (Source1Paths.TryGetSoundName(path, out var soundName))
        {
            resource = LoadSound(soundName);
        }
        else if (TryGetParticleName(path, out var particleName))
        {
            return LoadParticleSystem(particleName);
        }
        else if (path == Source1SurfaceProperties.ResourcePath && surfaceProperties.Value is { } properties)
        {
            resource = properties.CreateResource();
        }
        else if (path == Source1ImpactDecals.GroupsPath && impactDecals.Value is { } decals)
        {
            resource = decals.CreateGroupsResource();
        }
        else
        {
            return sounds.Load(path);
        }

        if (resource != null)
        {
            resource.FileName = file;
        }

        return resource;
    }

    private Resource? LoadParticleSystem(string name)
    {
        if (particles.GetParticleSystem(name) is not { } particleSystem)
        {
            return null;
        }

        var resource = new Resource(ResourceType.Particle)
        {
            FileName = GetParticlePath(name),
        };
        resource.AddBlock(particleSystem);
        Source1ResourceReferences.Update(resource);

        return resource;
    }

    private Resource? LoadSound(string name)
    {
        var wavePath = Source1Paths.ToWavePath(name);
        var mp3Path = Source1Paths.ToMp3Path(name);

        try
        {
            if (ReadFile(wavePath) is { } wave)
            {
                return SoundConverter.ConvertWave(wave);
            }

            if (ReadFile(mp3Path) is { } mp3)
            {
                return SoundConverter.ConvertMp3(mp3);
            }
        }
        catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException)
        {
            logger.LogWarning("Failed to load sound {Sound}: {Message}", name, e.Message);
        }

        return null;
    }

    private byte[]? ReadFile(string path)
    {
        using var stream = fileSystem.OpenFile(path);

        if (stream == null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        return memory.ToArray();
    }

    private Resource? LoadTextureFrame(string path, string gameFile)
    {
        Source1Paths.RemoveVariant(path, out var variant);

        if (variant == TextureSheetVariant)
        {
            return LoadTexture(gameFile, Source1TextureConverter.ConvertSheet);
        }

        if (variant == Bc7TextureVariant)
        {
            return LoadTexture(gameFile, Source1TextureConverter.ConvertToBc7);
        }

        if (variant != null && variant.StartsWith(CookieVariantPrefix, StringComparison.Ordinal))
        {
            var cookieFrame = variant.AsSpan(CookieVariantPrefix.Length);

            if (!int.TryParse(cookieFrame, NumberStyles.None, CultureInfo.InvariantCulture, out var cookie))
            {
                return null;
            }

            return LoadTexture(gameFile, vtf => Source1TextureConverter.Convert(vtf, cookie, decode: true));
        }

        var frame = 0;

        if (variant != null && !int.TryParse(variant, NumberStyles.None, CultureInfo.InvariantCulture, out frame))
        {
            return null;
        }

        return LoadTexture(gameFile, vtf => Source1TextureConverter.Convert(vtf, frame));
    }

    private static Resource CreateBlackTexture()
    {
        ReadOnlyMemory<byte> pixel = new byte[] { 0, 0, 0, 255 };

        return VrfTexture.Create(1, 1, VTexFormat.RGBA8888, 0, Vector4.Zero, [pixel], null);
    }

    private Resource? LoadTexture(string gameFile, Func<VtfFile, Resource?> convert)
    {
        using var stream = fileSystem.OpenFile(gameFile);

        if (stream == null)
        {
            return null;
        }

        try
        {
            var vtf = new VtfFile(stream);

            return convert(vtf);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException or UnexpectedMagicException)
        {
            logger.LogWarning("Failed to load texture {Texture}: {Message}", gameFile, e.Message);
            return null;
        }
    }

    // A variant of a material is drawn by another shader: a Source 1 shader of the variant's name
    private Resource? LoadMaterialResource(string path)
    {
        var materialPath = Source1Paths.RemoveVariant(path, out var variant);

        if (variant == null)
        {
            return LoadModelMaterial(materialPath);
        }

        if (variant == Source1ModelMaterials.SecondTexCoordVariant)
        {
            var secondTexCoordResource = LoadMaterialResource(materialPath);

            if (secondTexCoordResource?.DataBlock is ResourceTypes.Material material)
            {
                material.IntParams[SecondTexCoordFeature] = 1;
                material.BuildData();
            }

            return secondTexCoordResource;
        }

        if (variant != Source1ModelMaterials.AmbientBoostVariant)
        {
            return LoadMaterialVariant(materialPath, variant);
        }

        var resource = LoadMaterialResource(materialPath);

        if (resource?.DataBlock is ResourceTypes.Material boosted)
        {
            boosted.FloatParams[AmbientBoostParameter] = 1f;
            boosted.BuildData();
        }

        return resource;
    }

    // The eyes and the teeth of a model have a material of their own, which copies the material of the game
    private Resource? LoadModelMaterial(string path)
    {
        if (ModelMaterials.TryGetEyeball(path, out var eyeball))
        {
            if (LoadMaterialData(eyeball.Material) is not { } eyeMaterial)
            {
                return null;
            }

            return Source1MaterialConverter.AsEyeball(eyeMaterial, eyeball.Eyeball, eyeball.TargetBone, eyeball.TargetPosition);
        }

        if (ModelMaterials.TryGetMouth(path, out var mouth))
        {
            if (LoadMaterialData(mouth.Material) is not { } teethMaterial)
            {
                return null;
            }

            return Source1MaterialConverter.AsTeeth(teethMaterial, mouth.Bone, mouth.Forward);
        }

        return LoadMaterial(path);
    }

    private ResourceTypes.Material? LoadMaterialData(string path)
        => LoadMaterial(path)?.DataBlock as ResourceTypes.Material;

    private Resource? LoadMaterialVariant(string materialPath, string variant)
    {
        if (variant == ProjectedDecalVariant)
        {
            var gameFile = Source1Paths.ToGameMaterialPath(materialPath);

            return Source1MaterialConverter.ConvertProjectedDecal(gameFile, fileSystem);
        }

        if (variant == IconVariant)
        {
            return LoadToolSprite(materialPath);
        }

        if (LoadMaterialData(materialPath) is not { } material)
        {
            return null;
        }

        if (variant == OverlayVariant)
        {
            return Source1MaterialConverter.AsOverlay(material);
        }

        if (variant == CableVariant)
        {
            return Source1MaterialConverter.AsCable(material);
        }

        if (TryParseSpriteVariant(variant, out var renderMode))
        {
            return Source1MaterialConverter.AsSprite(material, renderMode, fileSystem);
        }

        var withShader = Source1MaterialConverter.WithShader(material, variant);

        if (variant == DetailSpriteVariant && map?.DetailFadeSquared is { } fade && withShader.DataBlock is ResourceTypes.Material detailMaterial)
        {
            detailMaterial.FloatParams["g_flDetailFadeStartSq"] = fade.X;
            detailMaterial.FloatParams["g_flDetailFadeEndSq"] = fade.Y;
            detailMaterial.BuildData();
        }

        return withShader;
    }

    private static bool TryParseSpriteVariant(string variant, out RenderMode renderMode)
    {
        renderMode = RenderMode.Normal;

        if (!variant.StartsWith(SpriteVariantPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var number = variant.AsSpan(SpriteVariantPrefix.Length);

        if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        renderMode = (RenderMode)value;

        return true;
    }

    private Resource? LoadToolSprite(string path)
    {
        var gameFile = Source1Paths.ToGameMaterialPath(path);

        try
        {
            // A tools material is loaded as the material it is, so that its faces are shown with the label of what they compile to
            var vmt = VmtFile.Load(gameFile, fileSystem.OpenFile);

            if (Source1MaterialConverter.IsToolsMaterial(gameFile, vmt))
            {
                return LoadMaterial(path);
            }

            return Source1MaterialConverter.ConvertToolSprite(gameFile, fileSystem);
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            logger.LogWarning("Failed to load icon {Material}: {Message}", path, e.Message);
            return null;
        }
    }

    private Resource? LoadMaterial(string path)
    {
        try
        {
            var gameFile = Source1Paths.ToGameMaterialPath(path);
            var resource = Source1MaterialConverter.Convert(gameFile, fileSystem, HighDynamicRange, logger);

            if (map != null && map.BumpedMaterials.Contains(path) && resource.DataBlock is ResourceTypes.Material material)
            {
                material.IntParams["F_BUMPED_LIGHTMAP"] = 1;
                material.BuildData();
            }

            return resource;
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            logger.LogWarning("Failed to load material {Material}: {Message}", path, e.Message);
            return null;
        }
    }
}
