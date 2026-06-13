namespace ValveResourceFormat
{
    /// <summary>
    /// Flags describing the type and rendering behavior of a world scene object.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/worldrenderer/ObjectTypeFlags_t">ObjectTypeFlags_t</seealso>
    [Flags]
    public enum ObjectTypeFlags
    {
        /// <summary>No flags set.</summary>
        None = 0x0,

        /// <summary>Object uses image-based LOD.</summary>
        ImageLod = 0x1,

        /// <summary>Object uses geometry-based LOD.</summary>
        GeometryLod = 0x2,

        /// <summary>Object is a decal.</summary>
        Decal = 0x4,

        /// <summary>Object is a model.</summary>
        Model = 0x8,

        /// <summary>Object blocks light.</summary>
        BlockLight = 0x10,

        /// <summary>Object casts no dynamic shadows.</summary>
        NoShadows = 0x20,

        /// <summary>Object uses worldspace texture blending. (Typo preserved from original engine enum: "Texure".)</summary>
        WorldspaceTexureBlend = 0x40, // do not fix typo, it's in the original enum

        /// <summary>Object is disabled in low-quality render settings.</summary>
        DisabledInLowQuality = 0x80,

        /// <summary>Object casts no sun shadows.</summary>
        NoSunShadows = 0x100,

        /// <summary>Object is rendered together with dynamic objects.</summary>
        RenderWithDynamic = 0x200,

        /// <summary>Object is rendered into cubemap captures.</summary>
        RenderToCubemaps = 0x400,

        /// <summary>Object's model contains LOD sub-meshes.</summary>
        ModelHasLods = 0x800,

        /// <summary>Object is an overlay rendered on top of other geometry.</summary>
        Overlay = 0x2000,

        /// <summary>Object has precomputed visibility cluster membership.</summary>
        PrecomputedVismembers = 0x4000,

        /// <summary>Object uses a static cube map for reflections.</summary>
        StaticCubeMap = 0x8000,

        /// <summary>Object is exempt from visibility culling.</summary>
        DisableVisCulling = 0x10000,

        /// <summary>Object is statically baked geometry.</summary>
        BakedGeometry = 0x20000,

        /// <summary>Object requires dynamic shadows.</summary>
        NeedsDynamicShadows = 0x40000,

        /// <summary>Object has an aggregate ray-tracing proxy.</summary>
        HasAggregateRtproxy = 0x80000,
    }
}
