using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeRG1616 : ITextureDecoder
    {
        public void Decode(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var span = pixels.GetPixelSpan<SKColorF>();
            var offset = 0;

            for (int i = 0, j = 0; j < span.Length; i += 4, j++)
            {
                var r = (float)BitConverter.ToUInt16(input.Slice(offset, sizeof(ushort))) / ushort.MaxValue;
                offset += sizeof(ushort);
                var g = (float)BitConverter.ToUInt16(input.Slice(offset, sizeof(ushort))) / ushort.MaxValue;
                offset += sizeof(ushort);

                span[j] = new SKColorF(r, g, 0f);
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
                var g = BitConverter.ToUInt16(input.Slice(offset, sizeof(ushort)));
                offset += sizeof(ushort);

                span[i] = new SKColor(
                    Common.ClampColor(r / 256),
                    Common.ClampColor(g / 256),
                    0,
                    255
                );
            }
        }
    }
}
