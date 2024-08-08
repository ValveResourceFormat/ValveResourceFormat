namespace ValveResourceFormat.CompiledShader;

[Flags]
#pragma warning disable CA1028 // Enum storage should be Int32
public enum WriteSeqFieldFlags : byte
#pragma warning restore CA1028 // Enum storage should be Int32
{
    None = 0,
    ExtraParam = 0x1,
}
