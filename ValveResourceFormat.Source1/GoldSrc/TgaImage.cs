using System.IO;
using SkiaSharp;

namespace ValveResourceFormat.GoldSrc;

/// <summary>
/// Reads the true color and gray TGA images that GoldSrc sky boxes are stored as.
/// </summary>
internal static class TgaImage
{
    private const int HeaderSize = 18;

    private const byte TypeTrueColor = 2;
    private const byte TypeGray = 3;
    private const byte TypeRunLengthTrueColor = 10;
    private const byte TypeRunLengthGray = 11;

    private const byte OriginTopBit = 0x20;

    /// <summary>
    /// Decodes an uncompressed or run length encoded image of 8, 24 or 32 bits per pixel.
    /// </summary>
    public static SKBitmap Decode(byte[] data)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("TGA is truncated.");
        }

        var idLength = data[0];
        var colorMapType = data[1];
        var imageType = data[2];
        var colorMapLength = data[5] | (data[6] << 8);
        var colorMapEntrySize = data[7];
        var width = data[12] | (data[13] << 8);
        var height = data[14] | (data[15] << 8);
        var bitsPerPixel = data[16];
        var descriptor = data[17];

        var isRunLength = imageType is TypeRunLengthTrueColor or TypeRunLengthGray;

        if (imageType is not (TypeTrueColor or TypeGray or TypeRunLengthTrueColor or TypeRunLengthGray) || colorMapType != 0)
        {
            throw new InvalidDataException($"TGA image type {imageType} is not supported.");
        }

        if (width <= 0 || height <= 0 || bitsPerPixel is not (8 or 24 or 32))
        {
            throw new InvalidDataException($"TGA {width}x{height} at {bitsPerPixel} bits is not supported.");
        }

        var bytesPerPixel = bitsPerPixel / 8;
        var position = HeaderSize + idLength + (colorMapLength * ((colorMapEntrySize + 7) / 8));
        var pixelCount = width * height;
        var raw = new byte[pixelCount * bytesPerPixel];

        if (isRunLength)
        {
            ReadRunLength(data, position, raw, bytesPerPixel);
        }
        else
        {
            if (position + raw.Length > data.Length)
            {
                throw new InvalidDataException("TGA pixel data is truncated.");
            }

            Buffer.BlockCopy(data, position, raw, 0, raw.Length);
        }

        var isTopFirst = (descriptor & OriginTopBit) != 0;
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

        try
        {
            var pixels = bitmap.GetPixelSpan();

            for (var y = 0; y < height; y++)
            {
                var sourceRow = isTopFirst ? y : height - 1 - y;

                for (var x = 0; x < width; x++)
                {
                    var source = ((sourceRow * width) + x) * bytesPerPixel;
                    var target = ((y * width) + x) * 4;

                    if (bytesPerPixel == 1)
                    {
                        pixels[target] = raw[source];
                        pixels[target + 1] = raw[source];
                        pixels[target + 2] = raw[source];
                        pixels[target + 3] = 255;
                        continue;
                    }

                    pixels[target] = raw[source + 2];
                    pixels[target + 1] = raw[source + 1];
                    pixels[target + 2] = raw[source];
                    pixels[target + 3] = bytesPerPixel == 4 ? raw[source + 3] : (byte)255;
                }
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static void ReadRunLength(byte[] data, int position, byte[] raw, int bytesPerPixel)
    {
        var written = 0;

        while (written < raw.Length)
        {
            if (position >= data.Length)
            {
                throw new InvalidDataException("TGA pixel data is truncated.");
            }

            var packet = data[position++];
            var count = (packet & 0x7f) + 1;
            var isRepeat = (packet & 0x80) != 0;
            var packetBytes = Math.Min(count * bytesPerPixel, raw.Length - written);

            if (isRepeat)
            {
                if (position + bytesPerPixel > data.Length)
                {
                    throw new InvalidDataException("TGA pixel data is truncated.");
                }

                for (var i = 0; i < packetBytes; i += bytesPerPixel)
                {
                    Buffer.BlockCopy(data, position, raw, written + i, bytesPerPixel);
                }

                position += bytesPerPixel;
            }
            else
            {
                if (position + packetBytes > data.Length)
                {
                    throw new InvalidDataException("TGA pixel data is truncated.");
                }

                Buffer.BlockCopy(data, position, raw, written, packetBytes);
                position += packetBytes;
            }

            written += packetBytes;
        }
    }
}
