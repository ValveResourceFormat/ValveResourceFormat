using System.Runtime.InteropServices;

namespace ValveResourceFormat.CompiledShader;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 4)]
public readonly struct VfxVariableIndexData
{
    public byte paramId { get; init; }
    public WriteSeqFieldFlags UnknFlags { get; init; }
    public byte Dest { get; init; }
    public byte Control { get; }

    public int ParamId => UnknFlags.HasFlag(WriteSeqFieldFlags.ExtraParam) ? paramId | 0x100 : paramId;
}
