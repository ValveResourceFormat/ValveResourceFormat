using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeIA88 : ITextureDecoder
    {
        public void Decode(SKBitmap res, Span<byte> input)
        {
            using var pixels = res.PeekPixels();
            var span = pixels.GetPixelSpan<SKColor>();
            var offset = 0;

            for (var i = 0; i < span.Length; i++)
            {
                var color = input[offset++];
                var alpha = input[offset++];
                span[i] = new SKColor(color, color, color, alpha);
            }
        }
    }
}
