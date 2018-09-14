using System;

namespace ValveResourceFormat
{
    public enum VTexFormat
    {
#pragma warning disable 1591
        UNKNOWN = 0,
        DXT1 = 1,
        DXT5 = 2,
        I8 = 3,
        RGBA8888 = 4,
        R16 = 5,
        RG1616 = 6,
        RGBA16161616 = 7,
        R16F = 8,
        RG1616F = 9,
        RGBA16161616F = 10,
        R32F = 11,
        RG3232F = 12,
        RGB323232F = 13,
        RGBA32323232F = 14,
        IA88 = 15,
        PNG = 16, // TODO: resourceinfo doesn't know about this
        JPG = 17,
        PNG2 = 18, // TODO: Why is there PNG twice?
#pragma warning restore 1591
    }
}
