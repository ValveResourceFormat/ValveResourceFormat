namespace ValveResourceFormat.Renderer.Particles.Utils
{
    static class Noise
    {
        // Simple perlin noise implementation, returns a value in [-1, 1] to match Source's NoiseSIMD.
        public static float Simplex1D(float t)
        {
            var previous = PseudoRandom(MathF.Floor(t));
            var next = PseudoRandom(MathF.Ceiling(t));

            return (2f * CosineInterpolate(previous, next, MathUtils.Fract(t))) - 1f;
        }

        /// <summary>
        /// Yes I know it's not actually a proper LCG but I need it to work without knowing the last value.
        /// </summary>
        private static float PseudoRandom(float t)
        {
            // Compute in double and wrap into [0, 1) so large or negative inputs stay well distributed
            var value = 1013904223517.0 * t % 1664525.0 / 1664525.0;
            return (float)(value < 0 ? value + 1 : value);
        }

        private static float CosineInterpolate(float start, float end, float mu)
        {
            var mu2 = (1 - float.CosPi(mu)) / 2f;
            return float.Lerp(start, end, mu2);
        }
    }
    /* PFNoiseType_t:
     * PF_NOISE_TYPE_PERLIN
     * PF_NOISE_TYPE_SIMPLEX
     * PF_NOISE_TYPE_WORLEY
     * PF_NOISE_TYPE_CURL
     */
    /* PFNoiseModifier_t:
     * PF_NOISE_MODIFIER_NONE
     * PF_NOISE_MODIFIER_LINES
     * PF_NOISE_MODIFIER_CLUMPS
     * PF_NOISE_MODIFIER_RINGS 
     */
    /* PFNoiseTurbulence_t:
     * PF_NOISE_TURB_NONE
     * PF_NOISE_TURB_HIGHLIGHT
     * PF_NOISE_TURB_FEEDBACK
     * PF_NOISE_TURB_LOOPY
     * PF_NOISE_TURB_CONTRAST
     * PF_NOISE_TURB_ALTERNATE
     */
}
