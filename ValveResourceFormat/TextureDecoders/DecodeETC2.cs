// Credit to https://github.com/mafaca/Etc

using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeETC2 : CommonETC, ITextureDecoder
    {
        readonly int width;
        readonly int height;

        public DecodeETC2(int width, int height)
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
            var blockSize = 8; // ETC2 blocks are 8 bytes

            for (int t = 0, d = 0; t < bch; t++)
            {
                for (var s = 0; s < bcw; s++, d += blockSize)
                {
                    if (s * 4 >= dstWidth)
                    {
                        continue;
                    }

                    DecodeEtc2Block(input.Slice(d, blockSize));

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
    }
}
