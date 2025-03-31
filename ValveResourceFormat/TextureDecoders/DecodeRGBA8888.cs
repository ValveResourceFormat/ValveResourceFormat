using System.Runtime.InteropServices;
using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeRGBA8888 : ITextureDecoder
    {
        public void Decode(SKBitmap res, Span<byte> input)
        {
            using var pixels = res.PeekPixels();
            var inputPixels = MemoryMarshal.Cast<byte, Color32>(input);
            var outPixels = pixels.GetPixelSpan<Color32>();

            inputPixels.CopyTo(outPixels);
        }
    }
}
