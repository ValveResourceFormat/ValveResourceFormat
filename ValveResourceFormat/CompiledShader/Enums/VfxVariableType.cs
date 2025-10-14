namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Shader variable data types.
/// </summary>
/// <remarks>
/// When updating this enum, make sure to update <see cref="ShaderUtilHelpers.VfxVariableTypeToString"/>
/// </remarks>
public enum VfxVariableType
{
#pragma warning disable CS1591
    Void = 0,
    Float = 1,
    Float2 = 2,
    Float3 = 3,
    Float4 = 4,
    Int = 5,
    Int2 = 6,
    Int3 = 7,
    Int4 = 8,
    Bool = 9,
    Bool2 = 10,
    Bool3 = 11,
    Bool4 = 12,
    Sampler1D = 13,
    Sampler2D = 14,
    Sampler3D = 15,
    SamplerCube = 16,
    Float3x3 = 17,
    Float4x3 = 18,
    Float4x4 = 19,
    Struct = 20,
    Cbuffer = 21,
    SamplerCubeArray = 22,
    Sampler2DArray = 23,
    Buffer = 24,
    Sampler1DArray = 25,
    Sampler3DArray = 26,
    StructuredBuffer = 27,
    ByteAddressBuffer = 28,
    RWBuffer = 29,
    RWTexture1D = 30,
    RWTexture1DArray = 31,
    RWTexture2D = 32,
    RWTexture2DArray = 33,
    RWTexture3D = 34,
    RWStructuredBuffer = 35,
    RWByteAddressBuffer = 36,
    AppendStructuredBuffer = 37,
    ConsumeStructuredBuffer = 38,
    RWStructuredBufferWithCounter = 39,
    ExternalDescriptorSet = 40,
    String = 41,
    SamplerStateIndex = 42,
    Texture2DIndex = 43,
    Texture3DIndex = 44,
    TextureCubeIndex = 45,
    Texture2DArrayIndex = 46,
    TextureCubeArrayIndex = 47,
#pragma warning restore CS1591
}
