using System.Runtime.InteropServices;
using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal readonly struct DecodeR16F : ITextureDecoder
    {
        public void Decode(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var inputPixels = MemoryMarshal.Cast<byte, Half>(input);

            if (bitmap.ColorType == ResourceTypes.Texture.HalfHdrBitmapColorType)
            {
                DecodeHdr(pixels, inputPixels);
                return;
            }

            DecodeLdr(pixels, inputPixels);
        }

        private static void DecodeHdr(SKPixmap pixels, ReadOnlySpan<Half> inputPixels)
        {
            var halfColors = MemoryMarshal.Cast<byte, Half>(pixels.GetPixelSpan<byte>());
            var pixelCount = halfColors.Length / 4;

            for (var i = 0; i < pixelCount; i++)
            {
                halfColors[i * 4] = inputPixels[i];
                halfColors[i * 4 + 1] = Half.Zero;
                halfColors[i * 4 + 2] = Half.Zero;
                halfColors[i * 4 + 3] = Half.One;
            }
        }

        private static void DecodeLdr(SKPixmap pixels, ReadOnlySpan<Half> inputPixels)
        {
            var ldrColors = pixels.GetPixelSpan<SKColor>();

            for (var i = 0; i < ldrColors.Length; i++)
            {
                ldrColors[i] = new SKColor(Common.ToClampedLdrColor((float)inputPixels[i]), 0, 0);
            }
        }
    }
}
