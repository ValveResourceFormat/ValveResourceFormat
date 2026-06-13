using System.Runtime.InteropServices;
using SkiaSharp;
using RG1616F = (System.Half R, System.Half G);

namespace ValveResourceFormat.TextureDecoders
{
    internal readonly struct DecodeRG1616F : ITextureDecoder
    {
        public void Decode(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var inputPixels = MemoryMarshal.Cast<byte, RG1616F>(input);

            if (bitmap.ColorType == ResourceTypes.Texture.HdrBitmapColorType)
            {
                DecodeHdr(pixels, inputPixels);
                return;
            }

            DecodeLdr(pixels, inputPixels);
        }

        private static void DecodeHdr(SKPixmap pixels, Span<RG1616F> inputPixels)
        {
            var hdrColors = pixels.GetPixelSpan<SKColorF>();

            for (var i = 0; i < hdrColors.Length; i++)
            {
                hdrColors[i] = new SKColorF(
                    (float)inputPixels[i].R,
                    (float)inputPixels[i].G,
                    0f
                );
            }
        }

        private static void DecodeLdr(SKPixmap pixels, Span<RG1616F> inputPixels)
        {
            var ldrColors = pixels.GetPixelSpan<SKColor>();

            for (var i = 0; i < ldrColors.Length; i++)
            {
                ldrColors[i] = new SKColor(
                    Common.ToClampedLdrColor((float)inputPixels[i].R),
                    Common.ToClampedLdrColor((float)inputPixels[i].G),
                    0
                );
            }
        }
    }
}
