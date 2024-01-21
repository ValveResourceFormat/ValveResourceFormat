using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeATI1N : ITextureDecoder
    {
        readonly int w;
        readonly int h;

        public DecodeATI1N(int w, int h)
        {
            this.w = w;
            this.h = h;
        }

        public void Decode(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var data = pixels.GetPixelSpan<byte>();

            var blockCountX = (w + 3) / 4;
            var blockCountY = (h + 3) / 4;
            var offset = 0;

            for (var j = 0; j < blockCountY; j++)
            {
                for (var i = 0; i < blockCountX; i++)
                {
                    var block1 = BitConverter.ToUInt64(input.Slice(offset, 8));
                    offset += 8;
                    var ofs = ((i * 4) + (j * 4 * w)) * 4;
                    DecodeDXT5.Decompress8BitBlock(i * 4, w, ofs, block1, data, w * 4);

                    for (var y = 0; y < 4; y++)
                    {
                        for (var x = 0; x < 4; x++)
                        {
                            var dataIndex = ofs + ((x + (y * w)) * 4);
                            if (data.Length < dataIndex + 3)
                            {
                                break;
                            }

                            data[dataIndex + 1] = data[dataIndex];
                            data[dataIndex + 2] = data[dataIndex];
                            data[dataIndex + 3] = byte.MaxValue;
                        }
                    }
                }
            }
        }
    }
}
