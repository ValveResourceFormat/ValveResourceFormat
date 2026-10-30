using System.Buffers.Binary;
using SkiaSharp;
using TinyBCSharp;
using ValveResourceFormat.TextureDecoders;

namespace ValveResourceFormat.Source1.Texture;

internal sealed partial class VtfFile
{
    public bool IsHighDynamicRange => Format is VtfFormat.RGBA16161616F or VtfFormat.RGB323232F
        or VtfFormat.RGBA32323232F or VtfFormat.R32F;

    public SKBitmap DecodeMip(int mip = 0, int frame = 0, int face = 0, int slice = 0)
    {
        var width = Math.Max(1, Width >> mip);
        var height = Math.Max(1, Height >> mip);
        var source = GetHiResPixelData(mip, frame, face, slice);
        var format = Format;

        var colorType = IsHighDynamicRange
            ? ResourceTypes.Texture.HdrBitmapColorType
            : ResourceTypes.Texture.DefaultBitmapColorType;
        var bitmap = new SKBitmap(width, height, colorType, SKAlphaType.Unpremul);

        try
        {
            ITextureDecoder? decoder = format switch
            {
                VtfFormat.DXT1 or VtfFormat.DXT1_ONEBITALPHA => new DecodeBCn(width, height, BlockFormat.BC1),
                VtfFormat.DXT3 => new DecodeBCn(width, height, BlockFormat.BC2),
                VtfFormat.DXT5 => new DecodeBCn(width, height, BlockFormat.BC3),
                VtfFormat.ATI1N => new DecodeBCn(width, height, BlockFormat.BC4U),
                VtfFormat.ATI2N => new DecodeBCn(width, height, BlockFormat.BC5U),
                VtfFormat.BGRA8888 => new DecodeBGRA8888(),
                VtfFormat.RGBA8888 => new DecodeRGBA8888(),
                VtfFormat.RGBA16161616 => new DecodeRGBA16161616(),
                VtfFormat.RGBA16161616F => new DecodeRGBA16161616F(),
                VtfFormat.RGB323232F => new DecodeRGB323232F(),
                VtfFormat.RGBA32323232F => new DecodeRGBA32323232F(),
                VtfFormat.R32F => new DecodeR32F(),
                _ => null,
            };

            if (decoder is not null)
            {
                decoder.Decode(bitmap, source);

                if (format == VtfFormat.ATI2N)
                {
                    using var normalPixmap = bitmap.PeekPixels();
                    ReconstructNormalZ(normalPixmap.GetPixelSpan<byte>());
                }

                return bitmap;
            }

            using var pixmap = bitmap.PeekPixels();
            var destination = pixmap.GetPixelSpan<byte>();
            ConvertPixels(source, destination, format);
            Common.SwapRB(destination);

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    // A two channel normal map stores x and y. Pixels are BGRA, with the decoded x in red and y in green.
    private static void ReconstructNormalZ(Span<byte> pixels)
    {
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var x = (pixels[i + 2] / 255f * 2f) - 1f;
            var y = (pixels[i + 1] / 255f * 2f) - 1f;
            var z = MathF.Sqrt(MathF.Max(0f, 1f - (x * x) - (y * y)));

            pixels[i] = (byte)MathF.Round((z * 0.5f + 0.5f) * 255f);
            pixels[i + 3] = 255;
        }
    }

    internal static void ConvertPixels(ReadOnlySpan<byte> source, Span<byte> destination, VtfFormat format)
    {
        var stride = format switch
        {
            VtfFormat.BGR888 or VtfFormat.RGB888
                or VtfFormat.RGB888_BLUESCREEN or VtfFormat.BGR888_BLUESCREEN => 3,
            VtfFormat.ARGB8888 or VtfFormat.ABGR8888 or VtfFormat.BGRX8888
                or VtfFormat.UVWQ8888 or VtfFormat.UVLX8888 => 4,
            VtfFormat.IA88 or VtfFormat.UV88
                or VtfFormat.RGB565 or VtfFormat.BGR565 or VtfFormat.BGRA4444
                or VtfFormat.BGRX5551 or VtfFormat.BGRA5551 => 2,
            VtfFormat.I8 or VtfFormat.A8 => 1,
            _ => throw new NotSupportedException($"VTF conversion: {format}")
        };

        if (source.Length % stride != 0 || destination.Length != checked(source.Length / stride * 4))
        {
            throw new ArgumentException("VTF pixel conversion buffer sizes do not match.");
        }

        for (int i = 0, j = 0; i < source.Length; i += stride, j += 4)
        {
            switch (format)
            {
                case VtfFormat.BGR888:
                case VtfFormat.BGR888_BLUESCREEN:
                case VtfFormat.BGRX8888:
                    destination[j] = source[i + 2];
                    destination[j + 1] = source[i + 1];
                    destination[j + 2] = source[i];
                    destination[j + 3] = 255;
                    break;
                case VtfFormat.RGB888:
                case VtfFormat.RGB888_BLUESCREEN:
                    source.Slice(i, 3).CopyTo(destination.Slice(j, 3));
                    destination[j + 3] = 255;
                    break;
                case VtfFormat.UVWQ8888:
                    // Signed channels are stored offset to unsigned, so zero is 128
                    destination[j] = (byte)(source[i] + 128);
                    destination[j + 1] = (byte)(source[i + 1] + 128);
                    destination[j + 2] = (byte)(source[i + 2] + 128);
                    destination[j + 3] = 255;
                    break;
                case VtfFormat.UVLX8888:
                    destination[j] = (byte)(source[i] + 128);
                    destination[j + 1] = (byte)(source[i + 1] + 128);
                    destination[j + 2] = source[i + 2];
                    destination[j + 3] = 255;
                    break;
                case VtfFormat.ARGB8888:
                    source.Slice(i + 1, 3).CopyTo(destination.Slice(j, 3));
                    destination[j + 3] = source[i];
                    break;
                case VtfFormat.ABGR8888:
                    destination[j] = source[i + 3];
                    destination[j + 1] = source[i + 2];
                    destination[j + 2] = source[i + 1];
                    destination[j + 3] = source[i];
                    break;
                case VtfFormat.RGB565:
                case VtfFormat.BGR565:
                case VtfFormat.BGRX5551:
                case VtfFormat.BGRA5551:
                case VtfFormat.BGRA4444:
                    var packed = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(i, 2));

                    if (format == VtfFormat.BGRA4444)
                    {
                        destination[j] = (byte)(((packed >> 8) & 15) * 17);
                        destination[j + 1] = (byte)(((packed >> 4) & 15) * 17);
                        destination[j + 2] = (byte)((packed & 15) * 17);
                        destination[j + 3] = (byte)((packed >> 12) * 17);
                    }
                    else
                    {
                        var is565 = format is VtfFormat.RGB565 or VtfFormat.BGR565;
                        var blue = packed & 31;
                        var green = (packed >> 5) & (is565 ? 63 : 31);
                        var red = (packed >> (is565 ? 11 : 10)) & 31;

                        destination[j] = (byte)((red << 3) | (red >> 2));
                        destination[j + 1] = is565
                            ? (byte)((green << 2) | (green >> 4))
                            : (byte)((green << 3) | (green >> 2));
                        destination[j + 2] = (byte)((blue << 3) | (blue >> 2));
                        destination[j + 3] = format == VtfFormat.BGRA5551 && (packed & 0x8000) == 0
                            ? (byte)0
                            : (byte)255;
                    }

                    break;
                case VtfFormat.I8:
                case VtfFormat.IA88:
                    destination[j] = destination[j + 1] = destination[j + 2] = source[i];
                    destination[j + 3] = format == VtfFormat.IA88 ? source[i + 1] : (byte)255;
                    break;
                case VtfFormat.A8:
                    destination[j] = destination[j + 1] = destination[j + 2] = 0;
                    destination[j + 3] = source[i];
                    break;
                case VtfFormat.UV88:
                    destination[j] = (byte)(source[i] + 128);
                    destination[j + 1] = (byte)(source[i + 1] + 128);
                    destination[j + 2] = 128;
                    destination[j + 3] = 255;
                    break;
            }

            if ((format is VtfFormat.RGB888_BLUESCREEN or VtfFormat.BGR888_BLUESCREEN)
                && destination[j] == 0 && destination[j + 1] == 0 && destination[j + 2] == 255)
            {
                destination.Slice(j, 4).Clear();
            }
        }
    }
}
