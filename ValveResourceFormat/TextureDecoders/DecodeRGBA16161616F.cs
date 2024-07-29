using System.Runtime.InteropServices;
using SkiaSharp;
using RGBA16161616F = (System.Half R, System.Half G, System.Half B, System.Half A);

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeRGBA16161616F : ITextureDecoder
    {
        public void Decode(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var inputPixels = MemoryMarshal.Cast<byte, RGBA16161616F>(input);

            if (bitmap.ColorType == SKColorType.RgbaF32)
            {
                DecodeHdr(pixels, inputPixels);
                return;
            }

            DecodeLdr(pixels, inputPixels);
        }

        private static void DecodeHdr(SKPixmap pixels, Span<RGBA16161616F> inputPixels)
        {
            var hdrColors = pixels.GetPixelSpan<SKColorF>();

            for (var i = 0; i < hdrColors.Length; i++)
            {
                hdrColors[i] = new SKColorF(
                    (float)inputPixels[i].R,
                    (float)inputPixels[i].G,
                    (float)inputPixels[i].B,
                    (float)inputPixels[i].A
                );
            }
        }

        public static void DecodeLdr(SKPixmap pixels, Span<RGBA16161616F> inputPixels)
        {
            var ldrColors = pixels.GetPixelSpan<SKColor>();
            var log = 0f;

            for (var i = 0; i < ldrColors.Length; i++)
            {
                var lum = (float)inputPixels[i].R * 0.299f
                        + (float)inputPixels[i].G * 0.587f
                        + (float)inputPixels[i].B * 0.114f;

                log += MathF.Log(MathF.Max(float.Epsilon, lum));
            }

            log = MathF.Exp(log / (pixels.Width * pixels.Height));

            for (var i = 0; i < ldrColors.Length; i++)
            {
                var hr = (float)inputPixels[i].R;
                var hg = (float)inputPixels[i].G;
                var hb = (float)inputPixels[i].B;
                var ha = (float)inputPixels[i].A;

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
