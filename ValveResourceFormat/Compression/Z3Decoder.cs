using System.Buffers.Binary;
using System.IO;
using System.IO.Hashing;
using System.Numerics.Tensors;
using System.Runtime.Intrinsics;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Compression
{
    /// <summary>
    /// Decoder for z3 compression frames.
    /// </summary>
    /// <remarks>
    /// <para>Frame: a byte, a varint content size, a varint window, blocks and an optional u32 checksum.
    /// The low nibble of the byte is 7, bit 4 is a dictionary id (rejected), bit 5 is the checksum flag and bits 6-7 select a preset table set.
    /// The window is present only when the content size is above 1 MiB. It must be at least 16 and bounds every new match offset.
    /// The checksum is the low 32 bits of XXH3-64 of the whole output, little endian.
    /// The frame must end exactly after the checksum. Varints are LEB128 limited to 64 bits.</para>
    /// <para>Block: a byte, then a varint size unless bit 7 is set. Bit 7 set takes all remaining output.
    /// Otherwise the size must be smaller than the remaining output. Sizes are 1 to 0x40000.
    /// Bit 6 clear is a raw block with the low 6 bits zero. Bit 6 set is followed by a varint payload length.
    /// Block byte bits: 0 delta literals, 1 literal contexts, 2-4 offset mode, 5 explicit section lengths.
    /// The payload is a varint token count, then these sections: tokens, offset codes, literal length codes, match length codes,
    /// offset low parts, offset extra bits, literal length extra bits, match length extra bits, literals.
    /// Matches, repeat offsets and entropy tables carry across blocks.</para>
    /// <para>Entropy section: table descriptions, a stream header, then interleaved Huffman streams.
    /// A section with no elements is empty and has no tables.
    /// With explicit section lengths every section except the literals starts with a varint byte length that must be consumed exactly.
    /// Extra bit sections have no tables and use the same stream header with raw bits.
    /// The literal section runs to the payload end.</para>
    /// </remarks>
    internal static partial class Z3Decoder
    {
        private const int MaxBlockSize = 0x40000;
        private const long MaxWindowlessSize = 0x100000;

        // Low offset bits per offset mode; mode 5 holds them in the low 2 bits of a 256 symbol offset code
        private static ReadOnlySpan<byte> ModeLowBits => [0, 2, 2, 3, 4, 2, 3, 4];

        private const int NewOffsetFlag = 1;
        private const int LiteralEscapeFlag = 2;
        private const int MatchEscapeFlag = 4;

        // Per token: flag bits, with the offset context min(r, 3) in the high nibble
        private static readonly byte[] TokenFlags = CreateTokenFlags();

        // Per token: literal length without an escape, 0 when escaped
        private static readonly byte[] TokenLiteralLength = CreateTokenLiteralLengths();

        // Per token: match length without an escape, 0 when escaped
        private static readonly byte[] TokenMatchLength = CreateTokenMatchLengths();

        // r = token % 12, q = token / 12: literal length is q & 3 (3 escapes), match length is r + 3 (11 escapes), q >= 16 reads a new offset
        private static byte[] CreateTokenFlags()
        {
            var flags = new byte[256];

            for (var token = 0; token < flags.Length; token++)
            {
                var r = token % 12;
                var q = token / 12;
                var value = Math.Min(r, 3) << 4;

                if (token >= 192)
                {
                    value |= NewOffsetFlag;
                }

                if ((q & 3) == 3)
                {
                    value |= LiteralEscapeFlag;
                }

                if (r == 11)
                {
                    value |= MatchEscapeFlag;
                }

                flags[token] = (byte)value;
            }

            return flags;
        }

        private static byte[] CreateTokenLiteralLengths()
        {
            var lengths = new byte[256];

            for (var token = 0; token < lengths.Length; token++)
            {
                var c = (token / 12) & 3;
                lengths[token] = c == 3 ? (byte)0 : (byte)c;
            }

            return lengths;
        }

        private static byte[] CreateTokenMatchLengths()
        {
            var lengths = new byte[256];

            for (var token = 0; token < lengths.Length; token++)
            {
                var r = token % 12;
                lengths[token] = r == 11 ? (byte)0 : (byte)(r + 3);
            }

            return lengths;
        }

        /// <summary>
        /// Decompresses exactly one z3 frame.
        /// </summary>
        /// <param name="input">The complete frame.</param>
        /// <param name="output">Destination, at least as long as the content size stored in the frame. Only that many bytes are written.</param>
        /// <returns>The content size of the frame.</returns>
        /// <exception cref="InvalidDataException">The frame is malformed or its content does not fit the output.</exception>
        public static int Decompress(ReadOnlySpan<byte> input, Span<byte> output)
        {
            if (input.IsEmpty)
            {
                throw new InvalidDataException("z3 frame is empty.");
            }

            var frameByte = input[0];

            if ((frameByte & 0x0F) != 7)
            {
                throw new InvalidDataException("z3 frame has a bad magic nibble.");
            }

            if ((frameByte & 0x10) != 0)
            {
                throw new InvalidDataException("z3 frames with a dictionary are not supported.");
            }

            var position = 1;
            var contentSize = ReadVarint(input, ref position, input.Length);

            if (contentSize > (ulong)output.Length)
            {
                throw new InvalidDataException("z3 content size is larger than the output.");
            }

            output = output[..(int)contentSize];

            var windowLimit = ulong.MaxValue;

            if (contentSize > MaxWindowlessSize)
            {
                windowLimit = ReadVarint(input, ref position, input.Length);

                if (windowLimit < 16)
                {
                    throw new InvalidDataException("z3 window is too small.");
                }
            }

            var maxBlockSize = (int)Math.Min(contentSize, MaxBlockSize);
            var tokenCapacity = maxBlockSize / 3;

            using var bytes = new RentedBuffer<byte>((6 * tokenCapacity) + (2 * maxBlockSize));
            using var longs = new RentedBuffer<long>(3 * tokenCapacity);
            using var lookups = new RentedBuffer<ushort>(TotalSlots * LookupSize);

            var hasChecksum = (frameByte & 0x20) != 0;
            var frame = new Frame(input, frameByte >> 6, windowLimit, maxBlockSize, bytes.Span, longs.Span, lookups.Span);
            frame.Decode(position, hasChecksum, output);

            return output.Length;
        }

        private static ulong ReadVarint(ReadOnlySpan<byte> data, ref int position, int limit)
        {
            var value = 0UL;
            var shift = 0;

            while (true)
            {
                if (position >= limit)
                {
                    throw new InvalidDataException("z3 varint is truncated.");
                }

                var b = data[position++];

                if (shift == 63 && b > 1)
                {
                    throw new InvalidDataException("z3 varint overflows 64 bits.");
                }

                value |= (ulong)(b & 0x7F) << shift;

                if (b < 0x80)
                {
                    return value;
                }

                shift += 7;
            }
        }

        private ref partial struct Frame
        {
            private readonly ReadOnlySpan<byte> Data;
            private readonly ulong WindowLimit;
            private readonly Span<ushort> Lookups;
            private readonly int Preset;

            private long Rep0;
            private long Rep1;
            private long Rep2;
            private long Rep3;

            // One bit per global table slot
            private int PresetSlots;
            private int DefinedSlots;

            private readonly Span<byte> Tokens;
            private readonly Span<byte> OffsetCodes;
            private readonly Span<byte> LiteralLengthCodes;
            private readonly Span<byte> MatchLengthCodes;
            private readonly Span<byte> LowParts;
            private readonly Span<byte> Packed;
            private readonly Span<byte> Contexts;
            private readonly Span<byte> Literals;
            private readonly Span<long> Offsets;
            private readonly Span<long> LiteralLengths;
            private readonly Span<long> MatchLengths;

            public Frame(ReadOnlySpan<byte> input, int preset, ulong windowLimit, int maxBlockSize, Span<byte> bytes, Span<long> longs, Span<ushort> lookups)
            {
                Data = input;
                WindowLimit = windowLimit;
                Preset = preset;
                Lookups = lookups;
                PresetSlots = PresetSlotMasks[preset];

                Rep0 = 1;
                Rep1 = 4;
                Rep2 = 8;
                Rep3 = 16;

                var tokenCapacity = maxBlockSize / 3;
                var blockCapacity = maxBlockSize;

                Tokens = bytes.Slice(0, tokenCapacity);
                OffsetCodes = bytes.Slice(tokenCapacity, tokenCapacity);
                LiteralLengthCodes = bytes.Slice(2 * tokenCapacity, tokenCapacity);
                MatchLengthCodes = bytes.Slice(3 * tokenCapacity, tokenCapacity);
                LowParts = bytes.Slice(4 * tokenCapacity, tokenCapacity);
                Packed = bytes.Slice(5 * tokenCapacity, tokenCapacity);
                Contexts = bytes.Slice(6 * tokenCapacity, blockCapacity);
                Literals = bytes.Slice((6 * tokenCapacity) + blockCapacity, blockCapacity);

                Offsets = longs.Slice(0, tokenCapacity);
                LiteralLengths = longs.Slice(tokenCapacity, tokenCapacity);
                MatchLengths = longs.Slice(2 * tokenCapacity, tokenCapacity);
            }

            public void Decode(int position, bool hasChecksum, Span<byte> output)
            {
                var outputPosition = 0;

                while (outputPosition < output.Length)
                {
                    var remaining = output.Length - outputPosition;

                    if (position >= Data.Length)
                    {
                        throw new InvalidDataException("z3 block header is truncated.");
                    }

                    var blockByte = Data[position++];
                    var blockSize = remaining;

                    if ((blockByte & 0x80) == 0)
                    {
                        var explicitSize = ReadVarint(Data, ref position, Data.Length);

                        if (explicitSize >= (ulong)remaining)
                        {
                            throw new InvalidDataException("z3 block size is not smaller than the remaining output.");
                        }

                        blockSize = (int)explicitSize;
                    }

                    if (blockSize is < 1 or > MaxBlockSize)
                    {
                        throw new InvalidDataException("z3 block size is out of range.");
                    }

                    if ((blockByte & 0x40) == 0)
                    {
                        if ((blockByte & 0x3F) != 0)
                        {
                            throw new InvalidDataException("z3 raw block has bits set.");
                        }

                        if (Data.Length - position < blockSize)
                        {
                            throw new InvalidDataException("z3 raw block is truncated.");
                        }

                        Data.Slice(position, blockSize).CopyTo(output[outputPosition..]);
                        position += blockSize;
                    }
                    else
                    {
                        var payloadLength = ReadVarint(Data, ref position, Data.Length);

                        if (payloadLength > (ulong)(Data.Length - position))
                        {
                            throw new InvalidDataException("z3 block payload is truncated.");
                        }

                        if (payloadLength > (ulong)(8L * blockSize) + 0x4000)
                        {
                            throw new InvalidDataException("z3 block payload is too large.");
                        }

                        DecodeBlock(position, position + (int)payloadLength, blockSize, blockByte, output, outputPosition);
                        position += (int)payloadLength;
                    }

                    outputPosition += blockSize;
                }

                if (hasChecksum)
                {
                    if (Data.Length - position < 4)
                    {
                        throw new InvalidDataException("z3 checksum is truncated.");
                    }

                    var stored = BinaryPrimitives.ReadUInt32LittleEndian(Data[position..]);
                    position += 4;

                    var computed = (uint)XxHash3.HashToUInt64(output);

                    if (computed != stored)
                    {
                        throw new InvalidDataException("z3 checksum mismatch.");
                    }
                }

                if (position != Data.Length)
                {
                    throw new InvalidDataException("z3 frame has trailing data.");
                }
            }

            private void GetLengths(byte token, ref int literalIndex, ref int matchIndex, out long literalLength, out long matchLength)
            {
                var flags = TokenFlags[token];

                literalLength = TokenLiteralLength[token];
                matchLength = TokenMatchLength[token];

                if ((flags & LiteralEscapeFlag) != 0)
                {
                    literalLength = LiteralLengths[literalIndex++];
                }

                if ((flags & MatchEscapeFlag) != 0)
                {
                    matchLength = MatchLengths[matchIndex++];
                }
            }

            private void DecodeBlock(int start, int end, int blockSize, int blockByte, Span<byte> output, int outputStart)
            {
                var hasLengths = (blockByte & 0x20) != 0;
                var literalContexts = (blockByte & 2) != 0;
                var delta = (blockByte & 1) != 0;
                var mode = (blockByte >> 2) & 7;
                var lowBits = ModeLowBits[mode];
                var packedLow = mode is 1 or 6 or 7;

                var position = start;
                var tokenCountValue = ReadVarint(Data, ref position, end);

                if (tokenCountValue > (ulong)(blockSize / 3))
                {
                    throw new InvalidDataException("z3 token count is too large.");
                }

                var tokenCount = (int)tokenCountValue;
                var cursor = new SectionCursor(position, end, hasLengths);

                EntropySection(ref cursor, TokenGroup, tokenCount, 1, Tokens, default);

                var offsetCount = 0;
                var literalEscapeCount = 0;
                var matchEscapeCount = 0;
                var totalLiteralLength = 0L;
                var totalMatchLength = 0L;

                for (var i = 0; i < tokenCount; i++)
                {
                    var token = Tokens[i];
                    var flags = TokenFlags[token];

                    Contexts[offsetCount] = (byte)(flags >> 4);
                    offsetCount += flags & NewOffsetFlag;
                    literalEscapeCount += (flags & LiteralEscapeFlag) >> 1;
                    matchEscapeCount += (flags & MatchEscapeFlag) >> 2;
                    totalLiteralLength += TokenLiteralLength[token];
                    totalMatchLength += TokenMatchLength[token];
                }

                var wideOffsetCodes = mode == 5;
                var offsetGroup = wideOffsetCodes ? WideOffsetGroup : OffsetGroup;
                var maxOffsetCode = EntropySectionWithMax(ref cursor, offsetGroup, offsetCount, 4, OffsetCodes, Contexts);
                var maxLiteralCode = EntropySectionWithMax(ref cursor, LiteralLengthGroup, literalEscapeCount, 1, LiteralLengthCodes, default);

                var matchContextCount = 0;

                for (var i = 0; i < tokenCount; i++)
                {
                    var flags = TokenFlags[Tokens[i]];

                    Contexts[matchContextCount] = (byte)(flags & NewOffsetFlag);
                    matchContextCount += (flags & MatchEscapeFlag) >> 2;
                }

                var maxMatchCode = EntropySectionWithMax(ref cursor, MatchLengthGroup, matchEscapeCount, 2, MatchLengthCodes, Contexts);

                var offsetCodes = OffsetCodes[..offsetCount];

                if (lowBits != 0)
                {
                    if (wideOffsetCodes)
                    {
                        for (var i = 0; i < offsetCount; i++)
                        {
                            var code = offsetCodes[i];
                            LowParts[i] = (byte)(code & 3);
                            offsetCodes[i] = (byte)(code >> 2);
                        }

                        maxOffsetCode >>= 2;
                    }
                    else
                    {
                        ReadLowParts(ref cursor, offsetCount, lowBits, packedLow);
                    }
                }

                ExtraBitsSection(ref cursor, offsetCodes, OffsetCodeTables[lowBits], maxOffsetCode, Offsets);

                if (lowBits != 0)
                {
                    for (var i = 0; i < offsetCount; i++)
                    {
                        Offsets[i] = (Offsets[i] << lowBits) | LowParts[i];
                    }
                }

                ExtraBitsSection(ref cursor, LiteralLengthCodes[..literalEscapeCount], LiteralLengthCode, maxLiteralCode, LiteralLengths);
                ExtraBitsSection(ref cursor, MatchLengthCodes[..matchEscapeCount], MatchLengthCode, maxMatchCode, MatchLengths);

                totalLiteralLength += TensorPrimitives.Sum<long>(LiteralLengths[..literalEscapeCount]);
                totalMatchLength += TensorPrimitives.Sum<long>(MatchLengths[..matchEscapeCount]);

                if (totalMatchLength > blockSize || totalLiteralLength > blockSize - totalMatchLength)
                {
                    throw new InvalidDataException("z3 token lengths exceed the block size.");
                }

                var literalCount = (int)(blockSize - totalMatchLength);

                if (literalContexts)
                {
                    BuildLiteralContexts(tokenCount, literalCount, outputStart & 3);
                }

                var literalStart = cursor.Position;
                var literalAvailable = end - literalStart;
                var literalTables = literalContexts ? 4 : 1;
                var literalContextSpan = literalContexts ? Contexts[..literalCount] : default;
                var consumed = DecodeSection(literalStart, literalAvailable, true, literalCount, literalTables, LiteralGroup, Literals, literalContextSpan);

                if (consumed != literalAvailable)
                {
                    throw new InvalidDataException("z3 literal section does not end at the payload end.");
                }

                Execute(tokenCount, literalCount, delta, output, outputStart);
            }

            // Literal context is the output position & 3 of each literal byte
            private void BuildLiteralContexts(int tokenCount, int literalCount, int phase)
            {
                var literalIndex = 0;
                var matchIndex = 0;
                var contextIndex = 0;

                for (var i = 0; i < tokenCount; i++)
                {
                    GetLengths(Tokens[i], ref literalIndex, ref matchIndex, out var literalLength, out var matchLength);

                    for (var j = 0; j < literalLength; j++)
                    {
                        Contexts[contextIndex++] = (byte)((phase + j) & 3);
                    }

                    phase = (int)((phase + literalLength + matchLength) & 3);
                }

                var trailing = Contexts[contextIndex..literalCount];
                var seed = Math.Min(trailing.Length, 4);

                for (var i = 0; i < seed; i++)
                {
                    trailing[i] = (byte)((phase + i) & 3);
                }

                var filled = seed;

                while (filled < trailing.Length)
                {
                    var chunk = Math.Min(filled, trailing.Length - filled);
                    trailing[..chunk].CopyTo(trailing[filled..]);
                    filled += chunk;
                }
            }

            // Packed modes store 4 (2 low bits) or 2 (3 or 4 low bits) low parts per byte, lane 0 in the low bits
            private void ReadLowParts(ref SectionCursor cursor, int offsetCount, int lowBits, bool packedLow)
            {
                if (!packedLow)
                {
                    var maxLow = EntropySectionWithMax(ref cursor, LowPartGroup, offsetCount, 1, LowParts, default);

                    if (maxLow >= (1 << lowBits))
                    {
                        throw new InvalidDataException("z3 offset low part is out of range.");
                    }

                    return;
                }

                var laneShift = lowBits == 2 ? 2 : 1;
                var lanes = 1 << laneShift;
                var packedCount = (offsetCount + lanes - 1) >> laneShift;
                var maxPacked = EntropySectionWithMax(ref cursor, PackedGroup, packedCount, 1, Packed, default);

                if (maxPacked >= (1 << (lowBits * lanes)))
                {
                    throw new InvalidDataException("z3 packed offset low parts are out of range.");
                }

                var mask = (1 << lowBits) - 1;
                var index = 0;

                for (var i = 0; i < packedCount; i++)
                {
                    var packed = Packed[i];

                    for (var lane = 0; lane < lanes && index < offsetCount; lane++)
                    {
                        LowParts[index++] = (byte)(packed & mask);
                        packed >>= lowBits;
                    }
                }
            }

            private void Execute(int tokenCount, int literalCount, bool delta, Span<byte> output, int outputStart)
            {
                var outputPosition = outputStart;
                var literalIndex = 0;
                var offsetIndex = 0;
                var literalLengthIndex = 0;
                var matchLengthIndex = 0;

                for (var i = 0; i < tokenCount; i++)
                {
                    var token = Tokens[i];

                    GetLengths(token, ref literalLengthIndex, ref matchLengthIndex, out var literalLength, out var matchLength);

                    WriteLiterals((int)literalLength, delta, output, ref outputPosition, ref literalIndex);

                    if ((TokenFlags[token] & NewOffsetFlag) != 0)
                    {
                        var newOffset = Offsets[offsetIndex++];

                        if (newOffset == 0)
                        {
                            throw new InvalidDataException("z3 offset is zero.");
                        }

                        if ((ulong)newOffset > WindowLimit)
                        {
                            throw new InvalidDataException("z3 offset is beyond the window.");
                        }

                        (Rep3, Rep2, Rep1, Rep0) = (Rep2, Rep1, Rep0, newOffset);
                    }
                    else
                    {
                        switch (token / 48)
                        {
                            case 1:
                                (Rep0, Rep1) = (Rep1, Rep0);
                                break;
                            case 2:
                                (Rep0, Rep1, Rep2) = (Rep2, Rep0, Rep1);
                                break;
                            case 3:
                                (Rep0, Rep1, Rep2, Rep3) = (Rep3, Rep0, Rep1, Rep2);
                                break;
                        }
                    }

                    var offset = Rep0;

                    if (offset > outputPosition)
                    {
                        throw new InvalidDataException("z3 match offset is out of range.");
                    }

                    var length = (int)matchLength;
                    var distance = (int)offset;

                    if (distance >= 16 && length <= 16 && outputPosition + 16 <= output.Length)
                    {
                        Vector128.Create<byte>(output[(outputPosition - distance)..]).CopyTo(output[outputPosition..]);
                    }
                    else if (distance >= length)
                    {
                        output.Slice(outputPosition - distance, length).CopyTo(output.Slice(outputPosition, length));
                    }
                    else
                    {
                        for (var j = 0; j < length; j++)
                        {
                            output[outputPosition + j] = output[outputPosition + j - distance];
                        }
                    }

                    outputPosition += length;
                }

                var trailing = literalCount - literalIndex;
                WriteLiterals(trailing, delta, output, ref outputPosition, ref literalIndex);
            }

            // Delta adds the byte rep0 behind the position, which is zero before the frame start
            private void WriteLiterals(int length, bool delta, Span<byte> output, ref int outputPosition, ref int literalIndex)
            {
                var position = outputPosition;

                if (!delta)
                {
                    if (length <= 16 && literalIndex + 16 <= Literals.Length && position + 16 <= output.Length)
                    {
                        Vector128.Create<byte>(Literals[literalIndex..]).CopyTo(output[position..]);
                    }
                    else
                    {
                        Literals.Slice(literalIndex, length).CopyTo(output[position..]);
                    }
                }
                else
                {
                    var rep0 = Rep0;

                    for (var j = 0; j < length; j++)
                    {
                        var source = position + j - rep0;
                        var previous = source >= 0 ? output[(int)source] : (byte)0;
                        output[position + j] = (byte)(Literals[literalIndex + j] + previous);
                    }
                }

                literalIndex += length;
                outputPosition += length;
            }
        }
    }
}
