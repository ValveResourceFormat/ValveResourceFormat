using System.Drawing;
using System.Threading;
using GUI.Utils;
using SkiaSharp;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.PackageViewer.ThumbnailRenderers;

internal class ThumbnailTextureRenderer : ThumbnailRenderer
{
    public override void SetResource(Resource resource) { }

    public override Bitmap? Render(PackageEntry entry, VrfGuiContext context, ThumbnailSizes Size, CancellationToken cancellationToken)
    {
        using var resource = LoadResourceFromPackageEntry(context, entry);

        if (resource == null)
        {
            return null;
        }

        var textureData = (Texture)(resource.DataBlock!);
        var size = (int)Size;

        var isCubemap = (textureData.Flags & VTexFlags.CUBE_TEXTURE) != 0;

        // Find the highest mip level that is still higher than the thumbnail size
        var mipLevel = 0;

        for (var i = 1; i < textureData.NumMipLevels; i++)
        {
            if (Math.Max(textureData.Width >> i, 1) < size || Math.Max(textureData.Height >> i, 1) < size)
            {
                break;
            }

            mipLevel = i;
        }

        using var decoded = textureData.GenerateBitmap(mipLevel: (uint)mipLevel);

        // A raw cubemap face decodes 90° counter-clockwise, so rotate it upright.
        using var rotated = isCubemap ? RotateClockwise90(decoded) : null;
        var bitmap = rotated ?? decoded;

        var originalWidth = bitmap.Width;
        var originalHeight = bitmap.Height;

        int renderWidth, renderHeight;

        if (originalWidth <= 0 || originalHeight <= 0)
        {
            renderWidth = size;
            renderHeight = size;
        }
        else
        {
            var scale = Math.Min((float)size / originalWidth, (float)size / originalHeight);
            renderWidth = Math.Max(1, (int)Math.Round(originalWidth * scale));
            renderHeight = Math.Max(1, (int)Math.Round(originalHeight * scale));
        }

        using var resizedBitmap = bitmap.Resize(new SKSizeI(renderWidth, renderHeight), SKSamplingOptions.Default);

        if (renderWidth == size && renderHeight == size)
        {
            return resizedBitmap.ToBitmap();
        }

        using var surface = SKSurface.Create(new SKImageInfo(size, size, resizedBitmap.ColorType, resizedBitmap.AlphaType));
        var canvas = surface.Canvas;
        var offsetX = (size - renderWidth) / 2;
        var offsetY = (size - renderHeight) / 2;
        canvas.DrawBitmap(resizedBitmap, offsetX, offsetY);

        using var snapshot = surface.Snapshot();
        using var finalBitmap = SKBitmap.FromImage(snapshot);
        return finalBitmap.ToBitmap();
    }

    private static SKBitmap RotateClockwise90(SKBitmap source)
    {
        var rotated = new SKBitmap(source.Height, source.Width, source.ColorType, source.AlphaType);

        using var canvas = new SKCanvas(rotated);
        canvas.Translate(rotated.Width, 0);
        canvas.RotateDegrees(90);
        canvas.DrawBitmap(source, 0, 0);

        return rotated;
    }
}
