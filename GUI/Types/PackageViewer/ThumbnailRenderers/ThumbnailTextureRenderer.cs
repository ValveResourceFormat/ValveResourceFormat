using System.Drawing;
using System.Threading;
using GUI.Utils;
using SkiaSharp;
using SteamDatabase.ValvePak;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.PackageViewer.ThumbnailRenderers;

internal class ThumbnailTextureRenderer : ThumbnailRenderer
{
    public override Bitmap? Render(PackageEntry entry, VrfGuiContext context, CancellationToken cancellationToken)
    {
        using var resource = LoadResourceFromPackageEntry(context, entry);

        if (resource == null)
        {
            return null;
        }

        using var bitmap = ((Texture)(resource.DataBlock!)).GenerateBitmap();
        var size = (int)Size;
        using var resizedBitmap = bitmap.Resize(new SKSizeI(size, size), SKSamplingOptions.Default);

        return resizedBitmap.ToBitmap();
    }
};
