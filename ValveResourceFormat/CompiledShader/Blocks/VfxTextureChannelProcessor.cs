using System.IO;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Processes texture channels for shader inputs.
/// </summary>
/// <remarks>
/// ChannelBlocks are always 280 bytes long
/// </remarks>
public class VfxTextureChannelProcessor : ShaderDataBlock
{
    /// <summary>Gets the block index.</summary>
    public int BlockIndex { get; }
    /// <summary>Gets the channel mapping.</summary>
    public ChannelMapping Channel { get; }
    /// <summary>Gets the input texture indices.</summary>
    public int[] InputTextureIndices { get; } = new int[4];
    /// <summary>Gets the color space mode.</summary>
    public int ColorMode { get; }
    /// <summary>Gets the texture processor command name.</summary>
    public string TexProcessorName { get; }

    /// <summary>
    /// Initializes a new instance from <see cref="KVObject"/> data.
    /// </summary>
    public VfxTextureChannelProcessor(KVObject data, int blockIndex) : base()
    {
        BlockIndex = blockIndex;

        var channelDesc = data.GetArray<byte>("m_nChannelDesc");
        Channel = ChannelMapping.FromChannels(channelDesc[0], channelDesc[1], channelDesc[2], channelDesc[3]);
        InputTextureIndices = data.GetArray<int>("m_nInputTextures");
        ColorMode = data.GetInt32Property("m_outputColorSpace");
        TexProcessorName = data.GetProperty<string>("m_mipProcessingCommand");
    }

    /// <summary>
    /// Initializes a new instance from a binary reader.
    /// </summary>
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
