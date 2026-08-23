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
    /// <summary>
    /// Drawn with the material's own shader in its depth mode, for geometry the shared depth-only shader
    /// cannot place: vertex animation lives in the material's vertex shader. Resolved per draw, because
    /// the mode belongs to the material. Materials whose shader has no depth mode fall back to the shared
    /// depth-only shader in the shadow passes rather than run a forward pixel shader there.
    /// </summary>
    MaterialDepthMode,
}
