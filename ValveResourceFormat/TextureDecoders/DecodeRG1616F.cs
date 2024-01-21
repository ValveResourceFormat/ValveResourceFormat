using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeRG1616F : ITextureDecoder
    {
        public void Decode(SKBitmap res, Span<byte> input)
        {
            using var pixels = res.PeekPixels();
            var span = pixels.GetPixelSpan<SKColor>();
            var offset = 0;

            for (var i = 0; i < span.Length; i++)
            {
                var r = (float)BitConverter.ToHalf(input.Slice(offset, 2));
                offset += 2;
                var g = (float)BitConverter.ToHalf(input.Slice(offset, 2));
                offset += 2;

                span[i] = new SKColor(
                    (byte)(Common.ClampHighRangeColor(r) * 255),
                    (byte)(Common.ClampHighRangeColor(g) * 255),
                    0,
                    255
                );
            }
        }
    }
}
