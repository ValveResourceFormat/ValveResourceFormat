using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Animation;

/// <summary>A range of floats between two bounds.</summary>
/// <param name="Min">The lower bound.</param>
/// <param name="Max">The upper bound.</param>
public record struct Range(float Min, float Max)
{
    /// <summary>Reads a range from its compiled data.</summary>
    public Range(KVObject data)
        : this(data.GetFloatProperty("m_flMin"), data.GetFloatProperty("m_flMax"))
    {
    }

    /// <summary>Creates an empty range at a single value.</summary>
    public Range(float singleValue)
        : this(singleValue, singleValue)
    {
    }

    /// <summary>Gets the distance between the bounds.</summary>
    public readonly float Length => Max - Min;

    /// <summary>Gets whether the range spans any distance.</summary>
    public readonly bool IsSet => Min < Max;

    /// <summary>Clamps a value into the range.</summary>
    public readonly float GetClampedValue(float input)
    {
        // Math.Clamp throws on an inverted range, which authored data can contain
        return MathF.Min(MathF.Max(input, Min), Max);
    }

    /// <summary>Gets how far through the range a value is, from 0 to 1, after clamping it into the range.</summary>
    public readonly float GetPercentageThroughClamped(float input)
    {
        return (GetClampedValue(input) - Min) / Length;
    }

    /// <summary>Gets whether a value lies within the range, bounds included.</summary>
    public readonly bool ContainsInclusive(float value)
    {
        return value >= Min && value <= Max;
    }

    /// <summary>Gets how far through the range a value is, which is outside 0 to 1 for a value outside the range.</summary>
    public readonly float GetPercentageThrough(float input)
    {
        return Length == 0f ? 0f : (input - Min) / Length;
    }
}
