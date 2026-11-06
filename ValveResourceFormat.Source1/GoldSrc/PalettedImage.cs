using System.Buffers.Binary;
using System.IO;
using System.Text;
using SkiaSharp;

namespace ValveResourceFormat.GoldSrc;

/// <summary>
/// A 256 color palettized image with an optional mip chain, as stored in WAD3 files, BSP texture lumps and sprites.
/// </summary>
internal sealed class PalettedImage
{
    public const int PaletteColorCount = 256;
    public const int PaletteSize = PaletteColorCount * 3;
    private const int MipTextureHeaderSize = 40;
    private const int MipCount = 4;

    public required string Name { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>Index data per mip level, the first being the full size image.</summary>
    public required byte[][] Mips { get; init; }

    /// <summary>RGB triplets, 256 colors.</summary>
    public required byte[] Palette { get; init; }

    /// <summary>Whether the last palette index is the transparent color. Set for textures named with a leading brace.</summary>
    public bool IsMasked => Name.StartsWith('{');

    /// <summary>
    /// Reads a miptex, as stored in a WAD3 lump or a BSP texture lump. Returns null when the pixel data is not embedded.
    /// </summary>
    public static PalettedImage? ReadMipTexture(ReadOnlySpan<byte> data)
    {
        if (data.Length < MipTextureHeaderSize)
        {
            throw new InvalidDataException("Miptex is too small.");
        }

        var name = ReadName(data[..16]);
        var width = BinaryPrimitives.ReadInt32LittleEndian(data[16..]);
        var height = BinaryPrimitives.ReadInt32LittleEndian(data[20..]);
        var firstOffset = BinaryPrimitives.ReadInt32LittleEndian(data[24..]);

        if (firstOffset == 0)
        {
            return null;
        }

        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
        {
            throw new InvalidDataException($"Miptex \"{name}\" has invalid dimensions {width}x{height}.");
        }

        var mips = new byte[MipCount][];

        for (var i = 0; i < MipCount; i++)
        {
            var offset = BinaryPrimitives.ReadInt32LittleEndian(data[(24 + (i * 4))..]);
            var size = Math.Max(1, width >> i) * Math.Max(1, height >> i);

            if (offset < 0 || offset > data.Length - size)
            {
                throw new InvalidDataException($"Miptex \"{name}\" mip {i} is out of range.");
            }

            mips[i] = data.Slice(offset, size).ToArray();
        }

        var lastOffset = BinaryPrimitives.ReadInt32LittleEndian(data[36..]);
        var paletteOffset = lastOffset + mips[MipCount - 1].Length;

        return new PalettedImage
        {
            Name = name,
            Width = width,
            Height = height,
            Mips = mips,
            Palette = ReadPalette(data, paletteOffset),
        };
    }

    /// <summary>
    /// Reads a qpic, an image without mips and a name.
    /// </summary>
    public static PalettedImage ReadQpic(string name, ReadOnlySpan<byte> data)
    {
        const int HeaderSize = 8;
        const int MaxDimension = 4096;

        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException($"Qpic \"{name}\" is truncated.");
        }

        var width = BinaryPrimitives.ReadInt32LittleEndian(data);
        var height = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);

        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension)
        {
            throw new InvalidDataException($"Qpic \"{name}\" has invalid dimensions {width}x{height}.");
        }

        var pixelCount = width * height;

        if (data.Length < HeaderSize + pixelCount)
        {
            throw new InvalidDataException($"Qpic \"{name}\" is truncated.");
        }

        return new PalettedImage
        {
            Name = name,
            Width = width,
            Height = height,
            Mips = [data.Slice(HeaderSize, pixelCount).ToArray()],
            Palette = ReadPalette(data, HeaderSize + pixelCount),
        };
    }

    /// <summary>
    /// Reads a font lump as one image of all the glyph rows.
    /// </summary>
    public static PalettedImage ReadFont(string name, ReadOnlySpan<byte> data)
    {
        const int GlyphCount = 256;
        const int HeaderSize = 16;
        const int GlyphSheetWidth = 256;
        const int MaxHeight = 4096;

        var pixelOffset = HeaderSize + (GlyphCount * 4);

        if (data.Length < pixelOffset)
        {
            throw new InvalidDataException($"Font \"{name}\" is truncated.");
        }

        var width = GlyphSheetWidth;
        var height = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);

        if (height <= 0 || height > MaxHeight)
        {
            throw new InvalidDataException($"Font \"{name}\" has invalid height {height}.");
        }

        var pixelCount = width * height;

        if (data.Length < pixelOffset + pixelCount)
        {
            throw new InvalidDataException($"Font \"{name}\" is truncated.");
        }

        return new PalettedImage
        {
            Name = name,
            Width = width,
            Height = height,
            Mips = [data.Slice(pixelOffset, pixelCount).ToArray()],
            Palette = ReadPalette(data, pixelOffset + pixelCount),
        };
    }

    /// <summary>
    /// Reads the palette that starts with a 16 bit color count, as stored after the pixels.
    /// </summary>
    public static byte[] ReadPalette(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset > data.Length - 2 - PaletteSize)
        {
            throw new InvalidDataException("Palette is out of range.");
        }

        return data.Slice(offset + 2, PaletteSize).ToArray();
    }

    /// <summary>
    /// Reads a null terminated, fixed size name.
    /// </summary>
    public static string ReadName(ReadOnlySpan<byte> field)
    {
        var length = field.IndexOf((byte)0);
        return Encoding.Latin1.GetString(length < 0 ? field : field[..length]);
    }

    /// <summary>Whether a texel of the base level uses the last palette entry.</summary>
    public bool HasTransparentColor => Mips[0].Contains((byte)(PaletteColorCount - 1));

    /// <summary>
    /// Expands one mip level to RGBA. When <paramref name="transparentLastColor"/> is set, the last palette entry becomes transparent black.
    /// </summary>
    public byte[] ToRgba(int mip, bool transparentLastColor)
    {
        var indices = Mips[mip];
        var rgba = new byte[indices.Length * 4];

        for (var i = 0; i < indices.Length; i++)
        {
            var index = indices[i];
            var color = index * 3;

            if (transparentLastColor && index == PaletteColorCount - 1)
            {
                continue;
            }

            rgba[i * 4] = Palette[color];
            rgba[(i * 4) + 1] = Palette[color + 1];
            rgba[(i * 4) + 2] = Palette[color + 2];
            rgba[(i * 4) + 3] = 255;
        }

        return rgba;
    }

    /// <summary>
    /// Gives each pixel of an RGBA image whose four channels are all zero the average color of the neighbours
    /// that are not fully transparent, so that texture filtering does not pull black into the edges of the opaque pixels.
    /// </summary>
    internal static void FillTransparentColors(byte[] rgba, int width, int height)
    {
        var source = (byte[])rgba.Clone();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var texel = ((y * width) + x) * 4;

                if (source[texel] != 0 || source[texel + 1] != 0 || source[texel + 2] != 0 || source[texel + 3] != 0)
                {
                    continue;
                }

                var sumR = 0;
                var sumG = 0;
                var sumB = 0;
                var count = 0;

                for (var ny = Math.Max(y - 1, 0); ny <= Math.Min(y + 1, height - 1); ny++)
                {
                    for (var nx = Math.Max(x - 1, 0); nx <= Math.Min(x + 1, width - 1); nx++)
                    {
                        var neighbour = ((ny * width) + nx) * 4;

                        if (source[neighbour + 3] == 0)
                        {
                            continue;
                        }

                        sumR += source[neighbour];
                        sumG += source[neighbour + 1];
                        sumB += source[neighbour + 2];
                        count++;
                    }
                }

                count = Math.Max(count, 1);
                rgba[texel] = (byte)(sumR / count);
                rgba[texel + 1] = (byte)(sumG / count);
                rgba[texel + 2] = (byte)(sumB / count);
            }
        }
    }

    /// <summary>
    /// Decodes one mip level to a bitmap.
    /// </summary>
    public SKBitmap ToBitmap(int mip = 0) => ToBitmap(mip, IsMasked);

    /// <inheritdoc cref="ToBitmap(int)"/>
    public SKBitmap ToBitmap(int mip, bool transparentLastColor)
    {
        var rgba = ToRgba(mip, transparentLastColor);
        var width = Math.Max(1, Width >> mip);
        var height = Math.Max(1, Height >> mip);
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

        try
        {
            using var pixmap = bitmap.PeekPixels();
            rgba.CopyTo(pixmap.GetPixelSpan<byte>());
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
}
