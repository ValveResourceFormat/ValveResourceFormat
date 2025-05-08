using System.Runtime.InteropServices;

namespace ValveResourceFormat.CompiledShader;

public readonly struct VfxVariableIndexData
{
    public short ParamId { get; init; }
    public short Dest { get; init; }
}
