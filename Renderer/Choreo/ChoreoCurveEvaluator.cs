using ValveResourceFormat.ResourceTypes.Choreo;
using ValveResourceFormat.ResourceTypes.Choreo.Curves;

namespace ValveResourceFormat.Renderer.Choreo
{
    /// <summary>
    /// Evaluates the curves of a choreography scene: event ramps and the samples of flex animation tracks.
    /// </summary>
    public static class ChoreoCurveEvaluator
    {
        private const int EaseInInterpolator = 2;
        private const int EaseOutInterpolator = 3;
        private const int EaseInOutInterpolator = 4;
        private const int LinearInterpolator = 6;
        private const int HoldInterpolator = 15;

        /// <summary>Gets the value of a curve at a time.</summary>
        /// <param name="curve">The curve.</param>
        /// <param name="time">The time in seconds from the start of the event.</param>
        /// <param name="duration">The length of the event in seconds, where the curve ends.</param>
        /// <param name="defaultZero">The value before the first sample and after the last when the curve has no edge to set it.</param>
        /// <returns>The value from 0 to 1, or 1 for a curve without samples.</returns>
        /// <remarks>
        /// Spline interpolators other than catmull-rom, and bezier samples, are drawn as catmull-rom.
        /// </remarks>
        public static float Evaluate(ChoreoCurveData curve, float time, float duration, float defaultZero)
        {
            ArgumentNullException.ThrowIfNull(curve);

            var samples = curve.Samples;
            if (samples.Length < 1)
            {
                return 1f;
            }

            var segment = 0;
            while (segment < samples.Length && time >= samples[segment].Time)
            {
                segment++;
            }

            // Segment 0 runs from the left edge to the first sample, the last from the last sample to the right edge
            var start = GetBoundedPoint(curve, segment - 1, duration, defaultZero, out var startCurve);
            var end = GetBoundedPoint(curve, segment, duration, defaultZero, out var endCurve);
            var pre = GetBoundedPoint(curve, segment - 2, duration, defaultZero, out _);
            var next = GetBoundedPoint(curve, segment + 1, duration, defaultZero, out _);

            var length = end.X - start.X;
            var fraction = 0f;
            if (length > 0f)
            {
                fraction = Math.Clamp((time - start.X) / length, 0f, 1f);
            }

            var earlyInterpolator = startCurve.OutType;
            var laterInterpolator = endCurve.InType;

            float value;
            if (earlyInterpolator == HoldInterpolator)
            {
                value = start.Y;
            }
            else if (laterInterpolator == HoldInterpolator)
            {
                value = end.Y;
            }
            else if (earlyInterpolator == laterInterpolator)
            {
                value = Interpolate(laterInterpolator, pre, start, end, next, fraction);
            }
            else
            {
                var early = Interpolate(earlyInterpolator, pre, start, end, next, fraction);
                var later = Interpolate(laterInterpolator, pre, start, end, next, fraction);
                value = float.Lerp(early, later, fraction);
            }

            return Math.Clamp(value, 0f, 1f);
        }

        // The samples with an extra point before the first and after the last, which are the zero values at the edges
        private static Vector2 GetBoundedPoint(ChoreoCurveData curve, int index, float duration, float defaultZero, out CurveType curveType)
        {
            var samples = curve.Samples;

            if (index < 0)
            {
                curveType = curve.LeftEdge?.CurveType ?? default;
                var zero = curve.LeftEdge?.ZeroValue ?? defaultZero;
                return new Vector2(0f, zero);
            }

            if (index >= samples.Length)
            {
                curveType = curve.RightEdge?.CurveType ?? default;
                var zero = curve.RightEdge?.ZeroValue ?? defaultZero;
                return new Vector2(duration, zero);
            }

            var sample = samples[index];
            curveType = sample.Curve ?? default;
            return new Vector2(sample.Time, sample.Value);
        }

        private static float Interpolate(int interpolator, Vector2 pre, Vector2 start, Vector2 end, Vector2 next, float fraction)
        {
            switch (interpolator)
            {
                case EaseInInterpolator:
                    return float.Lerp(start.Y, end.Y, MathF.Sin(MathF.PI * fraction * 0.5f));
                case EaseOutInterpolator:
                    return float.Lerp(start.Y, end.Y, 1f - MathF.Sin((MathF.PI * fraction * 0.5f) + (MathF.PI * 0.5f)));
                case EaseInOutInterpolator:
                    return float.Lerp(start.Y, end.Y, (3f * fraction * fraction) - (2f * fraction * fraction * fraction));
                case LinearInterpolator:
                    return float.Lerp(start.Y, end.Y, fraction);
                default:
                    return CatmullRomNormalizedX(pre, start, end, next, fraction);
            }
        }

        // Catmull-rom through the segment, with the neighbors moved along their direction to the same time span
        private static float CatmullRomNormalizedX(Vector2 pre, Vector2 start, Vector2 end, Vector2 next, float fraction)
        {
            var length = end.X - start.X;
            if (length != 0f)
            {
                if (pre.X != start.X)
                {
                    pre = Vector2.Lerp(start, pre, length / (start.X - pre.X));
                }

                if (next.X != end.X)
                {
                    next = Vector2.Lerp(end, next, length / (next.X - end.X));
                }
            }

            var squared = fraction * fraction;
            var cubed = squared * fraction;

            return 0.5f * (
                ((-pre.Y + (3f * start.Y) - (3f * end.Y) + next.Y) * cubed) +
                (((2f * pre.Y) - (5f * start.Y) + (4f * end.Y) - next.Y) * squared) +
                ((end.Y - pre.Y) * fraction) +
                (2f * start.Y));
        }
    }
}
