namespace ValveResourceFormat.Renderer.Particles.Utils
{
    // template for shared remapping functionality
    static class NumericBias
    {
        /// <summary>
        /// The engine's standard bias curve, <c>x / ((1 - x) * (1/bias - 2) + 1)</c>, for parameters
        /// authored directly on an operator field. A bias of 0.5 is the identity; at or below 0 the
        /// result is always 0, and at or above 1 it is always 1.
        /// </summary>
        /// <param name="x">Value in the 0-1 range.</param>
        /// <param name="bias">Bias parameter.</param>
        public static float Standard(float x, float bias)
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
        /// Applies one of the <see cref="ParticleFloatBiasType"/> curves to a 0-1 value for a
        /// <c>m_flBiasParameter</c>, which is authored in the -1 to 1 range and is the identity at 0.
        /// This is a different parameterization to <see cref="Standard(float, float)"/> and the two
        /// take different numbers for the same curve. Any type outside the three curves returns 0.
        /// </summary>
        /// <param name="x">Value in the 0-1 range.</param>
        /// <param name="biasParameter">Bias parameter in the -1 to 1 range.</param>
        /// <param name="biasType">Curve to apply.</param>
        public static float FromBiasParameter(float x, float biasParameter, ParticleFloatBiasType biasType)
        {
            if (biasType == ParticleFloatBiasType.PF_BIAS_TYPE_EXPONENTIAL)
            {
                var exponent = biasParameter >= 0f
                    ? 1f - Math.Clamp(biasParameter, 0f, 1f)
                    : 20f - (Math.Clamp(biasParameter + 1f, 0f, 1f) * 19f);

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

            var bias = Math.Clamp((biasParameter + 1f) * 0.5f, 0f, 1f);

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
                    ? Standard(x + x, bias) * 0.5f
                    : 1f - (Standard(2f - x - x, bias) * 0.5f);
            }

            return Standard(x, bias);
        }
    }
}
