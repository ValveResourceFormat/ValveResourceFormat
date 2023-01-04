// Credit to https://github.com/mafaca/Etc

using System;
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
            var output = pixels.GetPixelSpan<byte>();

            int bcw = (width + 3) / 4;
            int bch = (height + 3) / 4;
            int clen_last = (width + 3) % 4 + 1;
            int d = 0;

            for (int t = 0; t < bch; t++)
            {
                for (int s = 0; s < bcw; s++, d += 8)
                {
                    DecodeEtc2Block(input.Slice(d, 8));
                    int clen = (s < bcw - 1 ? 4 : clen_last) * 4;
                    for (int i = 0, y = t * 4; i < 4 && y < height; i++, y++)
                    {
                        // TODO: This is rather silly
                        var bytes = new byte[clen];
                        Buffer.BlockCopy(m_buf, i * 4 * 4, bytes, 0, clen);
                        bytes.CopyTo(output.Slice(y * 4 * width + s * 4 * 4, clen));
                    }
                }
            }
        }
    }
}
