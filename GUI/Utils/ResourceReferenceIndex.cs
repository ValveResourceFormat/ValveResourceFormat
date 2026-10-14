using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace GUI.Utils
{
    /// <summary>
    /// Inverts the external reference lists, and the names some types store in their data, of every
    /// compiled file in the mounted packages, so a resource can list the files that reference it.
    /// </summary>
    /// <remarks>
    /// Each package is indexed once per process and shared by every context that can see it. An index that
    /// fails to build is dropped, so the next context that sees the package builds it again. Entries are
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
        /// Types that name other files in their data or text as well, so they are read in full instead of
        /// only their external reference list.
        /// </summary>
        private static readonly HashSet<string> FullyReadTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "vanim_c", "vanmgrph_c", "vcd_c", "vcompmat_c", "vcss_c", "vdata_c", "vjs_c", "vmat_c", "vmix_c", "vpcf_c",
            "vpulse_c", "vrman_c", "vrr_c", "vsmart_c", "vsndevts_c", "vsndstck_c", "vts_c", "vxml_c",
        };

        private static readonly ConcurrentDictionary<string, LazyIndex> Indexes = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Package> Packages;
        private LazyIndex[] ActiveIndexes = [];
        private Task? Build;

        private ResourceReferenceIndex(Dictionary<string, Package> packages)
        {
            Packages = packages;
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

            return new ResourceReferenceIndex(packages);
        }

        /// <summary>
        /// The number of packages this index searches.
        /// </summary>
        public int SearchedCount => Packages.Count;

        /// <summary>
        /// The number of packages that could not be indexed.
        /// </summary>
        public int FailedCount => ActiveIndexes.Count(static index => index.Failed);

        /// <summary>
        /// Indexes the packages. The task does not fault,
        /// a package that fails to index is counted in <see cref="FailedCount"/>.
        /// </summary>
        public Task BuildAsync() => Build ??= BuildAllAsync();

        private async Task BuildAllAsync()
        {
            var packageIndexes = Packages.Select(static package => Indexes.GetOrAdd(package.Key, static (key, package) => new LazyIndex(key, () => IndexPackage(package)), package.Value)).ToArray();

            ActiveIndexes = packageIndexes;

            await Task.WhenAll(packageIndexes.Select(static index => index.BuildAsync())).ConfigureAwait(false);
        }

        /// <summary>
        /// A file that references the one looked up, and where it stores the reference.
        /// </summary>
        /// <param name="Name">The referencing file.</param>
        /// <param name="Kinds">Where the referencing file stores the reference.</param>
        public readonly record struct Referrer(string Name, ResourceReferenceKind Kinds);

        public IReadOnlyList<Referrer> Find(string name)
        {
            var key = Normalize(name);
            var found = new Dictionary<string, Referrer>(StringComparer.OrdinalIgnoreCase);

            foreach (var index in ActiveIndexes)
            {
                if (index.BuildAsync() is not { IsCompletedSuccessfully: true, Result: { } built } || !built.TryGetValue(key, out var referrers))
                {
                    continue;
                }

                foreach (var referrer in referrers)
                {
                    found[referrer.Name] = found.TryGetValue(referrer.Name, out var existing)
                        ? existing with { Kinds = existing.Kinds | referrer.Kinds }
                        : referrer;
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

        /// <summary>
        /// Builds an index on first use. A build that throws is logged and removed from <see cref="Indexes"/>.
        /// </summary>
        private sealed class LazyIndex(string key, Func<Dictionary<string, Referrer[]>> factory)
        {
            private readonly Lock BuildLock = new();
            private Func<Dictionary<string, Referrer[]>>? Factory = factory;
            private Task<Dictionary<string, Referrer[]>?>? Build;

            public bool Failed => Build is { IsCompletedSuccessfully: true, Result: null };

            public Task<Dictionary<string, Referrer[]>?> BuildAsync()
            {
                lock (BuildLock)
                {
                    if (Build == null)
                    {
                        var source = Factory!;
                        Factory = null;
                        Build = Task.Run(() => Run(source));
                    }

                    return Build;
                }
            }

            private Dictionary<string, Referrer[]>? Run(Func<Dictionary<string, Referrer[]>> source)
            {
                try
                {
                    return source();
                }
                catch (Exception e)
                {
                    Log.Error(nameof(ResourceReferenceIndex), $"Failed to index incoming references of {key}: {e}");
                    Indexes.TryRemove(KeyValuePair.Create(key, this));
                    return null;
                }
            }
        }

        private static Dictionary<string, Referrer[]> IndexPackage(Package package)
        {
            var timer = Stopwatch.StartNew();

            if (package.Entries == null)
            {
                return [];
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

            var referrers = Merge(results);

            Log.Debug(nameof(ResourceReferenceIndex), string.Create(CultureInfo.InvariantCulture,
                $"Indexed {indexed} files in {package.FileName} referencing {referrers.Count} targets in {timer.Elapsed.TotalSeconds:F2}s"));

            return referrers;
        }

        private static Dictionary<string, Referrer[]> Merge(IEnumerable<Dictionary<string, List<Referrer>>> results)
        {
            var found = new Dictionary<string, List<Referrer>>(StringComparer.OrdinalIgnoreCase);

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

            return referrers;
        }

        private static int IndexArchive(Package package, ushort archiveIndex, List<PackageEntry> entries,
            Dictionary<string, List<Referrer>> found)
        {
            var path = archiveIndex == EntryInDirectoryFile ? null : string.Create(CultureInfo.InvariantCulture, $"{package.FileName}_{archiveIndex:D3}.vpk");
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

        private static List<ResourceReference>? ReadReferencesThroughPackage(Package package, PackageEntry entry)
        {
            Stream stream;

            try
            {
                stream = GameFileLoader.GetPackageEntryStream(package, entry);
            }
            catch (Exception e) when (e is IOException or InvalidDataException)
            {
                Log.Debug(nameof(ResourceReferenceIndex), $"Failed to read {entry.GetFullPath()}: {e.Message}");
                return null;
            }

            using (stream)
            {
                return ReadReferences(entry, stream);
            }
        }

        /// <returns>The references, or <c>null</c> when the entry could not be read as a resource.</returns>
        private static List<ResourceReference>? ReadReferences(PackageEntry entry, Stream stream)
        {
            try
            {
                using var resource = new Resource { FileName = entry.GetFullPath() };
                resource.Read(stream, leaveOpen: true, mode: ReadMode.Deferred);

                if (FullyReadTypes.Contains(entry.TypeName))
                {
                    return [.. ResourceReferenceCollector.Collect(resource)];
                }

                var references = new List<ResourceReference>();

                if (resource.ExternalReferences is { } externalReferences)
                {
                    foreach (var reference in externalReferences.ResourceRefInfoList)
                    {
                        references.Add(ResourceReference.Create(reference.Name, ResourceReferenceKind.External, id: reference.Id));
                    }
                }

                return references;
            }
            catch (Exception e) when (e is not ObjectDisposedException)
            {
                Log.Debug(nameof(ResourceReferenceIndex), $"Failed to read references of {entry.GetFullPath()}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// A read only, buffered stream over the bytes of one entry in an archive file, which can be pointed at
        /// the next entry of the same archive.
        /// </summary>
        private sealed class ArchiveEntryStream(SafeFileHandle handle) : Stream
        {
            private readonly byte[] Buffer = new byte[BufferSize];
            private long Start;
            private long EntryLength;
            private long EntryPosition;
            private long BufferPosition;
            private int BufferLength;

            public void Open(PackageEntry entry)
            {
                Start = entry.Offset;
                EntryLength = entry.TotalLength;
                EntryPosition = 0;
                BufferLength = 0;
            }

            public override bool CanRead => true;

            public override bool CanSeek => true;

            public override bool CanWrite => false;

            public override long Length => EntryLength;

            public override long Position
            {
                get => EntryPosition;
                set => EntryPosition = value;
            }

            public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

            public override int Read(Span<byte> destination)
            {
                var count = (int)Math.Clamp(EntryLength - EntryPosition, 0, destination.Length);

                if (count == 0)
                {
                    return 0;
                }

                if (count >= BufferSize)
                {
                    count = RandomAccess.Read(handle, destination[..count], Start + EntryPosition);
                    EntryPosition += count;
                    return count;
                }

                if (EntryPosition < BufferPosition || EntryPosition + count > BufferPosition + BufferLength)
                {
                    BufferPosition = EntryPosition;
                    BufferLength = RandomAccess.Read(handle, Buffer.AsSpan(0, (int)Math.Min(BufferSize, EntryLength - EntryPosition)), Start + EntryPosition);
                    count = Math.Min(count, BufferLength);
                }

                Buffer.AsSpan((int)(EntryPosition - BufferPosition), count).CopyTo(destination);
                EntryPosition += count;
                return count;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                EntryPosition = origin switch
                {
                    SeekOrigin.Begin => offset,
                    SeekOrigin.Current => EntryPosition + offset,
                    _ => EntryLength + offset,
                };

                return EntryPosition;
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
