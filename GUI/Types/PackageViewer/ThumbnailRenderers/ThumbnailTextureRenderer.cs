using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using GUI.Utils;
using SkiaSharp;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.TextureDecoders;

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

        var bitmap = ((Texture)(resource.DataBlock!)).GenerateBitmap();

        var size = (int)Size;
        return bitmap.Resize(new SKSizeI(size, size), SKSamplingOptions.Default).ToBitmap();
    }
};
