using System.Runtime.InteropServices;
using SkiaSharp;
using RGBA16161616 = (ushort R, ushort G, ushort B, ushort A);

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeRGBA16161616 : ITextureDecoder
    {
        public void Decode(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var inputPixels = MemoryMarshal.Cast<byte, RGBA16161616>(input);
            var data = pixels.GetPixelSpan<SKColorF>();

            if (bitmap.ColorType == SKColorType.RgbaF32)
            {
                DecodeHdr(pixels, inputPixels);
                return;
            }

            DecodeLdr(pixels, inputPixels);
        }

        private static void DecodeHdr(SKPixmap pixels, Span<RGBA16161616> inputPixels)
        {
            var hdrColors = pixels.GetPixelSpan<SKColorF>();
            for (var i = 0; i < hdrColors.Length; i++)
            {
                hdrColors[i] = new SKColorF(
                    ((float)inputPixels[i].R) / ushort.MaxValue,
                    ((float)inputPixels[i].G) / ushort.MaxValue,
                    ((float)inputPixels[i].B) / ushort.MaxValue,
                    ((float)inputPixels[i].A) / ushort.MaxValue
                );
            }
        }

        public static void DecodeLdr(SKPixmap pixels, Span<RGBA16161616> inputPixels)
        {
            var ldrColors = pixels.GetPixelSpan<SKColor>();
            var log = 0f;

            for (var i = 0; i < ldrColors.Length; i++)
            {
                var lum = inputPixels[i].R / 256f * 0.299f
                        + inputPixels[i].G / 256f * 0.587f
                        + inputPixels[i].B / 256f * 0.114f;

                log += MathF.Log(MathF.Max(float.Epsilon, lum));
            }

            log = MathF.Exp(log / (pixels.Width * pixels.Height));

            for (var i = 0; i < ldrColors.Length; i++)
            {
                var hr = inputPixels[i].R / 256f;
                var hg = inputPixels[i].G / 256f;
                var hb = inputPixels[i].B / 256f;
                var ha = inputPixels[i].A / 256f;

                var y = (hr * 0.299f) + (hg * 0.587f) + (hb * 0.114f);
                var u = (hb - y) * 0.565f;
                var v = (hr - y) * 0.713f;

                var mul = 4.0f * y / log;
                mul /= 1.0f + mul;
                mul /= y;

                hr = MathF.Pow((y + (1.403f * v)) * mul, 2.25f);
                hg = MathF.Pow((y - (0.344f * u) - (0.714f * v)) * mul, 2.25f);
                hb = MathF.Pow((y + (1.770f * u)) * mul, 2.25f);

                ldrColors[i] = new SKColor(
                     Common.ToClampedLdrColor(hr),
                     Common.ToClampedLdrColor(hg),
                     Common.ToClampedLdrColor(hb),
                     Common.ToClampedLdrColor(ha)
                );
            }
        }
    }
}
