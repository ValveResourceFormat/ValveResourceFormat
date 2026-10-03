using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class FloatChannelData
{
    [KV3Transfer]
    internal partial class ChannelSettingsType
    {
        public CompressionSettings.QuantizationRange Range { get; }
        public bool IsStatic { get; }
    }
}
