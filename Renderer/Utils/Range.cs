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

    public readonly bool IsSet => Min < Max;

    public readonly float GetClampedValue(float input)
    {
        return Math.Clamp(input, Min, Max);
    }

    public readonly float GetPercentageThroughClamped(float input)
    {
        return (GetClampedValue(input) - Min) / Length;
    }

    public readonly bool ContainsInclusive(float value)
    {
        return value >= Min && value <= Max;
    }

    public readonly float GetPercentageThrough(float input)
    {
        return Length == 0f ? 0f : (input - Min) / Length;
    }
}
