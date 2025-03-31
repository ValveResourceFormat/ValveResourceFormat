// Copyright 2020 lewa_j [https://github.com/lewa-j]
// Reference: https://www.khronos.org/registry/DataFormat/specs/1.3/dataformat.1.3.html#BPTC
using System.Runtime.InteropServices;
using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeBC7 : CommonBPTC, ITextureDecoder
    {
        /// <summary>
        /// Partition table for 3-subset BPTC, with the 4×4 block of values for each partition number.
        /// </summary>
        private static readonly byte[,] BC7PartitionTable3 = new byte[64, 16]
        {
            { 0, 0, 1, 1, 0, 0, 1, 1, 0, 2, 2, 1, 2, 2, 2, 2 },
            { 0, 0, 0, 1, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2, 2, 1 },
            { 0, 0, 0, 0, 2, 0, 0, 1, 2, 2, 1, 1, 2, 2, 1, 1 },
            { 0, 2, 2, 2, 0, 0, 2, 2, 0, 0, 1, 1, 0, 1, 1, 1 },
            { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2 },
            { 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 2, 2, 0, 0, 2, 2 },
            { 0, 0, 2, 2, 0, 0, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1 },
            { 0, 0, 1, 1, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2, 1, 1 },
            { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2 },
            { 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2 },
            { 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2 },
            { 0, 0, 1, 2, 0, 0, 1, 2, 0, 0, 1, 2, 0, 0, 1, 2 },
            { 0, 1, 1, 2, 0, 1, 1, 2, 0, 1, 1, 2, 0, 1, 1, 2 },
            { 0, 1, 2, 2, 0, 1, 2, 2, 0, 1, 2, 2, 0, 1, 2, 2 },
            { 0, 0, 1, 1, 0, 1, 1, 2, 1, 1, 2, 2, 1, 2, 2, 2 },
            { 0, 0, 1, 1, 2, 0, 0, 1, 2, 2, 0, 0, 2, 2, 2, 0 },
            { 0, 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 2, 1, 1, 2, 2 },
            { 0, 1, 1, 1, 0, 0, 1, 1, 2, 0, 0, 1, 2, 2, 0, 0 },
            { 0, 0, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2, 1, 1, 2, 2 },
            { 0, 0, 2, 2, 0, 0, 2, 2, 0, 0, 2, 2, 1, 1, 1, 1 },
            { 0, 1, 1, 1, 0, 1, 1, 1, 0, 2, 2, 2, 0, 2, 2, 2 },
            { 0, 0, 0, 1, 0, 0, 0, 1, 2, 2, 2, 1, 2, 2, 2, 1 },
            { 0, 0, 0, 0, 0, 0, 1, 1, 0, 1, 2, 2, 0, 1, 2, 2 },
            { 0, 0, 0, 0, 1, 1, 0, 0, 2, 2, 1, 0, 2, 2, 1, 0 },
            { 0, 1, 2, 2, 0, 1, 2, 2, 0, 0, 1, 1, 0, 0, 0, 0 },
            { 0, 0, 1, 2, 0, 0, 1, 2, 1, 1, 2, 2, 2, 2, 2, 2 },
            { 0, 1, 1, 0, 1, 2, 2, 1, 1, 2, 2, 1, 0, 1, 1, 0 },
            { 0, 0, 0, 0, 0, 1, 1, 0, 1, 2, 2, 1, 1, 2, 2, 1 },
            { 0, 0, 2, 2, 1, 1, 0, 2, 1, 1, 0, 2, 0, 0, 2, 2 },
            { 0, 1, 1, 0, 0, 1, 1, 0, 2, 0, 0, 2, 2, 2, 2, 2 },
            { 0, 0, 1, 1, 0, 1, 2, 2, 0, 1, 2, 2, 0, 0, 1, 1 },
            { 0, 0, 0, 0, 2, 0, 0, 0, 2, 2, 1, 1, 2, 2, 2, 1 },
            { 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 2, 2, 1, 2, 2, 2 },
            { 0, 2, 2, 2, 0, 0, 2, 2, 0, 0, 1, 2, 0, 0, 1, 1 },
            { 0, 0, 1, 1, 0, 0, 1, 2, 0, 0, 2, 2, 0, 2, 2, 2 },
            { 0, 1, 2, 0, 0, 1, 2, 0, 0, 1, 2, 0, 0, 1, 2, 0 },
            { 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 0, 0, 0, 0 },
            { 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0 },
            { 0, 1, 2, 0, 2, 0, 1, 2, 1, 2, 0, 1, 0, 1, 2, 0 },
            { 0, 0, 1, 1, 2, 2, 0, 0, 1, 1, 2, 2, 0, 0, 1, 1 },
            { 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 0, 0, 0, 0, 1, 1 },
            { 0, 1, 0, 1, 0, 1, 0, 1, 2, 2, 2, 2, 2, 2, 2, 2 },
            { 0, 0, 0, 0, 0, 0, 0, 0, 2, 1, 2, 1, 2, 1, 2, 1 },
            { 0, 0, 2, 2, 1, 1, 2, 2, 0, 0, 2, 2, 1, 1, 2, 2 },
            { 0, 0, 2, 2, 0, 0, 1, 1, 0, 0, 2, 2, 0, 0, 1, 1 },
            { 0, 2, 2, 0, 1, 2, 2, 1, 0, 2, 2, 0, 1, 2, 2, 1 },
            { 0, 1, 0, 1, 2, 2, 2, 2, 2, 2, 2, 2, 0, 1, 0, 1 },
            { 0, 0, 0, 0, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1 },
            { 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 2, 2, 2, 2 },
            { 0, 2, 2, 2, 0, 1, 1, 1, 0, 2, 2, 2, 0, 1, 1, 1 },
            { 0, 0, 0, 2, 1, 1, 1, 2, 0, 0, 0, 2, 1, 1, 1, 2 },
            { 0, 0, 0, 0, 2, 1, 1, 2, 2, 1, 1, 2, 2, 1, 1, 2 },
            { 0, 2, 2, 2, 0, 1, 1, 1, 0, 1, 1, 1, 0, 2, 2, 2 },
            { 0, 0, 0, 2, 1, 1, 1, 2, 1, 1, 1, 2, 0, 0, 0, 2 },
            { 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 2, 2, 2, 2 },
            { 0, 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 2, 2, 1, 1, 2 },
            { 0, 1, 1, 0, 0, 1, 1, 0, 2, 2, 2, 2, 2, 2, 2, 2 },
            { 0, 0, 2, 2, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 2, 2 },
            { 0, 0, 2, 2, 1, 1, 2, 2, 1, 1, 2, 2, 0, 0, 2, 2 },
            { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 2 },
            { 0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 1 },
            { 0, 2, 2, 2, 1, 2, 2, 2, 0, 2, 2, 2, 1, 2, 2, 2 },
            { 0, 1, 0, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 },
            { 0, 1, 1, 1, 2, 0, 1, 1, 2, 2, 0, 1, 2, 2, 2, 0 },
        };

        /// <summary>
        /// BPTC anchor index values for the second subset of three-subset partitioning, by partition number.
        /// </summary>
        private static readonly byte[] BC7AnchorIndices32 =
        [
#pragma warning disable format
            3, 3, 15, 15, 8, 3, 15, 15,
            8, 8, 6, 6, 6, 5, 3, 3,
            3, 3, 8, 15, 3, 3, 6, 10,
            5, 8, 8, 6, 8, 5, 15, 15,
            8, 15, 3, 5, 6, 10, 8, 15,
            15, 3, 15, 5, 15, 15, 15, 15,
            3, 15, 5, 5, 5, 8, 5, 10,
            5, 10, 8, 13, 15, 12, 3, 3,
#pragma warning restore format
        ];

        /// <summary>
        /// BPTC anchor index values for the third subset of three-subset partitioning, by partition number.
        /// </summary>
        private static readonly byte[] BC7AnchorIndices33 =
        [
#pragma warning disable format
            15, 8, 8, 3, 15, 15, 3, 8,
            15, 15, 15, 15, 15, 15, 15, 8,
            15, 8, 15, 3, 15, 8, 15, 8,
            3, 15, 6, 10, 15, 15, 10, 8,
            15, 3, 15, 10, 10, 8, 9, 10,
            6, 15, 8, 15, 3, 6, 6, 8,
            15, 3, 15, 15, 15, 15, 15, 15,
            15, 15, 15, 15, 3, 15, 15, 8,
#pragma warning restore format
        ];
        private static readonly byte[] BC7IndLength = [3, 3, 2, 2, 2, 2, 4, 2];

        readonly int w;
        readonly int h;
        readonly TextureCodec decodeFlags;

        public DecodeBC7(int w, int h, TextureCodec codec)
        {
            this.w = w;
            this.h = h;
            decodeFlags = codec;
        }

        public void Decode(SKBitmap bitmap, Span<byte> input)
        {
            using var pixmap = bitmap.PeekPixels();
            var data = pixmap.GetPixelSpan<byte>();
            var pixels = MemoryMarshal.Cast<byte, Color>(data);

            var imageWidth = bitmap.Width;
            var rowBytes = bitmap.RowBytes;
            var blockCountX = (w + 3) / 4;
            var blockCountY = (h + 3) / 4;
            var offset = 0;

            for (var j = 0; j < blockCountY; j++)
            {
                for (var i = 0; i < blockCountX; i++)
                {
                    var block0 = BitConverter.ToUInt64(input.Slice(offset, sizeof(ulong)));
                    offset += sizeof(ulong);
                    var block64 = BitConverter.ToUInt64(input.Slice(offset, sizeof(ulong)));
                    offset += sizeof(ulong);

                    var m = 0;
                    for (; m < 8; m++)
                    {
                        if ((block0 >> m & 1) == 1)
                        {
                            break;
                        }
                    }

                    byte pb = 0;
                    byte rb = 0;
                    byte isb = 0;
                    var endpoints = new byte[6, 4];
                    byte epbits = 0;
                    byte spbits = 0;
                    ulong ib = 0;
                    ulong ib2 = 0;

                    if (m == 0)
                    {
                        pb = (byte)(block0 >> 1 & 0xF); //4bit
                    }
                    else if (m == 1 || m == 2 || m == 3 || m == 7)
                    {
                        pb = (byte)((block0 >> (m + 1)) & 0x3F); //6bit
                    }

                    void ReadEndpoints(int start, int ns2, int cb, int astart, int ab)
                    {
                        byte GetVal(int p, byte vm)
                        {
                            byte res = 0;
                            if (p < 64)
                            {
                                res = (byte)(block0 >> p & vm);
                                if (p + cb > 64)
                                {
                                    res |= (byte)(block64 << (64 - p) & vm);
                                }
                            }
                            else
                            {
                                res = (byte)(block64 >> (p - 64) & vm);
                            }

                            return res;
                        }

                        var mask = (byte)((0x1 << cb) - 1);
                        for (var c = 0; c < 3; c++)
                        {
                            for (var s = 0; s < ns2; s++)
                            {
                                var ofs = start + (cb * ((c * ns2) + s));
                                endpoints[s, c] = GetVal(ofs, mask);
                                if (m == 1)
                                {
                                    endpoints[s, c] = (byte)(endpoints[s, c] << 2 | ((spbits >> (s >> 1) & 1) << 1) | (endpoints[s, c] >> 5));
                                }
                                else if (m == 0 || m == 3 || m == 6 || m == 7)
                                {
                                    endpoints[s, c] = (byte)(endpoints[s, c] << (8 - cb) | ((epbits >> s & 1) << (7 - cb)) | (endpoints[s, c] >> ((cb * 2) - 7)));
                                }
                                else
                                {
                                    endpoints[s, c] = (byte)(endpoints[s, c] << (8 - cb) | (endpoints[s, c] >> ((cb * 2) - 8)));
                                }
                            }
                        }

                        if (ab != 0)
                        {
                            mask = (byte)((0x1 << ab) - 1);
                            for (var s = 0; s < ns2; s++)
                            {
                                var ofs = astart + (ab * s);
                                endpoints[s, 3] = GetVal(ofs, mask);
                                if (m == 6 || m == 7)
                                {
                                    endpoints[s, 3] = (byte)((endpoints[s, 3] << (8 - ab)) | ((epbits >> s & 1) << (7 - ab)) | (endpoints[s, 3] >> ((ab * 2) - 7)));
                                }
                                else
                                {
                                    endpoints[s, 3] = (byte)((endpoints[s, 3] << (8 - ab)) | (endpoints[s, 3] >> ((ab * 2) - 8)));
                                }
                            }
                        }
                    }

                    if (m == 0)
                    {
                        epbits = (byte)(block64 >> 13 & 0x3F);
                        ReadEndpoints(5, 6, 4, 0, 0);
                        ib = block64 >> 19;
                    }
                    else if (m == 1)
                    {
                        spbits = (byte)((block64 >> 16 & 1) | ((block64 >> 17 & 1) << 1));
                        ReadEndpoints(8, 4, 6, 0, 0);
                        ib = block64 >> 18;
                    }
                    else if (m == 2)
                    {
                        ReadEndpoints(9, 6, 5, 0, 0);
                        ib = block64 >> 35;
                    }
                    else if (m == 3)
                    {
                        epbits = (byte)(block64 >> 30 & 0xF);
                        ReadEndpoints(10, 4, 7, 0, 0);
                        ib = block64 >> 34;
                    }
                    else if (m == 4)
                    {
                        rb = (byte)(block0 >> 5 & 0x3);
                        isb = (byte)(block0 >> 7 & 0x1);
                        ReadEndpoints(8, 2, 5, 38, 6);
                        ib = (block0 >> 50) | (block64 << 14);
                        ib2 = block64 >> 17;
                    }
                    else if (m == 5)
                    {
                        rb = (byte)((block0 >> 6) & 0x3);
                        ReadEndpoints(8, 2, 7, 50, 8);
                        ib = block64 >> 2;
                        ib2 = block64 >> 33;
                    }
                    else if (m == 6)
                    {
                        epbits = (byte)((block0 >> 63) | ((block64 & 1) << 1));
                        ReadEndpoints(7, 2, 7, 49, 7);
                        ib = block64 >> 1;
                    }
                    else if (m == 7)
                    {
                        epbits = (byte)(block64 >> 30 & 0xF);
                        ReadEndpoints(14, 4, 5, 74, 5);
                        ib = block64 >> 34;
                    }

                    var ib2l = (m == 4) ? 3 : 2;
                    for (var by = 0; by < 4; by++)
                    {
                        for (var bx = 0; bx < 4; bx++)
                        {
                            var io = (by * 4) + bx;
                            var dataIndex = (((j * 4) + by) * rowBytes) + (((i * 4) + bx) * 4);
                            var pixelIndex = dataIndex / 4;

                            byte cweight = 0;
                            byte aweight = 0;
                            byte subset = 0;

                            var isAnchor = 0;
                            if (m == 0 || m == 2)
                            {//3 subsets
                                isAnchor = (io == 0 || io == BC7AnchorIndices32[pb] || io == BC7AnchorIndices33[pb]) ? 1 : 0;
                                subset = (byte)(BC7PartitionTable3[pb, io] * 2);
                            }
                            else if (m == 1 || m == 3 || m == 7)
                            {//2 subsets
                                subset = (byte)(BPTCPartitionTable2[pb, io] * 2);
                                isAnchor = (io == 0 || io == BPTCAnchorIndices2[pb]) ? 1 : 0;
                            }
                            else if (m == 4 || m == 5 || m == 6)
                            {//1 subset
                                isAnchor = (io == 0) ? 1 : 0;
                            }

                            if (m == 0 || m == 1)
                            {//3 bit
                                cweight = BPTCWeights3[ib & (0x7u >> isAnchor)];
                            }
                            else if (m == 6)
                            {//4 bit
                                cweight = BPTCWeights4[ib & (0xFu >> isAnchor)];
                            }
                            else
                            {//2 bit
                                cweight = BPTCWeights2[ib & (0x3u >> isAnchor)];
                            }

                            ib >>= BC7IndLength[m] - isAnchor;

                            if (m == 4)
                            {
                                aweight = BPTCWeights3[ib2 & (0x7u >> isAnchor)];
                                ib2 >>= ib2l - isAnchor;

                                if (isb == 1)
                                {
                                    var t = cweight;
                                    cweight = aweight;
                                    aweight = t;
                                }
                            }
                            else if (m == 5)
                            {
                                aweight = BPTCWeights2[ib2 & (0x3u >> isAnchor)];
                                ib2 >>= ib2l - isAnchor;
                            }
                            else if (m > 5)
                            {
                                aweight = cweight;
                            }

                            if ((i * 4) + bx >= imageWidth || data.Length <= dataIndex + 3)
                            {
                                continue;
                            }

                            pixels[pixelIndex].b = (byte)BPTCInterpolateFactor(cweight, endpoints[subset, 2], endpoints[subset + 1, 2]);
                            pixels[pixelIndex].g = (byte)BPTCInterpolateFactor(cweight, endpoints[subset, 1], endpoints[subset + 1, 1]);
                            pixels[pixelIndex].r = (byte)BPTCInterpolateFactor(cweight, endpoints[subset, 0], endpoints[subset + 1, 0]);

                            if (m < 4)
                            {
                                pixels[pixelIndex].a = byte.MaxValue;
                            }
                            else
                            {
                                pixels[pixelIndex].a = (byte)BPTCInterpolateFactor(aweight, endpoints[subset, 3], endpoints[subset + 1, 3]);

                                if ((m == 4 || m == 5) && rb != 0)
                                {
                                    // todo: figure out what channels this is trying to swizzle
                                    var t = data[dataIndex + 3];
                                    data[dataIndex + 3] = data[dataIndex + 3 - rb];
                                    data[dataIndex + 3 - rb] = t;
                                }
                            }

                            if ((decodeFlags & TextureCodec.HemiOctRB) != 0)
                            {
                                Common.Undo_HemiOct(ref pixels[pixelIndex]);
                            }
                        }
                    }
                }
            }
        }
    }
}
