namespace ValveResourceFormat
{
    [Flags]
    public enum VTexFlags
    {
        SUGGEST_CLAMPS = 1 << 0,
        SUGGEST_CLAMPT = 1 << 1,
        SUGGEST_CLAMPU = 1 << 2,
        NO_LOD = 1 << 3,
        CUBE_TEXTURE = 1 << 4,
        VOLUME_TEXTURE = 1 << 5,
        TEXTURE_ARRAY = 1 << 6,
        PANORAMA_DILATE_COLOR = 1 << 7,
        PANORAMA_CONVERT_TO_YCOCG_DXT5 = 1 << 8,
        CREATE_LINEAR_API_TEXTURE = 1 << 9,
    }
}
