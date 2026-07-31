namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Specifies sampler filtering.
/// </summary>
public enum RsFilter
{
#pragma warning disable CS1591
    MinMagMipPoint = 0x00,
    MinMagPointMipLinear = 0x01,
    MinPointMagLinearMipPoint = 0x04,
    MinPointMagMipLinear = 0x05,
    MinLinearMagMipPoint = 0x10,
    MinLinearMagPointMipLinear = 0x11,
    MinMagLinearMipPoint = 0x14,
    MinMagMipLinear = 0x15,
    Anisotropic = 0x55,
    ComparisonMinMagMipPoint = 0x80,
    ComparisonMinMagPointMipLinear = 0x81,
    ComparisonMinPointMagLinearMipPoint = 0x84,
    ComparisonMinPointMagMipLinear = 0x85,
    ComparisonMinLinearMagMipPoint = 0x90,
    ComparisonMinLinearMagPointMipLinear = 0x91,
    ComparisonMinMagLinearMipPoint = 0x94,
    ComparisonMinMagMipLinear = 0x95,
    ComparisonAnisotropic = 0xD5,
    UserConfig = 0xFF,
#pragma warning restore CS1591
}
