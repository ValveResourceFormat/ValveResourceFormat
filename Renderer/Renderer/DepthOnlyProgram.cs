namespace ValveResourceFormat.Renderer;

/// <summary>
/// Shader program variants for depth-only rendering in shadow maps and depth pre-pass.
/// </summary>
public enum DepthOnlyProgram
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    Static,
    //StaticAlphaTest,
    Animated,
    AnimatedEightBones,
    OcclusionQueryAABBProxy,
    Unspecified,
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
