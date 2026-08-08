namespace ValveResourceFormat.Renderer.Particles.Utils
{
    // template for shared remapping functionality
    static class NumericBias
    {
#pragma warning disable IDE0060 // Remove unused parameter - TODO: Remove this suppression when this is actually implemented
        public static float ApplyBias(float number, float bias, ParticleFloatBiasType biasType = ParticleFloatBiasType.PF_BIAS_TYPE_STANDARD)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            // !!!!REPLACE LATER!!!!

            // number must be between 0-1. with the bias at 0 the number is always at 0, and vice versa for 1.
            return number;
        }

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
    }
}
