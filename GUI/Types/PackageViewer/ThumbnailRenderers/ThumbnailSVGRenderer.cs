using System.Drawing;
using System.IO;
using System.Threading;
using GUI.Utils;
using SteamDatabase.ValvePak;
using Svg.Skia;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.PackageViewer.ThumbnailRenderers;

internal class ThumbnailSVGRenderer : ThumbnailRenderer
{
    public override void SetResource(Resource resource) { }

    public override Bitmap? Render(PackageEntry entry, VrfGuiContext context, ThumbnailSizes Size, CancellationToken cancellationToken)
    {
        using var resource = LoadResourceFromPackageEntry(context, entry);

        if (resource == null)
        {
            return null;
        }

        var panoramaData = (Panorama)(resource.DataBlock!);
        var size = (int)Size;

        using var ms = new MemoryStream(panoramaData.Data);
        using var svg = new SKSvg();
        svg.Load(ms);

        return Themer.SvgToBitmap(svg, size, size);
    }
}
