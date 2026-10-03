using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class FloatChannelData__ChannelSettings
{
    public CompressionSettings__QuantizationRange Range { get; }
    public bool IsStatic { get; }

    public FloatChannelData__ChannelSettings(KVObject data)
    {
        Range = new(data.GetProperty<KVObject>("m_range"));
        IsStatic = data.GetProperty<bool>("m_bIsStatic");
    }
}
