using System.Buffers;
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
        private static readonly byte[] DecodeBytesGroupCount = new byte[256];

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

                DecodeBytesGroupCount[mask] = count;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<byte> DecodeShuffleMask(byte mask0, byte mask1)
        {
            var sm0 = Vector128.Create(MemoryMarshal.Read<long>(DecodeBytesGroupShuffle.AsSpan()[(mask0 * 8)..]), 0);
            var sm1 = Vector128.Create(MemoryMarshal.Read<long>(DecodeBytesGroupShuffle.AsSpan()[(mask1 * 8)..]), 0);
            var sm1off = Vector128.Create(DecodeBytesGroupCount[mask0]).AsInt64();
            var sm1r = sm1 + sm1off;

            return Sse2.UnpackLow(sm0, sm1r).AsByte();
        }

        private static ReadOnlySpan<byte> DecodeBytesGroupSimd(ReadOnlySpan<byte> data, Span<byte> destination, int hbits)
        {
            switch (hbits)
            {
                case 0:
                case 4:
                    destination[..ByteGroupSize].Clear();

                    return data;
                case 1:
                case 6:
                    {
                        var data32 = MemoryMarshal.Read<uint>(data);
                        data32 &= data32 >> 1;

                        // arrange bits such that low bits of nibbles of data64 contain all 2-bit elements of data32
                        var data64 = ((ulong)data32 << 30) | (data32 & 0x3fffffff);

                        // adds all 1-bit nibbles together; the sum fits in 4 bits because datacnt=16 would have used mode 3
                        var datacnt = (int)(((data64 & 0x1111111111111111ul) * 0x1111111111111111ul) >> 60);

                        var sel2 = Vector128.Create(MemoryMarshal.Read<uint>(data), 0, 0, 0).AsByte();
                        var rest = Vector128.Create<byte>(data[4..]);

                        var sel22 = Sse2.UnpackLow((sel2.AsInt16() >>> 4).AsByte(), sel2);
                        var sel2222 = Sse2.UnpackLow((sel22.AsInt16() >>> 2).AsByte(), sel22);
                        var sel = sel2222 & Vector128.Create((byte)3);

                        var mask = Vector128.Equals(sel, Vector128.Create((byte)3));
                        var mask16 = mask.ExtractMostSignificantBits();
                        var mask0 = (byte)(mask16 & 255);
                        var mask1 = (byte)(mask16 >> 8);

                        var shuf = DecodeShuffleMask(mask0, mask1);
                        var result = Ssse3.Shuffle(rest, shuf) | Sse2.AndNot(mask, sel);

                        result.CopyTo(destination);

                        return data[(4 + datacnt)..];
                    }
                case 2:
                case 7:
                    {
                        var data64 = MemoryMarshal.Read<ulong>(data);
                        data64 &= data64 >> 1;
                        data64 &= data64 >> 2;

                        // adds all 1-bit nibbles together; the sum fits in 4 bits because datacnt=16 would have used mode 3
                        var datacnt = (int)(((data64 & 0x1111111111111111ul) * 0x1111111111111111ul) >> 60);

                        var sel4 = Vector64.Create<byte>(data[..8]).ToVector128();
                        var rest = Vector128.Create<byte>(data[8..]);

                        var sel44 = Sse2.UnpackLow((sel4.AsInt16() >>> 4).AsByte(), sel4);
                        var sel = sel44 & Vector128.Create((byte)15);

                        var mask = Vector128.Equals(sel, Vector128.Create((byte)15));
                        var mask16 = mask.ExtractMostSignificantBits();
                        var mask0 = (byte)(mask16 & 255);
                        var mask1 = (byte)(mask16 >> 8);

                        var shuf = DecodeShuffleMask(mask0, mask1);
                        var result = Ssse3.Shuffle(rest, shuf) | Sse2.AndNot(mask, sel);

                        result.CopyTo(destination);

                        return data[(8 + datacnt)..];
                    }
                case 3:
                case 8:
                    data[..ByteGroupSize].CopyTo(destination);

                    return data[ByteGroupSize..];
                case 5:
                    {
                        var mask0 = data[0];
                        var mask1 = data[1];
                        var rest = Vector128.Create<byte>(data[2..]);

                        var shuf = DecodeShuffleMask(mask0, mask1);
                        var result = Ssse3.Shuffle(rest, shuf);

                        result.CopyTo(destination);

                        return data[(2 + DecodeBytesGroupCount[mask0] + DecodeBytesGroupCount[mask1])..];
                    }
                default:
                    throw new ArgumentException("Unexpected bit length");
            }
        }

        private static ReadOnlySpan<byte> DecodeDeltas4Simd(int channel, ReadOnlySpan<byte> buffer, Span<byte> transposed, int vertexCountAligned, int vertexSize, ReadOnlySpan<byte> lastVertex, int rot)
        {
            var vertexSaveOffset = vertexSize / 4;
            var savep = MemoryMarshal.Cast<byte, int>(transposed);

            var pi = Vector128.Create(MemoryMarshal.Read<uint>(lastVertex), 0, 0, 0).AsByte();
            var savepOffset = 0;

            for (var j = 0; j < vertexCountAligned; j += 16)
            {
                var r0 = Vector128.Create<byte>(buffer[j..]);
                var r1 = Vector128.Create<byte>(buffer[(j + 1 * vertexCountAligned)..]);
                var r2 = Vector128.Create<byte>(buffer[(j + 2 * vertexCountAligned)..]);
                var r3 = Vector128.Create<byte>(buffer[(j + 3 * vertexCountAligned)..]);

                // Transpose8
                var t0 = Sse2.UnpackLow(r0, r1);
                var t1 = Sse2.UnpackHigh(r0, r1);
                var t2 = Sse2.UnpackLow(r2, r3);
                var t3 = Sse2.UnpackHigh(r2, r3);

                r0 = Sse2.UnpackLow(t0.AsInt16(), t2.AsInt16()).AsByte();
                r1 = Sse2.UnpackHigh(t0.AsInt16(), t2.AsInt16()).AsByte();
                r2 = Sse2.UnpackLow(t1.AsInt16(), t3.AsInt16()).AsByte();
                r3 = Sse2.UnpackHigh(t1.AsInt16(), t3.AsInt16()).AsByte();

                switch (channel)
                {
                    case 0:
                        r0 = Unzigzag8Simd(r0);
                        r1 = Unzigzag8Simd(r1);
                        r2 = Unzigzag8Simd(r2);
                        r3 = Unzigzag8Simd(r3);
                        break;
                    case 1:
                        r0 = Unzigzag16Simd(r0);
                        r1 = Unzigzag16Simd(r1);
                        r2 = Unzigzag16Simd(r2);
                        r3 = Unzigzag16Simd(r3);
                        break;
                    case 2:
                        r0 = Rotate32Simd(r0, rot);
                        r1 = Rotate32Simd(r1, rot);
                        r2 = Rotate32Simd(r2, rot);
                        r3 = Rotate32Simd(r3, rot);
                        break;
                }

                // 0
                t0 = Sse2.Shuffle(r0.AsUInt32(), 0).AsByte();
                t1 = Sse2.Shuffle(r0.AsUInt32(), 1).AsByte();
                t2 = Sse2.Shuffle(r0.AsUInt32(), 2).AsByte();
                t3 = Sse2.Shuffle(r0.AsUInt32(), 3).AsByte();

                ApplyChannelOperation(channel, ref pi, ref t0, ref t1, ref t2, ref t3);

                savep[savepOffset] = t0.AsInt32().GetElement(0);
                savepOffset += vertexSaveOffset;
                savep[savepOffset] = t1.AsInt32().GetElement(0);
                savepOffset += vertexSaveOffset;
                savep[savepOffset] = t2.AsInt32().GetElement(0);
                savepOffset += vertexSaveOffset;
                savep[savepOffset] = t3.AsInt32().GetElement(0);
                savepOffset += vertexSaveOffset;

                // 1
                t0 = Sse2.Shuffle(r1.AsUInt32(), 0).AsByte();
                t1 = Sse2.Shuffle(r1.AsUInt32(), 1).AsByte();
                t2 = Sse2.Shuffle(r1.AsUInt32(), 2).AsByte();
                t3 = Sse2.Shuffle(r1.AsUInt32(), 3).AsByte();

                ApplyChannelOperation(channel, ref pi, ref t0, ref t1, ref t2, ref t3);

                savep[savepOffset] = t0.AsInt32().GetElement(0);
                savepOffset += vertexSaveOffset;
                savep[savepOffset] = t1.AsInt32().GetElement(0);
                savepOffset += vertexSaveOffset;
                savep[savepOffset] = t2.AsInt32().GetElement(0);
                savepOffset += vertexSaveOffset;
                savep[savepOffset] = t3.AsInt32().GetElement(0);
                savepOffset += vertexSaveOffset;

                // 2
                t0 = Sse2.Shuffle(r2.AsUInt32(), 0).AsByte();
                t1 = Sse2.Shuffle(r2.AsUInt32(), 1).AsByte();
                t2 = Sse2.Shuffle(r2.AsUInt32(), 2).AsByte();
                t3 = Sse2.Shuffle(r2.AsUInt32(), 3).AsByte();

                ApplyChannelOperation(channel, ref pi, ref t0, ref t1, ref t2, ref t3);

                savep[savepOffset] = t0.AsInt32().GetElement(0);
                savepOffset += vertexSaveOffset;
                savep[savepOffset] = t1.AsInt32().GetElement(0);
                savepOffset += vertexSaveOffset;
                savep[savepOffset] = t2.AsInt32().GetElement(0);
                savepOffset += vertexSaveOffset;
                savep[savepOffset] = t3.AsInt32().GetElement(0);
                savepOffset += vertexSaveOffset;

                // 3
                t0 = Sse2.Shuffle(r3.AsUInt32(), 0).AsByte();
                t1 = Sse2.Shuffle(r3.AsUInt32(), 1).AsByte();
                t2 = Sse2.Shuffle(r3.AsUInt32(), 2).AsByte();
                t3 = Sse2.Shuffle(r3.AsUInt32(), 3).AsByte();

                ApplyChannelOperation(channel, ref pi, ref t0, ref t1, ref t2, ref t3);

                savep[savepOffset] = t0.AsInt32().GetElement(0);
                savepOffset += vertexSaveOffset;
                savep[savepOffset] = t1.AsInt32().GetElement(0);
                savepOffset += vertexSaveOffset;
                savep[savepOffset] = t2.AsInt32().GetElement(0);
                savepOffset += vertexSaveOffset;
                savep[savepOffset] = t3.AsInt32().GetElement(0);
                savepOffset += vertexSaveOffset;
            }

            return buffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ApplyChannelOperation(int channel, ref Vector128<byte> pi, ref Vector128<byte> t0, ref Vector128<byte> t1, ref Vector128<byte> t2, ref Vector128<byte> t3)
        {
            switch (channel)
            {
                case 0:
                    t0 = pi = Sse2.Add(pi, t0);
                    t1 = pi = Sse2.Add(pi, t1);
                    t2 = pi = Sse2.Add(pi, t2);
                    t3 = pi = Sse2.Add(pi, t3);
                    break;
                case 1:
                    t0 = pi = Sse2.Add(pi.AsInt16(), t0.AsInt16()).AsByte();
                    t1 = pi = Sse2.Add(pi.AsInt16(), t1.AsInt16()).AsByte();
                    t2 = pi = Sse2.Add(pi.AsInt16(), t2.AsInt16()).AsByte();
                    t3 = pi = Sse2.Add(pi.AsInt16(), t3.AsInt16()).AsByte();
                    break;
                case 2:
                    t0 = pi = Sse2.Xor(pi, t0);
                    t1 = pi = Sse2.Xor(pi, t1);
                    t2 = pi = Sse2.Xor(pi, t2);
                    t3 = pi = Sse2.Xor(pi, t3);
                    break;
            }
        }

        private static ReadOnlySpan<byte> DecodeVertexBlockSimd(ReadOnlySpan<byte> data, Span<byte> vertexData, int vertexCount, int vertexSize, Span<byte> lastVertex, ReadOnlySpan<byte> channels, int version)
        {
            if (vertexCount <= 0 || vertexCount > VertexBlockMaxSize)
            {
                throw new ArgumentException("Expected vertexCount to be between 0 and VertexMaxBlockSize");
            }

            var bufferPool = ArrayPool<byte>.Shared.Rent(VertexBlockMaxSize * 4);
            var buffer = bufferPool.AsSpan(0, VertexBlockMaxSize * 4);

            var transposedPool = ArrayPool<byte>.Shared.Rent(VertexBlockSizeBytes);
            var transposed = transposedPool.AsSpan(0, VertexBlockSizeBytes);

            var vertexCountAligned = (vertexCount + ByteGroupSize - 1) & ~(ByteGroupSize - 1);
            var controlSize = version == 0 ? 0 : vertexSize / 4;

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
                            // Literal encoding
                            if (data.Length < vertexCountAligned)
                            {
                                throw new InvalidOperationException("Data buffer too small for literal encoding.");
                            }

                            data[..vertexCountAligned].CopyTo(buffer.Slice(j * vertexCountAligned, vertexCountAligned));
                            data = data[vertexCount..];
                        }
                        else if (ctrl == 2)
                        {
                            // Zero encoding
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
                            DecodeDeltas4Simd(0, buffer, transposed[k..], vertexCountAligned, vertexSize, lastVertex[k..], 0);
                            break;
                        case 1:
                            DecodeDeltas4Simd(1, buffer, transposed[k..], vertexCountAligned, vertexSize, lastVertex[k..], 0);
                            break;
                        case 2:
                            DecodeDeltas4Simd(2, buffer, transposed[k..], vertexCountAligned, vertexSize, lastVertex[k..], (32 - (channel >> 4)) & 31);
                            break;
                        default:
                            throw new InvalidOperationException("Invalid channel type");
                    }
                }

                transposed[..(vertexCount * vertexSize)].CopyTo(vertexData);

                transposed.Slice(vertexSize * (vertexCount - 1), vertexSize).CopyTo(lastVertex);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bufferPool);
                ArrayPool<byte>.Shared.Return(transposedPool);
            }

            return data;
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
            var vv = v.AsUInt16();
            var xl = (Vector128<ushort>.Zero - (vv & Vector128<ushort>.One)).AsByte();
            var xr = (vv >>> 1).AsByte();

            return xl ^ xr;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<byte> Rotate32Simd(Vector128<byte> v, int r)
        {
            var v32 = v.AsUInt32();
            return (v32 << r | v32 >>> (32 - r)).AsByte();
        }

        private static ReadOnlySpan<byte> DecodeBytesSimd(ReadOnlySpan<byte> data, Span<byte> destination, int hshift)
        {
            if (destination.Length % ByteGroupSize != 0)
            {
                throw new ArgumentException("Expected data length to be a multiple of ByteGroupSize.");
            }

            var headerSize = ((destination.Length / ByteGroupSize) + 3) / 4;
            var header = data[..headerSize];

            data = data[headerSize..];

            var i = 0;

            // fast-path: process 4 groups at a time, do a shared bounds check
            for (; i + ByteGroupSize * 4 <= destination.Length && data.Length >= ByteGroupDecodeLimit * 4; i += ByteGroupSize * 4)
            {
                var header_offset = i / ByteGroupSize;
                var header_byte = header[header_offset / 4];

                data = DecodeBytesGroupSimd(data, destination[(i + ByteGroupSize * 0)..], hshift + ((header_byte >> 0) & 3));
                data = DecodeBytesGroupSimd(data, destination[(i + ByteGroupSize * 1)..], hshift + ((header_byte >> 2) & 3));
                data = DecodeBytesGroupSimd(data, destination[(i + ByteGroupSize * 2)..], hshift + ((header_byte >> 4) & 3));
                data = DecodeBytesGroupSimd(data, destination[(i + ByteGroupSize * 3)..], hshift + ((header_byte >> 6) & 3));
            }

            // slow-path: process remaining groups
            for (; i < destination.Length; i += ByteGroupSize)
            {
                if (data.Length < ByteGroupDecodeLimit)
                {
                    throw new InvalidOperationException("Cannot decode");
                }

                var headerOffset = i / ByteGroupSize;
                var headerByte = header[headerOffset / 4];

                data = DecodeBytesGroupSimd(data, destination[i..], hshift + ((headerByte >> ((headerOffset % 4) * 2)) & 3));
            }

            return data;
        }
    }
}
