using System.Runtime.InteropServices;
using SkiaSharp;
using RG3232F = (float R, float G);

namespace ValveResourceFormat.TextureDecoders
{
    internal readonly struct DecodeRG3232F : ITextureDecoder
    {
        public void Decode(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var inputPixels = MemoryMarshal.Cast<byte, RG3232F>(input);

            if (bitmap.ColorType == ResourceTypes.Texture.HdrBitmapColorType)
            {
                DecodeHdr(pixels, inputPixels);
                return;
            }

            DecodeLdr(pixels, inputPixels);
        }

        private static void DecodeHdr(SKPixmap pixels, Span<RG3232F> inputPixels)
        {
            var hdrColors = pixels.GetPixelSpan<SKColorF>();

            for (var i = 0; i < hdrColors.Length; i++)
            {
                var color = inputPixels[i];
                hdrColors[i] = new SKColorF(color.R, color.G, 0f);
            }
        }

        private static void DecodeLdr(SKPixmap pixels, Span<RG3232F> inputPixels)
        {
            var ldrColors = pixels.GetPixelSpan<SKColor>();

            for (var i = 0; i < ldrColors.Length; i++)
            {
                var color = inputPixels[i];
                ldrColors[i] = new SKColor(Common.ToClampedLdrColor(color.R), Common.ToClampedLdrColor(color.G), 0);
            }
        }
    }
}
