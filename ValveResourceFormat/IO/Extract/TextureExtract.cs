using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using ValveKeyValue;
using ValveResourceFormat.IO.ContentFormats.ValveTexture;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.TextureDecoders;
using ChannelMapping = ValveResourceFormat.CompiledShader.ChannelMapping;

namespace ValveResourceFormat.IO;

/// <summary>
/// Represents a content file for textures with an associated bitmap.
/// </summary>
public class TextureContentFile : ContentFile
{
    /// <summary>
    /// Gets the bitmap data.
    /// </summary>
    public required SKBitmap Bitmap { get; init; }

    /// <summary>
    /// Adds an image sub-file with a custom extraction function.
    /// </summary>
    public void AddImageSubFile(string fileName, Func<SKBitmap, byte[]> imageExtractFunction)
    {
        var image = new ImageSubFile()
        {
            FileName = fileName,
            Bitmap = Bitmap,
            ImageExtract = imageExtractFunction
        };

        SubFiles.Add(image);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (!Disposed && disposing)
        {
            Bitmap.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Represents an image sub-file with a bitmap and extraction function.
/// </summary>
public sealed class ImageSubFile : SubFile
{
    /// <summary>
    /// Gets the bitmap data.
    /// </summary>
    public required SKBitmap Bitmap { get; init; }

    /// <summary>
    /// Gets the image extraction function.
    /// </summary>
    public required Func<SKBitmap, byte[]> ImageExtract { get; init; }

    /// <inheritdoc/>
    public override Func<byte[]> Extract => () => ImageExtract(Bitmap);
}

/// <summary>
/// Handles extraction of texture resources to various formats.
/// </summary>
public sealed class TextureExtract
{
    private static readonly string[] CubemapNames =
    [
        "rt",
        "lf",
        "bk",
        "ft",
        "up",
        "dn",
    ];

    private readonly Texture texture;
    private readonly string fileName;
    private readonly bool isSpriteSheet;
    private readonly bool isCubeMap;
    private readonly bool isArray;
    private readonly bool isSliced;

    // Options
    /// <summary>
    /// Gets or sets the decode flags for texture extraction.
    /// </summary>
    public TextureCodec DecodeFlags { get; set; } = TextureCodec.Auto;

    /// <summary>
    /// Whether to combine cubemap faces into a single latlong image. Only applies to cubemaps exported as EXR without a
    /// vtex file, which reads the faces from a lossless strip instead.
    /// </summary>
    public bool LatLongCombineCubemap { get; set; } = true;

    /// <summary>
    /// Should the vtex file be ignored.
    /// </summary>
    public bool IgnoreVtexFile { get; set; }

    private bool writesMaterialMaps;

    /// <summary>
    /// Gets the flags the images are decoded with. Next to a vtex config, normal maps keep their stored channels
    /// instead of the swizzle and Z the decode reconstructs.
    /// </summary>
    private TextureCodec ImageDecodeFlags => IgnoreVtexFile || writesMaterialMaps
        ? DecodeFlags
        : texture.ResolveDecodeFlags(DecodeFlags, TextureCodec.Dxt5nm | TextureCodec.NormalizeNormals);

    /// <summary>
    /// Gets whether the texture should be exported as EXR format.
    /// </summary>
    public bool ExportExr => texture.IsHighDynamicRange && !DecodeFlags.HasFlag(TextureCodec.ForceLDR);

    /// <summary>
    /// Gets the output file extension for the texture.
    /// </summary>
    public string ImageOutputExtension => ExportExr ? ".exr" : ".png";

    /// <summary>
    /// Initializes a new instance of the <see cref="TextureExtract"/> class.
    /// </summary>
    public TextureExtract(Resource resource)
    {
        texture = (Texture)resource.DataBlock!;
        fileName = resource.FileName!;
        isSpriteSheet = texture.ExtraData?.ContainsKey(VTexExtraData.SHEET) ?? false;
        isCubeMap = texture.Flags.HasFlag(VTexFlags.CUBE_TEXTURE);
        isArray = texture.Depth > 1;
        isSliced = IsSliced(texture);
    }

    /// <summary>
    /// The vtex content file. Input image(s) come as subfiles.
    /// </summary>
    public ContentFile ToContentFile()
    {
        var writeVtex = !IgnoreVtexFile && !writesMaterialMaps;
        var rawImage = texture.ReadRawImageData();
        if (rawImage != null)
        {
            return new ContentFile() { Data = rawImage };
        }

        Func<SKBitmap, byte[]> ImageEncode = ExportExr ? ToExrImage : ToPngImage;

        if (isSliced || isCubeMap)
        {
            var contentFile = new ContentFile()
            {
                FileName = fileName,
                Data = writeVtex ? Encoding.UTF8.GetBytes(ToValveTexture()) : null,
            };

            for (uint depth = 0; depth < texture.Depth; depth++)
            {
                var currentDepth = depth;

                if (!isCubeMap)
                {
                    contentFile.AddSubFile(GetSliceImageFileName(depth), () =>
                    {
                        using var bitmap = texture.GenerateBitmap(depth: currentDepth, decodeFlags: ImageDecodeFlags);
                        return ImageEncode(bitmap);
                    });

                    continue;
                }

                var cubeName = GetCubeImageName(depth);

                if (CombinesLatLong && !writeVtex)
                {
                    contentFile.AddSubFile(cubeName + ImageOutputExtension, () =>
                    {
                        using var latLongBitmap = CombineCubemapFaces(currentDepth, CreateLatLongFromCubemapFaces);
                        return ImageEncode(latLongBitmap);
                    });

                    continue;
                }

                for (var face = 0; face < 6; face++)
                {
                    var currentFace = face;

                    contentFile.AddSubFile($"{cubeName}_{CubemapNames[face]}{ImageOutputExtension}", () =>
                    {
                        using var bitmap = texture.GenerateBitmap(depth: currentDepth, face: (Texture.CubemapFace)currentFace, decodeFlags: ImageDecodeFlags);
                        return ImageEncode(bitmap);
                    });
                }

                if (writeVtex)
                {
                    contentFile.AddSubFile(cubeName + ImageOutputExtension, () =>
                    {
                        using var strip = CombineCubemapFaces(currentDepth, CreateCubemapStrip);
                        return ImageEncode(strip);
                    });
                }
            }

            return contentFile;
        }

        var bitmap = texture.GenerateBitmap(decodeFlags: ImageDecodeFlags);

        var vtex = new TextureContentFile()
        {
            Data = writeVtex ? Encoding.UTF8.GetBytes(ToValveTexture()) : null,
            Bitmap = bitmap,
            FileName = fileName!,
        };

        if (TryGetMksData(out var sprites, out var mks))
        {
            vtex.AddSubFile(Path.GetFileName(GetMksFileName())!, () => Encoding.UTF8.GetBytes(mks));

            var packed = sprites.Keys.Select(static sprite => sprite.Pixels).ToArray();

            foreach (var (sprite, spriteFileName) in sprites)
            {
                vtex.AddImageSubFile(Path.GetFileName(spriteFileName)!, (bitmap) => SpriteToPngImage(bitmap, sprite, packed));
            }

            return vtex;
        }

        vtex.AddImageSubFile(Path.GetFileName(GetImageFileName())!, ImageEncode);

        if (writeVtex && GetRoughnessImageFileName() is { } roughnessFileName)
        {
            vtex.AddImageSubFile(Path.GetFileName(roughnessFileName), bitmap => ToPngImageChannels(bitmap, ChannelMapping.A));
        }

        return vtex;
    }

    /// <summary>
    /// Gets the settings file the compiler reads next to a material's image, with the compile settings the texture
    /// records, or <see langword="null"/> when it records none.
    /// </summary>
    internal string? GetImageSettings()
    {
        var settings = KVObject.ListCollection();

        if (texture.Flags.HasFlag(VTexFlags.NO_LOD))
        {
            settings.Add("nolod", "1");
        }

        foreach (var (key, flag) in (ReadOnlySpan<(string, VTexFlags)>)[("clampu", VTexFlags.SUGGEST_CLAMPS), ("clampv", VTexFlags.SUGGEST_CLAMPT), ("clampw", VTexFlags.SUGGEST_CLAMPU)])
        {
            if (texture.Flags.HasFlag(flag))
            {
                settings.Add(key, "1");
            }
        }

        if (texture.NumMipLevels == 1 && Math.Max(texture.Width, texture.Height) > 4)
        {
            settings.Add("nomip", "1");
        }

        if (texture.Format is VTexFormat.RGBA8888 or VTexFormat.BGRA8888)
        {
            settings.Add("nocompress", "1");
        }

        const string ArtistMip = "Texture Compiler Version Mip Artist ";

        foreach (var dependency in texture.Resource.EditInfo?.SpecialDependencies ?? [])
        {
            if (dependency.String.StartsWith(ArtistMip, StringComparison.Ordinal))
            {
                settings.Add("mip_algorithm", dependency.String[ArtistMip.Length..]);
            }
        }

        if (settings.Count == 0)
        {
            return null;
        }

        using var stream = new MemoryStream();
        KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(stream, new KVDocument(new(), "settings", settings));
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>
    /// Extracts texture to material map files by unpacking channels.
    /// </summary>
    public ContentFile ToMaterialMaps(IEnumerable<MaterialExtract.UnpackInfo> mapsToUnpack)
    {
        writesMaterialMaps = true;

        if (isSpriteSheet)
        {
            return ToContentFile();
        }

        if (CombinesLatLong || (isCubeMap && isArray))
        {
            var vtexContent = ToContentFile();

            if (CombinesLatLong)
            {
                // use the file name set in material properties
                var firstUnpackInfo = mapsToUnpack.FirstOrDefault();

                if (firstUnpackInfo.FileName != null)
                {
                    vtexContent.SubFiles[0].FileName = Path.GetFileName(firstUnpackInfo.FileName);
                }
            }

            return vtexContent;
        }

        if (isCubeMap || isSliced)
        {
            return ToLayeredMaterialMaps(mapsToUnpack);
        }

        var bitmap = texture.GenerateBitmap(decodeFlags: DecodeFlags);
        bitmap.SetImmutable();

        var vtex = new TextureContentFile()
        {
            Bitmap = bitmap,
            FileName = fileName!,
        };

        if (ExportExr)
        {
            // Channels are not unpacked from HDR images, the whole image is written under the first name the material uses
            var firstUnpackInfo = mapsToUnpack.FirstOrDefault();

            if (firstUnpackInfo.FileName != null)
            {
                vtex.AddImageSubFile(Path.GetFileName(firstUnpackInfo.FileName), ToExrImage);
            }

            return vtex;
        }

        foreach (var unpackInfo in mapsToUnpack)
        {
            vtex.AddImageSubFile(Path.GetFileName(unpackInfo.FileName)!, (bitmap) => ToPngImageChannels(bitmap, unpackInfo.Channel));
        }

        return vtex;
    }

    /// <summary>
    /// Writes a cubemap as one 6:1 strip and an array or volume texture as numbered slices, under the names the material uses.
    /// </summary>
    private SliceContentFile ToLayeredMaterialMaps(IEnumerable<MaterialExtract.UnpackInfo> mapsToUnpack)
    {
        var contentFile = new SliceContentFile
        {
            FileName = fileName,
        };

        var unpackInfos = (ExportExr ? mapsToUnpack.Take(1) : mapsToUnpack).ToList();
        var sliceCount = isCubeMap ? 1 : texture.Depth;

        for (uint slice = 0; slice < sliceCount; slice++)
        {
            var currentSlice = slice;

            foreach (var unpackInfo in unpackInfos)
            {
                var channel = unpackInfo.Channel;
                var sliceFileName = isCubeMap ? unpackInfo.FileName : GetSliceFileName(unpackInfo.FileName, slice);

                contentFile.AddSubFile(Path.GetFileName(sliceFileName), () => contentFile.EncodeSlice(
                    currentSlice,
                    () => isCubeMap ? CombineCubemapFaces(0, CreateCubemapStrip) : texture.GenerateBitmap(depth: currentSlice, decodeFlags: DecodeFlags),
                    bitmap => ExportExr ? ToExrImage(bitmap) : ToPngImageChannels(bitmap, channel)));
            }
        }

        return contentFile;
    }

    /// <summary>
    /// Keeps the last decoded slice so that the channel images unpacked from one slice share a single decode.
    /// </summary>
    private sealed class SliceContentFile : ContentFile
    {
        private readonly Lock decodeLock = new();
        private uint decodedSlice;
        private SKBitmap? decodedBitmap;

        public byte[] EncodeSlice(uint slice, Func<SKBitmap> decode, Func<SKBitmap, byte[]> encode)
        {
            lock (decodeLock)
            {
                if (decodedBitmap == null || decodedSlice != slice)
                {
                    decodedBitmap?.Dispose();
                    decodedBitmap = decode();
                    decodedSlice = slice;
                }

                return encode(decodedBitmap);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!Disposed && disposing)
            {
                decodedBitmap?.Dispose();
                decodedBitmap = null;
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Whether the texture is an array or volume texture that the compiler builds from a sequence of slice images.
    /// </summary>
    internal static bool IsSliced(Texture texture)
        => !texture.Flags.HasFlag(VTexFlags.CUBE_TEXTURE)
            && (texture.Depth > 1 || texture.Flags.HasFlag(VTexFlags.VOLUME_TEXTURE) || texture.Flags.HasFlag(VTexFlags.TEXTURE_ARRAY));

    /// <summary>
    /// Gets the name of the first image of a slice sequence. The compiler loads further slices by counting up the run of
    /// zeros that follows the first "_z0" in the name when a '.' or '_' ends it, so a name without such a run gets "_z000".
    /// </summary>
    internal static string GetFirstSliceFileName(string fileName, uint sliceCount)
    {
        if (TryGetSliceNumberRange(fileName, out _, out var length)
            && (sliceCount - 1).ToString(CultureInfo.InvariantCulture).Length <= length)
        {
            return fileName;
        }

        return Path.ChangeExtension(fileName, null) + "_z000" + Path.GetExtension(fileName);
    }

    private static string GetSliceFileName(string firstSliceFileName, uint slice)
    {
        if (!TryGetSliceNumberRange(firstSliceFileName, out var start, out var length))
        {
            return firstSliceFileName;
        }

        var number = slice.ToString(new string('0', length), CultureInfo.InvariantCulture);
        return string.Concat(firstSliceFileName.AsSpan(0, start), number, firstSliceFileName.AsSpan(start + length));
    }

    private static bool TryGetSliceNumberRange(string fileName, out int start, out int length)
    {
        var nameStart = fileName.Length - Path.GetFileName(fileName.AsSpan()).Length;
        var marker = fileName.IndexOf("_z0", nameStart, StringComparison.Ordinal);

        start = marker + 2;
        length = 0;

        if (marker < 0)
        {
            return false;
        }

        while (start + length < fileName.Length && fileName[start + length] == '0')
        {
            length++;
        }

        return start + length < fileName.Length && fileName[start + length] is '.' or '_';
    }

    /// <summary>
    /// Gets the appropriate image output extension for a texture.
    /// </summary>
    public static string GetImageOutputExtension(Texture texture)
    {
        if (texture.IsHighDynamicRange) // todo: also check DecodeFlags for ForceLDR
        {
            return "exr";
        }

        if (texture.IsRawJpeg)
        {
            return "jpeg";
        }

        if (texture.IsRawWebp)
        {
            return "webp";
        }

        return "png";
    }

    private bool CombinesLatLong => isCubeMap && LatLongCombineCubemap && ExportExr;

    private string ImageBaseName => Path.GetFileNameWithoutExtension(fileName);

    /// <summary>
    /// Gets the image of a slice. A single slice is a plain image.
    /// </summary>
    private string GetSliceImageFileName(uint depth)
        => texture.Depth == 1
            ? Path.GetFileName(GetImageFileName())
            : GetSliceFileName(GetFirstSliceFileName(Path.GetFileName(GetImageFileName()), texture.Depth), depth);

    private string GetCubeImageName(uint depth)
        => isArray ? string.Create(CultureInfo.InvariantCulture, $"{ImageBaseName}_f{depth:D2}") : ImageBaseName;

    private string GetImageFileName()
        => Path.ChangeExtension(fileName, ImageOutputExtension);

    private string GetContentPath(string name)
        => Path.Join(Path.GetDirectoryName(fileName), name).Replace(Path.DirectorySeparatorChar, '/');

    private SKBitmap CombineCubemapFaces(uint depth, Func<SKPixmap[], SKBitmap> combine)
    {
        var bitmaps = new SKBitmap[6];
        var faces = new SKPixmap[6];
        try
        {
            for (var face = 0; face < 6; face++)
            {
                bitmaps[face] = texture.GenerateBitmap(depth: depth, face: (Texture.CubemapFace)face, decodeFlags: ImageDecodeFlags);
                faces[face] = bitmaps[face].PeekPixels();
            }

            return combine(faces);
        }
        finally
        {
            for (var face = 0; face < 6; face++)
            {
                faces[face]?.Dispose();
                bitmaps[face]?.Dispose();
            }
        }
    }

    /// <summary>
    /// Lays the six faces of a cubemap side by side in compiled face order, the strip layout the compiler reads as a cubemap.
    /// </summary>
    private static SKBitmap CreateCubemapStrip(SKPixmap[] faces)
    {
        var faceInfo = faces[0].Info;
        var strip = new SKBitmap(faceInfo.WithSize(faceInfo.Width * 6, faceInfo.Height));

        for (var face = 0; face < 6; face++)
        {
            faces[face].ReadPixels(faceInfo, strip.GetPixels() + face * faceInfo.RowBytes, strip.RowBytes);
        }

        return strip;
    }

    private string GetMksFileName()
        => Path.ChangeExtension(fileName, "mks");

    /// <summary>
    /// Converts a bitmap to PNG image bytes.
    /// </summary>
    public static byte[] ToPngImage(SKBitmap bitmap)
    {
        return EncodePng(bitmap);
    }

    /// <summary>
    /// Converts a sheet sprite to PNG bytes, restored to its place in the source image with the
    /// packed content around it cleared.
    /// </summary>
    public static byte[] SpriteToPngImage(SKBitmap bitmap, SheetSprite sprite, SKRectI[] packed)
    {
        if (sprite.Pixels == sprite.Image)
        {
            return SubsetToPngImage(bitmap, sprite.Pixels);
        }

        using var image = new SKBitmap(sprite.Image.Width, sprite.Image.Height, bitmap.ColorType, bitmap.AlphaType);
        using var canvas = new SKCanvas(image);
        canvas.Clear(SKColors.Transparent);

        var copy = sprite.Cropped ? sprite.Pixels : SKRectI.Intersect(sprite.Image, new SKRectI(0, 0, bitmap.Width, bitmap.Height));

        if (!copy.IsEmpty)
        {
            using var pixels = new SKBitmap();

            if (bitmap.ExtractSubset(pixels, copy))
            {
                canvas.DrawBitmap(pixels, new SKPoint(copy.Left - sprite.Image.Left, copy.Top - sprite.Image.Top), SKSamplingOptions.Default);
            }
        }

        if (!sprite.Cropped)
        {
            using var clear = new SKPaint { Color = SKColors.Transparent, BlendMode = SKBlendMode.Src };

            foreach (var other in packed)
            {
                if (other == sprite.Pixels || !other.IntersectsWith(copy))
                {
                    continue;
                }

                var area = SKRectI.Intersect(other, copy);
                canvas.DrawRect(area.Left - sprite.Image.Left, area.Top - sprite.Image.Top, area.Width, area.Height, clear);
            }
        }

        return EncodePng(image);
    }

    /// <summary>
    /// Converts a subset of a bitmap to PNG image bytes.
    /// </summary>
    public static byte[] SubsetToPngImage(SKBitmap bitmap, SKRectI spriteRect)
    {
        using var subset = new SKBitmap();
        bitmap.ExtractSubset(subset, spriteRect);

        return EncodePng(subset);
    }

    /// <summary>
    /// Converts specific channels of a bitmap to PNG image bytes.
    /// </summary>
    public static byte[] ToPngImageChannels(SKBitmap bitmap, ChannelMapping channel)
    {
        if (channel.Count == 1)
        {
            using var newBitmap = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Gray8, SKAlphaType.Opaque);
            using var newPixelmap = newBitmap.PeekPixels();
            using var pixelmap = bitmap.PeekPixels();

            var newPixels = newPixelmap.GetPixelSpan<byte>();
            var pixels = pixelmap.GetPixelSpan<SKColor>();
            var sourceChannel = channel.Channels[0];

            for (var i = 0; i < pixels.Length; i++)
            {
                newPixels[i] = sourceChannel switch
                {
                    ChannelMapping.Channel.R => pixels[i].Red,
                    ChannelMapping.Channel.G => pixels[i].Green,
                    ChannelMapping.Channel.B => pixels[i].Blue,
                    ChannelMapping.Channel.A => pixels[i].Alpha,
                    _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
                };
            }

            return EncodePng(newPixelmap);
        }
        else if (channel == ChannelMapping.RG || channel == ChannelMapping.RGB)
        {
            // Wipe out the alpha channel
            using var newBitmap = new SKBitmap(bitmap.Info);
            using var newPixelmap = newBitmap.PeekPixels();
            using var pixelmap = bitmap.PeekPixels();
            pixelmap.GetPixelSpan<SKColor>().CopyTo(newPixelmap.GetPixelSpan<SKColor>());

            using var alphaPixelmap = newPixelmap.WithAlphaType(SKAlphaType.Opaque);

            return EncodePng(alphaPixelmap);
        }
        else if (channel == ChannelMapping.RGBA)
        {
            return EncodePng(bitmap);
        }
        else
        {
            // Swizzled channels, e.g. alpha-green DXT5nm
            var newBitmapType = bitmap.Info
                .WithAlphaType(channel.Count < 4 ? SKAlphaType.Opaque : SKAlphaType.Unpremul)
                .WithColorType(SKColorType.Rgba8888);
            using var newBitmap = new SKBitmap(newBitmapType);
            using var newPixelmap = newBitmap.PeekPixels();
            using var pixelmap = bitmap.PeekPixels();

            var newPixels = newPixelmap.GetPixelSpan<SKColor>();
            var pixels = pixelmap.GetPixelSpan<SKColor>();

            for (var i = 0; i < pixels.Length; i++)
            {
                var color = (uint)newPixels[i];
                for (var j = 0; j < channel.Count; j++)
                {
                    var c = channel.Channels[j] switch
                    {
                        ChannelMapping.Channel.R => pixels[i].Red,
                        ChannelMapping.Channel.G => pixels[i].Green,
                        ChannelMapping.Channel.B => pixels[i].Blue,
                        ChannelMapping.Channel.A => pixels[i].Alpha,
                        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
                    };

                    color |= ((uint)c) << (j * 8);
                }

                newPixels[i] = new SKColor(color);
            }

            return EncodePng(newPixelmap);
        }
    }

    /// <summary>
    /// Copies a single channel from source pixmap to destination pixmap.
    /// </summary>
    public static void CopyChannel(SKPixmap srcPixels, ChannelMapping srcChannel, SKPixmap dstPixels, ChannelMapping dstChannel)
    {
        if (srcChannel.Count != 1 || dstChannel.Count != 1)
        {
            throw new InvalidOperationException($"Can only copy individual channels. {srcChannel} -> {dstChannel}");
        }

        var srcPixelSpan = srcPixels.GetPixelSpan<SKColor>();
        var pixelSpan = dstPixels.GetPixelSpan<SKColor>();
        var source = srcChannel.Channels[0];
        var destination = dstChannel.Channels[0];

#pragma warning disable CS8509 // non exhaustive switch
        for (var i = 0; i < srcPixelSpan.Length; i++)
        {
            pixelSpan[i] = destination switch
            {
                ChannelMapping.Channel.R => source switch
                {
                    ChannelMapping.Channel.R => pixelSpan[i].WithRed(srcPixelSpan[i].Red),
                    ChannelMapping.Channel.G => pixelSpan[i].WithRed(srcPixelSpan[i].Green),
                    ChannelMapping.Channel.B => pixelSpan[i].WithRed(srcPixelSpan[i].Blue),
                    ChannelMapping.Channel.A => pixelSpan[i].WithRed(srcPixelSpan[i].Alpha),
                },
                ChannelMapping.Channel.G => source switch
                {
                    ChannelMapping.Channel.R => pixelSpan[i].WithGreen(srcPixelSpan[i].Red),
                    ChannelMapping.Channel.G => pixelSpan[i].WithGreen(srcPixelSpan[i].Green),
                    ChannelMapping.Channel.B => pixelSpan[i].WithGreen(srcPixelSpan[i].Blue),
                    ChannelMapping.Channel.A => pixelSpan[i].WithGreen(srcPixelSpan[i].Alpha),
                },
                ChannelMapping.Channel.B => source switch
                {
                    ChannelMapping.Channel.R => pixelSpan[i].WithBlue(srcPixelSpan[i].Red),
                    ChannelMapping.Channel.G => pixelSpan[i].WithBlue(srcPixelSpan[i].Green),
                    ChannelMapping.Channel.B => pixelSpan[i].WithBlue(srcPixelSpan[i].Blue),
                    ChannelMapping.Channel.A => pixelSpan[i].WithBlue(srcPixelSpan[i].Alpha),
                },
                ChannelMapping.Channel.A => source switch
                {
                    ChannelMapping.Channel.R => pixelSpan[i].WithAlpha(srcPixelSpan[i].Red),
                    ChannelMapping.Channel.G => pixelSpan[i].WithAlpha(srcPixelSpan[i].Green),
                    ChannelMapping.Channel.B => pixelSpan[i].WithAlpha(srcPixelSpan[i].Blue),
                    ChannelMapping.Channel.A => pixelSpan[i].WithAlpha(srcPixelSpan[i].Alpha),
                },
            };
        }
#pragma warning restore CS8509 // non exhaustive switch
    }

    /// <summary>
    /// Packs masks to a new texture.
    /// </summary>
    public class TexturePacker : IDisposable
    {
        private static readonly SKSamplingOptions SamplingOptions = new(SKFilterMode.Linear, SKMipmapMode.None);

        /// <summary>
        /// Gets the default color for unpacked channels.
        /// </summary>
        public SKColor DefaultColor { get; init; } = SKColors.Black;

        /// <summary>
        /// Gets the packed bitmap.
        /// </summary>
        public SKBitmap? Bitmap { get; private set; }
        private readonly HashSet<ChannelMapping> Packed = [];

        /// <summary>
        /// Collects a channel from source pixmap into the packed texture.
        /// </summary>
        public void Collect(SKPixmap srcPixels, ChannelMapping srcChannel, ChannelMapping dstChannel, string fileName)
        {
            if (!Packed.Add(dstChannel))
            {
                Console.WriteLine($"{dstChannel} has already been packed in texture: {fileName}");
            }

            if (Bitmap is null)
            {
                Bitmap = new SKBitmap(srcPixels.Width, srcPixels.Height, true);
                if (DefaultColor != SKColors.Black)
                {
                    using var pixels = Bitmap.PeekPixels();
                    pixels.GetPixelSpan<SKColor>().Fill(DefaultColor);
                }
            }

            if (Bitmap.Width < srcPixels.Width || Bitmap.Height < srcPixels.Height)
            {
                var newBitmap = new SKBitmap(srcPixels.Width, srcPixels.Height, true);
                using (Bitmap)
                {
                    // Scale Bitmap up to srcPixels size
                    using var oldPixels = Bitmap.PeekPixels();
                    using var newPixels = newBitmap.PeekPixels();
                    if (!oldPixels.ScalePixels(newPixels, SamplingOptions))
                    {
                        throw new InvalidOperationException($"Failed to scale up pixels of {fileName}");
                    }
                }
                Bitmap = newBitmap;
            }
            else if (Bitmap.Width > srcPixels.Width || Bitmap.Height > srcPixels.Height)
            {
                // Scale srcPixels up to Bitmap size
                using var newSrcBitmap = new SKBitmap(Bitmap.Width, Bitmap.Height, true);
                using var newSrcPixels = newSrcBitmap.PeekPixels();
                if (!srcPixels.ScalePixels(newSrcPixels, SamplingOptions))
                {
                    throw new InvalidOperationException($"Failed to scale up incoming pixels for {fileName}");
                }
                using var dstPixels2 = Bitmap.PeekPixels();
                CopyChannel(newSrcPixels, srcChannel, dstPixels2, dstChannel);
                return;
            }

            using var dstPixels = Bitmap.PeekPixels();
            CopyChannel(srcPixels, srcChannel, dstPixels, dstChannel);
        }

        /// <summary>
        /// Releases the resources used by the texture packer.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (Bitmap != null && disposing)
            {
                Bitmap.Dispose();
                Bitmap = null;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    private static byte[] EncodePng(SKBitmap bitmap)
    {
        using var pixels = bitmap.PeekPixels();
        return EncodePng(pixels);
    }

    private static byte[] EncodePng(SKPixmap pixels)
    {
        var options = new SKPngEncoderOptions(SKPngEncoderFilterFlags.Paeth, zLibLevel: 4);

        using var png = pixels.Encode(options);

        if (png is null)
        {
            return [];
        }

        return png.ToArray();
    }

    /// <summary>
    /// Converts a bitmap to EXR image bytes.
    /// </summary>
    public static byte[] ToExrImage(SKBitmap bitmap)
    {
        using var pixels = bitmap.PeekPixels();
        return ToExrImage(pixels);
    }

    /// <summary>
    /// Converts pixmap data to EXR image bytes.
    /// </summary>
    public static byte[] ToExrImage(SKPixmap pixels)
    {
        return ExrWriter.Write(pixels);
    }

    /// <summary>
    /// A sprite the sheet packs: the rectangle holding its pixels, the bounds of the image it was cut from, and
    /// whether the sheet kept nothing of that image outside those pixels.
    /// </summary>
    public readonly record struct SheetSprite(SKRectI Pixels, SKRectI Image, bool Cropped);

    /// <summary>
    /// Attempts to extract sprite sheet data and MKS texture script.
    /// </summary>
    public bool TryGetMksData(out Dictionary<SheetSprite, string> sprites, out string mks)
    {
        mks = string.Empty;
        sprites = [];

        if (!isSpriteSheet)
        {
            return false;
        }

        var spriteSheetData = texture.GetSpriteSheetData();

        if (spriteSheetData == null)
        {
            return false;
        }

        var width = texture.ActualWidth;
        var height = texture.ActualHeight;

        var mksBuilder = new StringBuilder();
        var textureName = Path.GetFileNameWithoutExtension(fileName);
        var packmodeNonFlat = false;
        var compilerNamedSequences = 0;
        var framesWithExtraImages = 0;

        for (var s = 0; s < spriteSheetData.Sequences.Length; s++)
        {
            var sequence = spriteSheetData.Sequences[s];
            var cropped = IsCropped(sequence);
            var alphaCrop = cropped ? GetAlphaCropMode(sequence) : string.Empty;
            mksBuilder.AppendLine();

            switch (sequence.NoColor, sequence.NoAlpha)
            {
                case (false, false):
                    mksBuilder.AppendLine(CultureInfo.InvariantCulture, $"sequence {sequence.Id}{alphaCrop}");
                    break;

                case (false, true):
                    mksBuilder.AppendLine(CultureInfo.InvariantCulture, $"sequence-rgb {sequence.Id}{alphaCrop}");
                    packmodeNonFlat = true;
                    break;

                case (true, false):
                    mksBuilder.AppendLine(CultureInfo.InvariantCulture, $"sequence-a {sequence.Id}{alphaCrop}");
                    packmodeNonFlat = true;
                    break;

                case (true, true):
                    throw new InvalidDataException($"Unexpected combination of {nameof(sequence.NoColor)} and {nameof(sequence.NoAlpha)}");
            }

            if (sequence.IsNamed)
            {
                mksBuilder.AppendLine(CultureInfo.InvariantCulture, $"name {sequence.Name}");
            }
            else if (sequence.Name.Length > 0)
            {
                compilerNamedSequences++;
            }

            if (!sequence.Clamp)
            {
                mksBuilder.AppendLine("LOOP");
            }
            else if (HoldsLastFrame(sequence))
            {
                mksBuilder.AppendLine("clamp-extendlastframe");
            }

            foreach (var (floatName, floatValue) in sequence.FloatParams)
            {
                mksBuilder.AppendLine(CultureInfo.InvariantCulture, $"{floatName} {floatValue}");
            }

            for (var f = 0; f < sequence.Frames.Length; f++)
            {
                var frame = sequence.Frames[f];

                if (frame.Images.Length == 0)
                {
                    continue;
                }

                // A frame holds up to four image slots, and the unused ones repeat the first rectangle.
                var image = frame.Images[0];

                if (frame.Images.Length > 1)
                {
                    framesWithExtraImages++;
                }

                var sprite = new SheetSprite(
                    image.GetCroppedRect(width, height),
                    ToPixelRect(image.UncroppedMin, image.UncroppedMax, width, height),
                    cropped);

                var imageFileName = sequence.Frames.Length == 1
                    ? $"{textureName}_seq{s}.png"
                    : $"{textureName}_seq{s}_{f}.png";

                sprites.TryAdd(sprite, imageFileName);
                mksBuilder.AppendLine(CultureInfo.InvariantCulture, $"frame {sprites[sprite]} {frame.DisplayTime}");
            }
        }

        if (packmodeNonFlat)
        {
            mksBuilder.Insert(0, "packmode rgb+a\n");
        }

        if (framesWithExtraImages > 0)
        {
            mksBuilder.Insert(0, $"// {framesWithExtraImages} frames hold more than one image, which this sheet format writes and the compiler no longer reads\n");
        }

        if (compilerNamedSequences > 0)
        {
            mksBuilder.Insert(0, $"// {compilerNamedSequences} sequences were left unnamed and carry what the compiler wrote instead\n");
        }

        mksBuilder.Insert(0, $"// Reconstructed with {StringToken.VRF_GENERATOR}\n\n");
        mks = mksBuilder.ToString();
        return true;
    }

    /// <summary>
    /// Whether this sequence spans the display time of its last frame. A clamping sequence that does not
    /// runs out at the frame before it, and the sheet compiler drops that last display time.
    /// </summary>
    private static bool HoldsLastFrame(Texture.SpritesheetData.Sequence sequence)
        => sequence.Frames.Length > 1 && sequence.Frames[^1].DisplayTime > 0f;

    /// <summary>
    /// Whether the sheet cropped this sequence's images down to the part the alpha channel covers and kept
    /// nothing of what surrounded it. Such an image reaches outside the texture, which holds the crop alone.
    /// </summary>
    private static bool IsCropped(Texture.SpritesheetData.Sequence sequence)
    {
        if (sequence.AlphaCrop)
        {
            return true;
        }

        foreach (var frame in sequence.Frames)
        {
            foreach (var image in frame.Images)
            {
                if (image.UncroppedMin.X < 0f || image.UncroppedMin.Y < 0f || image.UncroppedMax.X > 1f || image.UncroppedMax.Y > 1f)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The mks keyword that crops this sequence's images down to the alpha covered part of them, for the axes
    /// the sheet cropped.
    /// </summary>
    private static string GetAlphaCropMode(Texture.SpritesheetData.Sequence sequence)
    {
        var cropsU = false;
        var cropsV = false;

        foreach (var frame in sequence.Frames)
        {
            foreach (var image in frame.Images)
            {
                cropsU |= image.CroppedMin.X > image.UncroppedMin.X || image.CroppedMax.X < image.UncroppedMax.X;
                cropsV |= image.CroppedMin.Y > image.UncroppedMin.Y || image.CroppedMax.Y < image.UncroppedMax.Y;
            }
        }

        return (cropsU, cropsV) switch
        {
            (true, true) => " alphacrop",
            (true, false) => " alphacrop_u",
            (false, true) => " alphacrop_v",
            _ => string.Empty,
        };
    }

    private static SKRectI ToPixelRect(Vector2 min, Vector2 max, int width, int height)
    {
        var startX = (int)MathF.Floor(min.X * width);
        var startY = (int)MathF.Floor(min.Y * height);
        var endX = (int)MathF.Floor(max.X * width) + 1;
        var endY = (int)MathF.Floor(max.Y * height) + 1;

        return new SKRectI(startX, startY, endX, endY);
    }

    private string GetInputFileNameForVtex()
    {
        if (isSpriteSheet)
        {
            return GetMksFileName().Replace(Path.DirectorySeparatorChar, '/');
        }

        return GetImageFileName().Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>
    /// Converts the texture to a Valve texture (vtex) configuration string.
    /// </summary>
    /// <returns>A vtex configuration string in KeyValues2 format.</returns>
    public string ToValveTexture()
    {
        var vtex = CreateVtex();
        using var datamodel = new Datamodel.Datamodel("vtex", 1);
        vtex.Clamp = new Vector3(
            texture.Flags.HasFlag(VTexFlags.SUGGEST_CLAMPS) ? 1 : 0,
            texture.Flags.HasFlag(VTexFlags.SUGGEST_CLAMPT) ? 1 : 0,
            texture.Flags.HasFlag(VTexFlags.SUGGEST_CLAMPU) ? 1 : 0);
        vtex.NoLod = texture.Flags.HasFlag(VTexFlags.NO_LOD);
        datamodel.Root = vtex;

        using var stream = new MemoryStream();
        datamodel.Save(stream, "keyvalues2_noids", 1);
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    private CDmeVtex CreateVtex()
    {
        var volumeType = texture.Flags.HasFlag(VTexFlags.VOLUME_TEXTURE) ? "3D" : "2DARRAY";
        var (inputType, outputType, fileNames) = isCubeMap
            ? ("CUBE", texture.Depth == 1 && texture.Flags.HasFlag(VTexFlags.TEXTURE_ARRAY) ? "CUBEARRAY" : "CUBE",
                Enumerable.Range(0, texture.Depth).Select(depth => GetContentPath(GetCubeImageName((uint)depth) + ImageOutputExtension)).ToArray())
            : isSliced
                ? (volumeType, volumeType, [GetContentPath(GetSliceImageFileName(0))])
                : ("2D", "2D", [GetInputFileNameForVtex()]);

        var (imageProcessors, colorMip, alphaMip, normalizesNormals) = GetRecordedProcessing();
        var vtex = new CDmeVtex
        {
            OutputTypeString = outputType,
            OutputFormat = texture.Format.ToString(),
        };

        string colorSpace;
        string channels;

        if (ExportExr)
        {
            colorSpace = "linear";
            channels = texture.Format is VTexFormat.RGBA16161616 or VTexFormat.RGBA16161616F or VTexFormat.RGBA32323232F ? "rgba" : "rgb";
        }
        else
        {
            (colorSpace, channels) = GetColorSpaceAndChannels();
        }

        // The roughness mip algorithms read a normal and a roughness input and store one minus the roughness. A texture
        // that holds roughness alone gets a flat normal.
        var roughnessFileName = colorMip == "AnisoRoughness_RG" ? fileNames[0] : GetRoughnessImageFileName()?.Replace(Path.DirectorySeparatorChar, '/');

        if (roughnessFileName != null)
        {
            fileNames = [colorMip == "AnisoRoughness_RG" ? "[0.5 0.5 1.0 1.0]" : fileNames[0], roughnessFileName];
            imageProcessors = normalizesNormals ? ["NormalizeNormals"] : [];
        }

        var inputNames = new string[fileNames.Length];

        for (var i = 0; i < fileNames.Length; i++)
        {
            inputNames[i] = string.Create(CultureInfo.InvariantCulture, $"InputTexture{i}");
            var input = new CDmeInputTexture { Name = inputNames[i], FileName = fileNames[i], TypeString = inputType, ColorSpace = colorSpace };
            var processors = roughnessFileName != null && i == 1 ? ["Inverse"] : imageProcessors;

            foreach (var processor in processors.DefaultIfEmpty("None"))
            {
                input.ImageProcessorArray.Add(new CDmeImageProcessor { Algorithm = processor });
            }

            vtex.InputTextureArray.Add(input);
        }

        var colorInputNames = inputNames;

        if (colorMip == "AlphaWeighted")
        {
            colorInputNames = new string[fileNames.Length * 2];

            for (var i = 0; i < fileNames.Length; i++)
            {
                var alphaName = string.Create(CultureInfo.InvariantCulture, $"InputTextureAlpha{i}");
                var input = new CDmeInputTexture { Name = alphaName, FileName = fileNames[i], TypeString = inputType, ColorSpace = "linear" };
                input.ImageProcessorArray.Add(new CDmeImageProcessor());
                vtex.InputTextureArray.Add(input);
                colorInputNames[i * 2] = inputNames[i];
                colorInputNames[i * 2 + 1] = alphaName;
            }
        }

        var colorChannels = channels.Replace("a", string.Empty, StringComparison.Ordinal);

        if (channels.Contains('a', StringComparison.Ordinal) && colorChannels.Length > 0 && (colorSpace == "srgb" || colorMip != alphaMip))
        {
            vtex.TextureOutputChannelArray.Add(CreateOutputChannel(colorInputNames, colorChannels, colorSpace, colorMip));
            vtex.TextureOutputChannelArray.Add(CreateOutputChannel(inputNames, "a", "linear", alphaMip));
        }
        else
        {
            vtex.TextureOutputChannelArray.Add(CreateOutputChannel(colorInputNames, channels, colorSpace, colorMip));
        }

        return vtex;

        static CDmeTextureOutputChannel CreateOutputChannel(string[] inputNames, string channels, string colorSpace, string mipAlgorithm)
        {
            var output = new CDmeTextureOutputChannel { SrcChannels = channels, DstChannels = channels, OutputColorSpace = colorSpace };
            output.InputTextureArray.AddRange(inputNames);
            output.MipAlgorithm.Algorithm = mipAlgorithm;
            return output;
        }
    }

    /// <summary>
    /// Gets the image processors to run again on the decoded image, the color and alpha mip algorithms the compiler
    /// recorded in the special dependencies, and whether it normalized a normal input.
    /// </summary>
    private (List<string> ImageProcessors, string ColorMip, string AlphaMip, bool NormalizesNormals) GetRecordedProcessing()
    {
        const string Prefix = "Texture Compiler Version ";
        var imageProcessors = new List<string>();
        var mips = new List<string>();
        var normalizesNormals = false;

        foreach (var dependency in texture.Resource.EditInfo?.SpecialDependencies ?? [])
        {
            if (dependency.CompilerIdentifier != "CompileTexture" || !dependency.String.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var name = dependency.String[Prefix.Length..];

            if (name.StartsWith("Mip ", StringComparison.Ordinal))
            {
                name = name[4..];
                mips.Add(name switch
                {
                    // Box Inverse inverts the first mip, which the decoded pixels already hold inverted
                    "Box Inverse" => "Box",
                    "Normals" => "MipNormal",
                    "AnisoNormals" => "AnisoNormal",
                    _ when name.StartsWith("Artist ", StringComparison.Ordinal) => name["Artist ".Length..],
                    _ => name,
                });
            }
            else
            {
                // The decoded pixels already hold what the other processors wrote, the decode undoes the YCoCg encoding
                // and crops the power of two fill
                switch (name)
                {
                    case "Image FillToPowerOfTwo":
                        imageProcessors.Add("FillToPowerOfTwo");
                        break;
                    case "Image YCoCg Conversion":
                        imageProcessors.Add("ConvertToYCoCg");
                        break;
                    case "Image Dilate Color":
                        imageProcessors.Add("DilateColorInTransparentPixels");
                        break;
                    case "Image NormalizeNormals":
                        normalizesNormals = true;
                        break;
                }
            }
        }

        static bool IsAlphaMip(string mip) => mip is "Box" or "None" or "PreserveCoverage";

        if (mips.Count(mip => !IsAlphaMip(mip)) > 1)
        {
            mips.Remove("HemiOctAnisoRoughness");
        }

        var alphaMip = mips.Find(IsAlphaMip);
        var colorMip = mips.Find(mip => !IsAlphaMip(mip)) ?? alphaMip ?? (texture.NumMipLevels > 1 ? "Box" : "None");

        return (imageProcessors, LimitMips(colorMip), LimitMips(alphaMip ?? colorMip), normalizesNormals);
    }

    /// <summary>
    /// Limits a Box mip chain to the mips the texture has when it ends before the full chain, which stops at a smaller
    /// side of 4 for block compressed formats and at 1 for the others.
    /// </summary>
    private string LimitMips(string mipAlgorithm)
    {
        var size = texture.IsBlockCompressed
            ? Math.Min(texture.Width, texture.Height) / 4
            : Math.Max(Math.Max(texture.Width, texture.Height), texture.Flags.HasFlag(VTexFlags.VOLUME_TEXTURE) ? texture.Depth : 1);
        var fullMipCount = BitOperations.Log2((uint)Math.Max(size, 1)) + 1;

        return mipAlgorithm == "Box" && texture.NumMipLevels < fullMipCount
            ? string.Create(CultureInfo.InvariantCulture, $"Box{texture.NumMipLevels}")
            : mipAlgorithm;
    }

    /// <summary>
    /// Gets the image for the roughness that normal maps store in blue, which the decode moves to alpha, or
    /// <see langword="null"/> when the texture stores none.
    /// </summary>
    private string? GetRoughnessImageFileName()
        => isCubeMap || isSliced || isSpriteSheet || !ImageDecodeFlags.HasFlag(TextureCodec.BlueToAlpha)
            ? null
            : Path.ChangeExtension(fileName, null) + "_roughness.png";

    /// <summary>
    /// Gets whether the color channels were compiled from sRGB input, by which of the raw and the linearized mean of
    /// the decoded first mip is closer to the recorded reflectivity, and the channels that hold data: a color channel
    /// that is not all 0 and an alpha channel that is not all 1. Alpha and color channels that are constant 0 or 1
    /// are linear.
    /// </summary>
    private (string ColorSpace, string Channels) GetColorSpaceAndChannels()
    {
        using var bitmap = texture.GenerateBitmap(face: isCubeMap ? Texture.CubemapFace.PositiveX : default, decodeFlags: ImageDecodeFlags);
        using var pixmap = bitmap.PeekPixels();

        var sum = Vector4.Zero;
        var linearSum = Vector3.Zero;
        var min = new Vector4(255f);
        var max = Vector4.Zero;

        foreach (var pixel in pixmap.GetPixelSpan<SKColor>())
        {
            var value = new Vector4(pixel.Red, pixel.Green, pixel.Blue, pixel.Alpha);
            sum += value;
            linearSum += ColorSpace.SrgbGammaToLinear(value.AsVector3() / 255f);
            min = Vector4.Min(min, value);
            max = Vector4.Max(max, value);
        }

        var count = Math.Max(pixmap.Width * pixmap.Height, 1);
        var mean = sum / (255f * count);
        var linearMean = linearSum / count;
        var reflectivity = texture.Reflectivity;

        var channels = string.Concat("rgba".Where((_, i) => reflectivity[i] != 0f || (i < 3 ? max[i] != 0f : min[i] != 255f)));

        if (channels.Length == 0)
        {
            channels = "rgba";
        }

        var constant = Enumerable.Range(0, 3).All(i => min[i] == max[i] && min[i] is 0f or 255f);
        var srgbError = Vector3.Abs(reflectivity.AsVector3() - linearMean);
        var rawError = Vector3.Abs(reflectivity.AsVector3() - mean.AsVector3());
        var srgb = !constant && srgbError.X + srgbError.Y + srgbError.Z < rawError.X + rawError.Y + rawError.Z;

        return (srgb ? "srgb" : "linear", channels);
    }

    /// <summary>
    /// Creates a latlong layout bitmap from 6 projected cubemap faces.
    /// </summary>
    public static SKBitmap CreateLatLongFromCubemapFaces(SKPixmap[] faces)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(faces.Length, 6, nameof(faces));

        var colorType = faces[0].Info.ColorType;
        if (colorType != SKColorType.RgbaF32)
        {
            throw new InvalidOperationException($"Cubemap faces must be RgbaF32 format, got {colorType}");
        }

        var faceWidth = faces[0].Width;
        var faceHeight = faces[0].Height;

        var facePixels = new nint[6];
        for (var face = 0; face < 6; face++)
        {
            facePixels[face] = faces[face].GetPixels();
        }

        var latLongInfo = new SKImageInfo(faceWidth * 4, faceHeight * 2, colorType, faces[0].Info.AlphaType);
        var latLongBitmap = new SKBitmap(latLongInfo);
        using var latLongPixels = latLongBitmap.PeekPixels();

        // project faces to latlong layout with bilinear sampling
        // Each row is independent, so we can parallelize over y.
        var width = latLongInfo.Width;
        var height = latLongInfo.Height;

        Parallel.For(0, height, y =>
        {
            var latLongSpan = latLongPixels.GetPixelSpan<SKColorF>();
            var v = (y + 0.5f) / height * MathF.PI;
            for (var x = 0; x < width; x++)
            {
                var u = (x + 0.5f) / width * MathF.Tau;

                var (sinU, cosU) = MathF.SinCos(u);
                var (sinV, cosV) = MathF.SinCos(v);

                var dir = new Vector3(sinV * cosU, cosV, sinV * sinU);

                var color = SampleCubemapDirection(facePixels, faceWidth, faceHeight, dir);
                latLongSpan[y * width + x] = color;
            }
        });

        return latLongBitmap;
    }

    private static unsafe SKColorF SampleCubemapDirection(nint[] facePixels, int faceWidth, int faceHeight, Vector3 dir)
    {
        // determine which face the direction vector intersects and get the corresponding UV coordinates
        var absDir = Vector3.Abs(dir);
        var maxAxis = absDir.MaxComponent();

        int faceIndex;
        float uc, vc;

        if (maxAxis == absDir.X)
        {
            if (dir.X > 0)
            {
                faceIndex = 0; // right
                uc = -dir.Z / absDir.X;
                vc = -dir.Y / absDir.X;
            }
            else
            {
                faceIndex = 1; // left
                uc = dir.Z / absDir.X;
                vc = -dir.Y / absDir.X;
            }
        }
        else if (maxAxis == absDir.Y)
        {
            if (dir.Y > 0)
            {
                faceIndex = 4; // up
                uc = dir.X / absDir.Y;
                vc = dir.Z / absDir.Y;
            }
            else
            {
                faceIndex = 5; // down
                uc = dir.X / absDir.Y;
                vc = -dir.Z / absDir.Y;
            }
        }
        else
        {
            if (dir.Z > 0)
            {
                faceIndex = 3; // front
                uc = dir.X / absDir.Z;
                vc = -dir.Y / absDir.Z;
            }
            else
            {
                faceIndex = 2; // back
                uc = -dir.X / absDir.Z;
                vc = -dir.Y / absDir.Z;
            }
        }

        // convert from [-1,1] to [0,1]
        uc = (uc + 1) / 2;
        vc = (vc + 1) / 2;

        // adjust UVs for pre-rotated faces from GPU
        (uc, vc) = faceIndex switch
        {
            0 => (vc, 1 - uc), // right face: rotated 90 deg clockwise
            1 => (1 - vc, uc), // left face: rotated -90 deg (counterclockwise)
            2 or 5 => (1 - uc, 1 - vc), // back and down face: rotated 180 deg
            _ => (uc, vc)
        };

        // sample from face with bilinear filtering
        var fx = uc * faceWidth - 0.5f;
        var fy = vc * faceHeight - 0.5f;

        var x0 = (int)MathF.Floor(fx);
        var y0 = (int)MathF.Floor(fy);
        var x1 = x0 + 1;
        var y1 = y0 + 1;

        var tx = fx - x0;
        var ty = fy - y0;

        var colorSpan = new ReadOnlySpan<SKColorF>((void*)facePixels[faceIndex], faceWidth * faceHeight);

        // get the four neighboring pixels
        var c00 = SampleCubemapSeamless(colorSpan, x0, y0, faceWidth, faceHeight);
        var c10 = SampleCubemapSeamless(colorSpan, x1, y0, faceWidth, faceHeight);
        var c01 = SampleCubemapSeamless(colorSpan, x0, y1, faceWidth, faceHeight);
        var c11 = SampleCubemapSeamless(colorSpan, x1, y1, faceWidth, faceHeight);

        // bilinear interpolation
        var r = c00.Red * (1 - tx) * (1 - ty) + c10.Red * tx * (1 - ty) + c01.Red * (1 - tx) * ty + c11.Red * tx * ty;
        var g = c00.Green * (1 - tx) * (1 - ty) + c10.Green * tx * (1 - ty) + c01.Green * (1 - tx) * ty + c11.Green * tx * ty;
        var b = c00.Blue * (1 - tx) * (1 - ty) + c10.Blue * tx * (1 - ty) + c01.Blue * (1 - tx) * ty + c11.Blue * tx * ty;
        var a = c00.Alpha * (1 - tx) * (1 - ty) + c10.Alpha * tx * (1 - ty) + c01.Alpha * (1 - tx) * ty + c11.Alpha * tx * ty;

        return new SKColorF(r, g, b, a);
    }

    private static SKColorF SampleCubemapSeamless(ReadOnlySpan<SKColorF> colorSpan, int x, int y, int faceWidth, int faceHeight)
    {
        // if within bounds, sample directly
        if (x >= 0 && x < faceWidth && y >= 0 && y < faceHeight)
        {
            return colorSpan[y * faceWidth + x];
        }

        // clamp out of bounds samples
        var clampedX = Math.Clamp(x, 0, faceWidth - 1);
        var clampedY = Math.Clamp(y, 0, faceHeight - 1);

        return colorSpan[clampedY * faceWidth + clampedX];
    }
}

