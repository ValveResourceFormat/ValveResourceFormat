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

        try
        {
            svg.Load(ms);
        }
        catch
        {
        }

        if (svg == null || svg.Picture == null)
        {
            return null;
        }

        var originalWidth = svg.Picture.CullRect.Width;
        var originalHeight = svg.Picture.CullRect.Height;

        int renderWidth, renderHeight;

        if (originalWidth <= 0 || originalHeight <= 0)
        {
            renderWidth = size;
            renderHeight = size;
        }
        else
        {
            var scale = Math.Min(size / originalWidth, size / originalHeight);
            renderWidth = Math.Max(1, (int)Math.Round(originalWidth * scale));
            renderHeight = Math.Max(1, (int)Math.Round(originalHeight * scale));
        }

        var svgBitmap = Themer.SvgToBitmap(svg, renderWidth, renderHeight);

        if (svgBitmap == null || (renderWidth == size && renderHeight == size))
        {
            return svgBitmap;
        }

        var result = new Bitmap(size, size, svgBitmap.PixelFormat);

        using var g = Graphics.FromImage(result);
        var offsetX = (size - renderWidth) / 2;
        var offsetY = (size - renderHeight) / 2;
        g.DrawImage(svgBitmap, offsetX, offsetY, renderWidth, renderHeight);
        svgBitmap.Dispose();

        return result;
    }
}
