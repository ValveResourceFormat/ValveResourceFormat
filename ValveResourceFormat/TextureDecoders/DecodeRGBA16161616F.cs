using System;
using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeRGBA16161616F : ITextureDecoder
    {
        public void Decode(SKBitmap imageInfo, Span<byte> input)
        {
            using var pixels = imageInfo.PeekPixels();
            var data = pixels.GetPixelSpan<SKColor>();
            var log = 0f;

            for (int i = 0, j = 0; j < data.Length; i += 8, j++)
            {
                var hr = (float)BitConverter.ToHalf(input.Slice(i, 2));
                var hg = (float)BitConverter.ToHalf(input.Slice(i + 2, 2));
                var hb = (float)BitConverter.ToHalf(input.Slice(i + 4, 2));
                var lum = (hr * 0.299f) + (hg * 0.587f) + (hb * 0.114f);
                log += MathF.Log(MathF.Max(float.Epsilon, lum));
            }

            log = MathF.Exp(log / (imageInfo.Width * imageInfo.Height));

            for (int i = 0, j = 0; j < data.Length; i += 8, j++)
            {
                var hr = (float)BitConverter.ToHalf(input.Slice(i, 2));
                var hg = (float)BitConverter.ToHalf(input.Slice(i + 2, 2));
                var hb = (float)BitConverter.ToHalf(input.Slice(i + 4, 2));
                var ha = (float)BitConverter.ToHalf(input.Slice(i + 6, 2));

                var y = (hr * 0.299f) + (hg * 0.587f) + (hb * 0.114f);
                var u = (hb - y) * 0.565f;
                var v = (hr - y) * 0.713f;

                var mul = 4.0f * y / log;
                mul /= 1.0f + mul;
                mul /= y;


                if (hr < 0)
                {
                    hr = 0;
                }

                if (hr > 1)
                {
                    hr = 1;
                }

                if (hg < 0)
                {
                    hg = 0;
                }

                if (hg > 1)
                {
                    hg = 1;
                }

                if (hb < 0)
                {
                    hb = 0;
                }

                if (hb > 1)
                {
                    hb = 1;
                }
                hr = MathF.Pow((y + (1.403f * v)) * mul, 2.25f);
                hg = MathF.Pow((y - (0.344f * u) - (0.714f * v)) * mul, 2.25f);
                hb = MathF.Pow((y + (1.770f * u)) * mul, 2.25f);

                data[j] = new SKColor(
                    (byte)(hr * 255),
                    (byte)(hg * 255),
                    (byte)(hb * 255),
                    (byte)(ha * 255)
                );
            }
        }
    }
}
