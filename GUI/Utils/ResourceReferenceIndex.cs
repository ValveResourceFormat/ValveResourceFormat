using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace GUI.Utils
{
    /// <summary>
    /// Inverts the external reference lists of every compiled file in the mounted packages, so a
    /// resource can list the files that reference it.
    /// </summary>
    /// <remarks>
    /// Entries are read straight out of their archive file rather than through a package entry stream,
    /// because creating a memory mapped view per entry costs more than the parse does and serializes on
    /// the package lock. Dota's content package indexes in 0.4 seconds this way against 6 seconds
    /// through entry streams.
    /// </remarks>
    class ResourceReferenceIndex
    {
        // Enough for the header and the block table of any resource
        private const int HeaderSize = 4096;
        private const ushort EntryInDirectoryFile = 0x7FFF;

        private static readonly ConcurrentDictionary<string, ResourceReferenceIndex> Indexes = new(StringComparer.Ordinal);

        private readonly List<Package> packages;
        private readonly Dictionary<string, List<string>> referrers = new(StringComparer.OrdinalIgnoreCase);
        private Task? build;

        private ResourceReferenceIndex(List<Package> packages)
        {
            this.packages = packages;
        }

        /// <summary>
        /// Returns the index over the packages this context can see, building it at most once per set
        /// of packages.
        /// </summary>
        public static ResourceReferenceIndex? ForContext(VrfGuiContext guiContext)
        {
            var packages = CollectPackages(guiContext);

            if (packages.Count == 0)
            {
                return null;
            }

            var key = string.Join('|', packages.Select(static package => package.FileName));

            return Indexes.GetOrAdd(key, _ => new ResourceReferenceIndex(packages));
        }

        public Task BuildAsync()
        {
            lock (referrers)
            {
                build ??= Task.Run(Build);
                return build;
            }
        }

        public IReadOnlyList<string> Find(string name)
        {
            lock (referrers)
            {
                return referrers.TryGetValue(Normalize(name), out var found) ? found : [];
            }
        }

        private static List<Package> CollectPackages(VrfGuiContext guiContext)
        {
            var packages = new List<Package>();

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
                if (package?.FileName != null && !packages.Contains(package))
                {
                    packages.Add(package);
                }
            }

            return packages;
        }

        private void Build()
        {
            var timer = Stopwatch.StartNew();
            var found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var files = 0;

            foreach (var package in packages)
            {
                files += IndexPackage(package, found);
            }

            foreach (var list in found.Values)
            {
                list.Sort(StringComparer.OrdinalIgnoreCase);
            }

            lock (referrers)
            {
                foreach (var (key, list) in found)
                {
                    referrers[key] = list;
                }
            }

            Log.Debug(nameof(ResourceReferenceIndex), $"Indexed {files} files referencing {found.Count} targets in {timer.Elapsed.TotalSeconds:F2}s");
        }

        private static int IndexPackage(Package package, Dictionary<string, List<string>> found)
        {
            if (package.Entries == null)
            {
                return 0;
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

            var results = new ConcurrentBag<Dictionary<string, List<string>>>();
            var indexed = 0;

            Parallel.ForEach(
                byArchive,
                () => new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
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

            return indexed;
        }

        private static int IndexArchive(Package package, ushort archiveIndex, List<PackageEntry> entries,
            Dictionary<string, List<string>> found)
        {
            var path = archiveIndex == EntryInDirectoryFile ? null : $"{package.FileName}_{archiveIndex:D3}.vpk";
            SafeFileHandle? handle = null;
            var buffer = ArrayPool<byte>.Shared.Rent(HeaderSize);
            var indexed = 0;

            try
            {
                if (path != null && File.Exists(path))
                {
                    handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, FileOptions.RandomAccess);
                }

                foreach (var entry in entries)
                {
                    var references = handle != null && entry.SmallData.Length == 0
                        ? ReadReferences(handle, entry, buffer)
                        : ReadReferencesThroughPackage(package, entry);

                    if (references == null)
                    {
                        continue;
                    }

                    indexed++;

                    if (references.Count == 0)
                    {
                        continue;
                    }

                    var referrer = Normalize(entry.GetFullPath());

                    foreach (var reference in references)
                    {
                        var key = Normalize(reference);

                        if (!found.TryGetValue(key, out var list))
                        {
                            list = [];
                            found.Add(key, list);
                        }

                        list.Add(referrer);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                handle?.Dispose();
            }

            return indexed;
        }

        private static List<string>? ReadReferences(SafeFileHandle handle, PackageEntry entry, byte[] buffer)
        {
            try
            {
                var headerLength = (int)Math.Min(HeaderSize, entry.TotalLength);

                if (headerLength < 16)
                {
                    return null;
                }

                RandomAccess.Read(handle, buffer.AsSpan(0, headerLength), entry.Offset);

                if (!TryFindExternalReferenceBlock(buffer.AsSpan(0, headerLength), entry.TotalLength, out var offset, out var size))
                {
                    return null;
                }

                if (size == 0)
                {
                    return [];
                }

                var block = ArrayPool<byte>.Shared.Rent((int)size);

                try
                {
                    RandomAccess.Read(handle, block.AsSpan(0, (int)size), entry.Offset + offset);
                    return ParseReferences(block.AsSpan(0, (int)size));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(block);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Entries kept in the directory file, or carrying preloaded bytes, are not a plain slice of an archive
        private static List<string>? ReadReferencesThroughPackage(Package package, PackageEntry entry)
        {
            try
            {
                using var stream = GameFileLoader.GetPackageEntryStream(package, entry);
                using var resource = new Resource();
                resource.Read(stream, new ResourceReadOptions
                {
                    IncludeBlocks = [BlockType.RERL],
                    SkipFileSizeVerification = true,
                });

                var references = resource.ExternalReferences;

                return references == null ? [] : references.ResourceRefInfoList.ConvertAll(static reference => reference.Name);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool TryFindExternalReferenceBlock(ReadOnlySpan<byte> header, uint entryLength, out uint offset, out uint size)
        {
            offset = 0;
            size = 0;

            if (BitConverter.ToUInt16(header[4..]) != Resource.KnownHeaderVersion)
            {
                return false;
            }

            var blockOffset = BitConverter.ToUInt32(header[8..]);
            var blockCount = BitConverter.ToUInt32(header[12..]);
            var position = 8 + (int)blockOffset;

            for (var i = 0; i < blockCount; i++)
            {
                if (position + 12 > header.Length)
                {
                    return false;
                }

                var blockType = (BlockType)BitConverter.ToUInt32(header[position..]);
                var blockDataOffset = (uint)(position + 4) + BitConverter.ToUInt32(header[(position + 4)..]);
                var blockSize = BitConverter.ToUInt32(header[(position + 8)..]);
                position += 12;

                if (blockType != BlockType.RERL)
                {
                    continue;
                }

                if (blockDataOffset + blockSize > entryLength)
                {
                    return false;
                }

                offset = blockDataOffset;
                size = blockSize;
                return true;
            }

            return true;
        }

        // Same layout as ResourceExtRefList, read straight off the block bytes
        private static List<string> ParseReferences(ReadOnlySpan<byte> block)
        {
            var offset = BitConverter.ToUInt32(block);
            var count = BitConverter.ToUInt32(block[4..]);

            if (count == 0)
            {
                return [];
            }

            var names = new List<string>((int)count);
            var position = (int)offset;

            for (var i = 0; i < count; i++)
            {
                if (position + 16 > block.Length)
                {
                    break;
                }

                var stringOffset = BitConverter.ToInt32(block[(position + 8)..]);
                var stringStart = position + 8 + stringOffset;

                if (stringStart < 0 || stringStart >= block.Length)
                {
                    break;
                }

                var end = block[stringStart..].IndexOf((byte)0);

                if (end < 0)
                {
                    break;
                }

                names.Add(Encoding.UTF8.GetString(block.Slice(stringStart, end)));
                position += 16;
            }

            return names;
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
