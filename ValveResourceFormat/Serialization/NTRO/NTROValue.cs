using System.Globalization;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Serialization.NTRO
{
    public abstract class NTROValue
    {
        public SchemaFieldType Type { get; protected set; }
        public bool Pointer { get; protected set; }
        public abstract KVValue ToKVValue();
        public abstract void WriteText(IndentedTextWriter writer);

        public abstract object ValueObject { get; }
    }

    public class NTROValue<T> : NTROValue
    {
        public T Value { get; private set; }

        public override object ValueObject => Value as object;

        public NTROValue(SchemaFieldType type, T value, bool pointer = false)
        {
            Type = type;
            Value = value;
            Pointer = pointer;
        }

        public override KVValue ToKVValue()
        {
            return Type switch
            {
                SchemaFieldType.Struct => new KVValue(KVType.OBJECT, (Value as NTROStruct).ToKVObject()),
                SchemaFieldType.Enum => new KVValue(KVType.UINT64, Value),
                SchemaFieldType.Char => new KVValue(KVType.STRING, Value),
                SchemaFieldType.SByte => new KVValue(KVType.INT64, Value),
                SchemaFieldType.Byte => new KVValue(KVType.UINT64, Value),
                SchemaFieldType.Int16 => new KVValue(KVType.INT64, Value),
                SchemaFieldType.UInt16 => new KVValue(KVType.UINT64, Value),
                SchemaFieldType.Int32 => new KVValue(KVType.INT64, Value),
                SchemaFieldType.UInt32 => new KVValue(KVType.UINT64, Value),
                SchemaFieldType.Int64 => new KVValue(KVType.INT64, Value),
                SchemaFieldType.UInt64 => new KVValue(KVType.UINT64, Value),
                SchemaFieldType.Float => new KVValue(KVType.DOUBLE, (double)(float)(object)Value),
                SchemaFieldType.Vector2D => MakeArray<float>(Value, Type, KVType.DOUBLE, 2),
                SchemaFieldType.Vector3D => MakeArray<float>(Value, Type, KVType.DOUBLE, 3),
                SchemaFieldType.Vector4D => MakeArray<float>(Value, Type, KVType.DOUBLE, 4),
                SchemaFieldType.Color => MakeArray<byte>(Value, Type, KVType.INT64, 4),
                SchemaFieldType.Boolean => new KVValue(KVType.BOOLEAN, Value),
                SchemaFieldType.ResourceString => new KVValue(KVType.STRING, Value),
                SchemaFieldType.ExternalReference => new KVFlaggedValue(KVType.STRING, KVFlag.ResourceName, Value),
                _ => throw new NotImplementedException($"Converting {Type} to keyvalues is not implemented."),
            };

            static KVValue MakeArray<StructMembersType>(T value, SchemaFieldType type, KVType kvValuesType, int num)
            {
                if (value is not NTROStruct structValue)
                {
                    throw new InvalidOperationException($"Can only make array from a NTROStruct value, not ({typeof(T)}).");
                }

                var arrayObject = new KVObject(type.ToString(), true, capacity: num);
                for (var i = 0; i < num; i++)
                {
                    var index = i.ToString(CultureInfo.InvariantCulture);
                    if (kvValuesType == KVType.DOUBLE)
                    {
                        arrayObject.AddProperty(null, new KVValue(kvValuesType, structValue.GetDoubleProperty(index)));
                        continue;
                    }

                    arrayObject.AddProperty(null, new KVValue(kvValuesType, structValue.GetProperty<StructMembersType>(index)));
                }

                return new KVValue(KVType.ARRAY, arrayObject);
            }
        }

        public override string ToString()
        {
            using var writer = new IndentedTextWriter();
            WriteText(writer);

            return writer.ToString();
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
                case SchemaFieldType.Enum:
                    // TODO: Lookup in ReferencedEnums
                    writer.WriteLine("0x{0:X8}", Value);
                    break;

                case SchemaFieldType.Byte: // TODO: Valve print it as hex, why?
                    writer.WriteLine("0x{0:X2}", Value);
                    break;

                case SchemaFieldType.Boolean:
                    // Booleans ToString() returns "True" and "False", we want "true" and "false"
                    writer.WriteLine(Value.ToString().ToLowerInvariant());
                    break;

                case SchemaFieldType.UInt16: // TODO: Valve print it as hex, why?
                    writer.WriteLine("0x{0:X4}", Value);
                    break;

                case SchemaFieldType.UInt32: // TODO: Valve print it as hex, why?
                    writer.WriteLine("0x{0:X8}", Value);
                    break;

                case SchemaFieldType.Float:
                    writer.WriteLine("{0:F6}", Value);
                    break;

                case SchemaFieldType.UInt64: // TODO: Valve print it as hex, why?
                    writer.WriteLine("0x{0:X16}", Value);
                    break;

                case SchemaFieldType.ExternalReference:
                    writer.WriteLine($"resource:\"{Value}\"");
                    break;

                case SchemaFieldType.Color:
                    var color = Value as NTROStruct;
                    writer.WriteLine("({0}, {1}, {2}, {3})", color.GetProperty<byte>("0"), color.GetProperty<byte>("1"), color.GetProperty<byte>("2"), color.GetProperty<byte>("3"));
                    break;

                case SchemaFieldType.Fltx4:
                case SchemaFieldType.Vector4D:
                case SchemaFieldType.FourVectors:
                    var vector4 = (Value as NTROStruct).ToVector4();
                    writer.WriteLine("({0:F6}, {1:F6}, {2:F6}, {3:F6})", vector4.X, vector4.Y, vector4.Z, vector4.W);
                    break;

                case SchemaFieldType.Quaternion:
                    var quaternion = (Value as NTROStruct).ToQuaternion();
                    writer.WriteLine("{{x: {0:F2}, y: {1:F2}, z: {2:F2}, w: {3}}}", quaternion.X, quaternion.Y, quaternion.Z, quaternion.W.ToString("F2", CultureInfo.InvariantCulture));
                    break;

                case SchemaFieldType.Vector3D:
                    var vector3 = (Value as NTROStruct).ToVector3();
                    writer.WriteLine($"({vector3.X:F6}, {vector3.Y:F6}, {vector3.Z:F6})");
                    break;

                case SchemaFieldType.Char:
                case SchemaFieldType.ResourceString:
                    writer.WriteLine("\"{0}\"", Value);
                    break;

                // Stuff we can let our generic value ToString() handle.
                case SchemaFieldType.Int16:
                case SchemaFieldType.Int32:
                case SchemaFieldType.Int64:
                case SchemaFieldType.SByte:
                case SchemaFieldType.Transform:
                    writer.WriteLine(Value);
                    break;

                case SchemaFieldType.Vector2D:
                    var vector2 = (Value as NTROStruct).ToVector2();
                    writer.WriteLine($"({vector2.X:F6}, {vector2.Y:F6})");
                    break;

                case SchemaFieldType.Matrix3x4:
                case SchemaFieldType.Matrix3x4a:
                    var matrix3x4 = Value as NTROStruct;
                    writer.WriteLine();
                    writer.WriteLine($"{matrix3x4.GetFloatProperty("0"):F4} {matrix3x4.GetFloatProperty("1"):F4} {matrix3x4.GetFloatProperty("2"):F4} {matrix3x4.GetFloatProperty("3"):F4}");
                    writer.WriteLine($"{matrix3x4.GetFloatProperty("4"):F4} {matrix3x4.GetFloatProperty("5"):F4} {matrix3x4.GetFloatProperty("6"):F4} {matrix3x4.GetFloatProperty("7"):F4}");
                    writer.WriteLine($"{matrix3x4.GetFloatProperty("8"):F4} {matrix3x4.GetFloatProperty("9"):F4} {matrix3x4.GetFloatProperty("10"):F4} {matrix3x4.GetFloatProperty("11"):F4}");
                    break;

                case SchemaFieldType.Struct:
                    (Value as NTROStruct).WriteText(writer);
                    break;

                default:
                    throw new UnexpectedMagicException("Unknown data type", (int)Type, nameof(Type));
            }
        }
    }
}
