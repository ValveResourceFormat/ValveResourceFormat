using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class FloatCurveCompressionSettings
{
    public CompressionSettings__QuantizationRange Range { get; }
    public bool IsStatic { get; }

    public FloatCurveCompressionSettings(KVObject data)
    {
        Range = new(data.GetProperty<KVObject>("m_range"));
        IsStatic = data.GetProperty<bool>("m_bIsStatic");
    }
}
