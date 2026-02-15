namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Rendering pass types that define draw call ordering.
    /// </summary>
    public enum RenderPass
    {
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        DepthOnly,
        OpaqueMeshlets,
        Opaque,
        StaticOverlay,
        Water,
        Translucent,
        Outline,
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
    }
}
