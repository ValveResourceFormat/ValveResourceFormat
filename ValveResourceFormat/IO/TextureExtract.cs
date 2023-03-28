using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SkiaSharp;
using ValveResourceFormat.ResourceTypes;
using static ValveResourceFormat.IO.MaterialExtract;
namespace ValveResourceFormat.IO;

public class TextureContentFile : ContentFile
{
    public SKBitmap Bitmap { get; init; }

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

    protected override void Dispose(bool disposing)
    {
        if (!Disposed && disposing)
        {
            Bitmap.Dispose();
        }

        base.Dispose(disposing);
    }
}

public sealed class ImageSubFile : SubFile
{
    public SKBitmap Bitmap { get; init; }
    public Func<SKBitmap, byte[]> ImageExtract { get; init; }
    public override Func<byte[]> Extract => () => ImageExtract(Bitmap);
}

public sealed class TextureExtract
{
    private readonly Texture texture;
    private readonly string fileName;
    private readonly bool isSpriteSheet;

    public TextureExtract(Texture texture, string fileName)
    {
        this.texture = texture;
        this.fileName = fileName;

        if (texture.ExtraData.ContainsKey(VTexExtraData.SHEET))
        {
            isSpriteSheet = true;
        }
    }

    public TextureExtract(Resource resource)
        : this((Texture)resource.DataBlock, resource.FileName)
    {
    }

    /// <summary>
    /// The vtex content file. Input image(s) come as subfiles.
    /// </summary>
    public TextureContentFile ToContentFile()
    {
        var bitmap = texture.GenerateBitmap();

        var vtex = new TextureContentFile()
        {
            Data = Encoding.UTF8.GetBytes(ToValveTexture()),
            Bitmap = bitmap
        };

        if (TryGetMksData(out var sprites, out var mks))
        {
            vtex.AddSubFile(Path.GetFileName(GetMksFileName()), () => Encoding.UTF8.GetBytes(mks));

            foreach (var (spriteRect, spriteFileName) in sprites)
            {
                vtex.AddImageSubFile(Path.GetFileName(spriteFileName), (bitmap) => SubsetToPngImage(bitmap, spriteRect));
            }

            return vtex;
        }

        vtex.AddImageSubFile(Path.GetFileName(GetImageFileName()), ToPngImage);
        return vtex;
    }

    public TextureContentFile ToMaterialMaps(IEnumerable<MaterialExtract.UnpackInfo> mapsToUnpack)
    {
        var bitmap = texture.GenerateBitmap();
        bitmap.SetImmutable();

        var vtex = new TextureContentFile()
        {
            Bitmap = bitmap
        };

        foreach (var unpackInfo in mapsToUnpack)
        {
            vtex.AddImageSubFile(Path.GetFileName(unpackInfo.FileName), (bitmap) => ToPngImageChannels(bitmap, unpackInfo.Channel));
        }

        return vtex;
    }

    private string GetImageFileName()
        => Path.ChangeExtension(fileName, "png");

    private string GetMksFileName()
        => Path.ChangeExtension(fileName, "mks");

    public static byte[] ToPngImage(SKBitmap bitmap)
    {
        return EncodePng(bitmap);
    }

    public static byte[] SubsetToPngImage(SKBitmap bitmap, SKRectI spriteRect)
    {
        using var subset = new SKBitmap();
        bitmap.ExtractSubset(subset, spriteRect);

        return EncodePng(subset);
    }

    public static byte[] ToPngImageChannels(SKBitmap bitmap, Channel channel)
    {
        if (channel < Channel._Single)
        {
            using var newBitmap = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Gray8, SKAlphaType.Opaque);
            using var newPixelmap = newBitmap.PeekPixels();
            using var pixelmap = bitmap.PeekPixels();

            var newPixels = newPixelmap.GetPixelSpan<byte>();
            var pixels = pixelmap.GetPixelSpan<SKColor>();

            for (var i = 0; i < pixels.Length; i++)
            {
                newPixels[i] = channel switch
                {
                    Channel.R => pixels[i].Red,
                    Channel.G => pixels[i].Green,
                    Channel.B => pixels[i].Blue,
                    Channel.A => pixels[i].Alpha,
                    _ => throw new InvalidOperationException($"{channel} is not a single channel."),
                };
            }

            return EncodePng(newPixelmap);
        }

        // Shift a combination of two channels to RG
        if (channel < Channel._Double && channel == Channel.GA)
        {
            using var newBitmap = new SKBitmap(bitmap.Info);
            using var newPixelmap = newBitmap.PeekPixels();
            using var pixelmap = bitmap.PeekPixels();

            var newPixels = newPixelmap.GetPixelSpan<SKColor>();
            var pixels = pixelmap.GetPixelSpan<SKColor>();

            for (var i = 0; i < pixels.Length; i++)
            {
                newPixels[i] = newPixels[i].WithRed(pixels[i].Green).WithGreen(pixels[i].Alpha);
            }

            return EncodePng(newPixelmap);
        }

        // Wipes out the alpha channel
        if (channel == Channel.RGB)
        {
            using var newBitmap = new SKBitmap(bitmap.Info);
            using var newPixelmap = newBitmap.PeekPixels();
            using var pixelmap = bitmap.PeekPixels();
            pixelmap.GetPixelSpan<SKColor>().CopyTo(newPixelmap.GetPixelSpan<SKColor>());

            return EncodePng(newPixelmap.WithAlphaType(SKAlphaType.Opaque));
        }

        if (channel == Channel.RGBA)
        {
            return EncodePng(bitmap);
        }

        throw new InvalidOperationException($"{channel} is not a valid channel to extract.");
    }

    public static void CopyChannel(SKPixmap srcPixels, Channel srcChannel, SKPixmap dstPixels, Channel dstChannel, bool invert)
    {
        if (srcChannel >= Channel._Single)
        {
            throw new ArgumentOutOfRangeException(nameof(srcChannel), "Can only copy individual channels.");
        }

        if (dstChannel >= Channel._Single)
        {
            throw new ArgumentOutOfRangeException(nameof(dstChannel), "Can only copy individual channels.");
        }

        var srcPixelSpan = srcPixels.GetPixelSpan<SKColor>();
        var pixelSpan = dstPixels.GetPixelSpan<SKColor>();

        var inv = (byte)(invert ? 0xFF : 0x00);

#pragma warning disable CS8509 // non exhaustive switch
        for (var i = 0; i < srcPixelSpan.Length; i++)
        {
            pixelSpan[i] = dstChannel switch
            {
                Channel.R => srcChannel switch
                {
                    Channel.R => pixelSpan[i].WithRed((byte)(srcPixelSpan[i].Red ^ inv)),
                    Channel.G => pixelSpan[i].WithRed((byte)(srcPixelSpan[i].Green ^ inv)),
                    Channel.B => pixelSpan[i].WithRed((byte)(srcPixelSpan[i].Blue ^ inv)),
                    Channel.A => pixelSpan[i].WithRed((byte)(srcPixelSpan[i].Alpha ^ inv)),
                },
                Channel.G => srcChannel switch
                {
                    Channel.R => pixelSpan[i].WithGreen((byte)(srcPixelSpan[i].Red ^ inv)),
                    Channel.G => pixelSpan[i].WithGreen((byte)(srcPixelSpan[i].Green ^ inv)),
                    Channel.B => pixelSpan[i].WithGreen((byte)(srcPixelSpan[i].Blue ^ inv)),
                    Channel.A => pixelSpan[i].WithGreen((byte)(srcPixelSpan[i].Alpha ^ inv)),
                },
                Channel.B => srcChannel switch
                {
                    Channel.R => pixelSpan[i].WithBlue((byte)(srcPixelSpan[i].Red ^ inv)),
                    Channel.G => pixelSpan[i].WithBlue((byte)(srcPixelSpan[i].Green ^ inv)),
                    Channel.B => pixelSpan[i].WithBlue((byte)(srcPixelSpan[i].Blue ^ inv)),
                    Channel.A => pixelSpan[i].WithBlue((byte)(srcPixelSpan[i].Alpha ^ inv)),
                },
                Channel.A => srcChannel switch
                {
                    Channel.R => pixelSpan[i].WithAlpha((byte)(srcPixelSpan[i].Red ^ inv)),
                    Channel.G => pixelSpan[i].WithAlpha((byte)(srcPixelSpan[i].Green ^ inv)),
                    Channel.B => pixelSpan[i].WithAlpha((byte)(srcPixelSpan[i].Blue ^ inv)),
                    Channel.A => pixelSpan[i].WithAlpha((byte)(srcPixelSpan[i].Alpha ^ inv)),
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
        public SKColor DefaultColor { get; init; } = SKColors.Black;
        public SKBitmap Bitmap { get; private set; }
        public string FileName { get; private set; }
        private readonly HashSet<Channel> Packed = new();

        public void Collect(SKPixmap srcPixels, string fileName, Channel srcChannel, Channel dstChannel, bool invert = false)
        {
            FileName ??= fileName;

            if (!Packed.Add(dstChannel))
            {
                Console.WriteLine($"{dstChannel} has already been packed in texture: {FileName}");
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
                    if (!oldPixels.ScalePixels(newPixels, SKFilterQuality.Low))
                    {
                        throw new InvalidOperationException($"Failed to scale up pixels of {FileName}");
                    }
                }
                Bitmap = newBitmap;
            }
            else if (Bitmap.Width > srcPixels.Width || Bitmap.Height > srcPixels.Height)
            {
                // Scale srcPixels up to Bitmap size
                using var newSrcBitmap = new SKBitmap(Bitmap.Width, Bitmap.Height, true);
                using var newSrcPixels = newSrcBitmap.PeekPixels();
                if (!srcPixels.ScalePixels(newSrcPixels, SKFilterQuality.Low))
                {
                    throw new InvalidOperationException($"Failed to scale up incoming pixels for {FileName}");
                }
                using var dstPixels2 = Bitmap.PeekPixels();
                CopyChannel(newSrcPixels, srcChannel, dstPixels2, dstChannel, invert);
                return;
            }

            using var dstPixels = Bitmap.PeekPixels();
            CopyChannel(srcPixels, srcChannel, dstPixels, dstChannel, invert);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Bitmap != null && disposing)
            {
                Bitmap.Dispose();
                Bitmap = null;
            }
        }

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
        using var png = pixels.Encode(SKPngEncoderOptions.Default);
        return png.ToArray();
    }

    public bool TryGetMksData(out Dictionary<SKRectI, string> sprites, out string mks)
    {
        mks = string.Empty;
        sprites = new Dictionary<SKRectI, string>();

        if (!isSpriteSheet)
        {
            return false;
        }

        var spriteSheetData = texture.GetSpriteSheetData();

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

                if (imageRect.Size.IsEmpty)
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

        mksBuilder.Insert(0, "// Reconstructed with VRF - https://vrf.steamdb.info/\n\n");
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

    public string ToValveTexture()
    {
        var inputTextureFileName = GetInputFileNameForVtex();
        var outputFormat = texture.Format.ToString();

        return string.Join(Environment.NewLine,
        "<!-- dmx encoding keyvalues2_noids 1 format vtex 1 -->",
        @"""CDmeVtex""",
        @"{",
        @"    ""m_inputTextureArray"" ""element_array""",
        @"    [",
        @"        ""CDmeInputTexture""",
        @"        {",
        @"            ""m_name"" ""string"" ""InputTexture0""",
        $@"            ""m_fileName"" ""string"" ""{inputTextureFileName}""",
        @"            ""m_colorSpace"" ""string"" ""srgb""",
        @"            ""m_typeString"" ""string"" ""2D""",
        @"            ""m_imageProcessorArray"" ""element_array""",
        @"            [",
        @"                ""CDmeImageProcessor""",
        @"                {",
        @"                    ""m_algorithm"" ""string"" ""None""",
        @"                    ""m_stringArg"" ""string"" """"",
        @"                    ""m_vFloat4Arg"" ""vector4"" ""0 0 0 0""",
        @"                }",
        @"            ]",
        @"        }",
        @"    ]",
        @"    ""m_outputTypeString"" ""string"" ""2D""",
        $@"    ""m_outputFormat"" ""string"" ""{outputFormat}""",
        @"    ""m_outputClearColor"" ""vector4"" ""0 0 0 0""",
        @"    ""m_nOutputMinDimension"" ""int"" ""0""",
        @"    ""m_nOutputMaxDimension"" ""int"" ""0""",
        @"    ""m_textureOutputChannelArray"" ""element_array"" ",
        @"    [",
        @"        ""CDmeTextureOutputChannel""",
        @"        {",
        @"            ""m_inputTextureArray"" ""string_array"" [ ""InputTexture0"" ]",
        @"            ""m_srcChannels"" ""string"" ""rgba""",
        @"            ""m_dstChannels"" ""string"" ""rgba""",
        @"            ""m_mipAlgorithm"" ""CDmeImageProcessor""",
        @"            {",
        @"                ""m_algorithm"" ""string"" ""Box""",
        @"                ""m_stringArg"" ""string"" """"",
        @"                ""m_vFloat4Arg"" ""vector4"" ""0 0 0 0""",
        @"            }",
        @"            ""m_outputColorSpace"" ""string"" ""srgb""",
        @"        }",
        @"    ]",
        @"}");
    }
}

