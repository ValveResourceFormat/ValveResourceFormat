using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class CompressionSettings__QuantizationRange
{
    public float RangeStart { get; }
    public float RangeLength { get; }

    public CompressionSettings__QuantizationRange(KVObject data)
    {
        RangeStart = data.GetFloatProperty("m_flRangeStart");
        RangeLength = data.GetFloatProperty("m_flRangeLength");
    }
}
