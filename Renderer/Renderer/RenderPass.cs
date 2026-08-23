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
        /// <summary>Translucent (alpha-blended) pass, sorted back to front on the scene.</summary>
        Translucent,
        /// <summary>
        /// Translucent draws accumulated into the order independent transparency targets in any order.
        /// Filled only while <see cref="Scene.OrderIndependentTransparency"/> is on.
        /// </summary>
        TranslucentOrderIndependent,
        /// <summary>Selection outline pass.</summary>
        Outline,
    }

    /// <summary>
    /// Which target a pass draws into, for the layers that share one.
    /// </summary>
    public enum RenderLayer
    {
        /// <summary>The scene itself.</summary>
        Scene,

        /// <summary>The water effects map the fancy water shader samples.</summary>
        WaterEffects,

        /// <summary>Effects rendered in the bloom buffer.</summary>
        Bloom,
    }

    /// <summary>
    /// Per node flags about their desired render passes.
    /// </summary>
    [Flags]
    public enum CustomRenderPasses
    {
        /// <summary>Draws in no pass at all.</summary>
        None = 0,

        /// <summary>Draws in <see cref="RenderPass.Opaque"/>.</summary>
        Opaque = 1 << 0,

        /// <summary>Draws in <see cref="RenderPass.Translucent"/>.</summary>
        Translucent = 1 << 1,

        /// <summary>
        /// Routes the node's drawing into the dedicated first-person viewmodel layer.
        /// </summary>
        Viewmodel = 1 << 2,

        /// <summary>Draws in the translucent pass, into the water effects map instead of the scene.</summary>
        WaterEffects = 1 << 3,

        /// <summary>Draws in <see cref="RenderPass.DepthOnly"/> too, and so casts a shadow.</summary>
        DepthOnly = 1 << 4,

        /// <summary>
        /// The node's translucent draws can go into the order independent transparency targets, so it is
        /// drawn in <see cref="RenderPass.TranslucentOrderIndependent"/> instead of <see cref="RenderPass.Translucent"/>
        /// when that is on.
        /// </summary>
        OrderIndependentTranslucent = 1 << 5,

        /// <summary>Draws in the opaque and translucent passes, the default for a node that draws itself.</summary>
        Default = Opaque | Translucent,
    }
}
