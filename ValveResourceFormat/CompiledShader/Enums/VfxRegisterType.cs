namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Shader register types.
/// </summary>
public enum VfxRegisterType
{
#pragma warning disable CS1591
    Bool = 1,
    Int,
    Uniform,
    Texture,
    RenderState,
    SamplerState,
    InputTexture,
    Buffer,
    Unkn9,
    Unkn10,
    CBuffer = 11,
#pragma warning restore CS1591
}
