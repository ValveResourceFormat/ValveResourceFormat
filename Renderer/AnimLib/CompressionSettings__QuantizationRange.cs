using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class CompressionSettings__QuantizationRange
{
    public float RangeStart { get; }
    public float RangeLength { get; } = -1f;
}
