namespace GUI.Types.ParticleRenderer.Utils
{
    public enum PfBiasType
    {
        Invalid = -1,
        Standard = 0,
        Gain = 1,
        Exponential = 2
    }

    public enum PfRandomMode
    {
        Invalid = -1,

        /// <summary>
        /// Random per-particle but doesn't change per frame.
        /// </summary>
        Constant = 0,

        /// <summary>
        /// Random per-particle, per-frame.
        /// </summary>
        Varying = 1,
    }

    // template for shared remapping functionality
    static class NumericBias
    {
        public static float ApplyBias(float number, float bias, PfBiasType biasType = PfBiasType.Standard)
        {
            // !!!!REPLACE LATER!!!!

            // number must be between 0-1. with the bias at 0 the number is always at 0, and vice versa for 1.
            return number;
        }
    }
}
