namespace ValveResourceFormat.Source1.Material;

/// <summary>
/// Derives the roughness of a Source 1 material from its specular settings. Shiny surfaces are smooth, and where a mask
/// says how shiny the surface is, the roughness follows the mask. The image written is the roughness, white being rough.
/// </summary>
internal static class Source1Roughness
{
    private const float DefaultFresnel = 9f / 41f;
    private const float MinimumBoost = 0.2f;
    private const float PowerExponent = 0.2f;

    private enum MaskSource
    {
        Alpha,
        Luminance,
        Green,
    }

    // How much of a mask value turns into shine: the roughness is (1 - scale * mask)^2
    private readonly record struct Shine(float Scale, float Boost);

    /// <summary>
    /// Gets the roughness of a material.
    /// </summary>
    /// <param name="material">The converted material, which holds the paths of the textures.</param>
    /// <param name="values">The parameters of the VMT of the material.</param>
    /// <param name="isPhong">Whether the material has a phong highlight.</param>
    /// <param name="hasEnvmap">Whether the material has an environment map.</param>
    /// <returns>The roughness, or null when the material has neither highlight nor reflection.</returns>
    public static MaterialInput? Read(ResourceTypes.Material material, VmtParameters values, bool isPhong, bool hasEnvmap)
    {
        if (!isPhong && !hasEnvmap)
        {
            return null;
        }

        var textures = material.TextureParams;
        var baseTexture = textures.GetValueOrDefault("g_tColor");
        var normalTexture = textures.GetValueOrDefault("g_tNormal");
        var envmapMaskTexture = textures.GetValueOrDefault("g_tEnvMapMask");

        var strength = hasEnvmap ? GetEnvmapStrength(values) : 0f;

        var exponentTexture = textures.GetValueOrDefault("g_tPhongExponent");

        if (isPhong && ReadPhongMask(values, baseTexture, normalTexture, exponentTexture) is { } phongMask)
        {
            return phongMask;
        }

        if (hasEnvmap)
        {
            var scale = isPhong ? (strength + 1f) / 2f : strength;

            if (envmapMaskTexture is not null)
            {
                return MaterialInput.FromFunction(envmapMaskTexture, pixel => GetEnvmapRoughness(scale, GetMask(pixel, MaskSource.Green)));
            }

            if (baseTexture is not null && values.Boolean("$basealphaenvmapmask"))
            {
                return MaterialInput.FromFunction(baseTexture, pixel => GetEnvmapRoughness(scale, GetMask(pixel, MaskSource.Alpha)));
            }

            if (normalTexture is not null && values.Boolean("$normalmapalphaenvmapmask"))
            {
                return MaterialInput.FromFunction(normalTexture, pixel => GetEnvmapRoughness(scale, GetMask(pixel, MaskSource.Alpha)));
            }
        }

        var constantTexture = baseTexture ?? normalTexture;

        if (constantTexture is null)
        {
            return null;
        }

        var roughness = GetConstantRoughness(isPhong, hasEnvmap, strength);

        return MaterialInput.FromFunction(constantTexture, _ => roughness);
    }

    private static MaterialInput? ReadPhongMask(VmtParameters values, string? baseTexture, string? normalTexture, string? exponentTexture)
    {
        var shine = GetShine(values);
        var constantExponent = MathF.Max(0f, values.Float("$phongexponent", 0f));
        var exponentFactor = values.Float("$phongexponentfactor", 0f);
        var power = GetPower(constantExponent);

        string maskTexture;
        MaskSource source;

        if (baseTexture is not null && values.Boolean("$basemapalphaphongmask"))
        {
            maskTexture = baseTexture;
            source = MaskSource.Alpha;
        }
        else if (baseTexture is not null && values.Boolean("$basemapluminancephongmask"))
        {
            maskTexture = baseTexture;
            source = MaskSource.Luminance;
        }
        else if (normalTexture is not null)
        {
            maskTexture = normalTexture;
            source = MaskSource.Alpha;
        }
        else
        {
            return null;
        }

        if (exponentTexture is not null && (exponentFactor != 0f || constantExponent <= 0f))
        {
            return MaterialInput.FromPair(maskTexture, exponentTexture, (maskPixel, exponentPixel) =>
            {
                var exponent = exponentFactor != 0f ? 1f + (exponentFactor * exponentPixel.X) : 1f + (149f * exponentPixel.X);
                return GetPhongRoughness(shine, GetPower(exponent), GetMask(maskPixel, source));
            });
        }

        return MaterialInput.FromFunction(maskTexture, pixel => GetPhongRoughness(shine, power, GetMask(pixel, source)));
    }

    private static float GetMask(Vector4 pixel, MaskSource source) => source switch
    {
        MaskSource.Alpha => pixel.W,
        MaskSource.Luminance => GetLuminance(new Vector3(pixel.X, pixel.Y, pixel.Z)),
        _ => pixel.Y,
    };

    private static float GetLuminance(Vector3 color) => (color.X * 0.2125f) + (color.Y * 0.7154f) + (color.Z * 0.0721f);

    private static float GetEnvmapStrength(VmtParameters values)
    {
        if (values.String("$envmaptint") is null)
        {
            return 1f;
        }

        return MathF.Min(1f, GetLuminance(values.Vector("$envmaptint", Vector3.One)));
    }

    // The highlight is scaled by the boost, which the tint and the fresnel ranges of the material change
    private static Shine GetShine(VmtParameters values)
    {
        var boost = values.Float("$phongboost", 1f);

        if (values.String("$phongtint") is not null)
        {
            var tintLuminance = GetLuminance(values.Vector("$phongtint", Vector3.One));

            if (tintLuminance > 0f)
            {
                boost *= tintLuminance;
            }
        }

        var fresnel = DefaultFresnel;

        if (values.String("$phongfresnelranges") is not null)
        {
            var ranges = values.Vector("$phongfresnelranges", Vector3.One);
            var envmapFresnel = values.Float("$envmapfresnel", 1f);

            boost *= ranges.Z;
            fresnel = ranges.X * ((envmapFresnel * (ranges.X - 1f)) + 1f);
        }

        return new Shine(fresnel, MathF.Max(MinimumBoost, boost));
    }

    // A higher exponent sharpens the highlight
    private static float GetPower(float exponent)
    {
        return MathF.Pow(0.5f + (0.5f * MathF.Min(1f, exponent / 128f)), PowerExponent);
    }

    private static float GetPhongRoughness(Shine shine, float power, float mask)
    {
        var amount = MathF.Min(1f, Math.Clamp(mask, 0f, 1f) * shine.Boost);
        var smoothness = 1f - (power * shine.Scale * amount);

        return smoothness * smoothness;
    }

    private static float GetEnvmapRoughness(float scale, float mask)
    {
        var smoothness = 1f - (scale * Math.Clamp(mask, 0f, 1f));

        return smoothness * smoothness;
    }

    private static float GetConstantRoughness(bool isPhong, bool hasEnvmap, float strength)
    {
        var shine = MathF.Max(strength, isPhong ? 1f : 0f);

        if (isPhong && hasEnvmap && strength > 0f)
        {
            shine = (strength + 1f) / 2f;
        }

        var smoothness = 1f - shine;

        return smoothness * smoothness;
    }
}
