using System.Runtime.CompilerServices;

namespace ValveResourceFormat.Renderer
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
        public static float RemapRange(float x, float inputMin, float inputMax, float outputMin, float outputMax)
        {
            return float.Lerp(outputMin, outputMax, Remap(x, inputMin, inputMax));
        }

        /// <summary>
        /// Swaps min and max if min > max.
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
        /// Returns the fractional part of a value (x - floor(x)).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Fract(float x) => x % 1f;

        /// <summary>
        /// Wraps a value within a range (modulo with offset).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Wrap(float x, float lowBounds, float highBounds)
        {
            return ((x - lowBounds) % highBounds) + lowBounds;
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
    }
}
