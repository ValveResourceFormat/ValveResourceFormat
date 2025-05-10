namespace ValveResourceFormat.CompiledShader;

public class VfxShaderFileVulkan : VfxShaderFile
{
    public override string BlockName => "VULKAN";
    public int Version { get; } = -1;
    public int BytecodeSize { get; } = -1;
    public Span<byte> Bytecode => Sourcebytes.AsSpan(0, BytecodeSize);
    public Span<byte> Metadata => Sourcebytes.AsSpan(BytecodeSize..);

    public VfxShaderFileVulkan(ShaderDataReader datareader, int sourceId) : base(datareader, sourceId)
    {
        if (Size > 0)
        {
            Version = datareader.ReadInt32();
            BytecodeSize = datareader.ReadInt32();
            Sourcebytes = datareader.ReadBytes(Size - 8);
        }

        HashMD5 = new Guid(datareader.ReadBytes(16));
    }
}
