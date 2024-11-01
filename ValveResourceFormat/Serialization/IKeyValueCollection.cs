using System.Globalization;
using System.Linq;
using System.Text;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Serialization
{
    public static class ResourceDataExtensions
    {
        public static KVObject AsKeyValueCollection(this ResourceData data) =>
            data switch
            {
                BinaryKV3 kv => kv.Data,
                NTRO ntro => ntro.Output,
                _ => throw new InvalidOperationException($"Cannot use {data.GetType().Name} as key-value collection")
            };
    }

    public static class KVObjectExtensions
    {
        public static KVObject GetSubCollection(this KVObject collection, string name)
            => collection.GetProperty<KVObject>(name);

        public static T[] GetArray<T>(this KVObject collection, string name, Func<KVObject, T> mapper)
            => collection.GetArray<KVObject>(name)
                .Select(mapper)
                .ToArray();

        public static string GetStringProperty(this KVObject collection, string name)
           => collection.GetProperty<string>(name);

        public static long GetIntegerProperty(this KVObject collection, string name)
            => Convert.ToInt64(collection.GetProperty<object>(name), CultureInfo.InvariantCulture);

        public static ulong GetUnsignedIntegerProperty(this KVObject collection, string name)
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

        public static int GetInt32Property(this KVObject collection, string name)
            => Convert.ToInt32(collection.GetProperty<object>(name), CultureInfo.InvariantCulture);

        public static uint GetUInt32Property(this KVObject collection, string name)
            => Convert.ToUInt32(collection.GetProperty<object>(name), CultureInfo.InvariantCulture);

        public static double GetDoubleProperty(this KVObject collection, string name)
            => Convert.ToDouble(collection.GetProperty<object>(name), CultureInfo.InvariantCulture);

        public static float GetFloatProperty(this KVObject collection, string name)
            => (float)GetDoubleProperty(collection, name);

        public static byte GetByteProperty(this KVObject collection, string name)
            => Convert.ToByte(collection.GetProperty<object>(name), CultureInfo.InvariantCulture);

        public static long[] GetIntegerArray(this KVObject collection, string name)
            => collection.GetArray<object>(name)
                .Select(x => Convert.ToInt64(x, CultureInfo.InvariantCulture))
                .ToArray();

        public static float[] GetFloatArray(this KVObject collection, string name)
            => collection.GetArray<object>(name)
                .Select(x => Convert.ToSingle(x, CultureInfo.InvariantCulture))
                .ToArray();

        public static ulong[] GetUnsignedIntegerArray(this KVObject collection, string name)
        {
            var array = collection.GetArray<object>(name);

            if (array.Length == 0)
            {
                return [];
            }

            if (array[0] is int)
            {
                return array.Select(x => unchecked((ulong)(int)x)).ToArray();
            }

            return array.Select(x => Convert.ToUInt64(x, CultureInfo.InvariantCulture)).ToArray();
        }

        public static KVObject[] GetArray(this KVObject collection, string name)
            => collection.GetArray<KVObject>(name);

        public static TEnum GetEnumValue<TEnum>(this KVObject collection, string name, bool normalize = false) where TEnum : Enum
        {
            var rawValue = collection.GetProperty<object>(name);

            if (rawValue is int)
            {
                return (TEnum)rawValue;
            }
            else if (rawValue is uint u) // NTRO byte enums are upconverted to uint
            {
                return (TEnum)(object)(int)u;
            }
            else if (rawValue is long l)
            {
                return (TEnum)(object)(int)l;
            }

            var strValue = (string)rawValue;

            // Normalize VALVE_ENUM_VALUE_1 to ValveEnum.Value1
            if (normalize)
            {
                var enumTypeName = typeof(TEnum).Name;
                const string FlagsSuffix = "Flags";
                if (enumTypeName.EndsWith(FlagsSuffix, StringComparison.Ordinal))
                {
                    enumTypeName = enumTypeName[..^FlagsSuffix.Length];
                }

                var sb = new StringBuilder(strValue.Length);
                var i = 0;
                var nextUpper = true;
                var startsWithEnumTypeName = true;

                foreach (var c in strValue)
                {
                    if (c == '_' || char.IsDigit(c))
                    {
                        nextUpper = true;
                        continue;
                    }

                    var cs = nextUpper ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c);
                    sb.Append(cs);

                    if (i < enumTypeName.Length && cs != enumTypeName[i])
                    {
                        startsWithEnumTypeName = false;
                    }

                    nextUpper = false;
                    i++;
                }

                if (startsWithEnumTypeName)
                {
                    sb.Remove(0, enumTypeName.Length);
                }

                strValue = sb.ToString();
            }

            if (Enum.TryParse(typeof(TEnum), strValue, false, out var value))
            {
                return (TEnum)value;
            }
            else
            {
                throw new ArgumentException($"Unable to map {strValue} to a member of enum {typeof(TEnum).Name}");
            }
        }

        public static bool IsNotBlobType(this KVObject collection, string key)
            => collection.Properties[key].Type == KVType.ARRAY;

        public static Vector2 ToVector2(this KVObject collection) => new(
            collection.GetFloatProperty("0"),
            collection.GetFloatProperty("1"));

        public static Vector3 ToVector3(this KVObject collection) => new(
            collection.GetFloatProperty("0"),
            collection.GetFloatProperty("1"),
            collection.GetFloatProperty("2"));

        public static Vector4 ToVector4(this KVObject collection) => new(
            collection.GetFloatProperty("0"),
            collection.GetFloatProperty("1"),
            collection.GetFloatProperty("2"),
            collection.GetFloatProperty("3"));

        public static Quaternion ToQuaternion(this KVObject collection) => new(
            collection.GetFloatProperty("0"),
            collection.GetFloatProperty("1"),
            collection.GetFloatProperty("2"),
            collection.GetFloatProperty("3"));

        public static Matrix4x4 ToMatrix4x4(this KVObject[] array)
        {
            var column1 = array[0].ToVector4();
            var column2 = array[1].ToVector4();
            var column3 = array[2].ToVector4();
            var column4 = array.Length > 3 ? array[3].ToVector4() : new Vector4(0, 0, 0, 1);

            return new Matrix4x4(column1.X, column2.X, column3.X, column4.X, column1.Y, column2.Y, column3.Y, column4.Y, column1.Z, column2.Z, column3.Z, column4.Z, column1.W, column2.W, column3.W, column4.W);
        }

        public static Matrix4x4 ToMatrix4x4(this KVObject array)
        {
            var column4 = array.Count > 12
                ? new Vector4(array.GetFloatProperty("12"), array.GetFloatProperty("13"), array.GetFloatProperty("14"), array.GetFloatProperty("15"))
                : new Vector4(0, 0, 0, 1);
            return new Matrix4x4(
                array.GetFloatProperty("0"), array.GetFloatProperty("4"), array.GetFloatProperty("8"), column4.X,
                array.GetFloatProperty("1"), array.GetFloatProperty("5"), array.GetFloatProperty("9"), column4.Y,
                array.GetFloatProperty("2"), array.GetFloatProperty("6"), array.GetFloatProperty("10"), column4.Z,
                array.GetFloatProperty("3"), array.GetFloatProperty("7"), array.GetFloatProperty("11"), column4.W
            );
        }
    }
}
