using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

        private static readonly Guid KV3_ENCODING_BINARY_BLOCK_COMPRESSED = new([0x46, 0x1A, 0x79, 0x95, 0xBC, 0x95, 0x6C, 0x4F, 0xA7, 0x0B, 0x05, 0xBC, 0xA1, 0xB7, 0xDF, 0xD2]);
        private static readonly Guid KV3_ENCODING_BINARY_UNCOMPRESSED = new([0x00, 0x05, 0x86, 0x1B, 0xD8, 0xF7, 0xC1, 0x40, 0xAD, 0x82, 0x75, 0xA4, 0x82, 0x67, 0xE7, 0x14]);
        private static readonly Guid KV3_ENCODING_BINARY_BLOCK_LZ4 = new([0x8A, 0x34, 0x47, 0x68, 0xA1, 0x63, 0x5C, 0x4F, 0xA1, 0x97, 0x53, 0x80, 0x6F, 0xD9, 0xB1, 0x19]);
        private static readonly Guid KV3_FORMAT_GENERIC = new([0x7C, 0x16, 0x12, 0x74, 0xE9, 0x06, 0x98, 0x46, 0xAF, 0xF2, 0xE6, 0x3E, 0xB5, 0x90, 0x37, 0xE7]);
        public const int MAGIC0 = 0x03564B56; // VKV3 (3 isn't ascii, its 0x03)
        public const int MAGIC1 = 0x4B563301; // KV3\x01
        public const int MAGIC2 = 0x4B563302; // KV3\x02
        public const int MAGIC3 = 0x4B563303; // KV3\x03
        public const int MAGIC4 = 0x4B563304; // KV3\x04
        public const int MAGIC5 = 0x4B563305; // KV3\x05

        public static bool IsBinaryKV3(uint magic) => magic is MAGIC0 or MAGIC1 or MAGIC2 or MAGIC3 or MAGIC4 or MAGIC5;

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
        private long currentEightBytesOffset = -1;
        private long currentBinaryBytesOffset = -1;
        private bool isUsingLinearFlagTypes; // Version KV3\x03 uses a different enum for mapping flags
        private bool isUsingTwoBytesBuffer;

        private class Buffers
        {
            public ArraySegment<byte> Bytes1;
            public ArraySegment<byte> Bytes2;
            public ArraySegment<byte> Bytes4;
            public ArraySegment<byte> Bytes8;
        }

        private class Context
        {
            public int Version;
            public ArraySegment<byte> Types;
            public ArraySegment<byte> ObjectLengths;
            public ArraySegment<byte> BinaryBlobs;
            public ArraySegment<byte> BinaryBlobLengths;
            public string[] Strings;
            public Buffers Buffer;
            public Buffers AuxiliaryBuffer;
        }

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
                case MAGIC0: ReadVersion1(reader); break;
                case MAGIC1: ReadVersion2(reader); break;
                case MAGIC2: ReadVersion3(reader); break;
                case MAGIC3:
                    ReadVersion3(reader);
                    break;
                case MAGIC4:
                    ReadVersion5(4, reader);
                    break;
                case MAGIC5:
                    ReadVersion5(5, reader);
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

            if (isUsingTwoBytesBuffer)
            {
                countofTwoByteValue = reader.ReadUInt32();
                var sizeBlockCompressedBytes = reader.ReadUInt32();
            }

            if (compressedSize > int.MaxValue)
            {
                throw new NotImplementedException("KV3 compressedSize is higher than 32-bit integer, which we currently don't handle.");
            }

            if (blockTotalSize > int.MaxValue)
            {
                throw new NotImplementedException("KV3 blockTotalSize is higher than 32-bit integer, which we currently don't handle.");
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

                // TODO: use byte arraypool
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

        private void ReadVersion5(int version, BinaryReader reader)
        {
            var context = new Context
            {
                Version = version,
            };

            Format = new Guid(reader.ReadBytes(16));

            var compressionMethod = reader.ReadUInt32();
            var compressionDictionaryId = reader.ReadUInt16();
            var compressionFrameSize = reader.ReadUInt16();

            var countBytes1 = reader.ReadInt32();
            var countBytes4 = reader.ReadInt32();
            var countBytes8 = reader.ReadInt32();
            var countTypes = reader.ReadInt32();
            var countObjects = reader.ReadUInt16();
            var countArrays = reader.ReadUInt16();
            var sizeUncompressedTotal = reader.ReadInt32();
            var sizeCompressedTotal = reader.ReadInt32();
            var countBlocks = reader.ReadInt32();
            var sizeBinaryBlobsBytes = reader.ReadInt32();

            var countBytes2 = 0;
            var sizeBlockCompressedSizesBytes = 0;

            if (version >= 4)
            {
                countBytes2 = reader.ReadInt32();
                sizeBlockCompressedSizesBytes = reader.ReadInt32();
            }

            var sizeUncompressedBuffer1 = 0;
            var sizeCompressedBuffer1 = 0;
            var sizeUncompressedBuffer2 = 0;
            var sizeCompressedBuffer2 = 0;
            var countBytes1_buffer2 = 0;
            var countBytes2_buffer2 = 0;
            var countBytes4_buffer2 = 0;
            var countBytes8_buffer2 = 0;
            var countObjects_buffer2 = 0;
            var countArrays_buffer2 = 0;

            if (version >= 5)
            {
                sizeUncompressedBuffer1 = reader.ReadInt32();
                sizeCompressedBuffer1 = reader.ReadInt32();
                sizeUncompressedBuffer2 = reader.ReadInt32();
                sizeCompressedBuffer2 = reader.ReadInt32();
                countBytes1_buffer2 = reader.ReadInt32();
                countBytes2_buffer2 = reader.ReadInt32();
                countBytes4_buffer2 = reader.ReadInt32();
                countBytes8_buffer2 = reader.ReadInt32();
                var unk13 = reader.ReadInt32();
                countObjects_buffer2 = reader.ReadInt32();
                countArrays_buffer2 = reader.ReadInt32();
                var unk16 = reader.ReadInt32();

                Debug.Assert(sizeUncompressedTotal == sizeUncompressedBuffer1 + sizeUncompressedBuffer2);
            }
            else
            {
                sizeCompressedBuffer1 = sizeCompressedTotal;
                sizeUncompressedBuffer1 = sizeUncompressedTotal;
            }

            var buffer1Raw = new byte[sizeUncompressedBuffer1]; // TODO: ArrayPool
            var buffer2Raw = version >= 5 ? new byte[sizeUncompressedBuffer2] : null; // TODO: ArrayPool - TODO: move into the version>=5 buffer reading below

            if (compressionMethod == 0) // uncompressed
            {
                if (compressionDictionaryId != 0)
                {
                    throw new UnexpectedMagicException("Unhandled", compressionDictionaryId, nameof(compressionDictionaryId));
                }

                if (compressionFrameSize != 0)
                {
                    throw new UnexpectedMagicException("Unhandled", compressionFrameSize, nameof(compressionFrameSize));
                }

                if (version >= 5)
                {
                    Debug.Assert(sizeCompressedBuffer1 == 0);
                }
                else
                {
                    Debug.Assert(sizeCompressedBuffer1 == sizeUncompressedBuffer1);
                }

                reader.Read(buffer1Raw.AsSpan(0, sizeUncompressedBuffer1));

                if (version >= 5)
                {
                    Debug.Assert(sizeCompressedBuffer2 == 0);

                    reader.Read(buffer2Raw.AsSpan(0, sizeUncompressedBuffer2));
                }
            }
            else if (compressionMethod == 1) // LZ4
            {
                if (compressionDictionaryId != 0)
                {
                    throw new UnexpectedMagicException("Unhandled", compressionDictionaryId, nameof(compressionDictionaryId));
                }

                if (compressionFrameSize != 16384)
                {
                    throw new UnexpectedMagicException("Unhandled", compressionFrameSize, nameof(compressionFrameSize));
                }

                Debug.Assert(sizeCompressedBuffer1 > 0);

                DecompressLZ4(reader, buffer1Raw, sizeCompressedBuffer1);

                if (version >= 5)
                {
                    Debug.Assert(sizeCompressedBuffer2 > 0);

                    DecompressLZ4(reader, buffer2Raw, sizeCompressedBuffer2);
                }
            }
            else if (compressionMethod == 2) // ZSTD
            {
                if (compressionDictionaryId != 0)
                {
                    throw new UnexpectedMagicException("Unhandled", compressionDictionaryId, nameof(compressionDictionaryId));
                }

                if (compressionFrameSize != 0)
                {
                    throw new UnexpectedMagicException("Unhandled", compressionFrameSize, nameof(compressionFrameSize));
                }

                Debug.Assert(sizeCompressedBuffer1 > 0);

                using var zstd = new ZstdSharp.Decompressor();

                var inputBuf = ArrayPool<byte>.Shared.Rent(Math.Max(sizeCompressedBuffer1, sizeCompressedBuffer2));

                try
                {
                    // Buffer 1
                    var output = buffer1Raw.AsSpan(0, sizeUncompressedBuffer1);
                    var input = inputBuf.AsSpan(0, sizeCompressedBuffer1);
                    reader.Read(input);

                    if (!zstd.TryUnwrap(input, output, out var written) || sizeUncompressedBuffer1 != written)
                    {
                        throw new InvalidDataException($"Failed to decompress zstd correctly (written {written} bytes, expected {sizeUncompressedBuffer1} bytes)");
                    }

                    // Buffer 2
                    if (version >= 5)
                    {
                        Debug.Assert(sizeCompressedBuffer2 > 0);

                        output = buffer2Raw.AsSpan(0, sizeUncompressedBuffer2);
                        input = inputBuf.AsSpan(0, sizeCompressedBuffer2);
                        reader.Read(input);

                        if (!zstd.TryUnwrap(input, output, out written) || sizeUncompressedBuffer2 != written)
                        {
                            throw new InvalidDataException($"Failed to decompress zstd correctly (written {written} bytes, expected {sizeUncompressedBuffer2} bytes)");
                        }
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

            ArraySegment<byte> bufferWithBinaryBlobSizes = null;

            // Buffer 1
            {
                var buffer1Span = new ArraySegment<byte>(buffer1Raw);
                var buffer1 = new Buffers();

                var offset = 0;

                if (countBytes1 > 0)
                {
                    var end = offset + countBytes1;
                    buffer1.Bytes1 = buffer1Span[offset..end];
                    offset = end;
                }

                if (countBytes2 > 0)
                {
                    if (offset % 2 != 0)
                    {
                        offset += 2 - (offset % 2);
                    }

                    var end = offset + countBytes2 * 2;
                    buffer1.Bytes2 = buffer1Span[offset..end];
                    offset = end;
                }

                if (countBytes4 > 0)
                {
                    if (offset % 4 != 0)
                    {
                        offset += 4 - (offset % 4);
                    }

                    var end = offset + countBytes4 * 4;
                    buffer1.Bytes4 = buffer1Span[offset..end];
                    offset = end;
                }

                if ((version < 5 || countBytes8 > 0) && offset % 8 != 0)
                {
                    offset += 8 - (offset % 8);
                }

                if (countBytes8 > 0)
                {
                    var end = offset + countBytes8 * 8;
                    buffer1.Bytes8 = buffer1Span[offset..end];
                    offset = end;
                }

                Debug.Assert(countBytes4 > 0); // should be guaranteed to be at least 1 for the strings count

                var countStrings = MemoryMarshal.Read<int>(buffer1.Bytes4);
                buffer1.Bytes4 = buffer1.Bytes4[sizeof(int)..];
                context.Strings = new string[countStrings];

                if (version >= 5)
                {
                    context.AuxiliaryBuffer = buffer1;

                    var readStringBytes = 0;

                    for (var i = 0; i < countStrings; i++)
                    {
                        context.Strings[i] = ReadNullTermUtf8String(ref buffer1.Bytes1, ref readStringBytes);
                    }

                    Debug.Assert(buffer1Span.Count == offset);
                }
                else
                {
                    context.Buffer = buffer1;

                    var stringsBuffer = buffer1Span[offset..];
                    var stringsStartOffset = offset;

                    for (var i = 0; i < countStrings; i++)
                    {
                        context.Strings[i] = ReadNullTermUtf8String(ref stringsBuffer, ref offset);
                    }

                    // Types before v5
                    var typesLength = countTypes - offset + stringsStartOffset;
                    context.Types = buffer1Span[offset..(offset + typesLength)];
                    offset += typesLength;

                    if (countBlocks == 0)
                    {
                        var trailer = MemoryMarshal.Read<uint>(buffer1Span[offset..]);
                        offset += 4;
                        UnexpectedMagicException.Assert(trailer == 0xFFEEDD00, trailer);
                    }
                    else
                    {
                        bufferWithBinaryBlobSizes = buffer1Span[offset..];
                    }
                }
            }

            // Buffer 2
            if (version >= 5)
            {
                var buffer2Span = new ArraySegment<byte>(buffer2Raw);
                var buffer2 = new Buffers();
                context.Buffer = buffer2;

                var end = countObjects_buffer2 * sizeof(int);
                var offset = end;

                context.ObjectLengths = buffer2Span[..end];

                if (countBytes1_buffer2 > 0)
                {
                    end = offset + countBytes1_buffer2;
                    buffer2.Bytes1 = buffer2Span[offset..end];
                    offset = end;
                }

                if (countBytes2_buffer2 > 0)
                {
                    if (offset % 2 != 0)
                    {
                        offset += 2 - (offset % 2);
                    }

                    end = offset + countBytes2_buffer2 * 2;
                    buffer2.Bytes2 = buffer2Span[offset..end];
                    offset = end;
                }

                if (countBytes4_buffer2 > 0)
                {
                    if (offset % 4 != 0)
                    {
                        offset += 4 - (offset % 4);
                    }

                    end = offset + countBytes4_buffer2 * 4;
                    buffer2.Bytes4 = buffer2Span[offset..end];
                    offset = end;
                }

                if (countBytes8_buffer2 > 0)
                {
                    if (offset % 8 != 0)
                    {
                        offset += 8 - (offset % 8);
                    }

                    end = offset + countBytes8_buffer2 * 8;
                    buffer2.Bytes8 = buffer2Span[offset..end];
                    offset = end;
                }

                // Types in v5
                context.Types = buffer2Span[offset..(offset + countTypes)];
                offset += countTypes;

                if (countBlocks == 0)
                {
                    var trailer = MemoryMarshal.Read<uint>(buffer2Span[offset..]);
                    offset += 4;
                    UnexpectedMagicException.Assert(trailer == 0xFFEEDD00, trailer);
                }
                else
                {
                    bufferWithBinaryBlobSizes = buffer2Span[offset..];
                }
            }

            if (countBlocks > 0)
            {
                Debug.Assert(bufferWithBinaryBlobSizes != null);

                var end = countBlocks * sizeof(int);
                context.BinaryBlobLengths = bufferWithBinaryBlobSizes[..end];
                bufferWithBinaryBlobSizes = bufferWithBinaryBlobSizes[end..];

                var trailer = MemoryMarshal.Read<uint>(bufferWithBinaryBlobSizes);
                bufferWithBinaryBlobSizes = bufferWithBinaryBlobSizes[sizeof(int)..];
                UnexpectedMagicException.Assert(trailer == 0xFFEEDD00, trailer);

                // TODO: Cleanup this crap
                if (version >= 5)
                {
                    if (sizeBlockCompressedSizesBytes > 0)
                    {
                        if (compressionMethod == 1)
                        {
                            context.BinaryBlobs = new byte[sizeBinaryBlobsBytes]; // TODO: ArrayPool
                                                                                  // uncompressedBlocks = new ArraySegment<byte>(uncompressedBlocksBuffer, 0, sizeBlockBytes);

                            using var lz4decoder = new LZ4ChainDecoder(compressionFrameSize, 0);

                            end = sizeBlockCompressedSizesBytes;

                            var decompressedOffset = 0;

                            while (bufferWithBinaryBlobSizes.Count > 0)
                            {
                                var compressedBlockLength = MemoryMarshal.Read<ushort>(bufferWithBinaryBlobSizes);
                                bufferWithBinaryBlobSizes = bufferWithBinaryBlobSizes[sizeof(ushort)..];

                                var inputBuf = ArrayPool<byte>.Shared.Rent(compressedBlockLength);

                                try
                                {
                                    var decodedFrameSize = decompressedOffset + compressionFrameSize > sizeBinaryBlobsBytes ? sizeBinaryBlobsBytes - decompressedOffset : compressionFrameSize;
                                    var output = context.BinaryBlobs.AsSpan(decompressedOffset, decodedFrameSize);

                                    var input = inputBuf.AsSpan(0, compressedBlockLength);
                                    reader.Read(input);

                                    if (!lz4decoder.DecodeAndDrain(input, output, out var decoded) || decoded < 1)
                                    {
                                        throw new InvalidOperationException("LZ4 decode drain failed, this is likely a bug.");
                                    }

                                    decompressedOffset += decoded;
                                }
                                finally
                                {
                                    ArrayPool<byte>.Shared.Return(inputBuf);
                                }
                            }
                        }
                        else
                        {
                            throw new UnexpectedMagicException("Unsupported compression", compressionMethod, nameof(compressionMethod));
                        }
                    }
                    else if (sizeBlockCompressedSizesBytes == 0)
                    {
                        var sizeCompressedBinaryBlobs = sizeCompressedTotal - sizeCompressedBuffer1 - sizeCompressedBuffer2;

                        if (compressionMethod == 2)
                        {
                            context.BinaryBlobs = new byte[sizeBinaryBlobsBytes]; // TODO: ArrayPool

                            using var zstd = new ZstdSharp.Decompressor();

                            var inputBuf = ArrayPool<byte>.Shared.Rent(sizeCompressedBinaryBlobs);

                            try
                            {
                                var output = context.BinaryBlobs.AsSpan(0, sizeBinaryBlobsBytes);
                                var input = inputBuf.AsSpan(0, sizeCompressedBinaryBlobs);
                                reader.Read(input);

                                if (!zstd.TryUnwrap(input, output, out var written) || sizeBinaryBlobsBytes != written)
                                {
                                    throw new InvalidDataException($"Failed to decompress zstd correctly (written {written} bytes, expected {sizeBinaryBlobsBytes} bytes)");
                                }
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(inputBuf);
                            }
                        }
                        else if (sizeCompressedBinaryBlobs > 0)
                        {
                            throw new UnexpectedMagicException("Unsupported compression", compressionMethod, nameof(compressionMethod));
                        }
                    }
                }
                else if (version >= 3)
                {
                    if (compressionMethod == 0)
                    {
                        context.BinaryBlobs = new byte[sizeBinaryBlobsBytes];

                        var offset = 0;

                        for (var i = 0; i < countBlocks; i++)
                        {
                            var length = uncompressedBlockLengthArray[i];
                            reader.Read(context.BinaryBlobs.AsSpan(offset, length));
                            offset += length;
                        }
                    }
                    else if (compressionMethod == 1)
                    {
                        context.BinaryBlobs = new byte[sizeBinaryBlobsBytes];

                        using var lz4decoder = new LZ4ChainDecoder(compressionFrameSize, 0);
                        var offset = 0;

                        while (offset < sizeBinaryBlobsBytes)
                        {
                            var compressedBlockLength = MemoryMarshal.Read<ushort>(bufferWithBinaryBlobSizes);
                            bufferWithBinaryBlobSizes = bufferWithBinaryBlobSizes[sizeof(ushort)..];

                            var inputBuf = ArrayPool<byte>.Shared.Rent(compressedBlockLength);

                            try
                            {
                                var decodedFrameSize = offset + compressionFrameSize > sizeBinaryBlobsBytes ? sizeBinaryBlobsBytes - offset : compressionFrameSize;
                                var output = context.BinaryBlobs.AsSpan(offset, decodedFrameSize);

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
                    }
                    else if (compressionMethod == 2)
                    {
                        // This is supposed to be a streaming decompress using ZSTD_decompressStream,
                        // but as it turns out, zstd unwrap above already decompressed all of the blocks for us.
                        // It's possible that Valve's code needs extra decompress because they set ZSTD_d_stableOutBuffer parameter.
                        //context.BinaryBlobs = new ArraySegment<byte>(outputBuf, (int)outRead.BaseStream.Position, sizeBinaryBlobsBytes);
                        Debug.Assert(false);
                    }
                    else
                    {
                        throw new UnexpectedMagicException("Unimplemented compression method in block decoder", compressionMethod, nameof(compressionMethod));
                    }
                }

                trailer = reader.ReadUInt32();
                UnexpectedMagicException.Assert(trailer == 0xFFEEDD00, trailer);
            }

            Debug.Assert(reader.BaseStream.Position == Offset + Size);

            Data = ParseBinaryKV3(context, null, true);

            Debug.Assert(context.Types.Count == 0);
            Debug.Assert(context.ObjectLengths.Count == 0);
            Debug.Assert(context.BinaryBlobs.Count == 0);
            Debug.Assert(context.BinaryBlobLengths.Count == 0);
            Debug.Assert(context.Buffer.Bytes1.Count == 0);
            Debug.Assert(context.Buffer.Bytes2.Count == 0);
            Debug.Assert(context.Buffer.Bytes4.Count == 0);
            Debug.Assert(context.Buffer.Bytes8.Count == 0);

            if (version >= 5)
            {
                Debug.Assert(context.AuxiliaryBuffer.Bytes1.Count == 0);
                Debug.Assert(context.AuxiliaryBuffer.Bytes2.Count == 0);
                Debug.Assert(context.AuxiliaryBuffer.Bytes4.Count == 0);
                Debug.Assert(context.AuxiliaryBuffer.Bytes8.Count == 0);
            }
        }

        private static (KVType Type, KVFlag Flag) ReadType(Context context)
        {
            var databyte = context.Types[0];
            context.Types = context.Types[1..];
            var flagInfo = KVFlag.None;

            if (context.Version >= 4)
            {
                if ((databyte & 0x80) > 0)
                {
                    databyte &= 0x3F; // Remove the flag bit

                    flagInfo = (KVFlag)context.Types[0];
                    context.Types = context.Types[1..];

                    if (flagInfo > KVFlag.SubClass)
                    {
                        throw new UnexpectedMagicException("Unexpected kv3 flag", (int)flagInfo, nameof(flagInfo));
                    }
                }
            }
            else if ((databyte & 0x80) > 0) // TODO: Valve's new code also checks for 0x40 even for old kv3 version
            {
                databyte &= 0x7F; // Remove the flag bit

                flagInfo = (KVFlag)context.Types[0];
                context.Types = context.Types[1..];

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

        private static KVObject ParseBinaryKV3(Context context, KVObject parent, bool inArray = false)
        {
            string name = null;
            if (!inArray)
            {
                var stringID = MemoryMarshal.Read<int>(context.Buffer.Bytes4);
                context.Buffer.Bytes4 = context.Buffer.Bytes4[sizeof(int)..];

                name = (stringID == -1) ? string.Empty : context.Strings[stringID];
            }

            var (datatype, flagInfo) = ReadType(context);

            return ReadBinaryValue(context, name, datatype, flagInfo, parent);
        }

        private static KVObject ReadBinaryValue(Context context, string name, KVType datatype, KVFlag flagInfo, KVObject parent)
        {
            // We don't support non-object roots properly, so this is a hack to handle "null" kv3
            if (datatype != KVType.OBJECT && parent == null)
            {
                name ??= "root";
                parent ??= new KVObject(name);
            }

            var buffer = context.Buffer;

            switch (datatype)
            {
                // Hardcoded values
                case KVType.NULL:
                    parent.AddProperty(name, MakeValue(datatype, null, flagInfo));
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
                case KVType.DOUBLE_ZERO:
                    parent.AddProperty(name, MakeValue(datatype, 0.0D, flagInfo));
                    break;
                case KVType.DOUBLE_ONE:
                    parent.AddProperty(name, MakeValue(datatype, 1.0D, flagInfo));
                    break;

                // 1 byte values
                case KVType.BOOLEAN:
                    {
                        var value = buffer.Bytes1[0] == 1;
                        buffer.Bytes1 = buffer.Bytes1[1..];

                        parent.AddProperty(name, MakeValue(datatype, value, flagInfo));
                    }
                    break;
                // TODO: 22 might beINT32_AS_BYTE, and 23 UINT32_AS_BYTE
                case KVType.INT32_AS_BYTE:
                    {
                        var value = (int)buffer.Bytes1[0];
                        buffer.Bytes1 = buffer.Bytes1[1..];

                        parent.AddProperty(name, MakeValue(datatype, value, flagInfo));
                    }
                    break;

                // 2 byte values
                case KVType.INT16:
                    {
                        var value = MemoryMarshal.Read<short>(buffer.Bytes2);
                        buffer.Bytes2 = buffer.Bytes2[sizeof(short)..];

                        parent.AddProperty(name, MakeValue(datatype, value, flagInfo));
                    }
                    break;
                case KVType.UINT16:
                    {
                        var value = MemoryMarshal.Read<ushort>(buffer.Bytes2);
                        buffer.Bytes2 = buffer.Bytes2[sizeof(ushort)..];

                        parent.AddProperty(name, MakeValue(datatype, value, flagInfo));
                    }
                    break;

                // 4 byte values
                case KVType.INT32:
                    {
                        var value = MemoryMarshal.Read<int>(buffer.Bytes4);
                        buffer.Bytes4 = buffer.Bytes4[sizeof(int)..];

                        parent.AddProperty(name, MakeValue(datatype, value, flagInfo));
                    }
                    break;
                case KVType.UINT32:
                    {
                        var value = MemoryMarshal.Read<uint>(buffer.Bytes4);
                        buffer.Bytes4 = buffer.Bytes4[sizeof(uint)..];

                        parent.AddProperty(name, MakeValue(datatype, value, flagInfo));
                    }
                    break;
                case KVType.FLOAT:
                    {
                        var value = MemoryMarshal.Read<float>(buffer.Bytes4);
                        buffer.Bytes4 = buffer.Bytes4[sizeof(float)..];

                        parent.AddProperty(name, MakeValue(datatype, value, flagInfo));
                    }
                    break;

                // 8 byte values
                case KVType.INT64:
                    {
                        var value = MemoryMarshal.Read<long>(buffer.Bytes8);
                        buffer.Bytes8 = buffer.Bytes8[sizeof(long)..];

                        parent.AddProperty(name, MakeValue(datatype, value, flagInfo));
                    }
                    break;
                case KVType.UINT64:
                    {
                        var value = MemoryMarshal.Read<ulong>(buffer.Bytes8);
                        buffer.Bytes8 = buffer.Bytes8[sizeof(ulong)..];

                        parent.AddProperty(name, MakeValue(datatype, value, flagInfo));
                    }
                    break;
                case KVType.DOUBLE:
                    {
                        var value = MemoryMarshal.Read<double>(buffer.Bytes8);
                        buffer.Bytes8 = buffer.Bytes8[sizeof(double)..];

                        parent.AddProperty(name, MakeValue(datatype, value, flagInfo));
                    }
                    break;

                // Custom types
                case KVType.STRING:
                case KVType.STRING_MULTI:
                    {
                        var id = MemoryMarshal.Read<int>(buffer.Bytes4);
                        buffer.Bytes4 = buffer.Bytes4[sizeof(int)..];

                        parent.AddProperty(name, MakeValue(datatype, id == -1 ? string.Empty : context.Strings[id], flagInfo));
                    }
                    break;
                case KVType.BINARY_BLOB:
                    {
                        var blockLength = MemoryMarshal.Read<int>(context.BinaryBlobLengths);
                        context.BinaryBlobLengths = context.BinaryBlobLengths[sizeof(int)..];
                        byte[] output;

                        if (blockLength > 0)
                        {
                            output = [.. context.BinaryBlobs[..blockLength]]; // explicit copy
                            context.BinaryBlobs = context.BinaryBlobs[blockLength..];
                        }
                        else
                        {
                            output = [];
                        }

                        parent.AddProperty(name, MakeValue(datatype, output, flagInfo));
                    }
                    break;
                case KVType.ARRAY:
                    {
                        var arrayLength = MemoryMarshal.Read<int>(buffer.Bytes4);
                        buffer.Bytes4 = buffer.Bytes4[sizeof(int)..];

                        var array = new KVObject(name, isArray: true, capacity: arrayLength);

                        for (var i = 0; i < arrayLength; i++)
                        {
                            ParseBinaryKV3(context, array, true);
                        }

                        parent.AddProperty(name, MakeValue(datatype, array, flagInfo));
                    }
                    break;
                case KVType.ARRAY_TYPED:
                case KVType.ARRAY_TYPE_BYTE_LENGTH:
                    {
                        int arrayLength;

                        if (datatype == KVType.ARRAY_TYPE_BYTE_LENGTH)
                        {
                            arrayLength = buffer.Bytes1[0];
                            buffer.Bytes1 = buffer.Bytes1[1..];
                        }
                        else
                        {
                            arrayLength = MemoryMarshal.Read<int>(buffer.Bytes4);
                            buffer.Bytes4 = buffer.Bytes4[sizeof(int)..];
                        }

                        var (subType, subFlagInfo) = ReadType(context);
                        var typedArray = new KVObject(name, isArray: true, capacity: arrayLength);

                        for (var i = 0; i < arrayLength; i++)
                        {
                            ReadBinaryValue(context, name, subType, subFlagInfo, typedArray);
                        }

                        parent.AddProperty(name, MakeValue(datatype, typedArray, flagInfo));
                    }
                    break;
                case KVType.ARRAY_TYPE_AUXILIARY_BUFFER:
                    {
                        Debug.Assert(context.Version >= 5);

                        var arrayLength = buffer.Bytes1[0];
                        buffer.Bytes1 = buffer.Bytes1[1..];

                        var (subType, subFlagInfo) = ReadType(context);
                        var typedArray = new KVObject(name, isArray: true, capacity: arrayLength);

                        // Swap the buffers and simply call read again instead of reimplementing the switch here
                        (context.AuxiliaryBuffer, context.Buffer) = (context.Buffer, context.AuxiliaryBuffer);

                        for (var i = 0; i < arrayLength; i++)
                        {
                            ReadBinaryValue(context, name, subType, subFlagInfo, typedArray);
                        }

                        (context.AuxiliaryBuffer, context.Buffer) = (context.Buffer, context.AuxiliaryBuffer);

                        parent.AddProperty(name, MakeValue(datatype, typedArray, flagInfo));
                    }
                    break;

                case KVType.OBJECT:
                    {
                        int objectLength;

                        if (context.Version >= 5)
                        {
                            objectLength = MemoryMarshal.Read<int>(context.ObjectLengths);
                            context.ObjectLengths = context.ObjectLengths[sizeof(int)..];
                        }
                        else
                        {
                            objectLength = MemoryMarshal.Read<int>(buffer.Bytes4);
                            buffer.Bytes4 = buffer.Bytes4[sizeof(int)..];
                        }

                        var newObject = new KVObject(name, isArray: false, capacity: objectLength);

                        for (var i = 0; i < objectLength; i++)
                        {
                            ParseBinaryKV3(context, newObject, false);
                        }

                        if (parent == null)
                        {
                            parent = newObject;
                        }
                        else
                        {
                            parent.AddProperty(name, MakeValue(datatype, newObject, flagInfo));
                        }
                    }
                    break;
                default:
                    throw new UnexpectedMagicException($"Unknown KVType for field '{name}'", (int)datatype, nameof(datatype));
            }

            return parent;
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
                    int typeArrayLength;

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
                case KVType.ARRAY_TYPE_AUXILIARY_BUFFER:
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

        private static string ReadNullTermUtf8String(ref ArraySegment<byte> buffer, ref int offset)
        {
            var nullByte = buffer.AsSpan().IndexOf((byte)0);

            Debug.Assert(nullByte > 0);

            var str = buffer[..nullByte];
            buffer = buffer[(nullByte + 1)..];

            offset += nullByte + 1;

            return System.Text.Encoding.UTF8.GetString(str);
        }
    }
}
