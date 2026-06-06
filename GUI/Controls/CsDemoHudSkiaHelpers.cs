using System.Drawing;
using SkiaSharp;

namespace GUI.Controls
{
    internal static class CsDemoHudSkiaHelpers
    {
        private static readonly SKTypeface SegoeUiBold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;
        private static readonly SKTypeface SegoeUiRegular = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal) ?? SKTypeface.Default;

        public static SKColor Rgba(byte r, byte g, byte b, byte a) => new(r, g, b, a);

        public static SKRect ToSkRect(Rectangle rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);

        public static void FillRoundRect(SKCanvas canvas, SKRect rect, SKColor color, float radius)
        {
            using var paint = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.DrawRoundRect(rect, radius, radius, paint);
        }

        public static void DrawRoundRect(SKCanvas canvas, SKRect rect, SKColor color, float radius, float strokeWidth = 1f)
        {
            using var paint = new SKPaint
            {
                Color = color,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = strokeWidth,
            };
            canvas.DrawRoundRect(rect, radius, radius, paint);
        }

        public static void FillGlassRect(SKCanvas canvas, SKRect rect, SKColor top, SKColor bottom, float radius)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(rect.Left, rect.Top),
                    new SKPoint(rect.Left, rect.Bottom),
                    [top, bottom],
                    SKShaderTileMode.Clamp),
            };
            canvas.DrawRoundRect(rect, radius, radius, paint);
        }

        public static void FillVerticalGradient(SKCanvas canvas, SKRect rect, SKColor top, SKColor bottom)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(rect.Left, rect.Top),
                    new SKPoint(rect.Left, rect.Bottom),
                    [top, bottom],
                    SKShaderTileMode.Clamp),
            };
            canvas.DrawRect(rect, paint);
        }

        public static void DrawCenteredText(SKCanvas canvas, string text, SKRect rect, float textSize, SKColor color, bool bold = true)
        {
            using var font = new SKFont(bold ? SegoeUiBold : SegoeUiRegular, textSize) { Subpixel = true };
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            var textWidth = font.MeasureText(text);
            var metrics = font.Metrics;
            var y = rect.MidY - ((metrics.Ascent + metrics.Descent) / 2f);
            canvas.DrawText(text, rect.MidX - (textWidth / 2f), y, font, paint);
        }

        public static void DrawText(SKCanvas canvas, string text, SKRect rect, float textSize, SKColor color, SKTextAlign align, bool bold = true)
        {
            using var font = new SKFont(bold ? SegoeUiBold : SegoeUiRegular, textSize) { Subpixel = true };
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            var metrics = font.Metrics;
            var y = rect.MidY - ((metrics.Ascent + metrics.Descent) / 2f);
            var x = align switch
            {
                SKTextAlign.Center => rect.MidX,
                SKTextAlign.Right => rect.Right,
                _ => rect.Left,
            };
            canvas.DrawText(text, x, y, font, paint);
        }

        public static void DrawBitmapClipped(SKCanvas canvas, SKBitmap bitmap, SKRect dest, float cornerRadius)
        {
            canvas.Save();
            using (var clip = new SKRoundRect(dest, cornerRadius, cornerRadius))
            {
                canvas.ClipRoundRect(clip);
            }

            DrawBitmapScaled(canvas, bitmap, dest);
            canvas.Restore();
        }

        public static void DrawBitmap(SKCanvas canvas, SKBitmap? bitmap, SKRect dest)
        {
            if (bitmap == null)
            {
                return;
            }

            DrawBitmapScaled(canvas, bitmap, dest);
        }

        private static void DrawBitmapScaled(SKCanvas canvas, SKBitmap bitmap, SKRect dest)
        {
            using var paint = new SKPaint { IsAntialias = true };
            var source = new SKRect(0, 0, bitmap.Width, bitmap.Height);
            canvas.DrawBitmap(bitmap, source, dest, paint);
        }
    }
}
