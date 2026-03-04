using System.Drawing;
using System.Threading;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveResourceFormat;

namespace GUI.Types.PackageViewer.ThumbnailRenderers;

internal enum ThumbnailSizes : int
{
    Tiny = 24,
    Small = 128,
    Medium = 192,
    Big = 256,
}

internal interface IThumbnailRenderer : IDisposable
{
    public ResourceType RendererType { get; }
    public bool Loaded { get; }

    // gl load
    void Load(VrfGuiContext context);

    public Bitmap? Render(PackageEntry entry, VrfGuiContext context, CancellationToken cancellationToken);
}
