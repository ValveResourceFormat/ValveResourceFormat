namespace ValveResourceFormat.Renderer.Particles.Utils
{
    static class FastTrig
    {
        private const float OneOverPi = 0.31830987f;

        /// <summary>
        /// The engine's approximation of <c>sin(pi * x)</c>, used by the oscillation operators. The
        /// phase is wrapped to one period of 2 and each half is the parabola <c>4t(1 - t)</c>, which
        /// runs up to 0.056 above a true sine and is 4/pi times it as the phase approaches a zero
        /// crossing.
        /// </summary>
        public static float SinPi(float x)
        {
            var phase = Wrap(x, out var negative);
            var secondHalf = phase >= 1f;
            var t = secondHalf ? phase - 1f : phase;
            var value = (4f - (4f * t)) * t;

            return negative ^ secondHalf ? -value : value;
        }

        /// <summary>
        /// The engine's approximation of the sine and cosine of an angle in radians, used by the
        /// initializers that draw a random direction. It is the same parabola as <see cref="SinPi"/>
        /// with a correction term that brings the peak error down to 7e-4, and the cosine is taken
        /// from the sine rather than approximated separately.
        /// </summary>
        public static (float Sin, float Cos) SinCos(float radians)
        {
            var phase = Wrap(radians * OneOverPi, out var negative);
            var secondHalf = phase >= 1f;
            var t = secondHalf ? phase - 1f : phase;

            var parabola = (4f - (4f * t)) * t;
            var sin = (((parabola * parabola) - parabola) * 0.225f) + parabola;

            if (negative ^ secondHalf)
            {
                sin = -sin;
            }

            var cos = MathF.Sqrt(1f - (sin * sin));

            return (sin, phase is >= 0.5f and <= 1.5f ? -cos : cos);
        }

        /// <summary>
        /// Folds <paramref name="x"/> onto [0, 2), one period of <c>sin(pi * x)</c>, and reports
        /// whether it was negative.
        /// </summary>
        private static float Wrap(float x, out bool negative)
        {
            const float RoundingMagic = 8388608f;

            negative = float.IsNegative(x);

            var magnitude = MathF.Abs(x);

            // Adding 2^23 gives the mantissa's low bit a weight of 1, so clearing that bit lands on an
            // even integer, and the compare backs off a period where the rounding went up.
            var even = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(magnitude + RoundingMagic) & ~1) - RoundingMagic;

            return magnitude - (magnitude < even ? even - 2f : even);
        }
    }
}
