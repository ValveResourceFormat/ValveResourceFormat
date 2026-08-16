namespace ValveResourceFormat.Renderer;

/// <summary>
/// How a draw enters the shadow maps and the depth pre-pass. Declaration order is render order.
/// </summary>
public enum DepthOnlyBucket
{
    /// <summary>Drawn with the shared depth-only shader.</summary>
    Specialized,
    /// <summary>Drawn with the depth-only shader's alpha test variant, which samples the material's color texture and discards below its alpha reference.</summary>
    AlphaTest,
    /// <summary>Drawn with the material's own shader.</summary>
    MaterialShader,
}
