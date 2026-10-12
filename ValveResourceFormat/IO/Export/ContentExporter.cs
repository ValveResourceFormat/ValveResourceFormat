using System.Collections.Frozen;
using System.IO;
using System.Linq;
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
/// One instance covers one export run and may be used from several threads. A path is written once per run: the first
/// file written at a path keeps it, and every later file at that path is skipped.
/// </para>
/// <para>
/// Whether files that existed before the export are replaced is decided before anything of a content file and the files
/// below it is written, so <see cref="ExistingFileAction.Cancel"/> leaves no part of it behind.
/// </para>
/// </remarks>
public sealed class ContentExporter
{
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

    private static readonly FrozenSet<ResourceType> ConsumedDependencyTypes = new[]
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
    private readonly HashSet<string> WrittenPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> HandledFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> VisitedDependencies = new(StringComparer.OrdinalIgnoreCase);
    private ExistingFileAction? ConfirmedExistingFileAction;
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

    /// <summary>
    /// Gets the order to export resources of the given file name in, lowest first, so a resource exported before the
    /// ones it consumes marks them as handled, see <see cref="IsHandled"/>. Resources the compiler generated from
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
    /// Decompiles a resource and writes its files.
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

        using (var contentFile = FileExtract.Extract(resource, fileLoader, Progress, ExtractOptions))
        {
            Export(contentFile, targetFile, cancellationToken);
        }

        if (IncludeDependencies && source != null)
        {
            ExportDependencies(resource, fileLoader, source, cancellationToken);
        }
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

        if (ConfirmExistingFiles != null || ExistingFileAction == ExistingFileAction.Cancel)
        {
            ConfirmExistingFilesOf(contentFile, targetFile);
        }

        ExportContentFile(contentFile, targetFile, cancellationToken);
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
        Write(targetFile ?? Path.Join(OutputRoot, normalizedPath), data);
    }

    /// <summary>
    /// Whether a file outside the content paths, such as a glTF export, may be written, following
    /// <see cref="ExistingFileAction"/> and <see cref="ConfirmExistingFiles"/>.
    /// </summary>
    /// <exception cref="OperationCanceledException">The existing file action is <see cref="ExistingFileAction.Cancel"/>.</exception>
    public bool CanWrite(string path) => GetExistingFileAction(Path.GetFullPath(path)) != ExistingFileAction.Skip;

    private void ExportContentFile(ContentFile contentFile, string? targetFile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? contentPath = null;

        if (contentFile.FileName.Length > 0)
        {
            contentPath = NormalizeContentPath(contentFile.FileName);
            MarkHandled(contentPath);
        }

        if (targetFile != null && contentFile.Data == null && contentFile.SubFiles.Count == 1)
        {
            var subFile = contentFile.SubFiles[0];
            WriteSubFile(subFile, targetFile, NormalizeContentPath(subFile.FileName));
        }
        else
        {
            foreach (var subFile in contentFile.SubFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var subFilePath = NormalizeContentPath(subFile.FileName);
                WriteSubFile(subFile, Path.Join(OutputRoot, subFilePath), subFilePath);
            }
        }

        foreach (var additionalFile in contentFile.AdditionalFiles)
        {
            ExportContentFile(additionalFile, null, cancellationToken);
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

        Write(outputPath, contentFile.Data);
    }

    private void WriteSubFile(SubFile subFile, string outputPath, string contentPath)
    {
        byte[]? data;

        try
        {
            data = subFile.Extract?.Invoke();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Interlocked.Increment(ref FilesFailed);
            FileFailed?.Invoke(contentPath, e);
            return;
        }

        if (data is not { Length: > 0 })
        {
            return;
        }

        Write(outputPath, data);
    }

    private void Write(string outputPath, ReadOnlySpan<byte> data)
    {
        outputPath = Path.GetFullPath(outputPath);

        lock (StateLock)
        {
            if (!WrittenPaths.Add(outputPath))
            {
                return;
            }
        }

        if (GetExistingFileAction(outputPath) == ExistingFileAction.Skip)
        {
            FileSkipped?.Invoke(outputPath);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllBytes(outputPath, data);

        Interlocked.Increment(ref FilesWritten);
        FileWritten?.Invoke(outputPath);
    }

    private void ConfirmExistingFilesOf(ContentFile contentFile, string? targetFile)
    {
        foreach (var path in GetOutputPaths(contentFile, targetFile))
        {
            var fullPath = Path.GetFullPath(path);

            lock (StateLock)
            {
                if (WrittenPaths.Contains(fullPath))
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
            throw new OperationCanceledException($"Cancelled because \"{path}\" already exists.");
        }

        return action;
    }

    private void ExportDependencies(Resource resource, IFileLoader fileLoader, ContentExportSource source, CancellationToken cancellationToken)
    {
        var pending = new PriorityQueue<string, int>();
        EnqueueReferences(resource, pending);

        while (pending.TryDequeue(out var name, out _))
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (StateLock)
            {
                if (!VisitedDependencies.Add(name))
                {
                    continue;
                }
            }

            try
            {
                using var dependency = source.Load(name + GameFileLoader.CompiledFileSuffix);

                if (dependency == null)
                {
                    continue;
                }

                EnqueueReferences(dependency, pending);

                if (!ConsumedDependencyTypes.Contains(dependency.ResourceType) && !IsHandled(name))
                {
                    Export(dependency, fileLoader, cancellationToken: cancellationToken);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Interlocked.Increment(ref FilesFailed);
                FileFailed?.Invoke(name, e);
            }
        }
    }

    private static void EnqueueReferences(Resource resource, PriorityQueue<string, int> pending)
    {
        foreach (var reference in resource.ExternalReferences?.ResourceRefInfoList ?? [])
        {
            var name = ContentPaths.Normalize(reference.Name);

            if (name.Length > 0)
            {
                pending.Enqueue(name, GetExportOrder(name + GameFileLoader.CompiledFileSuffix));
            }
        }
    }

    private void MarkHandled(string contentPath)
    {
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
    private static string? GetGeneratingResource(string contentPath)
    {
        var separator = contentPath.IndexOf('+', StringComparison.Ordinal);

        return separator > 0 && Path.HasExtension(contentPath.AsSpan(0, separator)) ? contentPath[..separator] : null;
    }

    private static string GetHandledKey(string contentPath)
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
