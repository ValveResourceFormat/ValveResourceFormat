namespace ValveResourceFormat.CompiledShader;

[Flags]
public enum VariableFlags : byte
{
    TextureFlag3 = 1 << 3,
    SamplerFlag4 = 1 << 4,
    Bindless = 1 << 7,
}
