using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SkiaSharp;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.IO;

public sealed class TextureExtract
{
    private readonly string fileName;
    private readonly Texture texture;
    private readonly SKBitmap bitmap;
    private readonly Texture.SpritesheetData spriteSheetData;

    public TextureExtract(string fileName, Texture texture)
    {
        this.fileName = fileName;
        this.texture = texture;
        bitmap = texture.GenerateBitmap();
        spriteSheetData = texture.GetSpriteSheetData();
    }

    /// <summary>
    /// The vtex content file. Input image(s) come as subfiles.
    /// </summary>
    public ContentFile ToContentFile()
    {
        var contentFile = new ContentFile()
        {
            Data = Encoding.UTF8.GetBytes(ToValveTexture())
        };

        if (TryGetMksData(out var sprites, out var mks))
        {
            contentFile.AddSubFile(
                GetMksFileName(),
                () => Encoding.UTF8.GetBytes(mks)
            );

            foreach (var (spriteRect, spriteName) in sprites)
            {
                contentFile.AddSubFile(
                    spriteName,
                    () => SubsetToPngImage(spriteRect)
                );
            }

            return contentFile;
        }

        contentFile.AddSubFile(
            GetImageFileName(),
            ToPngImage
        );

        return contentFile;
    }

    private string GetImageFileName()
        => Path.ChangeExtension(fileName, "png");

    private string GetMksFileName()
        => Path.ChangeExtension(fileName, "mks");

    private static byte[] EncodePng(SKBitmap bitmap)
    {
        using var pixels = bitmap.PeekPixels();
        using var png = pixels.Encode(SKPngEncoderOptions.Default);
        return png.ToArray();
    }

    public byte[] ToPngImage()
        => EncodePng(bitmap);

    private byte[] SubsetToPngImage(SKRectI spriteRect)
    {
        using var subset = new SKBitmap();
        bitmap.ExtractSubset(subset, spriteRect);

        return EncodePng(subset);
    }

    public bool TryGetMksData(out Dictionary<SKRectI, string> sprites, out string mks)
    {
        mks = string.Empty;
        sprites = new Dictionary<SKRectI, string>();

        if (spriteSheetData is null)
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

                if (imageRect.IsEmpty)
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
        if (spriteSheetData is not null)
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
        @"    ""m_vClamp"" ""vector3"" ""0 0 0""",
        @"    ""m_bNoLod"" ""bool"" """"",
        @"}");
    }
}

