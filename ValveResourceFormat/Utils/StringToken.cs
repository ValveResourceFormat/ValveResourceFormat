using System.Collections.Concurrent;
using ValveResourceFormat.ThirdParty;

namespace ValveResourceFormat.Utils
{
    public static class StringToken
    {
        private static readonly string ProductVersionString = typeof(StringToken).Assembly.GetName().Version.ToString();
        public static readonly string VRF_GENERATOR = $"Source 2 Viewer {ProductVersionString} - https://valveresourceformat.github.io";

        public const uint MURMUR2SEED = 0x31415926; // It's pi!

        public static readonly ConcurrentDictionary<uint, string> InvertedTable = new(InitializeLookup().TokenToString);

        public static uint Get(string key) => MurmurHash2.Hash(key, MURMUR2SEED);

        public static uint Store(string key)
        {
            var token = Get(key);

            InvertedTable.TryAdd(token, key);
            return token;
        }

        public static void Store(IEnumerable<string> keys)
        {
            foreach (var key in keys)
            {
                Store(key);
            }
        }

        internal static (Dictionary<uint, string> TokenToString, Dictionary<string, uint> StringToToken) InitializeLookup()
        {
            var tokenToString = new Dictionary<uint, string>(EntityLumpKnownKeys.KnownKeys.Length);
            var stringToToken = new Dictionary<string, uint>(EntityLumpKnownKeys.KnownKeys.Length);

            foreach (var key in EntityLumpKnownKeys.KnownKeys)
            {
                var token = MurmurHash2.Hash(key, MURMUR2SEED);

                tokenToString.Add(token, key);
                stringToToken.Add(key, token);
            }

            return (tokenToString, stringToToken);
        }
    }
}
