using System;
using System.Numerics;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.Serialization.NTRO
{
    public abstract class NTROValue
    {
        public DataType Type { get; protected set; }
        public bool Pointer { get; protected set; }
        public abstract void WriteText(IndentedTextWriter writer);

        public abstract object ValueObject { get; }
    }

    public class NTROValue<T> : NTROValue
    {
        public T Value { get; private set; }

        public override object ValueObject => Value as object;

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
                    writer.WriteLine($"resource:\"{Value}\"");
                    break;

                case DataType.Color:
                case DataType.Fltx4:
                case DataType.Vector4D:
                case DataType.Vector4D_44:
                    var vector4 = (Value as NTROStruct).ToVector4();
                    writer.WriteLine("({0:F6}, {1:F6}, {2:F6}, {3:F6})", vector4.X, vector4.Y, vector4.Z, vector4.W);
                    break;

                case DataType.Quaternion:
                    var quaternion = (Value as NTROStruct).ToQuaternion();
                    writer.WriteLine("{{x: {0:F2}, y: {1:F2}, z: {2:F2}, w: {3}}}", quaternion.X, quaternion.Y, quaternion.Z, quaternion.W.ToString("F2"));
                    break;

                case DataType.Vector:
                    var vector3 = (Value as NTROStruct).ToVector3();
                    writer.WriteLine($"({vector3.X:F6}, {vector3.Y:F6}, {vector3.Z:F6})");
                    break;

                case DataType.String4:
                case DataType.String:
                    writer.WriteLine("\"{0}\"", Value);
                    break;

                // Stuff we can let our generic value ToString() handle.
                case DataType.Int16:
                case DataType.Int32:
                case DataType.Int64:
                case DataType.SByte:
                case DataType.CTransform:
                    writer.WriteLine(Value);
                    break;

                case DataType.Matrix2x4:
                    var matrix2x4 = Value as NTROStruct;
                    writer.WriteLine();
                    writer.WriteLine($"{matrix2x4.GetFloatProperty("0"):F4} {matrix2x4.GetFloatProperty("1"):F4} {matrix2x4.GetFloatProperty("2"):F4} {matrix2x4.GetFloatProperty("3"):F4}");
                    writer.WriteLine($"{matrix2x4.GetFloatProperty("4"):F4} {matrix2x4.GetFloatProperty("5"):F4} {matrix2x4.GetFloatProperty("6"):F4} {matrix2x4.GetFloatProperty("7"):F4}");
                    break;

                case DataType.Matrix3x4:
                case DataType.Matrix3x4a:
                    var matrix3x4 = Value as NTROStruct;
                    writer.WriteLine();
                    writer.WriteLine($"{matrix3x4.GetFloatProperty("0"):F4} {matrix3x4.GetFloatProperty("1"):F4} {matrix3x4.GetFloatProperty("2"):F4} {matrix3x4.GetFloatProperty("3"):F4}");
                    writer.WriteLine($"{matrix3x4.GetFloatProperty("4"):F4} {matrix3x4.GetFloatProperty("5"):F4} {matrix3x4.GetFloatProperty("6"):F4} {matrix3x4.GetFloatProperty("7"):F4}");
                    writer.WriteLine($"{matrix3x4.GetFloatProperty("8"):F4} {matrix3x4.GetFloatProperty("9"):F4} {matrix3x4.GetFloatProperty("10"):F4} {matrix3x4.GetFloatProperty("11"):F4}");
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
