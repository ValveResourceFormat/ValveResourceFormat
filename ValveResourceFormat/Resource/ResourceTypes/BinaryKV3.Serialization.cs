using System;
using System.Globalization;
using System.IO;
using ValveResourceFormat.Serialization.KeyValues;
using KVValueType = ValveKeyValue.KVValueType;

#nullable disable

namespace ValveResourceFormat.ResourceTypes
{
    public partial class BinaryKV3
    {
        private class SerializationContext : IDisposable
        {
            // TODO: Remove the extra list
            public Dictionary<string, int> StringMap = [];
            public List<string> Strings = [];
            public MemoryStream Bytes1 = new();
            public MemoryStream Bytes4 = new();
            public MemoryStream Bytes8 = new();
            public MemoryStream Types = new();

            public BinaryWriter Bytes1Writer;
            public BinaryWriter Bytes4Writer;
            public BinaryWriter Bytes8Writer;
            public BinaryWriter TypesWriter;

            public SerializationContext()
            {
                Bytes1Writer = new BinaryWriter(Bytes1, System.Text.Encoding.UTF8, leaveOpen: true);
                Bytes4Writer = new BinaryWriter(Bytes4, System.Text.Encoding.UTF8, leaveOpen: true);
                Bytes8Writer = new BinaryWriter(Bytes8, System.Text.Encoding.UTF8, leaveOpen: true);
                TypesWriter = new BinaryWriter(Types, System.Text.Encoding.UTF8, leaveOpen: true);
            }

            public int GetStringId(string str)
            {
                if (string.IsNullOrEmpty(str))
                {
                    return -1;
                }

                if (!StringMap.TryGetValue(str, out var id))
                {
                    id = Strings.Count;
                    Strings.Add(str);
                    StringMap[str] = id;
                }

                return id;
            }

            public void Dispose()
            {
                Bytes1Writer?.Dispose();
                Bytes4Writer?.Dispose();
                Bytes8Writer?.Dispose();
                TypesWriter?.Dispose();
                Bytes1?.Dispose();
                Bytes4?.Dispose();
                Bytes8?.Dispose();
                Types?.Dispose();
            }
        }

        /// <summary>
        /// Serialize KeyValues3 to binary keyvalues version 1.
        /// </summary>
        /// <param name="stream">Stream to write to.</param>
        public void Serialize(Stream stream)
        {
            if (Data == null)
            {
                throw new InvalidOperationException("No data to serialize");
            }

            using var context = new SerializationContext();

            context.Bytes4Writer.Write(0xDEADBEEF); // string count, will be updated

            WriteValue(Data, context, isRoot: true);

            context.Bytes4.Position = 0;
            context.Bytes4Writer.Write(context.Strings.Count);

            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

            WriteHeader(writer, context);

            var start = stream.Position;
            writer.Write(0xDEADBEEF); // uncompressed size, will be overwritten

            WriteData(writer, context);
            var end = stream.Position;

            // Go back and write uncompressed size
            var dataSize = (uint)(end - start);
            stream.Position = start;
            writer.Write(dataSize);
            stream.Position = end;

            // Finish with a trailer
            writer.Write(0xFFEEDD00);
        }

        private void WriteValue(KVObject obj, SerializationContext context, bool isRoot = false, KVFlag flag = KVFlag.None)
        {
            if (isRoot)
            {
                WriteType(context, KV3BinaryNodeType.OBJECT, flag);
                context.Bytes4Writer.Write(obj.Properties.Count);

                foreach (var property in obj.Properties)
                {
                    WriteProperty(property.Key, property.Value, context);
                }
            }
            else
            {
                if (obj.IsArray)
                {
                    context.Bytes4Writer.Write(obj.Properties.Count);
                    WriteType(context, KV3BinaryNodeType.ARRAY, flag);

                    foreach (var property in obj.Properties)
                    {
                        WriteValueRecursive(property.Value, context);
                    }
                }
                else
                {
                    context.Bytes4Writer.Write(obj.Properties.Count);
                    WriteType(context, KV3BinaryNodeType.OBJECT, flag);

                    foreach (var property in obj.Properties)
                    {
                        WriteProperty(property.Key, property.Value, context);
                    }
                }
            }
        }

        private void WriteProperty(string name, KVValue value, SerializationContext context)
        {
            context.Bytes4Writer.Write(context.GetStringId(name));
            WriteValueRecursive(value, context);
        }

        private void WriteValueRecursive(KVValue value, SerializationContext context)
        {
            if (value.Type == KVValueType.Boolean)
            {
                if ((bool)value.Value)
                {
                    WriteType(context, KV3BinaryNodeType.BOOLEAN_TRUE, value.Flag);
                }
                else
                {
                    WriteType(context, KV3BinaryNodeType.BOOLEAN_FALSE, value.Flag);
                }

                return;
            }

            var nodeType = GetKV3BinaryNodeType(value);
            WriteType(context, nodeType, value.Flag);

            // TODO: Support writing optimized 0/1 values
            switch (value.Type)
            {
                case KVValueType.Null:
                    break;
                case KVValueType.Int16:
                    context.Bytes4Writer.Write(Convert.ToInt32(value.Value, CultureInfo.InvariantCulture));
                    break;
                case KVValueType.UInt16:
                    context.Bytes4Writer.Write(Convert.ToUInt32(value.Value, CultureInfo.InvariantCulture));
                    break;
                case KVValueType.Int32:
                    context.Bytes4Writer.Write(Convert.ToInt32(value.Value, CultureInfo.InvariantCulture));
                    break;
                case KVValueType.UInt32:
                    context.Bytes4Writer.Write(Convert.ToUInt32(value.Value, CultureInfo.InvariantCulture));
                    break;
                case KVValueType.Int64:
                    context.Bytes8Writer.Write(Convert.ToInt64(value.Value, CultureInfo.InvariantCulture));
                    break;
                case KVValueType.UInt64:
                    context.Bytes8Writer.Write(Convert.ToUInt64(value.Value, CultureInfo.InvariantCulture));
                    break;
#if false // TODO: Needs v4 for floats
                case KVValueType.FloatingPoint:
                    context.Bytes4Writer.Write(Convert.ToSingle(value.Value, CultureInfo.InvariantCulture));
                    break;
#endif
                case KVValueType.FloatingPoint:
                case KVValueType.FloatingPoint64:
                    context.Bytes8Writer.Write(Convert.ToDouble(value.Value, CultureInfo.InvariantCulture));
                    break;
                case KVValueType.String:
                    context.Bytes4Writer.Write(context.GetStringId((string)value.Value));
                    break;
                case KVValueType.BinaryBlob:
                    var bytes = (byte[])value.Value;
                    context.Bytes4Writer.Write(bytes.Length);
                    if (bytes.Length > 0)
                    {
                        context.Bytes1Writer.Write(bytes);
                    }

                    break;
                case KVValueType.Collection:
                    context.Bytes4Writer.Write(((KVObject)value.Value).Properties.Count);

                    foreach (var property in ((KVObject)value.Value).Properties)
                    {
                        WriteProperty(property.Key, property.Value, context);
                    }
                    break;
                case KVValueType.Array:
                    context.Bytes4Writer.Write(((KVObject)value.Value).Properties.Count);

                    foreach (var property in ((KVObject)value.Value).Properties)
                    {
                        WriteValueRecursive(property.Value, context);
                    }
                    break;
                default:
                    throw new NotSupportedException($"Unsupported value type: {value.Type}");
            }
        }

        private static KV3BinaryNodeType GetKV3BinaryNodeType(KVValue value)
        {
            return value.Type switch
            {
                KVValueType.Null => KV3BinaryNodeType.NULL,
                KVValueType.Boolean => KV3BinaryNodeType.BOOLEAN,
                KVValueType.Int16 => KV3BinaryNodeType.INT16,
                KVValueType.UInt16 => KV3BinaryNodeType.UINT16,
                KVValueType.Int32 => KV3BinaryNodeType.INT32,
                KVValueType.UInt32 => KV3BinaryNodeType.UINT32,
                KVValueType.Int64 => KV3BinaryNodeType.INT64,
                KVValueType.UInt64 => KV3BinaryNodeType.UINT64,
                KVValueType.FloatingPoint => KV3BinaryNodeType.DOUBLE, // TODO: Needs v4 for floats
                KVValueType.FloatingPoint64 => KV3BinaryNodeType.DOUBLE,
                KVValueType.String => KV3BinaryNodeType.STRING,
                KVValueType.BinaryBlob => KV3BinaryNodeType.BINARY_BLOB,
                KVValueType.Array => KV3BinaryNodeType.ARRAY,
                KVValueType.Collection => KV3BinaryNodeType.OBJECT,
                _ => throw new NotSupportedException($"Unsupported value type: {value.Type}")
            };
        }

        private static void WriteType(SerializationContext context, KV3BinaryNodeType type, KVFlag flag = KVFlag.None)
        {
            var typeByte = (byte)type;

            if (flag != KVFlag.None)
            {
                typeByte |= 0x80;
            }

            context.TypesWriter.Write(typeByte);

            if (flag != KVFlag.None)
            {
                var version1Flag = ConvertFlagToVersion1(flag);
                context.TypesWriter.Write((byte)version1Flag);
            }
        }

        private static int ConvertFlagToVersion1(KVFlag flag)
        {
            return flag switch
            {
                KVFlag.None => 0,
                KVFlag.Resource => 1,
                KVFlag.ResourceName => 2,
                KVFlag.Panorama => 8,
                KVFlag.SoundEvent => 16,
                KVFlag.SubClass => 32,
                _ => 0,
            };
        }

        private void WriteHeader(BinaryWriter writer, SerializationContext context)
        {
            writer.Write(MAGIC1);
            writer.Write(Format.ToByteArray());
            writer.Write(0); // 0 = no compression
            writer.Write((int)context.Bytes1.Length);
            writer.Write((int)context.Bytes4.Length / 4);
            writer.Write((int)context.Bytes8.Length / 8);
        }

        private static void WriteData(BinaryWriter writer, SerializationContext context)
        {
            // We're aligning inside of the compressed data block (even though we don't compress)
            var offset = (int)context.Bytes1.Length;

            if (context.Bytes1.Length > 0)
            {
                context.Bytes1.WriteTo(writer.BaseStream);
            }

            if (context.Bytes4.Length > 0)
            {
                AlignWriter(ref offset, writer, 4);
                context.Bytes4.WriteTo(writer.BaseStream);
                offset += (int)context.Bytes4.Length;
            }

            if (context.Bytes8.Length > 0)
            {
                AlignWriter(ref offset, writer, 8);
                context.Bytes8.WriteTo(writer.BaseStream);
                offset += (int)context.Bytes8.Length;
            }
            else
            {
                // For version 1 (< 5), align even when empty
                AlignWriter(ref offset, writer, 8);
            }

            foreach (var str in context.Strings)
            {
                var strBytes = System.Text.Encoding.UTF8.GetBytes(str);
                writer.Write(strBytes);
                writer.Write((byte)0);
            }

            context.Types.WriteTo(writer.BaseStream);
        }

        private static int CalculateStringBytes(SerializationContext context)
        {
            var total = 0;
            foreach (var str in context.Strings)
            {
                total += System.Text.Encoding.UTF8.GetByteCount(str) + 1; // +1 for null terminator
            }
            return total;
        }

        private static void AlignWriter(ref int offset, BinaryWriter writer, int alignment)
        {
            var originalOffset = offset;
            Align(ref offset, alignment);
            var padding = offset - originalOffset;

            for (var i = 0; i < padding; i++)
            {
                writer.Write((byte)0);
            }
        }
    }
}
