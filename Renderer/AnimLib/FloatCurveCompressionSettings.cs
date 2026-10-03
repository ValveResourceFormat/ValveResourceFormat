using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FloatCurveCompressionSettings
{
    public CompressionSettings.QuantizationRange Range { get; }
    public bool IsStatic { get; }
}
