using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer;

record struct Range(float Min, float Max)
{
    public Range(KVObject data)
        : this(data.GetFloatProperty("m_flMin"), data.GetFloatProperty("m_flMax"))
    {
    }

    public Range(float singleValue)
        : this(singleValue, singleValue)
    {
    }

    public readonly float Length => Max - Min;

    public readonly float GetClampedValue(float input)
    {
        return Math.Clamp(input, Min, Max);
    }
}
