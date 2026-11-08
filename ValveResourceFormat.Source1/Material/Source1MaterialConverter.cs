using System.IO;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;
using ValveResourceFormat.Source1.Entities;
using ValveResourceFormat.Source1.FileSystem;
using ValveResourceFormat.Source1.Material.Proxies;
using ValveResourceFormat.Source1.Model;
using ValveResourceFormat.Source1.Texture;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Source1.Material;

/// <summary>
/// Converts Source 1 materials to Source 2 style material resources.
/// </summary>
internal static class Source1MaterialConverter
{
    private const float DefaultAlphaTestReference = 0.7f;
    private const string SkyShaderName = "sky.vfx";
    private const string SkyTextureParameter = "g_tSkyTexture";

    private static readonly (string Key, string Parameter)[] TextureMap =
    [
        ("$basetexture", "g_tColor"),
        ("$bumpmap", "g_tNormal"),
        ("$basetexture2", "g_tColor2"),
        ("$bumpmap2", "g_tNormal2"),
        ("$bumpmask", "g_tBumpMask"),
        ("$texture2", "g_tColor2"),
        ("$basetexture3", "g_tColor3"),
        ("$basetexture4", "g_tColor4"),
        ("$envmapmask", "g_tEnvMapMask"),
        ("$selfillummask", "g_tSelfIllumMask"),
        ("$detail", "g_tDetail"),
        ("$lightwarptexture", "g_tLightWarp"),
        ("$phongexponenttexture", "g_tPhongExponent"),
        ("$phongwarptexture", "g_tPhongWarp"),
        ("$blendmodulatetexture", "g_tBlendModulate"),
        ("$iris", "g_tIris"),
        ("$corneatexture", "g_tCornea"),
        ("$ambientoccltexture", "g_tAmbientOccl"),
        ("$gradienttexture", "g_tGradient"),
        ("$burndetailtexture", "g_tBurnDetail"),
    ];

    /// <summary>
    /// Loads a VMT, selecting the high dynamic range or the plain variants of its conditional blocks.
    /// </summary>
    /// <param name="path">The path of the material, such as <c>materials/concrete/floor.vmt</c>.</param>
    /// <param name="fileSystem">The files of the game.</param>
    /// <param name="highDynamicRange">Whether the <c>hdr</c> condition holds.</param>
    public static VmtFile LoadVmt(string path, Source1FileSystem fileSystem, bool highDynamicRange)
        => VmtFile.Load(path, fileSystem.OpenFile, symbol => ResolveHdrCondition(symbol, highDynamicRange));

    /// <summary>
    /// Converts a VMT of the game to a material resource.
    /// </summary>
    /// <param name="path">The path of the material, such as <c>materials/concrete/floor.vmt</c>. It names the material.</param>
    /// <param name="fileSystem">The files of the game, which the textures are read from for the flags that change the material.</param>
    /// <param name="highDynamicRange">Whether the material is loaded in high dynamic range, which selects the <c>hdr</c> variants of its
    /// conditional blocks and the <c>.hdr.vtex</c> variant of its cube map.</param>
    /// <param name="logger">Receives a warning for a material proxy that does not exist or cannot be set up.</param>
    /// <returns>The material resource, whose data block is a <see cref="ResourceTypes.Material"/>.</returns>
    /// <remarks>The proxies of the material are evaluated once, so what they animate keeps its value at time zero.</remarks>
    public static Resource Convert(string path, Source1FileSystem fileSystem, bool highDynamicRange, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var vmt = LoadVmt(path, fileSystem, highDynamicRange);
        var baseTextureHasAlpha = BaseTextureHasAlpha(fileSystem, vmt);
        var materialPath = Source1Paths.ToMaterialPath(path);
        var material = ToVrfMaterial(vmt, materialPath, baseTextureHasAlpha);

        ApplySsbumpDetail(fileSystem, material, vmt.NormalizedShaderName);
        Source1MaterialProxyApplier.Apply(material, vmt, texturePath => GetFrameCount(fileSystem, texturePath), texturePath => GetSheetGrid(fileSystem, texturePath), logger);
        ApplyTextureSampling(fileSystem, material);
        ApplyBlackBaseColor(material);

        if (IsToolsMaterial(path, vmt))
        {
            material.IntAttributes["tools.toolsmaterial"] = 1;

            if (ToolsMaterials.GetToolsLabel(materialPath) is { } label)
            {
                material.TextureParams["g_tColor"] = Source1Paths.GetToolLabelTexturePath(label);
            }
        }

        if (highDynamicRange && material.TextureParams.TryGetValue("g_tEnvMap", out var envMapPath))
        {
            // The provider falls back to the plain cube map when a high dynamic range one does not exist
            material.TextureParams["g_tEnvMap"] = Source1Paths.ToHdrTexturePath(envMapPath);
        }

        return Finish(material, materialPath);
    }

    // Materials that mark their faces as not drawn by the game, which Source 2 tools materials do too
    private static readonly string[] ToolsCompileFlags =
    [
        "%compiletrigger",
        "%compilenodraw",
        "%compileclip",
        "%playerclip",
        "%compileplayercontrolclip",
        "%compilenpcclip",
        "%compileinvisible",
        "%compileladder",
        "%compileskip",
        "%compilehint",
        "%compileorigin",
        "%compilefog",
        "%compilesky",
        "%compile2dsky",
        "%compileblocklos",
    ];

    // A material without a compile flag is still a tools material when Source 2 has one of that name, such as the
    // material of a fog volume, whose entity the game never creates on the client
    internal static bool IsToolsMaterial(string path, VmtFile vmt)
    {
        foreach (var flag in ToolsCompileFlags)
        {
            if (vmt.Values.Boolean(flag))
            {
                return true;
            }
        }

        return ToolsMaterials.GetToolsLabel(Path.ChangeExtension(path, ".vmat")) != null;
    }

    /// <summary>
    /// Copies a material so that another Source 1 shader draws it, with the same parameters and textures.
    /// </summary>
    /// <param name="source">The material to copy.</param>
    /// <param name="shaderName">The lowercase Source 1 shader name, such as <c>detailsprite</c>.</param>
    public static Resource WithShader(ResourceTypes.Material source, string shaderName)
    {
        ArgumentNullException.ThrowIfNull(source);

        var material = Copy(source, ResourceTypes.Material.Source1ShaderPrefix + shaderName);

        return Finish(material, source.Name);
    }

    /// <summary>
    /// Copies a material so that the Source 1 sprite shader draws it with the blending of a render mode of an entity.
    /// </summary>
    /// <param name="source">The material to copy.</param>
    /// <param name="renderMode">The render mode of the sprite.</param>
    /// <param name="fileSystem">The files of the game, which the frames of the texture are read from.</param>
    internal static Resource AsSprite(ResourceTypes.Material source, RenderMode renderMode, Source1FileSystem fileSystem)
    {
        var material = Copy(source, ResourceTypes.Material.Source1ShaderPrefix + "sprite");

        var isAdditive = renderMode is RenderMode.Glow or RenderMode.WorldGlow or RenderMode.TransAdd or RenderMode.TransAddFrameBlend;
        var isBlended = isAdditive || renderMode is RenderMode.TransColor or RenderMode.TransTexture or RenderMode.TransAlpha or RenderMode.TransAlphaAdd;

        material.IntParams["F_TRANSLUCENT"] = isBlended ? 1 : 0;
        material.IntParams["F_ADDITIVE_BLEND"] = isAdditive ? 1 : 0;
        material.IntParams["F_ALPHA_TEST"] = 0;

        // The shader places the quads itself, and checks a glow for being in view instead of depth testing it
        material.IntParams["F_RENDER_BACKFACES"] = 1;
        material.IntParams["F_NO_Z_PREPASS"] = 1;
        material.IntParams["F_DISABLE_Z_BUFFERING"] = renderMode is RenderMode.Glow or RenderMode.WorldGlow ? 1 : 0;

        BindSpriteSheet(material, fileSystem);

        return Finish(material, source.Name);
    }

    // The frames of a sprite texture become one grid texture, which the shader steps through at the rate of each sprite
    private static void BindSpriteSheet(ResourceTypes.Material material, Source1FileSystem fileSystem)
    {
        if (material.IntParams.GetValueOrDefault("F_TEXTURE_ANIMATION") > 0)
        {
            return;
        }

        if (!material.TextureParams.TryGetValue("g_tColor", out var currentPath))
        {
            return;
        }

        var path = Source1Paths.RemoveVariant(currentPath, out _);
        var frameCount = GetFrameCount(fileSystem, path);

        if (GetSheetGrid(fileSystem, path) is not { } grid)
        {
            return;
        }

        material.TextureParams["g_tColor"] = Source1Paths.AddVariant(path, Source1ResourceProvider.TextureSheetVariant);
        material.IntParams["g_nNumAnimationCells"] = frameCount;
        material.VectorParams["g_vAnimationGrid"] = new Vector4(grid.Columns, grid.Rows, 0f, 0f);
    }

    /// <summary>
    /// Copies a material so that the Source 1 cable shader draws it, which turns the ribbons of a rope to face the camera.
    /// </summary>
    /// <param name="source">The material to copy.</param>
    internal static Resource AsCable(ResourceTypes.Material source)
    {
        var material = Copy(source, ResourceTypes.Material.Source1ShaderPrefix + "cable");
        material.IntParams["F_RENDER_BACKFACES"] = 1;
        material.IntParams["F_NO_Z_PREPASS"] = 1;

        return Finish(material, source.Name);
    }

    /// <summary>
    /// Copies a material so that it is drawn as an overlay: on top of the surface under it, without writing depth.
    /// </summary>
    /// <param name="source">The material to copy.</param>
    public static Resource AsOverlay(ResourceTypes.Material source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var material = Copy(source, source.ShaderName);
        material.IntAttributes["overlay"] = 1;

        return Finish(material, source.Name);
    }

    /// <summary>
    /// Copies an eye material so its shader skins the eyeball with the bone the model attaches it to. Other materials are copied as they are.
    /// </summary>
    /// <param name="source">The material to copy.</param>
    /// <param name="eyeball">The eyeball in the space of the converted vertices.</param>
    /// <param name="targetBone">The bone that carries the view target.</param>
    /// <param name="targetPosition">The position of the view target in the space of the converted vertices.</param>
    internal static Resource AsEyeball(ResourceTypes.Material source, Source1Eyeball eyeball, int targetBone, Vector3 targetPosition)
    {
        var material = Copy(source, source.ShaderName);

        if (IsShader(source, "eyes") || IsShader(source, "eyerefract"))
        {
            material.IntParams["g_nEyeBoneIndex"] = eyeball.Bone;
            material.VectorParams["g_vEyeBindOrigin"] = new Vector4(eyeball.Origin, 0f);
            material.VectorParams["g_vEyeBindUp"] = new Vector4(eyeball.Up, 0f);
            material.VectorParams["g_vEyeBindForward"] = new Vector4(eyeball.Forward, 0f);
            material.FloatParams["g_flEyeZOffset"] = eyeball.ZOffset;
            material.FloatParams["g_flEyeIrisScale"] = eyeball.IrisScale;
            material.IntParams["g_nEyeTargetBindIdx"] = targetBone;
            material.VectorParams["g_vEyeTargetBindPos"] = new Vector4(targetPosition, 0f);
        }

        return Finish(material, source.Name);
    }

    /// <summary>
    /// Copies a teeth material so its shader lights the teeth from the mouth of the model. Other materials are copied as they are.
    /// </summary>
    /// <param name="source">The material to copy.</param>
    /// <param name="bone">The bone the mouth is attached to.</param>
    /// <param name="forward">The direction the mouth faces, in the space of the converted vertices.</param>
    internal static Resource AsTeeth(ResourceTypes.Material source, int bone, Vector3 forward)
    {
        var material = Copy(source, source.ShaderName);

        if (IsShader(source, "teeth"))
        {
            material.IntParams["g_nMouthBoneIndex"] = bone;
            material.VectorParams["g_vMouthBindForward"] = new Vector4(forward, 0f);
        }

        return Finish(material, source.Name);
    }

    private static bool IsShader(ResourceTypes.Material material, string shader)
    {
        return material.ShaderName == ResourceTypes.Material.Source1ShaderPrefix + shader;
    }

    private static ResourceTypes.Material Copy(ResourceTypes.Material source, string shaderName)
    {
        var material = new ResourceTypes.Material
        {
            Resource = new Resource(ResourceType.Material),
            Name = source.Name,
            ShaderName = shaderName,
        };

        foreach (var (name, value) in source.IntParams)
        {
            material.IntParams[name] = value;
        }

        foreach (var (name, value) in source.FloatParams)
        {
            material.FloatParams[name] = value;
        }

        foreach (var (name, value) in source.VectorParams)
        {
            material.VectorParams[name] = value;
        }

        foreach (var (name, value) in source.TextureParams)
        {
            material.TextureParams[name] = value;
        }

        foreach (var (name, value) in source.IntAttributes)
        {
            material.IntAttributes[name] = value;
        }

        return material;
    }

    /// <summary>
    /// Creates the material an editor icon is drawn with: the base texture of a material, drawn by the shader of the tools
    /// sprites, whose vertices the Source 1 sprite shaders do not read.
    /// </summary>
    /// <param name="path">The path of the material, such as <c>materials/editor/info_target.vmt</c>.</param>
    /// <param name="fileSystem">The files of the game.</param>
    /// <returns>The material resource, or <see langword="null"/> when the material has no base texture.</returns>
    public static Resource? ConvertToolSprite(string path, Source1FileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var vmt = VmtFile.Load(path, fileSystem.OpenFile);
        var baseTexture = vmt.Values.String("$basetexture");

        if (string.IsNullOrWhiteSpace(baseTexture))
        {
            return null;
        }

        var hasAlpha = BaseTextureHasAlpha(fileSystem, vmt);
        var material = new ResourceTypes.Material
        {
            Resource = new Resource(ResourceType.Material),
            ShaderName = "tools_sprite.vfx",
        };

        // The shader reads the alpha of the color texture only when it is set to cut the sprite out
        material.IntParams["F_ALPHA_TEST"] = hasAlpha ? 1 : 0;
        SetTexture(material, hasAlpha ? "g_tColorB" : "g_tColorA", baseTexture);

        return Finish(material, path);
    }

    /// <summary>
    /// Creates the material a decal that is projected onto the world is drawn with: the base texture of a material, at the
    /// size of the texture scaled by <c>$decalscale</c>. A decal modulate material multiplies what is under it by twice
    /// its color, any other blends by its alpha.
    /// </summary>
    /// <param name="path">The path of the material, such as <c>materials/decals/concrete/shot1.vmt</c>.</param>
    /// <param name="fileSystem">The files of the game.</param>
    /// <returns>The material resource, or <see langword="null"/> when the material has no base texture.</returns>
    public static Resource? ConvertProjectedDecal(string path, Source1FileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var vmt = VmtFile.Load(path, fileSystem.OpenFile);
        var baseTexture = vmt.Values.String("$basetexture");

        if (string.IsNullOrWhiteSpace(baseTexture) || Source1TextureHeaders.TryReadBaseTexture(fileSystem, vmt) is not { } header)
        {
            return null;
        }

        var material = new ResourceTypes.Material
        {
            Resource = new Resource(ResourceType.Material),
            ShaderName = "csgo_projected_decals.vfx",
        };

        var decalScale = vmt.Values.Float("$decalscale", 1f);
        var isModulated = vmt.NormalizedShaderName == "decalmodulate";

        material.IntParams["F_BLEND_MODE"] = isModulated ? 1 : 0;
        material.FloatAttributes["DecalWorldWidth"] = header.Width * decalScale;
        material.FloatAttributes["DecalWorldHeight"] = header.Height * decalScale;

        var texturePath = VmtFile.TryNormalizePath(baseTexture, Source1Paths.TextureExtension);

        if (texturePath != null)
        {
            material.TextureParams["g_tColor"] = Source1Paths.AddVariant(texturePath, Source1ResourceProvider.Bc7TextureVariant);
        }

        return Finish(material, path);
    }

    /// <summary>
    /// Creates a sky material that draws a cube texture around the camera.
    /// </summary>
    /// <param name="path">The path of the material.</param>
    /// <param name="cubeTexturePath">The path of the cube texture.</param>
    public static Resource CreateSkyMaterial(string path, string cubeTexturePath)
    {
        var material = new ResourceTypes.Material
        {
            Resource = new Resource(ResourceType.Material),
            Name = path,
            ShaderName = SkyShaderName,
        };

        material.TextureParams[SkyTextureParameter] = cubeTexturePath;

        return Finish(material, path);
    }

    /// <summary>Gets whether the material is a sky made by <see cref="CreateSkyMaterial"/>.</summary>
    public static bool IsSkyMaterial(ResourceTypes.Material material)
    {
        ArgumentNullException.ThrowIfNull(material);

        return material.ShaderName == SkyShaderName && material.TextureParams.ContainsKey(SkyTextureParameter);
    }

    /// <summary>Describes a sky made by <see cref="CreateSkyMaterial"/>.</summary>
    public static MaterialDescription DescribeSkyMaterial(ResourceTypes.Material material)
    {
        ArgumentNullException.ThrowIfNull(material);

        var description = new MaterialDescription { Role = MaterialRole.Sky };
        description.Layers[0].Color = MaterialInput.FromTexture(material.TextureParams[SkyTextureParameter], ChannelMapping.RGB);

        return description;
    }

    // A texture path that climbs out of its mount is never found by the game, so the material draws without it
    private static void SetTexture(ResourceTypes.Material material, string parameter, string path)
    {
        if (Source1Paths.IsRenderTarget(path))
        {
            return;
        }

        var texturePath = VmtFile.TryNormalizePath(path, Source1Paths.TextureExtension);

        if (texturePath != null)
        {
            material.TextureParams[parameter] = texturePath;
        }
    }

    private static Resource Finish(ResourceTypes.Material material, string path)
    {
        var materialPath = Source1Paths.ToMaterialPath(path);
        var resource = material.Resource;
        material.Name = materialPath;
        resource.FileName = materialPath;
        material.BuildData();
        resource.AddBlock(material);
        Source1ResourceReferences.Update(resource);

        return resource;
    }

    private static int GetFrameCount(Source1FileSystem fileSystem, string texturePath)
    {
        if (Source1TextureHeaders.TryRead(fileSystem, texturePath) is not { } header)
        {
            return 1;
        }

        return Math.Max(1, (int)header.Frames);
    }

    private static (int Columns, int Rows)? GetSheetGrid(Source1FileSystem fileSystem, string texturePath)
    {
        if (Source1TextureHeaders.TryRead(fileSystem, texturePath) is not { } header)
        {
            return null;
        }

        return Source1TextureConverter.GetSheetGrid(header);
    }

    // The sampler state of a texture is a property of the material. The base texture decides it for every texture of the
    // material that reads the shared coordinates.
    private static void ApplyTextureSampling(Source1FileSystem fileSystem, ResourceTypes.Material material)
    {
        if (!material.TextureParams.TryGetValue("g_tColor", out var path) && !material.TextureParams.TryGetValue("g_tNormal", out path))
        {
            return;
        }

        var texturePath = Source1Paths.RemoveVariant(path, out _);

        if (Source1TextureHeaders.TryRead(fileSystem, texturePath) is not { } header)
        {
            return;
        }

        var flags = header.Flags;

        if ((flags & (VtfFlags.CLAMPS | VtfFlags.BORDER)) != 0)
        {
            material.IntParams["g_nTextureAddressModeU"] = (long)RsTextureAddressMode.Clamp;
        }

        if ((flags & (VtfFlags.CLAMPT | VtfFlags.BORDER)) != 0)
        {
            material.IntParams["g_nTextureAddressModeV"] = (long)RsTextureAddressMode.Clamp;
        }

        if ((flags & VtfFlags.POINTSAMPLE) != 0)
        {
            material.IntParams["g_nTextureFilter"] = 1;
        }
    }

    // A surface that only reflects has no color of its own
    private static void ApplyBlackBaseColor(ResourceTypes.Material material)
    {
        var reflectsOnly = material.IntParams.GetValueOrDefault("F_ENVMAP") != 0 && material.IntParams.GetValueOrDefault("F_PHONG") == 0;

        if (reflectsOnly && !material.TextureParams.ContainsKey("g_tColor"))
        {
            material.TextureParams["g_tColor"] = Source1Paths.BlackTexturePath;
        }
    }

    /// <summary>
    /// Creates a material resource from a parsed VMT. The Source 1 shader name is kept, lowercase, with the <see cref="ResourceTypes.Material.Source1ShaderPrefix"/>.
    /// </summary>
    /// <param name="vmt">The parsed material.</param>
    /// <param name="name">The name of the material resource.</param>
    /// <param name="baseTextureHasAlpha">Whether the base texture has an alpha channel. Without one, its alpha cannot drive self illumination or the environment map mask.</param>
    public static ResourceTypes.Material ToVrfMaterial(VmtFile vmt, string? name = null, bool baseTextureHasAlpha = true)
    {
        ArgumentNullException.ThrowIfNull(vmt);

        var values = vmt.Values;
        var shader = vmt.NormalizedShaderName;
        var material = new ResourceTypes.Material
        {
            Resource = new Resource(ResourceType.Material),
            Name = name ?? string.Empty,
            ShaderName = ResourceTypes.Material.Source1ShaderPrefix + shader,
        };

        SetTextures(material, values, shader);
        SetWrinkleTextures(material, values, shader);

        var features = ResolveFeatures(material, values, shader, baseTextureHasAlpha);

        if (!features.HasEnvmapMask)
        {
            material.TextureParams.Remove("g_tEnvMapMask");
        }

        if (!features.HasBump)
        {
            material.TextureParams.Remove("g_tNormal2");
        }

        if (features.Envmap is { } envmap && !envmap.Equals("env_cubemap", StringComparison.OrdinalIgnoreCase))
        {
            SetTexture(material, "g_tEnvMap", envmap);
        }

        SetSurfaceFlags(material, values, shader, features);
        SetEnvmap(material, values, shader, features);
        SetSelfIllum(material, values, features);
        SetPhong(material, values, features);
        SetInfected(material, values, shader);
        SetEyeRefract(material, values, shader);
        SetWater(material, values, shader);
        SetModulate(material, values, shader);
        SetMonitorScreen(material, values, shader);
        SetSolidEnergy(material, values, shader);
        SetAlpha(material, values, shader);
        SetTextureFlags(material, features);
        SetDetail(material, values, shader, features);
        SetSecondDetail(material, values, shader, features);
        SetFourWayBlend(material, values, shader);
        SetColorTint(material, values, shader);
        SetTransforms(material, values, shader, features);

        return material;
    }

    private static void SetTextures(ResourceTypes.Material material, VmtParameters values, string shader)
    {
        var isWater = IsWater(shader);

        // The ripples of water come from the normal map, a $bumpmap of water is the du/dv map of older hardware
        if (isWater && values.String("$normalmap") is { } normalMap && !string.IsNullOrWhiteSpace(normalMap))
        {
            SetTexture(material, "g_tNormal", normalMap);
        }

        if (isWater)
        {
            SetOptionalTexture(material, values, "$flowmap", "g_tFlowmap");
            SetOptionalTexture(material, values, "$flow_noise_texture", "g_tFlowNoise");
        }

        // A bump mapped surface lit by its bumped lightmaps takes the albedo texture, which has no baked light, as its base texture
        if (shader == "worldtwotextureblend"
            && values.Contains("$bumpmap")
            && !values.Boolean("$nodiffusebumplighting")
            && values.String("$albedo") is { } albedo
            && !string.IsNullOrWhiteSpace(albedo))
        {
            SetTexture(material, "g_tColor", albedo);
        }

        foreach (var (key, parameter) in TextureMap)
        {
            var path = values.String(key);

            if (string.IsNullOrWhiteSpace(path) || material.TextureParams.ContainsKey(parameter))
            {
                continue;
            }

            if (isWater && key == "$bumpmap")
            {
                continue;
            }

            SetTexture(material, parameter, path);
        }

        if (shader == "character")
        {
            SetOptionalTexture(material, values, "$masks1", "g_tMasks1");
        }
    }

    // The wrinkle textures of a model replace the base and bump textures where a flex compresses or stretches the surface.
    // A pair applies only with its base texture. When one pair is missing, the other still blends and the missing pair
    // is the base texture itself.
    private static void SetWrinkleTextures(ResourceTypes.Material material, VmtParameters values, string shader)
    {
        if (shader is not ("vertexlitgeneric" or "skin"))
        {
            return;
        }

        var colorCompress = values.String("$compress");
        var colorStretch = values.String("$stretch");
        var normalCompress = values.String("$bumpcompress");
        var normalStretch = values.String("$bumpstretch");

        var hasColorWrinkle = material.TextureParams.ContainsKey("g_tColor")
            && !string.IsNullOrWhiteSpace(colorCompress)
            && !string.IsNullOrWhiteSpace(colorStretch);
        var hasNormalWrinkle = material.TextureParams.ContainsKey("g_tNormal")
            && !string.IsNullOrWhiteSpace(normalCompress)
            && !string.IsNullOrWhiteSpace(normalStretch);

        if (!hasColorWrinkle && !hasNormalWrinkle)
        {
            return;
        }

        if (hasColorWrinkle)
        {
            SetTexture(material, "g_tColorCompress", colorCompress!);
            SetTexture(material, "g_tColorStretch", colorStretch!);
        }
        else
        {
            material.TextureParams["g_tColorCompress"] = material.TextureParams["g_tColor"];
            material.TextureParams["g_tColorStretch"] = material.TextureParams["g_tColor"];
        }

        if (hasNormalWrinkle)
        {
            SetTexture(material, "g_tNormalCompress", normalCompress!);
            SetTexture(material, "g_tNormalStretch", normalStretch!);
        }
        else if (material.TextureParams.TryGetValue("g_tNormal", out var normal))
        {
            material.TextureParams["g_tNormalCompress"] = normal;
            material.TextureParams["g_tNormalStretch"] = normal;
        }

        SetFlag(material, "F_WRINKLE", true);
    }

    // Decides which of the optional features of the shader the textures and parameters of the material turn on, as the
    // shaders combine them
    private static SurfaceFeatures ResolveFeatures(ResourceTypes.Material material, VmtParameters values, string shader, bool baseTextureHasAlpha)
    {
        var isLightmapped = IsLightmapped(shader);
        var isVertexLit = IsVertexLit(shader);
        var isUnlit = shader is "unlitgeneric";

        var hasBaseTexture = material.TextureParams.ContainsKey("g_tColor");
        var hasBump = material.TextureParams.ContainsKey("g_tNormal");
        var hasLightWarp = material.TextureParams.ContainsKey("g_tLightWarp");
        var hasDetail = material.TextureParams.ContainsKey("g_tDetail");
        var hasEnvmapMask = material.TextureParams.ContainsKey("g_tEnvMapMask");
        // The lightmapped shaders have no sampler for a self illumination mask
        var hasSelfIllumMask = material.TextureParams.ContainsKey("g_tSelfIllumMask") && !isLightmapped;

        // Phong needs a bump map, the base alpha as its mask or a light warp texture. The infected shader takes its mask
        // from the variation texture.
        var basemapAlphaPhongMask = values.Boolean("$basemapalphaphongmask");
        var phong = values.Boolean("$phong") && (hasBump || basemapAlphaPhongMask || hasLightWarp || shader == "infected");

        var envmap = values.String("$envmap");
        var hasEnvmap = !string.IsNullOrWhiteSpace(envmap);

        var selfIllum = values.Boolean("$selfillum");
        var wantsSelfIllumFresnel = values.Boolean("$selfillumfresnel");
        var baseAlphaEnvmapMask = values.Boolean("$basealphaenvmapmask");
        var normalMapAlphaEnvmapMask = values.Boolean("$normalmapalphaenvmapmask");

        if (!hasBaseTexture)
        {
            selfIllum = false;
            baseAlphaEnvmapMask = false;
        }
        else if (!baseTextureHasAlpha)
        {
            // A vertex lit model can still glow from its own mask texture or a fresnel term
            selfIllum = selfIllum && isVertexLit && (hasSelfIllumMask || wantsSelfIllumFresnel);
            baseAlphaEnvmapMask = false;
        }

        if (!isVertexLit && !isLightmapped)
        {
            selfIllum = false;
        }

        if (isVertexLit && !phong)
        {
            if (!hasBump && !hasLightWarp)
            {
                normalMapAlphaEnvmapMask = false;
            }

            if (normalMapAlphaEnvmapMask)
            {
                hasEnvmapMask = false;
                baseAlphaEnvmapMask = false;
            }

            // A bump mapped model can only mask the environment map with the alpha of its normal map
            if (hasBump && baseAlphaEnvmapMask && !normalMapAlphaEnvmapMask)
            {
                hasEnvmap = false;
            }

            if (hasBump && hasEnvmapMask)
            {
                hasEnvmapMask = false;

                if (!normalMapAlphaEnvmapMask)
                {
                    hasEnvmap = false;
                }
            }
        }

        if (isLightmapped && hasBump)
        {
            hasEnvmapMask = false;
        }

        // The shader has no environment map and no fresnel term
        if (shader == "worldtwotextureblend")
        {
            hasEnvmap = false;
            hasEnvmapMask = false;
            baseAlphaEnvmapMask = false;
            wantsSelfIllumFresnel = false;
        }

        // An unlit surface has no normal map to take a mask from
        if (isUnlit)
        {
            normalMapAlphaEnvmapMask = false;
        }

        // Only the shader without a normal map or light warp can take the self illumination from the mask alpha, and it
        // cannot also use the base alpha
        var selfIllumFromMask = hasEnvmapMask
            && values.Float("$selfillum_envmapmask_alpha", 0f) != 0f
            && (isUnlit || (isVertexLit && !hasBump && !hasLightWarp && !phong));

        if (selfIllumFromMask)
        {
            selfIllum = false;
        }

        // The fresnel term is in the shaders with a normal map or light warp, which only have it without a detail texture,
        // and in the phong shader
        var selfIllumFresnel = selfIllum && wantsSelfIllumFresnel && (phong || ((hasBump || hasLightWarp) && !hasDetail));
        var useLightWarp = hasLightWarp && !(selfIllumFresnel && !phong);

        if (selfIllumFresnel && !phong)
        {
            normalMapAlphaEnvmapMask = false;
        }

        return new SurfaceFeatures(
            hasEnvmap ? envmap : null,
            phong,
            basemapAlphaPhongMask,
            hasBump,
            hasEnvmapMask,
            hasSelfIllumMask,
            hasDetail,
            selfIllum,
            selfIllumFromMask,
            selfIllumFresnel,
            baseAlphaEnvmapMask,
            normalMapAlphaEnvmapMask,
            useLightWarp);
    }

    private static void SetSurfaceFlags(ResourceTypes.Material material, VmtParameters values, string shader, SurfaceFeatures features)
    {
        var isVertexLit = IsVertexLit(shader);
        var hasBump = features.HasBump;

        // A surface that uses the alpha of its base texture for something else is not alpha tested
        var alphaTest = values.Boolean("$alphatest") && (features.Phong || !(features.SelfIllum || features.BaseAlphaEnvmapMask));

        SetFlag(material, "F_ALPHA_TEST", alphaTest);
        bool translucent;

        if (shader == "refract")
        {
            // The alpha of the normal map is the translucency, unless an environment map takes it as the reflection amount
            translucent = hasBump && !features.HasEnvmap;
        }
        else if (IsWater(shader))
        {
            // Cheap water that is not forced draws its reflection over the scene
            translucent = !values.Boolean("$forcecheap");
        }
        else
        {
            translucent = values.Boolean("$translucent");
        }

        SetFlag(material, "F_TRANSLUCENT", translucent);
        SetFlag(material, "F_ADDITIVE_BLEND", values.Boolean("$additive"));
        SetFlag(material, "F_RENDER_BACKFACES", values.Boolean("$nocull"));

        // Models are lit by their vertices and ignore the vertex colors of the mesh
        var vertexColor = values.Boolean("$vertexcolor");
        var vertexAlpha = values.Boolean("$vertexalpha");

        // The modulate shader takes the vertex color and alpha together
        if (shader == "modulate")
        {
            vertexColor |= vertexAlpha;
            vertexAlpha = vertexColor;
        }

        SetFlag(material, "F_VERTEX_COLOR", !isVertexLit && vertexColor);
        SetFlag(material, "F_VERTEX_ALPHA", !isVertexLit && vertexAlpha);
        SetFlag(material, "F_SELF_ILLUM", features.SelfIllum);

        // Meshes of models can have flexes, which reach the shader as morphs. The eye shaders do not read them.
        SetFlag(material, "F_MORPH_SUPPORTED", isVertexLit && shader is not ("eyes" or "eyerefract"));

        SetFlag(material, "F_BUMPMAP", hasBump);
        var hasBump2 = hasBump && material.TextureParams.ContainsKey("g_tNormal2");
        SetFlag(material, "F_BUMPMAP2", hasBump2);

        // The bump mask blends the two normal maps in place of the vertex alpha, and only without the features that
        // use the vertex alpha or the shader inputs it shares
        var hasBumpMask = shader == "lightmappedgeneric"
            && hasBump2
            && material.TextureParams.ContainsKey("g_tBumpMask")
            && !material.TextureParams.ContainsKey("g_tColor2")
            && !features.HasDetail
            && !values.Boolean("$selfillum")
            && !values.Boolean("$basetexturenoenvmap");
        SetFlag(material, "F_BUMPMASK", hasBumpMask);
        SetFlag(material, "F_SSBUMP", hasBump && values.Boolean("$ssbump"));

        var isLightmapped = IsLightmapped(shader);
        SetFlag(material, "F_MASKED_BLENDING", isLightmapped && values.Boolean("$maskedblending"));
        SetFlag(material, "F_BASE_TEXTURE_NO_ENVMAP", isLightmapped && values.Boolean("$basetexturenoenvmap"));
        SetFlag(material, "F_BASE_TEXTURE2_NO_ENVMAP", isLightmapped && values.Boolean("$basetexture2noenvmap"));

        SetFlag(material, "F_ENVMAP", features.HasEnvmap);
        SetFlag(material, "F_ENVMAP_SCENE", features.HasEnvmap && !material.TextureParams.ContainsKey("g_tEnvMap"));
        SetFlag(material, "F_ENVMAP_MASK", features.HasEnvmapMask);
        SetFlag(material, "F_BASE_ALPHA_ENVMAP_MASK", features.BaseAlphaEnvmapMask);
        SetFlag(material, "F_NORMALMAP_ALPHA_ENVMAP_MASK", features.NormalMapAlphaEnvmapMask);
    }

    private static void SetEnvmap(ResourceTypes.Material material, VmtParameters values, string shader, SurfaceFeatures features)
    {
        if (!features.HasEnvmap)
        {
            return;
        }

        var contrast = values.Float("$envmapcontrast", 0f);
        var saturation = values.Float("$envmapsaturation", 1f);
        var fresnelReflection = values.Float("$fresnelreflection", 1f);

        // The lightmapped shader only reads the contrast and the saturation when both change the reflection or when
        // the fresnel or a self illumination tint needs its slower variant. Otherwise it uses its fixed ones.
        if (IsLightmapped(shader))
        {
            var changesContrast = contrast != 0f && contrast != 1f && saturation != 1f;
            var hasSelfIllumTint = features.SelfIllum && values.Vector("$selfillumtint", Vector3.One) != Vector3.One;

            if (!changesContrast && fresnelReflection == 1f && !hasSelfIllumTint)
            {
                contrast = contrast == 1f ? 1f : 0f;
                saturation = 1f;
            }

            // The variant with a blend modulate texture has no contrast and saturation
            if (material.TextureParams.ContainsKey("g_tBlendModulate"))
            {
                contrast = 0f;
                saturation = 1f;
            }
        }

        var envMapTint = values.Vector("$envmaptint", Vector3.One);
        material.VectorParams["g_vEnvMapTint"] = new Vector4(ConvertTint("$envmaptint", envMapTint, shader), 1f);
        material.FloatParams["g_flEnvMapContrast"] = contrast;
        material.FloatParams["g_flEnvMapSaturation"] = saturation;
        material.FloatParams["g_flEnvMapFresnel"] = values.Float("$envmapfresnel", 0f);
        material.FloatParams["g_flFresnelReflection"] = fresnelReflection;

        if (IsLightmapped(shader))
        {
            material.FloatParams["g_flEnvMapLightScale"] = MathF.Max(values.Float("$envmaplightscale", 0f), 0f);

            // The multiplier is the sum of both values
            var lightScaleMinMax = values.Pair("$envmaplightscaleminmax", new Vector2(0f, 1f));
            var lightScaleMultiplier = lightScaleMinMax.X + lightScaleMinMax.Y;
            material.VectorParams["g_vEnvMapLightScaleRange"] = new Vector4(lightScaleMinMax.X, lightScaleMultiplier, 0f, 0f);
        }
    }

    private static void SetSelfIllum(ResourceTypes.Material material, VmtParameters values, SurfaceFeatures features)
    {
        SetFlag(material, "F_SELF_ILLUM_ENVMAPMASK_ALPHA", features.SelfIllumFromMask);
        SetFlag(material, "F_SELF_ILLUM_FRESNEL", features.SelfIllumFresnel);

        if (features.SelfIllum || features.SelfIllumFromMask)
        {
            material.VectorParams["g_vSelfIllumTint"] = new Vector4(values.Vector("$selfillumtint", Vector3.One), 1f);
        }

        if (features.SelfIllumFresnel)
        {
            var minMaxExp = values.Vector("$selfillumfresnelminmaxexp", new Vector3(0f, 1f, 1f));
            var bias = minMaxExp.Y != 0f ? minMaxExp.X / minMaxExp.Y : 0f;

            // Scale, bias, exponent and brightness
            material.VectorParams["g_vSelfIllumFresnel"] = new Vector4(1f - bias, bias, minMaxExp.Z, minMaxExp.Y);
        }
    }

    private static void SetPhong(ResourceTypes.Material material, VmtParameters values, SurfaceFeatures features)
    {
        var phong = features.Phong;

        SetFlag(material, "F_LIGHTWARP", features.UseLightWarp);
        SetFlag(material, "F_HALF_LAMBERT", phong || values.Boolean("$halflambert"));
        SetFlag(material, "F_PHONG", phong);

        if (phong)
        {
            var hasExponentTexture = material.TextureParams.ContainsKey("g_tPhongExponent");

            SetFlag(material, "F_PHONG_EXPONENT_TEXTURE", hasExponentTexture);
            SetFlag(material, "F_PHONG_WARP", material.TextureParams.ContainsKey("g_tPhongWarp"));
            SetFlag(material, "F_BASEMAP_ALPHA_PHONG_MASK", features.BasemapAlphaPhongMask);
            SetFlag(material, "F_INVERT_PHONG_MASK", values.Boolean("$invertphongmask"));

            material.FloatParams["g_flPhongExponent"] = values.Float("$phongexponent", 0f);
            material.FloatParams["g_flPhongExponentFactor"] = values.Float("$phongexponentfactor", 0f);
            material.FloatParams["g_flPhongBoost"] = values.Float("$phongboost", 1f);

            var ranges = values.Vector("$phongfresnelranges", new Vector3(0f, 0.5f, 1f));
            material.VectorParams["g_vPhongFresnelRanges"] = new Vector4(ranges, 0f);

            // A zero tint tints by the albedo when the exponent texture has a channel for it, flagged with a negative red
            // channel, and by white otherwise
            var tint = values.Vector("$phongtint", Vector3.Zero);

            if (tint == Vector3.Zero)
            {
                tint = hasExponentTexture && values.Boolean("$phongalbedotint") ? new Vector3(-1f, 0f, 0f) : Vector3.One;
            }

            material.VectorParams["g_vPhongTint"] = new Vector4(tint, 0f);
        }

        var rimLight = phong && values.Boolean("$rimlight");
        SetFlag(material, "F_RIM_LIGHT", rimLight);

        if (rimLight)
        {
            SetFlag(material, "F_RIM_MASK", values.Boolean("$rimmask") && material.TextureParams.ContainsKey("g_tPhongExponent"));
            material.FloatParams["g_flRimLightExponent"] = MathF.Max(values.Float("$rimlightexponent", 4f), 1f);
            material.FloatParams["g_flRimLightBoost"] = values.Float("$rimlightboost", 1f);
        }
    }

    private static void SetInfected(ResourceTypes.Material material, VmtParameters values, string shader)
    {
        if (shader != "infected")
        {
            return;
        }

        var disableVariation = values.Boolean("$disablevariation") || !material.TextureParams.ContainsKey("g_tGradient");

        SetFlag(material, "F_DISABLE_VARIATION", disableVariation);
        SetFlag(material, "F_BURNING", !disableVariation && values.Boolean("$burning") && material.TextureParams.ContainsKey("g_tBurnDetail"));

        material.IntParams["g_nSheetIndex"] = values.Integer("$sheetindex", 0);
        material.FloatParams["g_flSkinTintGradient"] = values.Float("$skintintgradient", 0f);
        material.FloatParams["g_flColorTintGradient"] = values.Float("$colortintgradient", 0f);
        material.FloatParams["g_flDefaultPhongExponent"] = values.Float("$defaultphongexponent", 5f);
        material.FloatParams["g_flSkinPhongExponent"] = values.Float("$skinphongexponent", 5f);
        material.FloatParams["g_flBloodPhongExponent"] = values.Float("$bloodphongexponent", 5f);
        material.FloatParams["g_flBurnStrength"] = values.Float("$burnstrength", 0f);
        material.VectorParams["g_vBloodColor"] = new Vector4(values.Vector("$bloodcolor", new Vector3(1f, 0f, 0f)), 0f);

        var bloodMaskRange = values.Vector("$bloodmaskrange", new Vector3(0f, 1f, 0f));
        material.VectorParams["g_vBloodMaskRange"] = new Vector4(bloodMaskRange.X, bloodMaskRange.Y, 0f, 0f);
    }

    private static void SetEyeRefract(ResourceTypes.Material material, VmtParameters values, string shader)
    {
        if (shader != "eyerefract")
        {
            return;
        }

        SetFlag(material, "F_RAYTRACE_SPHERE", values.Boolean("$raytracesphere", true));
        SetFlag(material, "F_SPHERE_TEXKILL", values.Boolean("$spheretexkillcombo", true));

        material.FloatParams["g_flDilation"] = values.Float("$dilation", 0.5f);
        material.FloatParams["g_flGlossiness"] = values.Float("$glossiness", 1f);
        material.FloatParams["g_flCorneaBumpStrength"] = values.Float("$corneabumpstrength", 1f);
        material.FloatParams["g_flParallaxStrength"] = values.Float("$parallaxstrength", 0.25f);
        material.FloatParams["g_flEyeballRadius"] = values.Float("$eyeballradius", 0.5f);
        material.VectorParams["g_vAmbientOcclColor"] = new Vector4(values.Vector("$ambientocclcolor", new Vector3(0.33f)), 0f);
    }

    private static void SetModulate(ResourceTypes.Material material, VmtParameters values, string shader)
    {
        if (shader != "modulate")
        {
            return;
        }

        const int BlendModeMod2x = 3;
        const int BlendModeMultiply = 5;

        material.IntParams["F_BLEND_MODE"] = values.Boolean("$mod2x") ? BlendModeMod2x : BlendModeMultiply;
        SetFlag(material, "F_DISABLE_Z_WRITE", !values.Boolean("$writez"));
    }

    private static void SetMonitorScreen(ResourceTypes.Material material, VmtParameters values, string shader)
    {
        if (shader != "monitorscreen")
        {
            return;
        }

        material.FloatParams["g_flMonitorContrast"] = values.Float("$contrast", 0f);
        material.FloatParams["g_flMonitorSaturation"] = values.Float("$saturation", 1f);
        material.VectorParams["g_vMonitorTint"] = new Vector4(values.Vector("$tint", Vector3.One), 1f);
    }

    // Energy of fizzlers and force fields: the base texture with up to two detail layers, or ripples that follow a flow map
    private static void SetSolidEnergy(ResourceTypes.Material material, VmtParameters values, string shader)
    {
        if (shader != "solidenergy")
        {
            return;
        }

        material.FloatParams["g_flOutputIntensity"] = values.Float("$outputintensity", 1f);

        // The vertex color and the vertex alpha are one input to this shader
        SetFlag(material, "F_VERTEX_COLOR", values.Boolean("$vertexcolor") || values.Boolean("$vertexalpha"));

        var hasDetail1 = SetOptionalTexture(material, values, "$detail1", "g_tDetail1");
        var hasDetail2 = hasDetail1 && SetOptionalTexture(material, values, "$detail2", "g_tDetail2");
        SetFlag(material, "F_DETAIL1", hasDetail1);
        SetFlag(material, "F_DETAIL2", hasDetail2);

        if (hasDetail1)
        {
            material.IntParams["g_nDetail1BlendMode"] = Math.Clamp(values.Integer("$detail1blendmode", 0), 0, 1);
            SetDetailTransform(material, values, "$detail1texturetransform", "$detail1scale", "g_vDetail1TransformRow0", "g_vDetail1TransformRow1");
        }

        if (hasDetail2)
        {
            material.IntParams["g_nDetail2BlendMode"] = Math.Clamp(values.Integer("$detail2blendmode", 0), 0, 1);
            SetDetailTransform(material, values, "$detail2texturetransform", "$detail2scale", "g_vDetail2TransformRow0", "g_vDetail2TransformRow1");
        }

        // The flow replaces the detail layers
        var hasFlow = !hasDetail1 && SetOptionalTexture(material, values, "$flowmap", "g_tFlowmap");
        SetFlag(material, "F_FLOWMAP", hasFlow);

        if (!hasFlow)
        {
            return;
        }

        SetOptionalTexture(material, values, "$flow_noise_texture", "g_tFlowNoise");
        SetOptionalTexture(material, values, "$flowbounds", "g_tFlowBounds");

        SetFlag(material, "F_FLOW_CHEAP", values.Boolean("$flow_cheap"));
        material.VectorParams["g_vFlowColor"] = new Vector4(values.Vector("$flow_color", new Vector3(0.1f, 0.2f, 0.4f)), 0f);
        material.FloatParams["g_flFlowColorIntensity"] = values.Float("$flow_color_intensity", 1f);
        SetFlowParameters(material, values);
        material.FloatParams["g_flFlowLerpExponent"] = values.Float("$flow_lerpexp", 0f);
    }

    private static void SetFlowParameters(ResourceTypes.Material material, VmtParameters values)
    {
        material.FloatParams["g_flFlowWorldUvScale"] = values.Float("$flow_worlduvscale", 1f);
        material.FloatParams["g_flFlowNormalUvScale"] = values.Float("$flow_normaluvscale", 1f);
        material.FloatParams["g_flFlowTimeInterval"] = values.Float("$flow_timeintervalinseconds", 0.4f);
        material.FloatParams["g_flFlowUvScrollDistance"] = values.Float("$flow_uvscrolldistance", 0.2f);
        material.FloatParams["g_flFlowNoiseScale"] = values.Float("$flow_noise_scale", 0.0002f);
    }

    private static bool SetOptionalTexture(ResourceTypes.Material material, VmtParameters values, string key, string parameter)
    {
        var path = values.String(key);

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        SetTexture(material, parameter, path);

        return material.TextureParams.ContainsKey(parameter);
    }

    // A detail takes the transform of its own, or else the one of the base texture, and the scale of the detail applies to
    // the coordinates it produces
    private static void SetDetailTransform(ResourceTypes.Material material, VmtParameters values, string transformKey, string scaleKey, string row0Name, string row1Name)
    {
        var key = values.Contains(transformKey) ? transformKey : "$basetexturetransform";
        var row0 = new Vector4(1f, 0f, 0f, 0f);
        var row1 = new Vector4(0f, 1f, 0f, 0f);

        if (TryReadTransform(values, key, out var transform))
        {
            row0 = new Vector4(transform.M11, transform.M21, 0f, transform.M41);
            row1 = new Vector4(transform.M12, transform.M22, 0f, transform.M42);
        }

        var scale = values.Pair(scaleKey, new Vector2(values.Float(scaleKey, 1f)));

        material.VectorParams[row0Name] = new Vector4(row0.X * scale.X, row0.Y * scale.Y, 0f, row0.W * scale.X);
        material.VectorParams[row1Name] = new Vector4(row1.X * scale.X, row1.Y * scale.Y, 0f, row1.W * scale.Y);
    }

    private static void SetWater(ResourceTypes.Material material, VmtParameters values, string shader)
    {
        if (!IsWater(shader))
        {
            return;
        }

        var fogColor = values.Vector("$fogcolor", new Vector3(0.1f, 0.2f, 0.25f));
        material.VectorParams["g_vWaterFogColor"] = new Vector4(ConvertTint("$fogcolor", fogColor, shader), 1f);
        material.VectorParams["g_vRefractTint"] = new Vector4(ConvertTint("$refracttint", values.Vector("$refracttint", Vector3.One), shader), 1f);
        var reflectTint = ConvertTint("$reflecttint", values.Vector("$reflecttint", Vector3.One), shader);
        material.VectorParams["g_vReflectTint"] = new Vector4(reflectTint, values.Float("$reflectblendfactor", 1f));
        SetFlag(material, "F_NO_FRESNEL", values.Boolean("$nofresnel"));
        material.FloatParams["g_flRefractAmount"] = values.Float("$refractamount", 0f);
        material.FloatParams["g_flWaterFogStart"] = values.Float("$fogstart", 0f);
        material.FloatParams["g_flWaterFogEnd"] = values.Float("$fogend", 0f);
        material.FloatParams["g_flCheapWaterStartDistance"] = values.Float("$cheapwaterstartdistance", 500f);
        material.FloatParams["g_flCheapWaterEndDistance"] = values.Float("$cheapwaterenddistance", 1000f);
        SetFlag(material, "F_REFRACT", values.Contains("$refracttexture") && !values.Boolean("$forcecheap"));
        SetFlag(material, "F_FADE_OUT_ON_SILHOUETTE", values.Boolean("$fadeoutonsilhouette"));
        SetFlow(material, values);
    }

    // Water whose ripples move along a flow map
    private static void SetFlow(ResourceTypes.Material material, VmtParameters values)
    {
        var hasFlow = material.TextureParams.ContainsKey("g_tFlowmap") && material.TextureParams.ContainsKey("g_tNormal");
        SetFlag(material, "F_FLOWMAP", hasFlow);

        if (!hasFlow)
        {
            return;
        }

        SetFlowParameters(material, values);
        material.FloatParams["g_flFlowBumpStrength"] = values.Float("$flow_bumpstrength", 1f);
        material.FloatParams["g_flFlowTimeScale"] = values.Float("$flow_timescale", 1f);

        var hasColorFlow = material.TextureParams.ContainsKey("g_tColor") && values.Contains("$color_flow_uvscale");
        SetFlag(material, "F_COLOR_FLOW", hasColorFlow);

        if (!hasColorFlow)
        {
            return;
        }

        material.FloatParams["g_flColorFlowUvScale"] = values.Float("$color_flow_uvscale", 1f);
        material.FloatParams["g_flColorFlowTimeInterval"] = values.Float("$color_flow_timeintervalinseconds", 1f);
        material.FloatParams["g_flColorFlowUvScrollDistance"] = values.Float("$color_flow_uvscrolldistance", 0f);
        material.FloatParams["g_flColorFlowLerpExp"] = values.Float("$color_flow_lerpexp", 1f);
        material.FloatParams["g_flColorFlowDisplaceByNormalStrength"] = values.Float("$color_flow_displacebynormalstrength", 0f);
    }

    private static void SetAlpha(ResourceTypes.Material material, VmtParameters values, string shader)
    {
        // Without a positive reference the alpha test uses the default of the render state, then the reference is quantized to 8 bits
        var alphaTestReference = values.Float("$alphatestreference", 0f);

        if (alphaTestReference <= 0f)
        {
            alphaTestReference = DefaultAlphaTestReference;
        }

        material.FloatParams["g_flAlphaTestReference"] = FloorToUnorm8Step(alphaTestReference);
        var opacity = values.Float("$alpha", 1f);
        material.FloatParams["g_flOpacityScale"] = ConvertOpacity(opacity, shader);
    }

    private static void SetTextureFlags(ResourceTypes.Material material, SurfaceFeatures features)
    {
        SetFlag(material, "F_BASE_TEXTURE2", material.TextureParams.ContainsKey("g_tColor2"));
        SetFlag(material, "F_BLEND_MODULATE", material.TextureParams.ContainsKey("g_tBlendModulate"));
        SetFlag(material, "F_SELF_ILLUM_MASK", features.SelfIllum && features.HasSelfIllumMask);
        SetFlag(material, "F_DETAIL", features.HasDetail);
    }

    private static void SetDetail(ResourceTypes.Material material, VmtParameters values, string shader, SurfaceFeatures features)
    {
        if (!features.HasDetail)
        {
            return;
        }

        // The two texture lightmapped shader declares a scale of one, the others four
        var defaultDetailScale = shader == "lightmappedtwotexture" ? 1f : 4f;
        material.FloatParams["g_flDetailScale"] = values.Float("$detailscale", defaultDetailScale);

        // The detail texture blends over the base by its own alpha, without a blend mode, factor or tint
        if (shader == "worldtwotextureblend")
        {
            SetFlag(material, "F_DETAIL_ALPHA_MASK_BASE_TEXTURE", values.Boolean("$detail_alpha_mask_base_texture"));
            return;
        }

        material.FloatParams["g_flDetailBlendFactor"] = values.Float("$detailblendfactor", 1f);
        var detailTint = values.Vector("$detailtint", Vector3.One);
        material.VectorParams["g_vDetailTint"] = new Vector4(ConvertTint("$detailtint", detailTint, shader), 1f);
        material.IntParams["g_nDetailBlendMode"] = values.Integer("$detailblendmode", 0);
    }

    // The second layer of a world material can have a detail texture of its own, which the viewer does not draw
    private static void SetSecondDetail(ResourceTypes.Material material, VmtParameters values, string shader, SurfaceFeatures features)
    {
        if (features.HasDetail && shader == "worldvertextransition")
        {
            SetOptionalTexture(material, values, "$detail2", "g_tDetail2");
        }
    }

    // The four textures of the shader take the weight of their layer from the vertices, and show where it lies within their
    // blend range and the luminance range of the textures
    private static void SetFourWayBlend(ResourceTypes.Material material, VmtParameters values, string shader)
    {
        if (shader != "lightmapped_4wayblend")
        {
            return;
        }

        SetUvScale(material, values, "g_vUvScale2", "$texture2_uvscale");
        SetUvScale(material, values, "g_vUvScale3", "$texture3_uvscale");
        SetUvScale(material, values, "g_vUvScale4", "$texture4_uvscale");

        SetRange(material, values, "g_vLumRange1", "$texture1_lumstart", "$texture1_lumend");
        SetRange(material, values, "g_vLumRange2", "$texture2_lumstart", "$texture2_lumend");
        SetRange(material, values, "g_vLumRange3", "$texture3_lumstart", "$texture3_lumend");
        SetRange(material, values, "g_vLumRange4", "$texture4_lumstart", "$texture4_lumend");

        SetRange(material, values, "g_vBlendRange2", "$texture2_blendstart", "$texture2_blendend");
        SetRange(material, values, "g_vBlendRange3", "$texture3_blendstart", "$texture3_blendend");
        SetRange(material, values, "g_vBlendRange4", "$texture4_blendstart", "$texture4_blendend");

        material.VectorParams["g_vLumBlendFactors"] = new Vector4(
            GetUnitFloat(values, "$lumblendfactor2"),
            GetUnitFloat(values, "$lumblendfactor3"),
            GetUnitFloat(values, "$lumblendfactor4"),
            0f);

        material.VectorParams["g_vBumpBlendFactors"] = new Vector4(
            GetUnitFloat(values, "$texture2_bumpblendfactor"),
            GetUnitFloat(values, "$texture3_bumpblendfactor"),
            GetUnitFloat(values, "$texture4_bumpblendfactor"),
            0f);

        material.VectorParams["g_vDetailBlendFactors"] = new Vector4(
            GetUnitFloat(values, "$detailblendfactor"),
            GetUnitFloat(values, "$detailblendfactor2"),
            GetUnitFloat(values, "$detailblendfactor3"),
            GetUnitFloat(values, "$detailblendfactor4"));

        material.IntParams["g_nTexture3BlendMode"] = values.Integer("$texture3_blendmode", 0);
        material.IntParams["g_nTexture4BlendMode"] = values.Integer("$texture4_blendmode", 0);
    }

    // A scale that is a single number applies to both axes
    private static void SetUvScale(ResourceTypes.Material material, VmtParameters values, string name, string key)
    {
        var uniform = values.Float(key, 1f);
        var scale = values.Pair(key, new Vector2(uniform));

        material.VectorParams[name] = new Vector4(scale, 0f, 0f);
    }

    private static void SetRange(ResourceTypes.Material material, VmtParameters values, string name, string startKey, string endKey)
    {
        var start = GetUnitFloat(values, startKey, 0f);
        var end = GetUnitFloat(values, endKey, 1f);

        material.VectorParams[name] = new Vector4(start, end, 0f, 0f);
    }

    private static float GetUnitFloat(VmtParameters values, string key, float fallback = 1f)
        => Math.Clamp(values.Float(key, fallback), 0f, 1f);

    private static void SetColorTint(ResourceTypes.Material material, VmtParameters values, string shader)
    {
        // $color2 modulates the color of every shader, on top of $color
        var colorTint = values.Vector("$color", Vector3.One) * values.Vector("$color2", Vector3.One);

        colorTint = ConvertColorTint(colorTint, shader);

        if (shader == "vertexlitgeneric")
        {
            var blendTint = values.Boolean("$blendtintbybasealpha") && !values.Boolean("$selfillum");

            SetFlag(material, "F_BLEND_TINT_BY_BASE_ALPHA", blendTint);
            material.FloatParams["g_flBlendTintColorOverBase"] = values.Float("$blendtintcoloroverbase", 0f);
        }

        material.VectorParams["g_vColorTint"] = new Vector4(colorTint, 1f);

        if (values.Boolean("$nofog"))
        {
            material.IntParams["g_bFogEnabled"] = 0;
        }
    }

    private static void SetTransforms(ResourceTypes.Material material, VmtParameters values, string shader, SurfaceFeatures features)
    {
        var isLightmapped = IsLightmapped(shader);

        SetTransform(material, values, "$basetexturetransform", "g_vTexCoordTransformRow0", "g_vTexCoordTransformRow1");

        if (shader is "unlittwotexture" or "monitorscreen")
        {
            SetTransform(material, values, "$texture2transform", "g_vTexCoord2TransformRow0", "g_vTexCoord2TransformRow1");
        }

        if (isLightmapped || IsWater(shader))
        {
            SetTransform(material, values, "$bumptransform", "g_vBumpTransformRow0", "g_vBumpTransformRow1");
        }

        if (!isLightmapped)
        {
            return;
        }

        SetTransform(material, values, "$bumptransform2", "g_vBumpTransform2Row0", "g_vBumpTransform2Row1");

        // Only a surface that blends by its blend modulate texture alone moves the texture
        if (values.Boolean("$maskedblending"))
        {
            SetTransform(material, values, "$blendmasktransform", "g_vBlendMaskTransformRow0", "g_vBlendMaskTransformRow1");
        }

        if (features.HasEnvmapMask)
        {
            SetTransform(material, values, "$envmapmasktransform", "g_vEnvMapMaskTransformRow0", "g_vEnvMapMaskTransformRow1");
        }

        // The seamless mapping replaces the base texture coordinates, and has no detail texture
        if (!features.HasDetail)
        {
            material.FloatParams["g_flSeamlessScale"] = values.Float("$seamless_scale", 0f);
        }
    }

    // What the textures and parameters of a material turn on in the shader, once the combinations the shaders cannot do are removed
    private readonly record struct SurfaceFeatures(
        string? Envmap,
        bool Phong,
        bool BasemapAlphaPhongMask,
        bool HasBump,
        bool HasEnvmapMask,
        bool HasSelfIllumMask,
        bool HasDetail,
        bool SelfIllum,
        bool SelfIllumFromMask,
        bool SelfIllumFresnel,
        bool BaseAlphaEnvmapMask,
        bool NormalMapAlphaEnvmapMask,
        bool UseLightWarp)
    {
        public bool HasEnvmap => Envmap != null;
    }

    // Lightmapped surfaces and detail sprites use the tint as it is, the others convert it from gamma space
    internal static Vector3 ConvertColorTint(Vector3 tint, string shader)
    {
        var rawTint = IsLightmapped(shader) || shader is "detailsprite" or "modulate";

        return rawTint ? tint : TintToLinear(tint);
    }

    // The shaders that tint with the material color clamp its alpha
    internal static float ConvertOpacity(float opacity, string shader)
    {
        var clamps = IsLightmapped(shader) || shader is "vertexlitgeneric" or "unlitgeneric" or "skin";

        return clamps ? Math.Clamp(opacity, 0f, 1f) : opacity;
    }

    // The shader tints the converter stores in linear space, any other tint is stored as it is
    internal static Vector3 ConvertTint(string variable, Vector3 tint, string shader)
    {
        var isLinear = false;

        if (variable.Equals("$envmaptint", StringComparison.OrdinalIgnoreCase))
        {
            isLinear = shader == "refract";
        }
        else if (variable.Equals("$detailtint", StringComparison.OrdinalIgnoreCase))
        {
            isLinear = IsVertexLit(shader);
        }
        else if (variable.Equals("$refracttint", StringComparison.OrdinalIgnoreCase)
            || variable.Equals("$reflecttint", StringComparison.OrdinalIgnoreCase)
            || variable.Equals("$fogcolor", StringComparison.OrdinalIgnoreCase))
        {
            isLinear = IsWater(shader);
        }

        return isLinear ? TintToLinear(tint) : tint;
    }

    private static bool IsWater(string shader) => shader is "water" or "refract" or "watercheap";

    private static bool IsLightmapped(string shader)
        => shader is "lightmappedgeneric" or "worldvertextransition" or "lightmappedreflective" or "lightmappedtwotexture" or "worldtwotextureblend" or "lightmapped_4wayblend";

    private static bool IsVertexLit(string shader) => shader is "vertexlitgeneric" or "eyes" or "eyerefract" or "teeth" or "skin";

    private static Vector3 TintToLinear(Vector3 tint)
        => new(TintChannelToLinear(tint.X), TintChannelToLinear(tint.Y), TintChannelToLinear(tint.Z));

    // A channel brighter than one stays as it is, a nearly white one becomes white and anything else is quantized to
    // 8 bits before the gamma curve
    private static float TintChannelToLinear(float channel)
    {
        if (channel > 1f)
        {
            return channel;
        }

        if (channel < 0f)
        {
            return 0f;
        }

        if (channel >= 0.95f)
        {
            return 1f;
        }

        return MathF.Pow(FloorToUnorm8Step(channel), 2.2f);
    }

    // Rounds a value down to a multiple of 1/255, the steps an 8-bit unsigned normalized value can hold
    private static float FloorToUnorm8Step(float x) => MathF.Floor(x * 255f) / 255f;

    private static bool? ResolveHdrCondition(string symbol, bool highDynamicRange)
    {
        if (symbol.Equals("hdr", StringComparison.OrdinalIgnoreCase))
        {
            return highDynamicRange;
        }

        return null;
    }

    // A detail texture of a lightmapped surface that is flagged as a self shadowed bump map holds lightmap weights
    // when the surface has a bump map and a brightness otherwise, whatever blend mode the material asks for
    private static void ApplySsbumpDetail(Source1FileSystem fileSystem, ResourceTypes.Material material, string shader)
    {
        const int DetailBlendBumpWeights = 10;
        const int DetailBlendBrightness = 11;

        if (!IsLightmapped(shader))
        {
            return;
        }

        if (!material.TextureParams.TryGetValue("g_tDetail", out var detailPath))
        {
            return;
        }

        if (Source1TextureHeaders.TryRead(fileSystem, detailPath) is not { } header || (header.Flags & VtfFlags.SSBUMP) == 0)
        {
            return;
        }

        var hasBump = material.TextureParams.ContainsKey("g_tNormal");
        material.IntParams["g_nDetailBlendMode"] = hasBump ? DetailBlendBumpWeights : DetailBlendBrightness;
    }

    internal static bool BaseTextureHasAlpha(Source1FileSystem fileSystem, VmtFile vmt)
    {
        if (Source1TextureHeaders.TryReadBaseTexture(fileSystem, vmt) is not { } header)
        {
            return true;
        }

        return (header.Flags & (VtfFlags.ONEBITALPHA | VtfFlags.EIGHTBITALPHA)) != 0;
    }

    private static void SetTransform(ResourceTypes.Material material, VmtParameters values, string key, string row0Name, string row1Name)
    {
        if (!TryReadTransform(values, key, out var transform))
        {
            return;
        }

        material.VectorParams[row0Name] = new Vector4(transform.M11, transform.M21, 0f, transform.M41);
        material.VectorParams[row1Name] = new Vector4(transform.M12, transform.M22, 0f, transform.M42);
    }

    private static bool TryReadTransform(VmtParameters values, string key, out Matrix4x4 transform)
    {
        transform = Matrix4x4.Identity;

        if (!values.Contains(key))
        {
            return false;
        }

        try
        {
            transform = values.Transform(key);
            return true;
        }
        catch (System.IO.InvalidDataException)
        {
            // Shipped materials have transforms that do not parse, which leave the texture untransformed
            return false;
        }
    }

    private static void SetFlag(ResourceTypes.Material material, string name, bool enabled)
        => material.IntParams[name] = enabled ? 1 : 0;
}
