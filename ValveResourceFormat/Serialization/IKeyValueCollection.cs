using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace ValveResourceFormat.Serialization
{
    public interface IKeyValueCollection : IEnumerable<KeyValuePair<string, object>>
    {
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

        public static long GetIntegerProperty(this IKeyValueCollection collection, string name)
            => Convert.ToInt64(collection.GetProperty<object>(name));

        public static double GetDoubleProperty(this IKeyValueCollection collection, string name)
            => Convert.ToDouble(collection.GetProperty<object>(name));

        public static float GetFloatProperty(this IKeyValueCollection collection, string name)
            => (float)GetDoubleProperty(collection, name);

        public static long[] GetIntegerArray(this IKeyValueCollection collection, string name)
            => collection.GetArray<object>(name)
                .Select(Convert.ToInt64)
                .ToArray();

        public static IKeyValueCollection[] GetArray(this IKeyValueCollection collection, string name)
            => collection.GetArray<IKeyValueCollection>(name);

        public static Vector3 ToVector3(this IKeyValueCollection collection) => new Vector3(
            collection.GetFloatProperty("0"),
            collection.GetFloatProperty("1"),
            collection.GetFloatProperty("2"));

        public static Vector4 ToVector4(this IKeyValueCollection collection) => new Vector4(
            collection.GetFloatProperty("0"),
            collection.GetFloatProperty("1"),
            collection.GetFloatProperty("2"),
            collection.GetFloatProperty("3"));

        public static Quaternion ToQuaternion(this IKeyValueCollection collection) => new Quaternion(
            collection.GetFloatProperty("0"),
            collection.GetFloatProperty("1"),
            collection.GetFloatProperty("2"),
            collection.GetFloatProperty("3"));

        public static string Print(this IKeyValueCollection collection) => PrintHelper(collection, 0);

        private static string PrintHelper(IKeyValueCollection collection, int indent)
        {
            var stringBuilder = new StringBuilder();
            var space = new string(' ', indent * 4);
            foreach (var kvp in collection)
            {
                if (kvp.Value is IKeyValueCollection nestedCollection)
                {
                    stringBuilder.AppendLine($"{space}{kvp.Key} = {{");
                    stringBuilder.Append(PrintHelper(nestedCollection, indent + 1));
                    stringBuilder.AppendLine($"{space}}}");
                }
                else
                {
                    stringBuilder.AppendLine($"{space}{kvp.Key} = {kvp.Value}");
                }
            }

            return stringBuilder.ToString();
        }
    }
}
