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
            var imageWidth = res.Width;

            var bcw = (width + 3) / 4;
            var bch = (height + 3) / 4;
            var clen_last = (width + 3) % 4 + 1;
            var d = 0;

            for (var t = 0; t < bch; t++)
            {
                for (var s = 0; s < bcw; s++, d += 8)
                {
                    DecodeEtc2Block(input.Slice(d, 8));
                    var clen = s < bcw - 1 ? 4 : clen_last;
                    for (int i = 0, y = t * 4; i < 4 && y < height; i++, y++)
                    {
                        var dataIndex = y * imageWidth + s * 4;

                        if (dataIndex > output.Length - clen)
                        {
                            // This is silly but required when decoding into a nonpow2 bitmap
                            continue;
                        }

                        m_bufSpan.Slice(i * 4, clen).CopyTo(output.Slice(y * width + s * 4, clen));
                    }
                }
            }
        }
    }
}
