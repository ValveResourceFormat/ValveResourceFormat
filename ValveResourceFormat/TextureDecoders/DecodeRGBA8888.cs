using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal readonly struct DecodeRGBA8888 : ITextureDecoder
    {
        public void Decode(SKBitmap res, Span<byte> input)
        {
            using var pixels = res.PeekPixels();
            var outPixels = pixels.GetPixelSpan<byte>(); // Note: output pixels have BGRA8888 order

            input[..outPixels.Length].CopyTo(outPixels);
            Common.SwapRB(outPixels);
        }
    }
}
