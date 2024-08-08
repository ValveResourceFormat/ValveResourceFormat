using System.Runtime.InteropServices;

namespace ValveResourceFormat.CompiledShader;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 4)]
public readonly struct WriteSeqField
{
    private readonly byte paramId;
    public WriteSeqFieldFlags UnknFlags { get; }
    public byte Dest { get; }
    public byte Control { get; }

    public int ParamId => UnknFlags.HasFlag(WriteSeqFieldFlags.ExtraParam) ? paramId | 0x100 : paramId;
}
