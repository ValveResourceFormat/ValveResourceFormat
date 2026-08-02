using System.Numerics;

namespace ValveResourceFormat.Renderer.Particles.Utils
{
    /// <summary>
    /// The engine's general-purpose particle noise primitive: a value-noise lattice whose corners come
    /// from a packed-index integer hash, trilinearly interpolated with unsmoothed weights, returning
    /// [-1, 1]. The hash is a MurmurHash3 mixing round followed by the engine's own finalizer.
    /// </summary>
    static class Noise
    {
        /// <summary>
        /// Samples the noise lattice. Operators that only vary one input sample the x=y=z diagonal,
        /// which is what the engine does when no world noise control point is set.
        /// </summary>
        public static float ValueDiagonal(float t) => Value3D(t, t, t);

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

        /// <summary>
        /// Hashes one lattice corner to [0, 1]. The corner is packed by plain addition rather than
        /// into disjoint bit fields, so the field aliases: Hash(x + 1024, y, z) == Hash(x, y + 1, z).
        /// </summary>
        private static float Hash(int x, int y, int z)
        {
            unchecked
            {
                var index = x + (y << 10) + (z << 20);

                var k = ((uint)index * 0xCC9E2D51u) & 0x7FFFFFFFu;
                k = (BitOperations.RotateLeft(k, 15) * 0x1B873593u) & 0x7FFFFFFFu;

                var h = (BitOperations.RotateLeft(k, 13) * 5u) + 0xE6546B64u;

                // Sign-extending shift: the unmasked bit 31 reaches bits that survive the mask
                var mixed = (uint)((int)h ^ ((int)h >> 16)) & 0x7FFFFFFFu;
                var scaled = mixed * 0x04B2AE35u;

                return ((uint)((int)scaled ^ ((int)scaled >> 16)) & 0xFFFFu) / 65535f;
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
