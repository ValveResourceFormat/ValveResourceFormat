using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeBGRA8888 : ITextureDecoder
    {
        public void Decode(SKBitmap res, Span<byte> input)
        {
            using var pixels = res.PeekPixels();
            var span = pixels.GetPixelSpan<SKColor>();
            var offset = 0;

            for (var i = 0; i < span.Length; i++)
            {
                var colorB = input[offset++];
                var colorG = input[offset++];
                var colorR = input[offset++];
                var colorA = input[offset++];
                span[i] = new SKColor(colorR, colorG, colorB, colorA);
            }
        }
    }
}
