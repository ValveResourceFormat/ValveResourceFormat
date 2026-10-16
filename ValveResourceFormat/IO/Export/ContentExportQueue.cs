using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ValvePak;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.NavMesh;

namespace ValveResourceFormat.IO;

/// <summary>
/// Runs <see cref="ContentExporter.Export(IEnumerable{ContentExportItem}, CancellationToken)"/>.
/// </summary>
internal sealed class ContentExportQueue(ContentExporter exporter, IEnumerable<ContentExportItem> items)
{
    private sealed record ResourceHeader(ResourceType Type, string[] References, string? LumpFolder);

    private sealed record Dependency(string Key, ContentExportSource Source, IFileLoader FileLoader, ContentExportOutput Output);

    private sealed class QueuedItem(ContentExportItem item)
    {
        public ContentExportItem Item { get; } = item;
        public string Key { get; } = ContentExporter.GetHandledKey(item.ContentPath);
        public List<ContentExportItem> ShaderFiles { get; init; } = [];
        public ResourceHeader? Header { get; set; }
        public int Rank { get; set; }
        public int PendingReferrers { get; set; }
        public List<QueuedItem> Dependents { get; } = [];
    }

    private readonly ContentExporter Exporter = exporter;
    private readonly List<ContentExportItem> Items = [.. items];
    private readonly List<(ResourceHeader Header, ContentExportSource Source, IFileLoader FileLoader)> DependencyRoots = [];
    private readonly ConcurrentDictionary<string, Lazy<(bool Exists, ResourceHeader? Header)>> SourceHeaders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds the dependencies of a resource that was already exported.
    /// </summary>
    internal void AddDependenciesOf(Resource resource, IFileLoader fileLoader, ContentExportSource source)
    {
        DependencyRoots.Add((new ResourceHeader(resource.ResourceType, GetReferences(resource.ExternalReferences), null), source, fileLoader));
    }

    public void Run(CancellationToken cancellationToken)
    {
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(Exporter.MaxDegreeOfParallelism, 1),
            CancellationToken = cancellationToken,
        };

        var selected = SelectShaderFiles();

        if (selected.Where(static queuedItem => queuedItem.Item.TargetFile != null)
            .GroupBy(static queuedItem => queuedItem.Item.TargetFile, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static target => target.Skip(1).Any()) is { } sharedTarget)
        {
            throw new ArgumentException($"More than one file would be written to \"{sharedTarget.Key}\".");
        }

        Parallel.ForEach(selected, parallelOptions, static queuedItem =>
        {
            var item = queuedItem.Item;

            if (item.Output != ContentExportOutput.Raw && !item.IsShaderFile)
            {
                queuedItem.Header = ReadHeader(item.Open, item.ContentPath).Header;
            }
        });

        var manifests = Exporter.ManifestOptions != null ? LoadManifests(selected) : [];
        var queued = SkipUnchanged(selected, manifests);

        foreach (var queuedItem in queued)
        {
            Complete(queuedItem.ShaderFiles, ContentExportResult.Handled);
        }

        if (Exporter.IncludeDependencies)
        {
            foreach (var queuedItem in queued)
            {
                if (queuedItem.Header != null && queuedItem.Item.Source == null)
                {
                    Exporter.Warning?.Invoke($"Dependencies of \"{queuedItem.Item.Origin ?? queuedItem.Item.ContentPath}\" are not exported, it is not in an addon or mod folder.");
                }
            }

            AddDependencies(queued, selected, parallelOptions);
        }

        Exporter.AddNonGltfItems(queued
            .Where(static queuedItem => queuedItem.Item.Output == ContentExportOutput.Decompile)
            .Select(static queuedItem => queuedItem.Item.ContentPath));

        var rank = 0;

        foreach (var queuedItem in queued
            .OrderBy(static queuedItem => ContentExporter.GetExportOrder(queuedItem.Item.ContentPath))
            .ThenBy(static queuedItem => queuedItem.Item.ContentPath, StringComparer.OrdinalIgnoreCase))
        {
            queuedItem.Rank = rank++;
        }

        LinkReferrers(queued, parallelOptions);
        ExportAll(queued, cancellationToken);

        foreach (var queuedItem in selected)
        {
            if (queuedItem.Item.Source?.Package is { } package && manifests.TryGetValue(package, out var manifest))
            {
                manifest.Record(queuedItem.Item, queuedItem.Item.Result);
                queuedItem.ShaderFiles.ForEach(shaderFile => manifest.Record(shaderFile, queuedItem.Item.Result));
            }
        }

        foreach (var manifest in manifests.Values)
        {
            manifest.Save(Exporter.Progress);
        }
    }

    /// <summary>
    /// Keeps one item per compiled shader in a package or folder to decompile the shader from, see
    /// <see cref="ContentExporter.SelectShaderFiles"/>, with the other items of the shader.
    /// </summary>
    private List<QueuedItem> SelectShaderFiles()
    {
        var shaderFiles = Items
            .Where(static item => item.IsShaderFile)
            .GroupBy(static item => item.Source?.Package ?? (object?)Path.GetDirectoryName(item.DiskPath));

        var selected = new Dictionary<ContentExportItem, List<ContentExportItem>>();
        var unselected = new HashSet<ContentExportItem>();

        foreach (var locationShaderFiles in shaderFiles)
        {
            foreach (var (item, others) in ContentExporter.SelectShaderFiles(locationShaderFiles, static item => item.ContentPath))
            {
                selected.Add(item, others);
                unselected.UnionWith(others);
            }
        }

        return Items
            .Where(item => !unselected.Contains(item))
            .Select(item => new QueuedItem(item) { ShaderFiles = selected.GetValueOrDefault(item, []) })
            .ToList();
    }

    private Dictionary<Package, ContentExportManifest> LoadManifests(List<QueuedItem> queued)
    {
        return queued
            .Select(static queuedItem => queuedItem.Item.Source?.Package)
            .OfType<Package>()
            .Distinct()
            .ToDictionary(static package => package, package => ContentExportManifest.Load(Exporter, package));
    }

    /// <summary>
    /// Completes the items whose files did not change since the export their manifest records, unless they consume a
    /// resource that changed, such as a model whose mesh changed.
    /// </summary>
    /// <returns>The items to export.</returns>
    private List<QueuedItem> SkipUnchanged(List<QueuedItem> queued, Dictionary<Package, ContentExportManifest> manifests)
    {
        if (manifests.Count == 0)
        {
            return queued;
        }

        var unchanged = new HashSet<QueuedItem>();

        foreach (var queuedItem in queued)
        {
            if (queuedItem.Item.Source?.Package is { } package && manifests.TryGetValue(package, out var manifest)
                && manifest.IsUnchanged(queuedItem.Item) && queuedItem.ShaderFiles.TrueForAll(manifest.IsUnchanged))
            {
                unchanged.Add(queuedItem);
            }
        }

        var referrers = new Dictionary<string, List<QueuedItem>>(StringComparer.OrdinalIgnoreCase);

        foreach (var queuedItem in queued)
        {
            foreach (var reference in queuedItem.Header?.References ?? [])
            {
                if (!referrers.TryGetValue(reference, out var referencing))
                {
                    referencing = [];
                    referrers.Add(reference, referencing);
                }

                referencing.Add(queuedItem);
            }
        }

        var consumedChanges = new Stack<QueuedItem>(queued.Where(queuedItem => !unchanged.Contains(queuedItem) && IsConsumed(queuedItem)));

        while (consumedChanges.TryPop(out var changed))
        {
            foreach (var referrer in referrers.GetValueOrDefault(changed.Key, []))
            {
                if (unchanged.Remove(referrer) && IsConsumed(referrer))
                {
                    consumedChanges.Push(referrer);
                }
            }
        }

        foreach (var queuedItem in unchanged)
        {
            Complete([queuedItem.Item, .. queuedItem.ShaderFiles], ContentExportResult.Unchanged);
        }

        return queued.FindAll(queuedItem => !unchanged.Contains(queuedItem));
    }

    private static bool IsConsumed(QueuedItem queuedItem)
        => queuedItem.Header != null && ContentExporter.ConsumedDependencyTypes.Contains(queuedItem.Header.Type);

    private void Complete(List<ContentExportItem> completed, ContentExportResult result)
    {
        foreach (var item in completed)
        {
            item.Result = result;
            Exporter.ItemCompleted?.Invoke(item);
        }
    }

    /// <param name="queued">The items to export, dependencies are added to them.</param>
    /// <param name="selected">Every item of the export, including those that are not exported again.</param>
    /// <param name="parallelOptions">How headers are read.</param>
    private void AddDependencies(List<QueuedItem> queued, List<QueuedItem> selected, ParallelOptions parallelOptions)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<Dependency>();

        foreach (var queuedItem in selected)
        {
            if (queuedItem.Item.Source is { } source)
            {
                known.Add(GetDependencyKey(source, queuedItem.Key));
            }
        }

        void Follow(ResourceHeader? header, ContentExportSource? source, IFileLoader fileLoader, ContentExportOutput output)
        {
            if (header == null || source == null)
            {
                return;
            }

            foreach (var name in header.References)
            {
                if (known.Add(GetDependencyKey(source, name)))
                {
                    pending.Add(new Dependency(name, source, fileLoader, output));
                }
            }
        }

        foreach (var queuedItem in queued)
        {
            var item = queuedItem.Item;
            Follow(queuedItem.Header, item.Source, Exporter.GetFileLoader(item), IsGltf(item.Output) ? item.Output : ContentExportOutput.Decompile);
        }

        foreach (var (header, source, fileLoader) in DependencyRoots)
        {
            Follow(header, source, fileLoader, ContentExportOutput.Decompile);
        }

        while (pending.Count > 0)
        {
            var dependencies = pending;
            var headers = new (bool Exists, ResourceHeader? Header)[dependencies.Count];
            pending = [];

            Parallel.For(0, dependencies.Count, parallelOptions, i => headers[i] = GetSourceHeader(dependencies[i].Source, dependencies[i].Key));

            for (var i = 0; i < dependencies.Count; i++)
            {
                var (exists, header) = headers[i];

                if (!exists)
                {
                    continue;
                }

                var dependency = dependencies[i];

                if (header == null || !ContentExporter.ConsumedDependencyTypes.Contains(header.Type))
                {
                    var contentPath = dependency.Key + GameFileLoader.CompiledFileSuffix;
                    var item = new ContentExportItem
                    {
                        ContentPath = contentPath,
                        Open = () => dependency.Source.OpenStream(contentPath)
                            ?? throw new FileNotFoundException($"\"{contentPath}\" is no longer in its source."),
                        Source = dependency.Source,
                        FileLoader = dependency.FileLoader,
                        Output = header != null && GltfModelExporter.CanExport(header.Type) ? dependency.Output : ContentExportOutput.Decompile,
                        IsDependency = true,
                    };

                    queued.Add(new QueuedItem(item) { Header = header });
                }

                Follow(header, dependency.Source, dependency.FileLoader, dependency.Output);
            }
        }
    }

    /// <summary>
    /// Makes every item wait for the earlier items that reference it, directly or through other resources in the queue
    /// or in their source, generated it, or are the map whose folder it is in.
    /// </summary>
    private void LinkReferrers(List<QueuedItem> queued, ParallelOptions parallelOptions)
    {
        var byKey = new Dictionary<string, List<QueuedItem>>(StringComparer.OrdinalIgnoreCase);

        foreach (var queuedItem in queued)
        {
            if (queuedItem.Item.Output == ContentExportOutput.Raw)
            {
                continue;
            }

            if (!byKey.TryGetValue(queuedItem.Key, out var sameKey))
            {
                sameKey = [];
                byKey.Add(queuedItem.Key, sameKey);
            }

            sameKey.Add(queuedItem);
        }

        var reachedItems = new HashSet<QueuedItem>?[queued.Count];

        Parallel.For(0, queued.Count, parallelOptions, i =>
        {
            if (queued[i].Header != null)
            {
                reachedItems[i] = FindReferenced(queued[i], byKey);
            }
        });

        for (var i = 0; i < queued.Count; i++)
        {
            var referrer = queued[i];

            foreach (var referenced in reachedItems[i] ?? [])
            {
                Link(referrer, referenced);
            }

            if (referrer.Header?.LumpFolder is not { } lumpFolder)
            {
                continue;
            }

            lumpFolder += "/";

            foreach (var (key, inLumpFolder) in byKey)
            {
                if (key.StartsWith(lumpFolder, StringComparison.OrdinalIgnoreCase))
                {
                    inLumpFolder.ForEach(queuedItem => Link(referrer, queuedItem));
                }
            }
        }

        foreach (var generated in queued)
        {
            if (generated.Item.Output != ContentExportOutput.Raw
                && ContentExporter.GetGeneratingResource(generated.Key) is { } generatingResource
                && byKey.TryGetValue(generatingResource, out var generators))
            {
                generators.ForEach(generator => Link(generator, generated));
            }
        }
    }

    /// <summary>
    /// Finds the items an item references, following references through every resource it reaches, whether it is in the
    /// queue or only in the item's source.
    /// </summary>
    private HashSet<QueuedItem> FindReferenced(QueuedItem referrer, Dictionary<string, List<QueuedItem>> byKey)
    {
        var reached = new HashSet<QueuedItem>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { referrer.Key };
        var pending = new Stack<string>(referrer.Header!.References);

        while (pending.TryPop(out var name))
        {
            if (!visited.Add(name))
            {
                continue;
            }

            ResourceHeader? header = null;

            if (byKey.TryGetValue(name, out var referenced))
            {
                reached.UnionWith(referenced);
                header = referenced.Find(static queuedItem => queuedItem.Header != null)?.Header;
            }

            if (header == null && referrer.Item.Source is { } source)
            {
                header = GetSourceHeader(source, name).Header;
            }

            foreach (var reference in header?.References ?? [])
            {
                pending.Push(reference);
            }
        }

        return reached;
    }

    /// <summary>
    /// Reads the header of a compiled resource from a source, once per source and resource.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="key">The content path of the resource without its <c>_c</c> suffix.</param>
    private (bool Exists, ResourceHeader? Header) GetSourceHeader(ContentExportSource source, string key)
    {
        var contentPath = key + GameFileLoader.CompiledFileSuffix;

        return SourceHeaders.GetOrAdd(GetDependencyKey(source, key), _ => new(() => ReadHeader(() => source.OpenStream(contentPath), contentPath))).Value;
    }

    /// <summary>
    /// Only an earlier item gates a later one, so references in both directions can not wait on each other.
    /// </summary>
    private static void Link(QueuedItem referrer, QueuedItem referenced)
    {
        if (referrer.Rank < referenced.Rank)
        {
            referrer.Dependents.Add(referenced);
            referenced.PendingReferrers++;
        }
    }

    private void ExportAll(List<QueuedItem> queued, CancellationToken cancellationToken)
    {
        var gate = new object();
        var ready = new PriorityQueue<QueuedItem, int>();
        var remaining = queued.Count;
        var stopped = false;

        foreach (var queuedItem in queued)
        {
            if (queuedItem.PendingReferrers == 0)
            {
                ready.Enqueue(queuedItem, queuedItem.Rank);
            }
        }

        void Stop()
        {
            lock (gate)
            {
                stopped = true;
                Monitor.PulseAll(gate);
            }
        }

        void Work()
        {
            while (true)
            {
                QueuedItem? next = null;

                lock (gate)
                {
                    while (!stopped && remaining > 0 && !ready.TryDequeue(out next, out _))
                    {
                        Monitor.Wait(gate);
                    }
                }

                if (next == null)
                {
                    return;
                }

                try
                {
                    Export(next.Item, cancellationToken);
                }
                finally
                {
                    lock (gate)
                    {
                        remaining--;

                        foreach (var dependent in next.Dependents)
                        {
                            if (--dependent.PendingReferrers == 0)
                            {
                                ready.Enqueue(dependent, dependent.Rank);
                            }
                        }

                        stopped |= cancellationToken.IsCancellationRequested || Exporter.IsCancelled;
                        Monitor.PulseAll(gate);
                    }
                }
            }
        }

        using (cancellationToken.Register(Stop))
        {
            var workers = new Task[Math.Clamp(Exporter.MaxDegreeOfParallelism, 1, Math.Max(queued.Count, 1)) - 1];

            for (var i = 0; i < workers.Length; i++)
            {
                workers[i] = Task.Factory.StartNew(Work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }

            try
            {
                Work();
            }
            catch
            {
                Stop();
                throw;
            }
            finally
            {
                Task.WaitAll(workers, CancellationToken.None);
            }
        }

        Exporter.ThrowIfCancelled(cancellationToken);
    }

    private void Export(ContentExportItem item, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || Exporter.IsCancelled)
        {
            return;
        }

        if (item.Output != ContentExportOutput.Raw && !Exporter.TryBeginExport(item.ContentPath, item.Origin ?? item.Source?.Name ?? item.ContentPath))
        {
            item.Result = ContentExportResult.Handled;
            Exporter.ItemCompleted?.Invoke(item);
            return;
        }

        Exporter.ItemStarted?.Invoke(item);

        try
        {
            item.Result = ExportFiles(item, cancellationToken) ? ContentExportResult.Exported : ContentExportResult.Failed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || Exporter.IsCancelled)
        {
            return;
        }
        catch (Exception e)
        {
            item.Result = ContentExportResult.Failed;
            Exporter.ReportFailure(item.ContentPath, e);
        }

        Exporter.ItemCompleted?.Invoke(item);
    }

    /// <returns>Whether every file of the item was written.</returns>
    private bool ExportFiles(ContentExportItem item, CancellationToken cancellationToken)
    {
        if (item.IsShaderFile)
        {
            return ExportShader(item, cancellationToken);
        }

        using var stream = item.Open();

        if (item.Output == ContentExportOutput.Raw)
        {
            var data = ReadToEnd(stream);

            if (item.Crc32 != null)
            {
                var crc32 = Crc32.HashToUInt32(data);

                if (crc32 != item.Crc32)
                {
                    throw new InvalidDataException($"CRC32 mismatch for read data (expected {item.Crc32:X8}, got {crc32:X8}).");
                }
            }

            Exporter.WriteFile(item.ContentPath, data, item.TargetFile);
            return true;
        }

        if (!ContentPaths.HasCompiledSuffix(item.ContentPath) && !IsResource(stream))
        {
            return ExportNonResource(item, stream, cancellationToken);
        }

        using var resource = new Resource
        {
            FileName = item.ContentPath,
        };

        resource.Read(stream);

        var fileLoader = Exporter.GetFileLoader(item);

        if (IsGltf(item.Output) && GltfModelExporter.CanExport(resource))
        {
            Exporter.GetGltfExporter(fileLoader).Export(resource, GetGltfPath(item), cancellationToken);
            return true;
        }

        return Exporter.Export(resource, fileLoader, targetFile: item.TargetFile, cancellationToken: cancellationToken);
    }

    private bool ExportNonResource(ContentExportItem item, Stream stream, CancellationToken cancellationToken)
    {
        if (IsGltf(item.Output) && FileExtract.PeekMagic(stream) == NavMeshFile.MAGIC)
        {
            var navMesh = new NavMeshFile();
            navMesh.Read(stream);

            Exporter.GetGltfExporter(Exporter.GetFileLoader(item)).Export(navMesh, item.ContentPath, GetGltfPath(item), cancellationToken);
            return true;
        }

        using var contentFile = FileExtract.ExtractNonResource(stream, item.ContentPath, Exporter.ExtractOptions);

        if (contentFile != null)
        {
            return Exporter.Export(contentFile, item.TargetFile, cancellationToken);
        }

        Exporter.WriteFile(item.ContentPath, ReadToEnd(stream), item.TargetFile);
        return true;
    }

    /// <summary>
    /// Decompiles a compiled shader from all of its files in the package or folder, to a <c>.vfx</c> file named after
    /// them without their program type.
    /// </summary>
    private bool ExportShader(ContentExportItem item, CancellationToken cancellationToken)
    {
        var vfxFileName = ContentPaths.Join(ContentPaths.GetDirectoryName(item.ContentPath), ShaderCollection.GetCollectionName(item.ContentPath) + ".vfx");
        var package = item.Source?.Package;

        using var collection = ShaderCollection.GetShaderCollection(package != null ? item.ContentPath : item.DiskPath!, package);
        using var contentFile = new ShaderExtract(collection).ToContentFile(vfxFileName);

        return Exporter.Export(contentFile, item.TargetFile, cancellationToken);
    }

    private static bool IsGltf(ContentExportOutput output) => output is ContentExportOutput.Gltf or ContentExportOutput.Glb;

    /// <summary>
    /// Gets the path a glTF export is written to: the target file or the item's content path, with the extension of
    /// its format.
    /// </summary>
    private string GetGltfPath(ContentExportItem item)
    {
        var extension = item.Output == ContentExportOutput.Glb ? ".glb" : ".gltf";

        return Path.ChangeExtension(item.TargetFile ?? Exporter.GetOutputPath(item.ContentPath), extension);
    }

    /// <summary>
    /// Whether a stream starts with the header of a compiled resource, keeping its position.
    /// </summary>
    internal static bool IsResource(Stream stream)
    {
        Span<byte> header = stackalloc byte[12];
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        stream.Seek(-read, SeekOrigin.Current);

        return read == header.Length
            && BinaryPrimitives.ReadUInt16LittleEndian(header[4..]) == Resource.KnownHeaderVersion
            && BinaryPrimitives.ReadUInt32LittleEndian(header[8..]) == 8;
    }

    /// <summary>
    /// Reads what ordering and dependencies need from a compiled resource, without parsing its data. A file without the
    /// <c>_c</c> suffix is only read when it starts like a compiled resource.
    /// </summary>
    /// <param name="open">Opens the file, returns null when it does not exist.</param>
    /// <param name="contentPath">Content path of the file.</param>
    /// <returns>Whether the file exists, and its header or null when it is not a resource that could be read.</returns>
    private static (bool Exists, ResourceHeader? Header) ReadHeader(Func<Stream?> open, string contentPath)
    {
        Stream? stream;

        try
        {
            stream = open();
        }
        catch (Exception)
        {
            return (true, null);
        }

        if (stream == null)
        {
            return (false, null);
        }

        using (stream)
        {
            try
            {
                if (!ContentPaths.HasCompiledSuffix(contentPath) && !IsResource(stream))
                {
                    return (true, null);
                }

                using var resource = new Resource
                {
                    FileName = contentPath,
                };

                resource.Read(stream, mode: ReadMode.Deferred);

                var externalReferences = resource.ExternalReferences;
                string? lumpFolder = null;

                if (resource.ResourceType == ResourceType.Map && externalReferences != null)
                {
                    try
                    {
                        lumpFolder = ContentPaths.Normalize(MapExtract.GetLumpFolderFromVmapRERL(externalReferences));
                    }
                    catch (InvalidDataException)
                    {
                    }
                }

                return (true, new ResourceHeader(resource.ResourceType, GetReferences(externalReferences), lumpFolder));
            }
            catch (Exception)
            {
                return (true, null);
            }
        }
    }

    private static string[] GetReferences(ResourceExtRefList? externalReferences)
    {
        if (externalReferences == null)
        {
            return [];
        }

        return externalReferences.ResourceRefInfoList
            .Select(static reference => ContentExporter.GetHandledKey(reference.Name))
            .Where(static name => name.Length > 0)
            .ToArray();
    }

    private static string GetDependencyKey(ContentExportSource source, string key)
    {
        return string.Concat(source.Name, "\0", key);
    }

    private static byte[] ReadToEnd(Stream stream)
    {
        var data = new byte[stream.Length - stream.Position];
        stream.ReadExactly(data);
        return data;
    }
}
