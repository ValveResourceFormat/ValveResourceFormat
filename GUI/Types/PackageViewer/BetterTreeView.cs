using System.Buffers;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Utils;
using SteamDatabase.ValvePak;

#nullable disable

namespace GUI.Types.PackageViewer
{
    /// <summary>
    /// Represents a TreeView with the ability to have its contents searched.
    /// </summary>
    partial class BetterTreeView : TreeViewDoubleBuffered
    {
        public VirtualPackageNode Root;
        public Dictionary<string, int> ExtensionIconList;
        public int FolderImage;

        public VrfGuiContext VrfGuiContext { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
        }

        /// <summary>
        /// Performs a breadth-first-search on the TreeView's nodes in search of the passed value. The matching conditions are based on the passed search type parameter.
        /// </summary>
        /// <param name="value">Value to search for in the TreeView. Matching on this value is based on the search type.</param>
        /// <param name="searchType">Determines the matching of the value. For example, full/partial text search or full path search.</param>
        /// <returns>A collection of nodes who match the conditions based on the search type.</returns>
        public List<PackageEntry> Search(string value, SearchType searchType)
        {
            var results = new List<PackageEntry>();

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
        /// Performs a breadth-first-search on the TreeView's nodes in search of the passed value. The matching conditions are based the passed function.
        /// </summary>
        /// <param name="matchFunction">Function which performs matching on the TreeNode. Returns true if there's a match.</param>
        /// <returns>Returns matched nodes.</returns>
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

            if (VrfGuiContext.ParentGuiContext != null)
            {
                throw new InvalidOperationException("Inner paks are not supported.");
            }

            using var progressDialog = new GenericProgressForm
            {
                Text = "Searching file contents…"
            };
            progressDialog.OnProcess += (_, __) =>
            {
                Log.Info(nameof(BetterTreeView), "Pattern search");

                var maxArchiveIndex = -1;
                var sortedEntriesPerArchive = new Dictionary<int, List<PackageEntry>>();

                foreach (var extensions in VrfGuiContext.CurrentPackage.Entries.Values)
                {
                    foreach (var entry in extensions)
                    {
                        if (entry.ArchiveIndex != 0x7FFF && entry.ArchiveIndex > maxArchiveIndex)
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
                }

                foreach (var archiveEntries in sortedEntriesPerArchive.Values)
                {
                    archiveEntries.Sort((a, b) => a.Offset.CompareTo(b.Offset));
                }

                var matches = new HashSet<PackageEntry>();

                if (sortedEntriesPerArchive.TryGetValue(0x7FFF, out var sortedEntriesInDirVpk))
                {
                    var fileName = $"{VrfGuiContext.CurrentPackage.FileName}{(VrfGuiContext.CurrentPackage.IsDirVPK ? "_dir" : "")}.vpk";

                    progressDialog.SetProgress($"Searching '{fileName}'");

                    var archiveMatches = SearchForContentsInFile(fileName, pattern, sortedEntriesInDirVpk);
                    matches.UnionWith(archiveMatches);
                }

                if (maxArchiveIndex > -1)
                {
                    var archivesScanned = 0;

                    Parallel.For(
                        0,
                        maxArchiveIndex,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = 3
                        },
                        archiveIndex =>
                        {
                            var fileName = $"{VrfGuiContext.CurrentPackage.FileName}_{archiveIndex:D3}.vpk";

                            var archiveMatches = SearchForContentsInFile(fileName, pattern, sortedEntriesPerArchive[archiveIndex]);

                            if (archiveMatches.Count > 0)
                            {
                                lock (archiveMatches)
                                {
                                    matches.UnionWith(archiveMatches);
                                }
                            }

                            Interlocked.Increment(ref archivesScanned);
                            progressDialog.SetProgress($"Searched {archivesScanned} vpks out of {maxArchiveIndex}, found {matches.Count} matches so far");
                        }
                    );
                }

                Log.Info(nameof(BetterTreeView), $"Found {matches.Count} matches");

                progressDialog.SetProgress($"Found {matches.Count} matches");

                foreach (var file in matches)
                {
                    results.Add(file);
                }
            };
            progressDialog.ShowDialog();
        }

        private static HashSet<PackageEntry> SearchForContentsInFile(string fileName, byte[] pattern, List<PackageEntry> archiveEntries)
        {
            var matches = new HashSet<PackageEntry>();

            //using var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);
            var data = File.ReadAllBytes(fileName).AsSpan(); // TODO: stream it

            var match = -1;
            var offset = 0;
            var lastEntryId = 0;

            do
            {
                match = data.IndexOf(pattern);

                if (match < 0)
                {
                    break;
                }

                match += pattern.Length;
                offset += match;
                data = data[match..];

                PackageEntry packageEntry = null;

                for (var entryId = lastEntryId; entryId < archiveEntries.Count; entryId++)
                {
                    if (offset >= archiveEntries[entryId].Offset)
                    {
                        lastEntryId = entryId;
                        continue;
                    }

                    break;
                }

                packageEntry = archiveEntries[lastEntryId];

                if (offset <= packageEntry.Offset + packageEntry.Length)
                {
                    matches.Add(packageEntry);
                }
            }
            while (true);

            return matches;
        }

        public void GenerateIconList(IEnumerable<string> extensions)
        {
            var defaultImage = MainForm.ImageListLookup["_default"];

            ExtensionIconList = [];

            foreach (var originalExtension in extensions)
            {
                var image = MainForm.GetImageIndexForExtension(originalExtension.ToLowerInvariant());

                if (image == defaultImage)
                {
                    continue;
                }

                ExtensionIconList.Add(originalExtension, image);
            }

            FolderImage = MainForm.ImageListLookup["_folder"];
        }

        public static VirtualPackageNode AddFolderNode(VirtualPackageNode currentNode, string directory, uint size)
        {
            foreach (var subPathSpan in directory.AsSpan().Split([Package.DirectorySeparatorChar]))
            {
                var subPath = subPathSpan.ToString();

                if (!currentNode.Folders.TryGetValue(subPath, out var subNode))
                {
                    var toAdd = new VirtualPackageNode(subPath, size, currentNode);
                    currentNode.Folders.Add(subPath, toAdd);
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
            if (!string.IsNullOrWhiteSpace(file.DirectoryName))
            {
                currentNode = AddFolderNode(currentNode, file.DirectoryName, file.TotalLength);
            }

            currentNode.Files.Add(file);

            return currentNode;
        }
    }
}
