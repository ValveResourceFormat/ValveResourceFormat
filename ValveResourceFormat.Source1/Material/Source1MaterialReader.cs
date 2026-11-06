using System.Globalization;
using System.IO;
using ValveResourceFormat.Source1.FileSystem;
using ValveResourceFormat.Source1.Texture;
using ValveResourceFormat.Utils;
using Channel = ValveResourceFormat.CompiledShader.ChannelMapping;

namespace ValveResourceFormat.Source1.Material;

/// <summary>
/// Describes converted Source 1 materials for export. The converted material has the textures and the render state of the
/// viewer, the VMT of the game says what the shader of the game does with them.
/// </summary>
internal sealed class Source1MaterialReader(Source1FileSystem fileSystem, Func<bool> highDynamicRange)
{
    /// <summary>Gets whether the material is a converted Source 1 material.</summary>
    public static bool CanRead(ResourceTypes.Material material)
    {
        ArgumentNullException.ThrowIfNull(material);

        return material.ShaderName.StartsWith(ResourceTypes.Material.Source1ShaderPrefix, StringComparison.Ordinal);
    }

    /// <summary>Describes the material.</summary>
    public MaterialDescription Read(ResourceTypes.Material material)
    {
        ArgumentNullException.ThrowIfNull(material);

        var shader = material.ShaderName[ResourceTypes.Material.Source1ShaderPrefix.Length..];
        var values = LoadValues(material);
        var textures = material.TextureParams;

        var isPhong = IsEnabled(material, "F_PHONG");
        var hasEnvmap = IsEnabled(material, "F_ENVMAP");
        var hasNormal = textures.ContainsKey("g_tNormal") && !values.Boolean("$ssbump");

        var description = new MaterialDescription
        {
            Role = GetRole(shader, material, values, hasNormal || isPhong),
            RenderBackfaces = IsEnabled(material, "F_RENDER_BACKFACES"),
            SpecularDirect = isPhong,
            SpecularIndirect = isPhong || hasEnvmap,
            UseVertexColor = IsEnabled(material, "F_VERTEX_COLOR"),
            Opacity = material.FloatParams.GetValueOrDefault("g_flOpacityScale", 1f),
            AlphaTestReference = material.FloatParams.GetValueOrDefault("g_flAlphaTestReference", 0.5f),
            SurfaceProperty = values.String("$surfaceprop")?.ToLowerInvariant(),
        };

        description.BlendMode = GetBlendMode(shader, material);

        ReadLayers(description, material, hasNormal);

        if (shader == "detailsprite")
        {
            description.Layers[0].ColorTint = ColorSpace.SrgbGammaToLinear(description.Layers[0].ColorTint);
        }

        ReadTransform(description, material);
        ReadSelfIllum(description, material);

        if (description.Role is MaterialRole.Water or MaterialRole.Refract)
        {
            ReadWater(description, material);
        }

        if (IsEnabled(material, "F_DETAIL") && textures.TryGetValue("g_tDetail", out var detail))
        {
            description.Detail = MaterialInput.FromTexture(detail, Channel.RGB);
        }

        if (textures.TryGetValue("g_tBlendModulate", out var blendModulate))
        {
            description.BlendMask = MaterialInput.FromTexture(blendModulate, Channel.G);
        }

        if (IsAlphaOpacity(material, values) && description.BlendMode is MaterialBlendMode.AlphaTest or MaterialBlendMode.Translucent)
        {
            AddTranslucency(description);
        }

        var isLit = description.Role is MaterialRole.LitModel or MaterialRole.LitWorld or MaterialRole.Blend2 or MaterialRole.Blend4;

        if (isLit && Source1Roughness.Read(material, values, isPhong, hasEnvmap) is { } roughness)
        {
            description.Layers[0].Roughness = roughness;
        }

        return description;
    }

    private VmtParameters LoadValues(ResourceTypes.Material material)
    {
        var path = Source1Paths.ToGameMaterialPath(Source1Paths.RemoveVariant(material.Name, out _));

        try
        {
            return Source1MaterialConverter.LoadVmt(path, fileSystem, highDynamicRange()).Values;
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            return new VmtParameters(new Dictionary<string, string>(), path);
        }
    }

    private static bool IsEnabled(ResourceTypes.Material material, string feature)
        => material.IntParams.GetValueOrDefault(feature) != 0;

    // The sky shader of the target draws a cube texture, the faces of a Source 1 skybox are flat textures
    private bool IsCubeTexture(ResourceTypes.Material material)
    {
        if (!material.TextureParams.TryGetValue("g_tColor", out var path))
        {
            return false;
        }

        var header = Source1TextureHeaders.TryRead(fileSystem, Source1Paths.RemoveVariant(path, out _));

        return header is { } vtf && (vtf.Flags & VtfFlags.ENVMAP) != 0;
    }

    private MaterialRole GetRole(string shader, ResourceTypes.Material material, VmtParameters values, bool isLitByDefault)
    {
        if (shader == "decalmodulate" || values.Boolean("$decal") || material.IntAttributes.GetValueOrDefault("overlay") != 0)
        {
            return MaterialRole.Decal;
        }

        return shader switch
        {
            "vertexlitgeneric" or "eyes" or "eyerefract" or "teeth" or "skin" or "infected" => MaterialRole.LitModel,
            "lightmappedtwotexture" or "worldtwotextureblend" when material.TextureParams.ContainsKey("g_tColor2") => MaterialRole.Blend2,
            "lightmappedgeneric" or "lightmappedreflective" or "lightmappedtwotexture" or "worldtwotextureblend" => MaterialRole.LitWorld,
            "worldvertextransition" => MaterialRole.Blend2,
            "lightmapped_4wayblend" => MaterialRole.Blend4,
            "unlitgeneric" or "unlittwotexture" or "modulate" or "monitorscreen" or "solidenergy" or "detailsprite" => MaterialRole.Unlit,
            "water" or "watercheap" => MaterialRole.Water,
            "refract" => MaterialRole.Refract,
            "sky" => IsCubeTexture(material) ? MaterialRole.Sky : MaterialRole.Unlit,
            _ => isLitByDefault ? MaterialRole.LitModel : MaterialRole.Unlit,
        };
    }

    private static MaterialBlendMode GetBlendMode(string shader, ResourceTypes.Material material)
    {
        if (shader is "decalmodulate" or "modulate")
        {
            return MaterialBlendMode.Mod2x;
        }

        if (IsEnabled(material, "F_ADDITIVE_BLEND"))
        {
            return MaterialBlendMode.Additive;
        }

        if (IsEnabled(material, "F_TRANSLUCENT"))
        {
            return MaterialBlendMode.Translucent;
        }

        return IsEnabled(material, "F_ALPHA_TEST") ? MaterialBlendMode.AlphaTest : MaterialBlendMode.Opaque;
    }

    // The alpha of the base texture is the opacity unless the material uses it for something else
    private static bool IsAlphaOpacity(ResourceTypes.Material material, VmtParameters values)
    {
        var isSelfIllumFromAlpha = IsEnabled(material, "F_SELF_ILLUM") && !IsEnabled(material, "F_SELF_ILLUM_MASK");

        return !isSelfIllumFromAlpha
            && !values.Boolean("$basealphaenvmapmask")
            && !values.Boolean("$basemapalphaphongmask");
    }

    private static void ReadLayers(MaterialDescription description, ResourceTypes.Material material, bool hasNormal)
    {
        var textures = material.TextureParams;
        var layerCount = description.Role switch
        {
            MaterialRole.Blend2 => 2,
            MaterialRole.Blend4 => 4,
            _ => 1,
        };

        while (description.Layers.Count < layerCount)
        {
            description.Layers.Add(new MaterialLayer());
        }

        if (textures.TryGetValue("g_tColor", out var color))
        {
            description.Layers[0].Color = MaterialInput.FromTexture(color, Channel.RGB);
        }

        if (hasNormal)
        {
            description.Layers[0].Normal = MaterialInput.FromTexture(textures["g_tNormal"], Channel.RGB) with { InvertGreen = true };
        }

        if (material.VectorParams.TryGetValue("g_vColorTint", out var tint))
        {
            description.Layers[0].ColorTint = new Vector3(tint.X, tint.Y, tint.Z);
        }

        for (var i = 1; i < layerCount; i++)
        {
            var number = (i + 1).ToString(CultureInfo.InvariantCulture);

            if (textures.TryGetValue("g_tColor" + number, out var layerColor))
            {
                description.Layers[i].Color = MaterialInput.FromTexture(layerColor, Channel.RGB);
            }

            if (hasNormal && textures.TryGetValue("g_tNormal" + number, out var layerNormal))
            {
                description.Layers[i].Normal = MaterialInput.FromTexture(layerNormal, Channel.RGB) with { InvertGreen = true };
            }
        }
    }

    // The base texture transform reaches the material as the rows of a texture matrix
    private static void ReadTransform(MaterialDescription description, ResourceTypes.Material material)
    {
        if (!material.VectorParams.TryGetValue("g_vTexCoordTransformRow0", out var row0)
            || !material.VectorParams.TryGetValue("g_vTexCoordTransformRow1", out var row1))
        {
            return;
        }

        var rotation = MathF.Atan2(row1.X, row0.X);
        var cosine = MathF.Cos(rotation);
        var sine = MathF.Sin(rotation);

        // The target scales around the origin and rotates around the middle of the texture, so the offset
        // is what remains of the translation once the rotation of the middle is taken out
        description.UvScale = new Vector2(new Vector2(row0.X, row1.X).Length(), new Vector2(row0.Y, row1.Y).Length());
        description.UvOffset = new Vector2(row0.W - 0.5f + (0.5f * (cosine - sine)), row1.W - 0.5f + (0.5f * (sine + cosine)));
        description.UvRotation = float.RadiansToDegrees(rotation);
    }

    private static void ReadWater(MaterialDescription description, ResourceTypes.Material material)
    {
        var textures = material.TextureParams;
        var vectors = material.VectorParams;
        var floats = material.FloatParams;

        if (vectors.TryGetValue("g_vRefractTint", out var refractTint))
        {
            description.RefractTint = new Vector3(refractTint.X, refractTint.Y, refractTint.Z);
        }

        description.RefractAmount = floats.GetValueOrDefault("g_flRefractAmount");

        if (description.Role != MaterialRole.Water)
        {
            return;
        }

        if (vectors.TryGetValue("g_vWaterFogColor", out var fogColor))
        {
            description.WaterFogColor = new Vector3(fogColor.X, fogColor.Y, fogColor.Z);
        }

        description.WaterFogStart = floats.GetValueOrDefault("g_flWaterFogStart");
        description.WaterFogEnd = floats.GetValueOrDefault("g_flWaterFogEnd");
        description.FlowColor = IsEnabled(material, "F_COLOR_FLOW");

        if (IsEnabled(material, "F_FLOWMAP") && textures.TryGetValue("g_tFlowmap", out var flow))
        {
            description.Flow = MaterialInput.FromTexture(flow, Channel.RGB);

            if (textures.TryGetValue("g_tFlowNoise", out var noise))
            {
                description.FlowNoise = MaterialInput.FromTexture(noise, Channel.G);
            }
        }
    }

    private static void ReadSelfIllum(MaterialDescription description, ResourceTypes.Material material)
    {
        if (!IsEnabled(material, "F_SELF_ILLUM"))
        {
            return;
        }

        if (IsEnabled(material, "F_SELF_ILLUM_MASK") && material.TextureParams.TryGetValue("g_tSelfIllumMask", out var mask))
        {
            description.SelfIllumMask = MaterialInput.FromTexture(mask, Channel.R);
        }
        else if (description.Layers[0].Color.Texture is { } color)
        {
            description.SelfIllumMask = MaterialInput.FromTexture(color, Channel.A);
        }

        if (material.VectorParams.TryGetValue("g_vSelfIllumTint", out var tint))
        {
            description.SelfIllumTint = new Vector3(tint.X, tint.Y, tint.Z);
        }
    }

    private static void AddTranslucency(MaterialDescription description)
    {
        if (description.Layers[0].Color.Texture is { } color)
        {
            description.Translucency = MaterialInput.FromTexture(color, Channel.A);
        }
    }
}
