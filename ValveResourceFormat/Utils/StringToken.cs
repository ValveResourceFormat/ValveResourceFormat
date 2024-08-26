using System.Collections.Concurrent;
using ValveResourceFormat.ThirdParty;

namespace ValveResourceFormat.Utils
{
    public static class StringToken
    {
        private static readonly string ProductVersionString = typeof(StringToken).Assembly.GetName().Version.ToString();
        public static readonly string VRF_GENERATOR = $"Source 2 Viewer {ProductVersionString} - https://valveresourceformat.github.io";

        public const uint MURMUR2SEED = 0x31415926; // It's pi!

        internal static readonly ConcurrentDictionary<string, uint> Lookup = new(InitializeLookup());

        public static Dictionary<uint, string> InvertedTable
        {
            get
            {
                var inverted = new Dictionary<uint, string>(Lookup.Count);

                foreach (var (key, hash) in Lookup)
                {
                    inverted[hash] = key;
                }

                return inverted;
            }
        }

        public static uint Get(string key)
        {
            return Lookup.GetOrAdd(key, s =>
            {
#if DEBUG
                Console.WriteLine($"New string: {s}");
#endif

                return MurmurHash2.Hash(s, MURMUR2SEED);
            });
        }

        private static Dictionary<string, uint> InitializeLookup()
        {
            var dictionary = new Dictionary<string, uint>(EntityLumpKnownKeys.KnownKeys.Length);

            foreach (var key in EntityLumpKnownKeys.KnownKeys)
            {
                dictionary.Add(key, MurmurHash2.Hash(key, MURMUR2SEED));
            }

            return dictionary;
        }
    }
}
