using System.Buffers;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat.IO;

namespace GUI.Types.PackageViewer
{
    /// <summary>
    /// Represents a TreeView with the ability to have its contents searched.
    /// </summary>
    partial class BetterTreeView : TreeViewDoubleBuffered
    {
        public VirtualPackageNode? Root;
        public Dictionary<string, int> ExtensionIconList = [];
        public int FolderImage;

        public VrfGuiContext? VrfGuiContext { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
        }

        /// <summary>
        /// Performs a depth-first search on the tree's nodes in search of the passed value. The matching conditions are based on the passed search type parameter.
        /// </summary>
        /// <param name="value">Value to search for in the TreeView. Matching on this value is based on the search type.</param>
        /// <param name="searchType">Determines the matching of the value. For example, full/partial text search or full path search.</param>
        /// <returns>A collection of package entries that match the conditions based on the search type.</returns>
        public List<PackageEntry> Search(string value, SearchType searchType)
        {
            var results = new List<PackageEntry>();

            if (Root == null)
            {
                return results;
            }

            if (searchType == SearchType.FileNamePartialMatch || searchType == SearchType.FullPath)
            {
                value = value.Replace('\\', Package.DirectorySeparatorChar);
            }

            // If only file name search is selected, but entered text contains a slash, search full path
            if (searchType == SearchType.FileNamePartialMatch && value.Contains(Package.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                searchType = SearchType.FullPath;
            }

            if (searchType == SearchType.FileNameExactMatch)
            {
                bool MatchFunction(PackageEntry entry) => entry.GetFileName().Equals(value, StringComparison.OrdinalIgnoreCase);
                Search(Root, results, MatchFunction);
            }
            else if (searchType == SearchType.FileNamePartialMatch)
            {
                bool MatchFunction(PackageEntry entry) => entry.GetFileName().Contains(value, StringComparison.OrdinalIgnoreCase);
                Search(Root, results, MatchFunction);
            }
            else if (searchType == SearchType.FullPath)
            {
                bool MatchFunction(PackageEntry entry) => entry.GetFullPath().Contains(value, StringComparison.OrdinalIgnoreCase);
                Search(Root, results, MatchFunction);
            }
            else if (searchType == SearchType.Regex)
            {
                var regex = new Regex(value, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

                bool MatchFunction(PackageEntry entry) => regex.IsMatch(entry.GetFileName());
                Search(Root, results, MatchFunction);
            }
            else if (searchType == SearchType.FileContents)
            {
                SearchFileContents(results, Encoding.UTF8.GetBytes(value));
            }
            else if (searchType == SearchType.FileContentsHex)
            {
                // TODO: Optimize this
                value = value.Replace(" ", "", StringComparison.Ordinal);

                var bytes = Enumerable.Range(0, value.Length)
                     .Where(x => x % 2 == 0)
                     .Select(x => Convert.ToByte(value.Substring(x, 2), 16))
                     .ToArray();

                SearchFileContents(results, bytes);
            }

            return results;
        }

        /// <summary>
        /// Performs a depth-first search on the virtual package node tree, adding entries for which the match function returns true. The matching conditions are based on the passed function.
        /// </summary>
        /// <param name="node">Node whose subtree is searched.</param>
        /// <param name="results">List that receives the matching entries.</param>
        /// <param name="matchFunction">Function which performs matching on each PackageEntry. Returns true if there's a match.</param>
        private static void Search(VirtualPackageNode node, List<PackageEntry> results, Func<PackageEntry, bool> matchFunction)
        {
            foreach (var entry in node.Files)
            {
                if (matchFunction(entry))
                {
                    results.Add(entry);
                }
            }

            foreach (var folder in node.Folders)
            {
                Search(folder.Value, results, matchFunction);
            }
        }

        private void SearchFileContents(List<PackageEntry> results, byte[] pattern)
        {
            if (pattern.Length < 3)
            {
                throw new ArgumentException("Search input is too short.", nameof(pattern));
            }

            if (VrfGuiContext?.CurrentPackage is not { } package)
            {
                return;
            }

            using var progressDialog = new GenericProgressForm
            {
                Text = "Searching file contents…",
                WaitForWorkToStop = true,
            };
            progressDialog.OnProcess = cancellationToken =>
            {
                Log.Info(nameof(BetterTreeView), "Pattern search");

                var maxArchiveIndex = -1;
                var sortedEntriesPerArchive = new Dictionary<int, List<PackageEntry>>();
                var unreadableEntries = 0;

                foreach (var entry in package.Entries?.Values.SelectMany(static entries => entries) ?? [])
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (package is NestedPackage && entry.ArchiveIndex != Package.DirectoryArchiveIndex)
                    {
                        unreadableEntries++;
                        continue;
                    }

                    if (entry.SmallData.Length > 0 || entry.ArchiveIndex == Package.DirectoryArchiveIndex)
                    {
                        if (entry.TotalLength == 0)
                        {
                            continue;
                        }

                        package.ReadEntry(entry, out var entryData, validateCrc: false);

                        if (entryData.AsSpan().IndexOf(pattern) >= 0)
                        {
                            results.Add(entry);
                        }

                        continue;
                    }

                    if (entry.ArchiveIndex > maxArchiveIndex)
                    {
                        maxArchiveIndex = entry.ArchiveIndex;
                    }

                    if (entry.Length == 0)
                    {
                        continue;
                    }

                    if (!sortedEntriesPerArchive.TryGetValue(entry.ArchiveIndex, out var archiveEntries))
                    {
                        archiveEntries = [];
                        sortedEntriesPerArchive.Add(entry.ArchiveIndex, archiveEntries);
                    }

                    archiveEntries.Add(entry);
                }

                if (unreadableEntries > 0)
                {
                    Log.Warn(nameof(BetterTreeView), string.Create(CultureInfo.InvariantCulture, $"Not searching {unreadableEntries} files in chunk files of a package inside of another package"));
                }

                foreach (var archiveEntries in sortedEntriesPerArchive.Values)
                {
                    archiveEntries.Sort((a, b) => a.Offset.CompareTo(b.Offset));
                }

                if (maxArchiveIndex > -1)
                {
                    var matches = new HashSet<PackageEntry>();
                    var archivesScanned = 0;

                    Parallel.For(
                        0,
                        maxArchiveIndex + 1,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = 3,
                            CancellationToken = cancellationToken,
                        },
                        archiveIndex =>
                        {
                            // Archives that only hold empty entries are never read
                            if (!sortedEntriesPerArchive.TryGetValue(archiveIndex, out var archiveEntries))
                            {
                                Interlocked.Increment(ref archivesScanned);
                                return;
                            }

                            using var archive = package.OpenArchiveStream((ushort)archiveIndex);
                            var archiveMatches = SearchForContentsInArchive(archive, pattern, archiveEntries, cancellationToken);

                            if (archiveMatches.Count > 0)
                            {
                                lock (matches)
                                {
                                    foreach (var match in archiveMatches)
                                    {
                                        if (matches.Add(match))
                                        {
                                            results.Add(match);
                                        }
                                    }
                                }
                            }

                            var scanned = Interlocked.Increment(ref archivesScanned);
                            progressDialog.SetProgress(string.Create(CultureInfo.InvariantCulture, $"Searched {scanned} vpks out of {maxArchiveIndex + 1}, found {results.Count} matches so far"));
                        }
                    );
                }

                Log.Info(nameof(BetterTreeView), $"Found {results.Count} matches");

                progressDialog.SetProgress($"Found {results.Count} matches");

                return Task.CompletedTask;
            };
            progressDialog.ShowDialog();
        }

        private static HashSet<PackageEntry> SearchForContentsInArchive(Stream archive, byte[] pattern, List<PackageEntry> archiveEntries, CancellationToken cancellationToken)
        {
            const int BufferSize = 4 * 1024 * 1024;

            var matches = new HashSet<PackageEntry>();
            var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(BufferSize, pattern.Length));

            try
            {
                var bufferStart = 0L;
                var bufferLength = 0;
                var position = 0L;
                var entryIndex = 0;

                while (true)
                {
                    var bufferOffset = position - bufferStart;

                    if (bufferOffset < 0 || bufferOffset + pattern.Length > bufferLength)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        archive.Position = position;
                        bufferStart = position;
                        bufferOffset = 0;
                        bufferLength = archive.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

                        if (bufferLength < pattern.Length)
                        {
                            break;
                        }
                    }

                    var match = buffer.AsSpan((int)bufferOffset, bufferLength - (int)bufferOffset).IndexOf(pattern);

                    if (match < 0)
                    {
                        position = bufferStart + bufferLength - pattern.Length + 1;
                        continue;
                    }

                    var start = position + match;

                    while (entryIndex + 1 < archiveEntries.Count && archiveEntries[entryIndex + 1].Offset <= start)
                    {
                        entryIndex++;
                    }

                    var entry = archiveEntries[entryIndex];
                    var entryEnd = (long)entry.Offset + entry.Length;

                    if (entry.Offset > start || start + pattern.Length > entryEnd)
                    {
                        position = start + 1;
                        continue;
                    }

                    matches.Add(entry);
                    position = entryEnd;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return matches;
        }

        public void GenerateIconList(IEnumerable<string> extensions)
        {
            var defaultImage = AppIcons.Icons["File"];

            foreach (var originalExtension in extensions)
            {
                var image = AppIcons.GetImageIndexForExtension(originalExtension.ToLowerInvariant());

                if (image == defaultImage)
                {
                    continue;
                }

                ExtensionIconList.TryAdd(originalExtension, image);
            }

            FolderImage = AppIcons.Icons["Folder"];
        }

        public static VirtualPackageNode AddFolderNode(VirtualPackageNode currentNode, string directory, uint size)
        {
            var directorySpan = directory.AsSpan();

            foreach (var subPathRange in directorySpan.Split([Package.DirectorySeparatorChar]))
            {
                var subPath = directorySpan[subPathRange];

                if (!currentNode.Folders.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(subPath, out var subNode))
                {
                    var subPathString = subPath.ToString();
                    var toAdd = new VirtualPackageNode(subPathString, size, currentNode);
                    currentNode.Folders.Add(subPathString, toAdd);
                    currentNode = toAdd;
                }
                else
                {
                    currentNode = subNode;
                    currentNode.TotalSize += size;
                }
            }

            return currentNode;
        }

        public static VirtualPackageNode AddFileNode(VirtualPackageNode currentNode, PackageEntry file)
        {
            var directory = file.GetDirectoryPath();

            if (directory.Length > 0)
            {
                currentNode = AddFolderNode(currentNode, directory, file.TotalLength);
            }

            currentNode.Files.Add(file);

            return currentNode;
        }
    }
}
