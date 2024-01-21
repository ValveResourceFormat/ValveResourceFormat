using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeR32F : ITextureDecoder
    {
        public void Decode(SKBitmap res, Span<byte> input)
        {
            using var pixels = res.PeekPixels();
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
