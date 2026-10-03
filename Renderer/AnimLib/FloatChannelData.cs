using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class FloatChannelData
{
    public string Skeleton { get; } // InfoForResourceTypeCNmSkeleton
    public GlobalSymbol SetID { get; }
    public FloatChannelData__ChannelSettings[] ChannelSettings { get; }
    public ushort[] CompressedData { get; }
    public uint[] CompressedOffsets { get; }

    public FloatChannelData(KVObject data)
    {
        Skeleton = data.GetProperty<string>("m_skeleton");
        SetID = data.GetProperty<string>("m_setID");
        ChannelSettings = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_channelSettings") ?? [], kv => new FloatChannelData__ChannelSettings(kv))];
        CompressedData = data.GetArray<ushort>("m_compressedData") ?? [];
        CompressedOffsets = data.GetArray<uint>("m_compressedOffsets") ?? [];
    }
}
