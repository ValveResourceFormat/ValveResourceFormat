using ValveResourceFormat.Renderer.Utils;
using ValveResourceFormat.ResourceTypes.Choreo;
using ValveResourceFormat.ResourceTypes.Choreo.Curves;
using ValveResourceFormat.ResourceTypes.Choreo.Enums;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Choreo
{
    /// <summary>
    /// Evaluates the curves of a choreography scene: event ramps, the samples of flex animation tracks and gesture timing.
    /// </summary>
    public static class ChoreoCurveEvaluator
    {
        private const string LoopTagName = "loop";
        private const string EndTagName = "end";

        private readonly record struct CurvePoint(Vector2 Position, CurveType Type, BezierData? Bezier, bool IsEdge);

        /// <summary>Gets the value of a curve at a time.</summary>
        /// <param name="curve">The curve.</param>
        /// <param name="time">The time in seconds from the start of the event.</param>
        /// <param name="duration">The length of the event in seconds, where the curve ends.</param>
        /// <param name="defaultZero">The value before the first sample and after the last when the curve has no edge to set it.</param>
        /// <returns>The value from 0 to 1, 1 for a curve without samples, or 0 for a time outside of the curve.</returns>
        /// <remarks>
        /// Kochanek, normalized and tangent catmull-rom and exponential decay interpolators are drawn as catmull-rom.
        /// No shipped scene uses them.
        /// </remarks>
        public static float Evaluate(ChoreoCurveData curve, float time, float duration, float defaultZero)
        {
            ArgumentNullException.ThrowIfNull(curve);

            var samples = curve.Samples;
            if (samples.Length < 1)
            {
                return 1f;
            }

            if (time < 0f || time > duration)
            {
                return 0f;
            }

            var segment = 0;
            while (segment < samples.Length && time > samples[segment].Time)
            {
                segment++;
            }

            // Segment 0 runs from the left edge to the first sample, the last from the last sample to the right edge
            var start = GetBoundedPoint(curve, segment - 1, duration, defaultZero);
            var end = GetBoundedPoint(curve, segment, duration, defaultZero);
            var pre = GetBoundedPoint(curve, segment - 2, duration, defaultZero);
            var next = GetBoundedPoint(curve, segment + 1, duration, defaultZero);

            var length = end.Position.X - start.Position.X;
            var fraction = 0f;
            if (length > 0f)
            {
                fraction = Math.Clamp((time - start.Position.X) / length, 0f, 1f);
            }

            var earlyInterpolator = start.Type.OutType;
            var laterInterpolator = end.Type.InType;

            float value;
            if (earlyInterpolator == CurveInterpolator.Hold)
            {
                value = start.Position.Y;
            }
            else if (laterInterpolator == CurveInterpolator.Hold)
            {
                value = end.Position.Y;
            }
            else
            {
                var early = Interpolate(earlyInterpolator, pre, start, end, next, fraction);
                var later = Interpolate(laterInterpolator, pre, start, end, next, fraction);
                value = float.Lerp(early, later, fraction);
            }

            return Math.Clamp(value, 0f, 1f);
        }

        /// <summary>
        /// Gets how far a gesture is through its sequence. The playback time tags mark fractions of the event
        /// and the shifted time tags the fractions of the sequence that play there.
        /// </summary>
        /// <param name="gesture">The gesture event.</param>
        /// <param name="fraction">How far playback is through the event, from 0 to 1.</param>
        /// <returns>The cycle of the sequence from 0 to 1.</returns>
        public static float GetGestureCycle(ChoreoEvent gesture, float fraction)
        {
            ArgumentNullException.ThrowIfNull(gesture);

            var playback = gesture.PlaybackTimeTags;
            var shifted = gesture.ShiftedTimeTags;

            if (playback.Length == 0 || playback.Length != shifted.Length)
            {
                return fraction;
            }

            var segment = -1;
            while (segment + 1 < playback.Length && fraction >= playback[segment + 1].Fraction)
            {
                segment++;
            }

            var start = GetTagPoint(playback, shifted, segment);
            var end = GetTagPoint(playback, shifted, segment + 1);

            var length = end.X - start.X;
            if (length <= 0f)
            {
                return start.Y;
            }

            var position = Math.Clamp((fraction - start.X) / length, 0f, 1f);
            var startLocked = IsLockedTag(playback, segment);
            var endLocked = IsLockedTag(playback, segment + 1);

            if (startLocked && endLocked)
            {
                return float.Lerp(start.Y, end.Y, position);
            }

            // A locked tag holds its tangent to the segment, so the point behind it is mirrored
            var pre = startLocked ? (2f * start) - end : GetTagPoint(playback, shifted, segment - 1);
            var next = endLocked ? (2f * end) - start : GetTagPoint(playback, shifted, segment + 2);

            return CatmullRomNormalizedX(pre, start, end, next, position);
        }

        // The event runs from the start to the end of the sequence, also where the tags do not say so
        private static Vector2 GetTagPoint(ChoreoTag[] playback, ChoreoTag[] shifted, int index)
        {
            if (index < 0)
            {
                return Vector2.Zero;
            }

            if (index >= playback.Length)
            {
                return Vector2.One;
            }

            return new Vector2(playback[index].Fraction, shifted[index].Fraction);
        }

        private static bool IsLockedTag(ChoreoTag[] playback, int index)
        {
            if (index < 0 || index >= playback.Length)
            {
                return false;
            }

            var name = playback[index].Name;
            return name.Equals(LoopTagName, StringComparison.OrdinalIgnoreCase) || name.Equals(EndTagName, StringComparison.OrdinalIgnoreCase);
        }

        // The samples with an extra point before the first and after the last, which are the zero values at the edges
        private static CurvePoint GetBoundedPoint(ChoreoCurveData curve, int index, float duration, float defaultZero)
        {
            var samples = curve.Samples;

            if (index < 0)
            {
                var zero = curve.LeftEdge?.ZeroValue ?? defaultZero;
                return new CurvePoint(new Vector2(0f, zero), curve.LeftEdge?.CurveType ?? default, null, IsEdge: true);
            }

            if (index >= samples.Length)
            {
                var zero = curve.RightEdge?.ZeroValue ?? defaultZero;
                return new CurvePoint(new Vector2(duration, zero), curve.RightEdge?.CurveType ?? default, null, IsEdge: true);
            }

            var sample = samples[index];
            return new CurvePoint(new Vector2(sample.Time, sample.Value), sample.Curve ?? default, sample.Bezier, IsEdge: false);
        }

        private static float Interpolate(CurveInterpolator interpolator, CurvePoint pre, CurvePoint start, CurvePoint end, CurvePoint next, float fraction)
        {
            var startValue = start.Position.Y;
            var endValue = end.Position.Y;

            switch (interpolator)
            {
                case CurveInterpolator.EaseIn:
                    return float.Lerp(startValue, endValue, MathF.Sin(MathF.PI * fraction * 0.5f));
                case CurveInterpolator.EaseOut:
                    return float.Lerp(startValue, endValue, 1f - MathF.Sin((MathF.PI * fraction * 0.5f) + (MathF.PI * 0.5f)));
                case CurveInterpolator.EaseInOut:
                case CurveInterpolator.SimpleCubic:
                    return float.Lerp(startValue, endValue, MathUtils.Smoothstep(0f, 1f, fraction));
                case CurveInterpolator.Linear:
                    return float.Lerp(startValue, endValue, fraction);
                case CurveInterpolator.BSpline:
                    return BSpline(pre.Position.Y, startValue, endValue, next.Position.Y, fraction);
                case CurveInterpolator.Bezier:
                    return Bezier(pre, start, end, next, fraction);
                default:
                    return CatmullRomNormalizedX(pre.Position, start.Position, end.Position, next.Position, fraction);
            }
        }

        // Does not pass through the samples, it only pulls towards them
        private static float BSpline(float pre, float start, float end, float next, float fraction)
        {
            var squared = fraction * fraction;
            var cubed = squared * fraction;

            return (
                ((-pre + (3f * start) - (3f * end) + next) * cubed) +
                (((3f * pre) - (6f * start) + (3f * end)) * squared) +
                (((-3f * pre) + (3f * end)) * fraction) +
                pre + (4f * start) + end) / 6f;
        }

        // Runs on the fraction of the segment, the handles move values and leave time alone
        private static float Bezier(CurvePoint pre, CurvePoint start, CurvePoint end, CurvePoint next, float fraction)
        {
            var startHandle = pre.Position.Y;
            if (start.Bezier is { } startBezier)
            {
                startHandle = start.Position.Y + (MathF.Sin(float.DegreesToRadians(startBezier.OutDegrees)) * startBezier.OutWeight);
            }
            else if (start.IsEdge)
            {
                startHandle = start.Position.Y;
            }

            var endHandle = next.Position.Y;
            if (end.Bezier is { } endBezier)
            {
                endHandle = end.Position.Y + (MathF.Sin(float.DegreesToRadians(endBezier.InDegrees)) * endBezier.InWeight);
            }
            else if (end.IsEdge)
            {
                endHandle = end.Position.Y;
            }

            var inverse = 1f - fraction;

            return (inverse * inverse * inverse * start.Position.Y) +
                (3f * inverse * inverse * fraction * startHandle) +
                (3f * inverse * fraction * fraction * endHandle) +
                (fraction * fraction * fraction * end.Position.Y);
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

            return CableMeshBuilder.CatmullRom(pre.Y, start.Y, end.Y, next.Y, fraction);
        }
    }
}
