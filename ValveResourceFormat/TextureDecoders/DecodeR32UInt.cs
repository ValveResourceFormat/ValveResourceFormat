using System.Runtime.InteropServices;
using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal readonly struct DecodeR32UInt : ITextureDecoder
    {
        public void Decode(SKBitmap res, Span<byte> input)
        {
            using var pixels = res.PeekPixels();
            var outPixels = pixels.GetPixelSpan<SKColor>();
            var inputPixels = MemoryMarshal.Cast<byte, uint>(input);

            // Integer data such as ids, values above 255 saturate
            for (var i = 0; i < outPixels.Length; i++)
            {
                outPixels[i] = new SKColor((byte)Math.Min(inputPixels[i], byte.MaxValue), 0, 0, 255);
            }
        }
    }
}
