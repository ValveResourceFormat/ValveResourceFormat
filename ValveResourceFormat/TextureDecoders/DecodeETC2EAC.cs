// Credit to https://github.com/mafaca/Etc

using System.Runtime.CompilerServices;
using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeETC2EAC : CommonETC, ITextureDecoder
    {
        private static readonly byte[] WriteOrderTableRev = [15, 11, 7, 3, 14, 10, 6, 2, 13, 9, 5, 1, 12, 8, 4, 0];
        private static readonly sbyte[,] Etc2AlphaModTable =
        {
            {-3, -6,  -9, -15, 2, 5, 8, 14},
            {-3, -7, -10, -13, 2, 6, 9, 12},
            {-2, -5,  -8, -13, 1, 4, 7, 12},
            {-2, -4,  -6, -13, 1, 3, 5, 12},
            {-3, -6,  -8, -12, 2, 5, 7, 11},
            {-3, -7,  -9, -11, 2, 6, 8, 10},
            {-4, -7,  -8, -11, 3, 6, 7, 10},
            {-3, -5,  -8, -11, 2, 4, 7, 10},
            {-2, -6,  -8, -10, 1, 5, 7,  9},
            {-2, -5,  -8, -10, 1, 4, 7,  9},
            {-2, -4,  -8, -10, 1, 3, 7,  9},
            {-2, -5,  -7, -10, 1, 4, 6,  9},
            {-3, -4,  -7, -10, 2, 3, 6,  9},
            {-1, -2,  -3, -10, 0, 1, 2,  9},
            {-4, -6,  -8,  -9, 3, 5, 7,  8},
            {-3, -5,  -7,  -9, 2, 4, 6,  8}
        };

        readonly int width;
        readonly int height;

        public DecodeETC2EAC(int width, int height)
        {
            this.width = width;
            this.height = height;
        }

        public void Decode(SKBitmap res, Span<byte> input)
        {
            using var pixels = res.PeekPixels();
            var output = pixels.GetPixelSpan<uint>();
            var m_bufSpan = m_buf.AsSpan();

            var dstWidth = res.Width;
            var dstHeight = res.Height;

            var bcw = (width + 3) / 4;
            var bch = (height + 3) / 4;
            var blockSize = 16; // ETC2EAC blocks are 16 bytes (8 for alpha + 8 for color)

            for (int t = 0, d = 0; t < bch; t++)
            {
                for (var s = 0; s < bcw; s++, d += blockSize)
                {
                    if (s * 4 >= dstWidth)
                    {
                        continue;
                    }

                    DecodeEtc2Block(input.Slice(d + 8, 8));
                    DecodeEtc2a8Block(input.Slice(d, 8));

                    var blockWidth = Math.Min(4, width - s * 4);
                    var copyWidth = Math.Min(blockWidth, dstWidth - s * 4);

                    for (int i = 0, y = t * 4; i < 4 && y < dstHeight; i++, y++)
                    {
                        var dstIndex = y * dstWidth + s * 4;

                        if (dstIndex >= output.Length)
                        {
                            continue;
                        }

                        var availableSpace = output.Length - dstIndex;
                        var copySize = Math.Min(copyWidth, availableSpace);

                        if (copySize > 0)
                        {
                            m_bufSpan.Slice(i * 4, copySize).CopyTo(output.Slice(dstIndex, copySize));
                        }
                    }
                }
            }
        }

        private void DecodeEtc2a8Block(Span<byte> block)
        {
            int @base = block[0];
            int data1 = block[1];
            var mul = data1 >> 4;
            if (mul == 0)
            {
                for (var i = 0; i < 16; i++)
                {
                    var c = m_buf[WriteOrderTableRev[i]];
                    c &= 0x00FFFFFF;
                    c |= unchecked((uint)(@base << 24));
                    m_buf[WriteOrderTableRev[i]] = c;
                }
            }
            else
            {
                var table = data1 & 0xF;
                var l = Get6SwapedBytes(block);
                for (var i = 0; i < 16; i++, l >>= 3)
                {
                    var c = m_buf[WriteOrderTableRev[i]];
                    c &= 0x00FFFFFF;
                    c |= unchecked((uint)(Clamp255(@base + mul * Etc2AlphaModTable[table, l & 7]) << 24));
                    m_buf[WriteOrderTableRev[i]] = c;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Get6SwapedBytes(Span<byte> block)
        {
            return block[7] | (uint)block[6] << 8 |
                    (uint)block[5] << 16 | (uint)block[4] << 24 |
                    (ulong)block[3] << 32 | (ulong)block[2] << 40;
        }
    }
}
