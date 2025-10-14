namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Flags for shader variables.
/// </summary>
[Flags]
public enum VariableFlags : byte
{
#pragma warning disable CS1591
    TextureFlag3 = 1 << 3,
    SamplerFlag4 = 1 << 4,
    Bindless = 1 << 7,
#pragma warning restore CS1591
}
