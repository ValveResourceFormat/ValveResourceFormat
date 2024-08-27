using System.Drawing;
using System.Drawing.Imaging;
using SkiaSharp;

namespace GUI.Utils
{
    static class SkiaSharpExtensions
    {
        public static Bitmap ToBitmap(this SKBitmap skiaBitmap)
        {
            using var pixmap = skiaBitmap.PeekPixels();
            using var skiaImage = SKImage.FromPixels(pixmap);
            var result = skiaImage.ToBitmap();
            GC.KeepAlive(skiaBitmap);
            return result;
        }

        public static Bitmap ToBitmap(this SKImage skiaImage)
        {
            var bitmap = new Bitmap(skiaImage.Width, skiaImage.Height, skiaImage.ColorType switch
            {
                SKColorType.Bgra8888 => skiaImage.AlphaType == SKAlphaType.Premul ? PixelFormat.Format32bppPArgb : PixelFormat.Format32bppArgb,
                SKColorType.Rgb888x => PixelFormat.Format32bppRgb,
                _ => PixelFormat.Format32bppArgb,
            });
            var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);

            using (var pixmap = new SKPixmap(new SKImageInfo(bitmapData.Width, bitmapData.Height), bitmapData.Scan0, bitmapData.Stride))
            {
                skiaImage.ReadPixels(pixmap, 0, 0);
            }

            bitmap.UnlockBits(bitmapData);
            return bitmap;
        }
    }
}
