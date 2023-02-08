namespace ValveResourceFormat.CompiledShader;

#pragma warning disable CA1028 // If possible, make the underlying type System.Int32 instead of uint
public enum Channel : uint
{
    R = 0xFFFFFF00,
    G = 0xFFFFFF01,
    B = 0xFFFFFF02,
    A = 0xFFFFFF03,
    RG = 0xFFFF0100,
    GB = 0xFFFF0201,
    BA = 0xFFFF0302,
    //AR = 0xFFFF0003,
    AG = 0xFFFF0103,
    //AB = 0xFFFF0203,
    RGB = 0xFF020100,
    GBA = 0xFF030201,
    RGBA = 0x03020100,
}
#pragma warning restore CA1028
