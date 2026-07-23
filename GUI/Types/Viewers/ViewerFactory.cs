using System.IO;
using System.Threading.Tasks;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveResourceFormat.IO;

namespace GUI.Types.Viewers;

// detects correct viewer for file and creates it
static class ViewerFactory
{
    public static async Task<IViewer> CreateAndLoadAsync(VrfGuiContext vrfGuiContext, PackageEntry? entry, ResourceViewMode viewMode)
    {
        await Task.Yield();

        Stream? stream = null;
        Span<byte> magicData = stackalloc byte[6];

        if (entry != null)
        {
            var parentContext = vrfGuiContext.ParentGuiContext;
            if (parentContext?.CurrentPackage == null)
            {
                throw new InvalidDataException("Parent context or package is null");
            }

            stream = GameFileLoader.GetPackageEntryStream(parentContext.CurrentPackage, entry);

            if (stream.Length >= magicData.Length)
            {
                stream.ReadExactly(magicData);
                stream.Seek(-magicData.Length, SeekOrigin.Current);
            }
        }
        else
        {
            using var fs = new FileStream(vrfGuiContext.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            if (fs.Length >= magicData.Length)
            {
                fs.ReadExactly(magicData);
            }
        }

        var magic = BitConverter.ToUInt32(magicData[..4]);
        var magicResourceVersion = BitConverter.ToUInt16(magicData[4..]);

        if (PackageViewer.PackageViewer.IsAccepted(magic))
        {
            var viewer = new PackageViewer.PackageViewer(vrfGuiContext);
            await viewer.LoadAsync(stream).ConfigureAwait(false);
            return viewer;
        }
        else if (CompiledShader.IsAccepted(magic))
        {
            var viewer = new CompiledShader(vrfGuiContext);
            await viewer.LoadAsync(stream).ConfigureAwait(false);
            return viewer;
        }
        else if (ClosedCaptions.IsAccepted(magic))
        {
            var viewer = new ClosedCaptions(vrfGuiContext);
            await viewer.LoadAsync(stream).ConfigureAwait(false);
            return viewer;
        }
        else if (ToolsAssetInfo.IsAccepted(magic))
        {
            var viewer = new ToolsAssetInfo(vrfGuiContext);
            await viewer.LoadAsync(stream).ConfigureAwait(false);
            return viewer;
        }
        else if (FlexSceneFile.IsAccepted(magic))
        {
            var viewer = new FlexSceneFile(vrfGuiContext);
            await viewer.LoadAsync(stream).ConfigureAwait(false);
            return viewer;
        }
        else if (NavView.IsAccepted(magic))
        {
            var viewer = new NavView(vrfGuiContext);
            await viewer.LoadAsync(stream).ConfigureAwait(false);
            return viewer;
        }
        else if (BinaryKeyValues3.IsAccepted(magic))
        {
            var viewer = new BinaryKeyValues3(vrfGuiContext);
            await viewer.LoadAsync(stream).ConfigureAwait(false);
            return viewer;
        }
        else if (BinaryKeyValues2.IsAccepted(magic, vrfGuiContext.FileName))
        {
            var viewer = new BinaryKeyValues2(vrfGuiContext);
            await viewer.LoadAsync(stream).ConfigureAwait(false);
            return viewer;
        }
        else if (BinaryKeyValues1.IsAccepted(magic))
        {
            var viewer = new BinaryKeyValues1(vrfGuiContext);
            await viewer.LoadAsync(stream).ConfigureAwait(false);
            return viewer;
        }
        else if (Resource.IsAccepted(magicResourceVersion))
        {
            var viewer = new Resource(vrfGuiContext, viewMode, verifyFileSize: entry == null || entry.CRC32 > 0);
            await viewer.LoadAsync(stream).ConfigureAwait(false);
            return viewer;
        }
        // Raw images and audio files do not really appear in Source 2 projects, but we support viewing them anyway.
        // As some detections rely on the file extension instead of magic bytes,
        // they should be detected at the bottom here, after failing to detect a proper resource file.
        else if (Image.IsAccepted(magic))
        {
            var viewer = new Image(vrfGuiContext);
            await viewer.LoadAsync(stream).ConfigureAwait(false);
            return viewer;
        }
        else if (ImageVector.IsAccepted(vrfGuiContext.FileName))
        {
            var viewer = new ImageVector(vrfGuiContext);
            await viewer.LoadAsync(stream).ConfigureAwait(false);
            return viewer;
        }
        else if (Audio.IsAccepted(magic, vrfGuiContext.FileName))
        {
            var viewer = new Audio(vrfGuiContext, viewMode == ResourceViewMode.ViewerOnly);
            await viewer.LoadAsync(stream).ConfigureAwait(false);
            return viewer;
        }
        else if (GridNavFile.IsAccepted(magic))
        {
            var viewer = new GridNavFile(vrfGuiContext);
            await viewer.LoadAsync(stream).ConfigureAwait(false);
            return viewer;
        }
        else if (SpirvBinary.IsAccepted(magic))
        {
            var viewer = new SpirvBinary(vrfGuiContext);
            await viewer.LoadAsync(stream).ConfigureAwait(false);
            return viewer;
        }
        else if (TextKeyValues3.IsAccepted(magic, magicResourceVersion))
        {
            var viewer = new TextKeyValues3(vrfGuiContext);
            await viewer.LoadAsync(stream).ConfigureAwait(false);
            return viewer;
        }

        var byteViewer = new ByteViewer(vrfGuiContext);
        await byteViewer.LoadAsync(stream).ConfigureAwait(false);
        return byteViewer;
    }
}
