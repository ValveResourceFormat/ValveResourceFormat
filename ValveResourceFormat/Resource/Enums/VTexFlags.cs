namespace ValveResourceFormat
{
    [Flags]
    public enum VTexFlags
    {
        SUGGEST_CLAMPS = 0x00000001,
        SUGGEST_CLAMPT = 0x00000002,
        SUGGEST_CLAMPU = 0x00000004,
        NO_LOD = 0x00000008,
        CUBE_TEXTURE = 0x00000010,
        VOLUME_TEXTURE = 0x00000020,
        TEXTURE_ARRAY = 0x00000040,
    }
}
