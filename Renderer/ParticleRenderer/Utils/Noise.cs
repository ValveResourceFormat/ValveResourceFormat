using System.Numerics;

namespace ValveResourceFormat.Renderer.Particles.Utils
{
    /// <summary>
    /// The engine's particle noise primitives. The default is a value-noise lattice whose corners come
    /// from a packed-index integer hash, trilinearly interpolated with unsmoothed weights, returning
    /// [-1, 1]. The hash is a MurmurHash3 mixing round followed by the engine's own finalizer. The
    /// gradient and cellular primitives share the corner packing with a shorter hash chain; the curl
    /// primitive interpolates a table-driven vector lattice instead.
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
        /// Samples a vector-valued lattice at a point, returning each component in [-1, 1]. Shares the
        /// packed-corner indexing and unsmoothed trilinear weights of <see cref="Value3D(float, float, float)"/>,
        /// but draws all three components from a single hash of each corner, so they are correlated.
        /// </summary>
        public static Vector3 ValueVector3(Vector3 position)
        {
            var ix = (int)MathF.Floor(position.X);
            var iy = (int)MathF.Floor(position.Y);
            var iz = (int)MathF.Floor(position.Z);

            var fx = position.X - ix;
            var fy = position.Y - iy;
            var fz = position.Z - iz;

            var c00 = Lerp(HashVector(ix, iy, iz), HashVector(ix + 1, iy, iz), fx);
            var c10 = Lerp(HashVector(ix, iy + 1, iz), HashVector(ix + 1, iy + 1, iz), fx);
            var c01 = Lerp(HashVector(ix, iy, iz + 1), HashVector(ix + 1, iy, iz + 1), fx);
            var c11 = Lerp(HashVector(ix, iy + 1, iz + 1), HashVector(ix + 1, iy + 1, iz + 1), fx);

            var c0 = Lerp(c00, c10, fy);
            var c1 = Lerp(c01, c11, fy);

            return Lerp(c0, c1, fz);
        }

        /// <summary>
        /// Hashes one lattice corner to a vector with each component in [-1, 1], taken from three bytes
        /// of a single mixed value.
        /// </summary>
        private static Vector3 HashVector(int x, int y, int z)
        {
            unchecked
            {
                var index = x + (y << 10) + (z << 20);

                var k = (int)((uint)index * 0xCC9E2D51u) & 0x7FFFFFFF;
                k = (int)(BitOperations.RotateLeft((uint)k, 15) * 0x1B873593u) & 0x7FFFFFFF;
                k = (int)BitOperations.RotateLeft((uint)k, 13) & 0x7FFFFFFF;

                var mixed = k * 0x04B2AE35;
                mixed ^= mixed >> 16;

                const float scale = 2f / 255f;

                return new Vector3(
                    ((mixed & 255) * scale) - 1f,
                    (((mixed >> 8) & 255) * scale) - 1f,
                    (((mixed >> 16) & 255) * scale) - 1f
                );
            }
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

        /// <inheritdoc cref="Simplex3D(float, float, float)"/>
        public static float Simplex3D(Vector3 position) => Simplex3D(position.X, position.Y, position.Z);

        /// <summary>
        /// Samples the simplex-named primitive, which is gradient noise with a quintic fade over the
        /// same packed corner indexing as the value lattice, returning roughly [-0.87, 0.87]. Zero on
        /// the lattice itself.
        /// </summary>
        public static float Simplex3D(float x, float y, float z)
        {
            var ix = (int)MathF.Floor(x);
            var iy = (int)MathF.Floor(y);
            var iz = (int)MathF.Floor(z);

            var fx = x - ix;
            var fy = y - iy;
            var fz = z - iz;

            var u = Fade(fx);
            var v = Fade(fy);
            var w = Fade(fz);

            var lower = GradientPlane(ix, iy, iz, fx, fy, fz, u, v, 1f - w);
            var upper = GradientPlane(ix, iy, iz + 1, fx, fy, fz - 1f, u, v, w);

            return upper + lower;
        }

        private static float Fade(float t) => (((6f * t) - 15f) * t + 10f) * (t * t * t);

        private static float GradientPlane(int ix, int iy, int iz, float fx, float fy, float dz, float u, float v, float wz)
        {
            var c0 = GradientCorner(ix, iy, iz, fx, fy, dz) * (wz * ((1f - v) * (1f - u)));
            var c1 = GradientCorner(ix + 1, iy, iz, fx - 1f, fy, dz) * (wz * ((1f - v) * u));
            var c2 = GradientCorner(ix, iy + 1, iz, fx, fy - 1f, dz) * (wz * (v * (1f - u)));
            var c3 = GradientCorner(ix + 1, iy + 1, iz, fx - 1f, fy - 1f, dz) * (wz * (v * u));

            return (c2 + c3) + (c0 + c1);
        }

        private static float GradientCorner(int x, int y, int z, float dx, float dy, float dz)
        {
            unchecked
            {
                var h = HashSeeded(x + (y << 10) + (z << 20), 0u) & 15u;

                var u = h < 8 ? dx : dy;
                var v = h < 4 ? dy : h == 12 || h == 14 ? dx : dz;

                u = (h & 1) != 0 ? -u : u;
                v = (h & 2) != 0 ? -v : v;

                return u + v;
            }
        }

        /// <summary>
        /// Samples the cellular primitive: the squared distance to the nearest jittered feature point
        /// in the surrounding 3x3x3 cells, remapped to [-1, 1]. The offset seeds the feature hash by
        /// its raw bit pattern rather than displacing the coordinate.
        /// </summary>
        public static float Worley3D(Vector3 position, float jitter, float offset)
        {
            var seed = BitConverter.SingleToUInt32Bits(offset);

            var ix = (int)MathF.Floor(position.X);
            var iy = (int)MathF.Floor(position.Y);
            var iz = (int)MathF.Floor(position.Z);

            var fx = position.X - ix;
            var fy = position.Y - iy;
            var fz = position.Z - iz;

            var nearest = float.MaxValue;

            for (var i = -1; i <= 1; i++)
            {
                var dx = i - fx;

                for (var j = -1; j <= 1; j++)
                {
                    var dy = j - fy;

                    for (var k = -1; k <= 1; k++)
                    {
                        var dz = k - fz;

                        uint h;
                        unchecked
                        {
                            h = HashSeeded((ix + i) + ((iy + j) << 10) + ((iz + k) << 20), seed);
                        }

                        var px = ((h & 255) * (1f / 255f) * jitter) + dx;
                        var py = (((h >> 8) & 255) * (1f / 255f) * jitter) + dy;
                        var pz = (((h >> 16) & 255) * (1f / 255f) * jitter) + dz;

                        nearest = MathF.Min(nearest, (pz * pz) + (py * py) + (px * px));
                    }
                }
            }

            return (MathF.Min(1f, MathF.Max(0f, nearest)) * 2f) - 1f;
        }

        /// <summary>
        /// Samples the cellular primitive's vector variant: the offset from the sample point to the
        /// nearest jittered feature point in the surrounding 3x3x3 cells, in lattice units. Ties keep
        /// the earlier cell. The offset seeds the feature hash by its raw bit pattern.
        /// </summary>
        public static Vector3 WorleyOffset3D(Vector3 position, float jitter, float offset)
        {
            var seed = BitConverter.SingleToUInt32Bits(offset);

            var ix = (int)MathF.Floor(position.X);
            var iy = (int)MathF.Floor(position.Y);
            var iz = (int)MathF.Floor(position.Z);

            var fx = position.X - ix;
            var fy = position.Y - iy;
            var fz = position.Z - iz;

            var nearest = float.MaxValue;
            var best = Vector3.Zero;

            for (var i = -1; i <= 1; i++)
            {
                var dx = i - fx;

                for (var j = -1; j <= 1; j++)
                {
                    var dy = j - fy;

                    for (var k = -1; k <= 1; k++)
                    {
                        var dz = k - fz;

                        uint h;
                        unchecked
                        {
                            h = HashSeeded((ix + i) + ((iy + j) << 10) + ((iz + k) << 20), seed);
                        }

                        var px = ((h & 255) * (1f / 255f) * jitter) + dx;
                        var py = (((h >> 8) & 255) * (1f / 255f) * jitter) + dy;
                        var pz = (((h >> 16) & 255) * (1f / 255f) * jitter) + dz;

                        var distance = (px * px) + (py * py) + (pz * pz);

                        if (distance < nearest)
                        {
                            best = new Vector3(px, py, pz);
                        }

                        nearest = MathF.Min(nearest, distance);
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// The feature point jitter the engine derives from the authored noise offset, in
        /// [0.75, 0.815]. The negative half of the remainder clamps to the 0.75 floor.
        /// </summary>
        public static float WorleyJitter(float noiseOffset)
        {
            var remainder = MathF.IEEERemainder(noiseOffset + 1.7233999967575073f, 0.6483259797096252f);

            return (MathF.Min(1f, MathF.Max(0f, remainder)) * 0.19999998807907104f) + 0.75f;
        }

        /// <summary>
        /// Samples the curl primitive: the curl combination of three decorrelated samples of a
        /// table-driven vector lattice. Scalar noise inputs read only the X component.
        /// </summary>
        public static Vector3 Curl3D(Vector3 position)
        {
            var first = CurlSample(position);
            var offsetPosition = position + new Vector3(43.256f, -67.89f, 1338.2f);
            var second = CurlSample(offsetPosition);
            var third = CurlSample(offsetPosition + new Vector3(-129.856f, -967.23f, 2338.98f));

            return new Vector3(third.Y - second.Z, first.Z - third.X, second.X - first.Y);
        }

        /// <summary>
        /// The vector lattice under the curl primitive: 8.8 fixed-point coordinates taken from the
        /// float bits after a +32768 bias (so the lattice repeats every 256 units and aliases outside
        /// the biased-positive range), three chained permutations, and unfaded trilinear blending.
        /// </summary>
        private static Vector3 CurlSample(Vector3 position)
        {
            var bx = BitConverter.SingleToUInt32Bits(position.X + 32768f) & 0xFFFFu;
            var by = BitConverter.SingleToUInt32Bits(position.Y + 32768f) & 0xFFFFu;
            var bz = BitConverter.SingleToUInt32Bits(position.Z + 32768f) & 0xFFFFu;

            var ix = (int)(bx >> 8);
            var iy = (int)(by >> 8);
            var iz = (int)(bz >> 8);

            var fx = (bx & 0xFF) * (1f / 256f);
            var fy = (by & 0xFF) * (1f / 256f);
            var fz = (bz & 0xFF) * (1f / 256f);

            var rowX0 = (NoiseTables.PermX[ix] + iy) & 0xFF;
            var rowX1 = (NoiseTables.PermX[(ix + 1) & 0xFF] + iy) & 0xFF;

            var cell00 = (NoiseTables.PermY[rowX0] + iz) & 0xFF;
            var cell01 = (NoiseTables.PermY[(rowX0 + 1) & 0xFF] + iz) & 0xFF;
            var cell10 = (NoiseTables.PermY[rowX1] + iz) & 0xFF;
            var cell11 = (NoiseTables.PermY[(rowX1 + 1) & 0xFF] + iz) & 0xFF;

            var x0 = Lerp(CurlVector(cell00), CurlVector(cell10), fx);
            var x1 = Lerp(CurlVector(cell01), CurlVector(cell11), fx);
            var x2 = Lerp(CurlVector((cell00 + 1) & 0xFF), CurlVector((cell10 + 1) & 0xFF), fx);
            var x3 = Lerp(CurlVector((cell01 + 1) & 0xFF), CurlVector((cell11 + 1) & 0xFF), fx);

            var y0 = Lerp(x0, x1, fy);
            var y1 = Lerp(x2, x3, fy);

            return Lerp(y0, y1, fz);
        }

        private static Vector3 CurlVector(int cell)
        {
            var index = NoiseTables.PermZ[cell] * 3;

            return new Vector3(NoiseTables.CurlVectors[index], NoiseTables.CurlVectors[index + 1], NoiseTables.CurlVectors[index + 2]);
        }

        /// <summary>Single-multiply blend; <see cref="Vector3.Lerp(Vector3, Vector3, float)"/> rounds differently.</summary>
        private static Vector3 Lerp(Vector3 from, Vector3 to, float amount) => ((to - from) * amount) + from;

        /// <summary>
        /// Hashes one lattice corner with a seed folded in mid-chain, as the gradient and cellular
        /// primitives do. Unlike <see cref="Hash"/> there is no second mixing round and the mask
        /// precedes the final multiply. The rotates come from sign-extending shifts, so a seed sign
        /// bit floods the rotated lanes.
        /// </summary>
        private static uint HashSeeded(int index, uint seed)
        {
            unchecked
            {
                var k = ((uint)index * 0xCC9E2D51u) & 0x7FFFFFFFu;
                k = ((BitOperations.RotateLeft(k, 15) * 0x1B873593u) & 0x7FFFFFFFu) ^ seed;
                k = ((uint)((int)k >> 19) | (k << 13)) & 0x7FFFFFFFu;

                var h = k * 0x04B2AE35u;

                return h ^ (uint)((int)h >> 16);
            }
        }
    }
}
