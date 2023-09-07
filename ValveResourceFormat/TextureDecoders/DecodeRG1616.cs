using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeRG1616 : ITextureDecoder
    {
        public void Decode(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var span = pixels.GetPixelSpan<SKColorF>();

            for (int i = 0, j = 0; j < span.Length; i += 4, j++)
            {
                var hr = BitConverter.ToUInt16(input.Slice(i, 2)) / 256f;
                var hg = BitConverter.ToUInt16(input.Slice(i + 2, 2)) / 256f;

                span[j] = new SKColorF(hr, hg, 0f);
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
                var b = BitConverter.ToUInt16(input.Slice(offset, sizeof(ushort)));
                offset += sizeof(ushort);

                span[i] = new SKColor(
                    Common.ClampColor(r / 256),
                    Common.ClampColor(b / 256),
                    0,
                    255
                );
            }
        }
    }
}
