using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class FloatRemapNode__RemapRange
{
    public float Begin { get; }
    public float End { get; }

    public FloatRemapNode__RemapRange(KVObject data)
    {
        Begin = data.GetFloatProperty("m_flBegin");
        End = data.GetFloatProperty("m_flEnd");
    }
}
