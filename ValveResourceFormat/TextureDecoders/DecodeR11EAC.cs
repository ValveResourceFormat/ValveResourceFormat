using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal readonly struct DecodeR11EAC : ITextureDecoder
    {
        readonly int width;
        readonly int height;

        public DecodeR11EAC(int width, int height)
        {
            this.width = width;
            this.height = height;
        }

        public void Decode(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var output = pixels.GetPixelSpan<SKColor>();

            var blocksWide = MathUtils.DivideRoundUp(width, 4);
            var blocksHigh = MathUtils.DivideRoundUp(height, 4);

            Span<byte> red = stackalloc byte[16];

            for (int blockY = 0, offset = 0; blockY < blocksHigh; blockY++)
            {
                for (var blockX = 0; blockX < blocksWide; blockX++, offset += 8)
                {
                    CommonEAC.DecodeBlock(input.Slice(offset, 8), red);

                    for (var y = 0; y < 4; y++)
                    {
                        var pixelY = blockY * 4 + y;

                        if (pixelY >= bitmap.Height)
                        {
                            break;
                        }

                        for (var x = 0; x < 4; x++)
                        {
                            var pixelX = blockX * 4 + x;

                            if (pixelX >= bitmap.Width)
                            {
                                break;
                            }

                            var value = red[y * 4 + x];
                            output[pixelY * bitmap.Width + pixelX] = new SKColor(value, value, value);
                        }
                    }
                }
            }
        }
    }
}
