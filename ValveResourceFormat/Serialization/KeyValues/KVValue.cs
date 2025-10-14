using KVValueType = ValveKeyValue.KVValueType;

namespace ValveResourceFormat.Serialization.KeyValues
{
    /// <summary>
    /// Flags for KeyValue values.
    /// </summary>
    public enum KVFlag : byte
    {
#pragma warning disable CS1591
        None = 0,
        Resource = 1,
        ResourceName = 2,
        Panorama = 3,
        SoundEvent = 4,
        SubClass = 5,
        EntityName = 6,

        // There are more types available in the S2 binaries, but they should not be persisted. Look for "The specific type '%s' cannot be persisted"
        MaxPersistedFlag = EntityName,
#pragma warning restore CS1591
    }

    /// <summary>
    /// Structure to hold type + flag + value
    /// </summary>
    public readonly struct KVValue
    {
        /// <summary>
        /// Gets the value type.
        /// </summary>
        public readonly KVValueType Type { get; } = KVValueType.Null;

        /// <summary>
        /// Gets the value flag.
        /// </summary>
        public readonly KVFlag Flag { get; }

        /// <summary>
        /// Gets the value.
        /// </summary>
        public readonly object? Value { get; } = null;

        /// <summary>
        /// Initializes a new instance of the <see cref="KVValue"/> struct.
        /// </summary>
        /// <param name="type">The value type.</param>
        /// <param name="value">The value.</param>
        public KVValue(KVValueType type, object value)
        {
            Type = type;
            Value = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="KVValue"/> struct.
        /// </summary>
        /// <param name="type">The value type.</param>
        /// <param name="flag">The value flag.</param>
        /// <param name="value">The value.</param>
        public KVValue(KVValueType type, KVFlag flag, object value)
        {
            Type = type;
            Flag = flag;
            Value = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="KVValue"/> struct, inferring the type from the value.
        /// </summary>
        /// <param name="value">The value.</param>
        public KVValue(object? value)
        {
            if (value is KVValue v)
            {
                Type = v.Type;
                Value = v.Value;
                // note: we remove the flag for decompilation purposes
                // we should not be hitting this path when parsing from binary resources
                Flag = KVFlag.None;
            }
            else if (value is Vector3 vec3)
            {
                Type = KVValueType.Array;
                Value = MakeArray([vec3.X, vec3.Y, vec3.Z]).Value;
            }
            else
            {
                Type = value switch
                {
                    string => KVValueType.String,
                    bool => KVValueType.Boolean,
                    int => KVValueType.Int32,
                    uint => KVValueType.UInt32,
                    long => KVValueType.Int64,
                    float => KVValueType.FloatingPoint,
                    double => KVValueType.FloatingPoint64,
                    KVObject kv => kv.IsArray ? KVValueType.Array : KVValueType.Collection,
                    null => KVValueType.Null,
                    _ => throw new NotImplementedException()
                };
                Value = value;
            }
        }

        internal static KVValue MakeArray<T>(IEnumerable<T> values)
        {
            var list = new KVObject(null, isArray: true);
            foreach (var value in values)
            {
                list.AddProperty(null, new KVValue(value));
            }

            return new KVValue(KVValueType.Array, list);
        }
    }
}
