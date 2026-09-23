using System.Diagnostics;
using ValveResourceFormat.Renderer.World;

namespace ValveResourceFormat.Renderer.SceneEnvironment;

/// <summary>
/// Scene node representing cubemap-based volumetric fog.
/// </summary>
public class SceneCubemapFog(Scene scene) : SceneNode(scene)
{
    /// <summary>Gets or sets the distance at which fog begins.</summary>
    public float StartDist { get; set; }

    /// <summary>Gets or sets the distance at which fog reaches full density.</summary>
    public float EndDist { get; set; }

    /// <summary>Gets or sets the exponent controlling the fog density falloff curve.</summary>
    public float FalloffExponent { get; set; }

    /// <summary>Gets or sets the world-space height at which height fog is at full density and begins to fade.</summary>
    public float HeightStart { get; set; }

    /// <summary>Gets or sets the world-space height at which height fog fades to zero density.</summary>
    public float HeightEnd { get; set; }

    /// <summary>Gets or sets the exponent controlling the height fog falloff curve.</summary>
    public float HeightExponent { get; set; }

    /// <summary>Gets or sets the mip LOD bias applied when sampling the cubemap fog texture.</summary>
    public float LodBias { get; set; }

    /// <summary>Gets or sets the overall opacity of the fog.</summary>
    public float Opacity { get; set; }

    /// <summary>Gets or sets whether height-based fog is enabled.</summary>
    public bool UseHeightFog { get; set; }

    /// <summary>Gets or sets the cubemap texture used for fog color sampling.</summary>
    public RenderTexture? CubemapFogTexture { get; set; }

    /// <summary>Gets or sets the exposure bias applied to the fog color.</summary>
    public float ExposureBias { get; set; }

    /// <summary>
    /// Returns a <see cref="Vector4"/> encoding the distance fog offset, scale, LOD bias, and falloff exponent for shader use.
    /// </summary>
    /// <param name="fogSpace">Converts authored distances and heights into the view's space.</param>
    public Vector4 OffsetScaleBiasExponent(FogSpace fogSpace)
    {
        var startDist = fogSpace.Distance(StartDist);

        var scale = 1f / (fogSpace.Distance(EndDist) - startDist);
        var offset = -(startDist * scale);

        return new Vector4(offset, scale, LodBias, FalloffExponent);
    }

    // HeightWidth is equal to HeightEnd - HeightStart
    // Height width ADDS to heightStart
    /// <summary>
    /// Returns a <see cref="Vector4"/> encoding the height fog offset, scale, exponent, and log2 mip level for shader use.
    /// </summary>
    /// <param name="fogSpace">Converts authored distances and heights into the view's space.</param>
    public Vector4 Height_OffsetScaleExponentLog2Mip(FogSpace fogSpace)
    {
        Debug.Assert(CubemapFogTexture != null);

        var offset = 1f;
        var scale = 0.000001f;
        var exponent = 0f;

        if (HeightEnd - HeightStart > 0) // width = 0 is a substitution for UseHeightFog
        {
            var heightStart = fogSpace.Height(HeightStart);

            scale = 1f / (heightStart - fogSpace.Height(HeightEnd));
            offset = 1f - (heightStart * scale);
            exponent = HeightExponent;
        }

        var value = new Vector4(offset, scale, exponent, Math.Min(7f, CubemapFogTexture.NumMipLevels)); // these latter two values are wrong on deskjob?

        return value;
    }

    /// <summary>
    /// Returns a <see cref="Vector4"/> encoding the distance culling squared, height culling, linear exposure, and opacity for shader use.
    /// </summary>
    /// <param name="fogSpace">Converts authored distances and heights into the view's space.</param>
    public Vector4 CullingParams_Opacity(FogSpace fogSpace)
    {
        var startDist = fogSpace.Distance(StartDist);
        var distCull = startDist * startDist;
        var heightCull = (UseHeightFog || ((HeightEnd - HeightStart) > 0)) ? fogSpace.Height(HeightStart) : float.PositiveInfinity;

        return new Vector4(distCull, heightCull, MathF.Pow(2f, ExposureBias), Opacity);
    }
}
