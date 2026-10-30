using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Texture;

internal sealed partial class VtfFile
{
    private const int HeaderLength = 63;

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
        FaceCount = (Header.Flags & VtfFlags.ENVMAP) != 0 ? 6 : 1;

        // Older environment maps can carry an additional spheremap after the six cube faces.
        if (FaceCount == 6 && Header.MinorVersion < 5 && Header.FirstFrame != ushort.MaxValue)
        {
            FaceCount = 7;
        }

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
                if (resource.Tag == SheetResourceType && resource.Flags == 0)
                {
                    sheetOffset = resource.Data;
                    break;
                }
            }
        }

        Skip(stream, Header.HeaderSize - readCount);

        if (imageOffset.HasValue && imageOffset.Value < Header.HeaderSize)
        {
            throw new InvalidDataException("VTF image resource overlaps its header.");
        }

        imageOffset ??= Header.HeaderSize + (long)GetImageDataSize(Header.LowResWidth, Header.LowResHeight, 1, 1, Header.LowResFormat);

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

        var position = (long)Header.HeaderSize;

        // The sheet sits ahead of the image in the files that have one
        if (sheetOffset is { } beforeImage && beforeImage >= position && beforeImage < imageOffset.Value)
        {
            Skip(stream, beforeImage - position);
            sheetData = ReadSheetResource(stream);
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
            sheetData = ReadSheetResource(stream);
        }
    }

    public static VtfHeader ReadHeader(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[HeaderLength];
        stream.ReadExactly(bytes);

        var header = MemoryMarshal.Read<VtfHeader>(bytes);

        if (header.Signature != 0x00465456)
        {
            throw new InvalidDataException("Invalid VTF signature.");
        }

        if (header.MajorVersion != 7 || header.MinorVersion > 5)
        {
            throw new NotSupportedException($"VTF version {header.MajorVersion}.{header.MinorVersion} is not supported.");
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
            VtfFormat.DXT3 or VtfFormat.DXT5 or VtfFormat.ATI2N => 16,
            VtfFormat.I8 or VtfFormat.P8 or VtfFormat.A8 => 1,
            VtfFormat.RGB565 or VtfFormat.IA88 or VtfFormat.BGR565
                or VtfFormat.BGRX5551 or VtfFormat.BGRA4444 or VtfFormat.BGRA5551
                or VtfFormat.UV88 => 2,
            VtfFormat.RGB888 or VtfFormat.BGR888 or VtfFormat.RGB888_BLUESCREEN
                or VtfFormat.BGR888_BLUESCREEN => 3,
            VtfFormat.RGBA8888 or VtfFormat.ABGR8888 or VtfFormat.ARGB8888
                or VtfFormat.BGRA8888 or VtfFormat.BGRX8888 or VtfFormat.UVWQ8888
                or VtfFormat.UVLX8888 or VtfFormat.R32F => 4,
            VtfFormat.RGBA16161616F or VtfFormat.RGBA16161616 => 8,
            VtfFormat.RGB323232F => 12,
            VtfFormat.RGBA32323232F => 16,
            _ => throw new NotSupportedException($"VTF format: {format}")
        };

        var compressed = format is VtfFormat.DXT1 or VtfFormat.DXT1_ONEBITALPHA
            or VtfFormat.DXT3 or VtfFormat.DXT5 or VtfFormat.ATI1N or VtfFormat.ATI2N;
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
