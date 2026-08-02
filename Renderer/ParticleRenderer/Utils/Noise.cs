using System.Numerics;

namespace ValveResourceFormat.Renderer.Particles.Utils
{
    /// <summary>
    /// The engine's general-purpose particle noise primitive: a value-noise lattice whose corners come
    /// from a MurmurHash3-family integer hash, trilinearly interpolated, returning [-1, 1].
    /// </summary>
    static class Noise
    {
        /// <summary>
        /// Samples the noise lattice. Operators that only vary one input sample the x=y=z diagonal,
        /// which is what the engine does when no world noise control point is set.
        /// </summary>
        public static float Simplex1D(float t) => Value3D(t, t, t);

        /// <inheritdoc cref="Value3D(float, float, float)"/>
        public static float Value3D(Vector3 position) => Value3D(position.X, position.Y, position.Z);

        /// <summary>Samples the noise lattice at a point, returning a value in [-1, 1].</summary>
        public static float Value3D(float x, float y, float z)
        {
            var ix = (int)MathF.Floor(x);
            var iy = (int)MathF.Floor(y);
            var iz = (int)MathF.Floor(z);

            var fx = x - ix;
            var fy = y - iy;
            var fz = z - iz;

            var c00 = float.Lerp(Hash(ix, iy, iz), Hash(ix + 1, iy, iz), fx);
            var c10 = float.Lerp(Hash(ix, iy + 1, iz), Hash(ix + 1, iy + 1, iz), fx);
            var c01 = float.Lerp(Hash(ix, iy, iz + 1), Hash(ix + 1, iy, iz + 1), fx);
            var c11 = float.Lerp(Hash(ix, iy + 1, iz + 1), Hash(ix + 1, iy + 1, iz + 1), fx);

            var c0 = float.Lerp(c00, c10, fy);
            var c1 = float.Lerp(c01, c11, fy);

            return (float.Lerp(c0, c1, fz) - 0.5f) * 2f;
        }

        private static float Hash(int x, int y, int z)
        {
            unchecked
            {
                var h = (uint)x * 0xCC9E2D51u;
                h = System.Numerics.BitOperations.RotateLeft(h, 15) * 0x1B873593u;
                h ^= (uint)y * 0x85EBCA6Bu;
                h = (System.Numerics.BitOperations.RotateLeft(h, 13) * 5u) + 0xE6546B64u;
                h ^= (uint)z * 0xC2B2AE35u;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;

                return (h >> 16) / 65536f;
            }
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
