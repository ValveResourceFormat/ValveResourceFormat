using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.ResourceTypes.NTROSerialization
{
    public class NTROStruct : IDictionary
    {
        private Dictionary<string, NTROValue> contents;
        public string name { get; private set; }

        public NTROStruct(string name)
        {
            this.name = name;
            contents = new Dictionary<string, NTROValue>();
        }
        public void WriteText(IndentedTextWriter Writer)
        {
            Writer.WriteLine(name);
            Writer.WriteLine("{");
            Writer.Indent++;

            foreach (KeyValuePair<string, NTROValue> entry in contents)
            {
                NTROArray array = entry.Value as NTROArray;
                if (entry.Value.pointer)
                {
                    Writer.Write("{0} {1}* = (ptr) ->", ValveDataType(entry.Value.Type), entry.Key);
                    entry.Value.WriteText(Writer);

                }
                else if (array != null)
                {
                    // TODO: This is matching Valve's incosistency
                    if (array.Type == DataType.Byte && array.IsIndirection)
                    {
                        Writer.WriteLine("{0}[{2}] {1} =", ValveDataType(array.Type), entry.Key, array.Count);
                    }
                    else
                    {
                        Writer.WriteLine("{0} {1}[{2}] =", ValveDataType(array.Type), entry.Key, array.Count);
                    }
                    Writer.WriteLine("[");
                    Writer.Indent++;
                    foreach (var innerEntry in array)
                    {
                        if (array.Type == DataType.Byte && array.IsIndirection)
                        {
                            Writer.WriteLine("{0:X2}", (innerEntry as NTROValue<byte>).value);
                        }
                        else
                        {
                            innerEntry.WriteText(Writer);
                        }
                    }
                    Writer.Indent--;
                    Writer.WriteLine("]");
                }
                else //Can either be NTROArray or NTROValue so...
                {
                    Writer.Write("{0} {1} = ", ValveDataType(entry.Value.Type), entry.Key);
                    entry.Value.WriteText(Writer);
                }
            }

            Writer.Indent--;
            Writer.WriteLine("}");
        }
        public override string ToString()
        {
            using (var output = new StringWriter())
            using (var Writer = new IndentedTextWriter(output, "\t"))
            {
                WriteText(Writer);
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
                return contents[key];
            }

            set
            {
                contents[key] = value;
            }
        }
        public object this[object key] {
            get
            {
                return ((IDictionary)contents)[key];
            }
            set
            {

            }
        }

        public int Count
        {
            get
            {
                return contents.Count;
            }
        }

        public bool IsFixedSize
        {
            get
            {
                return ((IDictionary)contents).IsFixedSize;
            }
        }

        public bool IsReadOnly
        {
            get
            {
                return ((IDictionary)contents).IsReadOnly;
            }
        }

        public bool IsSynchronized
        {
            get
            {
                return ((IDictionary)contents).IsSynchronized;
            }
        }

        public ICollection Keys
        {
            get
            {
                return contents.Keys;
            }
        }

        public object SyncRoot
        {
            get
            {
                return ((IDictionary)contents).SyncRoot;
            }
        }

        public ICollection Values
        {
            get
            {
                return contents.Values;
            }
        }

        public void Add(object key, object value)
        {
            ((IDictionary)contents).Add(key, value);
        }

        public void Clear()
        {
            contents.Clear();
        }

        public bool Contains(object key)
        {
            return ((IDictionary)contents).Contains(key);
        }

        public void CopyTo(Array array, int index)
        {
            ((IDictionary)contents).CopyTo(array, index);
        }

        public IDictionaryEnumerator GetEnumerator()
        {
            return ((IDictionary)contents).GetEnumerator();
        }

        public void Remove(object key)
        {
            ((IDictionary)contents).Remove(key);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IDictionary)contents).GetEnumerator();
        }
    }
}
