using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace ValveResourceFormat.Compression
{
    public static partial class MeshOptimizerVertexDecoder
    {
        /// <summary>
        /// Gets a value indicating whether hardware acceleration is available for decoding.
        /// </summary>
        public static bool IsHardwareAccelerated => Vector128.IsHardwareAccelerated && Sse2.IsSupported && Ssse3.IsSupported;

        private static readonly byte[] DecodeBytesGroupShuffle = new byte[256 * 8];

        static MeshOptimizerVertexDecoder()
        {
            for (var mask = 0; mask < 256; mask++)
            {
                byte count = 0;

                for (var i = 0; i < 8; i++)
                {
                    var maski = (mask >> i) & 1;
                    DecodeBytesGroupShuffle[mask * 8 + i] = maski != 0 ? count : (byte)0x80;
                    count += (byte)maski;
                }
            }
        }

        // sent mask, replicating shuffle, and two multipliers (even/odd) for multishift emulation
        private static ReadOnlySpan<byte> DecodeBytesGroupConfig =>
        [
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 0, 64, 0, 4, 0, 64, 0, 4, 0, 64, 0, 4, 0, 64, 0, 16, 0, 0, 1, 16, 0, 0, 1, 16, 0, 0, 1, 16, 0, 0, 1,
            15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 16, 0, 16, 0, 16, 0, 16, 0, 16, 0, 16, 0, 16, 0, 16, 0, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 64, 0, 16, 0, 4, 0, 0, 1, 64, 0, 16, 0, 4, 0, 128, 0, 32, 0, 8, 0, 2, 0, 128, 0, 32, 0, 8, 0, 2, 0,
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 0, 64, 0, 4, 0, 64, 0, 4, 0, 64, 0, 4, 0, 64, 0, 16, 0, 0, 1, 16, 0, 0, 1, 16, 0, 0, 1, 16, 0, 0, 1,
            15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 16, 0, 16, 0, 16, 0, 16, 0, 16, 0, 16, 0, 16, 0, 16, 0, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        ];

        private static ReadOnlySpan<byte> Hbtn => [4, 1, 2, 3, 4, 0, 1, 2, 3];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<byte> LoadConfig(int hbits, int index)
        {
            return Unsafe.ReadUnaligned<Vector128<byte>>(ref Unsafe.Add(ref MemoryMarshal.GetReference(DecodeBytesGroupConfig), hbits * 64 + index * 16));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ReadOnlySpan<byte> DecodeBytesGroupSimd(ReadOnlySpan<byte> data, Span<byte> buffer, int hbits)
        {
            // 0 for 1-bit, 1 for 2-bit, 2 for 4-bit, 3 for 8-bit, and 4 for 0-bit as it makes some of the uses easier
            var n = Hbtn[hbits];

            // for 8-bit groups, instead of loading the bytes through 'data', we load them through 'skip' as they are easier to preserve
            // for 0-bit groups, the load results get discarded because mask is always 0; in both cases the shift wraps to zero
            var skip = (2 << n) & 15;

            ref var dataRef = ref MemoryMarshal.GetReference(data);
            var selb = Vector128.CreateScalar(Unsafe.ReadUnaligned<long>(ref dataRef)).AsByte();
            var rest = Unsafe.ReadUnaligned<Vector128<byte>>(ref Unsafe.Add(ref dataRef, skip));

            // unpack 1, 2 or 4-bit values: shuffle replicates each source byte into both halves of a 16-bit lane
            // mulhi extracts even and odd fields into the low byte; the results are interleaved back with shift/or
            var selw = Ssse3.Shuffle(selb, LoadConfig(hbits, 1)).AsUInt16();
            var sel0 = Sse2.MultiplyHigh(selw, LoadConfig(hbits, 2).AsUInt16());
            var sel1 = Sse2.MultiplyHigh(selw, LoadConfig(hbits, 3).AsUInt16());
            var seli = (sel0 | (sel1 << 8)).AsByte();

            // the interleaved fields are masked by the bit count (special handling: for 0/8-bit values, mul produces 0)
            var sent = LoadConfig(hbits, 0);
            var sel = seli & sent;

            // compare sel to sentinel; returns 0 for 0-bit (mul produces 0, sent is 1), 1 for 8-bit (mul produces 0, sent is 0)
            var mask = Vector128.Equals(sel, sent);
            var mask16 = mask.ExtractMostSignificantBits();
            var mask0 = (byte)(mask16 & 255);
            var mask1 = (byte)(mask16 >> 8);

            // decode shuffle mask from two halves; second half needs to be shifted by popcount(mask0)
            ref var shuffleRef = ref MemoryMarshal.GetArrayDataReference(DecodeBytesGroupShuffle);
            var sm0 = Vector128.CreateScalar(Unsafe.ReadUnaligned<long>(ref Unsafe.Add(ref shuffleRef, mask0 * 8))).AsByte();
            var sm1 = Vector128.CreateScalar(Unsafe.ReadUnaligned<long>(ref Unsafe.Add(ref shuffleRef, mask1 * 8))).AsByte();

            // each lane of mask is 0x00 or 0xff; sad yields 255*popcount(mask0) in low word => low byte is -popcount(mask0)
            var npops = Sse2.SumAbsoluteDifferences(mask, Vector128<byte>.Zero).AsByte();
            var sm1r = sm1 - Ssse3.Shuffle(npops, Vector128<byte>.Zero);
            var shuf = Sse2.UnpackLow(sm0.AsInt64(), sm1r.AsInt64()).AsByte();

            // expand rest via shuffle mask and combine with sel; shuffle mask zeroes out bytes that are replaced by sel
            var result = Ssse3.Shuffle(rest, shuf) | Sse2.AndNot(mask, sel);

            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(buffer), result);

            return data[(skip + BitOperations.PopCount(mask16))..];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Transpose8(ref Vector128<byte> x0, ref Vector128<byte> x1, ref Vector128<byte> x2, ref Vector128<byte> x3)
        {
            var t0 = Sse2.UnpackLow(x0, x1);
            var t1 = Sse2.UnpackHigh(x0, x1);
            var t2 = Sse2.UnpackLow(x2, x3);
            var t3 = Sse2.UnpackHigh(x2, x3);

            x0 = Sse2.UnpackLow(t0.AsInt16(), t2.AsInt16()).AsByte();
            x1 = Sse2.UnpackHigh(t0.AsInt16(), t2.AsInt16()).AsByte();
            x2 = Sse2.UnpackLow(t1.AsInt16(), t3.AsInt16()).AsByte();
            x3 = Sse2.UnpackHigh(t1.AsInt16(), t3.AsInt16()).AsByte();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<byte> Unzigzag8Simd(Vector128<byte> v)
        {
            var xl = Vector128<byte>.Zero - (v & Vector128<byte>.One);
            var xr = (v.AsUInt16() >>> 1).AsByte() & Vector128.Create((byte)127);

            return xl ^ xr;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<byte> Unzigzag16Simd(Vector128<byte> v)
        {
            var xl = Vector128<ushort>.Zero - (v.AsUInt16() & Vector128<ushort>.One);
            var xr = v.AsUInt16() >>> 1;

            return (xl ^ xr).AsByte();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<byte> Rotate32Simd(Vector128<byte> v, int r)
        {
            return (v.AsUInt32() << r | v.AsUInt32() >>> (32 - r)).AsByte();
        }

        private static ReadOnlySpan<byte> DecodeBytesSimd(ReadOnlySpan<byte> data, Span<byte> buffer, int hshift)
        {
            if (buffer.Length % ByteGroupSize != 0)
            {
                throw new ArgumentException("Expected data length to be a multiple of ByteGroupSize.");
            }

            // round number of groups to 4 to get number of header bytes
            var headerSize = ((buffer.Length / ByteGroupSize) + 3) / 4;

            if (data.Length < headerSize)
            {
                throw new InvalidOperationException("Data buffer too small for header.");
            }

            var header = data[..headerSize];
            data = data[headerSize..];

            var i = 0;

            // fast-path: process 4 groups at a time, do a shared bounds check
            for (; i + ByteGroupSize * 4 <= buffer.Length && data.Length >= ByteGroupDecodeLimit * 4; i += ByteGroupSize * 4)
            {
                var headerOffset = i / ByteGroupSize;
                var headerByte = header[headerOffset / 4];

                // very-fast-path: for consecutive 4 groups that are all 0-bit (v0/0, v1/0/0000) or 8-bit (v0/3333, v1/1/3333),
                // the branchless decoders are slower than branching over the decoding of 4 groups and issuing a few load/store ops
                if (hshift != 5 && headerByte == 0)
                {
                    buffer.Slice(i, ByteGroupSize * 4).Clear();
                    continue;
                }
                else if (hshift != 4 && headerByte == 255)
                {
                    data[..(ByteGroupSize * 4)].CopyTo(buffer[i..]);
                    data = data[(ByteGroupSize * 4)..];
                    continue;
                }

                data = DecodeBytesGroupSimd(data, buffer[(i + ByteGroupSize * 0)..], hshift + ((headerByte >> 0) & 3));
                data = DecodeBytesGroupSimd(data, buffer[(i + ByteGroupSize * 1)..], hshift + ((headerByte >> 2) & 3));
                data = DecodeBytesGroupSimd(data, buffer[(i + ByteGroupSize * 2)..], hshift + ((headerByte >> 4) & 3));
                data = DecodeBytesGroupSimd(data, buffer[(i + ByteGroupSize * 3)..], hshift + ((headerByte >> 6) & 3));
            }

            // slow-path: process remaining groups
            for (; i < buffer.Length; i += ByteGroupSize)
            {
                if (data.Length < ByteGroupDecodeLimit)
                {
                    throw new InvalidOperationException("Cannot decode");
                }

                var headerOffset = i / ByteGroupSize;
                var headerByte = header[headerOffset / 4];

                data = DecodeBytesGroupSimd(data, buffer[i..], hshift + ((headerByte >> ((headerOffset % 4) * 2)) & 3));
            }

            return data;
        }

        private interface IChannel
        {
            static abstract Vector128<byte> Unzr(Vector128<byte> r, int rot);
            static abstract Vector128<byte> Fixd(Vector128<byte> pi, Vector128<byte> t);
        }

        private struct Channel0 : IChannel
        {
            public static Vector128<byte> Unzr(Vector128<byte> r, int rot) => Unzigzag8Simd(r);
            public static Vector128<byte> Fixd(Vector128<byte> pi, Vector128<byte> t) => Sse2.Add(pi, t);
        }

        private struct Channel1 : IChannel
        {
            public static Vector128<byte> Unzr(Vector128<byte> r, int rot) => Unzigzag16Simd(r);
            public static Vector128<byte> Fixd(Vector128<byte> pi, Vector128<byte> t) => Sse2.Add(pi.AsInt16(), t.AsInt16()).AsByte();
        }

        private struct Channel2 : IChannel
        {
            public static Vector128<byte> Unzr(Vector128<byte> r, int rot) => Rotate32Simd(r, rot);
            public static Vector128<byte> Fixd(Vector128<byte> pi, Vector128<byte> t) => Sse2.Xor(pi, t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Save(ref byte savep, ref int savepOffset, int vertexSize, Vector128<byte> t)
        {
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref savep, savepOffset), t.AsInt32().ToScalar());
            savepOffset += vertexSize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Grp4<TChannel>(ref Vector128<byte> pi, ref byte savep, ref int savepOffset, int vertexSize, Vector128<byte> r) where TChannel : struct, IChannel
        {
            var t0 = r;
            var t1 = Sse2.Shuffle(r.AsUInt32(), 1).AsByte();
            var t2 = Sse2.Shuffle(r.AsUInt32(), 2).AsByte();
            var t3 = Sse2.Shuffle(r.AsUInt32(), 3).AsByte();

            t0 = pi = TChannel.Fixd(pi, t0);
            t1 = pi = TChannel.Fixd(pi, t1);
            t2 = pi = TChannel.Fixd(pi, t2);
            t3 = pi = TChannel.Fixd(pi, t3);

            Save(ref savep, ref savepOffset, vertexSize, t0);
            Save(ref savep, ref savepOffset, vertexSize, t1);
            Save(ref savep, ref savepOffset, vertexSize, t2);
            Save(ref savep, ref savepOffset, vertexSize, t3);
        }

        private static void DecodeDeltas4Simd<TChannel>(ReadOnlySpan<byte> buffer, Span<byte> transposed, int vertexCountAligned, int vertexSize, ReadOnlySpan<byte> lastVertex, int rot) where TChannel : struct, IChannel
        {
            var pi = Vector128.Create(MemoryMarshal.Read<uint>(lastVertex), 0, 0, 0).AsByte();

            // the delta loop writes 16 vertices at a time, so the caller guarantees the output covers vertexCountAligned vertices
            if (transposed.Length < vertexCountAligned * vertexSize - (vertexSize - 4) || buffer.Length < vertexCountAligned * 4)
            {
                throw new ArgumentException("Buffer too small for delta decoding.");
            }

            ref var savep = ref MemoryMarshal.GetReference(transposed);
            ref var bufferRef = ref MemoryMarshal.GetReference(buffer);
            var savepOffset = 0;

            for (var j = 0; j < vertexCountAligned; j += 16)
            {
                var r0 = Unsafe.ReadUnaligned<Vector128<byte>>(ref Unsafe.Add(ref bufferRef, j + 0 * vertexCountAligned));
                var r1 = Unsafe.ReadUnaligned<Vector128<byte>>(ref Unsafe.Add(ref bufferRef, j + 1 * vertexCountAligned));
                var r2 = Unsafe.ReadUnaligned<Vector128<byte>>(ref Unsafe.Add(ref bufferRef, j + 2 * vertexCountAligned));
                var r3 = Unsafe.ReadUnaligned<Vector128<byte>>(ref Unsafe.Add(ref bufferRef, j + 3 * vertexCountAligned));

                Transpose8(ref r0, ref r1, ref r2, ref r3);

                r0 = TChannel.Unzr(r0, rot);
                Grp4<TChannel>(ref pi, ref savep, ref savepOffset, vertexSize, r0);

                r1 = TChannel.Unzr(r1, rot);
                Grp4<TChannel>(ref pi, ref savep, ref savepOffset, vertexSize, r1);

                r2 = TChannel.Unzr(r2, rot);
                Grp4<TChannel>(ref pi, ref savep, ref savepOffset, vertexSize, r2);

                r3 = TChannel.Unzr(r3, rot);
                Grp4<TChannel>(ref pi, ref savep, ref savepOffset, vertexSize, r3);
            }
        }

        private static ReadOnlySpan<byte> DecodeVertexBlockSimd(ReadOnlySpan<byte> data, Span<byte> vertexData, int vertexCount, int vertexSize, Span<byte> lastVertex, ReadOnlySpan<byte> channels, int version)
        {
            if (vertexCount <= 0 || vertexCount > VertexBlockMaxSize)
            {
                throw new ArgumentException("Expected vertexCount to be between 0 and VertexMaxBlockSize");
            }

            var vertexCountAligned = (vertexCount + ByteGroupSize - 1) & ~(ByteGroupSize - 1);
            var controlSize = version == 0 ? 0 : vertexSize / 4;

            if (data.Length < controlSize)
            {
                throw new InvalidOperationException("Data buffer too small for control data.");
            }

            var bufferPool = ArrayPool<byte>.Shared.Rent(VertexBlockMaxSize * 4);
            var buffer = bufferPool.AsSpan(0, VertexBlockMaxSize * 4);

            var transposedPool = ArrayPool<byte>.Shared.Rent(VertexBlockSizeBytes);
            var transposed = transposedPool.AsSpan(0, VertexBlockSizeBytes);

            // we can decode directly into the output buffer if vertex count is aligned to 16 (delta decode works 16 vertices at a time)
            var target = vertexCount == vertexCountAligned ? vertexData : transposed;

            try
            {
                var control = data[..controlSize];
                data = data[controlSize..];

                for (var k = 0; k < vertexSize; k += 4)
                {
                    var ctrlByte = version == 0 ? (byte)0 : control[k / 4];

                    for (var j = 0; j < 4; ++j)
                    {
                        var ctrl = (ctrlByte >> (j * 2)) & 3;

                        if (ctrl == 3)
                        {
                            // literal encoding; safe to over-copy due to tail
                            if (data.Length < vertexCountAligned)
                            {
                                throw new InvalidOperationException("Data buffer too small for literal encoding.");
                            }

                            data[..vertexCountAligned].CopyTo(buffer.Slice(j * vertexCountAligned, vertexCountAligned));
                            data = data[vertexCount..];
                        }
                        else if (ctrl == 2)
                        {
                            // zero encoding
                            buffer.Slice(j * vertexCountAligned, vertexCountAligned).Clear();
                        }
                        else
                        {
                            // for v0, headers are mapped to 0..3; for v1, headers are mapped to 4..8
                            var hshift = version == 0 ? 0 : 4 + ctrl;

                            data = DecodeBytesSimd(data, buffer.Slice(j * vertexCountAligned, vertexCountAligned), hshift);
                        }
                    }

                    var channel = version == 0 ? 0 : channels[k / 4];

                    switch (channel & 3)
                    {
                        case 0:
                            DecodeDeltas4Simd<Channel0>(buffer, target[k..], vertexCountAligned, vertexSize, lastVertex[k..], 0);
                            break;
                        case 1:
                            DecodeDeltas4Simd<Channel1>(buffer, target[k..], vertexCountAligned, vertexSize, lastVertex[k..], 0);
                            break;
                        case 2:
                            DecodeDeltas4Simd<Channel2>(buffer, target[k..], vertexCountAligned, vertexSize, lastVertex[k..], (32 - (channel >> 4)) & 31);
                            break;
                        default:
                            throw new InvalidOperationException("Invalid channel type");
                    }
                }

                if (target == transposed)
                {
                    transposed[..(vertexCount * vertexSize)].CopyTo(vertexData);
                }

                target.Slice(vertexSize * (vertexCount - 1), vertexSize).CopyTo(lastVertex);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bufferPool);
                ArrayPool<byte>.Shared.Return(transposedPool);
            }

            return data;
        }
    }
}
