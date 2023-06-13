using System.Drawing.Imaging;
using System.Drawing;
using SkiaSharp;
using System;

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
            var bitmap = new Bitmap(skiaImage.Width, skiaImage.Height, PixelFormat.Format32bppPArgb);
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
