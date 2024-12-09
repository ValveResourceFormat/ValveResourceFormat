using System.Buffers;
using System.Diagnostics;
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
        public const int MAGIC5 = 0x4B563304; // KV3\x04

        public static bool IsBinaryKV3(uint magic) => magic is MAGIC or MAGIC2 or MAGIC3 or MAGIC4 or MAGIC5;

        public KVObject Data { get; private set; }
        public Guid Encoding { get; private set; }
        public Guid Format { get; private set; }

        private string[] stringArray;
        private byte[] typesArray;
        private ArraySegment<byte> uncompressedBlocks;
        private int[] uncompressedBlockLengthArray;
        private int uncompressedBlockOffset;
        private long currentCompressedBlockIndex;
        private long currentTypeIndex;
        private long currentTwoBytesOffset = -1;
        private long currentEightBytesOffset;
        private long currentBinaryBytesOffset = -1;
        private bool isUsingLinearFlagTypes; // Version KV3\x03 uses a different enum for mapping flags
        private bool todoUnknownNewBytesInVersion4;

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
                case MAGIC4:
                    ReadVersion3(reader);
                    break;
                case MAGIC5:
                    isUsingLinearFlagTypes = true;
                    todoUnknownNewBytesInVersion4 = true;
                    ReadVersion3(reader);
                    break;
                default: throw new UnexpectedMagicException("Invalid KV3 signature", magic, nameof(magic));
            }

            stringArray = null;

            Debug.Assert(typesArray == null);
            Debug.Assert(uncompressedBlockLengthArray == null);
            Debug.Assert(uncompressedBlocks == null);
        }

        private static void DecompressLZ4(BinaryReader reader, Span<byte> output, int compressedSize)
        {
            var inputBuf = ArrayPool<byte>.Shared.Rent(compressedSize);

            try
            {
                var input = inputBuf.AsSpan(0, compressedSize);
                reader.Read(input);

                var written = LZ4Codec.Decode(input, output);

                if (written != output.Length)
                {
                    throw new InvalidDataException($"Failed to decompress LZ4 (expected {output.Length} bytes, got {written}).");
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(inputBuf);
            }
        }

        private void ReadVersion1(BinaryReader reader)
        {
            Encoding = new Guid(reader.ReadBytes(16));
            Format = new Guid(reader.ReadBytes(16));

            // Valve's implementation lives in LoadKV3Binary()
            // KV3_ENCODING_BINARY_BLOCK_COMPRESSED calls CBlockCompress::FastDecompress()
            // and then it proceeds to call LoadKV3BinaryUncompressed, which should be the same routine for KV3_ENCODING_BINARY_UNCOMPRESSED
            // Old binary with debug symbols for ref: https://users.alliedmods.net/~asherkin/public/bins/dota_symbols/bin/osx64/libmeshsystem.dylib

            byte[] outputBuf = null;

            try
            {
                int outBufferLength;

                if (Encoding.CompareTo(KV3_ENCODING_BINARY_BLOCK_COMPRESSED) == 0)
                {
                    var info = BlockCompress.GetDecompressedSize(reader);
                    outBufferLength = info.Size;
                    outputBuf = ArrayPool<byte>.Shared.Rent(outBufferLength);

                    BlockCompress.FastDecompress(info, reader, outputBuf.AsSpan(0, outBufferLength));
                }
                else if (Encoding.CompareTo(KV3_ENCODING_BINARY_BLOCK_LZ4) == 0)
                {
                    outBufferLength = reader.ReadInt32();
                    var compressedSize = (int)(Size - (reader.BaseStream.Position - Offset));
                    outputBuf = ArrayPool<byte>.Shared.Rent(outBufferLength);
                    DecompressLZ4(reader, outputBuf.AsSpan(0, outBufferLength), compressedSize);
                }
                else if (Encoding.CompareTo(KV3_ENCODING_BINARY_UNCOMPRESSED) == 0)
                {
                    outBufferLength = (int)(Size - (reader.BaseStream.Position - Offset));
                    outputBuf = ArrayPool<byte>.Shared.Rent(outBufferLength);
                    reader.Read(outputBuf.AsSpan(0, outBufferLength));
                }
                else
                {
                    throw new UnexpectedMagicException("Unrecognised KV3 Encoding", Encoding.ToString(), nameof(Encoding));
                }

                using var outStream = new MemoryStream(outputBuf, 0, outBufferLength);
                using var outRead = new BinaryReader(outStream, System.Text.Encoding.UTF8, true);

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
            finally
            {
                if (outputBuf != null)
                {
                    ArrayPool<byte>.Shared.Return(outputBuf);
                }
            }
        }

        private void ReadVersion2(BinaryReader reader)
        {
            Format = new Guid(reader.ReadBytes(16));

            var compressionMethod = reader.ReadInt32();
            var countOfBinaryBytes = reader.ReadInt32(); // how many bytes (binary blobs)
            var countOfIntegers = reader.ReadInt32(); // how many 4 byte values (ints)
            var countOfEightByteValues = reader.ReadInt32(); // how many 8 byte values (doubles)

            byte[] outputBuf = null;

            try
            {
                int outBufferLength;

                if (compressionMethod == 0)
                {
                    outBufferLength = reader.ReadInt32();
                    outputBuf = ArrayPool<byte>.Shared.Rent(outBufferLength);
                    reader.Read(outputBuf.AsSpan(0, outBufferLength));
                }
                else if (compressionMethod == 1)
                {
                    outBufferLength = reader.ReadInt32();
                    var compressedSize = (int)(Size - (reader.BaseStream.Position - Offset));
                    outputBuf = ArrayPool<byte>.Shared.Rent(outBufferLength);
                    DecompressLZ4(reader, outputBuf.AsSpan(0, outBufferLength), compressedSize);
                }
                else
                {
                    throw new UnexpectedMagicException("Unknown compression method", compressionMethod, nameof(compressionMethod));
                }

                using var outStream = new MemoryStream(outputBuf, 0, outBufferLength);
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
                var typesLength = (int)(outRead.BaseStream.Length - 4 - outRead.BaseStream.Position);

                typesArray = ArrayPool<byte>.Shared.Rent(typesLength);

                try
                {
                    outRead.Read(typesArray.AsSpan(0, typesLength));

                    // Move back to the start of the KV data for reading.
                    outRead.BaseStream.Position = kvDataOffset;

                    Data = ParseBinaryKV3(outRead, null, true);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(typesArray);
                    typesArray = null;
                }
            }
            finally
            {
                if (outputBuf != null)
                {
                    ArrayPool<byte>.Shared.Return(outputBuf);
                }
            }
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

            var countofTwoByteValue = 0u;

            if (todoUnknownNewBytesInVersion4)
            {
                countofTwoByteValue = reader.ReadUInt32();

                // TODO: Might be some preallocation number like above? The files seemingly parse if this isn't used for anything
                // and there's seemingly no extra data in the decoded stream - 2024-12-04
                reader.ReadUInt32();
            }

            if (compressedSize > int.MaxValue)
            {
                throw new NotImplementedException("KV3 compressedSize is higher than 32-bit integer, which we currently don't handle.");
            }

            if (blockTotalSize > int.MaxValue)
            {
                throw new NotImplementedException("KV3 compressedSize is higher than 32-bit integer, which we currently don't handle.");
            }

            byte[] outputBuf = null;

            try
            {
                int outBufferLength;

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

                    outBufferLength = (int)compressedSize;
                    outputBuf = ArrayPool<byte>.Shared.Rent(outBufferLength);
                    reader.Read(outputBuf.AsSpan(0, outBufferLength));
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

                    outBufferLength = (int)uncompressedSize;
                    outputBuf = ArrayPool<byte>.Shared.Rent(outBufferLength);

                    DecompressLZ4(reader, outputBuf.AsSpan(0, outBufferLength), (int)compressedSize);
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

                    outBufferLength = (int)(uncompressedSize + blockTotalSize);
                    outputBuf = ArrayPool<byte>.Shared.Rent(outBufferLength);
                    var inputBuf = ArrayPool<byte>.Shared.Rent((int)compressedSize);

                    try
                    {
                        var output = outputBuf.AsSpan(0, outBufferLength);
                        var input = inputBuf.AsSpan(0, (int)compressedSize);
                        reader.Read(input);

                        if (!zstd.TryUnwrap(input, output, out var written) || outBufferLength != written)
                        {
                            throw new InvalidDataException($"Failed to decompress zstd correctly (written {written} bytes, expected {outBufferLength} bytes)");
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(inputBuf);
                    }
                }
                else
                {
                    throw new UnexpectedMagicException("Unknown compression method", compressionMethod, nameof(compressionMethod));
                }

                using var outStream = new MemoryStream(outputBuf, 0, outBufferLength);
                using var outRead = new BinaryReader(outStream, System.Text.Encoding.UTF8, true);

                currentBinaryBytesOffset = 0;
                outRead.BaseStream.Position = countOfBinaryBytes;

                if (countofTwoByteValue > 0)
                {
                    if (outRead.BaseStream.Position % 2 != 0)
                    {
                        // Align to % 2 after binary blobs
                        outRead.BaseStream.Position += 1;
                    }

                    currentTwoBytesOffset = outRead.BaseStream.Position;
                    outRead.BaseStream.Position += countofTwoByteValue * sizeof(short);
                }
                else
                {
                    if (outRead.BaseStream.Position % 4 != 0)
                    {
                        // Align to % 4 after binary blobs
                        outRead.BaseStream.Position += 4 - (outRead.BaseStream.Position % 4);
                    }
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

                var typesLength = (int)(stringAndTypesBufferSize - (outRead.BaseStream.Position - stringArrayStartPosition));

                typesArray = ArrayPool<byte>.Shared.Rent(typesLength);
                outRead.Read(typesArray.AsSpan(0, typesLength));

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

                uncompressedBlockLengthArray = ArrayPool<int>.Shared.Rent((int)blockCount);

                for (var i = 0; i < blockCount; i++)
                {
                    uncompressedBlockLengthArray[i] = outRead.ReadInt32();
                }

                var trailer = outRead.ReadUInt32();
                if (trailer != 0xFFEEDD00)
                {
                    throw new UnexpectedMagicException("Invalid trailer", trailer, nameof(trailer));
                }

                byte[] uncompressedBlocksBuffer = null;

                try
                {
                    if (compressionMethod == 0)
                    {
                        uncompressedBlocksBuffer = ArrayPool<byte>.Shared.Rent((int)blockTotalSize);

                        var offset = 0;

                        for (var i = 0; i < blockCount; i++)
                        {
                            var length = uncompressedBlockLengthArray[i];
                            reader.Read(uncompressedBlocksBuffer.AsSpan(offset, length));
                            offset += length;
                        }

                        uncompressedBlocks = new ArraySegment<byte>(uncompressedBlocksBuffer, 0, (int)blockTotalSize);
                    }
                    else if (compressionMethod == 1)
                    {
                        uncompressedBlocksBuffer = ArrayPool<byte>.Shared.Rent((int)blockTotalSize);

                        using var lz4decoder = new LZ4ChainDecoder(compressionFrameSize, 0);
                        var offset = 0;

                        while (offset < blockTotalSize)
                        {
                            var compressedBlockLength = outRead.ReadUInt16();
                            var inputBuf = ArrayPool<byte>.Shared.Rent(compressedBlockLength);

                            try
                            {
                                var decodedFrameSize = offset + compressionFrameSize > blockTotalSize ? (int)blockTotalSize - offset : compressionFrameSize;
                                var output = uncompressedBlocksBuffer.AsSpan(offset, decodedFrameSize);

                                var input = inputBuf.AsSpan(0, compressedBlockLength);
                                reader.Read(input);

                                if (!lz4decoder.DecodeAndDrain(input, output, out var decoded) || decoded < 1)
                                {
                                    throw new InvalidOperationException("LZ4 decode drain failed, this is likely a bug.");
                                }

                                offset += decoded;
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(inputBuf);
                            }
                        }

                        uncompressedBlocks = new ArraySegment<byte>(uncompressedBlocksBuffer, 0, (int)blockTotalSize);
                    }
                    else if (compressionMethod == 2)
                    {
                        // This is supposed to be a streaming decompress using ZSTD_decompressStream,
                        // but as it turns out, zstd unwrap above already decompressed all of the blocks for us.
                        // It's possible that Valve's code needs extra decompress because they set ZSTD_d_stableOutBuffer parameter.
                        uncompressedBlocks = new ArraySegment<byte>(outputBuf, (int)outRead.BaseStream.Position, (int)blockTotalSize);
                    }
                    else
                    {
                        throw new UnexpectedMagicException("Unimplemented compression method in block decoder", compressionMethod, nameof(compressionMethod));
                    }

                    // Move back to the start of the KV data for reading.
                    outRead.BaseStream.Position = kvDataOffset;

                    Data = ParseBinaryKV3(outRead, null, true);
                }
                finally
                {
                    if (uncompressedBlocksBuffer != null)
                    {
                        ArrayPool<byte>.Shared.Return(uncompressedBlocksBuffer);
                    }

                    uncompressedBlocks = null;
                }
            }
            finally
            {
                if (outputBuf != null)
                {
                    ArrayPool<byte>.Shared.Return(outputBuf);
                }

                if (typesArray != null)
                {
                    ArrayPool<byte>.Shared.Return(typesArray);
                    typesArray = null;
                }

                if (uncompressedBlockLengthArray != null)
                {
                    ArrayPool<int>.Shared.Return(uncompressedBlockLengthArray);
                    uncompressedBlockLengthArray = null;
                }
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

            if (isUsingLinearFlagTypes)
            {
                if ((databyte & 0x80) > 0)
                {
                    databyte &= 0x3F; // Remove the flag bit

                    // TODO: Do new kv3 types always have typesArray?
                    if (typesArray != null)
                    {
                        flagInfo = (KVFlag)typesArray[currentTypeIndex++];
                    }
                    else
                    {
                        flagInfo = (KVFlag)reader.ReadByte();
                    }

                    if (flagInfo > KVFlag.SubClass)
                    {
                        throw new UnexpectedMagicException("Unexpected kv3 flag", (int)flagInfo, nameof(flagInfo));
                    }
                }
            }
            else if ((databyte & 0x80) > 0) // TODO: Valve's new code also checks for 0x40 even for old kv3 version
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

                if (((int)flagInfo & 4) > 0) // Multiline string
                {
                    databyte = (int)KVType.STRING_MULTI;
                    flagInfo ^= (KVFlag)4;
                }

                // Strictly speaking there could be more than one flag set, but in practice it was seemingly never.
                // Valve's new code just sets whichever flag is highest, new kv3 version does not support multiple flags at once.
                flagInfo = (int)flagInfo switch
                {
                    0 => KVFlag.None,
                    1 => KVFlag.Resource,
                    2 => KVFlag.ResourceName,
                    8 => KVFlag.Panorama,
                    16 => KVFlag.SoundEvent,
                    32 => KVFlag.SubClass,
                    _ => throw new UnexpectedMagicException("Unexpected kv3 flag", (int)flagInfo, nameof(flagInfo))
                };
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

            // We don't support non-object roots properly, so this is a hack to handle "null" kv3
            if (datatype != KVType.OBJECT && parent == null)
            {
                name ??= "root";
                parent ??= new KVObject(name);
            }

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
                case KVType.STRING_MULTI:
                    var id = reader.ReadInt32();
                    parent.AddProperty(name, MakeValue(datatype, id == -1 ? string.Empty : stringArray[id], flagInfo));
                    break;
                case KVType.BINARY_BLOB:
                    if (uncompressedBlocks != null)
                    {
                        var blockLength = uncompressedBlockLengthArray[currentCompressedBlockIndex++];
                        var output = uncompressedBlocks[uncompressedBlockOffset..(uncompressedBlockOffset + blockLength)].ToArray();
                        uncompressedBlockOffset += blockLength;
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
                    var array = new KVObject(name, isArray: true, capacity: arrayLength);

                    for (var i = 0; i < arrayLength; i++)
                    {
                        ParseBinaryKV3(reader, array, true);
                    }

                    parent.AddProperty(name, MakeValue(datatype, array, flagInfo));
                    break;
                case KVType.ARRAY_TYPED:
                case KVType.ARRAY_TYPE_BYTE_LENGTH:
                    var typeArrayLength = 0;

                    if (datatype == KVType.ARRAY_TYPE_BYTE_LENGTH)
                    {
                        reader.BaseStream.Position = currentBinaryBytesOffset;

                        typeArrayLength = reader.ReadByte();

                        currentBinaryBytesOffset++;
                        reader.BaseStream.Position = currentOffset;
                    }
                    else
                    {
                        typeArrayLength = reader.ReadInt32();
                    }

                    var (subType, subFlagInfo) = ReadType(reader);
                    var typedArray = new KVObject(name, isArray: true, capacity: typeArrayLength);

                    for (var i = 0; i < typeArrayLength; i++)
                    {
                        ReadBinaryValue(name, subType, subFlagInfo, reader, typedArray);
                    }

                    parent.AddProperty(name, MakeValue(datatype, typedArray, flagInfo));
                    break;
                case KVType.OBJECT:
                    var objectLength = reader.ReadInt32();
                    var newObject = new KVObject(name, isArray: false, capacity: objectLength);

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
                case KVType.FLOAT:
                    parent.AddProperty(name, MakeValue(datatype, reader.ReadSingle(), flagInfo));
                    break;
                case KVType.INT16:
                    reader.BaseStream.Position = currentTwoBytesOffset;
                    parent.AddProperty(name, MakeValue(datatype, reader.ReadInt16(), flagInfo));
                    currentTwoBytesOffset = reader.BaseStream.Position;
                    reader.BaseStream.Position = currentOffset;

                    break;
                case KVType.UINT16:
                    reader.BaseStream.Position = currentTwoBytesOffset;
                    parent.AddProperty(name, MakeValue(datatype, reader.ReadUInt16(), flagInfo));
                    currentTwoBytesOffset = reader.BaseStream.Position;
                    reader.BaseStream.Position = currentOffset;

                    break;
                // TODO: 22, 23 - reading from currentBinaryBytesOffset
                // 22 is related to 20, 23 is related to 21
                case KVType.INT32_AS_BYTE:
                    reader.BaseStream.Position = currentBinaryBytesOffset;

                    parent.AddProperty(name, MakeValue(datatype, (int)reader.ReadByte(), flagInfo));

                    currentBinaryBytesOffset++;
                    reader.BaseStream.Position = currentOffset;

                    break;
                default:
                    throw new UnexpectedMagicException($"Unknown KVType for field '{name}' on byte {reader.BaseStream.Position} (currentTypeIndex={currentTypeIndex})", (int)datatype, nameof(datatype));
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
                case KVType.INT32_AS_BYTE:
                    return KVType.INT64;
                case KVType.UINT64:
                case KVType.UINT32:
                    return KVType.UINT64;
                case KVType.DOUBLE:
                case KVType.DOUBLE_ZERO:
                case KVType.DOUBLE_ONE:
                    return KVType.DOUBLE;
                case KVType.ARRAY_TYPED:
                case KVType.ARRAY_TYPE_BYTE_LENGTH:
                    return KVType.ARRAY;
            }
#pragma warning restore IDE0066 // Convert switch statement to expression

            return type;
        }

        public static KVValue MakeValue(KVType type, object data, KVFlag flag = KVFlag.None)
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
            GetKV3File().WriteText(writer);
        }
    }
}
