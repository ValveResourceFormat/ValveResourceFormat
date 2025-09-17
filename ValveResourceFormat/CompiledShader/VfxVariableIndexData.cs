namespace ValveResourceFormat.CompiledShader;

public readonly struct VfxVariableIndexData
{
    public short Field2 { get; init; }
    public short Field1 { get; init; }

    public int VariableIndex => Field1 & 0xFFF; // index VariableDescriptions
    public int LayoutSet => Field1 >> 12; // Descriptor set id in the shader layout()

    public int Dest => Field2 & 0xFF;
    public int Control => (Field2 >> 8) & 0xFF;
}
