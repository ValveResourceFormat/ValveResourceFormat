namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// The scalar math the particle engine uses, reproduced operation for operation. Several of these
    /// look like they could be written more simply and cannot: the engine reaches for approximations
    /// and redundant-looking arithmetic whose rounding is part of the result, so a "simplification"
    /// here changes what particles do.
    /// </summary>
    static class ParticleMath
    {
        private const float OneOverPi = 0.31830987f;

        /// <summary>Length below which a vector counts as having no direction.</summary>
        public const float MinimumLength = 1e-6f;

        /// <summary>Squared length below which a vector is too short to normalise.</summary>
        public const float MinimumLengthSquared = 1e-12f;

        /// <summary>
        /// FLT_EPSILON, which the engine adds to a divisor so a zero-length span is a step rather
        /// than an infinity.
        /// </summary>
        public const float FloatEpsilon = 1.1920929e-7f;

        /// <summary>
        /// The parabola <c>4t(1 - t)</c> over [0, 1], which the engine uses in place of a sine half
        /// period and as a triangle-like wave.
        /// </summary>
        public static float HalfWave(float t) => (4f - (4f * t)) * t;

        public static float Square(float value) => value * value;

        /// <summary>
        /// A subtraction from one and back, which the engine's alternate noise turbulence puts its
        /// target through. It is not an identity in floats: it moves about a third of inputs by a ulp.
        /// </summary>
        public static float RoundTripThroughOne(float value) => 1f - (1f - value);

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
        /// Approximates the sine and cosine of an angle in radians, for the initializers that draw a random
        /// direction. It is <see cref="HalfWave"/> plus a correction term that brings the peak error to 0.0011,
        /// and the cosine is derived from the sine.
        /// </summary>
        public static (float Sin, float Cos) SinCos(float radians)
        {
            var phase = Wrap(radians * OneOverPi, out var negative);
            var secondHalf = phase >= 1f;

            var parabola = HalfWave(secondHalf ? phase - 1f : phase);
            var sin = (((parabola * parabola) - parabola) * 0.225f) + parabola;

            if (negative ^ secondHalf)
            {
                sin = -sin;
            }

            var cos = MathF.Sqrt(1f - (sin * sin));

            return (sin, phase is >= 0.5f and <= 1.5f ? -cos : cos);
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
            bias = Math.Clamp(bias, 1.17549435e-38f, 1f);

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
        /// world up as the reference; a vertical forward gets a fixed left instead.
        /// </summary>
        public static (Vector3 Left, Vector3 Up) VectorVectors(Vector3 forward)
        {
            if (MathF.Abs(forward.X) < 1e-6f && MathF.Abs(forward.Y) < 1e-6f)
            {
                return (Vector3.UnitY, new Vector3(-forward.Z, 0f, 0f));
            }

            var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
            var up = Vector3.Normalize(Vector3.Cross(right, forward));

            return (-right, up);
        }

        /// <summary>
        /// Interpolates from <paramref name="from"/> to <paramref name="to"/> by a normalized linear blend
        /// whose fraction is corrected toward constant angular speed. Takes the short way round and
        /// returns the identity when the blend cancels out.
        /// </summary>
        public static Quaternion Blend(Quaternion from, Quaternion to, float t)
        {
            var dot = Quaternion.Dot(to, from);
            var d = MathF.Abs(dot);
            var a = ((((3.55645f - (1.43519f * d)) * d) - 3.2452f) * d) + 1.0904f;
            var b = ((((0.215638f * d) - 1.06021f) * d) + 0.848013f);
            var centered = t - 0.5f;
            var corrected = (((a * centered * centered) + b) * (centered * t * (t - 1f))) + t;
            var toWeight = dot <= 0f ? -corrected : corrected;

            var blended = (to * toWeight) + (from * (1f - corrected));

            return blended.LengthSquared() > 0f ? Quaternion.Normalize(blended) : Quaternion.Identity;
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
