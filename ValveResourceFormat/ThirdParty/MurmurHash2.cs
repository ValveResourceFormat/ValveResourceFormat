using System.Buffers;

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
        /// Computes a case-insensitive MurmurHash2 hash for the given string.
        /// </summary>
        /// <param name="data">The string to hash.</param>
        /// <param name="seed">The hash seed.</param>
        /// <returns>The hash value.</returns>
        public static uint Hash(string data, uint seed) => Hash(data.AsSpan(), seed);

        /// <summary>
        /// Computes a case-insensitive MurmurHash2 hash for the given string.
        /// </summary>
        /// <param name="data">The string to hash.</param>
        /// <param name="seed">The hash seed.</param>
        /// <returns>The hash value.</returns>
        public static uint Hash(ReadOnlySpan<char> data, uint seed)
        {
            if (data.Length < 256)
            {
                Span<char> lowerData = stackalloc char[data.Length];

                for (var i = 0; i < data.Length; i++)
                {
                    var c = data[i];
                    lowerData[i] = c is >= 'A' and <= 'Z' ? (char)(c | 0x20) : c;
                }

                return HashCaseSensitive(lowerData, seed);
            }

            var lowercase = ArrayPool<char>.Shared.Rent(data.Length);
            try
            {
                MemoryExtensions.ToLowerInvariant(data, lowercase);
                return HashCaseSensitive(lowercase.AsSpan(0, data.Length), seed);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(lowercase);
            }
        }

        /// <summary>
        /// Computes a case-sensitive MurmurHash2 hash for the given string.
        /// </summary>
        public static uint HashCaseSensitive(string data, uint seed) => HashCaseSensitive(data.AsSpan(), seed);

        /// <summary>
        /// Computes a case sensitive MurmurHash2 hash for the given character span.
        /// </summary>
        /// <param name="data">The character span to hash.</param>
        /// <param name="seed">The hash seed.</param>
        /// <returns>The hash value.</returns>
        public static uint HashCaseSensitive(ReadOnlySpan<char> data, uint seed)
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
