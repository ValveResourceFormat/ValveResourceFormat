using System.Runtime.InteropServices;
using SkiaSharp;
using RG1616 = (ushort R, ushort G);

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeRG1616 : ITextureDecoder
    {
        public void Decode(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var inputPixels = MemoryMarshal.Cast<byte, RG1616>(input);

            if (bitmap.ColorType == SKColorType.RgbaF32)
            {
                DecodeHdr(pixels, inputPixels);
                return;
            }

            DecodeLdr(pixels, inputPixels);
        }

        private static void DecodeHdr(SKPixmap pixels, Span<RG1616> inputPixels)
        {
            var hdrColors = pixels.GetPixelSpan<SKColorF>();

            for (var i = 0; i < hdrColors.Length; i++)
            {
                hdrColors[i] = new SKColorF(
                    ((float)inputPixels[i].R) / ushort.MaxValue,
                    ((float)inputPixels[i].G) / ushort.MaxValue,
                    0f
                );
            }
        }

        private static void DecodeLdr(SKPixmap pixels, Span<RG1616> inputPixels)
        {
            var ldrColors = pixels.GetPixelSpan<SKColor>();

            for (var i = 0; i < ldrColors.Length; i++)
            {
                ldrColors[i] = new SKColor(
                    Common.ClampColor(inputPixels[i].R / 256),
                    Common.ClampColor(inputPixels[i].G / 256),
                    0
                );
            }
        }
    }
}
