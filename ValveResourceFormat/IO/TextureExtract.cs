using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using SkiaSharp;
using TinyEXR;
using ValveResourceFormat.IO.ContentFormats.ValveTexture;
using ValveResourceFormat.ResourceTypes;
using ChannelMapping = ValveResourceFormat.CompiledShader.ChannelMapping;

namespace ValveResourceFormat.IO;

/// <summary>
/// Represents a content file for textures with an associated bitmap.
/// </summary>
public class TextureContentFile : ContentFile
{
    /// <summary>
    /// Gets or initializes the bitmap data.
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
    /// Gets or initializes the bitmap data.
    /// </summary>
    public required SKBitmap Bitmap { get; init; }

    /// <summary>
    /// Gets or initializes the image extraction function.
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

    // Options
    /// <summary>
    /// Gets or sets the decode flags for texture extraction.
    /// </summary>
    public TextureDecoders.TextureCodec DecodeFlags { get; set; } = TextureDecoders.TextureCodec.Auto;

    /// <summary>
    /// Whether to combine cubemap faces into a single latlong image.
    /// </summary>
    public bool LatLongCombineCubemap { get; set; } = true;

    /// <summary>
    /// Should the vtex file be ignored. Defaults to true for files flagged as child resources.
    /// </summary>
    public bool IgnoreVtexFile { get; set; }

    /// <summary>
    /// Gets whether the texture should be exported as EXR format.
    /// </summary>
    public bool ExportExr => texture.IsHighDynamicRange && !DecodeFlags.HasFlag(TextureDecoders.TextureCodec.ForceLDR);

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
        IgnoreVtexFile = FileExtract.IsChildResource(resource);
        isSpriteSheet = texture.ExtraData?.ContainsKey(VTexExtraData.SHEET) ?? false;
        isCubeMap = texture.Flags.HasFlag(VTexFlags.CUBE_TEXTURE);
        isArray = texture.Depth > 1;
    }

    /// <summary>
    /// The vtex content file. Input image(s) come as subfiles.
    /// </summary>
    public ContentFile ToContentFile()
    {
        var rawImage = texture.ReadRawImageData();
        if (rawImage != null)
        {
            return new ContentFile() { Data = rawImage };
        }

        Func<SKBitmap, byte[]> ImageEncode = ExportExr ? ToExrImage : ToPngImage;

        //
        // Multiple images path
        //
        if (isArray || isCubeMap)
        {
            var contentFile = new ContentFile()
            {
                FileName = fileName,
            };

            for (uint depth = 0; depth < texture.Depth; depth++)
            {
                var outTextureName = Path.GetFileNameWithoutExtension(fileName);

                if (isArray)
                {
                    outTextureName += isCubeMap ? $"_f{depth:D2}" : $"_z{depth:D3}";
                }

                if (!isCubeMap)
                {
                    var currentDepth = depth;

                    contentFile.AddSubFile(outTextureName + ImageOutputExtension, () =>
                    {
                        return ImageEncode(texture.GenerateBitmap(depth: currentDepth, decodeFlags: DecodeFlags));
                    });

                    continue;
                }

                if (LatLongCombineCubemap && texture.IsHighDynamicRange)
                {
                    var currentDepth = depth;
                    contentFile.AddSubFile($"{outTextureName}{ImageOutputExtension}", () =>
                    {
                        var bitmaps = new SKBitmap[6];
                        var faces = new SKPixmap[6];
                        try
                        {
                            for (var face = 0; face < 6; face++)
                            {
                                bitmaps[face] = texture.GenerateBitmap(depth: currentDepth, face: (Texture.CubemapFace)face, decodeFlags: DecodeFlags);
                                faces[face] = bitmaps[face].PeekPixels();
                            }

                            using var latLongBitmap = CreateLatLongFromCubemapFaces(faces);
                            return ImageEncode(latLongBitmap);
                        }
                        finally
                        {
                            for (var face = 0; face < 6; face++)
                            {
                                faces[face]?.Dispose();
                                bitmaps[face]?.Dispose();
                            }
                        }
                    });

                }
                else
                {
                    for (var face = 0; face < 6; face++)
                    {
                        var currentDepth = depth;
                        var currentFace = face;

                        contentFile.AddSubFile($"{outTextureName}_{CubemapNames[face]}{ImageOutputExtension}", () =>
                        {
                            using var bitmap = texture.GenerateBitmap(depth: currentDepth, face: (Texture.CubemapFace)currentFace, decodeFlags: DecodeFlags);
                            return ImageEncode(bitmap);
                        });
                    }
                }
            }

            return contentFile;
        }

        var bitmap = texture.GenerateBitmap(decodeFlags: DecodeFlags);

        var vtex = new TextureContentFile()
        {
            Data = IgnoreVtexFile ? null : Encoding.UTF8.GetBytes(ToValveTexture()),
            Bitmap = bitmap,
            FileName = fileName!,
        };

        if (TryGetMksData(out var sprites, out var mks))
        {
            vtex.AddSubFile(Path.GetFileName(GetMksFileName())!, () => Encoding.UTF8.GetBytes(mks));

            foreach (var (spriteRect, spriteFileName) in sprites)
            {
                vtex.AddImageSubFile(Path.GetFileName(spriteFileName)!, (bitmap) => SubsetToPngImage(bitmap, spriteRect));
            }

            return vtex;
        }

        vtex.AddImageSubFile(Path.GetFileName(GetImageFileName())!, ImageEncode);
        return vtex;
    }

    /// <summary>
    /// Extracts texture to material map files by unpacking channels.
    /// </summary>
    public ContentFile ToMaterialMaps(IEnumerable<MaterialExtract.UnpackInfo> mapsToUnpack)
    {
        // unpacking not supported in these scenarios
        if (isCubeMap || isArray || ExportExr)
        {
            var vtexContent = ToContentFile();

            if (isCubeMap && LatLongCombineCubemap && ExportExr)
            {
                // use the file name set in material properties
                vtexContent.SubFiles[0].FileName = Path.GetFileName(mapsToUnpack.First().FileName);
            }

            return vtexContent;
        }

        var bitmap = texture.GenerateBitmap(decodeFlags: DecodeFlags);
        bitmap.SetImmutable();

        var vtex = new TextureContentFile()
        {
            Bitmap = bitmap,
            FileName = fileName!,
        };

        foreach (var unpackInfo in mapsToUnpack)
        {
            vtex.AddImageSubFile(Path.GetFileName(unpackInfo.FileName)!, (bitmap) => ToPngImageChannels(bitmap, unpackInfo.Channel));
        }

        return vtex;
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

    private string GetImageFileName()
        => Path.ChangeExtension(fileName, ImageOutputExtension);

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

            for (var i = 0; i < pixels.Length; i++)
            {
                newPixels[i] = channel.Channels[0] switch
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

#pragma warning disable CS8509 // non exhaustive switch
        for (var i = 0; i < srcPixelSpan.Length; i++)
        {
            pixelSpan[i] = dstChannel.Channels[0] switch
            {
                ChannelMapping.Channel.R => srcChannel.Channels[0] switch
                {
                    ChannelMapping.Channel.R => pixelSpan[i].WithRed(srcPixelSpan[i].Red),
                    ChannelMapping.Channel.G => pixelSpan[i].WithRed(srcPixelSpan[i].Green),
                    ChannelMapping.Channel.B => pixelSpan[i].WithRed(srcPixelSpan[i].Blue),
                    ChannelMapping.Channel.A => pixelSpan[i].WithRed(srcPixelSpan[i].Alpha),
                },
                ChannelMapping.Channel.G => srcChannel.Channels[0] switch
                {
                    ChannelMapping.Channel.R => pixelSpan[i].WithGreen(srcPixelSpan[i].Red),
                    ChannelMapping.Channel.G => pixelSpan[i].WithGreen(srcPixelSpan[i].Green),
                    ChannelMapping.Channel.B => pixelSpan[i].WithGreen(srcPixelSpan[i].Blue),
                    ChannelMapping.Channel.A => pixelSpan[i].WithGreen(srcPixelSpan[i].Alpha),
                },
                ChannelMapping.Channel.B => srcChannel.Channels[0] switch
                {
                    ChannelMapping.Channel.R => pixelSpan[i].WithBlue(srcPixelSpan[i].Red),
                    ChannelMapping.Channel.G => pixelSpan[i].WithBlue(srcPixelSpan[i].Green),
                    ChannelMapping.Channel.B => pixelSpan[i].WithBlue(srcPixelSpan[i].Blue),
                    ChannelMapping.Channel.A => pixelSpan[i].WithBlue(srcPixelSpan[i].Alpha),
                },
                ChannelMapping.Channel.A => srcChannel.Channels[0] switch
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
        /// Gets or initializes the default color for unpacked channels.
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
        var options = new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, zLibLevel: 4);

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
        var pixelSpan = pixels.GetPixelSpan<SKColorF>();
        var floatSpan = MemoryMarshal.Cast<SKColorF, float>(pixelSpan);
        var result = Exr.SaveEXRToMemory(floatSpan, pixels.Width, pixels.Height, components: 4, asFp16: false, out var exrData);
        if (result != ResultCode.Success)
        {
            throw new InvalidOperationException($"Got result {result} while saving EXR image");
        }

        return exrData;
    }

    /// <summary>
    /// Attempts to extract sprite sheet data and MKS texture script.
    /// </summary>
    public bool TryGetMksData(out Dictionary<SKRectI, string> sprites, out string mks)
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

        var mksBuilder = new StringBuilder();
        var textureName = Path.GetFileNameWithoutExtension(fileName);
        var packmodeNonFlat = false;

        for (var s = 0; s < spriteSheetData.Sequences.Length; s++)
        {
            var sequence = spriteSheetData.Sequences[s];
            mksBuilder.AppendLine();

            switch (sequence.NoColor, sequence.NoAlpha)
            {
                case (false, false):
                    mksBuilder.AppendLine(CultureInfo.InvariantCulture, $"sequence {s}");
                    break;

                case (false, true):
                    mksBuilder.AppendLine(CultureInfo.InvariantCulture, $"sequence-rgb {s}");
                    packmodeNonFlat = true;
                    break;

                case (true, false):
                    mksBuilder.AppendLine(CultureInfo.InvariantCulture, $"sequence-a {s}");
                    packmodeNonFlat = true;
                    break;

                case (true, true):
                    throw new InvalidDataException($"Unexpected combination of {nameof(sequence.NoColor)} and {nameof(sequence.NoAlpha)}");
            }

            if (!sequence.Clamp)
            {
                mksBuilder.AppendLine("LOOP");
            }

            for (var f = 0; f < sequence.Frames.Length; f++)
            {
                var frame = sequence.Frames[f];

                var imageFileName = sequence.Frames.Length == 1
                    ? $"{textureName}_seq{s}.png"
                    : $"{textureName}_seq{s}_{f}.png";

                // These images seem to be duplicates. So only extract the first one.
                var image = frame.Images[0];
                var imageRect = image.GetCroppedRect(texture.ActualWidth, texture.ActualHeight);

                if (imageRect.Size.Width == 0 || imageRect.Size.Height == 0)
                {
                    continue;
                }

                var displayTime = frame.DisplayTime;
                if (sequence.Clamp && displayTime == 0)
                {
                    displayTime = 1;
                }

                sprites.TryAdd(imageRect, imageFileName);
                mksBuilder.AppendLine(CultureInfo.InvariantCulture, $"frame {sprites[imageRect]} {displayTime.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        if (packmodeNonFlat)
        {
            mksBuilder.Insert(0, "packmode rgb+a\n");
        }

        mksBuilder.Insert(0, $"// Reconstructed with {StringToken.VRF_GENERATOR}\n\n");
        mks = mksBuilder.ToString();
        return true;
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
        var inputTextureFileName = GetInputFileNameForVtex();
        var outputFormat = texture.Format.ToString();

        using var datamodel = new Datamodel.Datamodel("vtex", 1);
        datamodel.Root = CDmeVtex.CreateTexture2D([(inputTextureFileName, "rgba", "Box")], outputFormat);

        using var stream = new MemoryStream();
        datamodel.Save(stream, "keyvalues2_noids", 1);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Creates a latlong layout bitmap from 6 projected cubemap faces.
    /// </summary>
    public static SKBitmap CreateLatLongFromCubemapFaces(SKPixmap[] faces)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(faces.Length, 6, nameof(faces));

        var colorType = faces[0].Info.ColorType;
        if (colorType != SKColorType.RgbaF16 && colorType != SKColorType.RgbaF32)
        {
            throw new InvalidOperationException($"Cubemap faces must be HDR format (RgbaF16 or RgbaF32), got {colorType}");
        }

        var faceWidth = faces[0].Width;
        var faceHeight = faces[0].Height;

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
            var v = (y + 0.5f) / height * (float)Math.PI;
            for (var x = 0; x < width; x++)
            {
                var u = (x + 0.5f) / width * 2 * (float)Math.PI;

                // direction vector
                var dir = new Vector3(
                    (float)(Math.Sin(v) * Math.Cos(u)),
                    (float)Math.Cos(v),
                    (float)(Math.Sin(v) * Math.Sin(u))
                );

                var color = SampleCubemapDirection(faces, dir);
                latLongSpan[y * width + x] = color;
            }
        });

        return latLongBitmap;
    }

    private static SKColorF SampleCubemapDirection(SKPixmap[] faces, Vector3 dir)
    {
        var faceWidth = faces[0].Width;
        var faceHeight = faces[0].Height;

        // determine which face the direction vector intersects and get the corresponding UV coordinates
        var absDir = new Vector3(Math.Abs(dir.X), Math.Abs(dir.Y), Math.Abs(dir.Z));
        var maxAxis = Math.Max(absDir.X, Math.Max(absDir.Y, absDir.Z));

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

        var colorSpan = faces[faceIndex].GetPixelSpan<SKColorF>();

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

