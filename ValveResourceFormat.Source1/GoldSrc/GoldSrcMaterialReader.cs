using ValveResourceFormat.Source1.Material;
using Channel = ValveResourceFormat.CompiledShader.ChannelMapping;

namespace ValveResourceFormat.GoldSrc;

/// <summary>
/// Describes converted GoldSrc materials for export. They have a color texture and a render mode.
/// </summary>
internal static class GoldSrcMaterialReader
{
    /// <summary>Gets whether the material is a converted GoldSrc material.</summary>
    public static bool CanRead(ResourceTypes.Material material)
    {
        ArgumentNullException.ThrowIfNull(material);

        return material.ShaderName.StartsWith(ResourceTypes.Material.GoldSrcShaderPrefix, StringComparison.Ordinal);
    }

    /// <summary>Describes the material.</summary>
    public static MaterialDescription Read(ResourceTypes.Material material)
    {
        ArgumentNullException.ThrowIfNull(material);

        var shader = material.ShaderName[ResourceTypes.Material.GoldSrcShaderPrefix.Length..];
        var isUnlit = IsEnabled(material, "F_UNLIT") || material.IntParams.GetValueOrDefault("g_nSurfaceMode") != 0 || shader.StartsWith("sprite", StringComparison.Ordinal);

        var description = new MaterialDescription
        {
            Role = GetRole(shader, isUnlit),
            BlendMode = GetBlendMode(material),
            RenderBackfaces = IsEnabled(material, "F_RENDER_BACKFACES"),
            AlphaTestReference = material.FloatParams.GetValueOrDefault("g_flAlphaTestReference", 0.5f),
        };

        if (material.TextureParams.TryGetValue("g_tColor", out var color))
        {
            description.Layers[0].Color = MaterialInput.FromTexture(color, Channel.RGB);

            if (description.BlendMode is MaterialBlendMode.AlphaTest or MaterialBlendMode.Translucent)
            {
                description.Translucency = MaterialInput.FromTexture(color, Channel.A);
            }
        }

        if (material.VectorParams.TryGetValue("g_vRenderColor", out var renderColor))
        {
            description.Layers[0].ColorTint = new Vector3(renderColor.X, renderColor.Y, renderColor.Z);
            description.Opacity = renderColor.W;
        }

        return description;
    }

    private static bool IsEnabled(ResourceTypes.Material material, string feature)
        => material.IntParams.GetValueOrDefault(feature) != 0;

    private static MaterialRole GetRole(string shader, bool isUnlit)
    {
        if (isUnlit)
        {
            return MaterialRole.Unlit;
        }

        return shader.StartsWith("world", StringComparison.Ordinal) ? MaterialRole.LitWorld : MaterialRole.LitModel;
    }

    private static MaterialBlendMode GetBlendMode(ResourceTypes.Material material)
    {
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
}
