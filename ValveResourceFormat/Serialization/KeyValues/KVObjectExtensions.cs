using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using ValveKeyValue;
using ValveKeyValue.KeyValues3;
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
        {
            if (!obj.TryGetValue(name, out var value))
            {
                return null!;
            }

            return value;
        }

        /// <summary>
        /// Gets a string property from the key-value object.
        /// </summary>
        //[return: NotNullIfNotNull(nameof(defaultValue))]
        public static string GetStringProperty(this KVObject obj, string name, string? defaultValue = null)
        {
            if (!obj.TryGetValue(name, out var value) || value.ValueType != KVValueType.String)
            {
                return defaultValue!;
            }

            return (string)value;
        }

        /// <summary>
        /// Gets an Int32 property from the key-value object.
        /// </summary>
        public static int GetInt32Property(this KVObject obj, string name, int defaultValue = 0)
        {
            if (!TryGetScalar(obj, name, out var value))
            {
                return defaultValue;
            }

            return (int)value;
        }

        /// <summary>
        /// Gets a UInt32 property from the key-value object.
        /// </summary>
        public static uint GetUInt32Property(this KVObject obj, string name, uint defaultValue = 0)
        {
            if (!TryGetScalar(obj, name, out var value))
            {
                return defaultValue;
            }

            return (uint)value;
        }

        /// <summary>
        /// Gets a 64-bit integer property from the key-value object.
        /// </summary>
        public static long GetIntegerProperty(this KVObject obj, string name, long defaultValue = 0)
        {
            if (!TryGetScalar(obj, name, out var value))
            {
                return defaultValue;
            }

            return (long)value;
        }

        /// <summary>
        /// Gets an unsigned integer property, with unchecked int-to-ulong conversion for binary KV3 compatibility.
        /// </summary>
        public static ulong GetUnsignedIntegerProperty(this KVObject obj, string name, ulong defaultValue = 0)
        {
            if (!TryGetScalar(obj, name, out var value))
            {
                return defaultValue;
            }

            if (value.ValueType == KVValueType.Int32)
            {
                // unchecked only applies to built-in conversions, not user-defined operators
                return unchecked((ulong)(int)value);
            }

            return (ulong)value;
        }

        /// <summary>
        /// Gets a double property from the key-value object.
        /// </summary>
        public static double GetDoubleProperty(this KVObject obj, string name, double defaultValue = 0)
        {
            if (!TryGetScalar(obj, name, out var value))
            {
                return defaultValue;
            }

            return (double)value;
        }

        /// <summary>
        /// Gets a float property from the key-value object.
        /// </summary>
        public static float GetFloatProperty(this KVObject obj, string name, float defaultValue = 0)
        {
            if (!TryGetScalar(obj, name, out var value))
            {
                return defaultValue;
            }

            return (float)value;
        }

        /// <summary>
        /// Gets a byte property from the key-value object.
        /// </summary>
        public static byte GetByteProperty(this KVObject obj, string name, byte defaultValue = 0)
        {
            if (!TryGetScalar(obj, name, out var value))
            {
                return defaultValue;
            }

            return (byte)value;
        }

        /// <summary>
        /// Gets a boolean property from the key-value object.
        /// </summary>
        public static bool GetBooleanProperty(this KVObject obj, string name, bool defaultValue = false)
        {
            if (!TryGetScalar(obj, name, out var value))
            {
                return defaultValue;
            }

            if (value.ValueType == KVValueType.String && bool.TryParse((string)value, out var parsed))
            {
                return parsed;
            }

            return (bool)value;
        }

        private static bool TryGetScalar(KVObject obj, string name, out KVObject value)
            => obj.TryGetValue(name, out value!) && !(value.ValueType == KVValueType.String && string.IsNullOrWhiteSpace((string)value));

        /// <summary>
        /// Gets the values of an array child by name.
        /// Returns the underlying collection without copying.
        /// </summary>
        public static IReadOnlyList<KVObject> GetArray(this KVObject obj, string name)
        {
            if (!obj.TryGetValue(name, out var child) || !child.IsArray)
            {
                return null!;
            }

            return child.Elements;
        }

        /// <summary>
        /// Gets a typed array of primitive values by name.
        /// Also handles binary blobs.
        /// </summary>
        public static T[] GetArray<T>(this KVObject obj, string name)
        {
            if (!obj.TryGetValue(name, out var child))
            {
                return null!;
            }

            if (child.ValueType == KVValueType.BinaryBlob)
            {
                if (typeof(T) == typeof(byte))
                {
                    return (T[])(object)child.AsBlob();
                }

                var bytes = child.AsBlob().AsSpan();
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

            if (child.IsPackedArray)
            {
                return child.ToArray<T>();
            }

            var elements = child.AsArraySpan();
            var result = new T[elements.Length];

            for (var i = 0; i < elements.Length; i++)
            {
                // Some files leave components null (e.g. [null, null, null]), these read as the default
                if (elements[i].ValueType is KVValueType.Null or KVValueType.Collection or KVValueType.Array)
                {
                    continue;
                }

                result[i] = child.GetElement<T>(i);
            }

            return result;
        }

        /// <summary>
        /// Gets an unsigned integer array of 64-bit bit patterns, with unchecked int-to-ulong conversion for binary KV3 compatibility.
        /// </summary>
        public static ulong[] GetUnsignedIntegerArray(this KVObject obj, string name)
            => obj.GetUnsignedIntegerArray<ulong>(name);

        /// <summary>
        /// Gets an unsigned integer array whose elements are bit patterns, such as flags, masks or hashes.
        /// Elements are truncated to <typeparamref name="T"/> instead of range checked, so negative values keep their bits.
        /// </summary>
        public static T[] GetUnsignedIntegerArray<T>(this KVObject obj, string name)
            where T : unmanaged, IBinaryInteger<T>, IUnsignedNumber<T>
        {
            if (!obj.TryGetValue(name, out var child))
            {
                return [];
            }

            if (child.ValueType == KVValueType.BinaryBlob)
            {
                var bytes = child.AsBlob();
                var blobResult = new T[bytes.Length];

                for (var i = 0; i < bytes.Length; i++)
                {
                    blobResult[i] = T.CreateTruncating(bytes[i]);
                }

                return blobResult;
            }

            if (!child.IsArray)
            {
                return [];
            }

            if (child.TryGetArraySpan<T>(out var values))
            {
                return values.ToArray();
            }

            var result = new T[child.Count];

            if (child.IsPackedArray)
            {
                // Every packed element type except ulong fits in a long
                if (child.TryGetArraySpan<ulong>(out var unsignedValues))
                {
                    for (var i = 0; i < result.Length; i++)
                    {
                        result[i] = T.CreateTruncating(unsignedValues[i]);
                    }

                    return result;
                }

                for (var i = 0; i < result.Length; i++)
                {
                    result[i] = T.CreateTruncating(child.GetElement<long>(i));
                }

                return result;
            }

            var elements = child.AsArraySpan();

            for (var i = 0; i < elements.Length; i++)
            {
                var element = elements[i];

                if (element.ValueType == KVValueType.UInt64)
                {
                    result[i] = T.CreateTruncating((ulong)element);
                }
                else
                {
                    result[i] = T.CreateTruncating((long)element);
                }
            }

            return result;
        }

        /// <summary>
        /// Gets an enum value from the key-value object.
        /// </summary>
        public static TEnum GetEnumValue<TEnum>(this KVObject obj, string name, bool normalize = false, string stripExtension = "Flags")
            where TEnum : struct, Enum
        {
            if (!obj.TryGetValue(name, out var value))
            {
                throw new KeyNotFoundException($"Key '{name}' not found");
            }

            if (value.ValueType != KVValueType.String)
            {
                return (TEnum)(object)(int)value;
            }

            var enumString = (string)value;

            if (!normalize)
            {
                return ParseEnumName<TEnum>(enumString);
            }

            // Scene objects repeat the same few flag names, so normalizing every one would be wasted work
            return NormalizedEnumValues<TEnum>.Cache.GetOrAdd(
                (enumString, stripExtension),
                static key => ParseEnumName<TEnum>(NormalizeEnumName<TEnum>(key.Name, key.StripExtension)));
        }

        private static class NormalizedEnumValues<TEnum>
            where TEnum : struct, Enum
        {
            public static readonly ConcurrentDictionary<(string Name, string StripExtension), TEnum> Cache = new();
        }

        private static TEnum ParseEnumName<TEnum>(string enumString)
            where TEnum : struct, Enum
        {
            if (Enum.TryParse<TEnum>(enumString, false, out var result))
            {
                return result;
            }

            throw new ArgumentException($"Unable to map {enumString} to a member of enum {typeof(TEnum).Name}");
        }

        /// <summary>
        /// Normalize C Style VALVE_ENUM_SOME_VALUE to C# ValveEnum.SomeValue (underscores and digits are stripped)
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
            => obj.TryGetValue(key, out var value) && value.ValueType == KVValueType.Array;

        /// <summary>
        /// Converts the key-value object to a Vector2.
        /// </summary>
        public static Vector2 ToVector2(this KVObject obj) => new(
            obj.GetElement<float>(0),
            obj.GetElement<float>(1));

        /// <summary>
        /// Converts the key-value object to a Vector3.
        /// </summary>
        public static Vector3 ToVector3(this KVObject obj) => new(
            obj.GetElement<float>(0),
            obj.GetElement<float>(1),
            obj.GetElement<float>(2));

        /// <summary>
        /// Converts the key-value object to a Vector4.
        /// </summary>
        public static Vector4 ToVector4(this KVObject obj) => new(
            obj.GetElement<float>(0),
            obj.GetElement<float>(1),
            obj.GetElement<float>(2),
            obj.GetElement<float>(3));

        /// <summary>
        /// Converts the key-value object to a Quaternion.
        /// </summary>
        public static Quaternion ToQuaternion(this KVObject obj) => new(
            obj.GetElement<float>(0),
            obj.GetElement<float>(1),
            obj.GetElement<float>(2),
            obj.GetElement<float>(3));

        /// <summary>
        /// Converts the key-value object to a tuple containing Position, Uniform Scale, and Rotation.
        /// </summary>
        public static (Vector3 Position, float Scale, Quaternion Rotation) ToTransform(this KVObject obj)
        {
            var position = obj.ToVector3();

            var compact = obj.Count == 7;
            var scale = compact ? 1f : obj.GetElement<float>(3);
            var rotationOffset = compact ? 3 : 4;

            var rotation = new Quaternion(
                obj.GetElement<float>(rotationOffset),
                obj.GetElement<float>(rotationOffset + 1),
                obj.GetElement<float>(rotationOffset + 2),
                obj.GetElement<float>(rotationOffset + 3));

            return (position, scale, rotation);
        }

        /// <summary>
        /// Converts an array of key-value objects to a Matrix4x4.
        /// </summary>
        public static Matrix4x4 ToMatrix4x4(this IReadOnlyList<KVObject> array)
        {
            var column1 = array[0].ToVector4();
            var column2 = array[1].ToVector4();
            var column3 = array[2].ToVector4();
            var column4 = array.Count > 3 ? array[3].ToVector4() : new Vector4(0, 0, 0, 1);

            return new Matrix4x4(column1.X, column2.X, column3.X, column4.X, column1.Y, column2.Y, column3.Y, column4.Y, column1.Z, column2.Z, column3.Z, column4.Z, column1.W, column2.W, column3.W, column4.W);
        }

        /// <summary>
        /// Converts the key-value object to a Matrix4x4.
        /// </summary>
        public static Matrix4x4 ToMatrix4x4(this KVObject array)
        {
            var column4 = array.Count > 12
                ? new Vector4(array.GetElement<float>(12), array.GetElement<float>(13), array.GetElement<float>(14), array.GetElement<float>(15))
                : new Vector4(0, 0, 0, 1);
            return new Matrix4x4(
                array.GetElement<float>(0), array.GetElement<float>(4), array.GetElement<float>(8), column4.X,
                array.GetElement<float>(1), array.GetElement<float>(5), array.GetElement<float>(9), column4.Y,
                array.GetElement<float>(2), array.GetElement<float>(6), array.GetElement<float>(10), column4.Z,
                array.GetElement<float>(3), array.GetElement<float>(7), array.GetElement<float>(11), column4.W
            );
        }

        /// <summary>
        /// Deserializes KeyValues1 text that keeps several keys at its top level rather than a single root.
        /// </summary>
        /// <param name="serializer">A KeyValues1 text serializer.</param>
        /// <param name="stream">The text to read.</param>
        /// <returns>An object holding every top level key.</returns>
        public static KVObject DeserializeWithoutRoot(this KVSerializer serializer, Stream stream)
        {
            // The format takes a single root, and would fold the other keys into the first
            using var wrapped = new MemoryStream();
            wrapped.Write("\"root\"\n{\n"u8);
            stream.CopyTo(wrapped);
            wrapped.Write("\n}\n"u8);
            wrapped.Position = 0;

            return serializer.Deserialize(wrapped);
        }
    }

    /// <summary>
    /// Extension methods for KV3 document serialization and creation.
    /// </summary>
    public static class KVDocumentExtensions
    {
        /// <summary>
        /// Creates a KV3 document from a root <see cref="KVObject"/> with optional format.
        /// </summary>
        public static KVDocument ToKV3Document(this KVObject root, KV3ID? format = null)
        {
            return new KVDocument(
                new KVHeader
                {
                    Encoding = KV3IDLookup.Get("text"),
                    Format = format ?? KV3IDLookup.Get("generic"),
                },
                null,
                root);
        }

        /// <summary>
        /// Serializes a <see cref="KVDocument"/> to KV3 text format.
        /// </summary>
        public static string ToKV3String(this KVDocument doc)
        {
            using var ms = new MemoryStream();
            var serializer = KVSerializer.Create(KVSerializationFormat.KeyValues3Text);
            serializer.Serialize(ms, doc);
            return System.Text.Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        }

        /// <summary>
        /// Serializes a <see cref="KVObject"/> to KV3 text format with optional format.
        /// </summary>
        public static string ToKV3String(this KVObject root, KV3ID? format = null)
            => root.ToKV3Document(format).ToKV3String();

        /// <summary>
        /// Writes the <see cref="KVDocument"/> as KV3 text to an <see cref="IndentedTextWriter"/>.
        /// </summary>
        public static void WriteKV3Text(this KVDocument doc, IndentedTextWriter writer)
        {
            writer.Write(doc.ToKV3String());
        }

        /// <summary>
        /// Parses a KeyValues3 file from the specified stream.
        /// </summary>
        public static KVDocument ParseKV3(Stream stream)
        {
            var serializer = KVSerializer.Create(KVSerializationFormat.KeyValues3Text);
            return serializer.Deserialize(stream);
        }

        /// <summary>
        /// Parses a KeyValues3 file from the specified filename.
        /// </summary>
        public static KVDocument ParseKV3(string filename)
        {
            using var fileStream = new FileStream(filename, FileMode.Open, FileAccess.Read);
            return ParseKV3(fileStream);
        }
    }
}
