using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SkiaSharp;
using ValveResourceFormat.ResourceTypes;

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

public sealed class ImageSubFile : ContentSubFile
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
            vtex.AddSubFile(GetMksFileName(), () => Encoding.UTF8.GetBytes(mks));

            foreach (var (spriteRect, spriteFileName) in sprites)
            {
                vtex.AddImageSubFile(spriteFileName, (bitmap) => SubsetToPngImage(bitmap, spriteRect));
            }

            return vtex;
        }

        vtex.AddImageSubFile(GetImageFileName(), ToPngImage);
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

    private static byte[] EncodePng(SKBitmap bitmap)
    {
        using var pixels = bitmap.PeekPixels();
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

