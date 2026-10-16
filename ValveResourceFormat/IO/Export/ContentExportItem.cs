using System.IO;
using System.Threading;

namespace ValveResourceFormat.IO;

/// <summary>
/// A file for a <see cref="ContentExportQueue"/> to export.
/// </summary>
public sealed class ContentExportItem
{
    /// <summary>Gets the content path of the file, a compiled resource keeps its <c>_c</c> suffix.</summary>
    public required string ContentPath { get; init; }

    /// <summary>Gets a function that opens the file's contents. It may be called from any thread, more than once.</summary>
    public required Func<Stream> Open { get; init; }

    /// <summary>
    /// Gets where the file is read from, such as the path of its package or of the file itself. A resource that was
    /// already exported from another origin is exported again, see <see cref="ContentExporter.TryBeginExport"/>.
    /// Defaults to the package or folder of <see cref="Source"/>.
    /// </summary>
    public string? Origin { get; init; }

    /// <summary>
    /// Gets where the file came from, its dependencies are exported from there with
    /// <see cref="ContentExporter.IncludeDependencies"/>.
    /// </summary>
    public ContentExportSource? Source { get; init; }

    /// <summary>Gets the loader a compiled resource's dependencies are read through while decompiling it.</summary>
    public IFileLoader? FileLoader { get; init; }

    /// <summary>
    /// Gets whether the file is decompiled, or extracted when it is not a compiled resource. Otherwise it is written as
    /// it is, at any point of the export. Defaults to true.
    /// </summary>
    public bool Decompile { get; init; } = true;

    /// <summary>Gets the CRC32 the file's contents must have when it is written as it is.</summary>
    public uint? Crc32 { get; init; }

    /// <summary>Gets a full path to write the main file to instead of its content path.</summary>
    public string? TargetFile { get; init; }

    /// <summary>
    /// Gets a callback that exports a compiled resource instead of decompiling it, such as to glTF. It returns false to
    /// decompile the resource after all.
    /// </summary>
    public Func<Resource, CancellationToken, bool>? ExportResource { get; init; }

    /// <summary>
    /// Gets a callback that exports a file that is not a compiled resource instead of extracting it. It returns false
    /// to extract the file after all.
    /// </summary>
    public Func<Stream, CancellationToken, bool>? ExportFile { get; init; }

    /// <summary>
    /// Gets whether the queue added the file as a dependency of another, see
    /// <see cref="ContentExporter.IncludeDependencies"/>.
    /// </summary>
    public bool IsDependency { get; internal init; }

    /// <summary>Gets what happened to the file, once <see cref="ContentExportQueue.Run"/> returned.</summary>
    public ContentExportResult Result { get; internal set; }
}
