using System.Collections.Concurrent;
using ValveResourceFormat.ThirdParty;

namespace ValveResourceFormat.Utils
{
    public static class StringToken
    {
#if VRF_NO_GENERATOR_VERSION
        // Use the following command to avoid putting version in the dumps for stable git tracking of dumped files
        // dotnet build -p:DefineConstants=VRF_NO_GENERATOR_VERSION
        public const string VRF_GENERATOR = $"Source 2 Viewer - https://valveresourceformat.github.io";
#else
        private static readonly string ProductVersionString = typeof(StringToken).Assembly.GetName().Version.ToString();
        public static readonly string VRF_GENERATOR = $"Source 2 Viewer {ProductVersionString} - https://valveresourceformat.github.io";
#endif

        public const uint MURMUR2SEED = 0x31415926; // It's pi!

        public static readonly ConcurrentDictionary<uint, string> InvertedTable = new(InitializeInverseLookup());

        public static uint Get(string key) => MurmurHash2.Hash(key, MURMUR2SEED);

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
        public static uint Store(string key)
        {
            var token = Get(key.ToLowerInvariant());

            InvertedTable[token] = key;
            return token;
        }

        /// <summary>
        /// Store a number of strings to the table of known string hashes, so they can later be retrieved using <see cref="GetKnownString"/>.
        /// </summary>
        public static void Store(IEnumerable<string> keys)
        {
            foreach (var key in keys)
            {
                Store(key);
            }
        }

        internal static Dictionary<uint, string> InitializeInverseLookup()
        {
            var inverseLookup = new Dictionary<uint, string>(EntityLumpKnownKeys.KnownKeys.Length);

            foreach (var key in EntityLumpKnownKeys.KnownKeys)
            {
                var token = MurmurHash2.Hash(key, MURMUR2SEED);
                inverseLookup.Add(token, key);
            }

            return inverseLookup;
        }
    }
}
