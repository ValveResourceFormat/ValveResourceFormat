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
        // Enough for the header and the block table of any resource
        private const int HeaderSize = 4096;
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

        private static readonly ConcurrentDictionary<string, PackageIndex> PackageIndexes = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Package> packages;
        private PackageIndex[] packageIndexes = [];

        private ResourceReferenceIndex(Dictionary<string, Package> packages)
        {
            this.packages = packages;
        }

        /// <summary>
        /// Returns the index over the packages this context can see, or <c>null</c> when it sees none.
        /// </summary>
        public static ResourceReferenceIndex? ForContext(VrfGuiContext guiContext)
        {
            var packages = CollectPackages(guiContext);

            return packages.Count == 0 ? null : new ResourceReferenceIndex(packages);
        }

        public Task BuildAsync()
        {
            if (packageIndexes.Length == 0)
            {
                packageIndexes = [.. packages.Select(static package => PackageIndexes.GetOrAdd(package.Key, static (_, package) => new PackageIndex(package), package.Value))];
            }

            return Task.WhenAll(packageIndexes.Select(static index => index.BuildAsync()));
        }

        /// <summary>
        /// A file that references the one looked up, and where it stores the reference.
        /// </summary>
        public readonly record struct Referrer(string Name, ResourceReferenceKind Kinds);

        public IReadOnlyList<Referrer> Find(string name)
        {
            var key = Normalize(name);
            var found = new Dictionary<string, ResourceReferenceKind>(StringComparer.OrdinalIgnoreCase);

            foreach (var index in packageIndexes)
            {
                var build = index.BuildAsync();

                if (build.IsCompletedSuccessfully && build.Result.TryGetValue(key, out var referrers))
                {
                    foreach (var referrer in referrers)
                    {
                        found[referrer.Name] = found.GetValueOrDefault(referrer.Name) | referrer.Kinds;
                    }
                }
            }

            return [.. found.Select(static referrer => new Referrer(referrer.Key, referrer.Value)).OrderBy(static referrer => referrer.Name, StringComparer.OrdinalIgnoreCase)];
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

        private sealed class PackageIndex(Package package)
        {
            private readonly Lock buildLock = new();
            private Package? package = package;
            private Task<Dictionary<string, Referrer[]>>? build;

            public Task<Dictionary<string, Referrer[]>> BuildAsync()
            {
                lock (buildLock)
                {
                    if (build == null)
                    {
                        var source = package!;
                        package = null;
                        build = Task.Run(() => Build(source));
                    }

                    return build;
                }
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
                        ? ReadReferences(entry, (offset, destination) => RandomAccess.Read(handle, destination, entry.Offset + offset), entry.TotalLength, buffer)
                        : ReadReferencesThroughPackage(package, entry, buffer);

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
                ArrayPool<byte>.Shared.Return(buffer);
                handle?.Dispose();
            }

            return indexed;
        }

        private delegate void ReadAt(long offset, Span<byte> destination);

        // Entries kept in the directory file, or carrying preloaded bytes, are not a plain slice of an archive
        private static List<ResourceReference>? ReadReferencesThroughPackage(Package package, PackageEntry entry, byte[] buffer)
        {
            try
            {
                using var stream = GameFileLoader.GetPackageEntryStream(package, entry);

                return ReadReferences(entry, (offset, destination) =>
                {
                    stream.Position = offset;
                    stream.ReadExactly(destination);
                }, stream.Length, buffer);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static List<ResourceReference>? ReadReferences(PackageEntry entry, ReadAt read, long length, byte[] buffer)
        {
            try
            {
                if (FullyReadTypes.Contains(entry.TypeName))
                {
                    var bytes = new byte[length];
                    read(0, bytes);

                    using var stream = new MemoryStream(bytes, writable: false);
                    return [.. ResourceReferenceCollector.CollectFromStream(stream, entry.GetFullPath())];
                }

                var headerLength = (int)Math.Min(HeaderSize, length);

                if (headerLength < 16)
                {
                    return null;
                }

                read(0, buffer.AsSpan(0, headerLength));

                ReadOnlySpan<byte> header = buffer.AsSpan(0, headerLength);
                var tableLength = BlockTableLength(header);

                if (tableLength > headerLength && tableLength <= length)
                {
                    var table = new byte[tableLength];
                    read(0, table);
                    header = table;
                }

                if (!TryFindBlock(header, length, BlockType.RERL, out var rerlOffset, out var rerlSize)
                    || !TryFindBlock(header, length, BlockType.DATA, out var dataOffset, out var dataSize))
                {
                    return null;
                }

                var references = new List<ResourceReference>();

                if (rerlSize > 0)
                {
                    var block = ArrayPool<byte>.Shared.Rent((int)rerlSize);

                    try
                    {
                        read(rerlOffset, block.AsSpan(0, (int)rerlSize));

                        foreach (var name in ParseReferences(block.AsSpan(0, (int)rerlSize)))
                        {
                            references.Add(new ResourceReference(name, ResourceReferenceKind.External, ResourceType.Unknown, null, 0));
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(block);
                    }
                }

                if (dataSize >= 4 && DataReferrerTypes.Contains(entry.TypeName))
                {
                    references.AddRange(ReadDataNames(read, dataOffset, dataSize));
                }

                return references;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static IReadOnlyList<ResourceReference> ReadDataNames(ReadAt read, uint offset, uint size)
        {
            var block = ArrayPool<byte>.Shared.Rent((int)size);

            try
            {
                read(offset, block.AsSpan(0, (int)size));

                if (!BinaryKV3.IsBinaryKV3(BitConverter.ToUInt32(block)))
                {
                    return [];
                }

                using var stream = new MemoryStream(block, 0, (int)size, writable: false);
                using var reader = new BinaryReader(stream);
                using var resource = new Resource();

                return ResourceReferenceCollector.CollectDataNames(BinaryKV3.ReadStringTable(reader, resource, 0, size));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(block);
            }
        }

        /// <summary>
        /// Returns how many bytes from the start of the resource cover its block table.
        /// </summary>
        private static int BlockTableLength(ReadOnlySpan<byte> header)
        {
            var blockOffset = BitConverter.ToUInt32(header[8..]);
            var blockCount = BitConverter.ToUInt32(header[12..]);
            var length = 8L + blockOffset + (blockCount * 12L);

            return length > int.MaxValue ? int.MaxValue : (int)length;
        }

        /// <summary>
        /// Finds a block in the block table, returning a size of zero when the resource has none of that type.
        /// </summary>
        private static bool TryFindBlock(ReadOnlySpan<byte> header, long entryLength, BlockType type, out uint offset, out uint size)
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

                if (blockType != type)
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
