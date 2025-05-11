namespace ValveResourceFormat.CompiledShader;

// ChannelBlocks are always 280 bytes long
public class VfxTextureChannelProcessor : ShaderDataBlock
{
    public int BlockIndex { get; }

    public ChannelMapping Channel { get; }
    public int[] InputTextureIndices { get; } = new int[4];
    public int ColorMode { get; }
    public string TexProcessorName { get; }

    public VfxTextureChannelProcessor(ShaderDataReader datareader, int blockIndex) : base(datareader)
    {
        // VfxTextureChannelProcessor::Unserialize
        BlockIndex = blockIndex;
        Channel = (ChannelMapping)datareader.ReadUInt32();
        InputTextureIndices[0] = datareader.ReadInt32();
        InputTextureIndices[1] = datareader.ReadInt32();
        InputTextureIndices[2] = datareader.ReadInt32();
        InputTextureIndices[3] = datareader.ReadInt32();
        ColorMode = datareader.ReadInt32();
        TexProcessorName = datareader.ReadNullTermStringAtPosition();
        datareader.BaseStream.Position += 256;
    }
}
