using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace ValveResourceFormat.Serialization.NTRO
{
    public class NTROStruct : IDictionary, IKeyValueCollection
    {
        private readonly Dictionary<string, NTROValue> Contents;
        public string Name { get; private set; }

        public NTROStruct(string name)
        {
            Name = name;
            Contents = new Dictionary<string, NTROValue>();
        }

        public NTROStruct(params NTROValue[] values)
        {
            Contents = new Dictionary<string, NTROValue>();
            for (var i = 0; i < values.Length; i++)
            {
                Contents.Add(i.ToString(), values[i]);
            }
        }

        public void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine(Name);
            writer.WriteLine("{");
            writer.Indent++;

            foreach (var entry in Contents)
            {
                var array = entry.Value as NTROArray;
                var byteArray = (entry.Value as NTROValue<byte[]>)?.Value;

                if (entry.Value.Pointer)
                {
                    writer.Write("{0} {1}* = (ptr) ->", ValveDataType(entry.Value.Type), entry.Key);
                    entry.Value.WriteText(writer);
                }
                else if (array != null)
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
                else if (byteArray != null)
                {
                    writer.WriteLine("{0}[{2}] {1} =", ValveDataType(entry.Value.Type), entry.Key, byteArray.Length);
                    writer.WriteLine("[");
                    writer.Indent++;

                    foreach (var val in byteArray)
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
            using (var writer = new IndentedTextWriter())
            {
                WriteText(writer);

                return writer.ToString();
            }
        }

        private static string ValveDataType(DataType type)
        {
            switch (type)
            {
                case DataType.SByte: return "int8";
                case DataType.Byte: return "uint8";
                case DataType.Int16: return "int16";
                case DataType.UInt16: return "uint16";
                case DataType.Int32: return "int32";
                case DataType.UInt32: return "uint32";
                case DataType.Int64: return "int64";
                case DataType.UInt64: return "uint64";
                case DataType.Float: return "float32";
                case DataType.String: return "CResourceString";
                case DataType.Boolean: return "bool";
                case DataType.Fltx4: return "fltx4";
                case DataType.Matrix3x4a: return "matrix3x4a_t";
            }

            return type.ToString();
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
                if (value.Type == DataType.Byte)
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
                return default(T[]);
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
                return default(T);
            }
        }

        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator()
            => Contents
                .Select(p => new KeyValuePair<string, object>(p.Key, p.Value.ValueObject))
                .GetEnumerator();
    }
}
