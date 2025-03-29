// Copyright 2020 lewa_j [https://github.com/lewa-j]
// Reference: https://registry.khronos.org/DataFormat/specs/1.3/dataformat.1.3.html#bptc_bc6h
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeBC6H : CommonBPTC, ITextureDecoder
    {
        readonly int blockCountX;
        readonly int blockCountY;

        public DecodeBC6H(int width, int height)
        {
            blockCountX = (width + 3) / 4;
            blockCountY = (height + 3) / 4;
        }

        public void Decode(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var output = pixels.GetPixelSpan<byte>();
            var rowBytes = bitmap.RowBytes;
            var bytesPerPixel = bitmap.BytesPerPixel;

            var isHdrBitmap = bytesPerPixel == 16;
            var outputHdr = MemoryMarshal.Cast<byte, float>(output);

            var endpoints = new int[4, 3];
            var deltas = new short[3, 3];

            for (var j = 0; j < blockCountY; j++)
            {
                for (var i = 0; i < blockCountX; i++)
                {
                    var block = MemoryMarshal.Read<UInt128>(input);
                    input = input[16..];

                    ushort GetValue(int offset, int length)
                    {
                        Debug.Assert(offset < 128);
                        Debug.Assert(length <= 16);

                        return (ushort)((block >> offset) & ((1U << length) - 1));
                    }

                    ulong GetUlongValue(int offset, int length)
                    {
                        Debug.Assert(offset < 128);
                        Debug.Assert(length <= 64);

                        return (ulong)((block >> offset) & ((1UL << length) - 1));
                    }

                    int Bit(int offset)
                    {
                        return (int)((block >> offset) & 1);
                    }

                    var m = (byte)(block & 0x3);
                    if (m >= 2)
                    {
                        m = (byte)(block & 0x1F);
                    }

                    Array.Clear(endpoints, 0, endpoints.Length);
                    Array.Clear(deltas, 0, deltas.Length);

                    var wBits = 0;

                    if (m == 0)
                    {
                        wBits = 10;
                        endpoints[0, 0] = GetValue(5, 10);
                        endpoints[0, 1] = GetValue(15, 10);
                        endpoints[0, 2] = GetValue(25, 10);
                        deltas[0, 0] = SignExtend(GetValue(35, 5), 5);
                        deltas[0, 1] = SignExtend(GetValue(45, 5), 5);
                        deltas[0, 2] = SignExtend(GetValue(55, 5), 5);
                        deltas[1, 0] = SignExtend(GetValue(65, 5), 5);
                        deltas[1, 1] = SignExtend(GetValue(41, 4) | (Bit(2) << 4), 5);
                        deltas[1, 2] = SignExtend(GetValue(61, 3) | (Bit(64) << 3) | (Bit(3) << 4), 5);
                        deltas[2, 0] = SignExtend(GetValue(71, 5), 5);
                        deltas[2, 1] = SignExtend(GetValue(51, 4) | (Bit(40) << 4), 5);
                        deltas[2, 2] = SignExtend(Bit(50) | (Bit(60) << 1) | (Bit(70) << 2) | (Bit(76) << 3) | (Bit(4) << 4), 5);
                    }
                    else if (m == 1)
                    {
                        wBits = 7;
                        endpoints[0, 0] = GetValue(5, 7);
                        endpoints[0, 1] = GetValue(15, 7);
                        endpoints[0, 2] = GetValue(25, 7);
                        deltas[0, 0] = SignExtend(GetValue(35, 6), 6);
                        deltas[0, 1] = SignExtend(GetValue(45, 6), 6);
                        deltas[0, 2] = SignExtend(GetValue(55, 6), 6);
                        deltas[1, 0] = SignExtend(GetValue(65, 6), 6);
                        deltas[1, 1] = SignExtend(GetValue(41, 4) | (Bit(24) << 4) | (Bit(2) << 5), 6);
                        deltas[1, 2] = SignExtend(GetValue(61, 3) | (Bit(64) << 3) | (Bit(14) << 4) | (Bit(22) << 5), 6);
                        deltas[2, 0] = SignExtend(GetValue(71, 6), 6);
                        deltas[2, 1] = SignExtend(GetValue(51, 4) | (Bit(3) << 4) | (Bit(4) << 5), 6);
                        deltas[2, 2] = SignExtend(GetValue(12, 2) | (Bit(23) << 2) | (Bit(32) << 3) | (Bit(34) << 4) | (Bit(33) << 5), 6);
                    }
                    else if (m == 2)
                    {
                        wBits = 11;
                        endpoints[0, 0] = (ushort)(GetValue(5, 10) | (Bit(40) << 10));
                        endpoints[0, 1] = (ushort)(GetValue(15, 10) | (Bit(49) << 10));
                        endpoints[0, 2] = (ushort)(GetValue(25, 10) | (Bit(59) << 10));
                        deltas[0, 0] = SignExtend(GetValue(35, 5), 5);
                        deltas[0, 1] = SignExtend(GetValue(45, 4), 4);
                        deltas[0, 2] = SignExtend(GetValue(55, 4), 4);
                        deltas[1, 0] = SignExtend(GetValue(65, 5), 5);
                        deltas[1, 1] = SignExtend(GetValue(41, 4), 4);
                        deltas[1, 2] = SignExtend(GetValue(61, 3) | (Bit(64) << 3), 4);
                        deltas[2, 0] = SignExtend(GetValue(71, 5), 5);
                        deltas[2, 1] = SignExtend(GetValue(51, 4), 4);
                        deltas[2, 2] = SignExtend(Bit(50) | (Bit(60) << 1) | (Bit(70) << 2) | (Bit(76) << 3), 4);
                    }
                    else if (m == 6)
                    {
                        wBits = 11;
                        endpoints[0, 0] = (ushort)(GetValue(5, 10) | (Bit(39) << 10));
                        endpoints[0, 1] = (ushort)(GetValue(15, 10) | (Bit(50) << 10));
                        endpoints[0, 2] = (ushort)(GetValue(25, 10) | (Bit(59) << 10));
                        deltas[0, 0] = SignExtend(GetValue(35, 4), 4);
                        deltas[0, 1] = SignExtend(GetValue(45, 5), 5);
                        deltas[0, 2] = SignExtend(GetValue(55, 4), 4);
                        deltas[1, 0] = SignExtend(GetValue(65, 4), 4);
                        deltas[1, 1] = SignExtend(GetValue(41, 4) | (Bit(75) << 4), 5);
                        deltas[1, 2] = SignExtend(GetValue(61, 3) | (Bit(64) << 3), 4);
                        deltas[2, 0] = SignExtend(GetValue(71, 4), 4);
                        deltas[2, 1] = SignExtend(GetValue(51, 4) | (Bit(40) << 4), 5);
                        deltas[2, 2] = SignExtend(Bit(69) | (Bit(60) << 1) | (Bit(70) << 2) | (Bit(76) << 3), 4);
                    }
                    else if (m == 10)
                    {
                        wBits = 11;
                        endpoints[0, 0] = (ushort)(GetValue(5, 10) | (Bit(39) << 10));
                        endpoints[0, 1] = (ushort)(GetValue(15, 10) | (Bit(49) << 10));
                        endpoints[0, 2] = (ushort)(GetValue(25, 10) | (Bit(60) << 10));
                        deltas[0, 0] = SignExtend(GetValue(35, 4), 4);
                        deltas[0, 1] = SignExtend(GetValue(45, 4), 4);
                        deltas[0, 2] = SignExtend(GetValue(55, 5), 5);
                        deltas[1, 0] = SignExtend(GetValue(65, 4), 4);
                        deltas[1, 1] = SignExtend(GetValue(41, 4), 4);
                        deltas[1, 2] = SignExtend(GetValue(61, 3) | (Bit(64) << 3) | (Bit(40) << 4), 5);
                        deltas[2, 0] = SignExtend(GetValue(71, 4), 4);
                        deltas[2, 1] = SignExtend(GetValue(51, 4), 4);
                        deltas[2, 2] = SignExtend(Bit(50) | (Bit(69) << 1) | (Bit(70) << 2) | (Bit(76) << 3) | (Bit(75) << 4), 5);
                    }
                    else if (m == 14)
                    {
                        wBits = 9;
                        endpoints[0, 0] = GetValue(5, 9);
                        endpoints[0, 1] = GetValue(15, 9);
                        endpoints[0, 2] = GetValue(25, 9);
                        deltas[0, 0] = SignExtend(GetValue(35, 5), 5);
                        deltas[0, 1] = SignExtend(GetValue(45, 5), 5);
                        deltas[0, 2] = SignExtend(GetValue(55, 5), 5);
                        deltas[1, 0] = SignExtend(GetValue(65, 5), 5);
                        deltas[1, 1] = SignExtend(GetValue(41, 4) | (Bit(24) << 4), 5);
                        deltas[1, 2] = SignExtend(GetValue(61, 3) | (Bit(64) << 3) | (Bit(14) << 4), 5);
                        deltas[2, 0] = SignExtend(GetValue(71, 5), 5);
                        deltas[2, 1] = SignExtend(GetValue(51, 4) | (Bit(40) << 4), 5);
                        deltas[2, 2] = SignExtend(Bit(50) | (Bit(60) << 1) | (Bit(70) << 2) | (Bit(76) << 3) | (Bit(34) << 4), 5);
                    }
                    else if (m == 18)
                    {
                        wBits = 8;
                        endpoints[0, 0] = GetValue(5, 8);
                        endpoints[0, 1] = GetValue(15, 8);
                        endpoints[0, 2] = GetValue(25, 8);
                        deltas[0, 0] = SignExtend(GetValue(35, 6), 6);
                        deltas[0, 1] = SignExtend(GetValue(45, 5), 5);
                        deltas[0, 2] = SignExtend(GetValue(55, 5), 5);
                        deltas[1, 0] = SignExtend(GetValue(65, 6), 6);
                        deltas[1, 1] = SignExtend(GetValue(41, 4) | (Bit(24) << 4), 5);
                        deltas[1, 2] = SignExtend(GetValue(61, 3) | (Bit(64) << 3) | (Bit(14) << 4), 5);
                        deltas[2, 0] = SignExtend(GetValue(71, 6), 6);
                        deltas[2, 1] = SignExtend(GetValue(51, 4) | (Bit(13) << 4), 5);
                        deltas[2, 2] = SignExtend(Bit(50) | (Bit(60) << 1) | (Bit(23) << 2) | (Bit(33) << 3) | (Bit(34) << 4), 5);
                    }
                    else if (m == 22)
                    {
                        wBits = 8;
                        endpoints[0, 0] = GetValue(5, 8);
                        endpoints[0, 1] = GetValue(15, 8);
                        endpoints[0, 2] = GetValue(25, 8);
                        deltas[0, 0] = SignExtend(GetValue(35, 5), 5);
                        deltas[0, 1] = SignExtend(GetValue(45, 6), 6);
                        deltas[0, 2] = SignExtend(GetValue(55, 5), 5);
                        deltas[1, 0] = SignExtend(GetValue(65, 5), 5);
                        deltas[1, 1] = SignExtend(GetValue(41, 4) | (Bit(24) << 4) | (Bit(23) << 5), 6);
                        deltas[1, 2] = SignExtend(GetValue(61, 3) | (Bit(64) << 3) | (Bit(14) << 4), 5);
                        deltas[2, 0] = SignExtend(GetValue(71, 5), 5);
                        deltas[2, 1] = SignExtend(GetValue(51, 4) | (Bit(40) << 4) | (Bit(33) << 5), 6);
                        deltas[2, 2] = SignExtend(Bit(13) | (Bit(60) << 1) | (Bit(70) << 2) | (Bit(76) << 3) | (Bit(34) << 4), 5);
                    }
                    else if (m == 26)
                    {
                        wBits = 8;
                        endpoints[0, 0] = GetValue(5, 8);
                        endpoints[0, 1] = GetValue(15, 8);
                        endpoints[0, 2] = GetValue(25, 8);
                        deltas[0, 0] = SignExtend(GetValue(35, 5), 5);
                        deltas[0, 1] = SignExtend(GetValue(45, 5), 5);
                        deltas[0, 2] = SignExtend(GetValue(55, 6), 6);
                        deltas[1, 0] = SignExtend(GetValue(65, 5), 5);
                        deltas[1, 1] = SignExtend(GetValue(41, 4) | (Bit(24) << 4), 5);
                        deltas[1, 2] = SignExtend(GetValue(61, 3) | (Bit(64) << 3) | (Bit(14) << 4) | (Bit(23) << 5), 6);
                        deltas[2, 0] = SignExtend(GetValue(71, 5), 5);
                        deltas[2, 1] = SignExtend(GetValue(51, 4) | (Bit(40) << 4), 5);
                        deltas[2, 2] = SignExtend(Bit(50) | (Bit(13) << 1) | (Bit(70) << 2) | (Bit(76) << 3) | (Bit(34) << 4) | (Bit(33) << 5), 6);
                    }
                    else if (m == 30)
                    {
                        wBits = 6;
                        endpoints[0, 0] = GetValue(5, 6);
                        endpoints[0, 1] = GetValue(15, 6);
                        endpoints[0, 2] = GetValue(25, 6);
                        endpoints[1, 0] = GetValue(35, 6);
                        endpoints[1, 1] = GetValue(45, 6);
                        endpoints[1, 2] = GetValue(55, 6);
                        endpoints[2, 0] = GetValue(65, 6);
                        endpoints[2, 1] = (ushort)(GetValue(41, 4) | (Bit(24) << 4) | (Bit(21) << 5));
                        endpoints[2, 2] = (ushort)(GetValue(61, 2) | (Bit(64) << 3) | (Bit(14) << 4) | (Bit(22) << 5));
                        endpoints[3, 0] = GetValue(71, 6);
                        endpoints[3, 1] = (ushort)(GetValue(51, 4) | (Bit(11) << 4) | (Bit(31) << 5));
                        endpoints[3, 2] = (ushort)(GetValue(12, 2) | (Bit(23) << 2) | (Bit(32) << 3) | (Bit(34) << 4) | (Bit(33) << 5));
                    }
                    else if (m == 3)
                    {
                        wBits = 10;
                        endpoints[0, 0] = GetValue(5, 10);
                        endpoints[0, 1] = GetValue(15, 10);
                        endpoints[0, 2] = GetValue(25, 10);
                        endpoints[1, 0] = GetValue(35, 10);
                        endpoints[1, 1] = GetValue(45, 10);
                        endpoints[1, 2] = (ushort)(GetValue(55, 9) | (Bit(64) << 9));
                    }
                    else if (m == 7)
                    {
                        wBits = 11;
                        endpoints[0, 0] = (ushort)(GetValue(5, 10) | (Bit(44) << 10));
                        endpoints[0, 1] = (ushort)(GetValue(15, 10) | (Bit(54) << 10));
                        endpoints[0, 2] = (ushort)(GetValue(25, 10) | (Bit(64) << 10));
                        deltas[0, 0] = SignExtend(GetValue(35, 9), 9);
                        deltas[0, 1] = SignExtend(GetValue(45, 9), 9);
                        deltas[0, 2] = SignExtend(GetValue(55, 9), 9);
                    }
                    else if (m == 11)
                    {
                        wBits = 12;
                        endpoints[0, 0] = (ushort)(GetValue(5, 10) | (Bit(44) << 10) | (Bit(43) << 11));
                        endpoints[0, 1] = (ushort)(GetValue(15, 10) | (Bit(54) << 10) | (Bit(53) << 11));
                        endpoints[0, 2] = (ushort)(GetValue(25, 10) | (Bit(64) << 10) | (Bit(63) << 11));
                        deltas[0, 0] = SignExtend(GetValue(35, 8), 8);
                        deltas[0, 1] = SignExtend(GetValue(45, 8), 8);
                        deltas[0, 2] = SignExtend(GetValue(55, 8), 8);
                    }
                    else if (m == 15)
                    {
                        wBits = 16;
                        endpoints[0, 0] = (ushort)(GetValue(5, 10) | (Bit(44) << 10) | (Bit(43) << 11) | (Bit(42) << 12) | (Bit(41) << 13) | (Bit(40) << 14) | (Bit(39) << 15));
                        endpoints[0, 1] = (ushort)(GetValue(15, 10) | (Bit(54) << 10) | (Bit(53) << 11) | (Bit(52) << 12) | (Bit(51) << 13) | (Bit(50) << 14) | (Bit(49) << 15));
                        endpoints[0, 2] = (ushort)(GetValue(25, 10) | (Bit(64) << 10) | (Bit(63) << 11) | (Bit(62) << 12) | (Bit(61) << 13) | (Bit(60) << 14) | (Bit(59) << 15));
                        deltas[0, 0] = SignExtend(GetValue(35, 4), 4);
                        deltas[0, 1] = SignExtend(GetValue(45, 4), 4);
                        deltas[0, 2] = SignExtend(GetValue(55, 4), 4);
                    }

                    var epm = (ushort)((1 << wBits) - 1);

                    var isTransformed = m is not 3 and not 30;
                    var isRegionOne = m is 3 or 7 or 11 or 15;
                    var shapeIndex = 0;
                    ulong packedIndices = 0;

                    if (isRegionOne)
                    {
                        packedIndices = GetUlongValue(65, 63);
                    }
                    else
                    {
                        shapeIndex = GetValue(77, 5);
                        packedIndices = GetUlongValue(82, 46);
                    }

                    if (isTransformed)
                    {
                        for (var d = 0; d < 3; d++)
                        {
                            for (var e = 0; e < 3; e++)
                            {
                                endpoints[d + 1, e] = (ushort)((endpoints[0, e] + deltas[d, e]) & epm);
                            }
                        }
                    }

                    for (var s = 0; s < 4; s++)
                    {
                        for (var e = 0; e < 3; e++)
                        {
                            endpoints[s, e] = Unquantize(endpoints[s, e], wBits);
                        }
                    }

                    for (var by = 0; by < 4; by++)
                    {
                        for (var bx = 0; bx < 4; bx++)
                        {
                            var pixelOffset = (j * 4 + by) * rowBytes + (i * 4 + bx) * bytesPerPixel;
                            var io = by * 4 + bx;

                            var isAnchor = 0;
                            byte bWeight = 0;
                            byte subset = 0;
                            if (isRegionOne)
                            {
                                isAnchor = (io == 0) ? 1 : 0;
                                bWeight = BPTCWeights4[packedIndices & 0xFu >> isAnchor];
                                packedIndices >>= 4 - isAnchor;
                            }
                            else
                            {
                                subset = (byte)(BPTCPartitionTable2[shapeIndex, io] * 2);
                                isAnchor = (io == 0 || io == BPTCAnchorIndices2[shapeIndex]) ? 1 : 0;
                                bWeight = BPTCWeights3[packedIndices & 0x7u >> isAnchor];
                                packedIndices >>= 3 - isAnchor;
                            }

                            var aWeight = 64 - bWeight;
                            var color = Vector3.Zero;

                            for (var e = 0; e < 3; e++)
                            {
                                var a = endpoints[subset, e];
                                var b = endpoints[subset + 1, e];

                                var lerped = (ushort)((a * aWeight + b * bWeight) / (float)(1 << 6));
                                lerped = FinishUnquantize(lerped);

                                var floatValue = (float)Unsafe.As<ushort, Half>(ref lerped);
                                color[e] = floatValue;
                            }


                            if (!isHdrBitmap)
                            {
                                output[pixelOffset] = Common.ToClampedLdrColor(color.Z);
                                output[pixelOffset + 1] = Common.ToClampedLdrColor(color.Y);
                                output[pixelOffset + 2] = Common.ToClampedLdrColor(color.X);
                                output[pixelOffset + 3] = byte.MaxValue;
                                continue;
                            }

                            var pixelOffsetFloat = pixelOffset / sizeof(float);

                            outputHdr[pixelOffsetFloat] = color.X;
                            outputHdr[pixelOffsetFloat + 1] = color.Y;
                            outputHdr[pixelOffsetFloat + 2] = color.Z;
                            outputHdr[pixelOffsetFloat + 3] = 1f;
                        }
                    }
                }
            }
        }

        private static short SignExtend(int v, int bits)
        {
            Debug.Assert(bits < 16);

            var signed = checked((short)v);
            var hasSetSignBit = ((v >> (bits - 1)) & 1) == 1;
            var extendedSignBit = -1 << (bits);

            if (hasSetSignBit)
            {
                signed |= (short)extendedSignBit;
            }

            return signed;
        }

        private static int Unquantize(int quantized, int precision)
        {
            if (precision >= 15)
            {
                return quantized;
            }

            if (quantized == 0)
            {
                return 0;
            }

            var precisionMax = (1 << precision) - 1;

            if (quantized == precisionMax)
            {
                return ushort.MaxValue;
            }

            var ushortRange = ushort.MaxValue + 1;
            return (quantized * ushortRange + ushortRange / 2) >> precision;
        }

        private static ushort FinishUnquantize(int q)
        {
            return (ushort)((q * 31) >> 6);
        }
    }
}
