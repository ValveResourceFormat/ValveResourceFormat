using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeRG1616F : ITextureDecoder
    {
        public void Decode(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var span = pixels.GetPixelSpan<SKColorF>();
            var offset = 0;
            var sizeOfHalf = 2;

            for (var i = 0; i < span.Length; i++)
            {
                var r = (float)BitConverter.ToHalf(input.Slice(offset, sizeOfHalf));
                offset += sizeOfHalf;
                var g = (float)BitConverter.ToSingle(input.Slice(offset, sizeOfHalf));
                offset += sizeOfHalf;

                span[i] = new SKColorF(r, g, 0f);
            }
        }

        public void DecodeLowDynamicRange(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var span = pixels.GetPixelSpan<SKColor>();
            var offset = 0;

            for (var i = 0; i < span.Length; i++)
            {
                var r = (float)BitConverter.ToHalf(input.Slice(offset, 2));
                offset += 2;
                var g = (float)BitConverter.ToHalf(input.Slice(offset, 2));
                offset += 2;

                span[i] = new SKColor(
                    (byte)(Common.ClampHighRangeColor(r) * 255),
                    (byte)(Common.ClampHighRangeColor(g) * 255),
                    0,
                    255
                );
            }
        }
    }
}
