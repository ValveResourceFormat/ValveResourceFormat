using System.Runtime.InteropServices;
using SkiaSharp;
using RGBA32323232F = (float R, float G, float B, float A);

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeRGBA32323232F : ITextureDecoder
    {
        public void Decode(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var inputPixels = MemoryMarshal.Cast<byte, RGBA32323232F>(input);

            if (bitmap.ColorType == ResourceTypes.Texture.HdrBitmapColorType)
            {
                DecodeHdr(pixels, inputPixels);
                return;
            }

            DecodeLdr(pixels, inputPixels);
        }

        private static void DecodeHdr(SKPixmap pixels, Span<RGBA32323232F> inputPixels)
        {
            var hdrColors = pixels.GetPixelSpan<RGBA32323232F>();
            inputPixels.CopyTo(hdrColors);
        }

        private static void DecodeLdr(SKPixmap pixels, Span<RGBA32323232F> inputPixels)
        {
            var ldrColors = pixels.GetPixelSpan<SKColor>();

            for (var i = 0; i < ldrColors.Length; i++)
            {
                ldrColors[i] = new SKColor(
                    Common.ToClampedLdrColor(inputPixels[i].R),
                    Common.ToClampedLdrColor(inputPixels[i].G),
                    Common.ToClampedLdrColor(inputPixels[i].B),
                    Common.ToClampedLdrColor(inputPixels[i].A)
                );
            }
        }
    }
}
