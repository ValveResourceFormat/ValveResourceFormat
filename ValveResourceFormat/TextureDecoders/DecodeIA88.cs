using System.Runtime.InteropServices;
using SkiaSharp;
using IA88 = (byte Intensity, byte Alpha);

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeIA88 : ITextureDecoder
    {
        public void Decode(SKBitmap res, Span<byte> input)
        {
            using var pixels = res.PeekPixels();
            var inputPixels = MemoryMarshal.Cast<byte, IA88>(input);
            var outPixels = pixels.GetPixelSpan<SKColor>();

            for (var i = 0; i < outPixels.Length; i++)
            {
                outPixels[i] = new SKColor(
                    inputPixels[i].Intensity,
                    inputPixels[i].Intensity,
                    inputPixels[i].Intensity,
                    inputPixels[i].Alpha
                );
            }
        }
    }
}
