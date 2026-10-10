using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.AnimLib
{
    static class KVObjectAnimExtensions
    {
        public static GlobalSymbol[] GetSymbolArray(this KVObject collection, string name)
        {
            // Missing on resources compiled before the field was added
            return [.. (collection.GetArray<string>(name) ?? []).Select(s => new GlobalSymbol(s))];
        }

        /// <summary>Parses an 8-float KV3 array property (position, scale, rotation) into a transform.</summary>
        public static Transform GetTransformProperty(this KVObject collection, string name)
        {
            var data = collection.GetProperty<KVObject>(name);
            if (data == null)
            {
                return Transform.Identity;
            }

            var (position, scale, rotation) = data.ToTransform();
            return new Transform(position, scale, rotation);
        }

        /// <summary>Parses an array of 8-float KV3 arrays (position, scale, rotation) into transforms.</summary>
        public static Transform[] GetTransformArray(this KVObject collection, string name)
        {
            var outer = collection.GetArray(name);
            if (outer == null)
            {
                return [];
            }

            var result = new Transform[outer.Count];
            for (var i = 0; i < result.Length; i++)
            {
                var (position, scale, rotation) = outer[i].ToTransform();
                result[i] = new Transform(position, scale, rotation);
            }

            return result;
        }
    }
}
