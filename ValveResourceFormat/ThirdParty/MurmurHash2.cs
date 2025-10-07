namespace ValveResourceFormat.ThirdParty
{
    /// <summary>
    /// Provides MurmurHash2 hashing algorithm implementation.
    /// </summary>
    public static class MurmurHash2
    {
        private const uint M = 0x5bd1e995;
        private const int R = 24;

        /// <summary>
        /// Computes a MurmurHash2 hash for the given string.
        /// </summary>
        /// <param name="data">The string to hash.</param>
        /// <param name="seed">The hash seed.</param>
        /// <returns>The hash value.</returns>
        public static uint Hash(string data, uint seed) => Hash(data.AsSpan(), seed);

        /// <summary>
        /// Computes a MurmurHash2 hash for the given character span.
        /// </summary>
        /// <param name="data">The character span to hash.</param>
        /// <param name="seed">The hash seed.</param>
        /// <returns>The hash value.</returns>
        public static uint Hash(ReadOnlySpan<char> data, uint seed)
        {
            var length = data.Length;

            if (length == 0)
            {
                return 0;
            }

            var h = seed ^ (uint)length;
            var currentIndex = 0;
            while (length >= 4)
            {
                var k = (uint)(data[currentIndex++] | data[currentIndex++] << 8 | data[currentIndex++] << 16 | data[currentIndex++] << 24);
                k *= M;
                k ^= k >> R;
                k *= M;

                h *= M;
                h ^= k;
                length -= 4;
            }

            switch (length)
            {
                case 3:
                    h ^= (ushort)(data[currentIndex++] | data[currentIndex++] << 8);
                    h ^= (uint)(data[currentIndex] << 16);
                    h *= M;
                    break;
                case 2:
                    h ^= (ushort)(data[currentIndex++] | data[currentIndex] << 8);
                    h *= M;
                    break;
                case 1:
                    h ^= data[currentIndex];
                    h *= M;
                    break;
                default:
                    break;
            }

            h ^= h >> 13;
            h *= M;
            h ^= h >> 15;

            return h;
        }
    }
}
