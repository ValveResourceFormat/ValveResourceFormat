using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal interface ITextureDecoder
    {
        public abstract void Decode(SKBitmap bitmap, Span<byte> input);
    }
}
