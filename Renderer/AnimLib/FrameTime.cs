namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A time within a frame based range: a frame index plus the percentage through that frame.</summary>
readonly struct FrameTime
{
    public int FrameIndex { get; }
    public float PercentageThrough { get; }

    public FrameTime(int frameIndex, float percentageThrough = 0f)
    {
        FrameIndex = frameIndex;
        PercentageThrough = percentageThrough;
    }

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

    public const float LargeEpsilon = 1.0e-04f;

    public bool IsExactlyAtKeyFrame => PercentageThrough == 0f;

    /// <summary>The nearest frame index to the current time (a round).</summary>
    public int NearestFrameIndex => FrameIndex + (int)MathF.Round(PercentageThrough);

    public int LowerBoundFrameIndex => FrameIndex;

    public int UpperBoundFrameIndex => PercentageThrough > 0f ? FrameIndex + 1 : FrameIndex;

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
