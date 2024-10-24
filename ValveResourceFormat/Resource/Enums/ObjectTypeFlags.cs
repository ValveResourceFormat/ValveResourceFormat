namespace ValveResourceFormat
{
    [Flags]
    public enum ObjectTypeFlags
    {
        None = 0x0,
        ImageLod = 0x1,
        GeometryLod = 0x2,
        Decal = 0x4,
        Model = 0x8,
        BlockLight = 0x10,
        NoShadows = 0x20,
        WorldspaceTextureBlend = 0x40,
        DisabledInLowQuality = 0x80,
        NoSunShadows = 0x100,
        RenderWithDynamic = 0x200,
        RenderToCubemaps = 0x400,
        ModelHasLods = 0x800,
        PrecomputedVismembers = 0x4000,
        StaticCubeMap = 0x8000,
        Overlay = 0x10000,
    }
}
