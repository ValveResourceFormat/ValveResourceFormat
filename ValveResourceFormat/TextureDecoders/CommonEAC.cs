namespace ValveResourceFormat.TextureDecoders
{
    internal static class CommonEAC
    {
        public static readonly sbyte[,] ModifierTable =
        {
            {-3, -6,  -9, -15, 2, 5, 8, 14},
            {-3, -7, -10, -13, 2, 6, 9, 12},
            {-2, -5,  -8, -13, 1, 4, 7, 12},
            {-2, -4,  -6, -13, 1, 3, 5, 12},
            {-3, -6,  -8, -12, 2, 5, 7, 11},
            {-3, -7,  -9, -11, 2, 6, 8, 10},
            {-4, -7,  -8, -11, 3, 6, 7, 10},
            {-3, -5,  -8, -11, 2, 4, 7, 10},
            {-2, -6,  -8, -10, 1, 5, 7,  9},
            {-2, -5,  -8, -10, 1, 4, 7,  9},
            {-2, -4,  -8, -10, 1, 3, 7,  9},
            {-2, -5,  -7, -10, 1, 4, 6,  9},
            {-3, -4,  -7, -10, 2, 3, 6,  9},
            {-1, -2,  -3, -10, 0, 1, 2,  9},
            {-4, -6,  -8,  -9, 3, 5, 7,  8},
            {-3, -5,  -7,  -9, 2, 4, 6,  8}
        };

        /// <summary>
        /// Decodes one 8 byte unsigned 11-bit EAC block into 16 single channel bytes, in row major order.
        /// </summary>
        /// <param name="block">The 8 bytes of the block.</param>
        /// <param name="output">Receives 16 bytes, the texel at (x, y) at index y * 4 + x.</param>
        public static void DecodeBlock(ReadOnlySpan<byte> block, Span<byte> output)
        {
            int baseCodeword = block[0];
            var multiplier = block[1] >> 4;
            var table = block[1] & 0xF;
            var step = multiplier == 0 ? 1 : multiplier * 8;

            var indices = (ulong)block[2] << 40 | (ulong)block[3] << 32 | (ulong)block[4] << 24
                | (ulong)block[5] << 16 | (ulong)block[6] << 8 | block[7];

            for (var i = 0; i < 16; i++)
            {
                var index = (int)(indices >> (45 - i * 3)) & 7;
                var value = Math.Clamp(baseCodeword * 8 + 4 + ModifierTable[table, index] * step, 0, 2047);

                output[(i & 3) * 4 + (i >> 2)] = ToUnorm8(value);
            }
        }

        /// <summary>
        /// Converts an 11-bit unsigned value to 8-bit using the same 1/2047 scale a sampler applies.
        /// </summary>
        /// <param name="value">The 11-bit value.</param>
        /// <returns>The 8-bit value.</returns>
        public static byte ToUnorm8(int value) => (byte)((value * 255 + 1023) / 2047);
    }
}
