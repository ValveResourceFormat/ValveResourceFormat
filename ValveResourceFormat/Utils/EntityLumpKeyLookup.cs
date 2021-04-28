using System.Collections.Concurrent;
using ValveResourceFormat.ThirdParty;

namespace ValveResourceFormat.Utils
{
    public static class EntityLumpKeyLookup
    {
        public const uint MURMUR2SEED = 0x31415926; // It's pi!

        private static readonly ConcurrentDictionary<string, uint> Lookup = new();

        public static uint Get(string key)
        {
            return Lookup.GetOrAdd(key, s => MurmurHash2.Hash(s, MURMUR2SEED));
        }
    }
}
