using System.Runtime.InteropServices;
using SkiaSharp;
using TinyBCSharp;

namespace ValveResourceFormat.TextureDecoders;

internal readonly struct DecodeBCn : ITextureDecoder
{
    readonly int w;
    readonly int h;
    readonly TextureCodec decodeFlags;
    readonly BlockFormat format;

    public DecodeBCn(int w, int h, TextureCodec decodeFlags, BlockFormat format)
    {
        this.w = w;
        this.h = h;
        this.decodeFlags = decodeFlags;
        this.format = format;
    }

    public void Decode(SKBitmap bitmap, Span<byte> input)
    {
        using var pixmap = bitmap.PeekPixels();
        var data = pixmap.GetPixelSpan<byte>();

        var decoder = BlockDecoder.Create(format);
        decoder.Decode(input, w, h, data, bitmap.Width, bitmap.Height);

        var undoYCoCg = (decodeFlags & TextureCodec.YCoCg) != 0; // DXT5
        var undoNormalizeNormals = (decodeFlags & TextureCodec.NormalizeNormals) != 0; // DXT5, ATI2N
        var undoHemiOctRB = (decodeFlags & TextureCodec.HemiOctRB) != 0; // DXT5, ATI2N, BC7

        if (!undoYCoCg && !undoNormalizeNormals && !undoHemiOctRB)
        {
            return;
        }

        var pixels = MemoryMarshal.Cast<byte, Color>(data);

        for (var i = 0; i < pixels.Length; i++)
        {
            if (undoYCoCg)
            {
                Common.Undo_YCoCg(ref pixels[i]);
            }

            if (format == BlockFormat.BC3)
            {
                // todo: dxt5nm check
                if (undoNormalizeNormals)
                {
                    var red = pixels[i].r;
                    pixels[i].r = pixels[i].a; // red to alpha
                    pixels[i].a = red;

                    if (undoHemiOctRB)
                    {
                        Common.Undo_HemiOct(ref pixels[i]);
                    }
                    else
                    {
                        Common.Undo_NormalizeNormals(ref pixels[i]);
                    }
                }
            }
            else
            {
                if (undoNormalizeNormals)
                {
                    Common.Undo_NormalizeNormals(ref pixels[i]);
                }

                if (undoHemiOctRB)
                {
                    Common.Undo_HemiOct(ref pixels[i]);
                }
            }
        }
    }
}
