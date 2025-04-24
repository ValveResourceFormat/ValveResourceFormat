using System.Runtime.InteropServices;
using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal readonly struct DecodeR32F : ITextureDecoder
    {
        public void Decode(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var inputPixels = MemoryMarshal.Cast<byte, float>(input);

            if (bitmap.ColorType == ResourceTypes.Texture.HdrBitmapColorType)
            {
                DecodeHdr(pixels, inputPixels);
                return;
            }

            DecodeLdr(pixels, inputPixels);
        }

        private static void DecodeHdr(SKPixmap pixels, Span<float> inputPixels)
        {
            var hdrColors = pixels.GetPixelSpan<SKColorF>();

            for (var i = 0; i < hdrColors.Length; i++)
            {
                hdrColors[i] = new SKColorF(inputPixels[i], 0f, 0f);
            }
        }

        private static void DecodeLdr(SKPixmap pixels, Span<float> inputPixels)
        {
            var ldrColors = pixels.GetPixelSpan<SKColor>();

            for (var i = 0; i < ldrColors.Length; i++)
            {
                ldrColors[i] = new SKColor(Common.ToClampedLdrColor(inputPixels[i]), 0, 0);
            }
        }
    }
}
