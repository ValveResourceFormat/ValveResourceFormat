using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Encoders;
using ValveKeyValue;
using ValveKeyValue.KeyValues3;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Compression methods supported for binary KV3 serialization.
    /// </summary>
    public enum KV3BinaryCompressionMethod
    {
        /// <summary>Do not compress the serialized data.</summary>
        Uncompressed = 0,

        /// <summary>Compress the serialized data with LZ4.</summary>
        Lz4 = 1,

        /// <summary>Compress the serialized data with Zstandard.</summary>
        Zstd = 2,
    }

    /// <summary>
    /// Represents a binary KeyValues3 data block.
    /// </summary>
    public partial class BinaryKV3 : Block
    {
        private readonly BlockType KVBlockType;

        // Objects up to this many members are list backed, a linear key search is cheaper than a dictionary at this size
        private const int MaxListBackedObjectLength = 8;

        private const int ZstdStreamInputSize = 128 * 1024;

        /// <inheritdoc/>
        public override BlockType Type => KVBlockType;

        /// <summary>
        /// Magic number for VKV3 format.
        /// </summary>
        public const int MAGIC0 = 0x03564B56; // VKV3 (3 isn't ascii, it's 0x03)

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
        /// <remarks>Can be set to null to release the data. Next probe will deserialize again.</remarks>
        [AllowNull]
        public KVDocument Data
        {
            get
            {
                if (data == null)
                {
                    lock (Resource.ReaderLock)
                    {
                        var reader = Resource.Reader ?? throw new InvalidOperationException($"Cannot read block {Type} because the resource's reader is not available.");
                        ReadDocument(reader);
                    }
                }

                return data!;
            }

            set => data = value;
        }

        private KVDocument? data;

        /// <summary>
        /// Gets or sets the binary KV3 version used when serializing this block. Supported values are 4 and 5.
        /// </summary>
        public int SerializationVersion { get; set; } = 5;

        /// <summary>
        /// Gets or sets the compression method used when serializing this block.
        /// </summary>
        public KV3BinaryCompressionMethod SerializationCompressionMethod { get; set; } = KV3BinaryCompressionMethod.Uncompressed;

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
            public byte[][] BinaryBlobs = [];
            public int NextBinaryBlob;
            public string[] Strings = null!;
            public Buffers Buffer = null!;
            public Buffers AuxiliaryBuffer = null!;
        }

        private struct Header
        {
            public int Version;
            public KV3ID Format;
            public KV3BinaryCompressionMethod CompressionMethod;
            public ushort CompressionFrameSize;
            public int CountBytes1;
            public int CountBytes2;
            public int CountBytes4;
            public int CountBytes8;
            public int CountTypes;
            public int SizeUncompressedTotal;
            public int SizeCompressedTotal;
            public int CountBlocks;
            public int SizeBinaryBlobsBytes;
            public int SizeBlockCompressedSizesBytes;
            public int SizeUncompressedBuffer1;
            public int SizeCompressedBuffer1;
            public int SizeUncompressedBuffer2;
            public int SizeCompressedBuffer2;
            public int CountBytes1Buffer2;
            public int CountBytes2Buffer2;
            public int CountBytes4Buffer2;
            public int CountBytes8Buffer2;
            public int CountObjectsBuffer2;

            // Before version 5, zstd stores the buffer and the binary blobs as two consecutive frames,
            // and the compressed size covers both, so they are decompressed together
            public readonly int SizeDecompressedBuffer1 => Version < 5 && CompressionMethod == KV3BinaryCompressionMethod.Zstd
                ? SizeUncompressedBuffer1 + SizeBinaryBlobsBytes
                : SizeUncompressedBuffer1;
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
            Data = new KVDocument(new KVHeader { Format = format }, null, data);
        }

        /// <inheritdoc/>
        public override void Read(BinaryReader reader) => ReadDocument(reader);

        private void ReadDocument(BinaryReader reader)
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

        internal static void DecompressLZ4(BinaryReader reader, Span<byte> output, int compressedSize)
        {
            var inputBuf = ArrayPool<byte>.Shared.Rent(compressedSize);

            try
            {
                var input = inputBuf.AsSpan(0, compressedSize);
                reader.BaseStream.ReadExactly(input);

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

        internal static void DecompressZSTD(ZstdSharp.Decompressor zstdDecompressor, BinaryReader reader, Span<byte> output, int compressedSize)
        {
            var inputBuf = ArrayPool<byte>.Shared.Rent(compressedSize);

            try
            {
                var input = inputBuf.AsSpan(0, compressedSize);
                reader.BaseStream.ReadExactly(input);

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

        // Streams the compressed blobs in chunks straight into each blob, so neither side needs one large buffer
        private static void DecompressZSTDBinaryBlobs(ZstdSharp.Decompressor zstdDecompressor, BinaryReader reader, byte[][] blobs, int compressedSize)
        {
            var inputBuf = ArrayPool<byte>.Shared.Rent(Math.Min(compressedSize, ZstdStreamInputSize));
            var input = new ArraySegment<byte>(inputBuf, 0, 0);
            var remainingInput = compressedSize;
            var status = OperationStatus.NeedMoreData;

            int Unwrap(Span<byte> output)
            {
                if (input.Count == 0)
                {
                    var length = Math.Min(remainingInput, inputBuf.Length);
                    reader.BaseStream.ReadExactly(inputBuf, 0, length);
                    remainingInput -= length;
                    input = new ArraySegment<byte>(inputBuf, 0, length);
                }

                status = zstdDecompressor.UnwrapStream(input, output, out var consumed, out var written);

                if (status == OperationStatus.InvalidData || (consumed == 0 && written == 0))
                {
                    throw new InvalidDataException("Failed to decompress ZSTD binary blobs");
                }

                input = input[consumed..];
                return written;
            }

            try
            {
                foreach (var blob in blobs)
                {
                    var blobOffset = 0;

                    while (blobOffset < blob.Length)
                    {
                        blobOffset += Unwrap(blob.AsSpan(blobOffset));
                    }
                }

                // The end of the frame, such as its checksum, may still be unread after the last blob is filled.
                // Decoding into an empty span makes no progress, so a single byte is given to catch excess data.
                Span<byte> excess = stackalloc byte[1];

                while (status != OperationStatus.Done || input.Count > 0 || remainingInput > 0)
                {
                    if (Unwrap(excess) > 0)
                    {
                        throw new InvalidDataException("ZSTD binary blobs decompress to more than the blob lengths");
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(inputBuf);
            }
        }

        /// <summary>
        /// Whether any decompressed buffer of a compressed binary KV3 block, or the stored bytes of any other block, satisfy a predicate.
        /// Binary blobs are only visited where zstd before version 5 compresses them together with the buffer.
        /// </summary>
        internal static bool AnyDecompressedBuffer(ArraySegment<byte> block, Func<ReadOnlySpan<byte>, bool> predicate)
        {
            if (block.Count < 4)
            {
                return predicate(block);
            }

            using var reader = new BinaryReader(new MemoryStream(block.Array!, block.Offset, block.Count, writable: false));

            var magic = reader.ReadUInt32();

            if (magic == MAGIC0)
            {
                var buffer = DecompressVersion0(reader, block.Count, out _, out var length);

                try
                {
                    return predicate(buffer.AsSpan(0, length));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            if (!IsBinaryKV3(magic))
            {
                return predicate(block);
            }

            var header = ReadHeader((int)(magic & 0xFF), reader, block.Count);

            if (header.CompressionMethod == KV3BinaryCompressionMethod.Uncompressed)
            {
                return predicate(block);
            }

            ZstdSharp.Decompressor? zstdDecompressor = null;

            try
            {
                return AnyDecompressed(reader, header.CompressionMethod, ref zstdDecompressor, header.SizeDecompressedBuffer1, header.SizeCompressedBuffer1, predicate)
                    || (header.Version >= 5 && AnyDecompressed(reader, header.CompressionMethod, ref zstdDecompressor, header.SizeUncompressedBuffer2, header.SizeCompressedBuffer2, predicate));
            }
            finally
            {
                zstdDecompressor?.Dispose();
            }
        }

        private static bool AnyDecompressed(BinaryReader reader, KV3BinaryCompressionMethod compressionMethod, ref ZstdSharp.Decompressor? zstdDecompressor,
            int size, int compressedSize, Func<ReadOnlySpan<byte>, bool> predicate)
        {
            if (size < 0 || compressedSize < 0 || compressedSize > reader.BaseStream.Length - reader.BaseStream.Position)
            {
                throw new InvalidDataException("A compressed KV3 buffer is outside of the block.");
            }

            var output = ArrayPool<byte>.Shared.Rent(size);

            try
            {
                ReadBufferData(reader, compressionMethod, ref zstdDecompressor, output.AsSpan(0, size), compressedSize);
                return predicate(output.AsSpan(0, size));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(output);
            }
        }

        private static Header ReadHeader(int version, BinaryReader reader, long blockEnd)
        {
            var header = new Header
            {
                Version = version,
                Format = KV3IDLookup.GetByValue(reader.ReadGuid()),
                CompressionMethod = (KV3BinaryCompressionMethod)reader.ReadUInt32(),
            };

            if (!Enum.IsDefined(header.CompressionMethod))
            {
                throw new UnexpectedMagicException("Unknown compression method", (uint)header.CompressionMethod, nameof(header.CompressionMethod));
            }

            if (version == 1)
            {
                // Version 1 did not have extra compression data
                header.CountBytes1 = reader.ReadInt32();
                header.CountBytes4 = reader.ReadInt32();
                header.CountBytes8 = reader.ReadInt32();
                header.SizeUncompressedTotal = reader.ReadInt32();

                header.SizeCompressedTotal = (int)(blockEnd - reader.BaseStream.Position);
            }
            else
            {
                var compressionDictionaryId = reader.ReadUInt16();

                if (compressionDictionaryId != 0)
                {
                    throw new UnexpectedMagicException("Unhandled", compressionDictionaryId, nameof(compressionDictionaryId));
                }

                header.CompressionFrameSize = reader.ReadUInt16();
                header.CountBytes1 = reader.ReadInt32();
                header.CountBytes4 = reader.ReadInt32();
                header.CountBytes8 = reader.ReadInt32();
                header.CountTypes = reader.ReadInt32();
                reader.ReadUInt16(); // countObjects
                reader.ReadUInt16(); // countArrays
                header.SizeUncompressedTotal = reader.ReadInt32();
                header.SizeCompressedTotal = reader.ReadInt32();
                header.CountBlocks = reader.ReadInt32();
                header.SizeBinaryBlobsBytes = reader.ReadInt32();

                if (header.CompressionFrameSize != (header.CompressionMethod == KV3BinaryCompressionMethod.Lz4 ? CompressionFrameSize : 0))
                {
                    throw new UnexpectedMagicException("Unhandled", header.CompressionFrameSize, nameof(header.CompressionFrameSize));
                }
            }

            if (version >= 4)
            {
                header.CountBytes2 = reader.ReadInt32();
                header.SizeBlockCompressedSizesBytes = reader.ReadInt32();
            }

            if (version >= 5)
            {
                header.SizeUncompressedBuffer1 = reader.ReadInt32();
                header.SizeCompressedBuffer1 = reader.ReadInt32();
                header.SizeUncompressedBuffer2 = reader.ReadInt32();
                header.SizeCompressedBuffer2 = reader.ReadInt32();
                header.CountBytes1Buffer2 = reader.ReadInt32();
                header.CountBytes2Buffer2 = reader.ReadInt32();
                header.CountBytes4Buffer2 = reader.ReadInt32();
                header.CountBytes8Buffer2 = reader.ReadInt32();
                // The node and element counts are only allocation hints, the data itself is self-describing
                reader.ReadInt32(); // countNodes
                header.CountObjectsBuffer2 = reader.ReadInt32();
                reader.ReadInt32(); // countArrays
                reader.ReadInt32(); // countArrayElements

                Debug.Assert(header.SizeUncompressedTotal == header.SizeUncompressedBuffer1 + header.SizeUncompressedBuffer2);
            }
            else
            {
                header.SizeCompressedBuffer1 = header.SizeCompressedTotal;
                header.SizeUncompressedBuffer1 = header.SizeUncompressedTotal;
            }

            Debug.Assert(header.CompressionMethod != KV3BinaryCompressionMethod.Zstd || version >= 2);

            return header;
        }

        private static void ReadBufferData(BinaryReader reader, KV3BinaryCompressionMethod compressionMethod, ref ZstdSharp.Decompressor? zstdDecompressor, Span<byte> output, int compressedSize)
        {
            switch (compressionMethod)
            {
                case KV3BinaryCompressionMethod.Uncompressed:
                    reader.ReadExactly(output);
                    break;
                case KV3BinaryCompressionMethod.Lz4:
                    DecompressLZ4(reader, output, compressedSize);
                    break;
                case KV3BinaryCompressionMethod.Zstd:
                    zstdDecompressor ??= new ZstdSharp.Decompressor();
                    DecompressZSTD(zstdDecompressor, reader, output, compressedSize);
                    break;
            }
        }

        private void ReadBuffer(int version, BinaryReader reader)
        {
            var context = new Context
            {
                Version = version,
            };

            var header = ReadHeader(version, reader, Offset + Size);

            // Versions before 4 cannot be written, they will be serialized as version 5
            SerializationVersion = version == 4 ? 4 : 5;
            SerializationCompressionMethod = header.CompressionMethod;

            var buffer1Raw = ArrayPool<byte>.Shared.Rent(header.SizeDecompressedBuffer1);
            byte[]? buffer2Raw = null;
            ZstdSharp.Decompressor? zstdDecompressor = null;

            try
            {
                ArraySegment<byte> bufferWithBinaryBlobSizes = default;

                // Buffer 1
                {
                    var buffer1Span = new ArraySegment<byte>(buffer1Raw, 0, header.SizeUncompressedBuffer1);

                    ReadBufferData(reader, header.CompressionMethod, ref zstdDecompressor, buffer1Raw.AsSpan(0, header.SizeDecompressedBuffer1), header.SizeCompressedBuffer1);

                    var buffer1 = new Buffers();

                    var offset = 0;

                    if (header.CountBytes1 > 0)
                    {
                        var end = offset + header.CountBytes1;
                        buffer1.Bytes1 = buffer1Span[offset..end];
                        offset = end;
                    }

                    if (header.CountBytes2 > 0)
                    {
                        Align(ref offset, 2);

                        var end = offset + header.CountBytes2 * 2;
                        buffer1.Bytes2 = buffer1Span[offset..end];
                        offset = end;
                    }

                    if (header.CountBytes4 > 0)
                    {
                        Align(ref offset, 4);

                        var end = offset + header.CountBytes4 * 4;
                        buffer1.Bytes4 = buffer1Span[offset..end];
                        offset = end;
                    }

                    if (header.CountBytes8 > 0)
                    {
                        Align(ref offset, 8);

                        var end = offset + header.CountBytes8 * 8;
                        buffer1.Bytes8 = buffer1Span[offset..end];
                        offset = end;
                    }
                    else if (version < 5)
                    {
                        // Before version 5 empty lanes are still aligned, empty 2 and 4 byte lanes are covered by this too
                        Align(ref offset, 8);
                    }

                    Debug.Assert(header.CountBytes4 > 0); // should be guaranteed to be at least 1 for the strings count

                    var countStrings = MemoryMarshal.Read<int>(buffer1.Bytes4);
                    buffer1.Bytes4 = buffer1.Bytes4[sizeof(int)..];
                    context.Strings = new string[countStrings];

                    // Before version 5 there is only one buffer, so auxiliary reads use it as well
                    context.AuxiliaryBuffer = buffer1;

                    if (version >= 5)
                    {
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
                            typesLength = header.SizeUncompressedTotal - offset - 4;
                        }
                        else
                        {
                            typesLength = header.CountTypes - offset + stringsStartOffset;
                        }

                        context.Types = buffer1Span[offset..(offset + typesLength)];
                        offset += typesLength;

                        if (header.CountBlocks == 0)
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
                    buffer2Raw = ArrayPool<byte>.Shared.Rent(header.SizeUncompressedBuffer2);
                    var buffer2Span = new ArraySegment<byte>(buffer2Raw, 0, header.SizeUncompressedBuffer2);

                    ReadBufferData(reader, header.CompressionMethod, ref zstdDecompressor, buffer2Span, header.SizeCompressedBuffer2);

                    var buffer2 = new Buffers();
                    context.Buffer = buffer2;

                    var end = header.CountObjectsBuffer2 * sizeof(int);
                    var offset = end;

                    context.ObjectLengths = buffer2Span[..end];

                    if (header.CountBytes1Buffer2 > 0)
                    {
                        end = offset + header.CountBytes1Buffer2;
                        buffer2.Bytes1 = buffer2Span[offset..end];
                        offset = end;
                    }

                    if (header.CountBytes2Buffer2 > 0)
                    {
                        Align(ref offset, 2);

                        end = offset + header.CountBytes2Buffer2 * 2;
                        buffer2.Bytes2 = buffer2Span[offset..end];
                        offset = end;
                    }

                    if (header.CountBytes4Buffer2 > 0)
                    {
                        Align(ref offset, 4);

                        end = offset + header.CountBytes4Buffer2 * 4;
                        buffer2.Bytes4 = buffer2Span[offset..end];
                        offset = end;
                    }

                    if (header.CountBytes8Buffer2 > 0)
                    {
                        Align(ref offset, 8);

                        end = offset + header.CountBytes8Buffer2 * 8;
                        buffer2.Bytes8 = buffer2Span[offset..end];
                        offset = end;
                    }

                    // Types in v5
                    context.Types = buffer2Span[offset..(offset + header.CountTypes)];
                    offset += header.CountTypes;

                    if (header.CountBlocks == 0)
                    {
                        var trailer = MemoryMarshal.Read<uint>(buffer2Span[offset..]);
                        offset += 4;
                        UnexpectedMagicException.Assert(trailer == 0xFFEEDD00, trailer);

                        Debug.Assert(buffer2Span.Count == offset + header.SizeBlockCompressedSizesBytes);
                    }
                    else
                    {
                        bufferWithBinaryBlobSizes = buffer2Span[offset..];
                    }
                }

                if (header.CountBlocks > 0)
                {
                    Debug.Assert(version >= 2);
                    Debug.Assert(bufferWithBinaryBlobSizes.Array != null);

                    {
                        var end = header.CountBlocks * sizeof(int);
                        var blobLengths = MemoryMarshal.Cast<byte, int>(bufferWithBinaryBlobSizes[..end]);
                        bufferWithBinaryBlobSizes = bufferWithBinaryBlobSizes[end..];

                        var trailer = MemoryMarshal.Read<uint>(bufferWithBinaryBlobSizes);
                        bufferWithBinaryBlobSizes = bufferWithBinaryBlobSizes[sizeof(int)..];
                        UnexpectedMagicException.Assert(trailer == 0xFFEEDD00, trailer);

                        context.BinaryBlobs = new byte[header.CountBlocks][];
                        var remainingBlobBytes = header.SizeBinaryBlobsBytes;

                        for (var i = 0; i < header.CountBlocks; i++)
                        {
                            var blobLength = blobLengths[i];

                            if (blobLength < 0 || blobLength > remainingBlobBytes)
                            {
                                throw new InvalidDataException($"Binary blob length {blobLength} exceeds the remaining {remainingBlobBytes} bytes");
                            }

                            remainingBlobBytes -= blobLength;
                            context.BinaryBlobs[i] = blobLength > 0 ? GC.AllocateUninitializedArray<byte>(blobLength) : [];
                        }

                        if (remainingBlobBytes != 0)
                        {
                            throw new InvalidDataException($"Binary blob lengths are {remainingBlobBytes} bytes short of {header.SizeBinaryBlobsBytes}");
                        }
                    }

                    if (header.CompressionMethod == KV3BinaryCompressionMethod.Uncompressed)
                    {
                        foreach (var blob in context.BinaryBlobs)
                        {
                            reader.BaseStream.ReadExactly(blob);
                        }
                    }
                    else if (header.CompressionMethod == KV3BinaryCompressionMethod.Lz4)
                    {
                        // Each blob is split into frames of up to the compression frame size, a frame never spans two blobs,
                        // and the frames are chained so that later frames can reference earlier blobs
                        using var lz4decoder = new LZ4ChainDecoder(header.CompressionFrameSize, 0);
                        var inputBuf = ArrayPool<byte>.Shared.Rent(ushort.MaxValue);

                        try
                        {
                            foreach (var blob in context.BinaryBlobs)
                            {
                                var blobOffset = 0;

                                while (blobOffset < blob.Length)
                                {
                                    var compressedBlockLength = MemoryMarshal.Read<ushort>(bufferWithBinaryBlobSizes);
                                    bufferWithBinaryBlobSizes = bufferWithBinaryBlobSizes[sizeof(ushort)..];

                                    var input = inputBuf.AsSpan(0, compressedBlockLength);
                                    reader.ReadExactly(input);

                                    if (!lz4decoder.DecodeAndDrain(input, blob.AsSpan(blobOffset), out var decoded) || decoded < 1)
                                    {
                                        throw new InvalidDataException("Failed to decompress LZ4 binary blob frame");
                                    }

                                    blobOffset += decoded;
                                }
                            }
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(inputBuf);
                        }

                        Debug.Assert(bufferWithBinaryBlobSizes.Count == 0);
                    }
                    else if (header.CompressionMethod == KV3BinaryCompressionMethod.Zstd)
                    {
                        if (version >= 5)
                        {
                            UnexpectedMagicException.Assert(header.SizeBlockCompressedSizesBytes == 0, header.SizeBlockCompressedSizesBytes);

                            var sizeCompressedBinaryBlobs = header.SizeCompressedTotal - header.SizeCompressedBuffer1 - header.SizeCompressedBuffer2;

                            zstdDecompressor ??= new ZstdSharp.Decompressor();

                            DecompressZSTDBinaryBlobs(zstdDecompressor, reader, context.BinaryBlobs, sizeCompressedBinaryBlobs);
                        }
                        else
                        {
                            // The blob frame was already decompressed together with the buffer frame above
                            var blobOffset = header.SizeUncompressedBuffer1;

                            foreach (var blob in context.BinaryBlobs)
                            {
                                buffer1Raw.AsSpan(blobOffset, blob.Length).CopyTo(blob);
                                blobOffset += blob.Length;
                            }
                        }
                    }
                    {
                        var trailer = reader.ReadUInt32();
                        UnexpectedMagicException.Assert(trailer == 0xFFEEDD00, trailer);
                    }
                }

                var (rootType, rootFlag) = ReadType(context);
                var root = ReadBinaryValue(context, rootType, rootFlag, context.Buffer);
                Data = new KVDocument(new KVHeader { Format = header.Format }, null, root);

                Debug.Assert(context.Types.Count == 0);
                Debug.Assert(context.ObjectLengths.Count == 0);
                Debug.Assert(context.NextBinaryBlob == context.BinaryBlobs.Length);
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
                if ((databyte & 0x80) != 0)
                {
                    flagInfo = (KVFlag)context.Types[0];
                    context.Types = context.Types[1..];

                    if (flagInfo == KVFlag.None || !Enum.IsDefined(flagInfo))
                    {
                        throw new UnexpectedMagicException("Unexpected kv3 flag", (int)flagInfo, nameof(flagInfo));
                    }
                }

                // No known writer sets this bit, readers skip the extra byte that follows
                if ((databyte & 0x40) != 0)
                {
                    context.Types = context.Types[1..];
                }

                databyte &= 0x3F;
            }
            else
            {
                if ((databyte & 0x80) != 0)
                {
                    flagInfo = ConvertLegacyFlags(context.Types[0]);
                    context.Types = context.Types[1..];
                }

                databyte &= 0x7F;
            }

            // Flags are not validated against the value type: subclass is only valid on objects,
            // and every other flag is only valid on strings
            return ((KV3BinaryNodeType)databyte, flagInfo);
        }

        private static string GetString(Context context, int id)
        {
            // Negative and out of range ids, including -1 for an empty string, resolve to an empty string
            return (uint)id < (uint)context.Strings.Length ? context.Strings[id] : string.Empty;
        }

        private static void ParseBinaryKV3(Context context, KVObject parent)
        {
            var (datatype, flagInfo) = ReadType(context);

            if (parent.IsArray)
            {
                parent.Add(ReadBinaryValue(context, datatype, flagInfo, context.Buffer));
                return;
            }

            var name = GetString(context, ReadLane<int>(ref context.Buffer.Bytes4));

            // A repeated member name replaces the earlier value
            parent[name] = ReadBinaryValue(context, datatype, flagInfo, context.Buffer);
        }

        private static T ReadLane<T>(ref ArraySegment<byte> lane) where T : unmanaged
        {
            var value = MemoryMarshal.Read<T>(lane);
            lane = lane[Unsafe.SizeOf<T>()..];
            return value;
        }

        private static ReadOnlySpan<T> ReadLaneSpan<T>(ref ArraySegment<byte> lane, int count) where T : unmanaged
        {
            var byteCount = count * Unsafe.SizeOf<T>();
            var values = MemoryMarshal.Cast<byte, T>(lane.AsSpan(0, byteCount));
            lane = lane[byteCount..];
            return values;
        }

        // Typed arrays of values stored in a lane are kept packed, so their elements are not separate objects.
        // Elements unpack to the same values ReadValue returns, so 8-bit integers widen to int and uint.
        private static KVObject? ReadPackedArray(KV3BinaryNodeType elementType, KVFlag elementFlag, Buffers lane, int count)
        {
            switch (elementType)
            {
                case KV3BinaryNodeType.BOOLEAN:
                {
                    var bytes = ReadLaneSpan<byte>(ref lane.Bytes1, count);
                    var values = new bool[count];
                    var valueBytes = MemoryMarshal.Cast<bool, byte>(values.AsSpan());

                    // Any non-zero byte is true, and a bool must hold exactly 1
                    TensorPrimitives.Min(bytes, (byte)1, valueBytes);

                    return KVObject.PackedArray(values, elementFlag);
                }
                case KV3BinaryNodeType.INT8:
                {
                    var bytes = ReadLaneSpan<sbyte>(ref lane.Bytes1, count);
                    var values = new int[count];

                    TensorPrimitives.ConvertToInt32(bytes, values);

                    return KVObject.PackedArray(values, elementFlag);
                }
                case KV3BinaryNodeType.UINT8:
                {
                    var bytes = ReadLaneSpan<byte>(ref lane.Bytes1, count);
                    var values = new uint[count];

                    TensorPrimitives.ConvertTruncating<byte, uint>(bytes, values);

                    return KVObject.PackedArray(values, elementFlag);
                }
                case KV3BinaryNodeType.INT16:
                    return KVObject.PackedArray(ReadLaneSpan<short>(ref lane.Bytes2, count).ToArray(), elementFlag);
                case KV3BinaryNodeType.UINT16:
                    return KVObject.PackedArray(ReadLaneSpan<ushort>(ref lane.Bytes2, count).ToArray(), elementFlag);
                case KV3BinaryNodeType.INT32:
                    return KVObject.PackedArray(ReadLaneSpan<int>(ref lane.Bytes4, count).ToArray(), elementFlag);
                case KV3BinaryNodeType.UINT32:
                    return KVObject.PackedArray(ReadLaneSpan<uint>(ref lane.Bytes4, count).ToArray(), elementFlag);
                case KV3BinaryNodeType.FLOAT:
                    return KVObject.PackedArray(ReadLaneSpan<float>(ref lane.Bytes4, count).ToArray(), elementFlag);
                case KV3BinaryNodeType.INT64:
                    return KVObject.PackedArray(ReadLaneSpan<long>(ref lane.Bytes8, count).ToArray(), elementFlag);
                case KV3BinaryNodeType.UINT64:
                    return KVObject.PackedArray(ReadLaneSpan<ulong>(ref lane.Bytes8, count).ToArray(), elementFlag);
                case KV3BinaryNodeType.DOUBLE:
                    return KVObject.PackedArray(ReadLaneSpan<double>(ref lane.Bytes8, count).ToArray(), elementFlag);
                default:
                    return null;
            }
        }

        private static KVObject ReadBinaryValue(Context context, KV3BinaryNodeType datatype, KVFlag flagInfo, Buffers lane)
        {
            var result = ReadValue(context, datatype, lane);

            if (flagInfo != KVFlag.None)
            {
                result.Flag = flagInfo;
            }

            return result;
        }

        // Primitive values are read from the given lane, which is the auxiliary buffer for elements of
        // ARRAY_TYPE_AUXILIARY_BUFFER. Strings, blobs and container lengths always come from the main buffer.
        private static KVObject ReadValue(Context context, KV3BinaryNodeType datatype, Buffers lane)
        {
            var buffer = context.Buffer;

            switch (datatype)
            {
                // Hardcoded values
                case KV3BinaryNodeType.NULL:
                    return KVObject.Null();
                case KV3BinaryNodeType.BOOLEAN_TRUE:
                    return true;
                case KV3BinaryNodeType.BOOLEAN_FALSE:
                    return false;
                case KV3BinaryNodeType.INT64_ZERO:
                    return 0L;
                case KV3BinaryNodeType.INT64_ONE:
                    return 1L;
                case KV3BinaryNodeType.DOUBLE_ZERO:
                    return 0.0D;
                case KV3BinaryNodeType.DOUBLE_ONE:
                    return 1.0D;
                case KV3BinaryNodeType.UINT32_ZERO:
                    return 0U;
                case KV3BinaryNodeType.UINT32_ONE:
                    return 1U;

                // 1 byte values
                case KV3BinaryNodeType.BOOLEAN:
                    return ReadLane<byte>(ref lane.Bytes1) != 0;
                case KV3BinaryNodeType.INT8:
                    Debug.Assert(context.Version >= 4);
                    return (int)ReadLane<sbyte>(ref lane.Bytes1);
                case KV3BinaryNodeType.UINT8:
                    Debug.Assert(context.Version >= 4);
                    return (uint)ReadLane<byte>(ref lane.Bytes1);

                // 2 byte values
                case KV3BinaryNodeType.INT16:
                    Debug.Assert(context.Version >= 4);
                    return ReadLane<short>(ref lane.Bytes2);
                case KV3BinaryNodeType.UINT16:
                    Debug.Assert(context.Version >= 4);
                    return ReadLane<ushort>(ref lane.Bytes2);

                // 4 byte values
                case KV3BinaryNodeType.INT32:
                    return ReadLane<int>(ref lane.Bytes4);
                case KV3BinaryNodeType.UINT32:
                    return ReadLane<uint>(ref lane.Bytes4);
                case KV3BinaryNodeType.FLOAT:
                    Debug.Assert(context.Version >= 4);
                    return ReadLane<float>(ref lane.Bytes4);

                // 8 byte values
                case KV3BinaryNodeType.INT64:
                    return ReadLane<long>(ref lane.Bytes8);
                case KV3BinaryNodeType.UINT64:
                    return ReadLane<ulong>(ref lane.Bytes8);
                case KV3BinaryNodeType.DOUBLE:
                    return ReadLane<double>(ref lane.Bytes8);

                // Custom types
                case KV3BinaryNodeType.STRING:
                    return GetString(context, ReadLane<int>(ref buffer.Bytes4));
                case KV3BinaryNodeType.BINARY_BLOB when context.Version < 2:
                {
                    var blockLength = ReadLane<int>(ref buffer.Bytes4);
                    byte[] output;

                    if (blockLength > 0)
                    {
                        output = buffer.Bytes1.AsSpan(0, blockLength).ToArray();
                        buffer.Bytes1 = buffer.Bytes1[blockLength..];
                    }
                    else
                    {
                        output = [];
                    }

                    return output;
                }
                case KV3BinaryNodeType.BINARY_BLOB:
                    return context.BinaryBlobs[context.NextBinaryBlob++];
                case KV3BinaryNodeType.ARRAY:
                {
                    var arrayLength = ReadLane<int>(ref buffer.Bytes4);
                    var array = KVObject.Array(arrayLength);

                    for (var i = 0; i < arrayLength; i++)
                    {
                        ParseBinaryKV3(context, array);
                    }

                    return array;
                }
                case KV3BinaryNodeType.ARRAY_TYPED:
                case KV3BinaryNodeType.ARRAY_TYPE_BYTE_LENGTH:
                case KV3BinaryNodeType.ARRAY_TYPE_AUXILIARY_BUFFER:
                {
                    var arrayLength = datatype == KV3BinaryNodeType.ARRAY_TYPED
                        ? ReadLane<int>(ref buffer.Bytes4)
                        : ReadLane<byte>(ref buffer.Bytes1);

                    if (arrayLength == 0 && context.Version >= 2)
                    {
                        throw new InvalidDataException("Typed KV3 arrays can not be empty");
                    }

                    var (subType, subFlagInfo) = ReadType(context);
                    var elementLane = datatype == KV3BinaryNodeType.ARRAY_TYPE_AUXILIARY_BUFFER ? context.AuxiliaryBuffer : buffer;
                    var packedArray = ReadPackedArray(subType, subFlagInfo, elementLane, arrayLength);

                    if (packedArray != null)
                    {
                        return packedArray;
                    }

                    var typedArray = KVObject.Array(arrayLength);

                    for (var i = 0; i < arrayLength; i++)
                    {
                        typedArray.Add(ReadBinaryValue(context, subType, subFlagInfo, elementLane));
                    }

                    return typedArray;
                }

                case KV3BinaryNodeType.OBJECT:
                {
                    var objectLength = context.Version >= 5
                        ? ReadLane<int>(ref context.ObjectLengths)
                        : ReadLane<int>(ref buffer.Bytes4);
                    var newObject = objectLength <= MaxListBackedObjectLength
                        ? KVObject.ListCollection(objectLength)
                        : KVObject.Collection(objectLength);

                    for (var i = 0; i < objectLength; i++)
                    {
                        ParseBinaryKV3(context, newObject);
                    }

                    return newObject;
                }
                default:
                    throw new UnexpectedMagicException("Unknown KVType", (int)datatype, nameof(datatype));
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Converts the binary KV3 data to text format and writes it.
        /// </remarks>
        public override void WriteText(IndentedTextWriter writer)
        {
            Data.WriteKV3Text(writer);
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
            offset = MathUtils.AlignUp(offset, alignment);
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

                var kv3 = new BinaryKV3(BlockType.Undefined)
                {
                    Resource = null!
                };
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
