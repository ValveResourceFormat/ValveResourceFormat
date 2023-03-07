using System;
using System.Buffers;
using System.IO;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Encoders;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Compression;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes
{
#pragma warning disable CA1001 // Types that own disposable fields should be disposable
    public class BinaryKV3 : ResourceData
#pragma warning restore CA1001 // Types that own disposable fields should be disposable
    {
        private readonly BlockType KVBlockType;
        public override BlockType Type => KVBlockType;

        private static readonly Guid KV3_ENCODING_BINARY_BLOCK_COMPRESSED = new(new byte[] { 0x46, 0x1A, 0x79, 0x95, 0xBC, 0x95, 0x6C, 0x4F, 0xA7, 0x0B, 0x05, 0xBC, 0xA1, 0xB7, 0xDF, 0xD2 });
        private static readonly Guid KV3_ENCODING_BINARY_UNCOMPRESSED = new(new byte[] { 0x00, 0x05, 0x86, 0x1B, 0xD8, 0xF7, 0xC1, 0x40, 0xAD, 0x82, 0x75, 0xA4, 0x82, 0x67, 0xE7, 0x14 });
        private static readonly Guid KV3_ENCODING_BINARY_BLOCK_LZ4 = new(new byte[] { 0x8A, 0x34, 0x47, 0x68, 0xA1, 0x63, 0x5C, 0x4F, 0xA1, 0x97, 0x53, 0x80, 0x6F, 0xD9, 0xB1, 0x19 });
        private static readonly Guid KV3_FORMAT_GENERIC = new(new byte[] { 0x7C, 0x16, 0x12, 0x74, 0xE9, 0x06, 0x98, 0x46, 0xAF, 0xF2, 0xE6, 0x3E, 0xB5, 0x90, 0x37, 0xE7 });
        public const int MAGIC = 0x03564B56; // VKV3 (3 isn't ascii, its 0x03)
        public const int MAGIC2 = 0x4B563301; // KV3\x01
        public const int MAGIC3 = 0x4B563302; // KV3\x02
        public const int MAGIC4 = 0x4B563303; // KV3\x03

        public static bool IsBinaryKV3(uint magic) => magic is MAGIC or MAGIC2 or MAGIC3 or MAGIC4;

        public KVObject Data { get; private set; }
        public Guid Encoding { get; private set; }
        public Guid Format { get; private set; }

        private string[] stringArray;
        private byte[] typesArray;
        private BinaryReader uncompressedBlockDataReader;
        private int[] uncompressedBlockLengthArray;
        private long currentCompressedBlockIndex;
        private long currentTypeIndex;
        private long currentEightBytesOffset;
        private long currentBinaryBytesOffset = -1;

        public BinaryKV3()
        {
            KVBlockType = BlockType.DATA;
        }

        public BinaryKV3(BlockType type)
        {
            KVBlockType = type;
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;

            var magic = reader.ReadUInt32();

            switch (magic)
            {
                case MAGIC: ReadVersion1(reader); break;
                case MAGIC2: ReadVersion2(reader); break;
                case MAGIC3: ReadVersion3(reader); break;
                case MAGIC4: ReadVersion3(reader); break;
                default: throw new UnexpectedMagicException("Invalid KV3 signature", magic, nameof(magic));
            }
        }

        private void DecompressLZ4(BinaryReader reader, MemoryStream outStream)
        {
            var uncompressedSize = reader.ReadUInt32();
            var compressedSize = (int)(Size - (reader.BaseStream.Position - Offset));
            var output = new Span<byte>(new byte[uncompressedSize]);
            var buf = ArrayPool<byte>.Shared.Rent(compressedSize);

            try
            {
                var input = buf.AsSpan(0, compressedSize);
                reader.Read(input);

                var written = LZ4Codec.Decode(input, output);

                if (written != output.Length)
                {
                    throw new InvalidDataException($"Failed to decompress LZ4 (expected {output.Length} bytes, got {written}).");
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }

            outStream.Write(output);
            outStream.Seek(0, SeekOrigin.Begin);
        }

        private void ReadVersion1(BinaryReader reader)
        {
            Encoding = new Guid(reader.ReadBytes(16));
            Format = new Guid(reader.ReadBytes(16));

            using var outStream = new MemoryStream();
            using var outWrite = new BinaryWriter(outStream, System.Text.Encoding.UTF8, true);
            using var outRead = new BinaryReader(outStream, System.Text.Encoding.UTF8, true);

            // Valve's implementation lives in LoadKV3Binary()
            // KV3_ENCODING_BINARY_BLOCK_COMPRESSED calls CBlockCompress::FastDecompress()
            // and then it proceeds to call LoadKV3BinaryUncompressed, which should be the same routine for KV3_ENCODING_BINARY_UNCOMPRESSED
            // Old binary with debug symbols for ref: https://users.alliedmods.net/~asherkin/public/bins/dota_symbols/bin/osx64/libmeshsystem.dylib

            if (Encoding.CompareTo(KV3_ENCODING_BINARY_BLOCK_COMPRESSED) == 0)
            {
                var decompressed = BlockCompress.FastDecompress(reader);
                outStream.Write(decompressed);
                outStream.Seek(0, SeekOrigin.Begin);
            }
            else if (Encoding.CompareTo(KV3_ENCODING_BINARY_BLOCK_LZ4) == 0)
            {
                DecompressLZ4(reader, outStream);
            }
            else if (Encoding.CompareTo(KV3_ENCODING_BINARY_UNCOMPRESSED) == 0)
            {
                reader.BaseStream.CopyTo(outStream);
                outStream.Seek(0, SeekOrigin.Begin);
            }
            else
            {
                throw new UnexpectedMagicException("Unrecognised KV3 Encoding", Encoding.ToString(), nameof(Encoding));
            }

            var stringCount = outRead.ReadUInt32();
            stringArray = new string[stringCount];
            for (var i = 0; i < stringCount; i++)
            {
                stringArray[i] = outRead.ReadNullTermString(System.Text.Encoding.UTF8);
            }

            Data = ParseBinaryKV3(outRead, null, true);

            var trailer = outRead.ReadUInt32();
            if (trailer != 0xFFFFFFFF)
            {
                throw new UnexpectedMagicException("Invalid trailer", trailer, nameof(trailer));
            }
        }

        private void ReadVersion2(BinaryReader reader)
        {
            Format = new Guid(reader.ReadBytes(16));

            var compressionMethod = reader.ReadInt32();
            var countOfBinaryBytes = reader.ReadInt32(); // how many bytes (binary blobs)
            var countOfIntegers = reader.ReadInt32(); // how many 4 byte values (ints)
            var countOfEightByteValues = reader.ReadInt32(); // how many 8 byte values (doubles)

            using var outStream = new MemoryStream();

            if (compressionMethod == 0)
            {
                var length = reader.ReadInt32();

                var output = new Span<byte>(new byte[length]);
                reader.Read(output);
                outStream.Write(output);
                outStream.Seek(0, SeekOrigin.Begin);
            }
            else if (compressionMethod == 1)
            {
                DecompressLZ4(reader, outStream);
            }
            else
            {
                throw new UnexpectedMagicException("Unknown compression method", compressionMethod, nameof(compressionMethod));
            }

            using var outRead = new BinaryReader(outStream, System.Text.Encoding.UTF8, true);

            currentBinaryBytesOffset = 0;
            outRead.BaseStream.Position = countOfBinaryBytes;

            if (outRead.BaseStream.Position % 4 != 0)
            {
                // Align to % 4 after binary blobs
                outRead.BaseStream.Position += 4 - (outRead.BaseStream.Position % 4);
            }

            var countOfStrings = outRead.ReadInt32();
            var kvDataOffset = outRead.BaseStream.Position;

            // Subtract one integer since we already read it (countOfStrings)
            outRead.BaseStream.Position += (countOfIntegers - 1) * 4;

            if (outRead.BaseStream.Position % 8 != 0)
            {
                // Align to % 8 for the start of doubles
                outRead.BaseStream.Position += 8 - (outRead.BaseStream.Position % 8);
            }

            currentEightBytesOffset = outRead.BaseStream.Position;

            outRead.BaseStream.Position += countOfEightByteValues * 8;

            stringArray = new string[countOfStrings];

            for (var i = 0; i < countOfStrings; i++)
            {
                stringArray[i] = outRead.ReadNullTermString(System.Text.Encoding.UTF8);
            }

            // bytes after the string table is kv types, minus 4 static bytes at the end
            var typesLength = outRead.BaseStream.Length - 4 - outRead.BaseStream.Position;
            typesArray = new byte[typesLength];

            for (var i = 0; i < typesLength; i++)
            {
                typesArray[i] = outRead.ReadByte();
            }

            // Move back to the start of the KV data for reading.
            outRead.BaseStream.Position = kvDataOffset;

            Data = ParseBinaryKV3(outRead, null, true);
        }

        private void ReadVersion3(BinaryReader reader)
        {
            Format = new Guid(reader.ReadBytes(16));

            var compressionMethod = reader.ReadUInt32();
            var compressionDictionaryId = reader.ReadUInt16();
            var compressionFrameSize = reader.ReadUInt16();
            var countOfBinaryBytes = reader.ReadUInt32(); // how many bytes (binary blobs)
            var countOfIntegers = reader.ReadUInt32(); // how many 4 byte values (ints)
            var countOfEightByteValues = reader.ReadUInt32(); // how many 8 byte values (doubles)

            // 8 bytes that help valve preallocate, useless for us
            var stringAndTypesBufferSize = reader.ReadUInt32();
            var b = reader.ReadUInt16();
            var c = reader.ReadUInt16();

            var uncompressedSize = reader.ReadUInt32();
            var compressedSize = reader.ReadUInt32();
            var blockCount = reader.ReadUInt32();
            var blockTotalSize = reader.ReadUInt32();

            if (compressedSize > int.MaxValue)
            {
                throw new NotImplementedException("KV3 compressedSize is higher than 32-bit integer, which we currently don't handle.");
            }

            if (blockTotalSize > int.MaxValue)
            {
                throw new NotImplementedException("KV3 compressedSize is higher than 32-bit integer, which we currently don't handle.");
            }

            using var outStream = new MemoryStream();

            if (compressionMethod == 0)
            {
                if (compressionDictionaryId != 0)
                {
                    throw new UnexpectedMagicException("Unhandled", compressionDictionaryId, nameof(compressionDictionaryId));
                }

                if (compressionFrameSize != 0)
                {
                    throw new UnexpectedMagicException("Unhandled", compressionFrameSize, nameof(compressionFrameSize));
                }

                var output = new Span<byte>(new byte[compressedSize]);
                reader.Read(output);
                outStream.Write(output);
            }
            else if (compressionMethod == 1)
            {
                if (compressionDictionaryId != 0)
                {
                    throw new UnexpectedMagicException("Unhandled", compressionDictionaryId, nameof(compressionDictionaryId));
                }

                if (compressionFrameSize != 16384)
                {
                    throw new UnexpectedMagicException("Unhandled", compressionFrameSize, nameof(compressionFrameSize));
                }

                var output = new Span<byte>(new byte[uncompressedSize]);
                var buf = ArrayPool<byte>.Shared.Rent((int)compressedSize);

                try
                {
                    var input = buf.AsSpan(0, (int)compressedSize);
                    reader.Read(input);

                    var written = LZ4Codec.Decode(input, output);

                    if (written != output.Length)
                    {
                        throw new InvalidDataException($"Failed to decompress LZ4 (expected {output.Length} bytes, got {written}).");
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buf);
                }

                outStream.Write(output);
            }
            else if (compressionMethod == 2)
            {
                if (compressionDictionaryId != 0)
                {
                    throw new UnexpectedMagicException("Unhandled", compressionDictionaryId, nameof(compressionDictionaryId));
                }

                if (compressionFrameSize != 0)
                {
                    throw new UnexpectedMagicException("Unhandled", compressionFrameSize, nameof(compressionFrameSize));
                }

                using var zstd = new ZstdSharp.Decompressor();

                var totalSize = uncompressedSize + blockTotalSize;
                var output = new Span<byte>(new byte[totalSize]);
                var buf = ArrayPool<byte>.Shared.Rent((int)compressedSize);

                try
                {
                    var input = buf.AsSpan(0, (int)compressedSize);
                    reader.Read(input);

                    if (!zstd.TryUnwrap(input, output, out var written) || totalSize != written)
                    {
                        throw new InvalidDataException($"Failed to decompress zstd correctly (written {written} bytes, expected {totalSize} bytes)");
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buf);
                }

                outStream.Write(output);
            }
            else
            {
                throw new UnexpectedMagicException("Unknown compression method", compressionMethod, nameof(compressionMethod));
            }

            outStream.Seek(0, SeekOrigin.Begin);
            using var outRead = new BinaryReader(outStream, System.Text.Encoding.UTF8, true);

            currentBinaryBytesOffset = 0;
            outRead.BaseStream.Position = countOfBinaryBytes;

            if (outRead.BaseStream.Position % 4 != 0)
            {
                // Align to % 4 after binary blobs
                outRead.BaseStream.Position += 4 - (outRead.BaseStream.Position % 4);
            }

            var countOfStrings = outRead.ReadUInt32();
            var kvDataOffset = outRead.BaseStream.Position;

            // Subtract one integer since we already read it (countOfStrings)
            outRead.BaseStream.Position += (countOfIntegers - 1) * 4;

            if (outRead.BaseStream.Position % 8 != 0)
            {
                // Align to % 8 for the start of doubles
                outRead.BaseStream.Position += 8 - (outRead.BaseStream.Position % 8);
            }

            currentEightBytesOffset = outRead.BaseStream.Position;

            outRead.BaseStream.Position += countOfEightByteValues * 8;
            var stringArrayStartPosition = outRead.BaseStream.Position;

            stringArray = new string[countOfStrings];

            for (var i = 0; i < countOfStrings; i++)
            {
                stringArray[i] = outRead.ReadNullTermString(System.Text.Encoding.UTF8);
            }

            var typesLength = stringAndTypesBufferSize - (outRead.BaseStream.Position - stringArrayStartPosition);
            typesArray = new byte[typesLength];

            for (var i = 0; i < typesLength; i++)
            {
                typesArray[i] = outRead.ReadByte();
            }

            if (blockCount == 0)
            {
                var noBlocksTrailer = outRead.ReadUInt32();
                if (noBlocksTrailer != 0xFFEEDD00)
                {
                    throw new UnexpectedMagicException("Invalid trailer", noBlocksTrailer, nameof(noBlocksTrailer));
                }

                // Move back to the start of the KV data for reading.
                outRead.BaseStream.Position = kvDataOffset;

                Data = ParseBinaryKV3(outRead, null, true);

                return;
            }

            uncompressedBlockLengthArray = new int[blockCount];

            for (var i = 0; i < blockCount; i++)
            {
                uncompressedBlockLengthArray[i] = outRead.ReadInt32();
            }

            var trailer = outRead.ReadUInt32();
            if (trailer != 0xFFEEDD00)
            {
                throw new UnexpectedMagicException("Invalid trailer", trailer, nameof(trailer));
            }

            try
            {
                using var uncompressedBlocks = new MemoryStream((int)blockTotalSize);
                uncompressedBlockDataReader = new BinaryReader(uncompressedBlocks);

                if (compressionMethod == 0)
                {
                    for (var i = 0; i < blockCount; i++)
                    {
                        reader.BaseStream.CopyTo(uncompressedBlocks, uncompressedBlockLengthArray[i]);
                    }
                }
                else if (compressionMethod == 1)
                {
                    using var lz4decoder = new LZ4ChainDecoder(compressionFrameSize, 0);

                    while (outRead.BaseStream.Position < outRead.BaseStream.Length)
                    {
                        var compressedBlockLength = outRead.ReadUInt16();
                        var output = new Span<byte>(new byte[compressionFrameSize]);
                        var buf = ArrayPool<byte>.Shared.Rent(compressedBlockLength);

                        try
                        {
                            var input = buf.AsSpan(0, compressedBlockLength);

                            reader.Read(input);

                            if (lz4decoder.DecodeAndDrain(input, output, out var decoded) && decoded > 0)
                            {
                                if (decoded < output.Length)
                                {
                                    uncompressedBlocks.Write(output[..decoded]);
                                }
                                else
                                {
                                    uncompressedBlocks.Write(output);
                                }
                            }
                            else
                            {
                                throw new InvalidOperationException("LZ4 decode drain failed, this is likely a bug.");
                            }
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buf);
                        }
                    }
                }
                else if (compressionMethod == 2)
                {
                    // This is supposed to be a streaming decompress using ZSTD_decompressStream,
                    // but as it turns out, zstd unwrap above already decompressed all of the blocks for us,
                    // so all we need to do is just copy the buffer.
                    // It's possible that Valve's code needs extra decompress because they set ZSTD_d_stableOutBuffer parameter.
                    outRead.BaseStream.CopyTo(uncompressedBlocks);
                }
                else
                {
                    throw new UnexpectedMagicException("Unimplemented compression method in block decoder", compressionMethod, nameof(compressionMethod));
                }

                uncompressedBlocks.Position = 0;

                // Move back to the start of the KV data for reading.
                outRead.BaseStream.Position = kvDataOffset;

                Data = ParseBinaryKV3(outRead, null, true);
            }
            finally
            {
                uncompressedBlockDataReader.Dispose();
            }
        }

        private (KVType Type, KVFlag Flag) ReadType(BinaryReader reader)
        {
            byte databyte;

            if (typesArray != null)
            {
                databyte = typesArray[currentTypeIndex++];
            }
            else
            {
                databyte = reader.ReadByte();
            }

            var flagInfo = KVFlag.None;

            if ((databyte & 0x80) > 0)
            {
                databyte &= 0x7F; // Remove the flag bit

                if (typesArray != null)
                {
                    flagInfo = (KVFlag)typesArray[currentTypeIndex++];
                }
                else
                {
                    flagInfo = (KVFlag)reader.ReadByte();
                }
            }

            return ((KVType)databyte, flagInfo);
        }

        private KVObject ParseBinaryKV3(BinaryReader reader, KVObject parent, bool inArray = false)
        {
            string name = null;
            if (!inArray)
            {
                var stringID = reader.ReadInt32();
                name = (stringID == -1) ? string.Empty : stringArray[stringID];
            }

            var (datatype, flagInfo) = ReadType(reader);

            return ReadBinaryValue(name, datatype, flagInfo, reader, parent);
        }

        private KVObject ReadBinaryValue(string name, KVType datatype, KVFlag flagInfo, BinaryReader reader, KVObject parent)
        {
            var currentOffset = reader.BaseStream.Position;

            switch (datatype)
            {
                case KVType.NULL:
                    parent.AddProperty(name, MakeValue(datatype, null, flagInfo));
                    break;
                case KVType.BOOLEAN:
                    if (currentBinaryBytesOffset > -1)
                    {
                        reader.BaseStream.Position = currentBinaryBytesOffset;
                    }

                    parent.AddProperty(name, MakeValue(datatype, reader.ReadBoolean(), flagInfo));

                    if (currentBinaryBytesOffset > -1)
                    {
                        currentBinaryBytesOffset++;
                        reader.BaseStream.Position = currentOffset;
                    }

                    break;
                case KVType.BOOLEAN_TRUE:
                    parent.AddProperty(name, MakeValue(datatype, true, flagInfo));
                    break;
                case KVType.BOOLEAN_FALSE:
                    parent.AddProperty(name, MakeValue(datatype, false, flagInfo));
                    break;
                case KVType.INT64_ZERO:
                    parent.AddProperty(name, MakeValue(datatype, 0L, flagInfo));
                    break;
                case KVType.INT64_ONE:
                    parent.AddProperty(name, MakeValue(datatype, 1L, flagInfo));
                    break;
                case KVType.INT64:
                    if (currentEightBytesOffset > 0)
                    {
                        reader.BaseStream.Position = currentEightBytesOffset;
                    }

                    parent.AddProperty(name, MakeValue(datatype, reader.ReadInt64(), flagInfo));

                    if (currentEightBytesOffset > 0)
                    {
                        currentEightBytesOffset = reader.BaseStream.Position;
                        reader.BaseStream.Position = currentOffset;
                    }

                    break;
                case KVType.UINT64:
                    if (currentEightBytesOffset > 0)
                    {
                        reader.BaseStream.Position = currentEightBytesOffset;
                    }

                    parent.AddProperty(name, MakeValue(datatype, reader.ReadUInt64(), flagInfo));

                    if (currentEightBytesOffset > 0)
                    {
                        currentEightBytesOffset = reader.BaseStream.Position;
                        reader.BaseStream.Position = currentOffset;
                    }

                    break;
                case KVType.INT32:
                    parent.AddProperty(name, MakeValue(datatype, reader.ReadInt32(), flagInfo));
                    break;
                case KVType.UINT32:
                    parent.AddProperty(name, MakeValue(datatype, reader.ReadUInt32(), flagInfo));
                    break;
                case KVType.DOUBLE:
                    if (currentEightBytesOffset > 0)
                    {
                        reader.BaseStream.Position = currentEightBytesOffset;
                    }

                    parent.AddProperty(name, MakeValue(datatype, reader.ReadDouble(), flagInfo));

                    if (currentEightBytesOffset > 0)
                    {
                        currentEightBytesOffset = reader.BaseStream.Position;
                        reader.BaseStream.Position = currentOffset;
                    }

                    break;
                case KVType.DOUBLE_ZERO:
                    parent.AddProperty(name, MakeValue(datatype, 0.0D, flagInfo));
                    break;
                case KVType.DOUBLE_ONE:
                    parent.AddProperty(name, MakeValue(datatype, 1.0D, flagInfo));
                    break;
                case KVType.STRING:
                    var id = reader.ReadInt32();
                    parent.AddProperty(name, MakeValue(datatype, id == -1 ? string.Empty : stringArray[id], flagInfo));
                    break;
                case KVType.BINARY_BLOB:
                    if (uncompressedBlockDataReader != null)
                    {
                        var output = uncompressedBlockDataReader.ReadBytes(uncompressedBlockLengthArray[currentCompressedBlockIndex++]);
                        parent.AddProperty(name, MakeValue(datatype, output, flagInfo));
                        break;
                    }

                    var length = reader.ReadInt32();

                    if (currentBinaryBytesOffset > -1)
                    {
                        reader.BaseStream.Position = currentBinaryBytesOffset;
                    }

                    parent.AddProperty(name, MakeValue(datatype, reader.ReadBytes(length), flagInfo));

                    if (currentBinaryBytesOffset > -1)
                    {
                        currentBinaryBytesOffset = reader.BaseStream.Position;
                        reader.BaseStream.Position = currentOffset + 4;
                    }

                    break;
                case KVType.ARRAY:
                    var arrayLength = reader.ReadInt32();
                    var array = new KVObject(name, true);
                    for (var i = 0; i < arrayLength; i++)
                    {
                        ParseBinaryKV3(reader, array, true);
                    }

                    parent.AddProperty(name, MakeValue(datatype, array, flagInfo));
                    break;
                case KVType.ARRAY_TYPED:
                    var typeArrayLength = reader.ReadInt32();
                    var (subType, subFlagInfo) = ReadType(reader);
                    var typedArray = new KVObject(name, true);

                    for (var i = 0; i < typeArrayLength; i++)
                    {
                        ReadBinaryValue(name, subType, subFlagInfo, reader, typedArray);
                    }

                    parent.AddProperty(name, MakeValue(datatype, typedArray, flagInfo));
                    break;
                case KVType.OBJECT:
                    var objectLength = reader.ReadInt32();
                    var newObject = new KVObject(name, false);
                    for (var i = 0; i < objectLength; i++)
                    {
                        ParseBinaryKV3(reader, newObject, false);
                    }

                    if (parent == null)
                    {
                        parent = newObject;
                    }
                    else
                    {
                        parent.AddProperty(name, MakeValue(datatype, newObject, flagInfo));
                    }

                    break;
                default:
                    throw new UnexpectedMagicException($"Unknown KVType for field '{name}' on byte {reader.BaseStream.Position - 1}", (int)datatype, nameof(datatype));
            }

            return parent;
        }

        private static KVType ConvertBinaryOnlyKVType(KVType type)
        {
#pragma warning disable IDE0066 // Convert switch statement to expression
            switch (type)
            {
                case KVType.BOOLEAN:
                case KVType.BOOLEAN_TRUE:
                case KVType.BOOLEAN_FALSE:
                    return KVType.BOOLEAN;
                case KVType.INT64:
                case KVType.INT32:
                case KVType.INT64_ZERO:
                case KVType.INT64_ONE:
                    return KVType.INT64;
                case KVType.UINT64:
                case KVType.UINT32:
                    return KVType.UINT64;
                case KVType.DOUBLE:
                case KVType.DOUBLE_ZERO:
                case KVType.DOUBLE_ONE:
                    return KVType.DOUBLE;
                case KVType.ARRAY_TYPED:
                    return KVType.ARRAY;
            }
#pragma warning restore IDE0066 // Convert switch statement to expression

            return type;
        }

        private static KVValue MakeValue(KVType type, object data, KVFlag flag)
        {
            var realType = ConvertBinaryOnlyKVType(type);

            if (flag != KVFlag.None)
            {
                return new KVFlaggedValue(realType, flag, data);
            }

            return new KVValue(realType, data);
        }

#pragma warning disable CA1024 // Use properties where appropriate
        public KV3File GetKV3File()
#pragma warning restore CA1024 // Use properties where appropriate
        {
            // TODO: Other format guids are not "generic" but strings like "vpc19"
            var formatType = "generic";

            if (Format != KV3_FORMAT_GENERIC)
            {
                formatType = "vrfunknown";
            }

            return new KV3File(Data, format: $"{formatType}:version{{{Format}}}");
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            Data.Serialize(writer);
        }
    }
}
