using System.Buffers.Binary;
using System.IO;
using ValveResourceFormat.Source1;

namespace ValveResourceFormat.GoldSrc;

/// <summary>
/// A version 2 sprite.
/// </summary>
internal sealed class SprFile
{
    public const int SupportedVersion = 2;
    private const int Identifier = 0x50534449;
    private const int HeaderSize = 40;

    public SprType Type { get; }
    public SprTextureFormat TextureFormat { get; }
    public float BoundingRadius { get; }
    public int Width { get; }
    public int Height { get; }
    public float BeamLength { get; }
    public int SyncType { get; }

    /// <summary>RGB triplets, 256 colors. The last entry is transparent for index alpha and alpha test sprites.</summary>
    public byte[] Palette { get; }

    public SprFrame[] Frames { get; }

    public SprFile(byte[] data)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("File is too small to be a sprite.");
        }

        var identifier = BinaryPrimitives.ReadInt32LittleEndian(data);

        if (identifier != Identifier)
        {
            throw new UnexpectedMagicException("Not a sprite", identifier, nameof(identifier));
        }

        var version = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));

        if (version != SupportedVersion)
        {
            throw new UnexpectedMagicException($"Unsupported sprite version, supported version is {SupportedVersion}", version, nameof(version));
        }

        Type = (SprType)BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8));
        TextureFormat = (SprTextureFormat)BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(12));
        BoundingRadius = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(16));
        Width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(20));
        Height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(24));
        var frameCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(28));
        BeamLength = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(32));
        SyncType = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(36));

        Palette = PalettedImage.ReadPalette(data, HeaderSize);

        if (frameCount < 1 || frameCount > 4096)
        {
            throw new InvalidDataException($"Sprite has an invalid frame count {frameCount}.");
        }

        var frames = new List<SprFrame>(frameCount);
        var position = HeaderSize + 2 + PalettedImage.PaletteSize;

        for (var i = 0; i < frameCount; i++)
        {
            var isGroup = ReadInt(data, position) != 0;
            position += 4;

            if (!isGroup)
            {
                frames.Add(ReadFrame(data, ref position));
                continue;
            }

            var groupCount = ReadInt(data, position);
            position += 4;

            if (groupCount < 1 || position + (groupCount * 4L) > data.Length)
            {
                throw new InvalidDataException("Sprite frame group is out of range.");
            }

            position += groupCount * 4;

            for (var j = 0; j < groupCount; j++)
            {
                frames.Add(ReadFrame(data, ref position));
            }
        }

        Frames = [.. frames];
    }

    public static SprFile Open(Stream stream) => new(Source1Streams.ReadAll(stream));

    /// <summary>
    /// Whether the last palette color is transparent.
    /// </summary>
    public bool HasTransparentLastColor => TextureFormat is SprTextureFormat.IndexAlpha or SprTextureFormat.AlphaTest;

    /// <summary>
    /// Wraps a frame in an image that shares the sprite palette.
    /// </summary>
    public PalettedImage GetImage(int frame, string name)
    {
        var data = Frames[frame];

        return new PalettedImage
        {
            Name = name,
            Width = data.Width,
            Height = data.Height,
            Mips = [data.Pixels],
            Palette = Palette,
        };
    }

    /// <summary>
    /// Expands a frame to RGBA as the sprite is drawn. An index alpha sprite has one color and takes its opacity from
    /// the pixel values. The transparent last color of the other formats takes the color of its opaque neighbours,
    /// which filtering would otherwise pull into the edges of the opaque texels.
    /// </summary>
    public byte[] ToRgba(int frame)
    {
        if (TextureFormat != SprTextureFormat.IndexAlpha)
        {
            var rgba = GetImage(frame, string.Empty).ToRgba(0, HasTransparentLastColor);

            if (HasTransparentLastColor)
            {
                PalettedImage.FillTransparentColors(rgba, Frames[frame].Width, Frames[frame].Height);
            }

            return rgba;
        }

        var pixels = Frames[frame].Pixels;
        var result = new byte[pixels.Length * 4];
        var color = (PalettedImage.PaletteColorCount - 1) * 3;

        for (var i = 0; i < pixels.Length; i++)
        {
            result[i * 4] = Palette[color];
            result[(i * 4) + 1] = Palette[color + 1];
            result[(i * 4) + 2] = Palette[color + 2];
            result[(i * 4) + 3] = pixels[i];
        }

        return result;
    }

    private static int ReadInt(byte[] data, int position)
    {
        if (position < 0 || position > data.Length - 4)
        {
            throw new InvalidDataException("Sprite is truncated.");
        }

        return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(position));
    }

    private static SprFrame ReadFrame(byte[] data, ref int position)
    {
        var originX = ReadInt(data, position);
        var originY = ReadInt(data, position + 4);
        var width = ReadInt(data, position + 8);
        var height = ReadInt(data, position + 12);
        position += 16;

        var size = (long)width * height;

        if (width <= 0 || height <= 0 || position + size > data.Length)
        {
            throw new InvalidDataException("Sprite frame is out of range.");
        }

        var pixels = data.AsSpan(position, (int)size).ToArray();
        position += (int)size;

        return new SprFrame(originX, originY, width, height, pixels);
    }
}

/// <summary>A frame of a sprite.</summary>
internal sealed record SprFrame(int OriginX, int OriginY, int Width, int Height, byte[] Pixels);

/// <summary>How a sprite is oriented to the view.</summary>
internal enum SprType
{
    ParallelUpright,
    FacingUpright,
    Parallel,
    Oriented,
    ParallelOriented,
}

/// <summary>How the sprite pixels are blended.</summary>
internal enum SprTextureFormat
{
    Normal,
    Additive,
    IndexAlpha,
    AlphaTest,
}
