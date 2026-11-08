using ValveResourceFormat.Source1.Bsp;

namespace ValveResourceFormat.Source1.Entities;

/// <summary>
/// Options for <see cref="Source1EntityConverter"/>.
/// </summary>
internal sealed class Source1EntityConverterOptions
{
    /// <summary>
    /// Gets whether Source 1 classnames are replaced with the Source 2 classes the renderer implements,
    /// such as <c>light</c> with <c>light_omni</c>.
    /// </summary>
    public bool RemapClassNames { get; init; } = true;

    /// <summary>
    /// Gets whether converted lights are written as dynamic (<c>directlight 2</c>), which is what lights models
    /// when no Source 2 lightmap exists. Otherwise they are written as baked (<c>directlight 1</c>).
    /// </summary>
    public bool DynamicLights { get; init; } = true;

    /// <summary>
    /// Gets whether the map is lit with its HDR lighting, which makes lights read their HDR keys such as
    /// <c>_lightHDR</c>. Otherwise only the LDR keys are read.
    /// </summary>
    public bool HdrLighting { get; init; }

    /// <summary>
    /// Gets whether the lights of the map are GoldSrc lights, which have no attenuation keys, read
    /// <c>_cone</c> and <c>_cone2</c> as the inner and outer angle of a spot, and give the sun a raw intensity.
    /// </summary>
    public bool GoldSrcLighting { get; init; }

    /// <summary>
    /// Gets the light level at which a dynamic light has faded to nothing at its radius, which depends on the
    /// version of the map.
    /// </summary>
    public float MinLightingValue { get; init; } = 1f / 256f;

    private static float GetMinLightingValue(int bspVersion)
        => bspVersion >= 20 ? 1f / 256f : 20f / 256f;

    /// <summary>
    /// Gets the origin of the first entity with each targetname, which is where a light with a <c>target</c>
    /// key points.
    /// </summary>
    public IReadOnlyDictionary<string, Vector3> TargetOrigins { get; init; } = new Dictionary<string, Vector3>();

    /// <summary>Creates the options for converting the entities of a map.</summary>
    public static Source1EntityConverterOptions ForMap(BspFile bsp)
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
            HdrLighting = bsp.HasHdrLighting,
            MinLightingValue = GetMinLightingValue(bsp.Version),
            TargetOrigins = targetOrigins,
        };
    }
}
