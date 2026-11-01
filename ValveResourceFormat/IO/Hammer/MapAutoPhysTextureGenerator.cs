using System.Buffers;
using System.Globalization;
using System.Reflection;
using SkiaSharp;

namespace ValveResourceFormat.IO;

/// <summary>
/// Generates placeholder textures for auto-generated physics surfaces and entity volumes.
/// </summary>
internal static class MapAutoPhysTextureGenerator
{
    private const int TextureSize = 64;
    private const float MinimumFontSize = 6f;

    private static readonly SKTypeface Typeface = LoadTypeface();
    private static readonly SearchValues<char> WordBreaks = SearchValues.Create([' ', '_']);

    /// <summary>
    /// Generates a texture with the surface name for auto physics.
    /// </summary>
    public static SKBitmap GenerateTexture(string surfaceName)
        => GenerateTexture("S2V\nAUTOPHYS", surfaceName, new SKColor(52, 58, 90), new SKColor(69, 143, 255), SKColors.White);

    /// <summary>
    /// Generates a texture for the volumes of entities of a class, with the class name in the color of the class
    /// and each word on its own line.
    /// </summary>
    /// <param name="entityClassName">The class of the entity.</param>
    public static SKBitmap GenerateTriggerTexture(string entityClassName)
    {
        var colors = ToolsMaterials.GetTriggerColors(entityClassName);
        var backgroundColor = new SKColor(colors.Background.R, colors.Background.G, colors.Background.B);
        var textColor = new SKColor(colors.Text.R, colors.Text.G, colors.Text.B);

        return GenerateTexture(null, entityClassName.Replace('_', '\n'), backgroundColor, textColor, textColor);
    }

    private static SKTypeface LoadTypeface()
    {
        using var fontStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ValveResourceFormat.Utils.RobotoAscii.ttf");
        using var fontData = SKData.Create(fontStream);
        return SKTypeface.FromData(fontData);
    }

    private static SKBitmap GenerateTexture(string? header, string name, SKColor backgroundColor, SKColor headerColor, SKColor nameColor)
    {
        var bitmap = new SKBitmap(TextureSize, TextureSize);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(backgroundColor);

        using var font = new SKFont(Typeface, size: 10)
        {
            Embolden = true,
        };
        using var textPaint = new SKPaint
        {
            Color = headerColor,
            IsAntialias = true,
        };

        var upperName = name.ToUpper(CultureInfo.InvariantCulture);
        var headerLineCount = 0;
        var text = upperName;

        if (header != null)
        {
            headerLineCount = header.Count('\n') + 1;
            text = $"{header}\n{upperName}";
        }

        // A word wider than a line would be split, so the font shrinks until the widest one fits, down to a legible size
        var lineWidth = bitmap.Width - 4f;
        var widestWord = 0f;

        foreach (var word in text.Split(['\n', ' ', '_']))
        {
            widestWord = MathF.Max(widestWord, font.MeasureText(word, textPaint));
        }

        if (widestWord > lineWidth)
        {
            font.Size = MathF.Max(MinimumFontSize, font.Size * lineWidth / widestWord);
        }

        var lines = BreakLines(text, font, textPaint, lineWidth);
        var textHeight = GetTextHeight(font, lines.Count);

        // Too many lines to fit shrink the font further, which may also fit more words on a line
        if (textHeight > bitmap.Height)
        {
            font.Size = MathF.Max(MinimumFontSize, font.Size * bitmap.Height / textHeight);
            lines = BreakLines(text, font, textPaint, lineWidth);
            textHeight = GetTextHeight(font, lines.Count);
        }

        var metrics = font.Metrics;
        var lineHeight = metrics.Bottom - metrics.Top;

        var textX = bitmap.Width / 2;
        var textY = -metrics.Top + (bitmap.Height - textHeight) / 2;

        for (var i = 0; i < lines.Count; i++)
        {
            if (i == headerLineCount)
            {
                textPaint.Color = nameColor;
            }

            var line = lines[i];
            canvas.DrawText(line, textX, textY, SKTextAlign.Center, font, textPaint);
            textY += lineHeight;
        }

        return bitmap;
    }

    private static float GetTextHeight(SKFont font, int lineCount)
    {
        var metrics = font.Metrics;
        return lineCount * (metrics.Bottom - metrics.Top) - metrics.Leading;
    }

    private static List<string> BreakLines(ReadOnlySpan<char> text, SKFont font, SKPaint paint, float width)
    {
        var lines = new List<string>(6);

        do
        {
            // Explicit line breaks end a line, and the text up to one wraps on its own when it is too wide
            var lineEnd = text.IndexOf('\n');
            var segment = lineEnd > -1 ? text[..lineEnd] : text;
            var lengthBreak = font.BreakText(segment, width, paint);

            if (lengthBreak < segment.Length)
            {
                // Prefer splitting on underscores
                var newLine = segment[..lengthBreak].LastIndexOfAny(WordBreaks);

                if (newLine > 0)
                {
                    lengthBreak = newLine;

                    if (segment.Length - lengthBreak > lengthBreak)
                    {
                        // If remaining text is longer than the current break, include underline on it
                        lengthBreak = newLine + 1;
                    }
                }
            }

            if (lengthBreak == 0 && !segment.IsEmpty)
            {
                break;
            }

            lines.Add(segment[..lengthBreak].ToString());
            text = text[lengthBreak..];

            if (lineEnd > -1 && lengthBreak == segment.Length)
            {
                text = text[1..];
            }
        }
        while (text.Length > 0);

        return lines;
    }
}
