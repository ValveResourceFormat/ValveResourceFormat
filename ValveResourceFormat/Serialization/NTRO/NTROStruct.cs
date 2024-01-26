using System.Collections;
using System.Globalization;
using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Serialization.NTRO
{
    public class NTROStruct : IDictionary, IKeyValueCollection
    {
        private readonly Dictionary<string, NTROValue> Contents;
        public string Name { get; private set; }

        public NTROStruct(string name)
        {
            Name = name;
            Contents = [];
        }

        public NTROStruct(params NTROValue[] values)
        {
            Contents = [];
            for (var i = 0; i < values.Length; i++)
            {
                Contents.Add(i.ToString(CultureInfo.InvariantCulture), values[i]);
            }
        }

        public void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine(Name);
            writer.WriteLine("{");
            writer.Indent++;

            foreach (var entry in Contents)
            {
                if (entry.Value.Pointer)
                {
                    writer.Write("{0} {1}* = (ptr) ->", ValveDataType(entry.Value.Type), entry.Key);
                    entry.Value.WriteText(writer);
                }
                else if (entry.Value is NTROArray array)
                {
                    writer.WriteLine("{0} {1}[{2}] =", ValveDataType(array.Type), entry.Key, array.Count);

                    writer.WriteLine("[");
                    writer.Indent++;

                    foreach (var innerEntry in array)
                    {
                        innerEntry.WriteText(writer);
                    }

                    writer.Indent--;
                    writer.WriteLine("]");
                }
                else if (entry.Value is NTROValue<byte[]> byteArray && byteArray?.Value != null)
                {
                    writer.WriteLine("{0}[{2}] {1} =", ValveDataType(entry.Value.Type), entry.Key, byteArray.Value.Length);
                    writer.WriteLine("[");
                    writer.Indent++;

                    foreach (var val in byteArray.Value)
                    {
                        writer.WriteLine("{0:X2}", val);
                    }

                    writer.Indent--;
                    writer.WriteLine("]");
                }
                else
                {
                    // Can either be NTROArray or NTROValue so...
                    writer.Write("{0} {1} = ", ValveDataType(entry.Value.Type), entry.Key);
                    entry.Value.WriteText(writer);
                }
            }

            writer.Indent--;
            writer.WriteLine("}");
        }

        public override string ToString()
        {
            using var writer = new IndentedTextWriter();
            WriteText(writer);

            return writer.ToString();
        }

        private static string ValveDataType(SchemaFieldType type)
        {
            return type switch
            {
                SchemaFieldType.SByte => "int8",
                SchemaFieldType.Byte => "uint8",
                SchemaFieldType.Int16 => "int16",
                SchemaFieldType.UInt16 => "uint16",
                SchemaFieldType.Int32 => "int32",
                SchemaFieldType.UInt32 => "uint32",
                SchemaFieldType.Int64 => "int64",
                SchemaFieldType.UInt64 => "uint64",
                SchemaFieldType.Float => "float32",
                SchemaFieldType.ResourceString => "CResourceString",
                SchemaFieldType.Boolean => "bool",
                SchemaFieldType.Fltx4 => "fltx4",
                SchemaFieldType.Matrix3x4a => "matrix3x4a_t",
                _ => type.ToString(),
            };
        }

        public NTROValue this[string key]
        {
            get => Contents[key];
            set => Contents[key] = value;
        }

        public object this[object key]
        {
            get => ((IDictionary)Contents)[key];
            set => ((IDictionary)Contents)[key] = value;
        }

        public int Count => Contents.Count;

        public bool IsFixedSize => ((IDictionary)Contents).IsFixedSize;

        public bool IsReadOnly => ((IDictionary)Contents).IsReadOnly;

        public bool IsSynchronized => ((IDictionary)Contents).IsSynchronized;

        public ICollection Keys => Contents.Keys;

        public object SyncRoot => ((IDictionary)Contents).SyncRoot;

        public ICollection Values => Contents.Values;

        public void Add(object key, object value)
        {
            ((IDictionary)Contents).Add(key, value);
        }

        public void Clear()
        {
            Contents.Clear();
        }

        public bool Contains(object key)
        {
            return ((IDictionary)Contents).Contains(key);
        }

        public void CopyTo(Array array, int index)
        {
            ((IDictionary)Contents).CopyTo(array, index);
        }

        public IDictionaryEnumerator GetEnumerator()
        {
            return ((IDictionary)Contents).GetEnumerator();
        }

        public void Remove(object key)
        {
            ((IDictionary)Contents).Remove(key);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IDictionary)Contents).GetEnumerator();
        }

        public bool ContainsKey(string name) => Contents.ContainsKey(name);

        public T[] GetArray<T>(string name)
        {
            if (Contents.TryGetValue(name, out var value))
            {
                if (value.Type == SchemaFieldType.Byte)
                {
                    //special case for byte arrays for faster access
                    if (typeof(T) == typeof(byte))
                    {
                        return (T[])value.ValueObject;
                    }
                    else
                    {
                        //still have to do a slow conversion if the requested type is different
                        return ((byte[])value.ValueObject).Select(v => (T)(object)v).ToArray();
                    }
                }
                else
                {
                    return ((NTROArray)value).Select(v => (T)v.ValueObject).ToArray();
                }
            }
            else
            {
                return default;
            }
        }

        public T GetProperty<T>(string name)
        {
            if (Contents.TryGetValue(name, out var value))
            {
                return (T)value.ValueObject;
            }
            else
            {
                return default;
            }
        }

        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator()
            => Contents
                .Select(p => new KeyValuePair<string, object>(p.Key, p.Value.ValueObject))
                .GetEnumerator();

        public KVObject ToKVObject()
        {
            var kv = new KVObject(Name, capacity: Contents.Count);
            foreach (var entry in Contents)
            {
                kv.AddProperty(entry.Key, entry.Value.ToKVValue());
            }

            return kv;
        }
    }
}
