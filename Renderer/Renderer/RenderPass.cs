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
        /// <summary>Water surface pass.</summary>
        Water,
        /// <summary>Translucent (alpha-blended) pass.</summary>
        Translucent,
        /// <summary>Selection outline pass.</summary>
        Outline,
    }
}
