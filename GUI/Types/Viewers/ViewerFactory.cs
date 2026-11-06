using System.Buffers.Binary;
using System.IO;
using System.Threading.Tasks;
using GUI.Utils;
using ValvePak;

namespace GUI.Types.Viewers;

// detects correct viewer for file and creates it
static class ViewerFactory
{
    /// <summary>The number of bytes at the start of a file that <see cref="ReadHeader"/> reads.</summary>
    public const int HeaderLength = sizeof(uint) + sizeof(ushort);

    /// <summary>
    /// Reads the four byte magic and the two bytes after it from the current position, and leaves the stream there.
    /// </summary>
    /// <returns>The magic and the version, which are zero when the stream is shorter than <see cref="HeaderLength"/>.</returns>
    public static (uint Magic, ushort Version) ReadHeader(Stream stream)
    {
        if (stream.Length - stream.Position < HeaderLength)
        {
            return (0, 0);
        }

        Span<byte> header = stackalloc byte[HeaderLength];
        var start = stream.Position;
        stream.ReadExactly(header);
        stream.Position = start;

        return (BinaryPrimitives.ReadUInt32LittleEndian(header), BinaryPrimitives.ReadUInt16LittleEndian(header[sizeof(uint)..]));
    }

    public static async Task<IViewer> CreateAndLoadAsync(VrfGuiContext vrfGuiContext, PackageEntry? entry, ResourceViewMode viewMode)
    {
        await Task.Yield();

        Stream? stream = null;
        uint magic;
        ushort magicResourceVersion;

        if (entry != null)
        {
            var parentContext = vrfGuiContext.ParentGuiContext;
            if (parentContext?.CurrentPackage == null)
            {
                throw new InvalidDataException("Parent context or package is null");
            }

            stream = parentContext.CurrentPackage.GetMemoryMappedStreamIfPossible(entry);
            (magic, magicResourceVersion) = ReadHeader(stream);
        }
        else
        {
            using var fs = new FileStream(vrfGuiContext.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            (magic, magicResourceVersion) = ReadHeader(fs);
        }

        if (PackageViewer.PackageViewer.IsAccepted(magic))
        {
            if (stream != null)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }

            var viewer = new PackageViewer.PackageViewer(vrfGuiContext);
            viewer.Load(entry);
            return viewer;
        }
        else if (GoldSrcContent.IsAccepted(magic, magicResourceVersion, vrfGuiContext.FileName))
        {
            var resource = GoldSrcContent.Convert(vrfGuiContext, stream);
            var viewer = new Resource(vrfGuiContext, viewMode);
            viewer.Load(resource);
            return viewer;
        }
        else if (GoldSrcContent.IsWad(magic))
        {
            var package = GoldSrcContent.OpenWad(vrfGuiContext.FileName, stream);

            if (stream != null)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }

            var viewer = new PackageViewer.PackageViewer(vrfGuiContext);
            viewer.LoadReadOnly(package);
            return viewer;
        }
        else if (Source1Content.IsAccepted(magic, vrfGuiContext.FileName))
        {
            var converted = Source1Content.Convert(vrfGuiContext, stream, magic);
            var viewer = new Resource(vrfGuiContext, viewMode);
            viewer.Load(converted.Resource, converted.OwnedFileSystem);
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
        else if (NavSpaceView.IsAccepted(magic))
        {
            var viewer = new NavSpaceView(vrfGuiContext);
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
            var viewer = new Resource(vrfGuiContext, viewMode);
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
