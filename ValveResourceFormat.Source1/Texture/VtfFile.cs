using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Texture;

internal sealed partial class VtfFile
{
    private const int HeaderLength = 63;
    private const uint CompressionResourceType = 0x00435841;
    private const short CompressionMethodZstd = 93;

    private readonly byte[]? pixelData;
    private readonly (int Offset, int Length)[] mipData = [];

    public VtfHeader Header { get; }
    public int Width => Header.Width;
    public int Height => Header.Height;
    public int Depth { get; }
    public int MipmapCount { get; }
    public int FrameCount { get; }
    public int FaceCount { get; }
    public VtfFlags Flags => Header.Flags;
    public VtfFormat Format => Header.HiResFormat;
    public Vector3 Reflectivity => new(Header.ReflectivityR, Header.ReflectivityG, Header.ReflectivityB);

    /// <summary>The entries of the resource directory of the header. Empty before version 7.3.</summary>
    public IReadOnlyList<VtfResource> Resources => resources;

    private readonly List<VtfResource> resources = [];

    public VtfFile(Stream stream, bool onlyHeader = false)
    {
        ArgumentNullException.ThrowIfNull(stream);

        Header = ReadHeader(stream);

        long readCount = HeaderLength;
        Span<byte> buffer = stackalloc byte[8];
        Depth = 1;

        if (Header.MinorVersion >= 2)
        {
            stream.ReadExactly(buffer[..2]);
            Depth = BinaryPrimitives.ReadUInt16LittleEndian(buffer);
            readCount += 2;
        }

        MipmapCount = Header.MipMapCount;
        FrameCount = Header.Frames;
        var maxDimension = Math.Max(Math.Max(Header.Width, Header.Height), Depth);
        var maxMips = 0;
        for (var dimension = maxDimension; dimension > 0; dimension >>= 1)
        {
            maxMips++;
        }

        if (Header.Width == 0 || Header.Height == 0 || Depth == 0 || FrameCount == 0
            || MipmapCount == 0 || MipmapCount > maxMips || Header.HiResFormat == VtfFormat.NONE)
        {
            throw new InvalidDataException("Invalid VTF dimensions, frames, mip count or image format.");
        }

        long? imageOffset = null;
        long? sheetOffset = null;
        long? compressionOffset = null;

        if (Header.MinorVersion >= 3)
        {
            Skip(stream, 3);
            stream.ReadExactly(buffer[..4]);
            var resourceCount = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
            Skip(stream, 8);
            readCount += 15;

            if (readCount + resourceCount * 8L > Header.HeaderSize)
            {
                throw new InvalidDataException("VTF resource directory exceeds its header.");
            }

            for (var i = 0U; i < resourceCount; i++)
            {
                stream.ReadExactly(buffer);
                readCount += 8;

                var type = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
                resources.Add(new VtfResource(type, BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..])));

                if ((type & 0x00ffffff) != 0x30)
                {
                    continue;
                }

                if (imageOffset.HasValue || (type & 0xff000000) != 0)
                {
                    throw new InvalidDataException("Invalid or duplicate VTF high-resolution image resource.");
                }

                imageOffset = BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..]);
            }

            if (resourceCount != 0 && !imageOffset.HasValue)
            {
                throw new InvalidDataException("VTF high-resolution image resource is missing.");
            }

            foreach (var resource in resources)
            {
                if (resource.Flags != 0)
                {
                    continue;
                }

                if (resource.Tag == SheetResourceType)
                {
                    sheetOffset ??= resource.Data;
                }
                else if (resource.Tag == CompressionResourceType)
                {
                    compressionOffset ??= resource.Data;
                }
            }
        }

        Skip(stream, Header.HeaderSize - readCount);

        if (imageOffset.HasValue && imageOffset.Value < Header.HeaderSize)
        {
            throw new InvalidDataException("VTF image resource overlaps its header.");
        }

        imageOffset ??= Header.HeaderSize + (long)GetImageDataSize(Header.LowResWidth, Header.LowResHeight, 1, 1, Header.LowResFormat);

        FaceCount = GetFaceCount(stream, imageOffset.Value);

        if (onlyHeader)
        {
            return;
        }

        mipData = new (int, int)[MipmapCount];
        var offset = 0;

        for (var mip = MipmapCount - 1; mip >= 0; mip--)
        {
            var length = GetImageDataSize(Math.Max(1, Header.Width >> mip), Math.Max(1, Header.Height >> mip), 1, 1, Header.HiResFormat);
            mipData[mip] = (offset, length);
            offset = checked(offset + checked(length * FrameCount * FaceCount * Math.Max(1, Depth >> mip)));
        }

        if (compressionOffset is { } compression)
        {
            pixelData = ReadCompressedImage(stream, imageOffset.Value, compression, sheetOffset, offset);
            return;
        }

        var position = (long)Header.HeaderSize;

        // The sheet sits ahead of the image in the files that have one
        if (sheetOffset is { } beforeImage && beforeImage >= position && beforeImage < imageOffset.Value)
        {
            Skip(stream, beforeImage - position);
            sheetData = ReadResourceData(stream);
            position = beforeImage + 4 + sheetData.Length;
        }

        Skip(stream, imageOffset.Value - position);

        if (stream.CanSeek && offset > stream.Length - stream.Position)
        {
            throw new InvalidDataException("VTF image data is truncated or too large.");
        }

        pixelData = new byte[offset];
        stream.ReadExactly(pixelData);
        position = imageOffset.Value + offset;

        if (sheetData == null && sheetOffset is { } afterImage && afterImage >= position)
        {
            Skip(stream, afterImage - position);
            sheetData = ReadResourceData(stream);
        }
    }

    private byte[] ReadCompressedImage(Stream stream, long imageOffset, long compressionOffset, long? sheetOffset, int imageLength)
    {
        if (Depth > 1)
        {
            throw new NotSupportedException("Compressed VTF volume textures are not supported.");
        }

        // The stream is at the end of the header, and the resource offsets are in any order after it
        using var body = new MemoryStream();
        stream.CopyTo(body);

        var bodyStart = (long)Header.HeaderSize;

        if (sheetOffset is { } sheet && sheet >= bodyStart)
        {
            SeekBody(body, sheet, bodyStart);
            sheetData = ReadResourceData(body);
        }

        SeekBody(body, compressionOffset, bodyStart);
        var compression = ReadResourceData(body);

        if (compression.Length < 4 || compression.Length % 4 != 0)
        {
            throw new InvalidDataException("Invalid VTF compression resource.");
        }

        var method = BinaryPrimitives.ReadInt16LittleEndian(compression.AsSpan(2));

        if (method != CompressionMethodZstd)
        {
            throw new NotSupportedException($"VTF compression method: {method}");
        }

        var chunkCount = compression.Length / 4 - 1;

        if (chunkCount != MipmapCount * FrameCount * FaceCount)
        {
            throw new InvalidDataException("VTF compression resource does not match the image layout.");
        }

        SeekBody(body, imageOffset, bodyStart);

        var compressed = body.GetBuffer().AsSpan((int)body.Position, (int)(body.Length - body.Position));
        var source = 0;
        var chunk = 0;
        var destination = 0;

        var pixels = new byte[imageLength];

        using var decompressor = new ZstdSharp.Decompressor();

        // Each mip, smallest first, holds one zstd frame per frame and face
        for (var mip = MipmapCount - 1; mip >= 0; mip--)
        {
            var length = mipData[mip].Length;

            for (var i = 0; i < FrameCount * FaceCount; i++)
            {
                var size = BinaryPrimitives.ReadUInt32LittleEndian(compression.AsSpan(4 + chunk * 4));
                chunk++;

                if (size > compressed.Length - source)
                {
                    throw new InvalidDataException("VTF image data is truncated or too large.");
                }

                var output = pixels.AsSpan(destination, length);

                // A frame without content leaves its image zeroed
                if (!decompressor.TryUnwrap(compressed.Slice(source, (int)size), output, out var written) || (written != length && written != 0))
                {
                    throw new InvalidDataException("Invalid compressed VTF image data.");
                }

                source += (int)size;
                destination += length;
            }
        }

        return pixels;
    }

    private static void SeekBody(MemoryStream body, long offset, long bodyStart)
    {
        if (offset < bodyStart || offset - bodyStart > body.Length)
        {
            throw new InvalidDataException("VTF resource offset is outside the file.");
        }

        body.Position = offset - bodyStart;
    }

    // Environment maps of versions 7.1 to 7.4 carry a spheremap after the six cube faces of each frame and mip when the image data has room for it.
    private int GetFaceCount(Stream stream, long imageOffset)
    {
        if ((Header.Flags & VtfFlags.ENVMAP) == 0)
        {
            return 1;
        }

        if (Header.MinorVersion == 0 || Header.MinorVersion >= 5)
        {
            return 6;
        }

        if (!stream.CanSeek)
        {
            return 7;
        }

        var available = stream.Length - stream.Position - (imageOffset - Header.HeaderSize);
        var faceSize = (long)GetImageDataSize(Header.Width, Header.Height, Depth, MipmapCount, Header.HiResFormat) * FrameCount;

        return available >= faceSize * 7 ? 7 : 6;
    }

    public static VtfHeader ReadHeader(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[HeaderLength];
        stream.ReadExactly(bytes);

        var header = MemoryMarshal.Read<VtfHeader>(bytes);

        if (header.Signature != (int)Source1Magic.Texture)
        {
            throw new UnexpectedMagicException("Invalid VTF signature", header.Signature, nameof(header.Signature));
        }

        if (header.MajorVersion != 7)
        {
            throw new UnexpectedMagicException("Unsupported VTF major version, supported version is 7", header.MajorVersion, nameof(header.MajorVersion));
        }

        if (header.MinorVersion > 6)
        {
            throw new UnexpectedMagicException("Unsupported VTF minor version, supported versions are 7.0 to 7.6", header.MinorVersion, nameof(header.MinorVersion));
        }

        if (header.HiResFormat == VtfFormat.ATI2N_ALT)
        {
            header.HiResFormat = VtfFormat.ATI2N;
        }

        return header;
    }

    public static int GetImageDataSize(int width, int height, int depth, int mipCount, VtfFormat format)
    {
        if (width < 0 || height < 0 || depth < 0 || mipCount < 0 || mipCount > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Invalid image dimensions or mip count.");
        }

        if (mipCount == 0 || format == VtfFormat.NONE || (width == 0 && height == 0))
        {
            return 0;
        }

        var bytesPerPixel = format switch
        {
            VtfFormat.DXT1 or VtfFormat.DXT1_ONEBITALPHA or VtfFormat.ATI1N => 8,
            VtfFormat.DXT3 or VtfFormat.DXT5 or VtfFormat.ATI2N or VtfFormat.BC7 => 16,
            VtfFormat.I8 or VtfFormat.P8 or VtfFormat.A8 => 1,
            VtfFormat.RGB565 or VtfFormat.IA88 or VtfFormat.BGR565
                or VtfFormat.BGRX5551 or VtfFormat.BGRA4444 or VtfFormat.BGRA5551
                or VtfFormat.UV88 => 2,
            VtfFormat.RGB888 or VtfFormat.BGR888 or VtfFormat.RGB888_BLUESCREEN
                or VtfFormat.BGR888_BLUESCREEN => 3,
            VtfFormat.RGBA8888 or VtfFormat.ABGR8888 or VtfFormat.ARGB8888
                or VtfFormat.BGRA8888 or VtfFormat.BGRX8888 or VtfFormat.UVWQ8888
                or VtfFormat.UVLX8888 or VtfFormat.R32F or VtfFormat.RG1616F => 4,
            VtfFormat.RGBA16161616F or VtfFormat.RGBA16161616 => 8,
            VtfFormat.RGB323232F => 12,
            VtfFormat.RGBA32323232F => 16,
            _ => throw new NotSupportedException($"VTF format: {format}")
        };

        var compressed = format is VtfFormat.DXT1 or VtfFormat.DXT1_ONEBITALPHA
            or VtfFormat.DXT3 or VtfFormat.DXT5 or VtfFormat.ATI1N or VtfFormat.ATI2N
            or VtfFormat.BC7;
        var size = 0;

        checked
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            depth = Math.Max(1, depth);

            for (var mip = 0; mip < mipCount; mip++)
            {
                size += compressed
                    ? ((width + 3) / 4) * ((height + 3) / 4) * depth * bytesPerPixel
                    : width * height * depth * bytesPerPixel;

                if (width == 1 && height == 1 && depth == 1)
                {
                    break;
                }

                width = Math.Max(1, width >> 1);
                height = Math.Max(1, height >> 1);
                depth = Math.Max(1, depth >> 1);
            }
        }

        return size;
    }

    public Span<byte> GetHiResPixelData(int mip, int frame = 0, int face = 0, int slice = 0)
    {
        if (pixelData is null)
        {
            throw new InvalidOperationException("Only the VTF header was loaded.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(mip);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(mip, MipmapCount);
        ArgumentOutOfRangeException.ThrowIfNegative(frame);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(frame, FrameCount);
        ArgumentOutOfRangeException.ThrowIfNegative(face);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(face, FaceCount);

        var depth = Math.Max(1, Depth >> mip);
        ArgumentOutOfRangeException.ThrowIfNegative(slice);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slice, depth);

        var (offset, length) = mipData[mip];
        offset += length * (slice + depth * (face + FaceCount * frame));

        return pixelData.AsSpan(offset, length);
    }

    private static void Skip(Stream stream, long count)
    {
        if (count < 0)
        {
            throw new InvalidDataException("VTF data offset precedes the header.");
        }

        if (count == 0)
        {
            return;
        }

        if (stream.CanSeek)
        {
            if (count > stream.Length - stream.Position)
            {
                throw new EndOfStreamException("VTF data offset exceeds the stream.");
            }

            stream.Seek(count, SeekOrigin.Current);
            return;
        }

        Span<byte> buffer = stackalloc byte[4096];

        while (count > 0)
        {
            var length = (int)Math.Min(count, buffer.Length);
            stream.ReadExactly(buffer[..length]);
            count -= length;
        }
    }
}
