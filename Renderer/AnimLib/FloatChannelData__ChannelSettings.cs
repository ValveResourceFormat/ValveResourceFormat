using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FloatChannelData__ChannelSettings
{
    public CompressionSettings__QuantizationRange Range { get; }
    public bool IsStatic { get; }
}
