using ValveResourceFormat.Renderer.World;

namespace ValveResourceFormat.Renderer.SceneEnvironment;

/// <summary>
/// Scene node representing distance and height-based gradient fog.
/// </summary>
public class SceneGradientFog(Scene scene) : SceneNode(scene)
{
    /// <summary>Gets or sets the distance at which fog begins.</summary>
    public float StartDist { get; set; }

    /// <summary>Gets or sets the distance at which fog reaches full density.</summary>
    public float EndDist { get; set; }

    /// <summary>Gets or sets the exponent controlling the horizontal fog density falloff.</summary>
    public float FalloffExponent { get; set; }

    /// <summary>Gets or sets the world-space height at which height fog is at full density.</summary>
    public float HeightStart { get; set; }

    /// <summary>Gets or sets the world-space height at which height fog fades to zero.</summary>
    public float HeightEnd { get; set; }

    /// <summary>Gets or sets the exponent controlling the vertical fog density falloff.</summary>
    public float VerticalExponent { get; set; }

    /// <summary>Gets or sets the base fog color.</summary>
    public Vector3 Color { get; set; }

    /// <summary>Gets or sets the strength multiplier applied to the fog color.</summary>
    public float Strength { get; set; }

    /// <summary>Gets or sets the maximum opacity the fog can reach.</summary>
    public float MaxOpacity { get; set; }

    /// <summary>
    /// Returns a <see cref="Vector4"/> encoding the distance bias, height bias, distance scale, and height scale for shader use.
    /// </summary>
    /// <param name="fogSpace">Converts authored distances and heights into the view's space.</param>
    public Vector4 GetBiasAndScale(FogSpace fogSpace)
    {
        var startDist = fogSpace.Distance(StartDist);
        var endDist = fogSpace.Distance(EndDist);

        var distScale = 1f / (endDist - startDist);
        var distBias = -(startDist * distScale);

        var heightStart = fogSpace.Height(HeightStart);
        var heightEnd = fogSpace.Height(HeightEnd);

        // this might be same as cubemap fog height calculations
        // Doesn't seem to be the case for SteamVR, it cuts off too early at the top
        var heightScale = 1f / (heightStart - heightEnd);
        var heightBias = -(heightEnd * heightScale);

        return new Vector4(distBias, heightBias, distScale, heightScale);
    }

    /// <summary>Gets the horizontal and vertical falloff exponents packed as a <see cref="Vector2"/>.</summary>
    public Vector2 Exponents => new(FalloffExponent, VerticalExponent);

    /// <summary>Gets the color scaled by strength and the max opacity packed as a <see cref="Vector4"/>.</summary>
    public Vector4 Color_Opacity => new(Color * Strength, MaxOpacity);

    /// <summary>Gets the squared start distance and height start packed as a <see cref="Vector2"/> for GPU culling.</summary>
    /// <param name="fogSpace">Converts authored distances and heights into the view's space.</param>
    public Vector2 CullingParams(FogSpace fogSpace)
    {
        var startDist = fogSpace.Distance(StartDist);

        return new Vector2(startDist * startDist, fogSpace.Height(HeightStart));
    }
}
