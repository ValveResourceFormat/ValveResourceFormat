namespace ValveResourceFormat.Renderer;

/// <summary>
/// Shader program variants for depth-only rendering in shadow maps and depth pre-pass.
/// </summary>
public enum DepthOnlyProgram
{
    /// <summary>Static (non-animated) geometry.</summary>
    Static,
    //StaticAlphaTest,
    /// <summary>Skinned geometry with up to 4 bone weights.</summary>
    Animated,
    /// <summary>Skinned geometry with up to 8 bone weights.</summary>
    AnimatedEightBones,
    /// <summary>AABB proxy used for hardware occlusion queries.</summary>
    OcclusionQueryAABBProxy,
    /// <summary>Unspecified program type.</summary>
    Unspecified,
}
