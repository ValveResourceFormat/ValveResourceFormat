using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using SkiaSharp;

namespace ValveResourceFormat.IO;

/// <summary>
/// Writes RGBA float16 and float32 images as scanline OpenEXR files with ZIP compression.
/// </summary>
internal static class ExrWriter
{
    private const int Magic = 20000630;
    private const int Version = 2;
    private const int PixelTypeHalf = 1;
    private const int PixelTypeFloat = 2;
    private const byte CompressionZip = 3;
    private const int LinesPerBlock = 16;
    private const int ChannelCount = 4;
    private const int BlockHeaderSize = 2 * sizeof(int);

    // Pixel type, pLinear, three reserved bytes, x and y sampling
    private const int ChannelInfoSize = 16;

    // Channels are stored in alphabetical order, these are their indices in an RGBA pixel.
    private static readonly int[] ChannelSourceIndices = [3, 2, 1, 0];
    private static readonly string[] ChannelNames = ["A", "B", "G", "R"];

    private readonly record struct EncodedBlock(byte[] Buffer, int Length);

    /// <summary>
    /// Encodes tightly packed RGBA half or float pixels, top row first, as an EXR file.
    /// Half pixels are stored as HALF channels and float pixels as FLOAT channels.
    /// </summary>
    public static byte[] Write(SKPixmap pixels)
    {
        var width = pixels.Width;
        var height = pixels.Height;

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var colorType = pixels.ColorType;

        if (colorType != SKColorType.RgbaF16 && colorType != SKColorType.RgbaF32)
        {
            throw new ArgumentException($"Pixels must be RgbaF16 or RgbaF32, got {colorType}.", nameof(pixels));
        }

        var isHalf = colorType == SKColorType.RgbaF16;
        var channelSize = isHalf ? sizeof(ushort) : sizeof(float);

        if (pixels.GetPixelSpan<byte>().Length != width * height * ChannelCount * channelSize)
        {
            throw new ArgumentException("Pixel data does not match the image size.", nameof(pixels));
        }

        var header = WriteHeader(width, height, isHalf ? PixelTypeHalf : PixelTypeFloat);
        var blockCount = MathUtils.DivideRoundUp(height, LinesPerBlock);
        var blocks = new EncodedBlock[blockCount];

        try
        {
            // Blocks are independent and each is stored by its index, so the output does not depend on timing.
            Parallel.For(0, blockCount, blockIndex =>
            {
                blocks[blockIndex] = EncodeBlock(pixels, channelSize, blockIndex * LinesPerBlock);
            });

            var offsetTableSize = blockCount * sizeof(long);
            var totalSize = header.Length + offsetTableSize;

            foreach (var block in blocks)
            {
                totalSize += block.Length;
            }

            var result = new byte[totalSize];
            header.CopyTo(result, 0);

            var offsetTablePosition = header.Length;
            var dataPosition = header.Length + offsetTableSize;

            for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
            {
                BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(offsetTablePosition), dataPosition);
                offsetTablePosition += sizeof(long);

                var block = blocks[blockIndex];
                block.Buffer.AsSpan(0, block.Length).CopyTo(result.AsSpan(dataPosition));
                dataPosition += block.Length;
            }

            return result;
        }
        finally
        {
            foreach (var block in blocks)
            {
                if (block.Buffer != null)
                {
                    ArrayPool<byte>.Shared.Return(block.Buffer);
                }
            }
        }
    }

    private static byte[] WriteHeader(int width, int height, int pixelType)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(Magic);
        writer.Write(Version);

        var channelListSize = 1;

        foreach (var name in ChannelNames)
        {
            channelListSize += name.Length + 1 + ChannelInfoSize;
        }

        WriteAttributeHeader(writer, "channels", "chlist", channelListSize);

        foreach (var name in ChannelNames)
        {
            WriteNullTerminated(writer, name);
            writer.Write(pixelType);

            // pLinear followed by three reserved bytes
            writer.Write((byte)1);
            writer.Write(new byte[3]);

            // x and y sampling
            writer.Write(1);
            writer.Write(1);
        }

        writer.Write((byte)0);

        WriteAttributeHeader(writer, "compression", "compression", sizeof(byte));
        writer.Write(CompressionZip);

        WriteWindowAttribute(writer, "dataWindow", width, height);
        WriteWindowAttribute(writer, "displayWindow", width, height);

        // Increasing Y, scanlines stored top to bottom
        WriteAttributeHeader(writer, "lineOrder", "lineOrder", sizeof(byte));
        writer.Write((byte)0);

        WriteAttributeHeader(writer, "pixelAspectRatio", "float", sizeof(float));
        writer.Write(1f);

        WriteAttributeHeader(writer, "screenWindowCenter", "v2f", 2 * sizeof(float));
        writer.Write(0f);
        writer.Write(0f);

        WriteAttributeHeader(writer, "screenWindowWidth", "float", sizeof(float));
        writer.Write(1f);

        writer.Write((byte)0);
        writer.Flush();

        return stream.ToArray();
    }

    private static void WriteAttributeHeader(BinaryWriter writer, string name, string type, int size)
    {
        WriteNullTerminated(writer, name);
        WriteNullTerminated(writer, type);
        writer.Write(size);
    }

    private static void WriteWindowAttribute(BinaryWriter writer, string name, int width, int height)
    {
        WriteAttributeHeader(writer, name, "box2i", 4 * sizeof(int));
        writer.Write(0);
        writer.Write(0);
        writer.Write(width - 1);
        writer.Write(height - 1);
    }

    private static void WriteNullTerminated(BinaryWriter writer, string text)
    {
        writer.Write(Encoding.ASCII.GetBytes(text));
        writer.Write((byte)0);
    }

    /// <summary>
    /// Builds one block in an array rented from the shared pool: the first scanline number, the data size, and the data.
    /// </summary>
    private static EncodedBlock EncodeBlock(SKPixmap pixels, int channelSize, int firstLine)
    {
        var width = pixels.Width;
        var lineCount = Math.Min(LinesPerBlock, pixels.Height - firstLine);
        var rawSize = lineCount * ChannelCount * width * channelSize;

        using var raw = new RentedBuffer<byte>(rawSize);
        using var scratch = new RentedBuffer<byte>(rawSize);

        // One byte smaller than raw, so compression fails exactly when the block would not shrink
        using var destination = new RentedBuffer<byte>(rawSize - 1);

        if (channelSize == sizeof(ushort))
        {
            var rgba = MemoryMarshal.Cast<byte, Half>(pixels.GetPixelSpan<byte>());
            DeinterleaveChannels(rgba, width, firstLine, lineCount, raw.Span);
        }
        else
        {
            var rgba = MemoryMarshal.Cast<byte, float>(pixels.GetPixelSpan<byte>());
            DeinterleaveChannels(rgba, width, firstLine, lineCount, raw.Span);
        }

        var data = CompressZip(raw.Span, scratch.Span, destination.Span);

        var blockSize = BlockHeaderSize + data.Length;
        var block = ArrayPool<byte>.Shared.Rent(blockSize);
        BinaryPrimitives.WriteInt32LittleEndian(block, firstLine);
        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(sizeof(int)), data.Length);
        data.CopyTo(block.AsSpan(BlockHeaderSize));

        return new EncodedBlock(block, blockSize);
    }

    private static void DeinterleaveChannels<T>(ReadOnlySpan<T> rgba, int width, int firstLine, int lineCount, Span<byte> destination)
        where T : unmanaged
    {
        var output = MemoryMarshal.Cast<byte, T>(destination);
        var position = 0;

        for (var line = 0; line < lineCount; line++)
        {
            var rowStart = (firstLine + line) * width * ChannelCount;

            foreach (var sourceIndex in ChannelSourceIndices)
            {
                for (var x = 0; x < width; x++)
                {
                    output[position] = rgba[rowStart + x * ChannelCount + sourceIndex];
                    position++;
                }
            }
        }
    }

    /// <summary>
    /// Returns the data to store for <paramref name="raw"/>: the bytes compressed into <paramref name="destination"/>,
    /// or <paramref name="raw"/> itself when compression does not shrink it.
    /// </summary>
    private static ReadOnlySpan<byte> CompressZip(ReadOnlySpan<byte> raw, Span<byte> scratch, Span<byte> destination)
    {
        // Even bytes go to the first half and odd bytes to the second, so similar bytes sit together
        var half = raw.Length / 2;

        for (var i = 0; i < half; i++)
        {
            scratch[i] = raw[2 * i];
            scratch[half + i] = raw[2 * i + 1];
        }

        // Delta predictor, biased so that small differences stay near 128
        for (var i = scratch.Length - 1; i > 0; i--)
        {
            scratch[i] = (byte)(scratch[i] - scratch[i - 1] + 128);
        }

        if (!ZLibEncoder.TryCompress(scratch, destination, out var written))
        {
            // A block that did not shrink is stored as it was, which is how readers tell the two apart
            return raw;
        }

        return destination[..written];
    }
}
