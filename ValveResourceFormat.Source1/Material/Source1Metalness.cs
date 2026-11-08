namespace ValveResourceFormat.Source1.Material;

/// <summary>
/// Derives the metalness of a Source 1 material. A surface is metal-like when its reflection takes the color of the surface:
/// an environment map with a colored tint, or a highlight tinted by the albedo. The image written is the metalness, white being metal.
/// </summary>
internal static class Source1Metalness
{
    /// <summary>
    /// Gets the metalness of a character material, which the blue channel of its first mask texture holds.
    /// </summary>
    /// <param name="material">The converted material, which holds the paths of the textures.</param>
    /// <param name="values">The parameters of the VMT of the material.</param>
    /// <returns>The metalness, or null when the material has no mask texture or its environment map tint is black or missing.</returns>
    public static MaterialInput? ReadCharacter(ResourceTypes.Material material, VmtParameters values)
    {
        if (!material.TextureParams.TryGetValue("g_tMasks1", out var masksTexture) || values.String("$envmaptint") is null)
        {
            return null;
        }

        var strength = GetLuminance(values.Vector("$envmaptint", Vector3.One));

        if (strength <= 0f)
        {
            return null;
        }

        return MaterialInput.FromFunction(masksTexture, pixel => Math.Clamp(pixel.Z * strength, 0f, 1f));
    }

    /// <summary>
    /// Gets the metalness of a material with a highlight or an environment map.
    /// </summary>
    /// <param name="material">The converted material, which holds the paths of the textures.</param>
    /// <param name="values">The parameters of the VMT of the material.</param>
    /// <param name="isPhong">Whether the material has a phong highlight.</param>
    /// <param name="hasEnvmap">Whether the material has an environment map.</param>
    /// <param name="normalHasAlpha">Whether the normal texture has an alpha channel, which is then a mask.</param>
    /// <returns>The metalness, or null when the material is not metal-like or has no mask that says where it is.</returns>
    public static MaterialInput? ReadReflective(ResourceTypes.Material material, VmtParameters values, bool isPhong, bool hasEnvmap, bool normalHasAlpha)
    {
        var textures = material.TextureParams;
        var baseTexture = textures.GetValueOrDefault("g_tColor");
        var normalTexture = textures.GetValueOrDefault("g_tNormal");

        if (hasEnvmap && HasColoredTint(values))
        {
            if (textures.GetValueOrDefault("g_tEnvMapMask") is { } envmapMask)
            {
                return MaterialInput.FromFunction(envmapMask, pixel => Math.Clamp(pixel.Y, 0f, 1f));
            }

            if (baseTexture is not null && values.Boolean("$basealphaenvmapmask"))
            {
                return MaterialInput.FromFunction(baseTexture, pixel => Math.Clamp(pixel.W, 0f, 1f));
            }

            if (normalTexture is not null && normalHasAlpha && values.Boolean("$normalmapalphaenvmapmask"))
            {
                return MaterialInput.FromFunction(normalTexture, pixel => Math.Clamp(pixel.W, 0f, 1f));
            }
        }
        else if (isPhong && values.Boolean("$phongalbedotint"))
        {
            if (baseTexture is not null && values.Boolean("$basemapalphaphongmask"))
            {
                return MaterialInput.FromFunction(baseTexture, pixel => Math.Clamp(pixel.W, 0f, 1f));
            }

            if (normalTexture is not null && normalHasAlpha)
            {
                return MaterialInput.FromFunction(normalTexture, pixel => Math.Clamp(pixel.W, 0f, 1f));
            }
        }

        // Without a mask the reflection is not known to be metal
        return null;
    }

    // A gray tint scales the reflection, a colored one filters it as metal does
    private static bool HasColoredTint(VmtParameters values)
    {
        if (values.String("$envmaptint") is null)
        {
            return false;
        }

        var tint = values.Vector("$envmaptint", Vector3.One);

        return tint.X != tint.Y || tint.Y != tint.Z;
    }

    private static float GetLuminance(Vector3 color) => (color.X * 0.2125f) + (color.Y * 0.7154f) + (color.Z * 0.0721f);
}
