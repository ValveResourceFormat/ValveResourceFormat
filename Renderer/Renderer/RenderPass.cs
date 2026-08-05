namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Rendering pass types that define draw call ordering.
    /// </summary>
    public enum RenderPass
    {
        /// <summary>Depth pre-pass or shadow map pass.</summary>
        DepthOnly,
        /// <summary>Opaque pass for GPU-driven aggregate scene nodes.</summary>
        OpaqueAggregate,
        /// <summary>Opaque pass for individual draw calls.</summary>
        OpaqueFragments,
        /// <summary>Standard opaque pass.</summary>
        Opaque,
        /// <summary>Static overlay decal pass.</summary>
        StaticOverlay,
        /// <summary>Geometry that reads the scene color.</summary>
        OpaqueRefract,
        /// <summary>Water surface pass.</summary>
        Water,
        /// <summary>Translucent (alpha-blended) pass.</summary>
        Translucent,
        /// <summary>Selection outline pass.</summary>
        Outline,
    }

    /// <summary>
    /// Per node flags about their desired render passes.
    /// </summary>
    [Flags]
    public enum CustomRenderPasses
    {
        /// <summary>Draws in no pass at all.</summary>
        None = 0,

        /// <summary>
        /// Draws in <see cref="RenderPass.Opaque"/>, and with it <see cref="RenderPass.DepthOnly"/>: solid
        /// geometry is a shadow caster.
        /// </summary>
        Opaque = 1 << 0,

        /// <summary>Draws in <see cref="RenderPass.Translucent"/>.</summary>
        Translucent = 1 << 1,

        /// <summary>
        /// Routes the node's drawing into the dedicated first-person viewmodel layer.
        /// </summary>
        Viewmodel = 1 << 2,

        /// <summary>Draws in the opaque and translucent passes, the default for a node that draws itself.</summary>
        Default = Opaque | Translucent,
    }
}
