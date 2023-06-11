using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Serialization.NTRO;

namespace ValveResourceFormat.Serialization
{
    public interface IKeyValueCollection : IEnumerable<KeyValuePair<string, object>>
    {
        bool ContainsKey(string name);

        T[] GetArray<T>(string name);

        T GetProperty<T>(string name);
    }

    public static class ResourceDataExtensions
    {
        public static IKeyValueCollection AsKeyValueCollection(this ResourceData data) =>
            data switch
            {
                BinaryKV3 kv => kv.Data,
                ResourceTypes.NTRO ntro => ntro.Output,
                _ => throw new InvalidOperationException($"Cannot use {data.GetType().Name} as key-value collection")
            };
    }

    public static class IKeyValueCollectionExtensions
    {
        public static IKeyValueCollection GetSubCollection(this IKeyValueCollection collection, string name)
            => collection.GetProperty<IKeyValueCollection>(name);

        public static T[] GetArray<T>(this IKeyValueCollection collection, string name, Func<IKeyValueCollection, T> mapper)
            => collection.GetArray<IKeyValueCollection>(name)
                .Select(mapper)
                .ToArray();

        public static string GetStringProperty(this IKeyValueCollection collection, string name)
           => collection.GetProperty<string>(name);

        public static long GetIntegerProperty(this IKeyValueCollection collection, string name)
            => Convert.ToInt64(collection.GetProperty<object>(name), CultureInfo.InvariantCulture);

        public static ulong GetUnsignedIntegerProperty(this IKeyValueCollection collection, string name)
        {
            var value = collection.GetProperty<object>(name);

            if (value is int i)
            {
                unchecked
                {
                    return (ulong)i;
                }
            }

            return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }

        public static int GetInt32Property(this IKeyValueCollection collection, string name)
            => Convert.ToInt32(collection.GetProperty<object>(name), CultureInfo.InvariantCulture);

        public static uint GetUInt32Property(this IKeyValueCollection collection, string name)
            => Convert.ToUInt32(collection.GetProperty<object>(name), CultureInfo.InvariantCulture);

        public static double GetDoubleProperty(this IKeyValueCollection collection, string name)
            => Convert.ToDouble(collection.GetProperty<object>(name), CultureInfo.InvariantCulture);

        public static float GetFloatProperty(this IKeyValueCollection collection, string name)
            => (float)GetDoubleProperty(collection, name);

        public static byte GetByteProperty(this IKeyValueCollection collection, string name)
            => Convert.ToByte(collection.GetProperty<object>(name), CultureInfo.InvariantCulture);

        public static long[] GetIntegerArray(this IKeyValueCollection collection, string name)
            => collection.GetArray<object>(name)
                .Select(x => Convert.ToInt64(x, CultureInfo.InvariantCulture))
                .ToArray();

        public static float[] GetFloatArray(this IKeyValueCollection collection, string name)
            => collection.GetArray<object>(name)
                .Select(x => Convert.ToSingle(x, CultureInfo.InvariantCulture))
                .ToArray();

        public static ulong[] GetUnsignedIntegerArray(this IKeyValueCollection collection, string name)
            => collection.GetArray<object>(name)
                .Select(x => Convert.ToUInt64(x, CultureInfo.InvariantCulture))
                .ToArray();

        public static IKeyValueCollection[] GetArray(this IKeyValueCollection collection, string name)
            => collection.GetArray<IKeyValueCollection>(name);

        public static TEnum GetEnumValue<TEnum>(this IKeyValueCollection collection, string name) where TEnum : Enum
        {
            var strValue = collection.GetProperty<string>(name);
            if (Enum.TryParse(typeof(TEnum), strValue, false, out var value))
            {
                return (TEnum)value;
            }
            else
            {
                throw new ArgumentException($"Unable to map {strValue} to a member of enum {typeof(TEnum).Name}");
            }
        }

        public static bool IsNotBlobType(this IKeyValueCollection collection, string key)
            => collection is NTROStruct || ((KVObject)collection).Properties[key].Type == KVType.ARRAY;

        public static Vector3 ToVector3(this IKeyValueCollection collection) => new(
            collection.GetFloatProperty("0"),
            collection.GetFloatProperty("1"),
            collection.GetFloatProperty("2"));

        public static Vector4 ToVector4(this IKeyValueCollection collection) => new(
            collection.GetFloatProperty("0"),
            collection.GetFloatProperty("1"),
            collection.GetFloatProperty("2"),
            collection.GetFloatProperty("3"));

        public static Quaternion ToQuaternion(this IKeyValueCollection collection) => new(
            collection.GetFloatProperty("0"),
            collection.GetFloatProperty("1"),
            collection.GetFloatProperty("2"),
            collection.GetFloatProperty("3"));

        public static Matrix4x4 ToMatrix4x4(this IKeyValueCollection[] array)
        {
            var column1 = array[0].ToVector4();
            var column2 = array[1].ToVector4();
            var column3 = array[2].ToVector4();
            var column4 = array.Length > 3 ? array[3].ToVector4() : new Vector4(0, 0, 0, 1);

            return new Matrix4x4(column1.X, column2.X, column3.X, column4.X, column1.Y, column2.Y, column3.Y, column4.Y, column1.Z, column2.Z, column3.Z, column4.Z, column1.W, column2.W, column3.W, column4.W);
        }

        public static Matrix4x4 ToMatrix4x4(this IKeyValueCollection array)
        {
            var column4 = array.Count() > 12
                ? new Vector4(array.GetFloatProperty("12"), array.GetFloatProperty("13"), array.GetFloatProperty("14"), array.GetFloatProperty("15"))
                : new Vector4(0, 0, 0, 1);
            return new Matrix4x4(
                array.GetFloatProperty("0"), array.GetFloatProperty("4"), array.GetFloatProperty("8"), column4.X,
                array.GetFloatProperty("1"), array.GetFloatProperty("5"), array.GetFloatProperty("9"), column4.Y,
                array.GetFloatProperty("2"), array.GetFloatProperty("6"), array.GetFloatProperty("10"), column4.Z,
                array.GetFloatProperty("3"), array.GetFloatProperty("7"), array.GetFloatProperty("11"), column4.W
            );
        }

        public static string Print(this IKeyValueCollection collection) => PrintHelper(collection, 0);

        private static string PrintHelper(IKeyValueCollection collection, int indent)
        {
            var stringBuilder = new StringBuilder();
            var space = new string(' ', indent * 4);
            foreach (var kvp in collection)
            {
                if (kvp.Value is IKeyValueCollection nestedCollection)
                {
                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"{space}{kvp.Key} = {{");
                    stringBuilder.Append(PrintHelper(nestedCollection, indent + 1));
                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"{space}}}");
                }
                else
                {
                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"{space}{kvp.Key} = {kvp.Value}");
                }
            }

            return stringBuilder.ToString();
        }
    }
}
