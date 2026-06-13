using System.Buffers;
using System.Runtime.InteropServices;
using SkiaSharp;
using TinyBCSharp;

namespace ValveResourceFormat.TextureDecoders;

internal readonly struct DecodeBCn : ITextureDecoder
{
    readonly int w;
    readonly int h;
    readonly BlockFormat format;

    public DecodeBCn(int w, int h, BlockFormat format)
    {
        this.w = w;
        this.h = h;
        this.format = format;
    }

    public void Decode(SKBitmap bitmap, Span<byte> input)
    {
        using var pixmap = bitmap.PeekPixels();
        var data = pixmap.GetPixelSpan<byte>();

        if (format == BlockFormat.BC6HUf32 && bitmap.ColorType != ResourceTypes.Texture.HdrBitmapColorType)
        {
            var dataHalf = ArrayPool<byte>.Shared.Rent(data.Length * sizeof(ushort));

            try
            {
                var decoder = BlockDecoder.Create(BlockFormat.BC6HUf16);
                decoder.Decode(input, w, h, dataHalf, bitmap.Width, bitmap.Height);

                var halfComponents = MemoryMarshal.Cast<byte, Half>(dataHalf);

                for (var i = 0; i < data.Length; i++)
                {
                    data[i] = Common.ToClampedLdrColor((float)halfComponents[i]);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(dataHalf);
            }
        }
        else
        {
            var decoder = BlockDecoder.Create(format);
            decoder.Decode(input, w, h, data, bitmap.Width, bitmap.Height);
        }

        // BlockDecoders produce RGBA, we need BGRA
        if (bitmap.ColorType == ResourceTypes.Texture.DefaultBitmapColorType)
        {
            Common.SwapRB(data);
        }
    }
}
