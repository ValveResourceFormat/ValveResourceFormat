using System.IO;
using ValvePak;

namespace ValveResourceFormat.IO;

/// <summary>
/// The package or folder an exported resource came from. Only dependencies found in the same source are exported
/// along with it, everything else is left referenced.
/// </summary>
public sealed class ContentExportSource
{
    private readonly Package? Package;
    private readonly string? Folder;

    private ContentExportSource(Package? package, string? folder)
    {
        Package = package;
        Folder = folder;
    }

    /// <summary>
    /// Dependencies come from the given package.
    /// </summary>
    public static ContentExportSource FromPackage(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return new(package, null);
    }

    /// <summary>
    /// Dependencies come from loose files below the given addon or mod root.
    /// </summary>
    public static ContentExportSource FromFolder(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return new(null, Path.GetFullPath(folder));
    }

    /// <summary>
    /// Gets the source of a file on disk: the addon or mod root it is in, or null when it is in neither.
    /// </summary>
    public static ContentExportSource? FromDiskFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Path.GetDirectoryName(Path.GetFullPath(path)) is { } directory && ContentPaths.FindContentRoot(directory) is { } root
            ? new(null, root)
            : null;
    }

    /// <summary>
    /// Reads a compiled resource from this source, named by its content path.
    /// </summary>
    /// <returns>The resource, or null when this source does not have it.</returns>
    internal Resource? Load(string compiledContentPath)
    {
        Stream? stream = null;

        if (Package?.FindEntry(compiledContentPath) is { } entry)
        {
            stream = GameFileLoader.GetPackageEntryStream(Package, entry);
        }
        else if (Folder != null && Path.Join(Folder, compiledContentPath) is var path && File.Exists(path))
        {
            stream = File.OpenRead(path);
        }

        if (stream == null)
        {
            return null;
        }

        var resource = new Resource
        {
            FileName = compiledContentPath,
        };

        try
        {
            resource.Read(stream);
            return resource;
        }
        catch
        {
            resource.Dispose();
            throw;
        }
    }
}
