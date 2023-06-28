namespace GUI.Types.ParticleRenderer.Utils
{
    // template for shared remapping functionality
    static class NumericBias
    {
        /*
         * ParticleFloatBiasType_t:
         * PF_BIAS_TYPE_INVALID
         * PF_BIAS_TYPE_STANDARD
         * PF_BIAS_TYPE_GAIN
         * PF_BIAS_TYPE_EXPONENTIAL
         */
        public static float ApplyBias(float number, float bias, string biasType = "PF_BIAS_TYPE_STANDARD")
        {
            // !!!!REPLACE LATER!!!!

            // number must be between 0-1. with the bias at 0 the number is always at 0, and vice versa for 1.
            return number;
        }
    }
}
