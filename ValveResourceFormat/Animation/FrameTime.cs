namespace ValveResourceFormat.Animation;

/// <summary>A time within a frame based range: a frame index plus the percentage through that frame.</summary>
public readonly struct FrameTime
{
    /// <summary>Gets the index of the frame.</summary>
    public int FrameIndex { get; }
    /// <summary>Gets how far past the frame the time is, from 0 to 1.</summary>
    public float PercentageThrough { get; }

    /// <summary>Creates a frame time from a frame index and the percentage through it.</summary>
    public FrameTime(int frameIndex, float percentageThrough = 0f)
    {
        FrameIndex = frameIndex;
        PercentageThrough = percentageThrough;
    }

    /// <summary>Creates a frame time from the percentage through a range of <paramref name="numFrames"/> frames.</summary>
    public FrameTime(float percent, int numFrames)
    {
        var lastFrameIdx = numFrames - 1;

        if (percent == 0f)
        {
            FrameIndex = 0;
            PercentageThrough = 0f;
        }
        else if (percent == 1f)
        {
            FrameIndex = lastFrameIdx;
            PercentageThrough = 0f;
        }
        else
        {
            percent = ClampPercentage(percent, allowLooping: true);
            var frameValue = percent * lastFrameIdx;
            var integerPortion = MathF.Truncate(frameValue);
            var percentageThrough = frameValue - integerPortion;

            if (MathF.Abs(percentageThrough) <= LargeEpsilon)
            {
                percentageThrough = 0f;
            }

            FrameIndex = (int)integerPortion;
            PercentageThrough = percentageThrough;
        }
    }

    /// <summary>The percentage through a frame below which a time counts as being on the frame.</summary>
    public const float LargeEpsilon = 1.0e-04f;

    /// <summary>Gets whether the time is on a frame, with nothing to interpolate.</summary>
    public bool IsExactlyAtKeyFrame => PercentageThrough == 0f;

    /// <summary>The nearest frame index to the current time (a round).</summary>
    public int NearestFrameIndex => FrameIndex + (int)MathF.Round(PercentageThrough);

    /// <summary>Gets the frame at or before the time.</summary>
    public int LowerBoundFrameIndex => FrameIndex;

    /// <summary>Gets the frame at or after the time.</summary>
    public int UpperBoundFrameIndex => PercentageThrough > 0f ? FrameIndex + 1 : FrameIndex;

    /// <summary>Gets the time as a fractional frame number.</summary>
    public float ToFloat() => PercentageThrough + FrameIndex;

    /// <summary>Wraps a percentage into [0, 1) when looping, otherwise clamps it into [0, 1].</summary>
    public static float ClampPercentage(float value, bool allowLooping)
    {
        if (allowLooping)
        {
            var clamped = value - MathF.Truncate(value);
            return clamped < 0f ? 1f - clamped : clamped;
        }

        return Math.Clamp(value, 0f, 1f);
    }
}
