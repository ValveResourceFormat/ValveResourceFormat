using System.IO;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.CompiledShader;

// ChannelBlocks are always 280 bytes long
public class VfxTextureChannelProcessor : ShaderDataBlock
{
    public int BlockIndex { get; }
    public ChannelMapping Channel { get; }
    public int[] InputTextureIndices { get; } = new int[4];
    public int ColorMode { get; }
    public string TexProcessorName { get; }

    public VfxTextureChannelProcessor(KVObject data, int blockIndex) : base()
    {
        BlockIndex = blockIndex;

        var channelDesc = data.GetArray<byte>("m_nChannelDesc");
        Channel = ChannelMapping.FromChannels(channelDesc[0], channelDesc[1], channelDesc[2], channelDesc[3]);
        InputTextureIndices = data.GetArray<int>("m_nInputTextures");
        ColorMode = data.GetInt32Property("m_outputColorSpace");
        TexProcessorName = data.GetProperty<string>("m_mipProcessingCommand");
    }

    public VfxTextureChannelProcessor(BinaryReader datareader, int blockIndex) : base(datareader)
    {
        // VfxTextureChannelProcessor::Unserialize
        BlockIndex = blockIndex;
        Channel = (ChannelMapping)datareader.ReadUInt32();
        InputTextureIndices[0] = datareader.ReadInt32();
        InputTextureIndices[1] = datareader.ReadInt32();
        InputTextureIndices[2] = datareader.ReadInt32();
        InputTextureIndices[3] = datareader.ReadInt32();
        ColorMode = datareader.ReadInt32();
        TexProcessorName = ReadStringWithMaxLength(datareader, 256);
    }
}
