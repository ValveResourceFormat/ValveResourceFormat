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
    /// Gets the name of this source: the path of its package, or its folder.
    /// </summary>
    internal string Name => Package?.FileName ?? Folder ?? string.Empty;

    /// <summary>
    /// Opens a file from this source, named by its content path.
    /// </summary>
    /// <returns>The file's contents, or null when this source does not have it.</returns>
    internal Stream? OpenStream(string contentPath)
    {
        if (Package?.FindEntry(contentPath) is { } entry)
        {
            return GameFileLoader.GetPackageEntryStream(Package, entry);
        }

        if (Folder != null && Path.Join(Folder, contentPath) is var path && File.Exists(path))
        {
            return File.OpenRead(path);
        }

        return null;
    }
}
