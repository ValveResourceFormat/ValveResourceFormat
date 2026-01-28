using System.Globalization;
using System.Linq;
using System.Text;
using ValveResourceFormat.ResourceTypes;
using KVValueType = ValveKeyValue.KVValueType;

namespace ValveResourceFormat.Serialization.KeyValues
{
    /// <summary>
    /// Extension methods for resource data blocks.
    /// </summary>
    public static class ResourceDataExtensions
    {
        /// <summary>
        /// Converts a resource data block to a key-value collection.
        /// </summary>
        public static KVObject AsKeyValueCollection(this Block data) =>
            data switch
            {
                BinaryKV3 kv => kv.Data,
                NTRO ntro => ntro.Output,
                _ => throw new InvalidOperationException($"Cannot use {data.GetType().Name} as key-value collection")
            };
    }

    /// <summary>
    /// Extension methods for KVObject.
    /// </summary>
    public static class KVObjectExtensions
    {
        /// <summary>
        /// Gets a sub-collection from the key-value object.
        /// </summary>
        public static KVObject GetSubCollection(this KVObject collection, string name)
            => collection.GetProperty<KVObject>(name);

        /// <summary>
        /// Gets an array from the key-value object and maps each element.
        /// </summary>
        public static T[] GetArray<T>(this KVObject collection, string name, Func<KVObject, T> mapper)
            => collection.GetArray<KVObject>(name)
                .Select(mapper)
                .ToArray();

        /// <summary>
        /// Gets a string property from the key-value object.
        /// </summary>
        public static string GetStringProperty(this KVObject collection, string name)
           => collection.GetProperty<string>(name);

        /// <summary>
        /// Gets an integer property from the key-value object.
        /// </summary>
        public static long GetIntegerProperty(this KVObject collection, string name)
            => Convert.ToInt64(collection.GetProperty<object>(name), CultureInfo.InvariantCulture);

        /// <summary>
        /// Gets an unsigned integer property from the key-value object.
        /// </summary>
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

        /// <summary>
        /// Gets an Int32 property from the key-value object.
        /// </summary>
        public static int GetInt32Property(this KVObject collection, string name)
            => Convert.ToInt32(collection.GetProperty<object>(name), CultureInfo.InvariantCulture);

        /// <summary>
        /// Gets an short property stored as long
        /// </summary>
        public static short GetInt16Property(this KVObject collection, string name) => collection.GetProperty<long>(name) switch
        {
            var l when l < 0 || l > short.MaxValue => throw new OverflowException($"Value {l} is out of range for Int16"),
            var l => (short)l,
        };

        /// <summary>
        /// Gets a UInt32 property from the key-value object.
        /// </summary>
        public static uint GetUInt32Property(this KVObject collection, string name)
            => Convert.ToUInt32(collection.GetProperty<object>(name), CultureInfo.InvariantCulture);

        /// <summary>
        /// Gets a double property from the key-value object.
        /// </summary>
        public static double GetDoubleProperty(this KVObject collection, string name)
            => Convert.ToDouble(collection.GetProperty<object>(name), CultureInfo.InvariantCulture);

        /// <summary>
        /// Gets a float property from the key-value object.
        /// </summary>
        public static float GetFloatProperty(this KVObject collection, string name)
            => (float)GetDoubleProperty(collection, name);

        /// <summary>
        /// Gets a byte property from the key-value object.
        /// </summary>
        public static byte GetByteProperty(this KVObject collection, string name)
            => Convert.ToByte(collection.GetProperty<object>(name), CultureInfo.InvariantCulture);

        /// <summary>
        /// Gets an integer array from the key-value object.
        /// </summary>
        public static long[] GetIntegerArray(this KVObject collection, string name)
            => collection.GetArray<object>(name)
                .Select(x => Convert.ToInt64(x, CultureInfo.InvariantCulture))
                .ToArray();

        /// <summary>
        /// Gets a float array from the key-value object.
        /// </summary>
        public static float[] GetFloatArray(this KVObject collection, string name)
            => collection.GetArray<object>(name)
                .Select(x => Convert.ToSingle(x, CultureInfo.InvariantCulture))
                .ToArray();

        /// <summary>
        /// Gets an unsigned integer array from the key-value object.
        /// </summary>
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

        /// <summary>
        /// Gets an array of KVObjects from the key-value object.
        /// </summary>
        public static KVObject[] GetArray(this KVObject collection, string name)
            => collection.GetArray<KVObject>(name);

        /// <summary>
        /// Gets an enum value from the key-value object.
        /// </summary>
        public static TEnum GetEnumValue<TEnum>(this KVObject collection, string name, bool normalize = false, string stripExtension = "Flags")
            where TEnum : Enum
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

            var enumString = (string)rawValue;
            if (normalize)
            {
                enumString = NormalizeEnumName<TEnum>(enumString, stripExtension);
            }

            if (Enum.TryParse(typeof(TEnum), enumString, false, out var value))
            {
                return (TEnum)value;
            }
            else
            {
                throw new ArgumentException($"Unable to map {enumString} to a member of enum {typeof(TEnum).Name}");
            }
        }

        /// <summary>
        /// Normalize C Style VALVE_ENUM_VALUE_1 to C# ValveEnum.Value1
        /// </summary>
        public static string NormalizeEnumName<TEnum>(string name, string stripExtension = "")
            where TEnum : Enum
        {
            var enumTypeName = typeof(TEnum).Name;

            if (enumTypeName.EndsWith(stripExtension, StringComparison.Ordinal))
            {
                enumTypeName = enumTypeName[..^stripExtension.Length];
            }

            var sb = new StringBuilder(name.Length);
            var i = 0;
            var nextUpper = true;
            var startsWithEnumTypeName = true;

            foreach (var c in name)
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

            name = sb.ToString();
            return name;
        }

        /// <summary>
        /// Determines whether the specified key contains an array (not a blob type).
        /// </summary>
        public static bool IsNotBlobType(this KVObject collection, string key)
            => collection.Properties[key].Type == KVValueType.Array;

        /// <summary>
        /// Converts the key-value object to a Vector2.
        /// </summary>
        public static Vector2 ToVector2(this KVObject collection) => new(
            collection.GetFloatProperty("0"),
            collection.GetFloatProperty("1"));

        /// <summary>
        /// Converts the key-value object to a Vector3.
        /// </summary>
        public static Vector3 ToVector3(this KVObject collection) => new(
            collection.GetFloatProperty("0"),
            collection.GetFloatProperty("1"),
            collection.GetFloatProperty("2"));

        /// <summary>
        /// Converts the key-value object to a Vector4.
        /// </summary>
        public static Vector4 ToVector4(this KVObject collection) => new(
            collection.GetFloatProperty("0"),
            collection.GetFloatProperty("1"),
            collection.GetFloatProperty("2"),
            collection.GetFloatProperty("3"));

        /// <summary>
        /// Converts the key-value object to a Quaternion.
        /// </summary>
        public static Quaternion ToQuaternion(this KVObject collection) => new(
            collection.GetFloatProperty("0"),
            collection.GetFloatProperty("1"),
            collection.GetFloatProperty("2"),
            collection.GetFloatProperty("3"));

        /// <summary>
        /// Converts an array of key-value objects to a Matrix4x4.
        /// </summary>
        public static Matrix4x4 ToMatrix4x4(this KVObject[] array)
        {
            var column1 = array[0].ToVector4();
            var column2 = array[1].ToVector4();
            var column3 = array[2].ToVector4();
            var column4 = array.Length > 3 ? array[3].ToVector4() : new Vector4(0, 0, 0, 1);

            return new Matrix4x4(column1.X, column2.X, column3.X, column4.X, column1.Y, column2.Y, column3.Y, column4.Y, column1.Z, column2.Z, column3.Z, column4.Z, column1.W, column2.W, column3.W, column4.W);
        }

        /// <summary>
        /// Converts the key-value object to a Matrix4x4.
        /// </summary>
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
