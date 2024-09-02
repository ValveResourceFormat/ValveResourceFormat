//#define DEBUG_ADD_KV_TYPE_COMMENTS

using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ValveResourceFormat.Serialization.KeyValues
{
    //Datastructure for a KV Object
    [DebuggerDisplay("{DebugRepresentation,nq}")]
    [DebuggerTypeProxy(typeof(DebugView))]
    public class KVObject : IEnumerable<KeyValuePair<string, object>>
    {
        public string Key { get; }
        public Dictionary<string, KVValue> Properties { get; }
        public bool IsArray { get; }
        public int Count { get; private set; }

        public KVObject(string name, int capacity = 0)
        {
            Key = name;
            Properties = new Dictionary<string, KVValue>(capacity);
            Count = 0;
        }

        public KVObject(string name, bool isArray, int capacity = 0)
            : this(name, capacity)
        {
            IsArray = isArray;
        }

        //Add a property to the structure
        public virtual void AddProperty(string name, KVValue value)
        {
            if (IsArray)
            {
                // Make up a key for the dictionary
                Properties.Add(Count.ToString(CultureInfo.InvariantCulture), value);
            }
            else
            {
                Properties.Add(name, value);
            }

            Count++;
        }

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

                pair.Value.PrintValue(writer);

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
                var value = Properties[i.ToString(CultureInfo.InvariantCulture)];
                value.PrintValue(writer);

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

                    case '\'':
                        escaped = true;
                        sb.Append('\\');
                        sb.Append('\'');
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

        public bool ContainsKey(string name) => Properties.ContainsKey(name);

        public T GetProperty<T>(string name)
        {
            if (Properties.TryGetValue(name, out var value))
            {
                return (T)value.Value;
            }
            else
            {
                return default;
            }
        }

        public T[] GetArray<T>(string name)
        {
            if (Properties.TryGetValue(name, out var value))
            {
                if (value.Type == KVType.OBJECT && value.Value is KVObject kvObject && kvObject.IsArray)
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

                if (value.Type == KVType.BINARY_BLOB)
                {
                    if (typeof(T) == typeof(byte))
                    {
                        return (T[])value.Value;
                    }

                    return ((byte[])value.Value).Cast<T>().ToArray();
                }

                if (value.Type != KVType.ARRAY && value.Type != KVType.ARRAY_TYPED)
                {
                    throw new InvalidOperationException($"Tried to cast non-array property {name} to array. Actual type: {value.Type}");
                }

                return ((KVObject)value.Value).Properties.Values.Select(v => (T)v.Value).ToArray();
            }
            else
            {
                return default;
            }
        }

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
                readonly KVType Type;

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
