/**
 * C# Port of https://github.com/zeux/meshoptimizer/blob/master/src/vertexcodec.cpp
 */
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace ValveResourceFormat.Compression
{
    public static partial class MeshOptimizerVertexDecoder
    {
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

        private static Span<byte> DecodeBytesGroupSimd(Span<byte> data, Span<byte> destination, int bitslog2)
        {
            switch (bitslog2)
            {
                case 0:
                    destination[..ByteGroupSize].Clear();

                    return data;
                case 1:
                    {
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

                        return data[(4 + DecodeBytesGroupCount[mask0] + DecodeBytesGroupCount[mask1])..];
                    }
                case 2:
                    {
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

                        return data[(8 + DecodeBytesGroupCount[mask0] + DecodeBytesGroupCount[mask1])..];
                    }
                case 3:
                    data[..ByteGroupSize].CopyTo(destination);

                    return data[ByteGroupSize..];
                default:
                    throw new ArgumentException("Unexpected bit length");
            }
        }

        private static Span<byte> DecodeVertexBlockSimd(Span<byte> data, Span<byte> vertexData, int vertexCount, int vertexSize, Span<byte> lastVertex)
        {
            if (vertexCount <= 0 || vertexCount > VertexBlockMaxSize)
            {
                throw new ArgumentException("Expected vertexCount to be between 0 and VertexMaxBlockSize");
            }

            var vertexCountAligned = (vertexCount + ByteGroupSize - 1) & ~(ByteGroupSize - 1);
            var vertexSaveOffset = vertexSize / 4;

            var bufferPool = ArrayPool<byte>.Shared.Rent(VertexBlockMaxSize * 4);
            var buffer = bufferPool.AsSpan(0, VertexBlockMaxSize * 4);
            var transposedPool = ArrayPool<byte>.Shared.Rent(VertexBlockSizeBytes);
            var transposed = transposedPool.AsSpan(0, VertexBlockSizeBytes);

            try
            {
                var savep = MemoryMarshal.Cast<byte, int>(transposed);
                var kInt = 0;

                for (var k = 0; k < vertexSize; k += 4)
                {
                    for (var j = 0; j < 4; ++j)
                    {
                        data = DecodeBytesSimd(data, buffer.Slice(j * vertexCountAligned, vertexCountAligned));

                        if (data.IsEmpty)
                        {
                            return [];
                        }
                    }

                    var pi = Vector128.Create(MemoryMarshal.Read<uint>(lastVertex[k..]), 0, 0, 0).AsByte();
                    var savepOffset = kInt++; // k / 4

                    for (var j = 0; j < vertexCountAligned; j += 16)
                    {
                        var r0 = Vector128.Create<byte>(buffer[j..]);
                        var r1 = Vector128.Create<byte>(buffer[(j + 1 * vertexCountAligned)..]);
                        var r2 = Vector128.Create<byte>(buffer[(j + 2 * vertexCountAligned)..]);
                        var r3 = Vector128.Create<byte>(buffer[(j + 3 * vertexCountAligned)..]);

                        r0 = Unzigzag8Simd(r0);
                        r1 = Unzigzag8Simd(r1);
                        r2 = Unzigzag8Simd(r2);
                        r3 = Unzigzag8Simd(r3);

                        // Transpose8
                        var t0 = Sse2.UnpackLow(r0, r1);
                        var t1 = Sse2.UnpackHigh(r0, r1);
                        var t2 = Sse2.UnpackLow(r2, r3);
                        var t3 = Sse2.UnpackHigh(r2, r3);

                        r0 = Sse2.UnpackLow(t0.AsInt16(), t2.AsInt16()).AsByte();
                        r1 = Sse2.UnpackHigh(t0.AsInt16(), t2.AsInt16()).AsByte();
                        r2 = Sse2.UnpackLow(t1.AsInt16(), t3.AsInt16()).AsByte();
                        r3 = Sse2.UnpackHigh(t1.AsInt16(), t3.AsInt16()).AsByte();

                        // 0
                        t0 = Sse2.Shuffle(r0.AsUInt32(), 0).AsByte();
                        t1 = Sse2.Shuffle(r0.AsUInt32(), 1).AsByte();
                        t2 = Sse2.Shuffle(r0.AsUInt32(), 2).AsByte();
                        t3 = Sse2.Shuffle(r0.AsUInt32(), 3).AsByte();
                        t0 = pi += t0;
                        t1 = pi += t1;
                        t2 = pi += t2;
                        t3 = pi += t3;

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
                        t0 = pi += t0;
                        t1 = pi += t1;
                        t2 = pi += t2;
                        t3 = pi += t3;

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
                        t0 = pi += t0;
                        t1 = pi += t1;
                        t2 = pi += t2;
                        t3 = pi += t3;

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
                        t0 = pi += t0;
                        t1 = pi += t1;
                        t2 = pi += t2;
                        t3 = pi += t3;

                        savep[savepOffset] = t0.AsInt32().GetElement(0);
                        savepOffset += vertexSaveOffset;
                        savep[savepOffset] = t1.AsInt32().GetElement(0);
                        savepOffset += vertexSaveOffset;
                        savep[savepOffset] = t2.AsInt32().GetElement(0);
                        savepOffset += vertexSaveOffset;
                        savep[savepOffset] = t3.AsInt32().GetElement(0);
                        savepOffset += vertexSaveOffset;
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

        private static Span<byte> DecodeBytesSimd(Span<byte> data, Span<byte> destination)
        {
            if (destination.Length % ByteGroupSize != 0)
            {
                throw new ArgumentException("Expected data length to be a multiple of ByteGroupSize.");
            }

            var headerSize = ((destination.Length / ByteGroupSize) + 3) / 4;
            var header = data[..];

            data = data[headerSize..];

            var i = 0;

            // fast-path: process 4 groups at a time, do a shared bounds check - each group reads <=24b
            for (; i + ByteGroupSize * 4 <= destination.Length && data.Length >= ByteGroupDecodeLimit * 4; i += ByteGroupSize * 4)
            {
                var header_offset = i / ByteGroupSize;
                var header_byte = header[header_offset / 4];

                data = DecodeBytesGroupSimd(data, destination[(i + ByteGroupSize * 0)..], (header_byte >> 0) & 3);
                data = DecodeBytesGroupSimd(data, destination[(i + ByteGroupSize * 1)..], (header_byte >> 2) & 3);
                data = DecodeBytesGroupSimd(data, destination[(i + ByteGroupSize * 2)..], (header_byte >> 4) & 3);
                data = DecodeBytesGroupSimd(data, destination[(i + ByteGroupSize * 3)..], (header_byte >> 6) & 3);
            }

            // slow-path: process remaining groups
            for (; i < destination.Length; i += ByteGroupSize)
            {
                if (data.Length < ByteGroupDecodeLimit)
                {
                    throw new InvalidOperationException("Cannot decode");
                }

                var headerOffset = i / ByteGroupSize;

                var bitslog2 = (header[headerOffset / 4] >> (headerOffset % 4 * 2)) & 3;

                data = DecodeBytesGroupSimd(data, destination[i..], bitslog2);
            }

            return data;
        }
    }
}
