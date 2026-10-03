using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class CompressionSettings
{
    [KV3Transfer]
    internal partial class QuantizationRange
    {
        public float RangeStart { get; }
        public float RangeLength { get; } = -1f;
    }
}
