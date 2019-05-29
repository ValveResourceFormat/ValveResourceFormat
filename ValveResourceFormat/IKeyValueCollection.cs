using System;
using System.Collections.Generic;
using System.Linq;

namespace ValveResourceFormat
{
    public interface IKeyValueCollection
    {
        IEnumerable<string> Keys { get; }

        bool ContainsKey(string name);

        T[] GetArray<T>(string name);

        T GetProperty<T>(string name);
    }

    public static class IKeyValueCollectionExtensions
    {
        public static IKeyValueCollection GetSubCollection(this IKeyValueCollection collection, string name)
            => collection.GetProperty<IKeyValueCollection>(name);

        public static T[] GetArray<T>(this IKeyValueCollection collection, string name, Func<IKeyValueCollection, T> mapper)
            => collection.GetArray<IKeyValueCollection>(name)
                .Select(mapper)
                .ToArray();
    }
}
