using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Utils
{
    /// <summary>
    /// String token, also known as global symbol, is a 32-bit hash of a string.
    /// The string can be interned into a global table, for later retrieval.
    /// </summary>
    public readonly struct StringToken : IEquatable<StringToken>
    {
        /// <summary>The name the token was hashed from, empty when unset.</summary>
        public string Name => Token == 0 ? string.Empty : StringToken.GetKnownString(Token);
        /// <summary>The hashed token.</summary>
        public uint Token { get; }

        /// <summary>Hashes and records a name. An empty name is unset.</summary>
        public StringToken(string name)
        {
            // An empty name is the unset symbol rather than the hash of nothing
            Token = name.Length == 0 ? 0 : StringToken.Store(name);
        }

        /// <summary>Hashes a name without recording it, so it neither allocates nor touches the shared table. An empty name is unset.</summary>
        public static StringToken Lookup(ReadOnlySpan<char> name) => name.IsEmpty ? default : new(StringToken.Get(name), allowUnknown: true);

        /// <summary>Wraps a token, which must be known unless allowed otherwise.</summary>
        public StringToken(uint token, bool allowUnknown = false)
        {
            Debug.Assert(
                allowUnknown || StringToken.InvertedTable.ContainsKey(token),
                $"Unknown symbol {token}."
            );

            Token = token;
        }

        /// <summary>Whether this symbol holds a stored token, as opposed to being unset.</summary>
        public bool IsValid => Token != 0;

        /// <inheritdoc/>
        public override readonly string ToString() => Name;

        /// <summary>Gets the token.</summary>
        public static implicit operator uint(StringToken symbol) => symbol.Token;
        /// <summary>Wraps a token, known or not.</summary>
        public static implicit operator StringToken(uint token) => new(token, allowUnknown: true);
        /// <summary>Hashes and records a name.</summary>
        public static implicit operator StringToken(string name) => new(name);

        /// <summary>Gets the token.</summary>
        public uint ToUInt32() => Token;

        /// <summary>Wraps a token, known or not.</summary>
        public static StringToken FromUInt32(uint token) => new(token, allowUnknown: true);

        /// <summary>Hashes and records a name.</summary>
        public static StringToken FromString(string name) => new(name);

        /// <inheritdoc/>
        public bool Equals(StringToken other) => Token == other.Token;
        /// <inheritdoc/>
        public override int GetHashCode() => (int)Token;

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            Debug.Assert(false, $"Boxing operation on {nameof(StringToken)}. Can you avoid this?");
            return obj is StringToken other && Equals(other);
        }

        /// <summary>Whether two symbols have the same token.</summary>
        public static bool operator ==(StringToken left, StringToken right) => left.Equals(right);
        /// <summary>Whether two symbols have different tokens.</summary>
        public static bool operator !=(StringToken left, StringToken right) => !left.Equals(right);

#if VRF_NO_GENERATOR_VERSION
        // Use the following command to avoid putting version in the dumps for stable git tracking of dumped files
        // dotnet build -p:DefineConstants=VRF_NO_GENERATOR_VERSION
        /// <summary>
        /// Generator identification string.
        /// </summary>
        public const string VRF_GENERATOR = $"Source 2 Viewer - https://valveresourceformat.github.io";
#else
        private static readonly string ProductVersionString = typeof(StringToken).Assembly.GetName().Version!.ToString();

        /// <summary>
        /// Generator identification string with version.
        /// </summary>
        public static readonly string VRF_GENERATOR = $"Source 2 Viewer {ProductVersionString} - https://valveresourceformat.github.io";
#endif

        private const uint Seed = 0x31415926; // It's pi!
        private const uint M = 0x5bd1e995;
        private const int R = 24;

        /// <summary>
        /// Gets the inverted table for hash to string lookups.
        /// </summary>
        public static ConcurrentDictionary<uint, string> InvertedTable => KnownKeysTable.Table;

        /// <summary>
        /// Computes the hash token for the given string, case insensitive.
        /// Only ASCII letters are case folded, the string is hashed as UTF-8.
        /// </summary>
        /// <param name="key">The string to hash.</param>
        /// <returns>The hash token.</returns>
        public static uint Get(ReadOnlySpan<char> key)
        {
            if (key.IsEmpty)
            {
                return 0;
            }

            // While the chars are ASCII they are their own UTF-8 bytes, so hash them without transcoding
            ref var chars = ref Unsafe.As<char, ushort>(ref MemoryMarshal.GetReference(key));
            var hash = Seed ^ (uint)key.Length;
            var i = 0;

            for (; i <= key.Length - 8; i += 8)
            {
                var block = Vector128.LoadUnsafe(ref chars, (nuint)i);

                if ((block & Vector128.Create((ushort)0xFF80)) != Vector128<ushort>.Zero)
                {
                    return GetNonAscii(key);
                }

                var lower = ToLowerAscii(Vector128.Narrow(block, block)).AsUInt64().ToScalar();
                hash = Mix(Mix(hash, (uint)lower), (uint)(lower >> 32));
            }

            if (i <= key.Length - 4)
            {
                var block = Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<ushort, byte>(ref Unsafe.Add(ref chars, i)));

                if ((block & 0xFF80FF80FF80FF80) != 0)
                {
                    return GetNonAscii(key);
                }

                // Pack the low byte of each of the four chars into one uint
                block |= block >> 8;
                hash = Mix(hash, ToLowerAscii((uint)(block & 0xFFFF) | ((uint)(block >> 16) & 0xFFFF0000)));
                i += 4;
            }

            var tail = 0u;
            var tailChars = 0;

            for (var j = key.Length - 1; j >= i; j--)
            {
                tail = (tail << 8) | key[j];
                tailChars |= key[j];
            }

            if (tailChars >= 0x80)
            {
                return GetNonAscii(key);
            }

            return MixTail(hash, tail, key.Length - i);
        }

        // Non-ASCII chars are hashed as UTF-8 bytes, only ASCII letters are folded
        private static uint GetNonAscii(ReadOnlySpan<char> key)
        {
            using var rented = new RentedBuffer<byte>(Encoding.UTF8.GetByteCount(key));
            var utf8 = rented.Span;
            Encoding.UTF8.GetBytes(key, utf8);

            var hash = Seed ^ (uint)utf8.Length;
            var i = 0;

            for (; i <= utf8.Length - 4; i += 4)
            {
                hash = Mix(hash, ToLowerAscii(BinaryPrimitives.ReadUInt32LittleEndian(utf8[i..])));
            }

            var tail = 0u;

            for (var j = utf8.Length - 1; j >= i; j--)
            {
                tail = (tail << 8) | utf8[j];
            }

            return MixTail(hash, tail, utf8.Length - i);
        }

        /// <summary>
        /// Gets a known string for the given <paramref name="hash"/>, or returns an unknown key placeholder.
        /// </summary>
        /// <param name="hash">The hash to look up.</param>
        /// <returns>The known string or a placeholder.</returns>
        public static string GetKnownString(uint hash)
        {
            if (InvertedTable.TryGetValue(hash, out var knownString))
            {
                return knownString;
            }

            return $"vrf_unknown_key_{hash}";
        }

        /// <summary>
        /// Store a string to the table of known string hashes, so it can later be retrieved using <see cref="GetKnownString"/>.
        /// </summary>
        public static uint Store(ReadOnlySpan<char> key)
        {
            var token = Get(key);

            // The first spelling stored for a token wins, so known keys keep their casing.
            // Checked first to only allocate the string for new tokens.
            if (!InvertedTable.ContainsKey(token))
            {
                InvertedTable.TryAdd(token, key.ToString());
            }

            return token;
        }

        internal static Dictionary<uint, string> InitializeInverseLookup()
        {
            var inverseLookup = new Dictionary<uint, string>(EntityLumpKnownKeys.KnownKeys.Length);

            foreach (var key in EntityLumpKnownKeys.KnownKeys)
            {
                var token = Get(key);
                inverseLookup.Add(token, key);
            }

            return inverseLookup;
        }

        private static class KnownKeysTable
        {
            public static readonly ConcurrentDictionary<uint, string> Table = new(InitializeInverseLookup());
        }

        // MurmurHash2 step for four little-endian bytes. Blocks are mixed strictly in order,
        // so vectors can only speed up the case folding, not the hash itself.
        private static uint Mix(uint hash, uint block)
        {
            var k = block * M;
            k ^= k >> R;
            k *= M;

            return (hash * M) ^ k;
        }

        private static uint MixTail(uint hash, uint tail, int tailLength)
        {
            if (tailLength > 0)
            {
                hash = (hash ^ ToLowerAscii(tail)) * M;
            }

            hash ^= hash >> 13;
            hash *= M;
            hash ^= hash >> 15;

            return hash;
        }

        // Folds 'A'-'Z' in each of the four packed bytes at once, other bytes (including non-ASCII) are kept.
        // The high bit of each lane of lowBits + (0x80 - c) is set when the 7-bit value is at least c.
        private static uint ToLowerAscii(uint packed)
        {
            var lowBits = packed & 0x7F7F7F7Fu;
            var atLeastA = lowBits + 0x3F3F3F3Fu;
            var aboveZ = lowBits + 0x25252525u;
            var isUpper = (atLeastA ^ aboveZ) & ~packed & 0x80808080u;

            return packed | (isUpper >> 2);
        }

        private static Vector128<byte> ToLowerAscii(Vector128<byte> bytes)
        {
            var isUpper = Vector128.LessThan(bytes - Vector128.Create((byte)'A'), Vector128.Create((byte)26));

            return bytes | (isUpper & Vector128.Create((byte)0x20));
        }
    }
}
