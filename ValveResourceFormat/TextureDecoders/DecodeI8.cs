using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal readonly struct DecodeI8 : ITextureDecoder
    {
        public void Decode(SKBitmap res, Span<byte> input)
        {
            using var pixels = res.PeekPixels();
            var outPixels = pixels.GetPixelSpan<SKColor>();

            for (var i = 0; i < outPixels.Length; i++)
            {
                var intensity = input[i];
                outPixels[i] = new SKColor(intensity, intensity, intensity);
            }
        }
    }
}
