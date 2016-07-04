namespace ValveResourceFormat
{
    public enum VTexFormat
    {
#pragma warning disable 1591
        UNKNOWN = 0,
        DXT1 = 1,
        DXT5 = 2,
        I8 = 3, // TODO: Not used in dota
        RGBA8888 = 4,
        R16 = 5, // TODO: Not used in dota
        RG1616 = 6, // TODO: Not used in dota
        RGBA16161616 = 7, // TODO: Not used in dota
        R16F = 8, // TODO: Not used in dota
        RG1616F = 9, // TODO: Not used in dota
        RGBA16161616F = 10, // TODO: Not used in dota
        R32F = 11, // TODO: Not used in dota
        RG3232F = 12, // TODO: Not used in dota
        RGB323232F = 13, // TODO: Not used in dota
        RGBA32323232F = 14, // TODO: Not used in dota
        PNG = 16 // TODO: resourceinfo doesn't know about this
#pragma warning restore 1591
    }
}
