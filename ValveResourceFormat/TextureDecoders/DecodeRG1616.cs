using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeRG1616 : ITextureDecoder
    {
        public void Decode(SKBitmap res, Span<byte> input)
        {
            using var pixels = res.PeekPixels();
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
