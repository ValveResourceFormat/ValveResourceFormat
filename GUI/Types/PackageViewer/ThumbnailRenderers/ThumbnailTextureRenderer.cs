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

    public override Bitmap? Render(PackageEntry entry, VrfGuiContext context, CancellationToken cancellationToken)
    {
        using var resource = LoadResourceFromPackageEntry(context, entry);

        if (resource == null)
        {
            return null;
        }

        var textureData = (Texture)(resource.DataBlock!);
        var size = (int)Size;

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

        using var bitmap = textureData.GenerateBitmap(mipLevel: (uint)mipLevel);
        using var resizedBitmap = bitmap.Resize(new SKSizeI(size, size), SKSamplingOptions.Default);

        return resizedBitmap.ToBitmap();
    }
}
