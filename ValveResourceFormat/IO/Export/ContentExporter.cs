using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Threading;

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
/// <see cref="ContentExportQueue"/> exports many files through one instance in parallel. File contents do not depend on
/// the order files are exported in, but when two different files claim the same path, which one keeps it does.
/// </para>
/// </remarks>
public sealed class ContentExporter
{
    private sealed class References
    {
        public References()
        {
            Root = this;
        }

        public References(References parent)
        {
            Root = parent.Root;
        }

        /// <summary>Gets the references of the outermost file of the export, which records whether any file of it failed.</summary>
        public References Root { get; }
        public bool AnyFailed { get; set; }
        public List<(string From, string To)> Renamed { get; } = [];
        public HashSet<string> Kept { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class PathState
    {
        public Lock Lock { get; } = new();
        public UInt128? WrittenHash { get; set; }
        public bool? KeepExisting { get; set; }
    }

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
    private readonly Dictionary<string, string> ExportOrigins = new(StringComparer.OrdinalIgnoreCase);
    private ExistingFileAction? ConfirmedExistingFileAction;
    private volatile bool Cancelled;
    private int FilesWritten;
    private int FilesFailed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentExporter"/> class.
    /// </summary>
    /// <param name="outputRoot">
    /// The folder content paths are relative to, usually from <see cref="ContentPaths.ResolveOutputRoot"/>.
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
    /// such as meshes, are not exported on their own but their references are followed.
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

    /// <summary>Gets a callback receiving the full path of every file written.</summary>
    public Action<string>? FileWritten { get; init; }

    /// <summary>Gets a callback receiving the full path of every existing file that was kept instead of written.</summary>
    public Action<string>? FileSkipped { get; init; }

    /// <summary>Gets a callback receiving warnings about where files were placed.</summary>
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
    /// Gets the order to export resources of the given file name in, lowest first, so a resource exported before the
    /// ones it consumes marks them as handled, see <see cref="TryBeginExport"/>. Resources the compiler generated from
    /// another one, named <c>&lt;parent&gt;+&lt;name&gt;</c>, come last.
    /// </summary>
    public static int GetExportOrder(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

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
    /// compiler generated from another one counts as handled once that one is. Front ends skip such resources when they
    /// come up again.
    /// </summary>
    /// <param name="contentPath">Content path of the resource, compiled or not.</param>
    public bool IsHandled(string contentPath)
    {
        ArgumentNullException.ThrowIfNull(contentPath);

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
    public bool TryBeginExport(string contentPath, string origin)
    {
        ArgumentNullException.ThrowIfNull(contentPath);
        ArgumentNullException.ThrowIfNull(origin);

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
    /// Decompiles a resource and writes its files. With <see cref="IncludeDependencies"/>, its dependencies are then
    /// exported in parallel through a <see cref="ContentExportQueue"/>.
    /// </summary>
    /// <param name="resource">The resource, its <see cref="Resource.FileName"/> must be its content path.</param>
    /// <param name="fileLoader">The loader the resource's dependencies are read through while decompiling it.</param>
    /// <param name="source">Where the resource came from, needed for <see cref="IncludeDependencies"/>.</param>
    /// <param name="targetFile">A full path to write the main file to instead of its content path.</param>
    /// <param name="cancellationToken">Stops the export between files.</param>
    public void Export(Resource resource, IFileLoader fileLoader, ContentExportSource? source = null, string? targetFile = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(fileLoader);
        ArgumentException.ThrowIfNullOrEmpty(resource.FileName);

        MarkHandled(resource.FileName);
        ExportResource(resource, fileLoader, targetFile, cancellationToken);

        if (IncludeDependencies && source != null)
        {
            var queue = new ContentExportQueue(this);
            queue.AddDependenciesOf(resource, fileLoader, source);
            queue.Run(cancellationToken);
        }
    }

    /// <summary>
    /// Decompiles a resource and writes its files, without its dependencies.
    /// </summary>
    /// <returns>Whether every file of it was written.</returns>
    internal bool ExportResource(Resource resource, IFileLoader fileLoader, string? targetFile, CancellationToken cancellationToken)
    {
        using var contentFile = FileExtract.Extract(resource, fileLoader, Progress, ExtractOptions);
        return ExportFiles(contentFile, targetFile, cancellationToken);
    }

    /// <summary>
    /// Writes an extracted content file with its sub files and additional files.
    /// </summary>
    /// <param name="contentFile">The content file.</param>
    /// <param name="targetFile">
    /// A full path to write the main file to instead of its content path. A content file without data that has one sub
    /// file writes that sub file there.
    /// </param>
    /// <param name="cancellationToken">Stops the export between files.</param>
    public void Export(ContentFile contentFile, string? targetFile = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contentFile);

        ExportFiles(contentFile, targetFile, cancellationToken);
    }

    /// <summary>
    /// Writes an extracted content file with its sub files and additional files.
    /// </summary>
    /// <returns>Whether every file of it was written.</returns>
    internal bool ExportFiles(ContentFile contentFile, string? targetFile, CancellationToken cancellationToken)
    {
        if (ConfirmExistingFiles != null || ExistingFileAction == ExistingFileAction.Cancel)
        {
            ConfirmExistingFilesOf(contentFile, targetFile);
        }

        var references = new References();
        ExportContentFile(contentFile, targetFile, references, cancellationToken);

        return !references.AnyFailed;
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

        var normalizedPath = NormalizeContentPath(contentPath);
        Write(targetFile ?? Path.Join(OutputRoot, normalizedPath), normalizedPath, data, new References());
    }

    /// <summary>
    /// Claims the path of a file whose contents depend on its own name, such as a glTF file that names its buffers after
    /// itself, before the file is created. A path that another file of this export was already written or reserved at is
    /// replaced by a unique name next to it. A file that already exists is handled following
    /// <see cref="ExistingFileAction"/> and <see cref="ConfirmExistingFiles"/>. The folder is created.
    /// </summary>
    /// <param name="contentPath">Content path of the file.</param>
    /// <param name="targetFile">A full path to write the file to instead of its content path.</param>
    /// <returns>
    /// The full path to write the file to with <see cref="WriteReservedFile"/>, or null when the file that already exists
    /// there is kept.
    /// </returns>
    /// <exception cref="OperationCanceledException">The existing file action is <see cref="ExistingFileAction.Cancel"/>.</exception>
    public string? ReservePath(string contentPath, string? targetFile = null)
    {
        ArgumentNullException.ThrowIfNull(contentPath);

        ThrowIfCancelled(CancellationToken.None);

        var normalizedPath = NormalizeContentPath(contentPath);
        var outputPath = Path.GetFullPath(targetFile ?? Path.Join(OutputRoot, normalizedPath));
        var directory = Path.GetDirectoryName(outputPath)!;
        var name = Path.GetFileNameWithoutExtension(outputPath);
        var extension = Path.GetExtension(outputPath);
        var path = outputPath;

        lock (StateLock)
        {
            for (var suffix = 1; !ClaimedPaths.TryAdd(path, default); suffix++)
            {
                path = Path.Join(directory, string.Create(CultureInfo.InvariantCulture, $"{name}_{suffix}{extension}"));
            }

            ReservedPaths.Add(path);
        }

        if (path != outputPath)
        {
            Warning?.Invoke($"\"{normalizedPath}\" is already written by another file of this export, it is written as \"{ContentPaths.GetRelativePath(OutputRoot, path)}\".");
        }

        var state = PathStates.GetOrAdd(path, static _ => new PathState());

        lock (state.Lock)
        {
            state.KeepExisting ??= GetExistingFileAction(path) == ExistingFileAction.Skip;

            if (state.KeepExisting.Value)
            {
                lock (StateLock)
                {
                    ReservedPaths.Remove(path);
                }

                FileSkipped?.Invoke(path);
                return null;
            }
        }

        Directory.CreateDirectory(directory);

        return path;
    }

    /// <summary>
    /// Writes a file at a path from <see cref="ReservePath"/>, or a file next to it that is named after it, such as a
    /// buffer of a glTF file. Such a file is part of the reserved one, so it is written whether or not it already exists.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    /// <param name="data">The file contents.</param>
    /// <exception cref="IOException">Another file of this export was already written at the path.</exception>
    public void WriteReservedFile(string path, ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(path);

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

        var state = PathStates.GetOrAdd(path, static _ => new PathState());
        bool isFirstWrite;

        lock (state.Lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);

            isFirstWrite = state.WrittenHash == null;
            state.WrittenHash = claim.Hash;
        }

        if (isFirstWrite)
        {
            Interlocked.Increment(ref FilesWritten);
            FileWritten?.Invoke(path);
        }
    }

    private void ExportContentFile(ContentFile contentFile, string? targetFile, References parentReferences, CancellationToken cancellationToken)
    {
        ThrowIfCancelled(cancellationToken);

        string? contentPath = null;

        if (contentFile.FileName.Length > 0)
        {
            contentPath = NormalizeContentPath(contentFile.FileName);
            MarkHandled(contentPath);
        }

        var references = contentFile.Data != null ? new References(parentReferences) : parentReferences;

        if (targetFile != null && contentFile.Data == null && contentFile.SubFiles.Count == 1)
        {
            var subFile = contentFile.SubFiles[0];
            WriteSubFile(subFile, targetFile, NormalizeContentPath(subFile.FileName), references);
        }
        else
        {
            foreach (var subFile in contentFile.SubFiles)
            {
                ThrowIfCancelled(cancellationToken);

                var subFilePath = NormalizeContentPath(subFile.FileName);
                WriteSubFile(subFile, Path.Join(OutputRoot, subFilePath), subFilePath, references);
            }
        }

        foreach (var additionalFile in contentFile.AdditionalFiles)
        {
            ExportContentFile(additionalFile, null, references, cancellationToken);
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

        Write(outputPath, contentPath, ApplyRenames(contentFile.Data, contentPath ?? outputPath, references), parentReferences);
    }

    private void WriteSubFile(SubFile subFile, string outputPath, string contentPath, References references)
    {
        byte[]? data;

        try
        {
            data = subFile.Extract?.Invoke();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            references.Root.AnyFailed = true;
            ReportFailure(contentPath, e);
            return;
        }

        if (data is not { Length: > 0 })
        {
            return;
        }

        Write(outputPath, contentPath, data, references, subFile.ImagePixelCount);
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

        var state = PathStates.GetOrAdd(claimedPath, static _ => new PathState());
        bool isFirstWrite;

        lock (state.Lock)
        {
            if (state.WrittenHash == claim.Hash)
            {
                return;
            }

            lock (StateLock)
            {
                if (ClaimedPaths[claimedPath].Hash != claim.Hash)
                {
                    return;
                }
            }

            if (state.KeepExisting == null)
            {
                state.KeepExisting = GetExistingFileAction(claimedPath) == ExistingFileAction.Skip;

                if (state.KeepExisting.Value)
                {
                    FileSkipped?.Invoke(claimedPath);
                }
            }

            if (state.KeepExisting.Value)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(claimedPath)!);
            File.WriteAllBytes(claimedPath, data);

            isFirstWrite = state.WrittenHash == null;
            state.WrittenHash = claim.Hash;
        }

        if (isFirstWrite)
        {
            Interlocked.Increment(ref FilesWritten);
            FileWritten?.Invoke(claimedPath);
        }
    }

    /// <summary>
    /// Gets the path a file is written at: the path it asks for, unless different content claimed that first. Images
    /// keep their path, the one with the most pixels takes the claim over.
    /// </summary>
    private string Claim(string outputPath, (long ImagePixelCount, UInt128 Hash) claim)
    {
        var directory = Path.GetDirectoryName(outputPath)!;
        var name = Path.GetFileNameWithoutExtension(outputPath);
        var extension = Path.GetExtension(outputPath);
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

            path = Path.Join(directory, string.Create(CultureInfo.InvariantCulture, $"{name}_{suffix}{extension}"));
        }
    }

    private void ConfirmExistingFilesOf(ContentFile contentFile, string? targetFile)
    {
        foreach (var path in GetOutputPaths(contentFile, targetFile))
        {
            var fullPath = Path.GetFullPath(path);

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

    private IEnumerable<string> GetOutputPaths(ContentFile contentFile, string? targetFile)
    {
        if (targetFile != null && contentFile.Data == null && contentFile.SubFiles.Count == 1)
        {
            yield return targetFile;
        }
        else
        {
            foreach (var subFile in contentFile.SubFiles)
            {
                yield return Path.Join(OutputRoot, ContentPaths.Normalize(subFile.FileName));
            }
        }

        foreach (var additionalFile in contentFile.AdditionalFiles)
        {
            foreach (var path in GetOutputPaths(additionalFile, null))
            {
                yield return path;
            }
        }

        if (contentFile.Data == null)
        {
            yield break;
        }

        if (targetFile != null)
        {
            yield return targetFile;
        }
        else if (contentFile.FileName.Length > 0)
        {
            yield return Path.Join(OutputRoot, ContentPaths.Normalize(contentFile.FileName));
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

    private void ThrowIfCancelled(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Cancelled)
        {
            throw new OperationCanceledException("The export was cancelled because a file already exists.");
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
    /// Marks a resource as handled, see <see cref="IsHandled"/>, for exports that consume resources outside of this
    /// exporter, such as a glTF export of a model that includes its meshes.
    /// </summary>
    /// <param name="contentPath">Content path of the resource, compiled or not.</param>
    public void MarkHandled(string contentPath)
    {
        ArgumentNullException.ThrowIfNull(contentPath);

        var key = GetHandledKey(contentPath);

        lock (StateLock)
        {
            HandledFiles.Add(key);
        }
    }

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
