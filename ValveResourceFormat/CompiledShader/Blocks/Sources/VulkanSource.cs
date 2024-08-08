namespace ValveResourceFormat.CompiledShader;

public class VulkanSource : GpuSource
{
    public override string BlockName => "VULKAN";
    public int Arg0 { get; } = -1;
    public int MetaDataSize { get; } = -1;
    public Span<byte> Bytecode => Sourcebytes.AsSpan()[0..MetaDataSize];
    public Span<byte> Metadata => Sourcebytes.AsSpan()[MetaDataSize..];

    public VulkanSource(ShaderDataReader datareader, int sourceId) : base(datareader, sourceId)
    {
        if (Size > 0)
        {
            Arg0 = datareader.ReadInt32();
            MetaDataSize = datareader.ReadInt32();
            Sourcebytes = datareader.ReadBytes(Size - 8);
        }

        HashMD5 = new Guid(datareader.ReadBytes(16));
    }
}
