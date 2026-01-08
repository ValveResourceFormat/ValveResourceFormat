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
    }
}
