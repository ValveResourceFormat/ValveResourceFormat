namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Shader register types.
/// </summary>
public enum VfxRegisterType
{
#pragma warning disable CS1591
    Bool = 1,
    Int4,
    Float4,
    Texture,
    RenderState,
    SamplerState,
    InputTexture,
    ConstantBuffer,
    Uav,
    DescriptorSet,
    PushConstantBuffer,
    TextureIndex,
    SamplerIndex,
#pragma warning restore CS1591
}
