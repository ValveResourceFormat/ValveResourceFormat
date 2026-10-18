namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// Math with particle-specific semantics: the parabolic sine of the oscillators, quarter step
    /// exponents, bias curves, the frame built around a forward vector, the epsilon normalize and range
    /// fraction, and the length and epsilon thresholds particle functions share. General-purpose
    /// helpers live in <see cref="MathUtils"/>.
    /// </summary>
    static class ParticleMath
    {
        /// <summary>Length below which a vector counts as having no direction.</summary>
        public const float MinimumLength = 1e-6f;

        /// <summary>Squared length below which a vector is too short to normalise.</summary>
        public const float MinimumLengthSquared = 1e-12f;

        /// <summary>
        /// FLT_EPSILON, which the engine adds to a divisor so a zero-length span is a step rather
        /// than an infinity.
        /// </summary>
        public const float FloatEpsilon = 1.1920929e-7f;

        /// <summary>FLT_MIN, the smallest positive normal float.</summary>
        public const float FloatMin = 1.17549435e-38f;

        /// <summary>Clamps <paramref name="value"/> to [0, 1], with NaN giving 0.</summary>
        public static float ClampToUnit(float value) => value >= 0f ? MathF.Min(value, 1f) : 0f;

        /// <summary>
        /// The parabola <c>4t(1 - t)</c> over [0, 1], which the engine uses in place of a sine half
        /// period and as a triangle-like wave.
        /// </summary>
        public static float HalfWave(float t) => (4f - (4f * t)) * t;

        public static float Square(float value) => value * value;

        /// <summary>
        /// <paramref name="exponent"/> clamped to [-255, 255] and truncated toward zero to a multiple of
        /// 0.25, the exponent <see cref="PowQuarterSteps"/> raises to.
        /// </summary>
        public static float TruncateToQuarterSteps(float exponent)
            => (int)(Math.Clamp(exponent, -255f, 255f) * 4f) * 0.25f;

        /// <summary>
        /// Raises <paramref name="value"/> to <paramref name="exponent"/> after
        /// <see cref="TruncateToQuarterSteps"/>, so an exponent of 2.3 acts as 2.25.
        /// </summary>
        public static float PowQuarterSteps(float value, float exponent)
            => MathF.Pow(value, TruncateToQuarterSteps(exponent));

        /// <summary>
        /// The engine's approximation of <c>sin(pi * x)</c>, used by the oscillation operators. The
        /// phase is wrapped to one period of 2 and each half is <see cref="HalfWave"/>, which runs up
        /// to 0.056 above a true sine and is 4/pi times it as the phase approaches a zero crossing.
        /// </summary>
        public static float SinPi(float x)
        {
            var phase = Wrap(x, out var negative);
            var secondHalf = phase >= 1f;
            var value = HalfWave(secondHalf ? phase - 1f : phase);

            return negative ^ secondHalf ? -value : value;
        }

        /// <summary>
        /// The engine's standard bias curve, <c>x / ((1 - x) * (1/bias - 2) + 1)</c>, for parameters
        /// authored directly on an operator field. A bias of 0.5 is the identity; at or below 0 the
        /// result is always 0, and at or above 1 it is always 1.
        /// </summary>
        /// <param name="x">Value in the 0-1 range.</param>
        /// <param name="bias">Bias parameter.</param>
        public static float Bias(float x, float bias)
        {
            if (bias <= 0f)
            {
                return 0f;
            }

            if (bias >= 1f)
            {
                return 1f;
            }

            return x / (((1f - x) * ((1f / bias) - 2f)) + 1f);
        }

        /// <summary>
        /// The curve of <see cref="Bias(float, float)"/> with <paramref name="x"/> clamped to [0, 1] and
        /// <paramref name="bias"/> to [smallest normal float, 1] instead of saturating at the bias limits.
        /// </summary>
        /// <param name="x">Value to bias.</param>
        /// <param name="bias">Bias parameter.</param>
        public static float BiasClamped(float x, float bias)
        {
            x = MathUtils.Saturate(x);
            bias = Math.Clamp(bias, FloatMin, 1f);

            return x / (((1f - x) * ((1f / bias) - 2f)) + 1f);
        }

        /// <summary>
        /// Applies one of the <see cref="ParticleFloatBiasType"/> curves to a 0-1 value for a
        /// <c>m_flBiasParameter</c>, which is authored in the -1 to 1 range and is the identity at 0.
        /// This is a different parameterization to <see cref="Bias(float, float)"/> and the two take
        /// different numbers for the same curve. Any type outside the three curves returns 0.
        /// </summary>
        /// <param name="x">Value in the 0-1 range.</param>
        /// <param name="biasParameter">Bias parameter in the -1 to 1 range.</param>
        /// <param name="biasType">Curve to apply.</param>
        public static float BiasFromParameter(float x, float biasParameter, ParticleFloatBiasType biasType)
        {
            if (biasType == ParticleFloatBiasType.PF_BIAS_TYPE_EXPONENTIAL)
            {
                var exponent = biasParameter >= 0f
                    ? 1f - MathUtils.Saturate(biasParameter)
                    : 20f - (MathUtils.Saturate(biasParameter + 1f) * 19f);

                if (exponent <= 0f)
                {
                    return 1f;
                }

                if (x <= 0f)
                {
                    return 0f;
                }

                if (x >= 1f)
                {
                    return 1f;
                }

                return MathF.Pow(x, Math.Min(exponent, 20f));
            }

            if (biasType is not ParticleFloatBiasType.PF_BIAS_TYPE_STANDARD
                and not ParticleFloatBiasType.PF_BIAS_TYPE_GAIN)
            {
                return 0f;
            }

            var bias = MathUtils.Saturate((biasParameter + 1f) * 0.5f);

            if (bias <= 0f)
            {
                return 0f;
            }

            if (bias >= 1f)
            {
                return 1f;
            }

            if (biasType == ParticleFloatBiasType.PF_BIAS_TYPE_GAIN)
            {
                return x < 0.5f
                    ? Bias(x + x, bias) * 0.5f
                    : 1f - (Bias(2f - x - x, bias) * 0.5f);
            }

            return Bias(x, bias);
        }

        /// <summary>
        /// Builds the left and up axes that complete a frame around <paramref name="forward"/>, taking
        /// world up as the reference; a forward whose X and Y are both under 0.001 counts as vertical
        /// and gets a fixed left instead.
        /// </summary>
        public static (Vector3 Left, Vector3 Up) VectorVectors(Vector3 forward)
        {
            if (MathF.Abs(forward.X) < 0.001f && MathF.Abs(forward.Y) < 0.001f)
            {
                return (Vector3.UnitY, new Vector3(-forward.Z, 0f, 0f));
            }

            var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
            var up = Vector3.Normalize(Vector3.Cross(right, forward));

            return (-right, up);
        }

        /// <summary>
        /// Folds <paramref name="x"/> onto [0, 2), one period of <c>sin(pi * x)</c>, and reports
        /// whether it was negative.
        /// </summary>
        private static float Wrap(float x, out bool negative)
        {
            negative = float.IsNegative(x);

            return MathF.Abs(x) % 2f;
        }

        /// <summary>
        /// Scales <paramref name="value"/> to unit length with <see cref="FloatEpsilon"/> added to its squared
        /// length, so a vector much shorter than the square root of that epsilon shrinks toward zero instead
        /// of growing to unit length.
        /// </summary>
        public static Vector3 NormalizeWithEpsilon(Vector3 value)
            => value / MathF.Sqrt(value.LengthSquared() + FloatEpsilon);

        /// <summary>
        /// Where <paramref name="x"/> lies between <paramref name="min"/> and <paramref name="max"/> as a
        /// fraction clamped to [0, 1], with a range of no width counting as one unit wide.
        /// </summary>
        public static float ClampedFraction(float x, float min, float max)
            => MathUtils.Saturate((x - min) / (max == min ? 1f : max - min));
    }
}
