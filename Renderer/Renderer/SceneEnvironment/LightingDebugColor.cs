namespace ValveResourceFormat.Renderer.SceneEnvironment;

/// <summary>
/// The colour lighting debug views give a light probe volume or an environment map, derived from where
/// it is so it stays the same between runs and differs between neighbours.
/// </summary>
public static class LightingDebugColor
{
    private const uint MurmurMultiplier = 0x5BD1E995;

    /// <summary>Gets the debug colour for a volume placed at the given origin.</summary>
    /// <param name="origin">The volume's origin in its scene.</param>
    /// <returns>The colour, in sRGB.</returns>
    public static Color32 FromOrigin(Vector3 origin)
    {
        var hash = HashOrigin(origin);

        var hue = hash * 1.40625f;
        var lightness = 0.2f + (hash & 15) * 0.6f / 16f;

        var linear = HslToRgb(hue, 0.75f, lightness);

        return new Color32(LinearToSrgb(linear.X), LinearToSrgb(linear.Y), LinearToSrgb(linear.Z), 1f);
    }

    /// <summary>MurmurHash2 over the floored coordinates, reduced to a byte.</summary>
    private static byte HashOrigin(Vector3 origin)
    {
        static uint Mix(float value)
        {
            var k = unchecked((uint)(int)MathF.Floor(value)) * MurmurMultiplier;
            k ^= k >> 24;
            return k * MurmurMultiplier;
        }

        // The seed mixed with the length of three ints, already multiplied through
        var h = Mix(origin.X) ^ 0xEA711BD8;
        h = (h * MurmurMultiplier) ^ Mix(origin.Y);
        h = (h * MurmurMultiplier) ^ Mix(origin.Z);

        h ^= h >> 13;
        h *= MurmurMultiplier;
        h ^= h >> 15;

        return (byte)h;
    }

    private static Vector3 HslToRgb(float hue, float saturation, float lightness)
    {
        var chroma = (1f - MathF.Abs(2f * lightness - 1f)) * saturation;
        var sector = hue % 360f / 60f;
        var x = chroma * (1f - MathF.Abs(sector % 2f - 1f));

        var rgb = (int)sector switch
        {
            0 => new Vector3(chroma, x, 0f),
            1 => new Vector3(x, chroma, 0f),
            2 => new Vector3(0f, chroma, x),
            3 => new Vector3(0f, x, chroma),
            4 => new Vector3(x, 0f, chroma),
            _ => new Vector3(chroma, 0f, x),
        };

        return rgb + new Vector3(lightness - chroma * 0.5f);
    }

    private static float LinearToSrgb(float value)
    {
        value = Math.Clamp(value, 0f, 1f);

        return value > 0.0031308f
            ? MathF.Pow(value, 1f / 2.4f) * 1.055f - 0.055f
            : value * 12.92f;
    }
}
