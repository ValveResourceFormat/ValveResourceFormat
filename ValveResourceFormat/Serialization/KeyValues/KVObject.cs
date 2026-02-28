//#define DEBUG_ADD_KV_TYPE_COMMENTS

using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;
using KVValueType = ValveKeyValue.KVValueType;

#nullable disable

namespace ValveResourceFormat.Serialization.KeyValues
{
    /// <summary>
    /// Represents a KeyValue object data structure.
    /// </summary>
    [DebuggerDisplay("{DebugRepresentation,nq}")]
    [DebuggerTypeProxy(typeof(DebugView))]
    public class KVObject : IEnumerable<KeyValuePair<string, object>>
    {
        /// <summary>
        /// Gets the key name of this object.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Gets the properties dictionary.
        /// </summary>
        public Dictionary<string, KVValue> Properties { get; }

        /// <summary>
        /// Gets a value indicating whether this object is an array.
        /// </summary>
        public bool IsArray { get; }

        /// <summary>
        /// Gets the number of properties or items in this object.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="KVObject"/> class.
        /// </summary>
        /// <param name="name">The key name.</param>
        /// <param name="capacity">The initial capacity.</param>
        public KVObject(string name, int capacity = 0)
        {
            Key = name;
            Properties = new Dictionary<string, KVValue>(capacity);
            Count = 0;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="KVObject"/> class.
        /// </summary>
        /// <param name="name">The key name.</param>
        /// <param name="isArray">Whether this object is an array.</param>
        /// <param name="capacity">The initial capacity.</param>
        public KVObject(string name, bool isArray, int capacity = 0)
            : this(name, capacity)
        {
            IsArray = isArray;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="KVObject"/> class with array items.
        /// </summary>
        /// <param name="name">The key name.</param>
        /// <param name="arrayItems">The array items.</param>
        public KVObject(string name, IList<KVValue> arrayItems)
            : this(name, true, arrayItems.Count)
        {
            foreach (var arrayItem in arrayItems)
            {
                AddProperty(null, arrayItem);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="KVObject"/> class with properties.
        /// </summary>
        /// <param name="name">The key name.</param>
        /// <param name="properties">The properties to add.</param>
        public KVObject(string name, params (string Name, object Value)[] properties)
            : this(name, properties.Length)
        {
            foreach (var prop in properties)
            {
                AddProperty(prop.Name, prop.Value);
            }
        }

        /// <summary>
        /// Adds a property to the structure.
        /// </summary>
        /// <param name="name">The property name.</param>
        /// <param name="value">The property value.</param>
        public virtual void AddProperty(string name, KVValue value)
        {
            if (IsArray)
            {
                // Make up a key for the dictionary
                Properties.Add(Count.ToString(CultureInfo.InvariantCulture), value);
            }
            else
            {
                ArgumentNullException.ThrowIfNull(name);

#if DEBUG
                if (!Properties.TryAdd(name, value))
                {
                    Console.WriteLine($"This KV3 object contains a duplicate key: {name} with value {value.Value}:\n{new StackTrace()}");
                    Properties[name] = value;
                }
#else
                Properties[name] = value;
#endif
            }

            Count++;
        }

        /// <summary>
        /// Adds a property to the structure.
        /// </summary>
        /// <param name="name">The property name.</param>
        /// <param name="value">The property value.</param>
        public void AddProperty(string name, object value)
        {
            AddProperty(name, new KVValue(value));
        }

        internal void AddItem(object item)
        {
            Debug.Assert(IsArray);
            AddProperty(null, item);
        }

        /// <summary>
        /// Gets the value at the specified array index.
        /// </summary>
        /// <param name="arrayIndex">The array index.</param>
        /// <returns>The value at the specified index.</returns>
        public KVValue this[int arrayIndex]
        {
            get => Properties[arrayIndex.ToString(CultureInfo.InvariantCulture)];
        }

        /// <summary>
        /// Serializes this object to the specified writer.
        /// </summary>
        /// <param name="writer">The writer to serialize to.</param>
        public void Serialize(IndentedTextWriter writer)
        {
            writer.Grow(12 + Count * 3 + (writer.Indent + 1) * Count); // Not exact

            if (IsArray)
            {
                SerializeArray(writer);
            }
            else
            {
                SerializeObject(writer);
            }
        }

        //Serialize the contents of the KV object
        private void SerializeObject(IndentedTextWriter writer)
        {
            //Don't enter the top-most object
            if (Key != null)
            {
                writer.WriteLine();
            }

            writer.WriteLine("{");
            writer.Indent++;

            foreach (var pair in Properties)
            {
                WriteKey(writer, pair.Key);

                KV3TextSerializer.WriteValue(pair.Value, writer);

#if DEBUG_ADD_KV_TYPE_COMMENTS
                writer.Write($" // {pair.Value.Type}");
#endif

                writer.WriteLine();
            }

            writer.Indent--;
            writer.Write("}");
        }

        private void SerializeArray(IndentedTextWriter writer)
        {
            writer.WriteLine();
            writer.WriteLine("[");
            writer.Indent++;

            // Need to preserve the order
            for (var i = 0; i < Count; i++)
            {
                var value = this[i];
                KV3TextSerializer.WriteValue(value, writer);

#if DEBUG_ADD_KV_TYPE_COMMENTS
                writer.WriteLine($", // {value.Type}");
#else
                writer.WriteLine(",");
#endif
            }

            writer.Indent--;
            writer.Write("]");
        }

        // Copied from ValveKeyValue kv3 branch
        private static void WriteKey(IndentedTextWriter writer, string key)
        {
            if (key == null)
            {
                return;
            }

            var escaped = key.Length == 0; // Quote empty strings
            var sb = new StringBuilder(key.Length + 2);
            sb.Append('"');

            if (key.Length > 0 && char.IsAsciiDigit(key[0]))
            {
                // Quote when first character is a digit
                escaped = true;
            }

            foreach (var @char in key)
            {
                switch (@char)
                {
                    case '\t':
                        escaped = true;
                        sb.Append('\\');
                        sb.Append('t');
                        break;

                    case '\n':
                        escaped = true;
                        sb.Append('\\');
                        sb.Append('n');
                        break;

                    case '"':
                        escaped = true;
                        sb.Append('\\');
                        sb.Append('"');
                        break;

                    case '\\':
                        escaped = true;
                        sb.Append('\\');
                        sb.Append('\\');
                        break;

                    default:
                        if (@char != '.' && @char != '_' && !char.IsAsciiLetterOrDigit(@char))
                        {
                            escaped = true;
                        }

                        sb.Append(@char);
                        break;
                }
            }

            if (escaped)
            {
                sb.Append('"');
                writer.Write(sb.ToString());
            }
            else
            {
                writer.Write(key);
            }

            writer.Write(" = ");
        }

        /// <summary>
        /// Determines whether this object contains the specified key.
        /// </summary>
        /// <param name="name">The key name.</param>
        /// <returns>True if the key exists; otherwise, false.</returns>
        public bool ContainsKey(string name) => Properties.ContainsKey(name);

        /// <summary>
        /// Gets a property value by name with type conversion.
        /// </summary>
        /// <typeparam name="T">The type to convert to.</typeparam>
        /// <param name="name">The property name.</param>
        /// <param name="defaultValue">The default value if the property doesn't exist.</param>
        /// <returns>The property value or default value.</returns>
        [return: NotNullIfNotNull(nameof(defaultValue))]
        public T GetProperty<T>(string name, T defaultValue = default)
        {
            if (Properties.TryGetValue(name, out var value))
            {
                return (T)value.Value;
            }

            return defaultValue;
        }

        /// <summary>
        /// Gets a property value by name with unchecked type conversion, attempting to parse strings as numbers.
        /// </summary>
        /// <typeparam name="T">The type to convert to.</typeparam>
        /// <param name="name">The property name.</param>
        /// <param name="defaultValue">The default value if the property doesn't exist.</param>
        /// <returns>The property value or default value.</returns>
        public T GetPropertyUnchecked<T>(string name, T defaultValue = default)
        {
            if (Properties.TryGetValue(name, out var property))
            {
                var valueObject = property.Value;

                // We typicallly want to get a bool, int, uint, or float property,
                // however it might be stored as string, which will raise FormatException.
                // So here we try to convert the string to floating point number.
                if (typeof(T) != typeof(string) && valueObject is string stringValue)
                {
                    if (float.TryParse(stringValue, CultureInfo.InvariantCulture, out var floatVal))
                    {
                        valueObject = floatVal;
                    }
                }

                return (T)Convert.ChangeType(valueObject, typeof(T), CultureInfo.InvariantCulture);
            }

            return defaultValue;
        }

        /// <summary>
        /// Gets an array property by name.
        /// </summary>
        /// <typeparam name="T">The element type of the array.</typeparam>
        /// <param name="name">The property name.</param>
        /// <returns>The array, or default if the property doesn't exist.</returns>
        public T[] GetArray<T>(string name)
        {
            if (Properties.TryGetValue(name, out var value))
            {
                if (value.Type == KVValueType.Collection && value.Value is KVObject kvObject && kvObject.IsArray)
                {
                    var properties = new List<T>(capacity: kvObject.Count);
                    var index = 0;
                    var property = kvObject.GetProperty<T>(index.ToString(CultureInfo.InvariantCulture));
                    while (!property.Equals(default(T)))
                    {
                        properties.Add(property);
                        ++index;
                    }

                    return [.. properties];
                }

                if (value.Type == KVValueType.BinaryBlob)
                {
                    if (typeof(T) == typeof(byte))
                    {
                        return (T[])value.Value;
                    }

                    return ((byte[])value.Value).Cast<T>().ToArray();
                }

                if (value.Type != KVValueType.Array) // && value.Type != KV3BinaryNodeType.ARRAY_TYPED)
                {
                    throw new InvalidOperationException($"Tried to cast non-array property {name} to array. Actual type: {value.Type}");
                }

                // TODO: Why are we trying to read floats as doubles
                if (typeof(T) == typeof(double))
                {
                    return ((KVObject)value.Value).Properties.Values.Select(static (v) =>
                    {
                        return v.Type == KVValueType.FloatingPoint ? (double)(float)v.Value : (double)v.Value;
                    }).Cast<T>().ToArray();
                }

                if (typeof(T) == typeof(short))
                {
                    return ((KVObject)value.Value).Properties.Values.Select(static (v) =>
                    {
                        return v.Type == KVValueType.Int64 ? (short)(int)v.Value : (short)v.Value;
                    }).Cast<T>().ToArray();
                }

                return ((KVObject)value.Value).Properties.Values.Select(static v => (T)v.Value).ToArray();
            }
            else
            {
                return default;
            }
        }

        /// <inheritdoc/>
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
            => Properties
                .Select(p => new KeyValuePair<string, object>(p.Key, p.Value.Value))
                .GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        #region Debugging
#pragma warning disable IDE0051, IDE0052 // Remove unread private members
        internal string DebugRepresentation => DebugView.GetRepresentation(this);

        internal class DebugView
        {
            readonly KVObject obj;

            internal DebugView(KVObject obj)
            {
                this.obj = obj;
            }

            internal static string GetRepresentation(KVObject obj)
            {
                if (!obj.IsArray)
                {
                    return $"Properties = {obj.Count}";
                }

                if (obj.Count > 0)
                {
                    var first = obj.Properties.First();
                    var type = first.Value.Type;
                    var allSameType = obj.Properties.All(p => p.Value.Type == type);
                    if (allSameType)
                    {
                        return $"KVArray<{type}> Items = {obj.Count}";
                    }
                }

                return $"KVArray Items = {obj.Count}";
            }

            [DebuggerDisplay("{Key,nq} = {ValueDebugRepresentation,nq}")]
            internal class KeyValue
            {
                readonly string Key;
                readonly object Value;
                readonly KVValueType Type;

                [DebuggerBrowsable(DebuggerBrowsableState.Never)]
                readonly string ValueDebugRepresentation;

                internal KeyValue(KeyValuePair<string, KVValue> keyValuePair)
                {
                    (Key, Value, Type) = (keyValuePair.Key, keyValuePair.Value.Value, keyValuePair.Value.Type);
                    ValueDebugRepresentation = Value switch
                    {
                        KVObject kvObject => $"<{(kvObject.IsArray ? "KVArray" : "KVObject")}>",
                        _ => keyValuePair.Value.Value?.ToString() ?? "null",
                    };
                }
            }

            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            KeyValue[] Properties => obj.IsArray
                ? []
                : obj.Properties.Select(p => new KeyValue(p)).ToArray();


            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            KVValue[] Items => obj.IsArray
                ? [.. obj.Properties.Values]
                : [];
        }
#pragma warning restore IDE0051, IDE0052
        #endregion Debugging
    }
}
