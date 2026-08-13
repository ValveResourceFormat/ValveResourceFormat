using System.IO;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Encoders;
using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes
{
    public partial class BinaryKV3
    {
        // LZ4 compressed data is split into chained frames of this size, which is also written into the header
        private const int CompressionFrameSize = 16384;

        private class SerializationContext : IDisposable
        {
            // TODO: Remove the extra list
            public Dictionary<string, int> StringMap = [];
            public List<string> Strings = [];
            public MemoryStream Bytes1 = new();
            public MemoryStream Bytes2 = new();
            public MemoryStream Bytes4 = new();
            public MemoryStream Bytes8 = new();
            public MemoryStream Types = new();
            public MemoryStream ObjectLengths = new();
            public MemoryStream BinaryBlobs = new();
            public List<int> BinaryBlobLengths = [];
            public int CountArrays;

            public BinaryWriter Bytes1Writer;
            public BinaryWriter Bytes2Writer;
            public BinaryWriter Bytes4Writer;
            public BinaryWriter Bytes8Writer;
            public BinaryWriter TypesWriter;
            public BinaryWriter ObjectLengthsWriter;
            public BinaryWriter BinaryBlobsWriter;

            public SerializationContext(int version)
            {
                Bytes1Writer = new BinaryWriter(Bytes1, System.Text.Encoding.UTF8, leaveOpen: true);
                Bytes2Writer = new BinaryWriter(Bytes2, System.Text.Encoding.UTF8, leaveOpen: true);
                Bytes4Writer = new BinaryWriter(Bytes4, System.Text.Encoding.UTF8, leaveOpen: true);
                Bytes8Writer = new BinaryWriter(Bytes8, System.Text.Encoding.UTF8, leaveOpen: true);
                TypesWriter = new BinaryWriter(Types, System.Text.Encoding.UTF8, leaveOpen: true);
                BinaryBlobsWriter = new BinaryWriter(BinaryBlobs, System.Text.Encoding.UTF8, leaveOpen: true);

                // Version 5 stores object member counts in a separate lane, earlier versions interleave them into Bytes4
                ObjectLengthsWriter = version >= 5
                    ? new BinaryWriter(ObjectLengths, System.Text.Encoding.UTF8, leaveOpen: true)
                    : Bytes4Writer;
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
                Bytes2Writer?.Dispose();
                Bytes4Writer?.Dispose();
                Bytes8Writer?.Dispose();
                TypesWriter?.Dispose();
                ObjectLengthsWriter?.Dispose();
                BinaryBlobsWriter?.Dispose();
                Bytes1?.Dispose();
                Bytes2?.Dispose();
                Bytes4?.Dispose();
                Bytes8?.Dispose();
                Types?.Dispose();
                ObjectLengths?.Dispose();
                BinaryBlobs?.Dispose();
            }
        }

        /// <inheritdoc/>
        public override void Serialize(Stream stream)
        {
            if (Data == null)
            {
                throw new InvalidOperationException("No data to serialize");
            }

            if (SerializationVersion is not (4 or 5))
            {
                throw new NotSupportedException($"Unsupported binary KV3 version: {SerializationVersion}");
            }

            if (!Enum.IsDefined(SerializationCompressionMethod))
            {
                throw new NotSupportedException($"Unsupported binary KV3 compression method: {(int)SerializationCompressionMethod}");
            }

            using var context = new SerializationContext(SerializationVersion);

            context.Bytes4Writer.Write(0xDEADBEEF); // string count, will be updated

            WriteValueRecursive(Data, context);

            context.Bytes4.Position = 0;
            context.Bytes4Writer.Write(context.Strings.Count);

            if (SerializationVersion == 5)
            {
                SerializeVersion5(stream, context);
                return;
            }

            SerializeVersion4(stream, context);
        }

        private void WriteHeader(BinaryWriter writer, int magic)
        {
            writer.Write(magic);
            writer.Write(Data.Header!.Format.Id.ToByteArray());
            writer.Write((uint)SerializationCompressionMethod);
            writer.Write((ushort)0); // compressionDictionaryId
            writer.Write(SerializationCompressionMethod == KV3BinaryCompressionMethod.Lz4 ? (ushort)CompressionFrameSize : (ushort)0);
        }

        private void SerializeVersion4(Stream stream, SerializationContext context)
        {
            var blobs = AsSegment(context.BinaryBlobs);
            List<ushort> blockCompressedSizes = [];

            // For Zstd, blobs are appended to the main buffer and compressed together with it
            var compressedBlobs = SerializationCompressionMethod != KV3BinaryCompressionMethod.Zstd
                ? CompressBinaryBlobs(blobs, [blobs.Count], out blockCompressedSizes)
                : default;
            var buffer = BuildVersion4Buffer(context, blockCompressedSizes, out var countTypes);
            var compressedBuffer = SerializationCompressionMethod == KV3BinaryCompressionMethod.Zstd
                ? CompressZstd([.. buffer, .. blobs])
                : CompressMainBuffer(buffer);

            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

            WriteHeader(writer, MAGIC4);
            writer.Write((int)context.Bytes1.Length);
            writer.Write((int)context.Bytes4.Length / 4);
            writer.Write((int)context.Bytes8.Length / 8);
            writer.Write(countTypes);
            writer.Write((ushort)0); // countObjects
            writer.Write((ushort)0); // countArrays
            writer.Write(buffer.Count);
            writer.Write(compressedBuffer.Count);
            writer.Write(context.BinaryBlobLengths.Count);
            writer.Write(blobs.Count);
            writer.Write((int)context.Bytes2.Length / 2);
            writer.Write(0); // sizeBlockCompressedSizesBytes
            writer.Write(compressedBuffer.AsSpan());

            if (context.BinaryBlobLengths.Count > 0)
            {
                if (SerializationCompressionMethod != KV3BinaryCompressionMethod.Zstd)
                {
                    writer.Write(compressedBlobs.AsSpan());
                }

                writer.Write(0xFFEEDD00);
            }
        }

        private static ArraySegment<byte> BuildVersion4Buffer(SerializationContext context, List<ushort> blockCompressedSizes, out int countTypes)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            countTypes = WriteData(writer, context);

            if (context.BinaryBlobLengths.Count == 0)
            {
                writer.Write(0xFFEEDD00);
            }

            foreach (var size in blockCompressedSizes)
            {
                writer.Write(size);
            }

            return AsSegment(stream);
        }

        private void SerializeVersion5(Stream stream, SerializationContext context)
        {
            var buffer1 = BuildVersion5Buffer1(context, out var stringBytesLength);
            var blobs = AsSegment(context.BinaryBlobs);
            List<ushort> blockCompressedSizes = [];
            var compressedBlobs = context.BinaryBlobLengths.Count > 0
                ? CompressBinaryBlobs(blobs, context.BinaryBlobLengths, out blockCompressedSizes)
                : default;
            var buffer2 = BuildVersion5Buffer2(context, blockCompressedSizes);
            var compressedBuffer1 = CompressMainBuffer(buffer1);
            var compressedBuffer2 = CompressMainBuffer(buffer2);
            var compressed = SerializationCompressionMethod != KV3BinaryCompressionMethod.Uncompressed;
            var countObjects = checked((ushort)(context.ObjectLengths.Length / sizeof(int)));
            var countArrays = checked((ushort)context.CountArrays);

            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            WriteHeader(writer, MAGIC5);
            writer.Write(stringBytesLength);
            writer.Write(1); // countBytes4 in buffer 1 (one int: string count)
            writer.Write(0); // 8-byte values in buffer 1
            writer.Write((int)context.Types.Length);
            writer.Write(countObjects);
            writer.Write(countArrays);
            writer.Write(checked(buffer1.Count + buffer2.Count));
            writer.Write(checked(compressedBuffer1.Count + compressedBuffer2.Count + compressedBlobs.Count));
            writer.Write(context.BinaryBlobLengths.Count);
            writer.Write(blobs.Count);
            writer.Write(0); // 2-byte values in buffer 1
            writer.Write(blockCompressedSizes.Count * sizeof(ushort));
            writer.Write(buffer1.Count);
            writer.Write(compressed ? compressedBuffer1.Count : 0);
            writer.Write(buffer2.Count);
            writer.Write(compressed ? compressedBuffer2.Count : 0);
            writer.Write((int)context.Bytes1.Length);
            writer.Write((int)context.Bytes2.Length / 2);
            writer.Write((int)context.Bytes4.Length / 4 - 1);
            writer.Write((int)context.Bytes8.Length / 8);
            writer.Write(0); // unknown, in official files it is close to the total member count, the reader ignores it
            writer.Write((int)countObjects);
            writer.Write((int)countArrays);
            writer.Write(0); // unknown, the reader ignores it

            writer.Write(compressedBuffer1.AsSpan());
            writer.Write(compressedBuffer2.AsSpan());
            writer.Write(compressedBlobs.AsSpan());

            if (context.BinaryBlobLengths.Count > 0)
            {
                writer.Write(0xFFEEDD00);
            }
        }

        private static ArraySegment<byte> BuildVersion5Buffer1(SerializationContext context, out int stringBytesLength)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            foreach (var str in context.Strings)
            {
                writer.Write(System.Text.Encoding.UTF8.GetBytes(str));
                writer.Write((byte)0);
            }

            stringBytesLength = checked((int)stream.Length);

            var offset = stringBytesLength;
            AlignWriter(ref offset, writer, 4);
            writer.Write(context.Strings.Count);
            return AsSegment(stream);
        }

        private static ArraySegment<byte> BuildVersion5Buffer2(SerializationContext context, List<ushort> blockCompressedSizes)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            context.ObjectLengths.WriteTo(stream);
            var offset = (int)stream.Length;
            WriteLane(context.Bytes1, writer, ref offset, 1);
            WriteLane(context.Bytes2, writer, ref offset, 2);
            WriteLane(context.Bytes4, writer, ref offset, 4, start: sizeof(int)); // skip the string count, it is stored in buffer 1
            WriteLane(context.Bytes8, writer, ref offset, 8);
            context.Types.WriteTo(stream);

            foreach (var length in context.BinaryBlobLengths)
            {
                writer.Write(length);
            }

            writer.Write(0xFFEEDD00);

            foreach (var size in blockCompressedSizes)
            {
                writer.Write(size);
            }

            return AsSegment(stream);
        }

        private static void WriteLane(MemoryStream lane, BinaryWriter writer, ref int offset, int alignment, int start = 0)
        {
            var remaining = checked((int)lane.Length) - start;

            if (remaining == 0)
            {
                return;
            }

            AlignWriter(ref offset, writer, alignment);
            writer.Write(lane.GetBuffer(), start, remaining);
            offset += remaining;
        }

        private static ArraySegment<byte> AsSegment(MemoryStream stream) => new(stream.GetBuffer(), 0, checked((int)stream.Length));

        private ArraySegment<byte> CompressMainBuffer(ArraySegment<byte> input)
        {
            return SerializationCompressionMethod switch
            {
                KV3BinaryCompressionMethod.Uncompressed => input,
                KV3BinaryCompressionMethod.Lz4 => CompressLz4(input),
                KV3BinaryCompressionMethod.Zstd => CompressZstd(input),
                _ => throw new NotSupportedException(),
            };
        }

        private ArraySegment<byte> CompressBinaryBlobs(ArraySegment<byte> input, IReadOnlyList<int> segmentLengths, out List<ushort> blockCompressedSizes)
        {
            blockCompressedSizes = [];

            return SerializationCompressionMethod switch
            {
                KV3BinaryCompressionMethod.Uncompressed => input,
                KV3BinaryCompressionMethod.Lz4 => CompressLz4BinaryBlobs(input, segmentLengths, out blockCompressedSizes),
                KV3BinaryCompressionMethod.Zstd => CompressZstd(input),
                _ => throw new NotSupportedException(),
            };
        }

        private static ArraySegment<byte> CompressLz4BinaryBlobs(ArraySegment<byte> input, IReadOnlyList<int> segmentLengths, out List<ushort> blockCompressedSizes)
        {
            blockCompressedSizes = [];
            using var output = new MemoryStream();
            using var encoder = new LZ4FastChainEncoder(CompressionFrameSize, 0);
            var target = new byte[LZ4Codec.MaximumOutputSize(CompressionFrameSize)];
            var inputOffset = 0;

            foreach (var segmentLength in segmentLengths)
            {
                for (var segmentOffset = 0; segmentOffset < segmentLength; segmentOffset += CompressionFrameSize)
                {
                    var length = Math.Min(CompressionFrameSize, segmentLength - segmentOffset);
                    encoder.TopupAndEncode(input.AsSpan(inputOffset + segmentOffset, length), target, true, false, out var loaded, out var encoded);

                    if (loaded != length || encoded <= 0 || encoded > ushort.MaxValue)
                    {
                        throw new InvalidOperationException("Failed to encode a chained LZ4 binary blob frame.");
                    }

                    blockCompressedSizes.Add((ushort)encoded);
                    output.Write(target, 0, encoded);
                }

                inputOffset += segmentLength;
            }

            return AsSegment(output);
        }

        private static ArraySegment<byte> CompressLz4(ReadOnlySpan<byte> input)
        {
            var output = new byte[LZ4Codec.MaximumOutputSize(input.Length)];
            var length = LZ4Codec.Encode(input, output);

            if (length <= 0)
            {
                throw new InvalidOperationException("Failed to compress binary KV3 data with LZ4.");
            }

            return new ArraySegment<byte>(output, 0, length);
        }

        private static ArraySegment<byte> CompressZstd(ReadOnlySpan<byte> input)
        {
            using var compressor = new ZstdSharp.Compressor();
            var output = new byte[ZstdSharp.Compressor.GetCompressBound(input.Length)];
            var length = compressor.Wrap(input, output);
            return new ArraySegment<byte>(output, 0, length);
        }

        private static void WriteProperty(string name, KVObject value, SerializationContext context)
        {
            context.Bytes4Writer.Write(context.GetStringId(name));
            WriteValueRecursive(value, context);
        }

        private static void WriteValueRecursive(KVObject value, SerializationContext context)
        {
            if (value.ValueType == KVValueType.Boolean)
            {
                if ((bool)value)
                {
                    WriteType(context, KV3BinaryNodeType.BOOLEAN_TRUE, value.Flag);
                }
                else
                {
                    WriteType(context, KV3BinaryNodeType.BOOLEAN_FALSE, value.Flag);
                }

                return;
            }
            else if (value.ValueType == KVValueType.Int64)
            {
                var writeValue = (long)value;

                if (writeValue == 0)
                {
                    WriteType(context, KV3BinaryNodeType.INT64_ZERO, value.Flag);
                    return;
                }
                else if (writeValue == 1)
                {
                    WriteType(context, KV3BinaryNodeType.INT64_ONE, value.Flag);
                    return;
                }

                WriteType(context, KV3BinaryNodeType.INT64, value.Flag);
                context.Bytes8Writer.Write(writeValue);
                return;
            }
            else if (value.ValueType == KVValueType.FloatingPoint64)
            {
                var writeValue = (double)value;

                if (BitConverter.DoubleToInt64Bits(writeValue) == 0)
                {
                    WriteType(context, KV3BinaryNodeType.DOUBLE_ZERO, value.Flag);
                    return;
                }
                else if (writeValue == 1.0)
                {
                    WriteType(context, KV3BinaryNodeType.DOUBLE_ONE, value.Flag);
                    return;
                }

                WriteType(context, KV3BinaryNodeType.DOUBLE, value.Flag);
                context.Bytes8Writer.Write(writeValue);
                return;
            }


            var nodeType = GetKV3BinaryNodeType(value);
            WriteType(context, nodeType, value.Flag);

            switch (value.ValueType)
            {
                case KVValueType.Null:
                    break;
                case KVValueType.Int16:
                    context.Bytes2Writer.Write((short)value);
                    break;
                case KVValueType.UInt16:
                    context.Bytes2Writer.Write((ushort)value);
                    break;
                case KVValueType.Int32:
                    context.Bytes4Writer.Write((int)value);
                    break;
                case KVValueType.UInt32:
                    context.Bytes4Writer.Write((uint)value);
                    break;
                case KVValueType.UInt64:
                    context.Bytes8Writer.Write((ulong)value);
                    break;
                case KVValueType.FloatingPoint:
                    context.Bytes4Writer.Write((float)value);
                    break;
                case KVValueType.String:
                    context.Bytes4Writer.Write(context.GetStringId((string)value));
                    break;
                case KVValueType.BinaryBlob:
                    var blobBytes = value.AsBlob();
                    context.BinaryBlobLengths.Add(blobBytes.Length);
                    if (blobBytes.Length > 0)
                    {
                        context.BinaryBlobsWriter.Write(blobBytes);
                    }
                    break;
                case KVValueType.Collection:
                    {
                        context.ObjectLengthsWriter.Write(value.Count);

                        foreach (var (key, property) in value)
                        {
                            WriteProperty(key, property, context);
                        }
                    }
                    break;
                case KVValueType.Array:
                    {
                        context.CountArrays++;
                        context.Bytes4Writer.Write(value.Count);

                        foreach (var (_, item) in value)
                        {
                            WriteValueRecursive(item, context);
                        }
                    }
                    break;
                default:
                    throw new NotSupportedException($"Unsupported value type: {value.ValueType}");
            }
        }

        private static KV3BinaryNodeType GetKV3BinaryNodeType(KVObject value)
        {
            return value.ValueType switch
            {
                KVValueType.Null => KV3BinaryNodeType.NULL,
                //KVValueType.Boolean => KV3BinaryNodeType.BOOLEAN,
                KVValueType.Int16 => KV3BinaryNodeType.INT16,
                KVValueType.UInt16 => KV3BinaryNodeType.UINT16,
                KVValueType.Int32 => KV3BinaryNodeType.INT32,
                KVValueType.UInt32 => KV3BinaryNodeType.UINT32,
                //KVValueType.Int64 => KV3BinaryNodeType.INT64,
                KVValueType.UInt64 => KV3BinaryNodeType.UINT64,
                KVValueType.FloatingPoint => KV3BinaryNodeType.FLOAT,
                //KVValueType.FloatingPoint64 => KV3BinaryNodeType.DOUBLE,
                KVValueType.String => KV3BinaryNodeType.STRING,
                KVValueType.BinaryBlob => KV3BinaryNodeType.BINARY_BLOB,
                KVValueType.Array => KV3BinaryNodeType.ARRAY,
                KVValueType.Collection => KV3BinaryNodeType.OBJECT,
                _ => throw new NotSupportedException($"Unsupported value type: {value.ValueType}")
            };
        }

        private static void WriteType(SerializationContext context, KV3BinaryNodeType type, KVFlag flag = KVFlag.None)
        {
            if (flag != KVFlag.None)
            {
                context.TypesWriter.Write((byte)((byte)type | 0x80));
                context.TypesWriter.Write((byte)flag);
            }
            else
            {
                context.TypesWriter.Write((byte)type);
            }
        }

        private static int WriteData(BinaryWriter writer, SerializationContext context)
        {
            // We're aligning inside of the compressed data block (even though we don't compress)
            var offset = 0;

            WriteLane(context.Bytes1, writer, ref offset, 1);
            WriteLane(context.Bytes2, writer, ref offset, 2);
            WriteLane(context.Bytes4, writer, ref offset, 4);

            if (context.Bytes8.Length > 0)
            {
                WriteLane(context.Bytes8, writer, ref offset, 8);
            }
            else
            {
                // For versions before 5, align even when empty
                AlignWriter(ref offset, writer, 8);
            }

            var stringsStartOffset = offset;

            foreach (var str in context.Strings)
            {
                var strBytes = System.Text.Encoding.UTF8.GetBytes(str);
                writer.Write(strBytes);
                writer.Write((byte)0);
                offset += strBytes.Length + 1;
            }

            context.Types.WriteTo(writer.BaseStream);
            offset += (int)context.Types.Length;

            var typesEndOffset = offset - stringsStartOffset;

            if (context.BinaryBlobLengths.Count > 0)
            {
                foreach (var length in context.BinaryBlobLengths)
                {
                    writer.Write(length);
                }

                writer.Write(0xFFEEDD00);
            }

            return typesEndOffset;
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
