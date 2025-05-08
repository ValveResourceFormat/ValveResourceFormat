using System.Runtime.InteropServices;

namespace ValveResourceFormat.CompiledShader;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 4)]
public readonly struct VfxVariableIndexData
{
    public short ParamId { get; init; }
    public short Dest { get; init; }
}
