using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ValveResourceFormat
{
    public interface IKeyValueCollection
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
    }
}
