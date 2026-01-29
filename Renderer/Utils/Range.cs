using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer;

record struct Range(float Min, float Max)
{
    public Range(KVObject data)
        : this(data.GetFloatProperty("m_flMin"), data.GetFloatProperty("m_flMax"))
    {
    }
}
