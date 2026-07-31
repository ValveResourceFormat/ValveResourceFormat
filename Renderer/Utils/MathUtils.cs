using System.Diagnostics;
using System.Runtime.CompilerServices;
using ValveResourceFormat.Renderer.AnimLib;

namespace ValveResourceFormat.Renderer.Utils
{
    /// <summary>
    /// Common math utility functions for rendering and animation.
    /// </summary>
    public static class MathUtils
    {
        /// <summary>
        /// Remaps a value from input range to 0-1.
        /// </summary>
        /// <param name="x">Value to remap.</param>
        /// <param name="inputMin">Input range minimum.</param>
        /// <param name="inputMax">Input range maximum.</param>
        /// <returns>Value remapped to 0-1 range.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Remap(float x, float inputMin, float inputMax)
        {
            if (inputMin == inputMax) { return inputMax; }

            return (x - inputMin) / (inputMax - inputMin);
        }

        /// <summary>
        /// Remaps a value from one range to another.
        /// </summary>
        /// <param name="x">Value to remap.</param>
        /// <param name="inputMin">Input range minimum.</param>
        /// <param name="inputMax">Input range maximum.</param>
        /// <param name="outputMin">Output range minimum.</param>
        /// <param name="outputMax">Output range maximum.</param>
        /// <returns>Value remapped to output range.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float RemapRange(float value, float inputMin, float inputMax, float outputMin, float outputMax)
        {
            // Matches Esoterica: the input percentage is clamped before remapping
            var t = inputMax != inputMin ? Math.Clamp((value - inputMin) / (inputMax - inputMin), 0f, 1f) : 0f;
            return Lerp(outputMin, outputMax, t);
        }

        /// <summary>
        /// Remaps a value from one range to another, holding the output at the range ends
        /// for inputs outside the input range. Mirrors Source's RemapValClamped.
        /// </summary>
        /// <param name="x">Value to remap.</param>
        /// <param name="inputMin">Input range minimum.</param>
        /// <param name="inputMax">Input range maximum.</param>
        /// <param name="outputMin">Output range minimum.</param>
        /// <param name="outputMax">Output range maximum.</param>
        /// <returns>Value remapped to the output range, clamped to it.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float RemapValClamped(float x, float inputMin, float inputMax, float outputMin, float outputMax)
        {
            // Source treats a degenerate input range as a threshold
            if (inputMin == inputMax)
            {
                return x >= inputMax ? outputMax : outputMin;
            }

            return float.Lerp(outputMin, outputMax, Saturate(Remap(x, inputMin, inputMax)));
        }

        /// <summary>
        /// Swaps <paramref name="min"/> and <paramref name="max"/> if <paramref name="min"/> > <paramref name="max"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MinMaxFixUp<T>(ref T min, ref T max) where T : INumber<T>
        {
            if (min > max)
            {
                (min, max) = (max, min);
            }
        }

        /// <summary>
        /// Clamps a value to [0, 1].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Saturate(float x)
        {
            return Math.Clamp(x, 0.0f, 1.0f);
        }

        /// <summary>
        /// Linearly interpolates between two values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        /// <summary>
        /// Returns the fractional part of a value (x - floor(x)).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Fract(float x) => x - MathF.Floor(x);

        /// <summary>
        /// Wraps a value into the half-open range [<paramref name="lowBounds"/>, <paramref name="highBounds"/>).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Wrap(float x, float lowBounds, float highBounds)
        {
            var range = highBounds - lowBounds;
            return x - range * MathF.Floor((x - lowBounds) / range);
        }

        /// <summary>
        /// Linearly interpolates between two angles in radians, taking the shortest path around the circle.
        /// </summary>
        /// <param name="from">Start angle in radians.</param>
        /// <param name="to">End angle in radians.</param>
        /// <param name="amount">Interpolation weight (0.0 = from, 1.0 = to).</param>
        /// <returns>Interpolated angle in radians.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float LerpAngle(float from, float to, float amount)
        {
            var diff = (to - from) % MathF.Tau;
            var shortestPath = 2.0f * diff % MathF.Tau - diff;

            return from + shortestPath * amount;
        }

        /// <summary>
        /// Implementations follow common Robert Penner easing equations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static float Ease(EasingOperation easingOp, float t)
        {
            Debug.Assert(t >= 0f && t <= 1f, "Easing parameter t is out of bounds [0,1]");

            return easingOp switch
            {
                EasingOperation.Linear => t,

                EasingOperation.InQuad => t * t,
                EasingOperation.OutQuad => t * (2f - t),
                EasingOperation.InOutQuad => t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t,

                EasingOperation.InCubic => t * t * t,
                EasingOperation.OutCubic => (t - 1f) * (t - 1f) * (t - 1f) + 1f,
                EasingOperation.InOutCubic => t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3) / 2f,

                EasingOperation.InQuart => t * t * t * t,
                EasingOperation.OutQuart => 1f - (t - 1f) * (t - 1f) * (t - 1f) * (t - 1f),
                EasingOperation.InOutQuart => t < 0.5f ? 8f * t * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 4) / 2f,

                EasingOperation.InQuint => t * t * t * t * t,
                EasingOperation.OutQuint => (t - 1f) * (t - 1f) * (t - 1f) * (t - 1f) * (t - 1f) + 1f,
                EasingOperation.InOutQuint => t < 0.5f ? 16f * t * t * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 5) / 2f,

                EasingOperation.InSine => 1f - MathF.Cos(t * MathF.PI / 2f),
                EasingOperation.OutSine => MathF.Sin(t * MathF.PI / 2f),
                EasingOperation.InOutSine => 0.5f * (1f - MathF.Cos(MathF.PI * t)),

                EasingOperation.InExpo => t <= 0f ? 0f : MathF.Pow(2f, 10f * (t - 1f)),
                EasingOperation.OutExpo => t >= 1f ? 1f : 1f - MathF.Pow(2f, -10f * t),
                EasingOperation.InOutExpo => t <= 0f ? 0f : t >= 1f ? 1f : (t < 0.5f) ? MathF.Pow(2f, 20f * t - 10f) / 2f : (2f - MathF.Pow(2f, -20f * t + 10f)) / 2f,

                EasingOperation.InCirc => 1f - MathF.Sqrt(1f - t * t),
                EasingOperation.OutCirc => MathF.Sqrt(1f - (t - 1f) * (t - 1f)),
                EasingOperation.InOutCirc => t < 0.5f ? (1f - MathF.Sqrt(1f - 4f * t * t)) / 2f : (MathF.Sqrt(1f - (-2f * t + 2f) * (-2f * t + 2f)) + 1f) / 2f,

                EasingOperation.None => t,
                _ => throw new UnreachableException(),
            };
        }

        /// <summary>
        /// Evaluates a cubic Bezier curve at <paramref name="t"/> in [0, 1].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 CubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            var u = 1.0f - t;
            var uu = u * u;
            var tt = t * t;
            return (uu * u * p0) + (3.0f * uu * t * p1) + (3.0f * u * tt * p2) + (tt * t * p3);
        }
    }
}
