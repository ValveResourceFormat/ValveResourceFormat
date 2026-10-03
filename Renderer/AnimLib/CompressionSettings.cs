using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class CompressionSettings
{
    public CompressionSettings__QuantizationRange TranslationRangeX { get; }
    public CompressionSettings__QuantizationRange TranslationRangeY { get; }
    public CompressionSettings__QuantizationRange TranslationRangeZ { get; }
    public CompressionSettings__QuantizationRange ScaleRange { get; }
    public int TrackReadOffset { get; }
    public Quaternion ConstantRotation { get; }
    public bool IsRotationStatic { get; }
    public bool IsTranslationStatic { get; }
    public bool IsScaleStatic { get; }
}
