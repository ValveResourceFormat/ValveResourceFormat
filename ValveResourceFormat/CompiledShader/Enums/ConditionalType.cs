namespace ValveResourceFormat.CompiledShader;

#pragma warning disable CA1028 // Enum Storage should be Int32
public enum ConditionalType : byte
#pragma warning restore CA1028 // Enum Storage should be Int32
{
    None = 0,
    Feature = 1,
    Static = 2,
    Dynamic = 3
}
