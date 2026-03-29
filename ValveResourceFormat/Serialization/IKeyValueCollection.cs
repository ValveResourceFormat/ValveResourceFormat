using System.Globalization;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;

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
    /// VRF-specific extension methods for KVObject.
    /// These provide the same behavior as the old VRF KVObject property getters.
    /// </summary>
    public static class KVObjectExtensions
    {
        /// <summary>
        /// Gets a child <see cref="KVObject"/> (sub-collection) by name.
        /// </summary>
        public static KVObject GetSubCollection(this KVObject obj, string name)
            => obj.GetChild(name);

        /// <summary>
        /// Gets a string property from the key-value object.
        /// </summary>
        public static string GetStringProperty(this KVObject obj, string name, string? defaultValue = null)
        {
            var value = obj[name];
            return value != null ? (string)value : defaultValue!;
        }

        /// <summary>
        /// Gets an Int32 property from the key-value object.
        /// </summary>
        public static int GetInt32Property(this KVObject obj, string name, int defaultValue = 0)
        {
            var value = obj[name];
            return value != null ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : defaultValue;
        }

        /// <summary>
        /// Gets a UInt32 property from the key-value object.
        /// </summary>
        public static uint GetUInt32Property(this KVObject obj, string name, uint defaultValue = 0)
        {
            var value = obj[name];
            return value != null ? Convert.ToUInt32(value, CultureInfo.InvariantCulture) : defaultValue;
        }

        /// <summary>
        /// Gets a 64-bit integer property from the key-value object.
        /// </summary>
        public static long GetIntegerProperty(this KVObject obj, string name, long defaultValue = 0)
        {
            var value = obj[name];
            return value != null ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : defaultValue;
        }

        /// <summary>
        /// Gets an unsigned integer property, with unchecked int-to-ulong conversion for binary KV3 compatibility.
        /// </summary>
        public static ulong GetUnsignedIntegerProperty(this KVObject obj, string name, ulong defaultValue = 0)
        {
            var value = obj[name];

            if (value == null)
            {
                return defaultValue;
            }

            if (value.ValueType == KVValueType.Int32)
            {
                unchecked
                {
                    return (ulong)Convert.ToInt32(value, CultureInfo.InvariantCulture);
                }
            }

            return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Gets a double property from the key-value object.
        /// </summary>
        public static double GetDoubleProperty(this KVObject obj, string name, double defaultValue = 0)
        {
            var value = obj[name];
            return value != null ? Convert.ToDouble(value, CultureInfo.InvariantCulture) : defaultValue;
        }

        /// <summary>
        /// Gets a float property from the key-value object.
        /// </summary>
        public static float GetFloatProperty(this KVObject obj, string name, float defaultValue = 0)
            => (float)GetDoubleProperty(obj, name, defaultValue);

        /// <summary>
        /// Gets a byte property from the key-value object.
        /// </summary>
        public static byte GetByteProperty(this KVObject obj, string name)
        {
            var value = obj[name];
            return value != null ? Convert.ToByte(value, CultureInfo.InvariantCulture) : default;
        }

        /// <summary>
        /// Gets a boolean property from the key-value object.
        /// </summary>
        public static bool GetBooleanProperty(this KVObject obj, string name)
        {
            var value = obj[name];
            return value != null && (bool)value;
        }

        /// <summary>
        /// Gets an array of sub-objects by name.
        /// Each array element is wrapped in a <see cref="KVObject"/>.
        /// </summary>
        public static KVObject[] GetArray(this KVObject obj, string name)
        {
            var child = obj.GetChild(name);

            if (child == null || !child.IsArray)
            {
                return null!;
            }

            var result = new KVObject[child.Count];

            for (var i = 0; i < child.Count; i++)
            {
                result[i] = child[i]!;
            }

            return result;
        }

        /// <summary>
        /// Gets an array of sub-objects by name and maps each element.
        /// </summary>
        public static T[] GetArray<T>(this KVObject obj, string name, Func<KVObject, T> mapper)
        {
            var items = GetArray(obj, name);

            if (items == null)
            {
                return null!;
            }

            var result = new T[items.Length];

            for (var i = 0; i < items.Length; i++)
            {
                result[i] = mapper(items[i]);
            }

            return result;
        }

        /// <summary>
        /// Gets a typed array of primitive values by name.
        /// Also handles binary blobs.
        /// </summary>
        public static T[] GetArray<T>(this KVObject obj, string name)
        {
            if (typeof(T) == typeof(KVObject))
            {
                return (T[])(object)GetArray(obj, name);
            }

            var child = obj.GetChild(name);

            if (child == null)
            {
                return null!; // TODO
            }

            if (child.ValueType == KVValueType.BinaryBlob)
            {
                if (typeof(T) == typeof(byte))
                {
                    return (T[])(object)child.Value.AsBlob();
                }

                var bytes = child.Value.AsSpan();
                var resultBytes = new T[bytes.Length];

                for (var i = 0; i < bytes.Length; i++)
                {
                    resultBytes[i] = (T)Convert.ChangeType(bytes[i], typeof(T), CultureInfo.InvariantCulture);
                }

                return resultBytes;
            }

            if (!child.IsArray)
            {
                return null!;
            }

            var result = new T[child.Count];

            for (var i = 0; i < child.Count; i++)
            {
                var elem = child[i]!;

                if (elem.ValueType is KVValueType.Null or KVValueType.Collection or KVValueType.Array)
                {
                    result[i] = default!;
                    continue;
                }

                result[i] = (T)Convert.ChangeType(elem, typeof(T), CultureInfo.InvariantCulture);
            }

            return result;
        }

        /// <summary>
        /// Gets an unsigned integer array, with unchecked int-to-ulong conversion for binary KV3 compatibility.
        /// </summary>
        public static ulong[] GetUnsignedIntegerArray(this KVObject obj, string name)
        {
            var child = obj.GetChild(name);

            if (child == null || !child.IsArray)
            {
                return [];
            }

            var result = new ulong[child.Count];

            for (var i = 0; i < child.Count; i++)
            {
                var elem = child[i]!;

                if (elem.ValueType == KVValueType.Int32)
                {
                    unchecked
                    {
                        result[i] = (ulong)(int)elem;
                    }
                }
                else
                {
                    result[i] = (ulong)elem;
                }
            }

            return result;
        }

        /// <summary>
        /// Gets an enum value from the key-value object.
        /// </summary>
        public static TEnum GetEnumValue<TEnum>(this KVObject obj, string name, bool normalize = false, string stripExtension = "Flags")
            where TEnum : Enum
        {
            var value = obj[name];

            switch (value.ValueType)
            {
                case KVValueType.Int32:
                    return (TEnum)(object)(int)value;
                case KVValueType.UInt32:
                    return (TEnum)(object)(int)(uint)value;
                case KVValueType.Int64:
                    return (TEnum)(object)(int)(long)value;
            }

            var enumString = (string)value;

            if (normalize)
            {
                enumString = NormalizeEnumName<TEnum>(enumString, stripExtension);
            }

            if (Enum.TryParse(typeof(TEnum), enumString, false, out var result))
            {
                return (TEnum)result;
            }

            throw new ArgumentException($"Unable to map {enumString} to a member of enum {typeof(TEnum).Name}");
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
        /// Gets a typed property value by name.
        /// </summary>
        public static T GetProperty<T>(this KVObject obj, string name, T defaultValue = default!)
        {
            var value = obj[name];

            if (value == null || value.ValueType == KVValueType.Null)
            {
                return defaultValue;
            }

            if (typeof(T) == typeof(KVObject))
            {
                return (T)(object)obj.GetChild(name);
            }

            if (typeof(T) == typeof(byte[]))
            {
                if (value.ValueType == KVValueType.BinaryBlob)
                {
                    return (T)(object)value.Value.AsBlob();
                }

                // Array of byte values
                var child = obj.GetChild(name);
                if (child?.IsArray == true)
                {
                    var result = new byte[child.Count];
                    for (var i = 0; i < child.Count; i++)
                    {
                        result[i] = (byte)child[i]!;
                    }
                    return (T)(object)result;
                }

                return defaultValue;
            }

            if (typeof(T) == typeof(object))
            {
                if (value.ValueType is KVValueType.Collection or KVValueType.Array)
                {
                    return (T)(object)obj.GetChild(name);
                }

                return value.ValueType switch
                {
                    KVValueType.String => (T)(object)(string)value,
                    KVValueType.Int32 => (T)(object)(int)value,
                    KVValueType.Int64 => (T)(object)(long)value,
                    KVValueType.UInt32 => (T)(object)(uint)value,
                    KVValueType.UInt64 => (T)(object)(ulong)value,
                    KVValueType.FloatingPoint => (T)(object)(float)value,
                    KVValueType.FloatingPoint64 => (T)(object)(double)value,
                    KVValueType.Boolean => (T)(object)(bool)value,
                    _ => defaultValue,
                };
            }

            return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Gets an array of long integers by name.
        /// </summary>
        public static long[] GetIntegerArray(this KVObject obj, string name)
            => obj.GetArray<long>(name) ?? [];

        /// <summary>
        /// Gets an array of floats by name.
        /// </summary>
        public static float[] GetFloatArray(this KVObject obj, string name)
            => obj.GetArray<float>(name) ?? [];

        /// <summary>
        /// Determines whether the specified key contains an array (not a blob type).
        /// </summary>
        public static bool IsNotBlobType(this KVObject obj, string key)
            => obj[key].ValueType == KVValueType.Array;

        /// <summary>
        /// Converts the key-value object to a Vector2.
        /// </summary>
        public static Vector2 ToVector2(this KVObject obj) => new(
            (float)obj[0],
            (float)obj[1]);

        /// <summary>
        /// Converts the key-value object to a Vector3.
        /// </summary>
        public static Vector3 ToVector3(this KVObject obj) => new(
            (float)obj[0],
            (float)obj[1],
            (float)obj[2]);

        /// <summary>
        /// Converts the key-value object to a Vector4.
        /// </summary>
        public static Vector4 ToVector4(this KVObject obj) => new(
            (float)obj[0],
            (float)obj[1],
            (float)obj[2],
            (float)obj[3]);

        /// <summary>
        /// Converts the key-value object to a Quaternion.
        /// </summary>
        public static Quaternion ToQuaternion(this KVObject obj) => new(
            (float)obj[0],
            (float)obj[1],
            (float)obj[2],
            (float)obj[3]);

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
                ? new Vector4((float)array[12], (float)array[13], (float)array[14], (float)array[15])
                : new Vector4(0, 0, 0, 1);
            return new Matrix4x4(
                (float)array[0], (float)array[4], (float)array[8], column4.X,
                (float)array[1], (float)array[5], (float)array[9], column4.Y,
                (float)array[2], (float)array[6], (float)array[10], column4.Z,
                (float)array[3], (float)array[7], (float)array[11], column4.W
            );
        }
    }
}
