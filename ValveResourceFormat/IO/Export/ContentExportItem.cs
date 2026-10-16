using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using ValvePak;

namespace ValveResourceFormat.IO;

/// <summary>
/// A file for <see cref="ContentExporter.Export(IEnumerable{ContentExportItem}, CancellationToken)"/> to export.
/// </summary>
public sealed class ContentExportItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ContentExportItem"/> class.
    /// </summary>
    public ContentExportItem()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentExportItem"/> class for a file in a package, which is also
    /// its <see cref="Source"/>.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="entry">The file in the package.</param>
    [SetsRequiredMembers]
    public ContentExportItem(Package package, PackageEntry entry)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(entry);

        ContentPath = entry.GetFullPath();
        Open = () => GameFileLoader.GetPackageEntryStream(package, entry);
        Crc32 = entry.CRC32 > 0 ? entry.CRC32 : null;
        Source = ContentExportSource.FromPackage(package);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentExportItem"/> class for a file on disk. Its content path and
    /// <see cref="Source"/> are found from where it is, see <see cref="ContentPaths.GetContentPathFromDiskOrFileName"/>
    /// and <see cref="ContentExportSource.FromDiskFile"/>.
    /// </summary>
    /// <param name="path">The file on disk.</param>
    /// <param name="warning">Receives a warning when the file is in no addon, mod or asset folder.</param>
    [SetsRequiredMembers]
    public ContentExportItem(string path, Action<string>? warning = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        var fullPath = Path.GetFullPath(path);

        ContentPath = ContentPaths.GetContentPathFromDiskOrFileName(fullPath, warning);
        Open = () => File.OpenRead(fullPath);
        Origin = fullPath;
        Source = ContentExportSource.FromDiskFile(fullPath);
        DiskPath = fullPath;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentExportItem"/> class for the same file as another item, with
    /// the same options, which an object initializer can then change.
    /// </summary>
    /// <param name="item">The item to copy.</param>
    [SetsRequiredMembers]
    public ContentExportItem(ContentExportItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        ContentPath = item.ContentPath;
        Open = item.Open;
        Origin = item.Origin;
        Source = item.Source;
        FileLoader = item.FileLoader;
        Output = item.Output;
        Crc32 = item.Crc32;
        TargetFile = item.TargetFile;
        DiskPath = item.DiskPath;
    }

    /// <summary>Gets the content path of the file, a compiled resource keeps its <c>_c</c> suffix.</summary>
    public required string ContentPath { get; init; }

    /// <summary>Gets a function that opens the file's contents. It may be called from any thread, more than once.</summary>
    public required Func<Stream> Open { get; init; }

    /// <summary>
    /// Gets where the file is read from, such as the path of the file on disk. A resource that was already exported
    /// from another origin is exported again. Defaults to the package or folder of <see cref="Source"/>.
    /// </summary>
    public string? Origin { get; init; }

    /// <summary>
    /// Gets where the file came from, its dependencies are exported from there with
    /// <see cref="ContentExporter.IncludeDependencies"/>.
    /// </summary>
    public ContentExportSource? Source { get; init; }

    /// <summary>
    /// Gets the loader a compiled resource's dependencies are read through while exporting it. When not set, the
    /// files it references are not loaded.
    /// </summary>
    public IFileLoader? FileLoader { get; init; }

    /// <summary>Gets what the file is exported as. Defaults to <see cref="ContentExportOutput.Decompile"/>.</summary>
    public ContentExportOutput Output { get; init; }

    /// <summary>Gets the CRC32 the file's contents must have when it is written as it is.</summary>
    public uint? Crc32 { get; init; }

    /// <summary>
    /// Gets a full path to write the main file to instead of its content path. A glTF export is written there with
    /// the extension of its format.
    /// </summary>
    public string? TargetFile { get; init; }

    /// <summary>
    /// Gets whether the export added the file as a dependency of another, see
    /// <see cref="ContentExporter.IncludeDependencies"/>.
    /// </summary>
    public bool IsDependency { get; internal init; }

    /// <summary>Gets what happened to the file, once the export returned.</summary>
    public ContentExportResult Result { get; internal set; }

    /// <summary>Gets the full path of the file on disk, when it was created from one.</summary>
    internal string? DiskPath { get; }

    /// <summary>
    /// Whether exporting the file as <see cref="ContentExportOutput.Decompile"/> writes anything other than the file
    /// as it is: it is a compiled resource, a compiled shader in a package or on disk, or a file
    /// <see cref="FileExtract.ExtractNonResource(Stream, string)"/> extracts.
    /// </summary>
    public bool CanDecompile()
    {
        if (IsInShaderLocation || ContentPaths.HasCompiledSuffix(ContentPath))
        {
            return true;
        }

        using var stream = Open();

        return ContentExportQueue.IsResource(stream) || FileExtract.CanExtractNonResource(stream, ContentPath);
    }

    /// <summary>
    /// Gets whether the file is decompiled together with the other files of its compiled shader.
    /// </summary>
    internal bool IsShaderFile => Output != ContentExportOutput.Raw && IsInShaderLocation;

    private bool IsInShaderLocation
        => (Source?.Package != null || DiskPath != null)
            && Path.GetExtension(ContentPath.AsSpan()).Equals(".vcs", StringComparison.OrdinalIgnoreCase);
}
