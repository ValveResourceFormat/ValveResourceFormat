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
/// Writes RGBA float32 images as scanline OpenEXR files with ZIP compression.
/// </summary>
internal static class ExrWriter
{
    private const int Magic = 20000630;
    private const int Version = 2;
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
    /// Encodes tightly packed RGBA float pixels, top row first, as an EXR file.
    /// </summary>
    public static byte[] Write(SKPixmap pixels)
    {
        var width = pixels.Width;
        var height = pixels.Height;

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (pixels.GetPixelSpan<SKColorF>().Length != width * height)
        {
            throw new ArgumentException("Pixel data does not match the image size.", nameof(pixels));
        }

        var header = WriteHeader(width, height);
        var blockCount = (height + LinesPerBlock - 1) / LinesPerBlock;
        var blocks = new EncodedBlock[blockCount];

        try
        {
            // Blocks are independent and each is stored by its index, so the output does not depend on timing.
            // Each worker compresses its blocks into one stream that it keeps for all of them.
            Parallel.For(0, blockCount, () => new MemoryStream(), (blockIndex, _, stream) =>
            {
                blocks[blockIndex] = EncodeBlock(pixels, blockIndex * LinesPerBlock, stream);
                return stream;
            }, stream => stream.Dispose());

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

    private static byte[] WriteHeader(int width, int height)
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
            writer.Write(PixelTypeFloat);

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
    private static EncodedBlock EncodeBlock(SKPixmap pixels, int firstLine, MemoryStream stream)
    {
        var width = pixels.Width;
        var lineCount = Math.Min(LinesPerBlock, pixels.Height - firstLine);
        var rawSize = lineCount * ChannelCount * width * sizeof(float);

        var raw = ArrayPool<byte>.Shared.Rent(rawSize);
        var scratch = ArrayPool<byte>.Shared.Rent(rawSize);

        try
        {
            var rgba = MemoryMarshal.Cast<SKColorF, float>(pixels.GetPixelSpan<SKColorF>());
            var rawSpan = raw.AsSpan(0, rawSize);

            DeinterleaveChannels(rgba, width, firstLine, lineCount, rawSpan);

            stream.SetLength(0);
            var data = CompressZip(rawSpan, scratch.AsSpan(0, rawSize), stream);

            var blockSize = BlockHeaderSize + data.Length;
            var block = ArrayPool<byte>.Shared.Rent(blockSize);
            BinaryPrimitives.WriteInt32LittleEndian(block, firstLine);
            BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(sizeof(int)), data.Length);
            data.CopyTo(block.AsSpan(BlockHeaderSize));

            return new EncodedBlock(block, blockSize);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(raw);
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    private static void DeinterleaveChannels(ReadOnlySpan<float> rgba, int width, int firstLine, int lineCount, Span<byte> destination)
    {
        var position = 0;

        for (var line = 0; line < lineCount; line++)
        {
            var rowStart = (firstLine + line) * width * ChannelCount;

            foreach (var sourceIndex in ChannelSourceIndices)
            {
                for (var x = 0; x < width; x++)
                {
                    var value = rgba[rowStart + x * ChannelCount + sourceIndex];
                    BinaryPrimitives.WriteSingleLittleEndian(destination[position..], value);
                    position += sizeof(float);
                }
            }
        }
    }

    /// <summary>
    /// Returns the data to store for <paramref name="raw"/>: the bytes compressed into <paramref name="stream"/>,
    /// or <paramref name="raw"/> itself when compression does not shrink it.
    /// </summary>
    private static ReadOnlySpan<byte> CompressZip(ReadOnlySpan<byte> raw, Span<byte> scratch, MemoryStream stream)
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

        using (var zlib = new ZLibStream(stream, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(scratch);
        }

        // A block that did not shrink is stored as it was, which is how readers tell the two apart
        if (stream.Length >= raw.Length)
        {
            return raw;
        }

        return stream.GetBuffer().AsSpan(0, (int)stream.Length);
    }
}
