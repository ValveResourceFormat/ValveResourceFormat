using System.Runtime.InteropServices;
using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeRGBA32323232F : ITextureDecoder
    {
        public void Decode(SKBitmap res, Span<byte> input)
        {
            using var pixels = res.PeekPixels();
            var span = pixels.GetPixelSpan<SKColor>();
            var offset = 0;
            var stride = 4 * sizeof(float);

            for (var i = 0; i < span.Length; i++)
            {
                var skColorF = MemoryMarshal.Cast<byte, SKColorF>(input.Slice(offset, stride))[0];
                offset += stride;

                span[i] = new SKColor(
                    (byte)(Common.ClampHighRangeColor(skColorF.Red) * 255),
                    (byte)(Common.ClampHighRangeColor(skColorF.Green) * 255),
                    (byte)(Common.ClampHighRangeColor(skColorF.Blue) * 255),
                    (byte)(Common.ClampHighRangeColor(skColorF.Alpha) * 255)
                );
            }
        }
    }
}
