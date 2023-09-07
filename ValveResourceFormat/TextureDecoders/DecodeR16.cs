using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeR16 : ITextureDecoder
    {
        public void Decode(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var span = pixels.GetPixelSpan<SKColorF>();
            var offset = 0;

            for (var i = 0; i < span.Length; i++)
            {
                var hr = BitConverter.ToUInt16(input[offset..(offset + 2)]) / 256f;
                offset += 2;

                span[i] = new SKColorF(hr, 0f, 0f);
            }
        }

        public void DecodeLowDynamicRange(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var span = pixels.GetPixelSpan<SKColor>();
            var offset = 0;

            for (var i = 0; i < span.Length; i++)
            {
                var r = BitConverter.ToUInt16(input.Slice(offset, sizeof(ushort)));
                offset += sizeof(ushort);

                span[i] = new SKColor(Common.ClampColor(r / 256), 0, 0, 255);
            }
        }
    }
}
