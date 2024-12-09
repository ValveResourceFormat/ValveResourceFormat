using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ValveResourceFormat.Serialization.KeyValues
{
    //Different type of value blocks for KeyValues (All in use for KV3)
#pragma warning disable CA1028 // Enum Storage should be Int32
    public enum KVType : byte
#pragma warning restore CA1028
    {
        STRING_MULTI = 0, // STRING_MULTI doesn't have an ID
        NULL = 1,
        BOOLEAN = 2,
        INT64 = 3,
        UINT64 = 4,
        DOUBLE = 5,
        STRING = 6,
        BINARY_BLOB = 7,
        ARRAY = 8,
        OBJECT = 9,
        ARRAY_TYPED = 10,
        INT32 = 11,
        UINT32 = 12,
        BOOLEAN_TRUE = 13,
        BOOLEAN_FALSE = 14,
        INT64_ZERO = 15,
        INT64_ONE = 16,
        DOUBLE_ZERO = 17,
        DOUBLE_ONE = 18,
        FLOAT = 19,
        INT16 = 20,
        UINT16 = 21,
        UNKNOWN_22 = 22,
        INT32_AS_BYTE = 23,
        ARRAY_TYPE_BYTE_LENGTH = 24,
    }

    /// <summary>
    /// Class to hold type + value
    /// </summary>
    public class KVValue
    {
        public KVType Type { get; private set; }
        public object Value { get; private set; }

        public KVValue(KVType type, object value)
        {
            Type = type;
            Value = value;
        }

        //Print a value in the correct representation
        public void PrintValue(IndentedTextWriter writer)
        {
            if (this is KVFlaggedValue flagValue)
            {
                switch (flagValue.Flag)
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
                        throw new InvalidOperationException($"Trying to print unknown keyvalues flag ({flagValue.Flag})");
                }
            }

            switch (Type)
            {
                case KVType.OBJECT:
                case KVType.ARRAY:
                    ((KVObject)Value).Serialize(writer);
                    break;
                case KVType.STRING:
                    writer.Write("\"");
                    writer.Write(EscapeUnescaped((string)Value, '"'));
                    writer.Write("\"");
                    break;
                case KVType.STRING_MULTI:
                    writer.Write("\"\"\"\n");
                    writer.Write((string)Value);
                    writer.Write("\n\"\"\"");
                    break;
                case KVType.BOOLEAN:
                    writer.Write((bool)Value ? "true" : "false");
                    break;
                case KVType.FLOAT:
                    writer.Write(Convert.ToSingle(Value, CultureInfo.InvariantCulture).ToString("#0.000000", CultureInfo.InvariantCulture));
                    break;
                case KVType.DOUBLE:
                    writer.Write(Convert.ToDouble(Value, CultureInfo.InvariantCulture).ToString("#0.000000", CultureInfo.InvariantCulture));
                    break;
                case KVType.INT64:
                    writer.Write(Convert.ToInt64(Value, CultureInfo.InvariantCulture));
                    break;
                case KVType.UINT64:
                    writer.Write(Convert.ToUInt64(Value, CultureInfo.InvariantCulture));
                    break;
                case KVType.INT32:
                    writer.Write(Convert.ToInt32(Value, CultureInfo.InvariantCulture));
                    break;
                case KVType.UINT32:
                    writer.Write(Convert.ToUInt32(Value, CultureInfo.InvariantCulture));
                    break;
                case KVType.INT16:
                    writer.Write(Convert.ToInt16(Value, CultureInfo.InvariantCulture));
                    break;
                case KVType.UINT16:
                    writer.Write(Convert.ToUInt16(Value, CultureInfo.InvariantCulture));
                    break;
                case KVType.NULL:
                    writer.Write("null");
                    break;
                case KVType.BINARY_BLOB:
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
