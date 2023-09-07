using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeRGBA16161616 : ITextureDecoder
    {
        public void Decode(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var data = pixels.GetPixelSpan<SKColorF>();

            for (int i = 0, j = 0; j < data.Length; i += 8, j++)
            {
                var hr = BitConverter.ToUInt16(input.Slice(i, 2)) / 256f;
                var hg = BitConverter.ToUInt16(input.Slice(i + 2, 2)) / 256f;
                var hb = BitConverter.ToUInt16(input.Slice(i + 2, 4)) / 256f;
                var ha = BitConverter.ToUInt16(input.Slice(i + 2, 6)) / 256f;

                data[j] = new SKColorF(hr, hg, hb, ha);
            }
        }

        public void DecodeLowDynamicRange(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var data = pixels.GetPixelSpan<SKColor>();
            var log = 0f;

            for (int i = 0, j = 0; j < data.Length; i += 8, j++)
            {
                var hr = BitConverter.ToUInt16(input.Slice(i, 2)) / 256f;
                var hg = BitConverter.ToUInt16(input.Slice(i + 2, 2)) / 256f;
                var hb = BitConverter.ToUInt16(input.Slice(i + 2, 4)) / 256f;
                var lum = (hr * 0.299f) + (hg * 0.587f) + (hb * 0.114f);
                log += MathF.Log(MathF.Max(float.Epsilon, lum));
            }

            log = MathF.Exp(log / (bitmap.Width * bitmap.Height));

            for (int i = 0, j = 0; j < data.Length; i += 8, j++)
            {
                var hr = BitConverter.ToUInt16(input.Slice(i, 2)) / 256f;
                var hg = BitConverter.ToUInt16(input.Slice(i + 2, 2)) / 256f;
                var hb = BitConverter.ToUInt16(input.Slice(i + 4, 2)) / 256f;
                var ha = BitConverter.ToUInt16(input.Slice(i + 6, 2)) / 256f;

                var y = (hr * 0.299f) + (hg * 0.587f) + (hb * 0.114f);
                var u = (hb - y) * 0.565f;
                var v = (hr - y) * 0.713f;

                var mul = 4.0f * y / log;
                mul /= 1.0f + mul;
                mul /= y;

                hr = MathF.Pow((y + (1.403f * v)) * mul, 2.25f);
                hg = MathF.Pow((y - (0.344f * u) - (0.714f * v)) * mul, 2.25f);
                hb = MathF.Pow((y + (1.770f * u)) * mul, 2.25f);

                data[j] = new SKColor(
                    (byte)(Common.ClampHighRangeColor(hr) * 255),
                    (byte)(Common.ClampHighRangeColor(hg) * 255),
                    (byte)(Common.ClampHighRangeColor(hb) * 255),
                    (byte)(Common.ClampHighRangeColor(ha) * 255)
                );
            }
        }
    }
}
