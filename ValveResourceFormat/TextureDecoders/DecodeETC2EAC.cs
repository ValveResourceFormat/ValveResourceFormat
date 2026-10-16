// Credit to https://github.com/mafaca/UtinyRipper (C# port of https://github.com/Ishotihadus/mikunyan)

using System.Runtime.CompilerServices;
using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeETC2EAC : CommonETC, ITextureDecoder
    {
        private static readonly byte[] WriteOrderTableRev = [15, 11, 7, 3, 14, 10, 6, 2, 13, 9, 5, 1, 12, 8, 4, 0];

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

            var bcw = MathUtils.DivideRoundUp(width, 4);
            var bch = MathUtils.DivideRoundUp(height, 4);
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

        private void DecodeEtc2a8Block(ReadOnlySpan<byte> block)
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
                    c |= unchecked((uint)(Clamp255(@base + mul * CommonEAC.ModifierTable[table, l & 7]) << 24));
                    m_buf[WriteOrderTableRev[i]] = c;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Get6SwapedBytes(ReadOnlySpan<byte> block)
        {
            return block[7] | (uint)block[6] << 8 |
                    (uint)block[5] << 16 | (uint)block[4] << 24 |
                    (ulong)block[3] << 32 | (ulong)block[2] << 40;
        }
    }
}
