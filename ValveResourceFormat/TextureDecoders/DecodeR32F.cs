using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeR32F : ITextureDecoder
    {
        public void Decode(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var span = pixels.GetPixelSpan<SKColorF>();
            var offset = 0;

            for (var i = 0; i < span.Length; i++)
            {
                var r = BitConverter.ToSingle(input[offset..(offset + sizeof(float))]);
                offset += sizeof(float);

                span[i] = new SKColorF(r, 0f, 0f);
            }
        }

        public void DecodeLowDynamicRange(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var span = pixels.GetPixelSpan<SKColor>();
            var offset = 0;

            for (var i = 0; i < span.Length; i++)
            {
                var r = BitConverter.ToSingle(input.Slice(offset, sizeof(float)));
                offset += sizeof(float);

                span[i] = new SKColor((byte)(Common.ClampHighRangeColor(r) * 255), 0, 0, 255);
            }
        }
    }
}
