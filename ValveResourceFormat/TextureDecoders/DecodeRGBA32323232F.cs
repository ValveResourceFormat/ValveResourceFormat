using System.Numerics.Tensors;
using System.Runtime.InteropServices;
using SkiaSharp;
using RGBA32323232F = (float R, float G, float B, float A);

namespace ValveResourceFormat.TextureDecoders
{
    internal readonly struct DecodeRGBA32323232F : ITextureDecoder
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

        private static void DecodeHdr(SKPixmap pixels, ReadOnlySpan<RGBA32323232F> inputPixels)
        {
            var hdrColors = pixels.GetPixelSpan<RGBA32323232F>();
            inputPixels.CopyTo(hdrColors);
        }

        private static void DecodeLdr(SKPixmap pixels, ReadOnlySpan<RGBA32323232F> inputPixels)
        {
            var ldrColors = pixels.GetPixelSpan<byte>();
            var components = MemoryMarshal.Cast<RGBA32323232F, float>(inputPixels)[..ldrColors.Length];

            TensorPrimitives.ConvertToClampedUnorm8(components, ldrColors);
            Common.SwapRB(ldrColors);
        }
    }
}
