using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Utils
{
    /// <summary>
    /// Inverts the external reference lists, and the names some types store in their data, of every
    /// compiled file in the mounted packages, so a resource can list the files that reference it.
    /// </summary>
    /// <remarks>
    /// Each package is indexed once per process and shared by every context that can see it. Entries are
    /// read straight out of their archive file, because a package entry stream maps a view per entry and
    /// serializes on the package lock.
    /// </remarks>
    class ResourceReferenceIndex
    {
        private const int BufferSize = 4096;
        private const ushort EntryInDirectoryFile = 0x7FFF;

        private const ResourceReferenceKind ShippedKinds = ResourceReferenceKind.External | ResourceReferenceKind.Child
            | ResourceReferenceKind.Weak | ResourceReferenceKind.Data | ResourceReferenceKind.PanoramaImage | ResourceReferenceKind.Manifest;

        /// <summary>
        /// Types that name other files in their data as well, so the string table of their data is read
        /// along with their external reference list.
        /// </summary>
        private static readonly HashSet<string> DataReferrerTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "vanim_c", "vanmgrph_c", "vcd_c", "vcompmat_c", "vcss_c", "vdata_c", "vjs_c", "vmat_c", "vmix_c", "vpcf_c",
            "vpulse_c", "vrman_c", "vrr_c", "vsmart_c", "vsndevts_c", "vsndstck_c", "vts_c", "vxml_c",
        };

        /// <summary>
        /// Types that keep the names they reference in text and in their edit info, so they are read in full.
        /// </summary>
        private static readonly HashSet<string> FullyReadTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "vcss_c", "vjs_c", "vts_c", "vxml_c",
        };

        private static readonly ConcurrentDictionary<string, LazyIndex> Indexes = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Package> packages;
        private readonly string[] mapFolders;
        private LazyIndex[] indexes = [];
        private Task? build;

        private ResourceReferenceIndex(Dictionary<string, Package> packages, string[] mapFolders)
        {
            this.packages = packages;
            this.mapFolders = mapFolders;
        }

        /// <summary>
        /// Returns the index over the packages this context can see, or <c>null</c> when it sees none.
        /// </summary>
        public static ResourceReferenceIndex? ForContext(VrfGuiContext guiContext)
        {
            var packages = CollectPackages(guiContext);

            if (packages.Count == 0)
            {
                return null;
            }

            var mapFolders = packages.Keys
                .Select(static package => Path.Combine(Path.GetDirectoryName(package)!, "maps"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(Directory.Exists)
                .ToArray();

            return new ResourceReferenceIndex(packages, mapFolders);
        }

        /// <summary>
        /// The number of packages and map folders this index searches.
        /// </summary>
        public int SearchedCount => packages.Count + mapFolders.Length;

        /// <summary>
        /// Indexes the packages, and the maps next to them once the packages are done.
        /// </summary>
        public Task BuildAsync()
        {
            if (build != null)
            {
                return build;
            }

            var packageIndexes = packages.Select(static package => Indexes.GetOrAdd(package.Key, static (_, package) => new LazyIndex(() => Build(package)), package.Value)).ToArray();
            var mapIndexes = mapFolders.Select(static folder => Indexes.GetOrAdd(folder, static folder => new LazyIndex(() => BuildMaps(folder)))).ToArray();

            indexes = [.. packageIndexes, .. mapIndexes];
            build = Task.WhenAll(packageIndexes.Select(static index => index.BuildAsync()))
                .ContinueWith(_ => Task.WhenAll(mapIndexes.Select(static index => index.BuildAsync())), TaskScheduler.Default)
                .Unwrap();

            return build;
        }

        /// <summary>
        /// A file that references the one looked up, and where it stores the reference.
        /// </summary>
        /// <param name="Name">The referencing file.</param>
        /// <param name="Kinds">Where the referencing file stores the reference.</param>
        /// <param name="Package">The map package the referencing file is in, when it is not in a loaded package.</param>
        public readonly record struct Referrer(string Name, ResourceReferenceKind Kinds, string? Package = null);

        public IReadOnlyList<Referrer> Find(string name)
        {
            var key = Normalize(name);
            var found = new Dictionary<string, Referrer>(StringComparer.OrdinalIgnoreCase);

            foreach (var index in indexes)
            {
                var build = index.BuildAsync();

                if (build.IsCompletedSuccessfully && build.Result.TryGetValue(key, out var referrers))
                {
                    foreach (var referrer in referrers)
                    {
                        found[referrer.Name] = found.TryGetValue(referrer.Name, out var existing)
                            ? existing with { Kinds = existing.Kinds | referrer.Kinds, Package = existing.Package ?? referrer.Package }
                            : referrer;
                    }
                }
            }

            return [.. found.Values.OrderBy(static referrer => referrer.Name, StringComparer.OrdinalIgnoreCase)];
        }

        private static Dictionary<string, Package> CollectPackages(VrfGuiContext guiContext)
        {
            var packages = new Dictionary<string, Package>(StringComparer.OrdinalIgnoreCase);

            for (var context = guiContext; context != null; context = context.ParentGuiContext)
            {
                Add(context.CurrentPackage);

                foreach (var package in context.MountedPackages)
                {
                    Add(package);
                }
            }

            void Add(Package? package)
            {
                if (package?.FileName != null)
                {
                    packages.TryAdd(Path.GetFullPath(package.FileName), package);
                }
            }

            return packages;
        }

        private sealed class LazyIndex(Func<Dictionary<string, Referrer[]>> factory)
        {
            private readonly Lock buildLock = new();
            private Func<Dictionary<string, Referrer[]>>? factory = factory;
            private Task<Dictionary<string, Referrer[]>>? build;

            public Task<Dictionary<string, Referrer[]>> BuildAsync()
            {
                lock (buildLock)
                {
                    if (build == null)
                    {
                        var source = factory!;
                        factory = null;
                        build = Task.Run(source);
                    }

                    return build;
                }
            }
        }

        /// <summary>
        /// Indexes the external reference list of the map in every map package in a maps folder. Maps ship as
        /// packages of their own, which are not mounted.
        /// </summary>
        private static Dictionary<string, Referrer[]> BuildMaps(string folder)
        {
            var timer = Stopwatch.StartNew();
            var found = new Dictionary<string, List<Referrer>>(StringComparer.OrdinalIgnoreCase);
            var results = new ConcurrentBag<Dictionary<string, List<Referrer>>>();
            var mapPackages = Directory.GetFiles(folder, "*.vpk", SearchOption.AllDirectories);

            Parallel.ForEach(
                mapPackages,
                () => new Dictionary<string, List<Referrer>>(StringComparer.OrdinalIgnoreCase),
                (path, _, local) =>
                {
                    IndexMapPackage(path, local);
                    return local;
                },
                results.Add);

            foreach (var local in results)
            {
                foreach (var (key, list) in local)
                {
                    if (found.TryGetValue(key, out var existing))
                    {
                        existing.AddRange(list);
                    }
                    else
                    {
                        found.Add(key, list);
                    }
                }
            }

            var referrers = new Dictionary<string, Referrer[]>(found.Count, StringComparer.OrdinalIgnoreCase);

            foreach (var (key, list) in found)
            {
                referrers.Add(key, [.. list]);
            }

            Log.Debug(nameof(ResourceReferenceIndex), $"Indexed {mapPackages.Length} map packages in {folder} referencing {referrers.Count} targets in {timer.Elapsed.TotalSeconds:F2}s");

            return referrers;
        }

        private static void IndexMapPackage(string path, Dictionary<string, List<Referrer>> found)
        {
            try
            {
                using var package = new Package();
                package.Read(path);

                if (package.Entries == null || !package.Entries.TryGetValue("vmap_c", out var maps))
                {
                    return;
                }

                foreach (var entry in maps)
                {
                    var references = ReadReferencesThroughPackage(package, entry);

                    if (references == null)
                    {
                        continue;
                    }

                    var referrer = new Referrer(Normalize(entry.GetFullPath()), ResourceReferenceKind.External, path);

                    foreach (var reference in references)
                    {
                        var key = Normalize(reference.Name);

                        if (!found.TryGetValue(key, out var list))
                        {
                            list = [];
                            found.Add(key, list);
                        }

                        list.Add(referrer);
                    }
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Log.Debug(nameof(ResourceReferenceIndex), $"Failed to index map package {path}: {e.Message}");
            }
        }

        private static Dictionary<string, Referrer[]> Build(Package package)
        {
            var timer = Stopwatch.StartNew();
            var (found, files) = IndexPackage(package);
            var referrers = new Dictionary<string, Referrer[]>(found.Count, StringComparer.OrdinalIgnoreCase);

            foreach (var (key, list) in found)
            {
                referrers.Add(key, [.. list]);
            }

            Log.Debug(nameof(ResourceReferenceIndex), $"Indexed {files} files in {package.FileName} referencing {referrers.Count} targets in {timer.Elapsed.TotalSeconds:F2}s");

            return referrers;
        }

        private static (Dictionary<string, List<Referrer>> Found, int Files) IndexPackage(Package package)
        {
            var found = new Dictionary<string, List<Referrer>>(StringComparer.OrdinalIgnoreCase);

            if (package.Entries == null)
            {
                return (found, 0);
            }

            var byArchive = new Dictionary<ushort, List<PackageEntry>>();

            foreach (var (extension, entries) in package.Entries.ToArray())
            {
                if (!extension.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var entry in entries.ToArray())
                {
                    if (!byArchive.TryGetValue(entry.ArchiveIndex, out var archiveEntries))
                    {
                        archiveEntries = [];
                        byArchive.Add(entry.ArchiveIndex, archiveEntries);
                    }

                    archiveEntries.Add(entry);
                }
            }

            var results = new ConcurrentBag<Dictionary<string, List<Referrer>>>();
            var indexed = 0;

            Parallel.ForEach(
                byArchive,
                () => new Dictionary<string, List<Referrer>>(StringComparer.OrdinalIgnoreCase),
                (archive, _, local) =>
                {
                    Interlocked.Add(ref indexed, IndexArchive(package, archive.Key, archive.Value, local));
                    return local;
                },
                results.Add);

            foreach (var local in results)
            {
                foreach (var (key, list) in local)
                {
                    if (found.TryGetValue(key, out var existing))
                    {
                        existing.AddRange(list);
                    }
                    else
                    {
                        found.Add(key, list);
                    }
                }
            }

            return (found, indexed);
        }

        private static int IndexArchive(Package package, ushort archiveIndex, List<PackageEntry> entries,
            Dictionary<string, List<Referrer>> found)
        {
            var path = archiveIndex == EntryInDirectoryFile ? null : $"{package.FileName}_{archiveIndex:D3}.vpk";
            SafeFileHandle? handle = null;
            ArchiveEntryStream? stream = null;
            var indexed = 0;

            try
            {
                if (path != null && File.Exists(path))
                {
                    handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, FileOptions.RandomAccess);
                    stream = new ArchiveEntryStream(handle);
                }

                foreach (var entry in entries)
                {
                    List<ResourceReference>? references;

                    if (stream != null && entry.SmallData.Length == 0)
                    {
                        stream.Open(entry);
                        references = ReadReferences(entry, stream);
                    }
                    else
                    {
                        references = ReadReferencesThroughPackage(package, entry);
                    }

                    if (references == null)
                    {
                        continue;
                    }

                    indexed++;

                    var referrer = Normalize(entry.GetFullPath());

                    foreach (var reference in references)
                    {
                        var kinds = reference.Kinds & ShippedKinds;
                        var key = Normalize(reference.Name);

                        if (kinds == ResourceReferenceKind.None || string.Equals(key, referrer, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (!found.TryGetValue(key, out var list))
                        {
                            list = [];
                            found.Add(key, list);
                        }

                        if (list.Count > 0 && ReferenceEquals(list[^1].Name, referrer))
                        {
                            list[^1] = list[^1] with { Kinds = list[^1].Kinds | kinds };
                        }
                        else
                        {
                            list.Add(new Referrer(referrer, kinds));
                        }
                    }
                }
            }
            finally
            {
                stream?.Dispose();
                handle?.Dispose();
            }

            return indexed;
        }

        // Entries kept in the directory file, or carrying preloaded bytes, are not a plain slice of an archive
        private static List<ResourceReference>? ReadReferencesThroughPackage(Package package, PackageEntry entry)
        {
            try
            {
                using var stream = GameFileLoader.GetPackageEntryStream(package, entry);
                return ReadReferences(entry, stream);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static List<ResourceReference>? ReadReferences(PackageEntry entry, Stream stream)
        {
            try
            {
                using var resource = new Resource { FileName = entry.GetFullPath() };
                resource.Read(stream, leaveOpen: true, parsing: BlockParsing.Deferred);

                if (FullyReadTypes.Contains(entry.TypeName))
                {
                    return [.. ResourceReferenceCollector.Collect(resource)];
                }

                var references = new List<ResourceReference>();

                if (resource.ExternalReferences is { } externalReferences)
                {
                    foreach (var reference in externalReferences.ResourceRefInfoList)
                    {
                        references.Add(new ResourceReference(reference.Name, ResourceReferenceKind.External, ResourceType.Unknown, null, reference.Id));
                    }
                }

                if (DataReferrerTypes.Contains(entry.TypeName))
                {
                    references.AddRange(ReadDataNames(resource));
                }

                return references;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static IReadOnlyList<ResourceReference> ReadDataNames(Resource resource)
        {
            var reader = resource.Reader!;
            var data = resource.UnparsedBlocks.Find(static block => block.Type == BlockType.DATA);

            if (data == null || data.Size < 4)
            {
                return [];
            }

            reader.BaseStream.Position = data.Offset;

            if (!BinaryKV3.IsBinaryKV3(reader.ReadUInt32()))
            {
                return [];
            }

            return ResourceReferenceCollector.CollectDataNames(BinaryKV3.ReadStringTable(reader, resource, data.Offset, data.Size));
        }

        /// <summary>
        /// A read only, buffered stream over the bytes of one entry in an archive file, which can be pointed at
        /// the next entry of the same archive.
        /// </summary>
        private sealed class ArchiveEntryStream(SafeFileHandle handle) : Stream
        {
            private readonly byte[] buffer = new byte[BufferSize];
            private long start;
            private long length;
            private long position;
            private long bufferPosition;
            private int bufferLength;

            public void Open(PackageEntry entry)
            {
                start = entry.Offset;
                length = entry.TotalLength;
                position = 0;
                bufferLength = 0;
            }

            public override bool CanRead => true;

            public override bool CanSeek => true;

            public override bool CanWrite => false;

            public override long Length => length;

            public override long Position
            {
                get => position;
                set => position = value;
            }

            public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

            public override int Read(Span<byte> destination)
            {
                var count = (int)Math.Clamp(length - position, 0, destination.Length);

                if (count == 0)
                {
                    return 0;
                }

                if (count >= BufferSize)
                {
                    count = RandomAccess.Read(handle, destination[..count], start + position);
                    position += count;
                    return count;
                }

                if (position < bufferPosition || position + count > bufferPosition + bufferLength)
                {
                    bufferPosition = position;
                    bufferLength = RandomAccess.Read(handle, buffer.AsSpan(0, (int)Math.Min(BufferSize, length - position)), start + position);
                    count = Math.Min(count, bufferLength);
                }

                buffer.AsSpan((int)(position - bufferPosition), count).CopyTo(destination);
                position += count;
                return count;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                position = origin switch
                {
                    SeekOrigin.Begin => offset,
                    SeekOrigin.Current => position + offset,
                    _ => length + offset,
                };

                return position;
            }

            public override void Flush()
            {
            }

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private static string Normalize(string name)
        {
            var normalized = name.Replace('\\', '/');

            if (normalized.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
            {
                normalized = normalized[..^GameFileLoader.CompiledFileSuffix.Length];
            }

            return normalized;
        }
    }
}
