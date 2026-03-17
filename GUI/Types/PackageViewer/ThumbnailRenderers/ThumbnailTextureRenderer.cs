using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.PackageViewer.ThumbnailRenderers;

internal class ThumbnailTextureRenderer : ThumbnailRenderer
{
    public override void SetResource(Resource resource)
    {
    }

    public override Bitmap? Render(PackageEntry entry, VrfGuiContext context, CancellationToken cancellationToken)
    {
        using var resource = LoadResourceFromPackageEntry(context, entry);

        if (resource == null)
        {
            return null;
        }

        var bitmap = ((Texture)(resource.DataBlock!)).GenerateBitmap();

        return bitmap.ToBitmap();
    }
};
