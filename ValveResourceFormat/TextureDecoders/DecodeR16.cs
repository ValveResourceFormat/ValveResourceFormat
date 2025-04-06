using System.Runtime.InteropServices;
using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeR16 : ITextureDecoder
    {
        public void Decode(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var inputPixels = MemoryMarshal.Cast<byte, ushort>(input);

            if (bitmap.ColorType == ResourceTypes.Texture.HdrBitmapColorType)
            {
                DecodeHdr(pixels, inputPixels);
                return;
            }

            DecodeLdr(pixels, inputPixels);
        }

        private static void DecodeHdr(SKPixmap pixels, Span<ushort> inputPixels)
        {
            var hdrColors = pixels.GetPixelSpan<SKColorF>();

            for (var i = 0; i < hdrColors.Length; i++)
            {
                hdrColors[i] = new SKColorF(((float)inputPixels[i]) / ushort.MaxValue, 0f, 0f);
            }
        }

        private static void DecodeLdr(SKPixmap pixels, Span<ushort> inputPixels)
        {
            var ldrColors = pixels.GetPixelSpan<SKColor>();

            for (var i = 0; i < ldrColors.Length; i++)
            {
                ldrColors[i] = new SKColor(Common.ClampColor(inputPixels[i] / 256), 0, 0, 255);
            }
        }
    }
}
