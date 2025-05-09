namespace ValveResourceFormat.CompiledShader;

public readonly struct VfxVariableIndexData
{
    public short Field1 { get; init; }
    public short Field2 { get; init; }

    private bool IsExtendedParameter => ((Field1 >> 8) & 1) != 0;
    public int ParamId => Field1 & 0xFF | (IsExtendedParameter ? 0x100 : 0x0);

    public int Dest => Field2 & 0xFF;
    public int Control => (Field2 >> 8) & 0xFF;
}
