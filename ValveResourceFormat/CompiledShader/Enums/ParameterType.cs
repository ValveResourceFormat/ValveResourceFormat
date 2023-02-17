namespace ValveResourceFormat.CompiledShader;

/// <remarks>
/// These are just guesses.
/// </remarks>
public enum ParameterType
{
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
}
