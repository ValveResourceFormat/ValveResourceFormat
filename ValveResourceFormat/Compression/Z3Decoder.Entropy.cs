using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Numerics.Tensors;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace ValveResourceFormat.Compression
{
    internal static partial class Z3Decoder
    {
        private const int MaxCodeLength = 11;
        private const int LookupSize = 1 << MaxCodeLength;
        private const int PresetTableSize = 256;
        private const int PresetTablesPerSet = 17;

        private const int TokenGroup = 0;
        private const int OffsetGroup = 1;
        private const int LiteralLengthGroup = 2;
        private const int MatchLengthGroup = 3;
        private const int LiteralGroup = 4;
        private const int LowPartGroup = 5;
        private const int WideOffsetGroup = 6;
        private const int PackedGroup = 7;

        // First global table slot of each group, then the total slot count
        private static ReadOnlySpan<byte> GroupFirstSlot => [0, 1, 5, 6, 8, 12, 13, 17, 18];
        private static ReadOnlySpan<short> GroupAlphabet => [256, 64, 72, 72, 256, 16, 256, 256];

        private static int TotalSlots => GroupFirstSlot[PackedGroup + 1];

        private static int GroupSlotCount(int group) => GroupFirstSlot[group + 1] - GroupFirstSlot[group];

        private static readonly byte[] PresetData = LoadPresets();
        private static readonly int[] PresetSlotMasks = CreatePresetSlotMasks();
        private static readonly ushort[]?[] PresetLookups = new ushort[4 * PresetTablesPerSet][];

        // Offset code tables indexed by low bit count
        private static readonly ExtraCode[] OffsetCodeTables =
        [
            CreateOffsetCode(0),
            CreateOffsetCode(1),
            CreateOffsetCode(2),
            CreateOffsetCode(3),
            CreateOffsetCode(4),
        ];

        private static readonly ExtraCode LiteralLengthCode = CreateLengthCode(3);
        private static readonly ExtraCode MatchLengthCode = CreateLengthCode(14);

        private static byte[] LoadPresets()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ValveResourceFormat.Utils.z3_presets.bin");
            Debug.Assert(stream != null);

            var data = new byte[4 * PresetTablesPerSet * PresetTableSize];
            stream.ReadExactly(data);

            return data;
        }

        private static ReadOnlySpan<byte> PresetLengths(int preset, int group, int slot)
        {
            var table = GroupFirstSlot[group] + slot;
            var offset = ((preset * PresetTablesPerSet) + table) * PresetTableSize;
            return PresetData.AsSpan(offset, GroupAlphabet[group]);
        }

        // Bit per global slot whose preset table is not empty
        private static int[] CreatePresetSlotMasks()
        {
            var masks = new int[4];

            for (var preset = 0; preset < masks.Length; preset++)
            {
                for (var group = 0; group < PackedGroup; group++)
                {
                    for (var slot = 0; slot < GroupSlotCount(group); slot++)
                    {
                        if (PresetLengths(preset, group, slot).ContainsAnyExcept((byte)0))
                        {
                            masks[preset] |= 1 << (GroupFirstSlot[group] + slot);
                        }
                    }
                }
            }

            return masks;
        }

        private static ushort[] GetPresetLookup(int preset, int group, int slot)
        {
            var index = (preset * PresetTablesPerSet) + GroupFirstSlot[group] + slot;
            var lookup = Volatile.Read(ref PresetLookups[index]);

            if (lookup is null)
            {
                lookup = new ushort[LookupSize];
                BuildLookup(PresetLengths(preset, group, slot), lookup, MaxCodeLength);
                Volatile.Write(ref PresetLookups[index], lookup);
            }

            return lookup;
        }

        private sealed class ExtraCode(byte[] bits, long[] values)
        {
            public readonly byte[] Bits = bits;
            public readonly long[] Values = values;
        }

        // Below 2 the base is 1 << index with index extra bits, else ((index & 1) + 2) << (index / 2) with index / 2 extra bits
        private static long OffsetClass(int index, out int bits)
        {
            if (index < 2)
            {
                bits = index;
                return 1L << index;
            }

            bits = index >> 1;
            return ((index & 1) + 2L) << bits;
        }

        // Below 16 the base is index, else ((index & 1) + 2) << bits with bits = ((index - 16) / 2) + 3
        private static long LengthClass(int index, out int bits)
        {
            if (index < 16)
            {
                bits = 0;
                return index;
            }

            bits = ((index - 16) >> 1) + 3;
            return ((index & 1) + 2L) << bits;
        }

        private static ExtraCode CreateOffsetCode(int lowBits)
        {
            var bits = new byte[64];
            var values = new long[64];

            for (var i = 0; i < 64; i++)
            {
                var classBase = OffsetClass(i, out var classBits);
                bits[i] = (byte)Math.Max(0, classBits - lowBits);
                values[i] = classBase >> lowBits;
            }

            return new ExtraCode(bits, values);
        }

        private static ExtraCode CreateLengthCode(int minimum)
        {
            var bits = new byte[72];
            var values = new long[72];

            for (var i = 0; i < 72; i++)
            {
                var classBase = LengthClass(i, out var classBits);
                bits[i] = (byte)classBits;
                values[i] = classBase + minimum;
            }

            return new ExtraCode(bits, values);
        }

        private static int ReverseBits(int value, int count)
        {
            var result = 0;

            for (var i = 0; i < count; i++)
            {
                result = (result << 1) | (value & 1);
                value >>= 1;
            }

            return result;
        }

        private static bool IsCompleteCode(ReadOnlySpan<byte> lengths)
        {
            var sum = 0;

            foreach (var length in lengths)
            {
                if (length == 0)
                {
                    continue;
                }

                if (length > MaxCodeLength)
                {
                    return false;
                }

                sum += LookupSize >> length;
            }

            return sum == LookupSize;
        }

        // Entry is symbol << 4 | code length, indexed by the next maxLength stream bits; an unwritten entry is 0
        private static void BuildLookup(ReadOnlySpan<byte> lengths, Span<ushort> lookup, int maxLength)
        {
            var lookupSize = 1 << maxLength;
            Span<int> nextCode = stackalloc int[maxLength + 2];

            foreach (var length in lengths)
            {
                nextCode[length + 1]++;
            }

            nextCode[1] = 0;

            var code = 0;

            for (var length = 1; length <= maxLength; length++)
            {
                code = (code + nextCode[length]) << 1;
                nextCode[length] = code;
            }

            for (var symbol = 0; symbol < lengths.Length; symbol++)
            {
                var length = lengths[symbol];

                if (length == 0)
                {
                    continue;
                }

                var entry = (ushort)((symbol << 4) | length);
                var reversed = ReverseBits(nextCode[length]++, length);

                for (var index = reversed; index < lookupSize; index += 1 << length)
                {
                    lookup[index] = entry;
                }
            }
        }

        private struct SectionCursor(int position, int end, bool hasLengths)
        {
            public int Position = position;
            public readonly int End = end;
            public readonly bool HasLengths = hasLengths;

            public void Next(ReadOnlySpan<byte> data, out int start, out int available)
            {
                available = End - Position;

                if (HasLengths)
                {
                    var length = ReadVarint(data, ref Position, End);

                    if (length > (ulong)(End - Position))
                    {
                        throw new InvalidDataException("z3 section length is past the payload end.");
                    }

                    available = (int)length;
                }

                start = Position;
            }

            public void Done(int consumed, int available)
            {
                if (HasLengths && consumed != available)
                {
                    throw new InvalidDataException("z3 section length does not match its content.");
                }

                Position += consumed;
            }
        }

        private ref partial struct Frame
        {
            private readonly uint PeekBits(long bitPosition, int count)
            {
                var index = bitPosition >> 3;

                if (index >= Data.Length)
                {
                    return 0;
                }

                var word = index <= Data.Length - 8
                    ? BinaryPrimitives.ReadUInt64LittleEndian(Data[(int)index..])
                    : ReadTailWord((int)index);

                return (uint)((word >> (int)(bitPosition & 7)) & ((1UL << count) - 1));
            }

            // Bytes past the end of the input read as zero
            private readonly ulong ReadTailWord(int index)
            {
                var tail = Data[index..];
                var word = 0UL;

                for (var i = 0; i < tail.Length; i++)
                {
                    word |= (ulong)tail[i] << (8 * i);
                }

                return word;
            }

            // Rounds where every stream can read 8 bytes inside Data
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private readonly int FastRounds(scoped ReadOnlySpan<long> cursors, int maxStepBits, int count)
            {
                var highestBit = 0L;

                foreach (var cursor in cursors)
                {
                    highestBit = Math.Max(highestBit, cursor);
                }

                var spareBits = (8L * (Data.Length - 8)) + 7 - highestBit;

                if (spareBits < 0)
                {
                    return 0;
                }

                return (int)Math.Min((spareBits / maxStepBits) + 1, count / cursors.Length);
            }

            private readonly ulong ReadWordUnchecked(long bitPosition)
            {
                ref var start = ref MemoryMarshal.GetReference(Data);
                var word = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref start, (nint)(bitPosition >> 3)));
                return BitConverter.IsLittleEndian ? word : BinaryPrimitives.ReverseEndianness(word);
            }

            private uint ReadBits(ref long bitPosition, int count)
            {
                var value = PeekBits(bitPosition, count);
                bitPosition += count;
                return value;
            }

            private void ParseSparse(ref long bitPosition, int alphabet, scoped Span<byte> lengths)
            {
                Span<bool> present = stackalloc bool[256];

                var last = 0;

                if (alphabet != 256)
                {
                    last = (int)ReadBits(ref bitPosition, 8);

                    if (last >= alphabet)
                    {
                        throw new InvalidDataException("z3 sparse table is larger than its alphabet.");
                    }
                }

                var baseLength = (int)ReadBits(ref bitPosition, 4);
                var widthBits = (int)ReadBits(ref bitPosition, 3);

                if (widthBits > 4)
                {
                    throw new InvalidDataException("z3 sparse table width is too large.");
                }

                if (alphabet == 256)
                {
                    var groups = ReadBits(ref bitPosition, 32);
                    Span<byte> masks = stackalloc byte[32];

                    for (var g = 0; g < 32; g++)
                    {
                        if (((groups >> g) & 1) != 0)
                        {
                            masks[g] = (byte)ReadBits(ref bitPosition, 8);
                        }
                    }

                    for (var g = 0; g < 32; g++)
                    {
                        for (var j = 0; j < 8; j++)
                        {
                            present[(8 * g) + j] = ((masks[g] >> j) & 1) != 0;
                        }
                    }
                }
                else
                {
                    for (var i = 0; i <= last; i += 32)
                    {
                        var chunk = Math.Min(32, last + 1 - i);
                        var bits = ReadBits(ref bitPosition, chunk);

                        for (var j = 0; j < chunk; j++)
                        {
                            present[i + j] = ((bits >> j) & 1) != 0;
                        }
                    }
                }

                for (var s = 0; s < alphabet; s++)
                {
                    if (present[s])
                    {
                        lengths[s] = (byte)(baseLength + ReadBits(ref bitPosition, widthBits));
                    }
                }
            }

            private void ParseRunLength(ref long bitPosition, int alphabet, scoped Span<byte> lengths)
            {
                var last = (int)ReadBits(ref bitPosition, 8);

                if (last >= alphabet)
                {
                    throw new InvalidDataException("z3 run length table is larger than its alphabet.");
                }

                Span<byte> codeLengths = stackalloc byte[18];

                for (var i = 0; i < codeLengths.Length; i++)
                {
                    codeLengths[i] = (byte)ReadBits(ref bitPosition, 3);
                }

                const int MaxRunLengthCodeLength = 7;
                Span<ushort> lookup = stackalloc ushort[1 << MaxRunLengthCodeLength];
                BuildLookup(codeLengths, lookup, MaxRunLengthCodeLength);

                var lengthIndex = 0;

                while (lengthIndex <= last)
                {
                    var entry = lookup[(int)PeekBits(bitPosition, MaxRunLengthCodeLength)];

                    bitPosition += entry & 15;

                    var symbol = entry >> 4;

                    if (symbol < 16)
                    {
                        lengths[lengthIndex++] = (byte)symbol;
                    }
                    else if (symbol == 16)
                    {
                        lengthIndex += 3 + (int)ReadBits(ref bitPosition, 3);
                    }
                    else
                    {
                        lengthIndex += 11 + (int)ReadBits(ref bitPosition, 7);
                    }
                }
            }

            private Span<ushort> GetLookup(int firstSlot, int slotCount)
            {
                return Lookups.Slice(firstSlot * LookupSize, slotCount * LookupSize);
            }

            // Table mode: 0 explicit lengths, 1 single symbol, 2 preset or previous table, 3 flat 8 bit codes.
            // Returns true when the only table is the preset itself, which is not copied into Lookups.
            private bool ParseTables(ref long bitPosition, int group, int tableCount)
            {
                var alphabet = (int)GroupAlphabet[group];
                var singleBits = BitOperations.Log2((uint)(alphabet - 1)) + 1;
                var usesPreset = false;
                Span<byte> lengthStorage = stackalloc byte[256];

                for (var slot = 0; slot < tableCount; slot++)
                {
                    var globalSlot = GroupFirstSlot[group] + slot;
                    var slotBit = 1 << globalSlot;
                    var mode = (int)ReadBits(ref bitPosition, 2);

                    if (mode != 2)
                    {
                        PresetSlots &= ~slotBit;
                    }

                    switch (mode)
                    {
                        case 0:
                            var lengths = lengthStorage[..alphabet];
                            lengths.Clear();

                            var sparse = ReadBits(ref bitPosition, 1) != 0;

                            if (sparse)
                            {
                                ParseSparse(ref bitPosition, alphabet, lengths);
                            }
                            else
                            {
                                ParseRunLength(ref bitPosition, alphabet, lengths);
                            }

                            if (!IsCompleteCode(lengths))
                            {
                                throw new InvalidDataException("z3 huffman table is not a complete code.");
                            }

                            BuildLookup(lengths, GetLookup(globalSlot, 1), MaxCodeLength);
                            break;
                        case 1:
                            var symbol = (int)ReadBits(ref bitPosition, singleBits);

                            if (symbol >= alphabet)
                            {
                                throw new InvalidDataException("z3 single symbol is outside the alphabet.");
                            }

                            GetLookup(globalSlot, 1).Fill((ushort)(symbol << 4));
                            break;
                        case 2:
                            if ((PresetSlots & slotBit) != 0)
                            {
                                if (tableCount == 1)
                                {
                                    usesPreset = true;
                                }
                                else
                                {
                                    GetPresetLookup(Preset, group, slot).CopyTo(GetLookup(globalSlot, 1));
                                }
                            }
                            else if ((DefinedSlots & slotBit) == 0)
                            {
                                throw new InvalidDataException("z3 table is reused before it was defined.");
                            }

                            break;
                        case 3:
                            if (alphabet != 256)
                            {
                                throw new InvalidDataException("z3 flat table needs a 256 symbol alphabet.");
                            }

                            var flat = lengthStorage[..256];
                            flat.Fill(8);
                            BuildLookup(flat, GetLookup(globalSlot, 1), MaxCodeLength);
                            break;
                    }

                    DefinedSlots |= slotBit;
                }

                return usesPreset;
            }

            // Stream count, a delta width, a total length unless section lengths are explicit, then zigzag stream offsets relative to the average.
            // Fills the stream start bits and the stream end offsets; total is 0 for a single stream without section lengths.
            private int ParseStreamHeader(ref long bitPosition, int sectionByte, int minStreams, int maxStreams, int elementCount, int budget, bool hasLengths, scoped Span<long> cursors, scoped Span<int> offsets, out int total)
            {
                var sectionBit = 8L * sectionByte;
                var countBits = BitOperations.Log2((uint)(maxStreams - 1)) + 1;
                var streams = (int)ReadBits(ref bitPosition, countBits) + 1;

                if (streams < minStreams || streams > maxStreams || streams > elementCount)
                {
                    throw new InvalidDataException("z3 stream count is out of range.");
                }

                if (streams == 1)
                {
                    if (bitPosition - sectionBit > 8L * budget)
                    {
                        throw new InvalidDataException("z3 stream header overruns its section.");
                    }

                    cursors[0] = bitPosition;
                    offsets[1] = budget;
                    total = hasLengths ? budget : 0;
                    return 1;
                }

                var width = (int)ReadBits(ref bitPosition, 4);

                if (width == 15)
                {
                    width = 24;
                }

                var totalLength = 0L;

                if (!hasLengths)
                {
                    var shift = 0;
                    int part;

                    do
                    {
                        if (shift == 28)
                        {
                            throw new InvalidDataException("z3 stream total length is too long.");
                        }

                        part = (int)ReadBits(ref bitPosition, 8);
                        totalLength |= (long)(part & 0x7F) << shift;
                        shift += 7;
                    }
                    while (part >= 0x80);
                }

                Span<int> deltas = stackalloc int[10];

                for (var i = 0; i < streams - 1; i++)
                {
                    deltas[i] = (int)ReadBits(ref bitPosition, width);
                }

                var headerEnd = bitPosition - sectionBit;

                if (headerEnd > 8L * budget)
                {
                    throw new InvalidDataException("z3 stream header overruns its section.");
                }

                var headerBytes = (int)((headerEnd + 7) >> 3);

                if (!hasLengths)
                {
                    totalLength += headerBytes;

                    if (totalLength > budget)
                    {
                        throw new InvalidDataException("z3 stream total length is past the section end.");
                    }

                    total = (int)totalLength;
                }
                else
                {
                    total = budget;
                }

                var padding = (int)(headerEnd & 7);

                if (padding != 0 && PeekBits(bitPosition, 8 - padding) != 0)
                {
                    throw new InvalidDataException("z3 stream header padding is not zero.");
                }

                offsets[0] = headerBytes;
                var average = (total - headerBytes) / streams;
                var current = headerBytes;

                for (var i = 0; i < streams - 1; i++)
                {
                    var delta = deltas[i];
                    var zigzag = delta >> 1;

                    if ((delta & 1) != 0)
                    {
                        zigzag = -1 - zigzag;
                    }

                    current += zigzag + average;

                    if (current < offsets[i] || current > total)
                    {
                        throw new InvalidDataException("z3 stream offset is out of range.");
                    }

                    offsets[i + 1] = current;
                }

                offsets[streams] = total;

                for (var s = 0; s < streams; s++)
                {
                    cursors[s] = 8L * (sectionByte + offsets[s]);
                }

                return streams;
            }

            // Returns the section length in bytes
            private int CheckStreamEnds(scoped ReadOnlySpan<long> cursors, scoped ReadOnlySpan<int> offsets, int sectionByte, bool hasLengths, int total)
            {
                var skipEnd = cursors.Length == 1 && !hasLengths;

                for (var s = 0; s < cursors.Length; s++)
                {
                    var endBit = cursors[s] - (8L * sectionByte);

                    if (!skipEnd && ((endBit + 7) >> 3) != offsets[s + 1])
                    {
                        throw new InvalidDataException("z3 stream does not end at its offset.");
                    }

                    var padding = (int)(endBit & 7);

                    if (padding != 0 && PeekBits(cursors[s], 8 - padding) != 0)
                    {
                        throw new InvalidDataException("z3 stream padding is not zero.");
                    }
                }

                return total != 0 ? total : (int)(((cursors[0] - (8L * sectionByte)) + 7) >> 3);
            }

            private int DecodeSection(int sectionByte, int available, bool hasLengths, int count, int tableCount, int group, Span<byte> destination, ReadOnlySpan<byte> contexts)
            {
                if (count == 0)
                {
                    return 0;
                }

                var sectionBit = 8L * sectionByte;
                var bitPosition = sectionBit;
                var usesPreset = ParseTables(ref bitPosition, group, tableCount);

                var bound = ((11 * count) >> 3) + (tableCount << 10) + 112;

                if (hasLengths && available > bound)
                {
                    throw new InvalidDataException("z3 section is larger than its bound.");
                }

                var budget = Math.Min(available, bound);
                var maxStreams = tableCount <= 1 ? 11 : 10;
                var minStreams = Math.Min(Math.Max(1, (count + 0x7FFF) >> 15), maxStreams);

                Span<int> offsets = stackalloc int[12];
                Span<long> cursors = stackalloc long[11];
                var streams = ParseStreamHeader(ref bitPosition, sectionByte, minStreams, maxStreams, count, budget, hasLengths, cursors, offsets, out var total);

                var lookups = usesPreset
                    ? GetPresetLookup(Preset, group, 0)
                    : GetLookup(GroupFirstSlot[group], tableCount);
                var j = 0;

                var rounds = FastRounds(cursors[..streams], MaxCodeLength, count);

                while (rounds > 0)
                {
                    if (tableCount == 1)
                    {
                        for (var round = 0; round < rounds; round++)
                        {
                            for (var s = 0; s < streams; s++)
                            {
                                var streamBit = cursors[s];
                                var word = ReadWordUnchecked(streamBit);
                                var next = (int)(word >> (int)(streamBit & 7)) & (LookupSize - 1);
                                var entry = lookups[next];

                                destination[j++] = (byte)(entry >> 4);
                                cursors[s] = streamBit + (entry & 15);
                            }
                        }
                    }
                    else
                    {
                        for (var round = 0; round < rounds; round++)
                        {
                            for (var s = 0; s < streams; s++)
                            {
                                var streamBit = cursors[s];
                                var word = ReadWordUnchecked(streamBit);
                                var next = (int)(word >> (int)(streamBit & 7)) & (LookupSize - 1);
                                var entry = lookups[(contexts[j] * LookupSize) + next];

                                destination[j++] = (byte)(entry >> 4);
                                cursors[s] = streamBit + (entry & 15);
                            }
                        }
                    }

                    rounds = FastRounds(cursors[..streams], MaxCodeLength, count - j);
                }

                var stream = 0;

                for (; j < count; j++)
                {
                    var table = tableCount > 1 ? contexts[j] : 0;
                    var entry = lookups[(table * LookupSize) + (int)PeekBits(cursors[stream], MaxCodeLength)];

                    destination[j] = (byte)(entry >> 4);
                    cursors[stream] += entry & 15;

                    if (++stream == streams)
                    {
                        stream = 0;
                    }
                }

                var consumed = CheckStreamEnds(cursors[..streams], offsets, sectionByte, hasLengths, total);

                if (consumed > budget)
                {
                    throw new InvalidDataException("z3 section consumes more than it has.");
                }

                return consumed;
            }

            private void EntropySection(ref SectionCursor cursor, int group, int count, int tableCount, Span<byte> destination, ReadOnlySpan<byte> contexts)
            {
                cursor.Next(Data, out var start, out var available);
                var consumed = DecodeSection(start, available, cursor.HasLengths, count, tableCount, group, destination, contexts);
                cursor.Done(consumed, available);
            }

            private int EntropySectionWithMax(ref SectionCursor cursor, int group, int count, int tableCount, Span<byte> destination, ReadOnlySpan<byte> contexts)
            {
                EntropySection(ref cursor, group, count, tableCount, destination, contexts);
                return count == 0 ? 0 : TensorPrimitives.Max<byte>(destination[..count]);
            }

            private void ExtraBitsSection(ref SectionCursor cursor, ReadOnlySpan<byte> codes, ExtraCode code, int maxSymbol, Span<long> values)
            {
                cursor.Next(Data, out var sectionByte, out var available);

                var count = codes.Length;
                var maxBits = code.Bits[maxSymbol];

                if (maxBits == 0)
                {
                    for (var index = 0; index < count; index++)
                    {
                        values[index] = code.Values[codes[index]];
                    }

                    cursor.Done(0, available);
                    return;
                }

                var bound = ((count * maxBits) >> 3) + 112;

                if (cursor.HasLengths && available > bound)
                {
                    throw new InvalidDataException("z3 extra bits section is larger than its bound.");
                }

                available = Math.Min(available, bound);

                var bitPosition = 8L * sectionByte;
                Span<int> offsets = stackalloc int[10];
                Span<long> cursors = stackalloc long[8];
                var ways = ParseStreamHeader(ref bitPosition, sectionByte, 1, 8, count, available, cursor.HasLengths, cursors, offsets, out var total);

                var rounds = FastRounds(cursors[..ways], maxBits, count);
                var i = 0;

                while (rounds > 0)
                {
                    for (var round = 0; round < rounds; round++)
                    {
                        for (var w = 0; w < ways; w++)
                        {
                            var symbol = codes[i];
                            var bits = code.Bits[symbol];
                            var streamBit = cursors[w];
                            var extra = (uint)(ReadWordUnchecked(streamBit) >> (int)(streamBit & 7)) & ((1u << bits) - 1);

                            cursors[w] = streamBit + bits;
                            values[i++] = code.Values[symbol] + extra;
                        }
                    }

                    rounds = FastRounds(cursors[..ways], maxBits, count - i);
                }

                var way = 0;

                for (; i < count; i++)
                {
                    var symbol = codes[i];
                    var bits = code.Bits[symbol];
                    var extra = PeekBits(cursors[way], bits);

                    cursors[way] += bits;
                    values[i] = code.Values[symbol] + extra;

                    if (++way == ways)
                    {
                        way = 0;
                    }
                }

                var consumed = CheckStreamEnds(cursors[..ways], offsets, sectionByte, cursor.HasLengths, total);

                if (consumed > available)
                {
                    throw new InvalidDataException("z3 extra bits section consumes more than it has.");
                }

                cursor.Done(consumed, available);
            }
        }
    }
}
