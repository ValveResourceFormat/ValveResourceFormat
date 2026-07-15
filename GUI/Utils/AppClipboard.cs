using System.IO;
using System.Windows.Forms;

#pragma warning disable RS0030 // Banned API: this is where all of the winforms clipboard code lives, it will be gone when we switch UI

namespace GUI.Utils;

public static class AppClipboard
{
    public static void SetText(string text) => Clipboard.SetText(text);

    public static string GetText() => Clipboard.GetText();

    public static void SetImage(SkiaSharp.SKBitmap bitmap)
    {
        var data = new DataObject();

        using var bitmapWindows = bitmap.ToBitmap();
        data.SetData(DataFormats.Bitmap, true, bitmapWindows);

        using var pngStream = new MemoryStream();
        using var pixels = bitmap.PeekPixels();
        var png = pixels.Encode(pngStream, new SkiaSharp.SKPngEncoderOptions(SkiaSharp.SKPngEncoderFilterFlags.Sub, zLibLevel: 1));

        bitmapWindows.Save(pngStream, System.Drawing.Imaging.ImageFormat.Png);
        data.SetData("PNG", false, pngStream);

        Clipboard.SetDataObject(data, copy: true);
    }
}
