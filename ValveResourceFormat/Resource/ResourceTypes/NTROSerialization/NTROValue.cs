using System;
using System.Numerics;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.ResourceTypes.NTROSerialization
{
    public abstract class NTROValue
    {
        public DataType Type { get; protected set; }
        public bool Pointer { get; protected set; }
        public abstract void WriteText(IndentedTextWriter writer);
    }

#pragma warning disable SA1402 // File may only contain a single type
    public class NTROValue<T> : NTROValue
#pragma warning restore SA1402
    {
        public T Value { get; private set; }

        public NTROValue(DataType type, T value, bool pointer = false)
        {
            Type = type;
            Value = value;
            Pointer = pointer;
        }

        public override string ToString()
        {
            using (var writer = new IndentedTextWriter())
            {
                WriteText(writer);

                return writer.ToString();
            }
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            if (Value == null)
            {
                if (Type == DataType.ExternalReference)
                {
                    writer.WriteLine("ID: {0:X16}", 0);
                    return;
                }

                writer.WriteLine("NULL");
                return;
            }

            switch (Type)
            {
                case DataType.Enum:
                    // TODO: Lookup in ReferencedEnums
                    writer.WriteLine("0x{0:X8}", Value);
                    break;

                case DataType.Byte: // TODO: Valve print it as hex, why?
                    writer.WriteLine("0x{0:X2}", Value);
                    break;

                case DataType.Boolean:
                    // Booleans ToString() returns "True" and "False", we want "true" and "false"
                    writer.WriteLine(Value.ToString().ToLower());
                    break;

                case DataType.UInt16: // TODO: Valve print it as hex, why?
                    writer.WriteLine("0x{0:X4}", Value);
                    break;

                case DataType.UInt32: // TODO: Valve print it as hex, why?
                    writer.WriteLine("0x{0:X8}", Value);
                    break;

                case DataType.Float:
                    writer.WriteLine("{0:F6}", Value);
                    break;

                case DataType.UInt64: // TODO: Valve print it as hex, why?
                    writer.WriteLine("0x{0:X16}", Value);
                    break;

                case DataType.ExternalReference:
                    var refInfo = Value as ResourceExtRefList.ResourceReferenceInfo;

                    writer.WriteLine("ID: {0:X16}", refInfo.Id);
                    break;

                case DataType.Quaternion:
                case DataType.Color:
                case DataType.Fltx4:
                case DataType.Vector4D:
                case DataType.Vector4D_44:
                    var vector4 = (Value as Vector4?).Value;

                    if (Type == DataType.Quaternion)
                    {
                        writer.WriteLine("{{x: {0:F2}, y: {1:F2}, z: {2:F2}, w: {3}}}", vector4.X, vector4.Y, vector4.Z, vector4.W.ToString("F2"));
                    }
                    else
                    {
                        writer.WriteLine("({0:F6}, {1:F6}, {2:F6}, {3:F6})", vector4.X, vector4.Y, vector4.Z, vector4.W);
                    }

                    break;

                case DataType.String4:
                case DataType.String:
                    writer.WriteLine("\"{0}\"", Value);
                    break;

                // Stuff we can let our generic value ToString() handle.
                case DataType.Int16:
                case DataType.Int32:
                case DataType.Int64:
                case DataType.Vector:
                case DataType.SByte:
                case DataType.CTransform:
                    writer.WriteLine(Value);
                    break;

                case DataType.Matrix3x4:
                case DataType.Matrix3x4a:
                    (Value as Matrix3x4).WriteText(writer);
                    break;

                case DataType.Struct:
                    (Value as NTROStruct).WriteText(writer);
                    break;

                default:
                    throw new NotImplementedException(string.Format("Unknown data type: {0}", Value.GetType()));
            }
        }
    }
}
