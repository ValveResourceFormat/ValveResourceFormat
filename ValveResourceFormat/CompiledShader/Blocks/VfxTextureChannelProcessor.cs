using System.Buffers.Binary;
using System.IO;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Processes texture channels for shader inputs.
/// </summary>
public class VfxTextureChannelProcessor : ShaderDataBlock
{
    /// <summary>Gets the index in the owning array.</summary>
    public int Index { get; }
    /// <summary>Gets the channel mapping.</summary>
    public ChannelMapping Channel { get; }
    /// <summary>Gets the input texture indices.</summary>
    public int[] InputTextureIndices { get; } = new int[4];
    /// <summary>Gets the output color space.</summary>
    public int OutputColorSpace { get; }
    /// <summary>Gets the mip processing command name.</summary>
    public string MipProcessingCommand { get; }

    /// <summary>
    /// Initializes a new instance from <see cref="KVObject"/> data.
    /// </summary>
    public VfxTextureChannelProcessor(KVObject data, int index) : base()
    {
        Index = index;

        var channelDesc = data.GetArray<byte>("m_nChannelDesc")!;
        Channel = ChannelMapping.FromUInt32(BinaryPrimitives.ReadUInt32LittleEndian(channelDesc), packedDestinations: true);
        InputTextureIndices = data.GetArray<int>("m_nInputTextures")!;
        OutputColorSpace = data.GetInt32Property("m_outputColorSpace");
        MipProcessingCommand = data.GetStringProperty("m_mipProcessingCommand");
    }

    /// <summary>
    /// Initializes a new instance from a binary reader.
    /// </summary>
    public VfxTextureChannelProcessor(BinaryReader datareader, int index, int vcsVersion) : base(datareader)
    {
        // VfxTextureChannelProcessor::Unserialize
        Index = index;
        Channel = ChannelMapping.FromUInt32(datareader.ReadUInt32(), packedDestinations: vcsVersion >= 67);
        InputTextureIndices[0] = datareader.ReadInt32();
        InputTextureIndices[1] = datareader.ReadInt32();
        InputTextureIndices[2] = datareader.ReadInt32();
        InputTextureIndices[3] = datareader.ReadInt32();
        OutputColorSpace = datareader.ReadInt32();
        MipProcessingCommand = ReadStringWithMaxLength(datareader, 256);
    }
}
