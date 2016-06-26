using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace ValveResourceFormat.ResourceTypes.NTROSerialization
{
    public class NTROStruct : IDictionary
    {
        private readonly Dictionary<string, NTROValue> Contents;
        public string Name { get; private set; }

        public NTROStruct(string name)
        {
            Name = name;
            Contents = new Dictionary<string, NTROValue>();
        }

        public void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine(Name);
            writer.WriteLine("{");
            writer.Indent++;

            foreach (var entry in Contents)
            {
                var array = entry.Value as NTROArray;

                if (entry.Value.Pointer)
                {
                    writer.Write("{0} {1}* = (ptr) ->", ValveDataType(entry.Value.Type), entry.Key);
                    entry.Value.WriteText(writer);
                }
                else if (array != null)
                {
                    // TODO: This is matching Valve's incosistency
                    if (array.Type == DataType.Byte && array.IsIndirection)
                    {
                        writer.WriteLine("{0}[{2}] {1} =", ValveDataType(array.Type), entry.Key, array.Count);
                    }
                    else
                    {
                        writer.WriteLine("{0} {1}[{2}] =", ValveDataType(array.Type), entry.Key, array.Count);
                    }

                    writer.WriteLine("[");
                    writer.Indent++;

                    foreach (var innerEntry in array)
                    {
                        if (array.Type == DataType.Byte && array.IsIndirection)
                        {
                            writer.WriteLine("{0:X2}", (innerEntry as NTROValue<byte>).Value);
                        }
                        else
                        {
                            innerEntry.WriteText(writer);
                        }
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
            using (var output = new StringWriter())
            using (var writer = new IndentedTextWriter(output, "\t"))
            {
                WriteText(writer);

                return output.ToString();
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
            get
            {
                return Contents[key];
            }
            set
            {
                Contents[key] = value;
            }
        }

        public object this[object key]
        {
            get
            {
                return ((IDictionary)Contents)[key];
            }
            set
            {
                ((IDictionary)Contents)[key] = value;
            }
        }

        public int Count
        {
            get
            {
                return Contents.Count;
            }
        }

        public bool IsFixedSize
        {
            get
            {
                return ((IDictionary)Contents).IsFixedSize;
            }
        }

        public bool IsReadOnly
        {
            get
            {
                return ((IDictionary)Contents).IsReadOnly;
            }
        }

        public bool IsSynchronized
        {
            get
            {
                return ((IDictionary)Contents).IsSynchronized;
            }
        }

        public ICollection Keys
        {
            get
            {
                return Contents.Keys;
            }
        }

        public object SyncRoot
        {
            get
            {
                return ((IDictionary)Contents).SyncRoot;
            }
        }

        public ICollection Values
        {
            get
            {
                return Contents.Values;
            }
        }

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

        public T Get<T>(object key)
        {
            if (typeof(T) == typeof(NTROArray))
            {
                return (T)this[key];
            }

            return ((NTROValue<T>)this[key]).Value;
        }
    }
}
