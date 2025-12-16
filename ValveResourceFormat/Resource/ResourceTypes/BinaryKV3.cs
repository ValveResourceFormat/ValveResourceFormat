using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Encoders;
using ValveResourceFormat.Serialization.KeyValues;
using KVValueType = ValveKeyValue.KVValueType;

#nullable disable

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a binary KeyValues3 data block.
    /// </summary>
    public partial class BinaryKV3 : Block
    {
        private readonly BlockType KVBlockType;

        /// <inheritdoc/>
        public override BlockType Type => KVBlockType;

        /// <summary>
        /// Magic number for VKV3 format.
        /// </summary>
        public const int MAGIC0 = 0x03564B56; // VKV3 (3 isn't ascii, its 0x03)

        /// <summary>
        /// Magic number for KV3 version 1.
        /// </summary>
        public const int MAGIC1 = 0x4B563301; // KV3\x01

        /// <summary>
        /// Magic number for KV3 version 2.
        /// </summary>
        public const int MAGIC2 = 0x4B563302; // KV3\x02

        /// <summary>
        /// Magic number for KV3 version 3.
        /// </summary>
        public const int MAGIC3 = 0x4B563303; // KV3\x03

        /// <summary>
        /// Magic number for KV3 version 4.
        /// </summary>
        public const int MAGIC4 = 0x4B563304; // KV3\x04

        /// <summary>
        /// Magic number for KV3 version 5.
        /// </summary>
        public const int MAGIC5 = 0x4B563305; // KV3\x05

        /// <summary>
        /// Checks if the given magic number represents a binary KV3 format.
        /// </summary>
        /// <param name="magic">The magic number to check.</param>
        /// <returns>True if the magic number is a valid binary KV3 format.</returns>
        public static bool IsBinaryKV3(uint magic) => magic is MAGIC0 or MAGIC1 or MAGIC2 or MAGIC3 or MAGIC4 or MAGIC5;

        /// <summary>
        /// Gets the deserialized KeyValues3 data.
        /// </summary>
        public KVObject Data { get; private set; }

        /// <summary>
        /// Gets the encoding identifier for this KV3 data.
        /// </summary>
        public KV3ID? Encoding { get; private set; }

        /// <summary>
        /// Gets the format identifier for this KV3 data.
        /// </summary>
        public KV3ID Format { get; private set; }

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

        /// <summary>
        /// Initializes a new instance of the <see cref="BinaryKV3"/> class with DATA block type.
        /// </summary>
        public BinaryKV3()
        {
            KVBlockType = BlockType.DATA;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BinaryKV3"/> class with the specified block type.
        /// </summary>
        /// <param name="type">The block type.</param>
        public BinaryKV3(BlockType type)
        {
            KVBlockType = type;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BinaryKV3"/> class with the specified data and format.
        /// </summary>
        /// <param name="data">The KeyValues3 data.</param>
        /// <param name="format">The format identifier.</param>
        /// <param name="blockType">The block type.</param>
        public BinaryKV3(KVObject data, KV3ID format, BlockType blockType = BlockType.Undefined)
        {
            KVBlockType = blockType;
            Data = data;
            Format = format;
        }

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            if (KVBlockType != BlockType.Undefined)
            {
                reader.BaseStream.Position = Offset;
            }

            var magic = reader.ReadUInt32();

            if (magic == MAGIC0)
            {
                ReadVersion0(reader);
                return;
            }

            var version = magic & 0xFF;
            magic &= 0xFFFFFF00;

            if (magic != 0x4B563300)
            {
                throw new UnexpectedMagicException("Unsupported KV3 signature", magic, nameof(magic));
            }

            if (version < 1 || version > 5)
            {
                throw new UnexpectedMagicException("Unsupported KV3 version", version, nameof(version));
            }

            ReadBuffer((int)version, reader);
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
                    throw new InvalidDataException($"Failed to decompress LZ4 (expected {output.Length} bytes, got {written})");
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(inputBuf);
            }
        }

        private static void DecompressZSTD(ZstdSharp.Decompressor zstdDecompressor, BinaryReader reader, Span<byte> output, int compressedSize)
        {
            var inputBuf = ArrayPool<byte>.Shared.Rent(compressedSize);

            try
            {
                var input = inputBuf.AsSpan(0, compressedSize);
                reader.Read(input);

                if (!zstdDecompressor.TryUnwrap(input, output, out var written) || output.Length != written)
                {
                    throw new InvalidDataException($"Failed to decompress ZSTD (expected {output.Length} bytes, got {written})");
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(inputBuf);
            }
        }

        private void ReadBuffer(int version, BinaryReader reader)
        {
            var context = new Context
            {
                Version = version,
            };

            Format = KV3IDLookup.GetByValue(new Guid(reader.ReadBytes(16)));

            var compressionMethod = reader.ReadUInt32();
            ushort compressionDictionaryId = 0;
            ushort compressionFrameSize = 0;
            var countBytes1 = 0;
            var countBytes4 = 0;
            var countBytes8 = 0;
            var countTypes = 0;
            var countObjects = 0;
            var countArrays = 0;
            var sizeUncompressedTotal = 0;
            var sizeCompressedTotal = 0;
            var countBlocks = 0;
            var sizeBinaryBlobsBytes = 0;

            if (version == 1)
            {
                // Version 1 did not have extra compression data
                countBytes1 = reader.ReadInt32();
                countBytes4 = reader.ReadInt32();
                countBytes8 = reader.ReadInt32();
                sizeUncompressedTotal = reader.ReadInt32();

                sizeCompressedTotal = (int)(Size - (reader.BaseStream.Position - Offset));
            }
            else
            {
                compressionDictionaryId = reader.ReadUInt16();
                compressionFrameSize = reader.ReadUInt16();
                countBytes1 = reader.ReadInt32();
                countBytes4 = reader.ReadInt32();
                countBytes8 = reader.ReadInt32();
                countTypes = reader.ReadInt32();
                countObjects = reader.ReadUInt16();
                countArrays = reader.ReadUInt16();
                sizeUncompressedTotal = reader.ReadInt32();
                sizeCompressedTotal = reader.ReadInt32();
                countBlocks = reader.ReadInt32();
                sizeBinaryBlobsBytes = reader.ReadInt32();
            }

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

            var buffer1Raw = ArrayPool<byte>.Shared.Rent(version < 5 && compressionMethod == 2 ? sizeUncompressedBuffer1 + sizeBinaryBlobsBytes : sizeUncompressedBuffer1);
            byte[] buffer2Raw = null;
            byte[] binaryBlobsRaw = null;
            ZstdSharp.Decompressor zstdDecompressor = null;

            try
            {
                ArraySegment<byte> bufferWithBinaryBlobSizes = null;

                // Buffer 1
                {
                    var buffer1Span = new ArraySegment<byte>(buffer1Raw, 0, sizeUncompressedBuffer1);

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

                        reader.Read(buffer1Span);
                    }
                    else if (compressionMethod == 1) // LZ4
                    {
                        if (compressionDictionaryId != 0)
                        {
                            throw new UnexpectedMagicException("Unhandled", compressionDictionaryId, nameof(compressionDictionaryId));
                        }

                        if (compressionFrameSize != 16384 && version >= 2)
                        {
                            throw new UnexpectedMagicException("Unhandled", compressionFrameSize, nameof(compressionFrameSize));
                        }

                        Debug.Assert(sizeCompressedBuffer1 > 0);

                        DecompressLZ4(reader, buffer1Span, sizeCompressedBuffer1);
                    }
                    else if (compressionMethod == 2) // ZSTD
                    {
                        Debug.Assert(version >= 2);

                        if (compressionDictionaryId != 0)
                        {
                            throw new UnexpectedMagicException("Unhandled", compressionDictionaryId, nameof(compressionDictionaryId));
                        }

                        if (compressionFrameSize != 0)
                        {
                            throw new UnexpectedMagicException("Unhandled", compressionFrameSize, nameof(compressionFrameSize));
                        }

                        Debug.Assert(sizeCompressedBuffer1 > 0);

                        var outBufferLength = sizeUncompressedBuffer1;

                        // Before version 5, when using zstd, both the buffer and binary blobs were compressed together
                        if (version < 5)
                        {
                            outBufferLength += sizeBinaryBlobsBytes;
                        }

                        zstdDecompressor = new ZstdSharp.Decompressor();

                        DecompressZSTD(zstdDecompressor, reader, buffer1Raw.AsSpan(0, outBufferLength), sizeCompressedBuffer1);
                    }
                    else
                    {
                        throw new UnexpectedMagicException("Unknown compression method", compressionMethod, nameof(compressionMethod));
                    }

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
                        Align(ref offset, 2);

                        var end = offset + countBytes2 * 2;
                        buffer1.Bytes2 = buffer1Span[offset..end];
                        offset = end;
                    }

                    if (countBytes4 > 0)
                    {
                        Align(ref offset, 4);

                        var end = offset + countBytes4 * 4;
                        buffer1.Bytes4 = buffer1Span[offset..end];
                        offset = end;
                    }

                    if (countBytes8 > 0)
                    {
                        Align(ref offset, 8);

                        var end = offset + countBytes8 * 8;
                        buffer1.Bytes8 = buffer1Span[offset..end];
                        offset = end;
                    }
                    else if (version < 5)
                    {
                        // For some reason V5 does not align this when empty, but earlier versions did
                        Align(ref offset, 8);
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
                        int typesLength;

                        if (version == 1)
                        {
                            typesLength = sizeUncompressedTotal - offset - 4;
                        }
                        else
                        {
                            typesLength = countTypes - offset + stringsStartOffset;
                        }

                        context.Types = buffer1Span[offset..(offset + typesLength)];
                        offset += typesLength;

                        if (countBlocks == 0)
                        {
                            var trailer = MemoryMarshal.Read<uint>(buffer1Span[offset..]);
                            offset += 4;
                            UnexpectedMagicException.Assert(trailer == 0xFFEEDD00, trailer);

                            Debug.Assert(buffer1Span.Count == offset);
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
                    buffer2Raw = ArrayPool<byte>.Shared.Rent(sizeUncompressedBuffer2);
                    var buffer2Span = new ArraySegment<byte>(buffer2Raw, 0, sizeUncompressedBuffer2);

                    if (compressionMethod == 0) // uncompressed
                    {
                        Debug.Assert(sizeCompressedBuffer2 == 0);

                        reader.Read(buffer2Span);
                    }
                    else if (compressionMethod == 1) // LZ4
                    {
                        Debug.Assert(sizeCompressedBuffer2 > 0);

                        DecompressLZ4(reader, buffer2Span, sizeCompressedBuffer2);
                    }
                    else if (compressionMethod == 2) // ZSTD
                    {
                        Debug.Assert(sizeCompressedBuffer2 > 0);

                        zstdDecompressor ??= new ZstdSharp.Decompressor();

                        DecompressZSTD(zstdDecompressor, reader, buffer2Span, sizeCompressedBuffer2);
                    }
                    else
                    {
                        throw new UnexpectedMagicException("Unknown compression method", compressionMethod, nameof(compressionMethod));
                    }

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
                        Align(ref offset, 2);

                        end = offset + countBytes2_buffer2 * 2;
                        buffer2.Bytes2 = buffer2Span[offset..end];
                        offset = end;
                    }

                    if (countBytes4_buffer2 > 0)
                    {
                        Align(ref offset, 4);

                        end = offset + countBytes4_buffer2 * 4;
                        buffer2.Bytes4 = buffer2Span[offset..end];
                        offset = end;
                    }

                    if (countBytes8_buffer2 > 0)
                    {
                        Align(ref offset, 8);

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
                    Debug.Assert(version >= 2);
                    Debug.Assert(bufferWithBinaryBlobSizes != null);

                    {
                        var end = countBlocks * sizeof(int);
                        context.BinaryBlobLengths = bufferWithBinaryBlobSizes[..end];
                        bufferWithBinaryBlobSizes = bufferWithBinaryBlobSizes[end..];

                        var trailer = MemoryMarshal.Read<uint>(bufferWithBinaryBlobSizes);
                        bufferWithBinaryBlobSizes = bufferWithBinaryBlobSizes[sizeof(int)..];
                        UnexpectedMagicException.Assert(trailer == 0xFFEEDD00, trailer);
                    }

                    if (compressionMethod == 0) // Uncompressed
                    {
                        binaryBlobsRaw = ArrayPool<byte>.Shared.Rent(sizeBinaryBlobsBytes);
                        context.BinaryBlobs = new ArraySegment<byte>(binaryBlobsRaw, 0, sizeBinaryBlobsBytes);
                        reader.Read(context.BinaryBlobs);
                    }
                    else if (compressionMethod == 1) // LZ4
                    {
                        binaryBlobsRaw = ArrayPool<byte>.Shared.Rent(sizeBinaryBlobsBytes);
                        context.BinaryBlobs = new ArraySegment<byte>(binaryBlobsRaw, 0, sizeBinaryBlobsBytes);

                        using var lz4decoder = new LZ4ChainDecoder(compressionFrameSize, 0);

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
                    else if (compressionMethod == 2) // ZSTD
                    {
                        if (version >= 5)
                        {
                            UnexpectedMagicException.Assert(sizeBlockCompressedSizesBytes == 0, sizeBlockCompressedSizesBytes);

                            var sizeCompressedBinaryBlobs = sizeCompressedTotal - sizeCompressedBuffer1 - sizeCompressedBuffer2;

                            binaryBlobsRaw = ArrayPool<byte>.Shared.Rent(sizeBinaryBlobsBytes);
                            context.BinaryBlobs = new ArraySegment<byte>(binaryBlobsRaw, 0, sizeBinaryBlobsBytes);

                            zstdDecompressor ??= new ZstdSharp.Decompressor();

                            DecompressZSTD(zstdDecompressor, reader, context.BinaryBlobs, sizeCompressedBinaryBlobs);
                        }
                        else
                        {
                            // This is supposed to be a streaming decompress using ZSTD_decompressStream,
                            // but as it turns out, zstd unwrap above already decompressed all of the blocks for us.
                            // It's possible that Valve's code needs extra decompress because they set ZSTD_d_stableOutBuffer parameter.
                            context.BinaryBlobs = new ArraySegment<byte>(buffer1Raw, sizeUncompressedBuffer1, sizeBinaryBlobsBytes);
                        }
                    }
                    else
                    {
                        throw new UnexpectedMagicException("Unsupported compression method in block decoder", compressionMethod, nameof(compressionMethod));
                    }

                    {
                        var trailer = reader.ReadUInt32();
                        UnexpectedMagicException.Assert(trailer == 0xFFEEDD00, trailer);
                    }
                }

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
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer1Raw);

                if (buffer2Raw != null)
                {
                    ArrayPool<byte>.Shared.Return(buffer2Raw);
                }

                if (binaryBlobsRaw != null)
                {
                    ArrayPool<byte>.Shared.Return(binaryBlobsRaw);
                }

                zstdDecompressor?.Dispose();
            }
        }

        private static (KV3BinaryNodeType Type, KVFlag Flag) ReadType(Context context)
        {
            var databyte = context.Types[0];
            context.Types = context.Types[1..];
            var flagInfo = KVFlag.None;

            if (context.Version >= 3)
            {
                if ((databyte & 0x80) > 0)
                {
                    databyte &= 0x3F; // Remove the flag bit

                    flagInfo = (KVFlag)context.Types[0];
                    context.Types = context.Types[1..];

                    if (flagInfo > KVFlag.MaxPersistedFlag)
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
                    Debug.Assert(databyte == (int)KV3BinaryNodeType.STRING);
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

            return ((KV3BinaryNodeType)databyte, flagInfo);
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

        private static KVObject ReadBinaryValue(Context context, string name, KV3BinaryNodeType datatype, KVFlag flagInfo, KVObject parent)
        {
            // We don't support non-object roots properly, so this is a hack to handle "null" kv3
            if (datatype != KV3BinaryNodeType.OBJECT && parent == null)
            {
                name ??= "root";
                parent ??= new KVObject(name);
            }

            var buffer = context.Buffer;

            switch (datatype)
            {
                // Hardcoded values
                case KV3BinaryNodeType.NULL:
                    parent.AddProperty(name, MakeValue(datatype, null, flagInfo));
                    break;
                case KV3BinaryNodeType.BOOLEAN_TRUE:
                    parent.AddProperty(name, MakeValue(datatype, true, flagInfo));
                    break;
                case KV3BinaryNodeType.BOOLEAN_FALSE:
                    parent.AddProperty(name, MakeValue(datatype, false, flagInfo));
                    break;
                case KV3BinaryNodeType.INT64_ZERO:
                    parent.AddProperty(name, MakeValue(datatype, 0L, flagInfo));
                    break;
                case KV3BinaryNodeType.INT64_ONE:
                    parent.AddProperty(name, MakeValue(datatype, 1L, flagInfo));
                    break;
                case KV3BinaryNodeType.DOUBLE_ZERO:
                    parent.AddProperty(name, MakeValue(datatype, 0.0D, flagInfo));
                    break;
                case KV3BinaryNodeType.DOUBLE_ONE:
                    parent.AddProperty(name, MakeValue(datatype, 1.0D, flagInfo));
                    break;

                // 1 byte values
                case KV3BinaryNodeType.BOOLEAN:
                    {
                        var value = buffer.Bytes1[0] == 1;
                        buffer.Bytes1 = buffer.Bytes1[1..];

                        parent.AddProperty(name, MakeValue(datatype, value, flagInfo));
                    }
                    break;
                // TODO: 22 might be INT32_AS_BYTE, and 23 is UINT32_AS_BYTE
                case KV3BinaryNodeType.INT32_AS_BYTE:
                    {
                        Debug.Assert(context.Version >= 4);

                        var value = (int)buffer.Bytes1[0];
                        buffer.Bytes1 = buffer.Bytes1[1..];

                        parent.AddProperty(name, MakeValue(datatype, value, flagInfo));
                    }
                    break;

                // 2 byte values
                case KV3BinaryNodeType.INT16:
                    {
                        Debug.Assert(context.Version >= 4);

                        var value = MemoryMarshal.Read<short>(buffer.Bytes2);
                        buffer.Bytes2 = buffer.Bytes2[sizeof(short)..];

                        parent.AddProperty(name, MakeValue(datatype, value, flagInfo));
                    }
                    break;
                case KV3BinaryNodeType.UINT16:
                    {
                        Debug.Assert(context.Version >= 4);

                        var value = MemoryMarshal.Read<ushort>(buffer.Bytes2);
                        buffer.Bytes2 = buffer.Bytes2[sizeof(ushort)..];

                        parent.AddProperty(name, MakeValue(datatype, value, flagInfo));
                    }
                    break;

                // 4 byte values
                case KV3BinaryNodeType.INT32:
                    {
                        var value = MemoryMarshal.Read<int>(buffer.Bytes4);
                        buffer.Bytes4 = buffer.Bytes4[sizeof(int)..];

                        parent.AddProperty(name, MakeValue(datatype, value, flagInfo));
                    }
                    break;
                case KV3BinaryNodeType.UINT32:
                    {
                        var value = MemoryMarshal.Read<uint>(buffer.Bytes4);
                        buffer.Bytes4 = buffer.Bytes4[sizeof(uint)..];

                        parent.AddProperty(name, MakeValue(datatype, value, flagInfo));
                    }
                    break;
                case KV3BinaryNodeType.FLOAT:
                    {
                        Debug.Assert(context.Version >= 4);

                        var value = MemoryMarshal.Read<float>(buffer.Bytes4);
                        buffer.Bytes4 = buffer.Bytes4[sizeof(float)..];

                        parent.AddProperty(name, MakeValue(datatype, value, flagInfo));
                    }
                    break;

                // 8 byte values
                case KV3BinaryNodeType.INT64:
                    {
                        var value = MemoryMarshal.Read<long>(buffer.Bytes8);
                        buffer.Bytes8 = buffer.Bytes8[sizeof(long)..];

                        parent.AddProperty(name, MakeValue(datatype, value, flagInfo));
                    }
                    break;
                case KV3BinaryNodeType.UINT64:
                    {
                        var value = MemoryMarshal.Read<ulong>(buffer.Bytes8);
                        buffer.Bytes8 = buffer.Bytes8[sizeof(ulong)..];

                        parent.AddProperty(name, MakeValue(datatype, value, flagInfo));
                    }
                    break;
                case KV3BinaryNodeType.DOUBLE:
                    {
                        var value = MemoryMarshal.Read<double>(buffer.Bytes8);
                        buffer.Bytes8 = buffer.Bytes8[sizeof(double)..];

                        parent.AddProperty(name, MakeValue(datatype, value, flagInfo));
                    }
                    break;

                // Custom types
                case KV3BinaryNodeType.STRING:
                    {
                        var id = MemoryMarshal.Read<int>(buffer.Bytes4);
                        buffer.Bytes4 = buffer.Bytes4[sizeof(int)..];

                        parent.AddProperty(name, MakeValue(datatype, id == -1 ? string.Empty : context.Strings[id], flagInfo));
                    }
                    break;
                case KV3BinaryNodeType.BINARY_BLOB when context.Version < 2:
                    {
                        var blockLength = MemoryMarshal.Read<int>(buffer.Bytes4);
                        buffer.Bytes4 = buffer.Bytes4[sizeof(int)..];
                        byte[] output;

                        if (blockLength > 0)
                        {
                            output = [.. buffer.Bytes1[..blockLength]]; // explicit copy
                            buffer.Bytes1 = buffer.Bytes1[blockLength..];
                        }
                        else
                        {
                            output = [];
                        }

                        parent.AddProperty(name, MakeValue(datatype, output, flagInfo));
                    }
                    break;
                case KV3BinaryNodeType.BINARY_BLOB:
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
                case KV3BinaryNodeType.ARRAY:
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
                case KV3BinaryNodeType.ARRAY_TYPED:
                case KV3BinaryNodeType.ARRAY_TYPE_BYTE_LENGTH:
                    {
                        int arrayLength;

                        if (datatype == KV3BinaryNodeType.ARRAY_TYPE_BYTE_LENGTH)
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
                case KV3BinaryNodeType.ARRAY_TYPE_AUXILIARY_BUFFER:
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

                case KV3BinaryNodeType.OBJECT:
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

        private static KVValueType ConvertBinaryOnlyKVType(KV3BinaryNodeType type)
        {
            // TODO: Why we are upcasting (u)int32 to 64
#pragma warning disable IDE0066 // Convert switch statement to expression
            switch (type)
            {
                case KV3BinaryNodeType.BOOLEAN:
                case KV3BinaryNodeType.BOOLEAN_TRUE:
                case KV3BinaryNodeType.BOOLEAN_FALSE:
                    return KVValueType.Boolean;
                case KV3BinaryNodeType.INT16:
                    return KVValueType.Int16;
                case KV3BinaryNodeType.UINT16:
                    return KVValueType.UInt16;
                case KV3BinaryNodeType.INT64:
                case KV3BinaryNodeType.INT32:
                case KV3BinaryNodeType.INT64_ZERO:
                case KV3BinaryNodeType.INT64_ONE:
                case KV3BinaryNodeType.INT32_AS_BYTE:
                    return KVValueType.Int64;
                case KV3BinaryNodeType.UINT64:
                case KV3BinaryNodeType.UINT32:
                    return KVValueType.UInt64;
                case KV3BinaryNodeType.FLOAT:
                    return KVValueType.FloatingPoint;
                case KV3BinaryNodeType.DOUBLE:
                case KV3BinaryNodeType.DOUBLE_ZERO:
                case KV3BinaryNodeType.DOUBLE_ONE:
                    return KVValueType.FloatingPoint64;
                case KV3BinaryNodeType.ARRAY:
                case KV3BinaryNodeType.ARRAY_TYPED:
                case KV3BinaryNodeType.ARRAY_TYPE_BYTE_LENGTH:
                case KV3BinaryNodeType.ARRAY_TYPE_AUXILIARY_BUFFER:
                    return KVValueType.Array;
                case KV3BinaryNodeType.OBJECT:
                    return KVValueType.Collection;
                case KV3BinaryNodeType.STRING:
                    return KVValueType.String;
                case KV3BinaryNodeType.BINARY_BLOB:
                    return KVValueType.BinaryBlob;
                case KV3BinaryNodeType.NULL:
                    return KVValueType.Null;
                default:
                    throw new NotImplementedException($"Unknown type {type}");
            }
#pragma warning restore IDE0066 // Convert switch statement to expression
        }

        private static KVValue MakeValue(KV3BinaryNodeType type, object data, KVFlag flag = KVFlag.None)
        {
            var realType = ConvertBinaryOnlyKVType(type);
            return new KVValue(realType, flag, data);
        }

        /// <summary>
        /// Gets the KeyValues3 data as a KV3File object.
        /// </summary>
        /// <returns>A KV3File object containing the data and format.</returns>
#pragma warning disable CA1024 // Use properties where appropriate
        public KV3File GetKV3File()
#pragma warning restore CA1024 // Use properties where appropriate
        {
            return new KV3File(Data, format: Format);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Converts the binary KV3 data to text format and writes it.
        /// </remarks>
        public override void WriteText(IndentedTextWriter writer)
        {
            GetKV3File().WriteText(writer);
        }

        private static string ReadNullTermUtf8String(ref ArraySegment<byte> buffer, ref int offset)
        {
            var nullByte = buffer.AsSpan().IndexOf((byte)0);
            var str = buffer[..nullByte];
            buffer = buffer[(nullByte + 1)..];

            offset += nullByte + 1;

            return System.Text.Encoding.UTF8.GetString(str);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Align(ref int offset, int alignment)
        {
            alignment -= 1;
            offset += alignment;
            offset &= ~alignment;
        }

        /// <summary>
        /// Converts binary KV3 data to text format. This method is exposed for unmanaged callers.
        /// </summary>
        /// <param name="dataPtr">Pointer to the binary KV3 data.</param>
        /// <param name="dataLength">Length of the binary data.</param>
        /// <returns>Pointer to the text representation of the KV3 data.</returns>
        [UnmanagedCallersOnly(EntryPoint = "ConvertBinaryKV3ToText")]
        public static IntPtr ConvertBinaryKV3ToText(IntPtr dataPtr, int dataLength)
        {
            try
            {
                var data = new byte[dataLength];
                Marshal.Copy(dataPtr, data, 0, dataLength);

                var kv3 = new BinaryKV3(BlockType.Undefined);
                using var stream = new MemoryStream(data);
                using var reader = new BinaryReader(stream);
                kv3.Read(reader);

                var text = kv3.ToString();
                var pointer = Marshal.StringToHGlobalAnsi(text);

                return pointer;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return Marshal.StringToHGlobalAnsi(string.Empty);
            }
        }
    }
}
