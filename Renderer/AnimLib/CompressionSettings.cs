using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class CompressionSettings
{
    public CompressionSettings__QuantizationRange TranslationRangeX { get; }
    public CompressionSettings__QuantizationRange TranslationRangeY { get; }
    public CompressionSettings__QuantizationRange TranslationRangeZ { get; }
    public CompressionSettings__QuantizationRange ScaleRange { get; }
    public Quaternion ConstantRotation { get; }
    public bool IsRotationStatic { get; }
    public bool IsTranslationStatic { get; }
    public bool IsScaleStatic { get; }

    public CompressionSettings(KVObject data)
    {
        TranslationRangeX = new(data.GetProperty<KVObject>("m_translationRangeX"));
        TranslationRangeY = new(data.GetProperty<KVObject>("m_translationRangeY"));
        TranslationRangeZ = new(data.GetProperty<KVObject>("m_translationRangeZ"));
        ScaleRange = new(data.GetProperty<KVObject>("m_scaleRange"));
        ConstantRotation = data.GetSubCollection("m_constantRotation").ToQuaternion();
        IsRotationStatic = data.GetProperty<bool>("m_bIsRotationStatic");
        IsTranslationStatic = data.GetProperty<bool>("m_bIsTranslationStatic");
        IsScaleStatic = data.GetProperty<bool>("m_bIsScaleStatic");
    }
}
