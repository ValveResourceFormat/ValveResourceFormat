namespace ValveResourceFormat.CompiledShader;

public readonly struct VfxVariableIndexData
{
    public short Field1 { get; init; }
    public short Dest { get; init; }


    private bool IsExtendedParameter => ((Field1 >> 8) & 1) != 0;
    public int ParamId => Field1 & 0xFF | (IsExtendedParameter ? 0x100 : 0x0);
}
