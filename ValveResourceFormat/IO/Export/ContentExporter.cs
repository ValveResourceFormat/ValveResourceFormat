using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Threading;
using ValveResourceFormat.CompiledShader;

namespace ValveResourceFormat.IO;

/// <summary>
/// Decides where every file of an export goes and writes it: decompiled resources with their sub files, additional
/// files and optionally their dependencies, and raw files.
/// </summary>
/// <remarks>
/// <para>
/// Every file is written to <see cref="OutputRoot"/> at its content path (<see cref="ContentFile.FileName"/>,
/// <see cref="SubFile.FileName"/>), except a main file given an explicit target. A path that would leave the root is
/// brought back under it with a warning, see <see cref="ContentPaths.Normalize(string, out bool)"/>.
/// </para>
/// <para>
/// One instance covers one export run and may be used from several threads. A path is written once per run: the same
/// content again is skipped. Different content for a path that was already written goes to a unique name next to it,
/// and the file that references it, the closest content file above it with data, is pointed at that name. A file that
/// references two different files at the same path keeps its references and gets a warning. Images decoded from
/// textures (<see cref="SubFile.ImagePixelCount"/>) are not renamed: the one with the most pixels is kept.
/// </para>
/// <para>
/// Whether files that existed before the export are replaced is decided before anything of a content file and the files
/// below it is written, so <see cref="ExistingFileAction.Cancel"/> leaves no part of it behind, and stops every thread of
/// the export at its next file. A file this export wrote is never treated as existing.
/// </para>
/// <para>
/// <see cref="Export(IEnumerable{ContentExportItem}, CancellationToken)"/> exports many files through one instance in
/// parallel. File contents do not depend on the order files are exported in, but when two different files claim the
/// same path, which one keeps it does.
/// </para>
/// </remarks>
public sealed class ContentExporter
{
    private sealed class References
    {
        public List<(string From, string To)> Renamed { get; } = [];
        public HashSet<string> Kept { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A file of a content file to write: a sub file, or the data of a content file once the files below it are written.
    /// </summary>
    private sealed record PlannedFile(string OutputPath, string? ContentPath, References References, SubFile? SubFile = null, ContentFile? ContentFile = null, References? OwnReferences = null);

    private sealed class PathState
    {
        public Lock Lock { get; } = new();
        public UInt128? WrittenHash { get; set; }
        public bool? KeepExisting { get; set; }
    }

    private const string CancelledMessage = "The export was cancelled because a file already exists.";

    private static readonly FrozenDictionary<ResourceType, int> ExportOrder = new[]
    {
        ResourceType.Map,
        ResourceType.World,
        ResourceType.WorldNode,
        ResourceType.ResourceManifest,
        ResourceType.Model,
        ResourceType.Mesh,
        ResourceType.PhysicsCollisionMesh,
        ResourceType.AnimationGroup,
        ResourceType.Animation,
        ResourceType.Sequence,
        ResourceType.Morph,
        ResourceType.Material,
        ResourceType.Texture,
    }.Select(static (type, index) => KeyValuePair.Create(type, index)).ToFrozenDictionary();

    internal static readonly FrozenSet<ResourceType> ConsumedDependencyTypes = new[]
    {
        ResourceType.Animation,
        ResourceType.AnimationGroup,
        ResourceType.EntityLump,
        ResourceType.Mesh,
        ResourceType.Morph,
        ResourceType.NmGraphVariation,
        ResourceType.PhysicsCollisionMesh,
        ResourceType.ResourceManifest,
        ResourceType.ResourceRemapTable,
        ResourceType.Sequence,
        ResourceType.Shader,
        ResourceType.Sheet,
        ResourceType.World,
        ResourceType.WorldEnvironmentMaps,
        ResourceType.WorldLighting,
        ResourceType.WorldNode,
        ResourceType.WorldVisibility,
    }.ToFrozenSet();

    private readonly Lock StateLock = new();
    private readonly Lock ExistingFilesLock = new();
    private readonly Dictionary<string, (long ImagePixelCount, UInt128 Hash)> ClaimedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> ReservedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PathState> PathStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> HandledFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> NonGltfItems = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> ExportOrigins = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<IFileLoader, GltfModelExporter> GltfExporters = new(ReferenceEqualityComparer.Instance);
    private readonly NullFileLoader NullFileLoader = new();
    private ExistingFileAction? ConfirmedExistingFileAction;
    private volatile bool Cancelled;
    private int FilesWritten;
    private int FilesFailed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentExporter"/> class.
    /// </summary>
    /// <param name="outputRoot">
    /// The folder content paths are relative to, usually from <see cref="ContentPaths.ResolveOutputRoot(string, IEnumerable{string})"/>.
    /// </param>
    public ContentExporter(string outputRoot)
    {
        ArgumentNullException.ThrowIfNull(outputRoot);
        OutputRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
    }

    /// <summary>Gets the folder content paths are relative to.</summary>
    public string OutputRoot { get; }

    /// <summary>
    /// Gets whether exporting a resource also exports every resource it references, directly or through other
    /// references, that is in the same <see cref="ContentExportSource"/>. Resources that only exist as part of another,
    /// such as meshes, are not exported on their own but their references are followed. A file with no source gets a
    /// warning that its dependencies are not exported.
    /// </summary>
    public bool IncludeDependencies { get; init; }

    /// <summary>
    /// Gets what to do with files that existed before the export, when <see cref="ConfirmExistingFiles"/> is not set.
    /// Defaults to <see cref="ExistingFileAction.Overwrite"/>.
    /// </summary>
    public ExistingFileAction ExistingFileAction { get; init; }

    /// <summary>
    /// Gets a callback asked once, with the path of the first file that already exists, what to do with every existing
    /// file of the export. It may be called from any thread the export runs on.
    /// </summary>
    public Func<string, ExistingFileAction>? ConfirmExistingFiles { get; init; }

    /// <summary>Gets the options resources are decompiled with.</summary>
    public FileExtractOptions ExtractOptions { get; init; } = new();

    /// <summary>Gets the reporter extractors send their progress messages to.</summary>
    public IProgress<string>? Progress { get; init; }

    /// <summary>
    /// Gets a function that creates the glTF exporter for the items with a glTF <see cref="ContentExportItem.Output"/>,
    /// given the loader their dependencies are read through. It is called once per loader, and its
    /// <see cref="GltfModelExporter.Exporter"/> is set to this exporter. When not set, a <see cref="GltfModelExporter"/>
    /// with its default options that reports to <see cref="Progress"/> is used.
    /// </summary>
    public Func<IFileLoader, GltfModelExporter>? GltfExporterFactory { get; init; }

    /// <summary>
    /// Gets a callback that takes every content file of the export and every file written as it is, in place of writing
    /// them, such as to print them. glTF exports are still written. See <see cref="ContentFile.MainSubFile"/> for the
    /// file that stands for a content file without data.
    /// </summary>
    public Action<ContentFile>? ContentFileHandler { get; init; }

    /// <summary>
    /// Gets how many files <see cref="Export(IEnumerable{ContentExportItem}, CancellationToken)"/> exports at once.
    /// Defaults to <see cref="Environment.ProcessorCount"/>.
    /// </summary>
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;

    /// <summary>
    /// Gets the options of this export as text, which turns on incremental exports of packages when set. The files of
    /// each package that exported are recorded with their CRC32 in <c>&lt;package&gt;.manifest.txt</c> in
    /// <see cref="OutputRoot"/>, and the next export of the package to the same root with the same options and version
    /// skips the files that did not change, see <see cref="ContentExportResult.Unchanged"/>. A changed file of a
    /// compiled shader exports the whole shader again, and a changed resource that is consumed by another, such as a
    /// mesh, exports the resources that consume it again. It needs to hold every option that changes what is exported
    /// and that this exporter does not know of, such as the options of <see cref="GltfExporterFactory"/>.
    /// </summary>
    public string? ManifestOptions { get; init; }

    /// <summary>Gets a callback receiving every item before it is exported, on the thread that exports it.</summary>
    public Action<ContentExportItem>? ItemStarted { get; init; }

    /// <summary>
    /// Gets a callback receiving every item once it was exported or skipped, with its
    /// <see cref="ContentExportItem.Result"/> set.
    /// </summary>
    public Action<ContentExportItem>? ItemCompleted { get; init; }

    /// <summary>Gets a callback receiving the full path of every file written.</summary>
    public Action<string>? FileWritten { get; init; }

    /// <summary>Gets a callback receiving the full path of every existing file that was kept instead of written.</summary>
    public Action<string>? FileSkipped { get; init; }

    /// <summary>Gets a callback receiving warnings about where files were placed and what was not exported.</summary>
    public Action<string>? Warning { get; init; }

    /// <summary>
    /// Gets a callback receiving the name of a file and the exception it failed to export with. Such a failure skips
    /// that file and the export carries on.
    /// </summary>
    public Action<string, Exception>? FileFailed { get; init; }

    /// <summary>Gets the number of files written so far.</summary>
    public int WrittenFileCount => FilesWritten;

    /// <summary>Gets the number of files that failed to export so far.</summary>
    public int FailedFileCount => FilesFailed;

    /// <summary>Gets whether the export was cancelled because of an existing file, see <see cref="ExistingFileAction.Cancel"/>.</summary>
    internal bool IsCancelled => Cancelled;

    /// <summary>
    /// Whether a file can be exported to glTF, see <see cref="ContentExportOutput.Gltf"/>: a resource of a type
    /// <see cref="GltfModelExporter.CanExport(ResourceType)"/> accepts, or a navigation mesh.
    /// </summary>
    /// <param name="contentPath">Content path of the file, a compiled resource with or without its <c>_c</c> suffix.</param>
    public static bool CanExportGltf(string contentPath)
    {
        ArgumentNullException.ThrowIfNull(contentPath);

        var extension = Path.GetExtension(contentPath.AsSpan());

        return extension.Equals(".nav", StringComparison.OrdinalIgnoreCase)
            || GltfModelExporter.CanExport(ResourceTypeExtensions.DetermineByFileExtension(extension));
    }

    /// <summary>
    /// Gets the extension, without its dot, of the file a file of the given name is decompiled to: that of the source
    /// format of a compiled resource, <c>vfx</c> for a compiled shader, or else its own.
    /// </summary>
    /// <param name="contentPath">Content path of the file.</param>
    public static string GetDecompiledExtension(string contentPath)
    {
        ArgumentNullException.ThrowIfNull(contentPath);

        var extension = Path.GetExtension(contentPath);

        if (extension.Equals(".vcs", StringComparison.OrdinalIgnoreCase))
        {
            return "vfx";
        }

        var resourceType = ResourceTypeExtensions.DetermineByFileExtension(extension);

        return resourceType != ResourceType.Unknown
            ? FileExtract.GetExtension(resourceType)
            : ContentPaths.TrimCompiledSuffix(extension.TrimStart('.'));
    }

    /// <summary>
    /// Gets the order to export resources of the given file name in, lowest first, so a resource exported before the
    /// ones it consumes marks them as handled, see <see cref="TryBeginExport"/>. Resources the compiler generated from
    /// another one, named <c>&lt;parent&gt;+&lt;name&gt;</c>, come last.
    /// </summary>
    internal static int GetExportOrder(string fileName)
    {
        if (GetGeneratingResource(fileName) != null)
        {
            return ExportOrder.Count + 1;
        }

        var type = ResourceTypeExtensions.DetermineByFileExtension(Path.GetExtension(fileName.AsSpan()));
        return ExportOrder.GetValueOrDefault(type, ExportOrder.Count);
    }

    /// <summary>
    /// Gets the full path a content path is written at.
    /// </summary>
    public string GetOutputPath(string contentPath) => Path.Join(OutputRoot, NormalizeContentPath(contentPath));

    /// <summary>
    /// Whether a resource was exported, or consumed by an exported resource, during this export. A resource the
    /// compiler generated from another one counts as handled once that one is.
    /// </summary>
    /// <param name="contentPath">Content path of the resource, compiled or not.</param>
    internal bool IsHandled(string contentPath)
    {
        var key = GetHandledKey(contentPath);
        var generatingResource = GetGeneratingResource(key);

        lock (StateLock)
        {
            return HandledFiles.Contains(key) || (generatingResource != null && HandledFiles.Contains(generatingResource));
        }
    }

    /// <summary>
    /// Claims a resource for export, so that it is exported once. A resource that was already exported from another
    /// origin, such as the same path in another package, is exported again.
    /// </summary>
    /// <param name="contentPath">Content path of the resource, compiled or not.</param>
    /// <param name="origin">Where the resource is read from, such as the path of its package or of the file.</param>
    /// <returns>
    /// False when the resource was already exported from the same origin, or was consumed by an exported resource, see
    /// <see cref="IsHandled"/>.
    /// </returns>
    internal bool TryBeginExport(string contentPath, string origin)
    {
        var key = GetHandledKey(contentPath);
        var generatingResource = GetGeneratingResource(key);

        lock (StateLock)
        {
            if (generatingResource != null && HandledFiles.Contains(generatingResource))
            {
                return false;
            }

            if (HandledFiles.Add(key))
            {
                ExportOrigins[key] = origin;
                return true;
            }

            return ExportOrigins.TryGetValue(key, out var firstOrigin) && firstOrigin != origin;
        }
    }

    /// <summary>
    /// Exports files on the calling thread and up to <see cref="MaxDegreeOfParallelism"/> minus one others, and returns
    /// once all of them are done. A file that fails is reported through <see cref="FileFailed"/> and the others carry on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Files are exported by resource type, maps and models before the resources they consume and materials before
    /// textures, then by content path, as threads become free. A resource waits for every resource in the export that
    /// references it and comes before it in that order, so a resource that consumes another, such as a model its
    /// meshes, has marked it as handled by the time it comes up.
    /// Resources the compiler generated from another one wait for it, and files in a map's folder wait for the map.
    /// </para>
    /// <para>
    /// With <see cref="IncludeDependencies"/>, every resource an item references, directly or through other references,
    /// that is in the item's <see cref="ContentExportItem.Source"/> is exported as an item of its own. Resources that
    /// only exist as part of another, such as meshes, are not, but their references are followed. A dependency of a glTF
    /// item that can be exported to glTF is exported to glTF too.
    /// </para>
    /// <para>
    /// The files of a compiled shader in a package or a folder are decompiled together once, from the file of the highest
    /// shader model. The other items of that shader are skipped as handled.
    /// </para>
    /// <para>
    /// A resource that a glTF export includes, such as a mesh of a model, is skipped as handled unless it is an item
    /// whose output is not glTF.
    /// </para>
    /// </remarks>
    /// <param name="items">The files to export.</param>
    /// <param name="cancellationToken">Stops the export, every thread finishes the file it is on.</param>
    /// <exception cref="ArgumentException">More than one file would be written to the same <see cref="ContentExportItem.TargetFile"/>.</exception>
    /// <exception cref="OperationCanceledException">
    /// The export was cancelled, through the token or <see cref="ExistingFileAction.Cancel"/>.
    /// </exception>
    public void Export(IEnumerable<ContentExportItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        new ContentExportQueue(this, items).Run(cancellationToken);
    }

    /// <summary>
    /// Decompiles a resource and writes its files. With <see cref="IncludeDependencies"/>, its dependencies are then
    /// exported in parallel.
    /// </summary>
    /// <param name="resource">The resource, its <see cref="Resource.FileName"/> must be its content path.</param>
    /// <param name="fileLoader">The loader the resource's dependencies are read through while decompiling it.</param>
    /// <param name="source">Where the resource came from, needed for <see cref="IncludeDependencies"/>.</param>
    /// <param name="targetFile">A full path to write the main file to instead of its content path.</param>
    /// <param name="cancellationToken">Stops the export between files.</param>
    /// <returns>Whether every file of the resource was written.</returns>
    public bool Export(Resource resource, IFileLoader fileLoader, ContentExportSource? source = null, string? targetFile = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(fileLoader);
        ArgumentException.ThrowIfNullOrEmpty(resource.FileName);

        MarkHandled(resource.FileName);

        if (targetFile != null && Path.GetExtension(targetFile) is var targetExtension
            && FileExtract.GetExtension(resource) is var extension
            && !targetExtension.Equals($".{extension}", StringComparison.OrdinalIgnoreCase))
        {
            Warning?.Invoke($"Extension '.{extension}' might be more suitable than the one provided '{targetExtension}'");
        }

        bool written;

        using (var contentFile = FileExtract.Extract(resource, fileLoader, Progress, ExtractOptions))
        {
            written = Export(contentFile, targetFile, cancellationToken);
        }

        if (IncludeDependencies && source != null)
        {
            var queue = new ContentExportQueue(this, []);
            queue.AddDependenciesOf(resource, fileLoader, source);
            queue.Run(cancellationToken);
        }

        return written;
    }

    /// <summary>
    /// Writes an extracted content file with its sub files and additional files.
    /// </summary>
    /// <param name="contentFile">The content file.</param>
    /// <param name="targetFile">
    /// A full path to write the main file to instead of its content path. A content file without data that has one sub
    /// file writes that sub file there, see <see cref="ContentFile.MainSubFile"/>.
    /// </param>
    /// <param name="cancellationToken">Stops the export between files.</param>
    /// <returns>Whether every file of it was written.</returns>
    public bool Export(ContentFile contentFile, string? targetFile = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contentFile);

        if (ContentFileHandler != null)
        {
            ContentFileHandler(contentFile);
            return true;
        }

        var files = new List<PlannedFile>();
        PlanContentFile(contentFile, targetFile, new References(), files);

        if (ConfirmExistingFiles != null || ExistingFileAction == ExistingFileAction.Cancel)
        {
            ConfirmExistingFilesOf(files);
        }

        var allWritten = true;

        foreach (var file in files)
        {
            ThrowIfCancelled(cancellationToken);

            if (file.SubFile != null)
            {
                allWritten &= WriteSubFile(file.SubFile, file.OutputPath, file.ContentPath!, file.References);
            }
            else
            {
                var data = ApplyRenames(file.ContentFile!.Data!, file.ContentPath ?? file.OutputPath, file.OwnReferences!);
                Write(file.OutputPath, file.ContentPath, data, file.References);
            }
        }

        return allWritten;
    }

    /// <summary>
    /// Writes a file as it is.
    /// </summary>
    /// <param name="contentPath">Content path of the file.</param>
    /// <param name="data">The file contents.</param>
    /// <param name="targetFile">A full path to write the file to instead of its content path.</param>
    public void WriteFile(string contentPath, ReadOnlySpan<byte> data, string? targetFile = null)
    {
        ArgumentNullException.ThrowIfNull(contentPath);

        if (ContentFileHandler != null)
        {
            using var contentFile = new ContentFile
            {
                FileName = contentPath,
                Data = data.ToArray(),
            };

            ContentFileHandler(contentFile);
            return;
        }

        var normalizedPath = NormalizeContentPath(contentPath);
        Write(targetFile ?? Path.Join(OutputRoot, normalizedPath), normalizedPath, data, new References());
    }

    /// <summary>
    /// Claims the path of a file whose contents depend on its own name, such as a glTF file that names its buffers after
    /// itself, before the file is created. A path that another file of this export was already written or reserved at is
    /// replaced by a unique name next to it. A file that already exists is handled following
    /// <see cref="ExistingFileAction"/> and <see cref="ConfirmExistingFiles"/>. The folder is created.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    /// <returns>
    /// The full path to write the file to with <see cref="WriteReservedFile"/>, or null when the file that already exists
    /// there is kept.
    /// </returns>
    /// <exception cref="OperationCanceledException">The existing file action is <see cref="ExistingFileAction.Cancel"/>.</exception>
    internal string? ReservePath(string path)
    {
        ThrowIfCancelled(CancellationToken.None);

        var outputPath = Path.GetFullPath(path);
        var reservedPath = outputPath;

        lock (StateLock)
        {
            for (var suffix = 1; !ClaimedPaths.TryAdd(reservedPath, default); suffix++)
            {
                reservedPath = GetSuffixedPath(outputPath, suffix);
            }

            ReservedPaths.Add(reservedPath);
        }

        if (reservedPath != outputPath)
        {
            Warning?.Invoke($"\"{ContentPaths.GetRelativePath(OutputRoot, outputPath)}\" is already written by another file of this export, it is written as \"{ContentPaths.GetRelativePath(OutputRoot, reservedPath)}\".");
        }

        var state = PathStates.GetOrAdd(reservedPath, static _ => new PathState());

        lock (state.Lock)
        {
            if (KeepsExisting(reservedPath, state))
            {
                lock (StateLock)
                {
                    ReservedPaths.Remove(reservedPath);
                }

                return null;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(reservedPath)!);

        return reservedPath;
    }

    /// <summary>
    /// Writes a file at a path from <see cref="ReservePath"/>, or a file next to it that is named after it, such as a
    /// buffer of a glTF file. Such a file is part of the reserved one, so it is written whether or not it already exists.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    /// <param name="data">The file contents.</param>
    /// <exception cref="IOException">Another file of this export was already written at the path.</exception>
    internal void WriteReservedFile(string path, ReadOnlySpan<byte> data)
    {
        ThrowIfCancelled(CancellationToken.None);

        path = Path.GetFullPath(path);
        var claim = (ImagePixelCount: 0L, Hash: XxHash128.HashToUInt128(data));

        lock (StateLock)
        {
            if (!ReservedPaths.Remove(path) && ClaimedPaths.TryGetValue(path, out var claimed))
            {
                if (claimed.Hash == claim.Hash)
                {
                    return;
                }

                throw new IOException($"\"{path}\" was already written by another file of this export.");
            }

            ClaimedPaths[path] = claim;
        }

        WriteClaimedPath(path, claim.Hash, data, checkExisting: false);
    }

    /// <summary>
    /// Lists the files of a content file and the files below it in the order they are written: its sub files, its
    /// additional files, then its own data, which may be pointed at the files below it that were renamed.
    /// </summary>
    private void PlanContentFile(ContentFile contentFile, string? targetFile, References parentReferences, List<PlannedFile> files)
    {
        string? contentPath = null;

        if (contentFile.FileName.Length > 0)
        {
            contentPath = NormalizeContentPath(contentFile.FileName);
            MarkHandled(contentPath);
        }

        var references = contentFile.Data != null ? new References() : parentReferences;

        if (targetFile != null && contentFile.MainSubFile is { } mainSubFile)
        {
            files.Add(new PlannedFile(targetFile, NormalizeContentPath(mainSubFile.FileName), references, mainSubFile));
        }
        else
        {
            foreach (var subFile in contentFile.SubFiles)
            {
                var subFilePath = NormalizeContentPath(subFile.FileName);
                files.Add(new PlannedFile(Path.Join(OutputRoot, subFilePath), subFilePath, references, subFile));
            }
        }

        foreach (var additionalFile in contentFile.AdditionalFiles)
        {
            PlanContentFile(additionalFile, null, references, files);
        }

        if (contentFile.Data == null)
        {
            return;
        }

        var outputPath = targetFile ?? (contentPath != null ? Path.Join(OutputRoot, contentPath) : null);

        if (outputPath == null)
        {
            Warning?.Invoke("Skipped an extracted file that has no file name.");
            return;
        }

        files.Add(new PlannedFile(outputPath, contentPath, parentReferences, ContentFile: contentFile, OwnReferences: references));
    }

    /// <returns>Whether the sub file was written or had nothing to write.</returns>
    private bool WriteSubFile(SubFile subFile, string outputPath, string contentPath, References references)
    {
        byte[]? data;

        try
        {
            data = subFile.Extract?.Invoke();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            ReportFailure(contentPath, e);
            return false;
        }

        if (data is { Length: > 0 })
        {
            Write(outputPath, contentPath, data, references, subFile.ImagePixelCount);
        }

        return true;
    }

    private void Write(string outputPath, string? contentPath, ReadOnlySpan<byte> data, References references, long imagePixelCount = 0)
    {
        ThrowIfCancelled(CancellationToken.None);

        outputPath = Path.GetFullPath(outputPath);

        var claim = (ImagePixelCount: imagePixelCount, Hash: XxHash128.HashToUInt128(data));
        string claimedPath;

        lock (StateLock)
        {
            claimedPath = Claim(outputPath, claim);
        }

        if (claimedPath == outputPath)
        {
            if (contentPath != null)
            {
                references.Kept.Add(contentPath);
            }
        }
        else
        {
            var newContentPath = ContentPaths.GetRelativePath(OutputRoot, claimedPath);
            Warning?.Invoke($"\"{contentPath ?? outputPath}\" differs from a file already written to that path, it is written as \"{newContentPath}\".");

            if (contentPath != null)
            {
                references.Renamed.Add((contentPath, newContentPath));
            }
        }

        WriteClaimedPath(claimedPath, claim.Hash, data, checkExisting: true);
    }

    /// <summary>
    /// Writes a file at a path it claimed, unless the same content was already written there or different content
    /// claimed the path since.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    /// <param name="hash">The hash of the file contents.</param>
    /// <param name="data">The file contents.</param>
    /// <param name="checkExisting">Whether a file that existed before the export is kept, see <see cref="ExistingFileAction"/>.</param>
    private void WriteClaimedPath(string path, UInt128 hash, ReadOnlySpan<byte> data, bool checkExisting)
    {
        var state = PathStates.GetOrAdd(path, static _ => new PathState());
        bool isFirstWrite;

        lock (state.Lock)
        {
            if (state.WrittenHash == hash)
            {
                return;
            }

            lock (StateLock)
            {
                if (ClaimedPaths[path].Hash != hash)
                {
                    return;
                }
            }

            if (checkExisting && KeepsExisting(path, state))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);

            isFirstWrite = state.WrittenHash == null;
            state.WrittenHash = hash;
        }

        if (isFirstWrite)
        {
            Interlocked.Increment(ref FilesWritten);
            FileWritten?.Invoke(path);
        }
    }

    /// <summary>
    /// Whether the file that existed at a path before the export is kept, decided once per path. The path's state must
    /// be locked.
    /// </summary>
    private bool KeepsExisting(string path, PathState state)
    {
        if (state.KeepExisting == null)
        {
            state.KeepExisting = GetExistingFileAction(path) == ExistingFileAction.Skip;

            if (state.KeepExisting.Value)
            {
                FileSkipped?.Invoke(path);
            }
        }

        return state.KeepExisting.Value;
    }

    /// <summary>
    /// Gets the path a file is written at: the path it asks for, unless different content claimed that first. Images
    /// keep their path, the one with the most pixels takes the claim over.
    /// </summary>
    private string Claim(string outputPath, (long ImagePixelCount, UInt128 Hash) claim)
    {
        var path = outputPath;

        for (var suffix = 1; ; suffix++)
        {
            if (!ClaimedPaths.TryGetValue(path, out var claimed))
            {
                ClaimedPaths.Add(path, claim);
                return path;
            }

            if (claimed.Hash == claim.Hash)
            {
                return path;
            }

            if (claim.ImagePixelCount > 0 && claimed.ImagePixelCount > 0)
            {
                if (claim.CompareTo(claimed) > 0)
                {
                    ClaimedPaths[path] = claim;
                }

                return path;
            }

            path = GetSuffixedPath(outputPath, suffix);
        }
    }

    private static string GetSuffixedPath(string path, int suffix)
        => ContentPaths.AppendToFileName(path, string.Create(CultureInfo.InvariantCulture, $"_{suffix}"));

    private void ConfirmExistingFilesOf(List<PlannedFile> files)
    {
        foreach (var file in files)
        {
            var fullPath = Path.GetFullPath(file.OutputPath);

            lock (StateLock)
            {
                if (ClaimedPaths.ContainsKey(fullPath))
                {
                    continue;
                }
            }

            if (File.Exists(fullPath))
            {
                GetExistingFileAction(fullPath);
                return;
            }
        }
    }

    private ExistingFileAction GetExistingFileAction(string path)
    {
        if (!File.Exists(path))
        {
            return ExistingFileAction.Overwrite;
        }

        var action = ExistingFileAction;

        if (ConfirmExistingFiles != null)
        {
            lock (ExistingFilesLock)
            {
                ConfirmedExistingFileAction ??= ConfirmExistingFiles(path);
                action = ConfirmedExistingFileAction.Value;
            }
        }

        if (action == ExistingFileAction.Cancel)
        {
            Cancelled = true;
            throw new OperationCanceledException($"Cancelled because \"{path}\" already exists.");
        }

        return action;
    }

    internal void ThrowIfCancelled(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Cancelled)
        {
            throw new OperationCanceledException(CancelledMessage);
        }
    }

    internal void ReportFailure(string fileName, Exception exception)
    {
        Interlocked.Increment(ref FilesFailed);
        FileFailed?.Invoke(fileName, exception);
    }

    private byte[] ApplyRenames(byte[] data, string path, References references)
    {
        if (references.Renamed.Count == 0)
        {
            return data;
        }

        var text = data.AsSpan().Contains((byte)0) ? null : Encoding.UTF8.GetString(data);

        foreach (var (from, to) in references.Renamed)
        {
            if (references.Kept.Contains(from) || references.Renamed.Count(rename => rename.From.Equals(from, StringComparison.OrdinalIgnoreCase)) > 1)
            {
                Warning?.Invoke($"\"{path}\" references different files at \"{from}\", the references are left pointing at \"{from}\" and not at \"{to}\".");
            }
            else if (text != null && text.Contains($"\"{from}\"", StringComparison.Ordinal))
            {
                text = text.Replace($"\"{from}\"", $"\"{to}\"", StringComparison.Ordinal);
            }
            else
            {
                Warning?.Invoke($"Could not point the reference to \"{from}\" in \"{path}\" at \"{to}\".");
            }
        }

        return text != null ? Encoding.UTF8.GetBytes(text) : data;
    }

    /// <summary>
    /// Marks a resource as handled, see <see cref="IsHandled"/>.
    /// </summary>
    /// <param name="contentPath">Content path of the resource, compiled or not.</param>
    internal void MarkHandled(string contentPath)
    {
        var key = GetHandledKey(contentPath);

        lock (StateLock)
        {
            HandledFiles.Add(key);
        }
    }

    /// <summary>
    /// Marks a resource a glTF export includes as handled, unless it is an item of an export whose output is not glTF.
    /// </summary>
    /// <param name="contentPath">Content path of the resource, compiled or not.</param>
    internal void MarkIncludedInGltf(string contentPath)
    {
        var key = GetHandledKey(contentPath);

        lock (StateLock)
        {
            if (!NonGltfItems.Contains(key))
            {
                HandledFiles.Add(key);
            }
        }
    }

    /// <summary>
    /// Records the items of an export whose output is not glTF, so a glTF export that includes them leaves them to be
    /// exported, see <see cref="MarkIncludedInGltf"/>.
    /// </summary>
    internal void AddNonGltfItems(IEnumerable<string> contentPaths)
    {
        var keys = contentPaths.Select(GetHandledKey).ToList();

        lock (StateLock)
        {
            NonGltfItems.UnionWith(keys);
        }
    }

    /// <summary>
    /// Picks the files compiled shaders are decompiled from: of the files of each shader and platform, the first one of
    /// the highest shader model. A file whose name does not follow the shader naming is picked on its own.
    /// </summary>
    /// <returns>Each picked file with the other files of its shader.</returns>
    internal static List<(T Selected, List<T> Others)> SelectShaderFiles<T>(IEnumerable<T> files, Func<T, string> getContentPath)
    {
        var selected = new List<(T Selected, List<T> Others)>();
        var highestByShader = new Dictionary<(string ShaderName, VcsPlatformType Platform), (int Index, VcsShaderModelType ShaderModel)>();

        foreach (var file in files)
        {
            string shaderName;
            VcsPlatformType platform;
            VcsShaderModelType shaderModel;

            try
            {
                (shaderName, _, platform, shaderModel) = ShaderUtilHelpers.ComputeVCSFileName(getContentPath(file));
            }
            catch (ShaderParserException)
            {
                selected.Add((file, []));
                continue;
            }

            if (!highestByShader.TryGetValue((shaderName, platform), out var highest))
            {
                highestByShader.Add((shaderName, platform), (selected.Count, shaderModel));
                selected.Add((file, []));
            }
            else if (shaderModel > highest.ShaderModel)
            {
                var (previous, others) = selected[highest.Index];
                others.Add(previous);
                highestByShader[(shaderName, platform)] = (highest.Index, shaderModel);
                selected[highest.Index] = (file, others);
            }
            else
            {
                selected[highest.Index].Others.Add(file);
            }
        }

        return selected;
    }

    /// <summary>
    /// Gets the glTF exporter for resources whose dependencies are read through the given loader, see
    /// <see cref="GltfExporterFactory"/>.
    /// </summary>
    internal GltfModelExporter GetGltfExporter(IFileLoader fileLoader)
    {
        return GltfExporters.GetOrAdd(fileLoader, loader =>
        {
            var gltfExporter = GltfExporterFactory?.Invoke(loader) ?? new GltfModelExporter(loader)
            {
                ProgressReporter = Progress,
            };

            gltfExporter.Exporter = this;
            return gltfExporter;
        });
    }

    /// <summary>
    /// Gets the loader an item's references are read through, see <see cref="ContentExportItem.FileLoader"/>.
    /// </summary>
    internal IFileLoader GetFileLoader(ContentExportItem item) => item.FileLoader ?? NullFileLoader;

    /// <summary>
    /// Gets the resource the compiler generated a resource named <c>&lt;parent&gt;+&lt;name&gt;</c> from, such as the
    /// non-additive version of an additive clip or a graph variation.
    /// </summary>
    internal static string? GetGeneratingResource(string contentPath)
    {
        var separator = contentPath.IndexOf('+', StringComparison.Ordinal);

        return separator > 0 && Path.HasExtension(contentPath.AsSpan(0, separator)) ? contentPath[..separator] : null;
    }

    internal static string GetHandledKey(string contentPath)
        => ContentPaths.TrimCompiledSuffix(ContentPaths.Normalize(contentPath));

    private string NormalizeContentPath(string path)
    {
        var contentPath = ContentPaths.Normalize(path, out var escapedRoot);

        if (contentPath.Length == 0)
        {
            contentPath = "unnamed";
        }

        if (escapedRoot)
        {
            Warning?.Invoke($"\"{path}\" is outside of the output folder, it is written to \"{contentPath}\" instead.");
        }

        return contentPath;
    }
}
