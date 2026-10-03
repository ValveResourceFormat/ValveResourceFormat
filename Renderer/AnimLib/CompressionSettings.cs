using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class CompressionSettings
{
    public CompressionSettings.QuantizationRange TranslationRangeX { get; }
    public CompressionSettings.QuantizationRange TranslationRangeY { get; }
    public CompressionSettings.QuantizationRange TranslationRangeZ { get; }
    public CompressionSettings.QuantizationRange ScaleRange { get; }
    public int TrackReadOffset { get; }
    public Quaternion ConstantRotation { get; }
    public bool IsRotationStatic { get; }
    public bool IsTranslationStatic { get; }
    public bool IsScaleStatic { get; }
}
