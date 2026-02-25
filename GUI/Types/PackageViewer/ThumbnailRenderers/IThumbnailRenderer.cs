using System.Drawing;
using System.Threading;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveResourceFormat;

namespace GUI.Types.PackageViewer.ThumbnailRenderers;

internal interface IThumbnailRenderer : IDisposable
{
    public ResourceType RendererType { get; }
    public bool Loaded { get; }

    // gl load
    void Load(VrfGuiContext context);

    public (Bitmap? bitmap, string? cacheKey) Render(PackageEntry entry, VrfGuiContext context, CancellationToken cancellationToken);
}
