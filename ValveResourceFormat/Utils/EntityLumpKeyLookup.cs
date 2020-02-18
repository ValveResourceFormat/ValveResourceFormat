using System.Collections.Generic;
using ValveResourceFormat.ThirdParty;

namespace ValveResourceFormat.Utils
{
    public static class EntityLumpKeyLookup
    {
        public const uint MURMUR2SEED = 0x31415926; // It's pi!

        private static Dictionary<string, uint> Lookup = new Dictionary<string, uint>();

        public static uint Get(string key)
        {
            if (Lookup.ContainsKey(key))
            {
                return Lookup[key];
            }

            var hash = MurmurHash2.Hash(key, MURMUR2SEED);
            Lookup[key] = hash;

            return hash;
        }
    }
}
