using System;
using System.CodeDom.Compiler;
using System.IO;

namespace ValveResourceFormat.ResourceTypes.NTROSerialization
{
    public abstract class NTROValue
    {
        public DataType Type { get; protected set; }
        public bool pointer { get; protected set; }
        public abstract void WriteText(IndentedTextWriter Writer);

    }
    public class NTROValue<T> : NTROValue
    {
        public T value; //Can freely change, right?


        public NTROValue(DataType Type, T value, bool pointer = false)
        {
            this.Type = Type;
            this.value = value;
            this.pointer = pointer;
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
        public override void WriteText(IndentedTextWriter Writer)
        {
            if (value == null)
            {
                Writer.WriteLine("NULL");
                return;
            }
            switch (Type)
            {
                case DataType.Enum:
                    // TODO: Lookup in ReferencedEnums
                    Writer.WriteLine("0x{0:X8}", (value as UInt32?));
                    break;

                case DataType.Byte: // TODO: Valve print it as hex, why?
                    Writer.WriteLine("0x{0:X2}", (value as byte?));
                    break;
                case DataType.Boolean:
                    //Booleans ToString() returns "True" and "False", we want "true" and "false"
                    Writer.WriteLine(value.ToString().ToLower());
                    break;
                case DataType.UInt16: // TODO: Valve print it as hex, why?
                    Writer.WriteLine("0x{0:X4}", (value as UInt16?));
                    break;

                case DataType.UInt32: // TODO: Valve print it as hex, why?
                    Writer.WriteLine("0x{0:X8}", (value as UInt32?));
                    break;

                case DataType.Float:
                    Writer.WriteLine("{0:F6}", (value as Single?));
                    break;

                case DataType.UInt64: // TODO: Valve print it as hex, why?
                    Writer.WriteLine("0x{0:X16}", (value as UInt64?));
                    break;

                case DataType.ExternalReference:
                    Writer.WriteLine("ID: {0:X16}", (value as UInt64?));
                    break;

                case DataType.Quaternion:
                case DataType.Color:
                case DataType.Fltx4:
                case DataType.Vector4D:
                    var vector4 = value as Vector4;
                    if (this.Type == DataType.Quaternion)
                    {
                        Writer.WriteLine("{{x: {0:F}, y: {1:F}, z: {2:F}, w: {3}}}", vector4.field0, vector4.field1, vector4.field2, vector4.field3.ToString("F"));
                    }
                    else
                    {
                        Writer.WriteLine("({0:F6}, {1:F6}, {2:F6}, {3:F6})", vector4.field0, vector4.field1, vector4.field2, vector4.field3);
                    }
                    break;

                case DataType.String4:
                case DataType.String:
                    Writer.WriteLine("\"{0}\"", value as String);
                    break;


                //Stuff we can let our generic value ToString() handle.
                case DataType.Int16:
                case DataType.Int32:
                case DataType.Int64:
                case DataType.Vector:
                case DataType.SByte:
                case DataType.CTransform:
                    Writer.WriteLine(value.ToString());
                    break;
                case DataType.Matrix3x4:
                case DataType.Matrix3x4a:
                    (value as Matrix3x4).WriteText(Writer);
                    break;
                case DataType.Struct:
                    (value as NTROStruct).WriteText(Writer);
                    break;
                default:
                    throw new NotImplementedException(string.Format("Unknown data type: {0}", value.GetType()));
            }
        }
    }
}