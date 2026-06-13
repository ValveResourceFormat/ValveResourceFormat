using System.Runtime.InteropServices;
using SkiaSharp;
using RGBA8888 = (byte R, byte G, byte B, byte A);

namespace ValveResourceFormat.TextureDecoders
{
    internal readonly struct DecodeRGBA8888 : ITextureDecoder
    {
        public void Decode(SKBitmap res, Span<byte> input)
        {
            using var pixels = res.PeekPixels();
            var inputPixels = MemoryMarshal.Cast<byte, RGBA8888>(input);
            var outPixels = pixels.GetPixelSpan<SKColor>(); // Note: output pixels have BGRA8888 order

            for (var i = 0; i < outPixels.Length; i++)
            {
                var color = inputPixels[i];
                outPixels[i] = new SKColor(color.R, color.G, color.B, color.A);
            }
        }
    }
}
