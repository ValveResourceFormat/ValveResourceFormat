using System.Buffers;
using System.Globalization;
using System.IO;
using System.Reflection;
using SkiaSharp;

namespace ValveResourceFormat.IO;

public static class MapAutoPhysTextureGenerator
{
    public static SKBitmap GenerateTexture(string surfaceName)
    {
        using var fontStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ValveResourceFormat.Utils.RobotoAscii.ttf");
        using var typeface = SKTypeface.FromStream(fontStream);

        var backgroundColor = new SKColor(52, 58, 90);
        var textColor = new SKColor(69, 143, 255);

        var bitmap = new SKBitmap(128, 128);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(backgroundColor);

        using var textPaint = new SKPaint
        {
            Color = textColor,
            TextSize = 16,
            TextAlign = SKTextAlign.Center,
            IsAntialias = true,
            Typeface = typeface,
        };

        var lines = BreakLines($"S2V\nAUTOPHYS\n{surfaceName.ToUpper(CultureInfo.InvariantCulture)}", textPaint, bitmap.Width);

        var metrics = textPaint.FontMetrics;
        var lineHeight = metrics.Bottom - metrics.Top;
        var textHeight = lines.Count * lineHeight - metrics.Leading;

        var textX = bitmap.Width / 2;
        var textY = -metrics.Top + (bitmap.Height - textHeight) / 2;

        for (var i = 0; i < lines.Count; i++)
        {
            if (i == 2)
            {
                textPaint.Color = SKColors.White;
            }

            var line = lines[i];
            canvas.DrawText(line, textX, textY, textPaint);
            textY += lineHeight;
        }

        return bitmap;
    }

    private static List<string> BreakLines(ReadOnlySpan<char> text, SKPaint paint, float width)
    {
        var breaks = SearchValues.Create([' ', '_']);
        var lines = new List<string>(6);

        do
        {
            var newLine = text.IndexOf('\n');

            if (newLine > -1)
            {
                // Since we only have 3 line breaks, assume left side is never gonna wrap
                var left = text[..newLine];
                lines.Add(left.ToString());

                text = text[(newLine + 1)..];

                continue;
            }

            var lengthBreak = (int)paint.BreakText(text, width);

            if (lengthBreak < text.Length)
            {
                // Prefer splitting on underscores
                newLine = text[..lengthBreak].LastIndexOfAny(breaks);

                if (newLine > 0)
                {
                    lengthBreak = newLine;

                    if (text.Length - lengthBreak > lengthBreak)
                    {
                        // If remaining text is longer than the current break, include underline on it
                        lengthBreak = newLine + 1;
                    }
                }
            }

            if (lengthBreak == 0)
            {
                break;
            }

            var lastLine = text[..lengthBreak];
            lines.Add(lastLine.ToString());

            text = text[lengthBreak..];
        }
        while (text.Length > 0);

        return lines;
    }
}
