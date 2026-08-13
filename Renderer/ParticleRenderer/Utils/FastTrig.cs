namespace ValveResourceFormat.Renderer.Particles.Utils
{
    static class FastTrig
    {
        /// <summary>
        /// The engine's approximation of <c>sin(pi * x)</c>, used by the oscillation operators. The
        /// phase is wrapped to one period of 2 and each half is the parabola <c>4t(1 - t)</c>, which
        /// runs up to 0.056 above a true sine and is 4/pi times it as the phase approaches a zero
        /// crossing.
        /// </summary>
        public static float SinPi(float x)
        {
            const float RoundingMagic = 8388608f;

            var magnitude = MathF.Abs(x);

            // Adding 2^23 gives the mantissa's low bit a weight of 1, so clearing that bit lands on an
            // even integer, and the compare backs off a period where the rounding went up.
            var even = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(magnitude + RoundingMagic) & ~1) - RoundingMagic;
            var phase = magnitude - (magnitude < even ? even - 2f : even);

            var secondHalf = phase >= 1f;
            var t = secondHalf ? phase - 1f : phase;
            var value = (4f - (4f * t)) * t;

            return float.IsNegative(x) ^ secondHalf ? -value : value;
        }
    }
}
