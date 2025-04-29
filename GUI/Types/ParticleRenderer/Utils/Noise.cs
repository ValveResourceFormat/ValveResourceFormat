using GUI.Utils;

namespace GUI.Types.ParticleRenderer.Utils
{
    static class Noise
    {
        // Simple perlin noise implementation
        public static float Simplex1D(float t)
        {
            var previous = PseudoRandom(MathF.Floor(t));
            var next = PseudoRandom(MathF.Ceiling(t));

            return CosineInterpolate(previous, next, MathUtils.Fract(t));
        }

        /// <summary>
        /// Yes I know it's not actually a proper LCG but I need it to work without knowing the last value.
        /// </summary>
        private static float PseudoRandom(float t)
            => ((1013904223517 * t) % 1664525) / 1664525f;

        private static float CosineInterpolate(float start, float end, float mu)
        {
            var mu2 = (1 - MathF.Cos(mu * MathF.PI)) / 2f;
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
