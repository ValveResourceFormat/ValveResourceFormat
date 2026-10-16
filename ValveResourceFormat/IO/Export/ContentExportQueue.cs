using System.Collections.Concurrent;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.IO;

/// <summary>
/// Exports many files through one <see cref="ContentExporter"/> on several threads at once.
/// </summary>
/// <remarks>
/// <para>
/// Files are exported in <see cref="ContentExporter.GetExportOrder"/> order, then by content path, as threads become
/// free. A resource waits for every resource in the queue that references it and comes before it in that order, so a
/// resource that consumes another, such as a model its meshes, has marked it as handled by the time it comes up.
/// Resources the compiler generated from another one wait for it, and files in a map's folder wait for the map.
/// </para>
/// <para>
/// With <see cref="ContentExporter.IncludeDependencies"/>, every resource an item references, directly or through
/// other references, that is in the item's <see cref="ContentExportItem.Source"/> is exported as an item of its own.
/// Resources that only exist as part of another, such as meshes, are not, but their references are followed.
/// </para>
/// </remarks>
/// <param name="exporter">The exporter every file is written through.</param>
public sealed class ContentExportQueue(ContentExporter exporter)
{
    private sealed record ResourceHeader(ResourceType Type, string[] References, string? LumpFolder);

    private sealed record Dependency(string ContentPath, ContentExportSource Source, IFileLoader FileLoader);

    private sealed class QueuedItem(ContentExportItem item)
    {
        public ContentExportItem Item { get; } = item;
        public string Key { get; } = ContentExporter.GetHandledKey(item.ContentPath);
        public ResourceHeader? Header { get; set; }
        public int Rank { get; set; }
        public int PendingReferrers { get; set; }
        public List<QueuedItem> Dependents { get; } = [];
    }

    private readonly ContentExporter Exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
    private readonly List<ContentExportItem> Items = [];
    private readonly List<(ResourceHeader Header, ContentExportSource Source, IFileLoader FileLoader)> DependencyRoots = [];
    private readonly ConcurrentDictionary<string, Lazy<ResourceHeader?>> SourceHeaders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets how many files are exported at once. Defaults to <see cref="Environment.ProcessorCount"/>.</summary>
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;

    /// <summary>Gets a callback receiving every item before it is exported, on the thread that exports it.</summary>
    public Action<ContentExportItem>? ItemStarted { get; init; }

    /// <summary>
    /// Gets a callback receiving every item once it was exported or skipped, with its
    /// <see cref="ContentExportItem.Result"/> set.
    /// </summary>
    public Action<ContentExportItem>? ItemCompleted { get; init; }

    /// <summary>
    /// Adds a file to export.
    /// </summary>
    public void Add(ContentExportItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Items.Add(item);
    }

    /// <summary>
    /// Adds the dependencies of a resource that was already exported.
    /// </summary>
    internal void AddDependenciesOf(Resource resource, IFileLoader fileLoader, ContentExportSource source)
    {
        DependencyRoots.Add((new ResourceHeader(resource.ResourceType, GetReferences(resource.ExternalReferences), null), source, fileLoader));
    }

    /// <summary>
    /// Exports every added file on the calling thread and up to <see cref="MaxDegreeOfParallelism"/> minus one others,
    /// and returns once all of them are done. A file that fails is reported through
    /// <see cref="ContentExporter.FileFailed"/> and the others carry on.
    /// </summary>
    /// <param name="cancellationToken">Stops the export, every thread finishes the file it is on.</param>
    /// <exception cref="OperationCanceledException">
    /// The export was cancelled, through the token or <see cref="ExistingFileAction.Cancel"/>.
    /// </exception>
    public void Run(CancellationToken cancellationToken = default)
    {
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(MaxDegreeOfParallelism, 1),
            CancellationToken = cancellationToken,
        };

        var queued = Items.ConvertAll(static item => new QueuedItem(item));

        Parallel.ForEach(queued, parallelOptions, static queuedItem =>
        {
            var item = queuedItem.Item;

            if (item.Decompile && ContentPaths.HasCompiledSuffix(item.ContentPath))
            {
                queuedItem.Header = ReadHeader(item.Open, item.ContentPath);
            }
        });

        if (Exporter.IncludeDependencies)
        {
            AddDependencies(queued, parallelOptions);
        }

        var rank = 0;

        foreach (var queuedItem in queued
            .OrderBy(static queuedItem => ContentExporter.GetExportOrder(queuedItem.Item.ContentPath))
            .ThenBy(static queuedItem => queuedItem.Item.ContentPath, StringComparer.OrdinalIgnoreCase))
        {
            queuedItem.Rank = rank++;
        }

        LinkReferrers(queued, parallelOptions);
        ExportAll(queued, cancellationToken);
    }

    private void AddDependencies(List<QueuedItem> queued, ParallelOptions parallelOptions)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<Dependency>();

        foreach (var queuedItem in queued)
        {
            if (queuedItem.Item.Source is { } source)
            {
                known.Add(GetDependencyKey(source, queuedItem.Key));
            }
        }

        void Follow(ResourceHeader? header, ContentExportSource? source, IFileLoader? fileLoader)
        {
            if (header == null || source == null || fileLoader == null)
            {
                return;
            }

            foreach (var name in header.References)
            {
                if (known.Add(GetDependencyKey(source, name)))
                {
                    pending.Add(new Dependency(name + GameFileLoader.CompiledFileSuffix, source, fileLoader));
                }
            }
        }

        foreach (var queuedItem in queued)
        {
            if (queuedItem.Item.Decompile)
            {
                Follow(queuedItem.Header, queuedItem.Item.Source, queuedItem.Item.FileLoader);
            }
        }

        foreach (var (header, source, fileLoader) in DependencyRoots)
        {
            Follow(header, source, fileLoader);
        }

        while (pending.Count > 0)
        {
            var dependencies = pending;
            var headers = new (bool Exists, ResourceHeader? Header)[dependencies.Count];
            pending = [];

            Parallel.For(0, dependencies.Count, parallelOptions, i =>
            {
                var dependency = dependencies[i];
                Stream? stream;

                try
                {
                    stream = dependency.Source.OpenStream(dependency.ContentPath);
                }
                catch (Exception)
                {
                    headers[i] = (true, null);
                    return;
                }

                headers[i] = stream == null ? (false, null) : (true, ReadHeader(() => stream, dependency.ContentPath));
            });

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
                    var item = new ContentExportItem
                    {
                        ContentPath = dependency.ContentPath,
                        Open = () => dependency.Source.OpenStream(dependency.ContentPath)
                            ?? throw new FileNotFoundException($"\"{dependency.ContentPath}\" is no longer in its source."),
                        Source = dependency.Source,
                        FileLoader = dependency.FileLoader,
                        IsDependency = true,
                    };

                    queued.Add(new QueuedItem(item) { Header = header });
                }

                Follow(header, dependency.Source, dependency.FileLoader);
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
            if (!queuedItem.Item.Decompile)
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
            var referrer = queued[i];

            if (referrer.Item.Decompile && referrer.Header != null)
            {
                reachedItems[i] = FindReferenced(referrer, byKey);
            }
        });

        for (var i = 0; i < queued.Count; i++)
        {
            var referrer = queued[i];

            foreach (var referenced in reachedItems[i] ?? [])
            {
                Link(referrer, referenced);
            }

            if (!referrer.Item.Decompile || referrer.Header is not { } header)
            {
                continue;
            }

            if (header.LumpFolder != null)
            {
                var lumpFolder = header.LumpFolder + "/";

                foreach (var (key, inLumpFolder) in byKey)
                {
                    if (key.StartsWith(lumpFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        inLumpFolder.ForEach(queuedItem => Link(referrer, queuedItem));
                    }
                }
            }
        }

        foreach (var generated in queued)
        {
            if (generated.Item.Decompile
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
                header = GetSourceHeader(source, name);
            }

            foreach (var reference in header?.References ?? [])
            {
                pending.Push(reference);
            }
        }

        return reached;
    }

    private ResourceHeader? GetSourceHeader(ContentExportSource source, string key)
    {
        var header = SourceHeaders.GetOrAdd(GetDependencyKey(source, key), _ => new Lazy<ResourceHeader?>(() =>
        {
            Stream? stream;

            try
            {
                stream = source.OpenStream(key + GameFileLoader.CompiledFileSuffix);
            }
            catch (Exception)
            {
                return null;
            }

            return stream == null ? null : ReadHeader(() => stream, key + GameFileLoader.CompiledFileSuffix);
        }));

        return header.Value;
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
            var workers = new Task[Math.Clamp(MaxDegreeOfParallelism, 1, Math.Max(queued.Count, 1)) - 1];

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

        cancellationToken.ThrowIfCancellationRequested();

        if (Exporter.IsCancelled)
        {
            throw new OperationCanceledException("The export was cancelled because a file already exists.");
        }
    }

    private void Export(ContentExportItem item, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || Exporter.IsCancelled)
        {
            return;
        }

        if (item.Decompile && !Exporter.TryBeginExport(item.ContentPath, item.Origin ?? item.Source?.Name ?? item.ContentPath))
        {
            item.Result = ContentExportResult.Handled;
            ItemCompleted?.Invoke(item);
            return;
        }

        ItemStarted?.Invoke(item);

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

        ItemCompleted?.Invoke(item);
    }

    /// <returns>Whether every file of the item was written.</returns>
    private bool ExportFiles(ContentExportItem item, CancellationToken cancellationToken)
    {
        using var stream = item.Open();

        if (!item.Decompile)
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

        if (!ContentPaths.HasCompiledSuffix(item.ContentPath))
        {
            if (item.ExportFile != null)
            {
                if (item.ExportFile(stream, cancellationToken))
                {
                    return true;
                }

                stream.Position = 0;
            }

            using var contentFile = FileExtract.ExtractNonResource(stream, item.ContentPath);

            if (contentFile != null)
            {
                return Exporter.ExportFiles(contentFile, item.TargetFile, cancellationToken);
            }

            Exporter.WriteFile(item.ContentPath, ReadToEnd(stream), item.TargetFile);
            return true;
        }

        using var resource = new Resource
        {
            FileName = item.ContentPath,
        };

        resource.Read(stream);

        if (item.ExportResource?.Invoke(resource, cancellationToken) == true)
        {
            return true;
        }

        var fileLoader = item.FileLoader ?? throw new InvalidOperationException($"\"{item.ContentPath}\" has no file loader to decompile it with.");

        return Exporter.ExportResource(resource, fileLoader, item.TargetFile, cancellationToken);
    }

    /// <summary>
    /// Reads what ordering and dependencies need from a compiled resource, without parsing its data.
    /// </summary>
    /// <returns>The header, or null when the resource could not be read.</returns>
    private static ResourceHeader? ReadHeader(Func<Stream> open, string contentPath)
    {
        try
        {
            using var resource = new Resource
            {
                FileName = contentPath,
            };

            resource.Read(open(), mode: ReadMode.Deferred);

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

            return new ResourceHeader(resource.ResourceType, GetReferences(externalReferences), lumpFolder);
        }
        catch (Exception)
        {
            return null;
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
