//Copyright 2020 lewa_j [https://github.com/lewa-j]
using System;
using System.IO;
using SkiaSharp;

namespace BPTC
{
    public static class BPTCDecoders
    {
        //https://www.khronos.org/registry/DataFormat/specs/1.3/dataformat.1.3.html#BPTC

        private static readonly byte[,] BPTCPartitionTable2 = new byte[64, 16]
        {//Partition table for 2-subset BPTC, with the 4×4 block of values for each partition number
            { 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1 },
            { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1 },
            { 0, 1, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1 },
            { 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 1, 1, 1 },
            { 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 1 },
            { 0, 0, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1 },
            { 0, 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1 },
            { 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 1 },
            { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 1 },
            { 0, 0, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
            { 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 1, 1, 1, 1, 1, 1 },
            { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 1, 1 },
            { 0, 0, 0, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
            { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1 },
            { 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
            { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1 },
            { 0, 0, 0, 0, 1, 0, 0, 0, 1, 1, 1, 0, 1, 1, 1, 1 },
            { 0, 1, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0 },
            { 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 1, 1, 0 },
            { 0, 1, 1, 1, 0, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0 },
            { 0, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0 },
            { 0, 0, 0, 0, 1, 0, 0, 0, 1, 1, 0, 0, 1, 1, 1, 0 },
            { 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 1, 0, 0 },
            { 0, 1, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 0, 1 },
            { 0, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0 },
            { 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1, 0, 0 },
            { 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0 },
            { 0, 0, 1, 1, 0, 1, 1, 0, 0, 1, 1, 0, 1, 1, 0, 0 },
            { 0, 0, 0, 1, 0, 1, 1, 1, 1, 1, 1, 0, 1, 0, 0, 0 },
            { 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0 },
            { 0, 1, 1, 1, 0, 0, 0, 1, 1, 0, 0, 0, 1, 1, 1, 0 },
            { 0, 0, 1, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 1, 0, 0 },
            { 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1 },
            { 0, 0, 0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 1 },
            { 0, 1, 0, 1, 1, 0, 1, 0, 0, 1, 0, 1, 1, 0, 1, 0 },
            { 0, 0, 1, 1, 0, 0, 1, 1, 1, 1, 0, 0, 1, 1, 0, 0 },
            { 0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 1, 0, 0 },
            { 0, 1, 0, 1, 0, 1, 0, 1, 1, 0, 1, 0, 1, 0, 1, 0 },
            { 0, 1, 1, 0, 1, 0, 0, 1, 0, 1, 1, 0, 1, 0, 0, 1 },
            { 0, 1, 0, 1, 1, 0, 1, 0, 1, 0, 1, 0, 0, 1, 0, 1 },
            { 0, 1, 1, 1, 0, 0, 1, 1, 1, 1, 0, 0, 1, 1, 1, 0 },
            { 0, 0, 0, 1, 0, 0, 1, 1, 1, 1, 0, 0, 1, 0, 0, 0 },
            { 0, 0, 1, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 1, 0, 0 },
            { 0, 0, 1, 1, 1, 0, 1, 1, 1, 1, 0, 1, 1, 1, 0, 0 },
            { 0, 1, 1, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 1, 1, 0 },
            { 0, 0, 1, 1, 1, 1, 0, 0, 1, 1, 0, 0, 0, 0, 1, 1 },
            { 0, 1, 1, 0, 0, 1, 1, 0, 1, 0, 0, 1, 1, 0, 0, 1 },
            { 0, 0, 0, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 0, 0, 0 },
            { 0, 1, 0, 0, 1, 1, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0 },
            { 0, 0, 1, 0, 0, 1, 1, 1, 0, 0, 1, 0, 0, 0, 0, 0 },
            { 0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 1, 1, 0, 0, 1, 0 },
            { 0, 0, 0, 0, 0, 1, 0, 0, 1, 1, 1, 0, 0, 1, 0, 0 },
            { 0, 1, 1, 0, 1, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 1 },
            { 0, 0, 1, 1, 0, 1, 1, 0, 1, 1, 0, 0, 1, 0, 0, 1 },
            { 0, 1, 1, 0, 0, 0, 1, 1, 1, 0, 0, 1, 1, 1, 0, 0 },
            { 0, 0, 1, 1, 1, 0, 0, 1, 1, 1, 0, 0, 0, 1, 1, 0 },
            { 0, 1, 1, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 0, 0, 1 },
            { 0, 1, 1, 0, 0, 0, 1, 1, 0, 0, 1, 1, 1, 0, 0, 1 },
            { 0, 1, 1, 1, 1, 1, 1, 0, 1, 0, 0, 0, 0, 0, 0, 1 },
            { 0, 0, 0, 1, 1, 0, 0, 0, 1, 1, 1, 0, 0, 1, 1, 1 },
            { 0, 0, 0, 0, 1, 1, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1 },
            { 0, 0, 1, 1, 0, 0, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0 },
            { 0, 0, 1, 0, 0, 0, 1, 0, 1, 1, 1, 0, 1, 1, 1, 0 },
            { 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1, 1, 0, 1, 1, 1 },
        };
        private static readonly byte[] BPTCAnchorIndices2 = new byte[64]
        {// BPTC anchor index values for the second subset of two-subset partitioning, by partition number
            15, 15, 15, 15, 15, 15, 15, 15,
            15, 15, 15, 15, 15, 15, 15, 15,
            15, 2, 8, 2, 2, 8, 8, 15,
            2, 8, 2, 2, 8, 8, 2, 2,
            15, 15, 6,  8, 2, 8, 15, 15,
            2, 8, 2, 2, 2, 15, 15, 6,
            6, 2, 6, 8, 15, 15, 2, 2,
            15, 15, 15, 15, 15, 2, 2, 15,
        };
        private static readonly byte[] BPTCWeights2 = { 0, 21, 43, 64 };
        private static readonly byte[] BPTCWeights3 = { 0, 9, 19, 27, 47, 46, 55, 64 };
        private static readonly byte[] BPTCWeights4 = { 0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64 };

        private static ushort BPTCInterpolateFactor(int weight, int e0, int e1)
        {
            return (ushort)((((64 - weight) * e0) + (weight * e1) + 32) >> 6);
        }

        private static short SignExtend(ulong v, int bits)
        {
            if (((v >> (bits - 1)) & 1) == 1)
            {
                v |= (uint)(-1L << bits);
            }

            return (short)v;
        }

        public static SKBitmap UncompressBC6H(BinaryReader r, int w, int h)
        {
            var imageInfo = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            var data = imageInfo.PeekPixels().GetPixelSpan<byte>();
            var blockCountX = (w + 3) / 4;
            var blockCountY = (h + 3) / 4;
            var rowBytes = imageInfo.RowBytes;

            for (var j = 0; j < blockCountY; j++)
            {
                for (var i = 0; i < blockCountX; i++)
                {
                    ulong block0 = r.ReadUInt64();
                    ulong block64 = r.ReadUInt64();
                    ulong Bit(int p)
                    {
                        return (byte)(p < 64 ? block0 >> p & 1 : block64 >> (p - 64) & 1);
                    }

                    byte m = (byte)(block0 & 0x3);
                    if (m >= 2)
                    {
                        m = (byte)(block0 & 0x1F);
                    }

                    int epb = 0;
                    ushort[,] endpoints = new ushort[4, 3];
                    short[,] deltas = new short[3, 3];
                    byte pb = 0;
                    ulong ib = 0;

                    if (m == 0)
                    {
                        epb = 10;
                        endpoints[0, 0] = (ushort)(block0 >> 5 & 0x3FF);
                        endpoints[0, 1] = (ushort)(block0 >> 15 & 0x3FF);
                        endpoints[0, 2] = (ushort)(block0 >> 25 & 0x3FF);
                        deltas[0, 0] = SignExtend(block0 >> 35 & 0x1F, 5);
                        deltas[0, 1] = SignExtend(block0 >> 45 & 0x1F, 5);
                        deltas[0, 2] = SignExtend(block0 >> 55 & 0x1F, 5);
                        deltas[1, 0] = SignExtend(block64 >> 1 & 0x1F, 5);
                        deltas[1, 1] = SignExtend((block0 >> 41 & 0xF) | (Bit(2) << 4), 5);
                        deltas[1, 2] = SignExtend((block0 >> 61 & 0x7) | (Bit(64) << 3) | (Bit(3) << 4), 5);
                        deltas[2, 0] = SignExtend(block64 >> 7 & 0x1F, 5);
                        deltas[2, 1] = SignExtend((block0 >> 51 & 0xF) | (Bit(40) << 4), 5);
                        deltas[2, 2] = SignExtend(Bit(50) | (Bit(60) << 1) | (Bit(70) << 2) | (Bit(76) << 3) | (Bit(4) << 4), 5);
                    }
                    else if (m == 1)
                    {
                        epb = 7;
                        endpoints[0, 0] = (ushort)(block0 >> 5 & 0x7F);
                        endpoints[0, 1] = (ushort)(block0 >> 15 & 0x7F);
                        endpoints[0, 2] = (ushort)(block0 >> 25 & 0x7F);
                        deltas[0, 0] = SignExtend(block0 >> 35 & 0x3F, 6);
                        deltas[0, 1] = SignExtend(block0 >> 45 & 0x3F, 6);
                        deltas[0, 2] = SignExtend(block0 >> 55 & 0x3F, 6);
                        deltas[1, 0] = SignExtend(block64 >> 1 & 0x3F, 6);
                        deltas[1, 1] = SignExtend((block0 >> 41 & 0xF) | (Bit(24) << 4) | (Bit(2) << 5), 6);
                        deltas[1, 2] = SignExtend((block0 >> 61 & 0x7) | (Bit(64) << 3) | (Bit(14) << 4) | (Bit(22) << 5), 6);
                        deltas[2, 0] = SignExtend(block64 >> 7 & 0x3F, 6);
                        deltas[2, 1] = SignExtend((block0 >> 51 & 0xF) | ((block0 >> 3 & 0x3) << 4), 6);
                        deltas[2, 2] = SignExtend((block0 >> 12 & 0x3) | (Bit(23) << 2) | (Bit(32) << 3) | (Bit(34) << 4) | (Bit(33) << 5), 6);
                    }
                    else if (m == 2)
                    {
                        epb = 11;
                        endpoints[0, 0] = (ushort)((block0 >> 5 & 0x3FF) | (Bit(40) << 10));
                        endpoints[0, 1] = (ushort)((block0 >> 15 & 0x3FF) | (Bit(49) << 10));
                        endpoints[0, 2] = (ushort)((block0 >> 25 & 0x3FF) | (Bit(59) << 10));
                        deltas[0, 0] = SignExtend(block0 >> 35 & 0x1F, 5);
                        deltas[0, 1] = SignExtend(block0 >> 45 & 0xF, 4);
                        deltas[0, 2] = SignExtend(block0 >> 55 & 0xF, 4);
                        deltas[1, 0] = SignExtend(block64 >> 1 & 0x1F, 5);
                        deltas[1, 1] = SignExtend(block0 >> 41 & 0xF, 4);
                        deltas[1, 2] = SignExtend((block0 >> 61 & 0x7) | (Bit(64) << 3), 4);
                        deltas[2, 0] = SignExtend(block64 >> 7 & 0x1F, 5);
                        deltas[2, 1] = SignExtend(block0 >> 51 & 0xF, 4);
                        deltas[2, 2] = SignExtend(Bit(50) | (Bit(60) << 1) | (Bit(70) << 2) | (Bit(76) << 3), 4);
                    }
                    else if (m == 6)
                    {
                        epb = 11;
                        endpoints[0, 0] = (ushort)((block0 >> 5 & 0x3FF) | (Bit(39) << 10));
                        endpoints[0, 1] = (ushort)((block0 >> 15 & 0x3FF) | (Bit(50) << 10));
                        endpoints[0, 2] = (ushort)((block0 >> 25 & 0x3FF) | (Bit(59) << 10));
                        deltas[0, 0] = SignExtend(block0 >> 35 & 0xF, 4);
                        deltas[0, 1] = SignExtend(block0 >> 45 & 0x1F, 5);
                        deltas[0, 2] = SignExtend(block0 >> 55 & 0xF, 4);
                        deltas[1, 0] = SignExtend(block64 >> 1 & 0xF, 4);
                        deltas[1, 1] = SignExtend((block0 >> 41 & 0xF) | (Bit(75) << 4), 5);
                        deltas[1, 2] = SignExtend((block0 >> 61 & 0x7) | (Bit(64) << 3), 4);
                        deltas[2, 0] = SignExtend(block64 >> 7 & 0xF, 4);
                        deltas[2, 1] = SignExtend((block0 >> 51 & 0xF) | (Bit(40) << 4), 5);
                        deltas[2, 2] = SignExtend(Bit(69) | (Bit(60) << 1) | (Bit(70) << 2) | (Bit(76) << 3), 4);
                    }
                    else if (m == 10)
                    {
                        epb = 11;
                        endpoints[0, 0] = (ushort)((block0 >> 5 & 0x3FF) | (Bit(39) << 10));
                        endpoints[0, 1] = (ushort)((block0 >> 15 & 0x3FF) | (Bit(49) << 10));
                        endpoints[0, 2] = (ushort)((block0 >> 25 & 0x3FF) | (Bit(60) << 10));
                        deltas[0, 0] = SignExtend(block0 >> 35 & 0xF, 4);
                        deltas[0, 1] = SignExtend(block0 >> 45 & 0xF, 4);
                        deltas[0, 2] = SignExtend(block0 >> 55 & 0x1F, 5);
                        deltas[1, 0] = SignExtend(block64 >> 1 & 0xF, 4);
                        deltas[1, 1] = SignExtend(block0 >> 41 & 0xF, 4);
                        deltas[1, 2] = SignExtend((block0 >> 61 & 0x7) | (Bit(64) << 3) | (Bit(40) << 4), 5);
                        deltas[2, 0] = SignExtend(block64 >> 7 & 0xF, 4);
                        deltas[2, 1] = SignExtend(block0 >> 51 & 0xF, 4);
                        deltas[2, 2] = SignExtend(Bit(50) | (Bit(69) << 1) | (Bit(70) << 2) | (Bit(76) << 3) | (Bit(75) << 3), 5);
                    }
                    else if (m == 14)
                    {
                        epb = 9;
                        endpoints[0, 0] = (ushort)(block0 >> 5 & 0x1FF);
                        endpoints[0, 1] = (ushort)(block0 >> 15 & 0x1FF);
                        endpoints[0, 2] = (ushort)(block0 >> 25 & 0x1FF);
                        deltas[0, 0] = SignExtend(block0 >> 35 & 0x1F, 5);
                        deltas[0, 1] = SignExtend(block0 >> 45 & 0x1F, 5);
                        deltas[0, 2] = SignExtend(block0 >> 55 & 0x1F, 5);
                        deltas[1, 0] = SignExtend(block64 >> 1 & 0x1F, 5);
                        deltas[1, 1] = SignExtend((block0 >> 41 & 0xF) | (Bit(24) << 4), 5);
                        deltas[1, 2] = SignExtend((block0 >> 61 & 0x7) | (Bit(64) << 3) | (Bit(14) << 4), 5);
                        deltas[2, 0] = SignExtend(block64 >> 7 & 0x1F, 5);
                        deltas[2, 1] = SignExtend((block0 >> 51 & 0xF) | (Bit(40) << 4), 5);
                        deltas[2, 2] = SignExtend(Bit(50) | (Bit(60) << 1) | (Bit(70) << 2) | (Bit(76) << 3) | (Bit(34) << 4), 5);
                    }
                    else if (m == 18)
                    {
                        epb = 8;
                        endpoints[0, 0] = (ushort)(block0 >> 5 & 0xFF);
                        endpoints[0, 1] = (ushort)(block0 >> 15 & 0xFF);
                        endpoints[0, 2] = (ushort)(block0 >> 25 & 0xFF);
                        deltas[0, 0] = SignExtend(block0 >> 35 & 0x3F, 6);
                        deltas[0, 1] = SignExtend(block0 >> 45 & 0x1F, 5);
                        deltas[0, 2] = SignExtend(block0 >> 55 & 0x1F, 5);
                        deltas[1, 0] = SignExtend(block64 >> 1 & 0x3F, 6);
                        deltas[1, 1] = SignExtend((block0 >> 41 & 0xF) | (Bit(24) << 4), 5);
                        deltas[1, 2] = SignExtend((block0 >> 61 & 0x7) | (Bit(64) << 3) | (Bit(14) << 4), 5);
                        deltas[2, 0] = SignExtend(block64 >> 7 & 0x3F, 6);
                        deltas[2, 1] = SignExtend((block0 >> 51 & 0xF) | (Bit(13) << 4), 5);
                        deltas[2, 2] = SignExtend(Bit(50) | (Bit(60) << 1) | (Bit(23) << 2) | (Bit(33) << 3) | (Bit(34) << 4), 5);
                    }
                    else if (m == 22)
                    {
                        epb = 8;
                        endpoints[0, 0] = (ushort)(block0 >> 5 & 0xFF);
                        endpoints[0, 1] = (ushort)(block0 >> 15 & 0xFF);
                        endpoints[0, 2] = (ushort)(block0 >> 25 & 0xFF);
                        deltas[0, 0] = SignExtend(block0 >> 35 & 0x1F, 5);
                        deltas[0, 1] = SignExtend(block0 >> 45 & 0x3F, 6);
                        deltas[0, 2] = SignExtend(block0 >> 55 & 0x1F, 5);
                        deltas[1, 0] = SignExtend(block64 >> 1 & 0x1F, 5);
                        deltas[1, 1] = SignExtend((block0 >> 41 & 0xF) | (Bit(24) << 4) | (Bit(23) << 5), 6);
                        deltas[1, 2] = SignExtend((block0 >> 61 & 0x7) | (Bit(64) << 3) | (Bit(14) << 4), 5);
                        deltas[2, 0] = SignExtend(block64 >> 7 & 0x1F, 5);
                        deltas[2, 1] = SignExtend((block0 >> 51 & 0xF) | (Bit(40) << 4) | (Bit(33) << 5), 6);
                        deltas[2, 2] = SignExtend(Bit(13) | (Bit(60) << 1) | (Bit(70) << 2) | (Bit(76) << 3) | (Bit(34) << 4), 5);
                    }
                    else if (m == 26)
                    {
                        epb = 8;
                        endpoints[0, 0] = (ushort)(block0 >> 5 & 0xFF);
                        endpoints[0, 1] = (ushort)(block0 >> 15 & 0xFF);
                        endpoints[0, 2] = (ushort)(block0 >> 25 & 0xFF);
                        deltas[0, 0] = SignExtend(block0 >> 35 & 0x1F, 5);
                        deltas[0, 1] = SignExtend(block0 >> 45 & 0x1F, 5);
                        deltas[0, 2] = SignExtend(block0 >> 55 & 0x3F, 6);
                        deltas[1, 0] = SignExtend(block64 >> 1 & 0x1F, 5);
                        deltas[1, 1] = SignExtend((block0 >> 41 & 0xF) | (Bit(24) << 4), 5);
                        deltas[1, 2] = SignExtend((block0 >> 61 & 0x7) | (Bit(64) << 3) | (Bit(14) << 4) | (Bit(23) << 5), 6);
                        deltas[2, 0] = SignExtend(block64 >> 7 & 0x1F, 5);
                        deltas[2, 1] = SignExtend((block0 >> 51 & 0xF) | (Bit(40) << 4), 5);
                        deltas[2, 2] = SignExtend(Bit(50) | (Bit(13) << 1) | (Bit(70) << 2) | (Bit(76) << 3) | (Bit(34) << 4) | (Bit(33) << 5), 6);
                    }
                    else if (m == 30)
                    {
                        epb = 6;
                        endpoints[0, 0] = (ushort)(block0 >> 5 & 0x3F);
                        endpoints[0, 1] = (ushort)(block0 >> 15 & 0x3F);
                        endpoints[0, 2] = (ushort)(block0 >> 25 & 0x3F);
                        endpoints[1, 0] = (ushort)(block0 >> 35 & 0x3F);
                        endpoints[1, 1] = (ushort)(block0 >> 45 & 0x3F);
                        endpoints[1, 2] = (ushort)(block0 >> 55 & 0x3F);
                        endpoints[2, 0] = (ushort)(block64 >> 1 & 0x3F);
                        endpoints[2, 1] = (ushort)((block0 >> 41 & 0xF) | (Bit(24) << 4) | (Bit(21) << 5));
                        endpoints[2, 2] = (ushort)((block0 >> 61 & 0x3) | (Bit(64) << 3) | (Bit(14) << 4) | (Bit(22) << 5));
                        endpoints[3, 0] = (ushort)(block64 >> 7 & 0x3F);
                        endpoints[3, 1] = (ushort)((block0 >> 51 & 0xF) | (Bit(11) << 4) | (Bit(31) << 5));
                        endpoints[3, 2] = (ushort)((block0 >> 12 & 0x3) | (Bit(23) << 2) | (Bit(32) << 3) | (Bit(34) << 4) | (Bit(33) << 5));
                    }
                    else if (m == 3)
                    {
                        epb = 10;
                        endpoints[0, 0] = (ushort)(block0 >> 5 & 0x3FF);
                        endpoints[0, 1] = (ushort)(block0 >> 15 & 0x3FF);
                        endpoints[0, 2] = (ushort)(block0 >> 25 & 0x3FF);
                        endpoints[1, 0] = (ushort)(block0 >> 35 & 0x3FF);
                        endpoints[1, 1] = (ushort)(block0 >> 45 & 0x3FF);
                        endpoints[1, 2] = (ushort)((block0 >> 55 & 0x1FF) | ((block64 & 0x1) << 9));
                    }
                    else if (m == 7)
                    {
                        epb = 11;
                        endpoints[0, 0] = (ushort)((block0 >> 5 & 0x3FF) | (Bit(44) << 10));
                        endpoints[0, 1] = (ushort)((block0 >> 15 & 0x3FF) | (Bit(54) << 10));
                        endpoints[0, 2] = (ushort)((block0 >> 25 & 0x3FF) | (Bit(64) << 10));
                        deltas[0, 0] = SignExtend(block0 >> 35 & 0x1FF, 9);
                        deltas[0, 1] = SignExtend(block0 >> 45 & 0x1FF, 9);
                        deltas[0, 2] = SignExtend(block0 >> 55 & 0x1FF, 9);
                    }
                    else if (m == 11)
                    {
                        epb = 12;
                        endpoints[0, 0] = (ushort)((block0 >> 5 & 0x3FF) | (Bit(44) << 10) | (Bit(43) << 11));
                        endpoints[0, 1] = (ushort)((block0 >> 15 & 0x3FF) | (Bit(54) << 10) | (Bit(53) << 11));
                        endpoints[0, 2] = (ushort)((block0 >> 25 & 0x3FF) | (Bit(64) << 10) | (Bit(63) << 11));
                        deltas[0, 0] = SignExtend((block0 >> 35) & 0xFF, 8);
                        deltas[0, 1] = SignExtend((block0 >> 45) & 0xFF, 8);
                        deltas[0, 2] = SignExtend((block0 >> 55) & 0xFF, 8);
                    }
                    else if (m == 15)
                    {
                        epb = 16;
                        endpoints[0, 0] = (ushort)((block0 >> 5 & 0x3FF) | (Bit(44) << 10) | (Bit(43) << 11) | (Bit(42) << 12) | (Bit(41) << 13) | (Bit(40) << 14) | (Bit(39) << 15));
                        endpoints[0, 1] = (ushort)((block0 >> 15 & 0x3FF) | (Bit(54) << 10) | (Bit(53) << 11) | (Bit(52) << 12) | (Bit(51) << 13) | (Bit(50) << 14) | (Bit(49) << 15));
                        endpoints[0, 2] = (ushort)((block0 >> 25 & 0x3FF) | (Bit(64) << 10) | (Bit(63) << 11) | (Bit(62) << 12) | (Bit(61) << 13) | (Bit(60) << 14) | (Bit(59) << 15));
                        deltas[0, 0] = SignExtend((block0 >> 35) & 0xFF, 4);
                        deltas[0, 1] = SignExtend((block0 >> 45) & 0xFF, 4);
                        deltas[0, 2] = SignExtend((block0 >> 55) & 0xFF, 4);
                    }

                    ushort epm = (ushort)((1U << epb) - 1);

                    if (m != 3 && m != 7 && m != 11 && m != 15)
                    {
                        pb = (byte)(block64 >> 13 & 0x1F);
                        ib = block64 >> 18;
                    }
                    else
                    {
                        ib = block64 >> 1;
                    }

                    ushort Unquantize(ushort e)
                    {
                        if (epb >= 15)
                        {
                            return e;
                        }
                        else if (e == 0)
                        {
                            return 0;
                        }
                        else if (e == epm)
                        {
                            return 0xFFFF;
                        }

                        return (ushort)(((e << 15) + 0x4000) >> (epb - 1));
                    }

                    if (m != 3 && m != 30)
                    {
                        for (int d = 0; d < 3; d++)
                        {
                            for (int e = 0; e < 3; e++)
                            {
                                endpoints[d + 1, e] = (ushort)((endpoints[0, e] + deltas[d, e]) & epm);
                            }
                        }
                    }

                    for (int s = 0; s < 4; s++)
                    {
                        for (int e = 0; e < 3; e++)
                        {
                            endpoints[s, e] = Unquantize(endpoints[s, e]);
                        }
                    }

                    for (int by = 0; by < 4; by++)
                    {
                        for (int bx = 0; bx < 4; bx++)
                        {
                            var pixelIndex = (((j * 4) + by) * rowBytes) + (((i * 4) + bx) * 4);
                            int io = (by * 4) + bx;

                            int isAnchor = 0;
                            byte cweight = 0;
                            byte subset = 0;
                            if (m == 3 || m == 7 || m == 11 || m == 15)
                            {
                                isAnchor = (io == 0) ? 1 : 0;
                                cweight = BPTCWeights4[ib & 0xFu >> isAnchor];
                                ib >>= 4 - isAnchor;
                            }
                            else
                            {
                                subset = (byte)(BPTCPartitionTable2[pb, io] * 2);
                                isAnchor = (io == 0 || io == BPTCAnchorIndices2[pb]) ? 1 : 0;
                                cweight = BPTCWeights3[ib & 0x7u >> isAnchor];
                                ib >>= 3 - isAnchor;
                            }

                            for (int e = 0; e < 3; e++)
                            {
                                ushort factor = BPTCInterpolateFactor(cweight, endpoints[subset, e], endpoints[subset + 1, e]);
                                //gamma correction and mul 4
                                factor = (ushort)Math.Min(0xFFFF, Math.Pow(factor / (float)((1U << 16) - 1), 2.2f) * ((1U << 16) - 1) * 4);
                                data[pixelIndex + 2 - e] = (byte)(factor >> 8);
                            }

                            data[pixelIndex + 3] = byte.MaxValue;
                        }
                    }
                }
            }

            return imageInfo;
        }

        private static readonly byte[,] BC7PartitionTable3 = new byte[64, 16]
        {//Partition table for 3-subset BPTC, with the 4×4 block of values for each partition number
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
        private static readonly byte[] BC7AnchorIndices32 = new byte[64]
        {//BPTC anchor index values for the second subset of three-subset partitioning, by partition number
            3, 3, 15, 15, 8, 3, 15, 15,
            8, 8, 6, 6, 6, 5, 3, 3,
            3, 3, 8, 15, 3, 3, 6, 10,
            5, 8, 8, 6, 8, 5, 15, 15,
            8, 15, 3, 5, 6, 10, 8, 15,
            15, 3, 15, 5, 15, 15, 15, 15,
            3, 15, 5, 5, 5, 8, 5, 10,
            5, 10, 8, 13, 15, 12, 3, 3,
        };
        private static readonly byte[] BC7AnchorIndices33 = new byte[64]
        {//BPTC anchor index values for the third subset of three-subset partitioning, by partition number
            15, 8, 8, 3, 15, 15, 3, 8,
            15, 15, 15, 15, 15, 15, 15, 8,
            15, 8, 15, 3, 15, 8, 15, 8,
            3, 15, 6, 10, 15, 15, 10, 8,
            15, 3, 15, 10, 10, 8, 9, 10,
            6, 15, 8, 15, 3, 6, 6, 8,
            15, 3, 15, 15, 15, 15, 15, 15,
            15, 15, 15, 15, 3, 15, 15, 8,
        };
        private static readonly byte[] BC7IndLength = { 3, 3, 2, 2, 2, 2, 4, 2 };

        public static SKBitmap UncompressBC7(BinaryReader r, int w, int h, bool hemiOctRB, bool invert)
        {
            var imageInfo = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            var data = imageInfo.PeekPixels().GetPixelSpan<byte>();
            var blockCountX = (w + 3) / 4;
            var blockCountY = (h + 3) / 4;

            for (var j = 0; j < blockCountY; j++)
            {
                for (var i = 0; i < blockCountX; i++)
                {
                    ulong block0 = r.ReadUInt64();
                    ulong block64 = r.ReadUInt64();
                    int m = 0;
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
                    byte[,] endpoints = new byte[6, 4];
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

                        byte mask = (byte)((0x1 << cb) - 1);
                        for (int c = 0; c < 3; c++)
                        {
                            for (int s = 0; s < ns2; s++)
                            {
                                int ofs = start + (cb * ((c * ns2) + s));
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
                            for (int s = 0; s < ns2; s++)
                            {
                                int ofs = astart + (ab * s);
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

                    int ib2l = (m == 4) ? 3 : 2;
                    for (int by = 0; by < 4; by++)
                    {
                        for (int bx = 0; bx < 4; bx++)
                        {
                            int io = (by * 4) + bx;
                            var pixelIndex = (((j * 4) + by) * imageInfo.RowBytes) + (((i * 4) + bx) * 4);

                            byte cweight = 0;
                            byte aweight = 0;
                            byte subset = 0;

                            int isAnchor = 0;
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
                                    byte t = cweight;
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

                            data[pixelIndex] = (byte)BPTCInterpolateFactor(cweight, endpoints[subset, 2], endpoints[subset + 1, 2]);
                            data[pixelIndex + 1] = (byte)BPTCInterpolateFactor(cweight, endpoints[subset, 1], endpoints[subset + 1, 1]);
                            data[pixelIndex + 2] = (byte)BPTCInterpolateFactor(cweight, endpoints[subset, 0], endpoints[subset + 1, 0]);

                            if (m < 4)
                            {
                                data[pixelIndex + 3] = byte.MaxValue;
                            }
                            else
                            {
                                data[pixelIndex + 3] = (byte)BPTCInterpolateFactor(aweight, endpoints[subset, 3], endpoints[subset + 1, 3]);

                                if ((m == 4 || m == 5) && rb != 0)
                                {
                                    byte t = data[pixelIndex + 3];
                                    data[pixelIndex + 3] = data[pixelIndex + 3 - rb];
                                    data[pixelIndex + 3 - rb] = t;
                                }
                            }

                            if (hemiOctRB)
                            {
                                float nx = ((data[pixelIndex + 2] + data[pixelIndex + 1]) / 255.0f) - 1.003922f;
                                float ny = (data[pixelIndex + 2] - data[pixelIndex + 1]) / 255.0f;
                                float nz = 1 - Math.Abs(nx) - Math.Abs(ny);

                                float l = (float)Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
                                data[pixelIndex + 3] = data[pixelIndex + 0]; //b to alpha
                                data[pixelIndex + 2] = (byte)(((nx / l * 0.5f) + 0.5f) * 255);
                                data[pixelIndex + 1] = (byte)(((ny / l * 0.5f) + 0.5f) * 255);
                                data[pixelIndex + 0] = (byte)(((nz / l * 0.5f) + 0.5f) * 255);
                            }

                            if (invert)
                            {
                                data[pixelIndex + 1] = (byte)(~data[pixelIndex + 1]);  // LegacySource1InvertNormals
                            }
                        }
                    }
                }
            }

            return imageInfo;
        }
    }
}
