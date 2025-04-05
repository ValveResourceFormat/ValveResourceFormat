using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using KVValueType = ValveKeyValue.KVValueType;

namespace ValveResourceFormat.Serialization.KeyValues
{
#pragma warning disable CA1028 // Enum Storage should be Int32
    public enum KVFlag : byte
#pragma warning restore CA1028
    {
        None = 0,
        Resource = 1,
        ResourceName = 2,
        Panorama = 3,
        SoundEvent = 4,
        SubClass = 5,
        // March 2023: There are more types available in the S2 binaries, but they should not be persisted.
    }

    /// <summary>
    /// Structure to hold type + flag + value
    /// </summary>
    public struct KVValue
    {
        public KVValueType Type { get; private set; }
        public KVFlag Flag { get; private set; }
        public object Value { get; private set; }

        public KVValue(KVValueType type, object value)
        {
            Type = type;
            Value = value;
        }

        public KVValue(KVValueType type, KVFlag flag, object value)
        {
            Type = type;
            Flag = flag;
            Value = value;
        }

        public KVValue(object value)
        {
            if (value is KVValue v)
            {
                Type = v.Type;
                Value = v.Value;
                // note: we remove the flag for decompilation purposes
                // we should not be hitting this path when parsing from binary resources
                Flag = KVFlag.None;
            }
            else if (value is Vector3 vec3)
            {
                Type = KVValueType.Array;
                Value = MakeArray([vec3.X, vec3.Y, vec3.Z]).Value;
            }
            else
            {
                Type = value switch
                {
                    string => KVValueType.String,
                    bool => KVValueType.Boolean,
                    int => KVValueType.Int32,
                    uint => KVValueType.UInt32,
                    long => KVValueType.Int64,
                    float => KVValueType.FloatingPoint,
                    double => KVValueType.FloatingPoint64,
                    KVObject kv => kv.IsArray ? KVValueType.Array : KVValueType.Collection,
                    null => KVValueType.Null,
                    _ => throw new NotImplementedException()
                };
                Value = value;
            }
        }

        internal static KVValue MakeArray<T>(IEnumerable<T> values)
        {
            var list = new KVObject(null, isArray: true);
            foreach (var value in values)
            {
                list.AddProperty(null, new KVValue(value));
            }

            return new KVValue(KVValueType.Array, list);
        }

        //Print a value in the correct representation
        public void PrintValue(IndentedTextWriter writer)
        {
            if (Flag != KVFlag.None)
            {
                switch (Flag)
                {
                    case KVFlag.Resource:
                        writer.Write("resource:");
                        break;
                    case KVFlag.ResourceName:
                        writer.Write("resource_name:");
                        break;
                    case KVFlag.Panorama:
                        writer.Write("panorama:");
                        break;
                    case KVFlag.SoundEvent:
                        writer.Write("soundevent:");
                        break;
                    case KVFlag.SubClass:
                        writer.Write("subclass:");
                        break;
                    default:
                        throw new InvalidOperationException($"Trying to print unknown keyvalues flag ({Flag})");
                }
            }

            switch (Type)
            {
                case KVValueType.Collection:
                case KVValueType.Array:
                    ((KVObject)Value).Serialize(writer);
                    break;
                case KVValueType.String:
                    {
                        var text = (string)Value;
                        var isMultiline = text.Contains('\n', StringComparison.Ordinal);

                        if (isMultiline)
                        {
                            writer.Write("\"\"\"\n");
                            writer.Write(text);
                            writer.Write("\n\"\"\"");
                        }
                        else
                        {
                            writer.Write("\"");
                            writer.Write(EscapeUnescaped(text, '"'));
                            writer.Write("\"");
                        }
                        break;
                    }
                case KVValueType.Boolean:
                    writer.Write((bool)Value ? "true" : "false");
                    break;
                case KVValueType.FloatingPoint:
                    writer.Write(Convert.ToSingle(Value, CultureInfo.InvariantCulture).ToString("#0.000000", CultureInfo.InvariantCulture));
                    break;
                case KVValueType.FloatingPoint64:
                    writer.Write(Convert.ToDouble(Value, CultureInfo.InvariantCulture).ToString("#0.000000", CultureInfo.InvariantCulture));
                    break;
                case KVValueType.Int64:
                    writer.Write(Convert.ToInt64(Value, CultureInfo.InvariantCulture));
                    break;
                case KVValueType.UInt64:
                    writer.Write(Convert.ToUInt64(Value, CultureInfo.InvariantCulture));
                    break;
                case KVValueType.Int32:
                    writer.Write(Convert.ToInt32(Value, CultureInfo.InvariantCulture));
                    break;
                case KVValueType.UInt32:
                    writer.Write(Convert.ToUInt32(Value, CultureInfo.InvariantCulture));
                    break;
                case KVValueType.Int16:
                    writer.Write(Convert.ToInt16(Value, CultureInfo.InvariantCulture));
                    break;
                case KVValueType.UInt16:
                    writer.Write(Convert.ToUInt16(Value, CultureInfo.InvariantCulture));
                    break;
                case KVValueType.Null:
                    writer.Write("null");
                    break;
                case KVValueType.BinaryBlob:
                    var byteArray = (byte[])Value;
                    var count = 0;

                    {
                        // This might be longer than required
                        var lines = byteArray.Length / 32;
                        var size = 12 + byteArray.Length * 3;
                        size += (writer.Indent + 1) * lines;
                        size += Environment.NewLine.Length * lines;
                        writer.Grow(size);
                    }

                    writer.WriteLine();
                    writer.WriteLine("#[");
                    writer.Indent++;

                    foreach (var oneByte in byteArray)
                    {
                        writer.Write(HexToCharUpper(oneByte >> 4));
                        writer.Write(HexToCharUpper(oneByte));

                        if (++count % 32 == 0)
                        {
                            writer.WriteLine();
                        }
                        else
                        {
                            writer.Write(' ');
                        }
                    }

                    writer.Indent--;

                    if (count % 32 != 0)
                    {
                        writer.WriteLine();
                    }

                    writer.Write("]");
                    break;
                default:
                    throw new InvalidOperationException($"Trying to print unknown type '{Type}'");
            }
        }

        private static string EscapeUnescaped(string input, char toEscape)
        {
            if (input.Length == 0)
            {
                return input;
            }

            var index = 1;
            while (true)
            {
                index = input.IndexOf(toEscape, index);

                //Break out of the loop if no more occurrences were found
                if (index == -1)
                {
                    break;
                }

                if (input.ElementAt(index - 1) != '\\')
                {
                    input = input.Insert(index, "\\");
                }

                //Don't read this one again
                index++;
            }

            return input;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static char HexToCharUpper(int value)
        {
            value &= 0xF;
            value += '0';

            if (value > '9')
            {
                value += ('A' - ('9' + 1));
            }

            return (char)value;
        }
    }
}
