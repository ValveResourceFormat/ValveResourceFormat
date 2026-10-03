using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FloatChannelData
{
    public string Skeleton { get; } // InfoForResourceTypeCNmSkeleton
    public GlobalSymbol SetID { get; }
    public FloatChannelData.ChannelSettingsType[] ChannelSettings { get; } = [];
    public ushort[] CompressedData { get; } = [];
    public uint[] CompressedOffsets { get; } = [];
}
