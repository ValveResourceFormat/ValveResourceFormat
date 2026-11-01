using System.Globalization;
using System.Reflection;
using SkiaSharp;

namespace ValveResourceFormat.IO;

/// <summary>
/// Generates placeholder textures for auto-generated physics surfaces, tools materials and entity volumes.
/// </summary>
internal static class MapAutoPhysTextureGenerator
{
    private const int TextureSize = 256;
    private const float FontSize = 40f;
    private const float MinimumFontSize = 24f;
    private const float LineMargin = 16f;
    private const float LineSpacing = 1.5f;

    private static readonly SKTypeface Typeface = LoadTypeface();

    /// <summary>
    /// Generates a texture with the surface name for auto physics.
    /// </summary>
    public static SKBitmap GenerateTexture(string surfaceName)
        => GenerateTexture(surfaceName, new SKColor(52, 58, 90), SKColors.White);

    /// <summary>
    /// Generates the texture of a label, such as that of a tools material, an entity volume or a set of collision layers.
    /// </summary>
    /// <param name="label">The label.</param>
    /// <param name="backgroundOpacity">The alpha of the background. The text is always opaque.</param>
    public static SKBitmap GenerateTexture(ToolsMaterials.TextureLabel label, float backgroundOpacity = 1f)
    {
        var background = label.Background;
        var text = label.Text;
        var backgroundAlpha = (byte)MathF.Round(backgroundOpacity * 255f);

        return GenerateTexture(label.Name, new SKColor(background.R, background.G, background.B, backgroundAlpha), new SKColor(text.R, text.G, text.B));
    }

    private static SKTypeface LoadTypeface()
    {
        using var fontStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ValveResourceFormat.Utils.ManropeAscii.ttf");
        using var fontData = SKData.Create(fontStream);
        return SKTypeface.FromData(fontData);
    }

    // Underscores separate the words of the name
    private static SKBitmap GenerateTexture(string name, SKColor backgroundColor, SKColor textColor)
    {
        using var font = new SKFont(Typeface, FontSize);
        using var textPaint = new SKPaint
        {
            Color = textColor,
            IsAntialias = true,
        };

        var text = name.Replace('_', '\n').ToUpper(CultureInfo.InvariantCulture);

        // A word wider than a line would be split, so the font shrinks until the widest one fits, down to a legible size
        var lineWidth = TextureSize - LineMargin;
        var widestWord = 0f;

        foreach (var word in text.Split('\n'))
        {
            widestWord = MathF.Max(widestWord, font.MeasureText(word, textPaint));
        }

        if (widestWord > lineWidth)
        {
            // Text width does not scale exactly with the font size, so a pixel of slack keeps the word on one line
            font.Size = MathF.Max(MinimumFontSize, font.Size * (lineWidth - 1f) / widestWord);
        }

        var lines = BreakLines(text, font, textPaint, lineWidth);
        var textHeight = GetTextHeight(font, lines.Count);
        var maxTextHeight = TextureSize - LineMargin;

        // Too many lines to fit shrink the font further
        if (textHeight > maxTextHeight)
        {
            font.Size = MathF.Max(MinimumFontSize, font.Size * maxTextHeight / textHeight);
            lines = BreakLines(text, font, textPaint, lineWidth);
            textHeight = GetTextHeight(font, lines.Count);
        }

        var lineHeight = GetLineHeight(font);

        // The text is uppercase only, so lines are centered on their cap height
        var textX = TextureSize / 2;
        var textY = font.Metrics.CapHeight + (TextureSize - textHeight) / 2;

        // Unpremultiplied keeps the color of a translucent background
        var bitmap = new SKBitmap(new SKImageInfo(TextureSize, TextureSize, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(backgroundColor);

        foreach (var line in lines)
        {
            canvas.DrawText(line, textX, textY, SKTextAlign.Center, font, textPaint);
            textY += lineHeight;
        }

        return bitmap;
    }

    private static float GetLineHeight(SKFont font) => font.Metrics.CapHeight * LineSpacing;

    private static float GetTextHeight(SKFont font, int lineCount)
        => (lineCount - 1) * GetLineHeight(font) + font.Metrics.CapHeight;

    // Each word goes on its own line, and a word too wide even at the smallest font size wraps where the line runs out
    private static List<string> BreakLines(string text, SKFont font, SKPaint paint, float width)
    {
        var lines = new List<string>(6);

        foreach (var word in text.Split('\n'))
        {
            var rest = word.AsSpan();

            while (!rest.IsEmpty)
            {
                var length = Math.Max(1, font.BreakText(rest, width, paint));
                lines.Add(rest[..length].ToString());
                rest = rest[length..];
            }
        }

        return lines;
    }
}
