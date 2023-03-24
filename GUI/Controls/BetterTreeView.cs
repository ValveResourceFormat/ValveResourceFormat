using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Utils;
using SteamDatabase.ValvePak;

namespace GUI.Controls
{
    /// <summary>
    /// Represents a TreeView with the ability to have its contents searched.
    /// </summary>
    public partial class BetterTreeView : TreeView
    {
        private Dictionary<string, string> ExtensionIconList;

        public BetterTreeView()
        {
            InitializeComponent();
        }

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
        public IReadOnlyCollection<TreeNode> Search(string value, SearchType searchType)
        {
            IReadOnlyCollection<TreeNode> results = new List<TreeNode>().AsReadOnly();

            if (searchType != SearchType.FileNameExactMatch && searchType != SearchType.FileContents && searchType != SearchType.FileContentsHex)
            {
                value = value.ToUpperInvariant().Replace('\\', Package.DirectorySeparatorChar);
            }

            // If only file name search is selected, but entered text contains a slash, search full path
            if (searchType == SearchType.FileNamePartialMatch && value.Contains(Package.DirectorySeparatorChar, StringComparison.InvariantCulture))
            {
                searchType = SearchType.FullPath;
            }

            if (searchType == SearchType.FileNameExactMatch)
            {
                results = Nodes.Find(value, true).ToList();
            }
            else if (searchType == SearchType.FileNamePartialMatch)
            {
                bool MatchFunction(TreeNode node) => node.Text.Contains(value, StringComparison.InvariantCultureIgnoreCase);
                results = Search(MatchFunction);
            }
            else if (searchType == SearchType.FullPath)
            {
                bool MatchFunction(TreeNode node) => node.FullPath.Contains(value, StringComparison.InvariantCultureIgnoreCase);
                results = Search(MatchFunction);
            }
            else if (searchType == SearchType.Regex)
            {
                var regex = new Regex(value, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

                bool MatchFunction(TreeNode node) => regex.IsMatch(node.Text);
                results = Search(MatchFunction);
            }
            else if (searchType == SearchType.FileContents)
            {
                results = SearchFileContents(Encoding.UTF8.GetBytes(value));
            }
            else if (searchType == SearchType.FileContentsHex)
            {
                // TODO: Optimize this
                value = value.Replace(" ", "", StringComparison.Ordinal);

                var bytes = Enumerable.Range(0, value.Length)
                     .Where(x => x % 2 == 0)
                     .Select(x => Convert.ToByte(value.Substring(x, 2), 16))
                     .ToArray();

                results = SearchFileContents(bytes);
            }

            return results;
        }

        /// <summary>
        /// Performs a breadth-first-search on the TreeView's nodes in search of the passed value. The matching conditions are based the passed function.
        /// </summary>
        /// <param name="matchFunction">Function which performs matching on the TreeNode. Returns true if there's a match.</param>
        /// <returns>Returns matched nodes.</returns>
        private IReadOnlyCollection<TreeNode> Search(Func<TreeNode, bool> matchFunction)
        {
            var searchQueue = new Queue<TreeNode>();

            // queue up every child of the root to begin the search
            foreach (TreeNode childNode in Nodes)
            {
                searchQueue.Enqueue(childNode);
            }

            var matchedNodes = new List<TreeNode>();

            // while there are items in the queue to search
            while (searchQueue.Count > 0)
            {
                var currentNode = searchQueue.Dequeue();

                // if our match function is true, add the node to our matches
                if (matchFunction(currentNode))
                {
                    matchedNodes.Add(currentNode);
                }

                // if the node being inspected has children, queue them all up
                if (currentNode.Nodes.Count < 1)
                {
                    continue;
                }

                foreach (TreeNode childNode in currentNode.Nodes)
                {
                    searchQueue.Enqueue(childNode);
                }
            }

            return matchedNodes.AsReadOnly();
        }

        private IReadOnlyCollection<TreeNode> SearchFileContents(byte[] pattern)
        {
            var vrfGuiContext = Tag as VrfGuiContext;

            if (pattern.Length < 3)
            {
                throw new Exception("Search input is too short.");
            }

            if (vrfGuiContext.ParentGuiContext != null)
            {
                throw new Exception("Inner paks are not supported.");
            }

            if (!vrfGuiContext.CurrentPackage.IsDirVPK)
            {
                throw new Exception("Implement non dir vpks");
            }

            Console.WriteLine("Pattern search");

            var maxArchiveIndex = -1;
            var sortedEntriesPerArchive = new Dictionary<int, List<PackageEntry>>();

            foreach (var extensions in vrfGuiContext.CurrentPackage.Entries.Values)
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
                        archiveEntries = new();
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

            if (maxArchiveIndex > -1)
            {
                Parallel.For(
                    0,
                    maxArchiveIndex,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = 3
                    },
                    archiveIndex =>
                    {
                        var fileName = $"{vrfGuiContext.CurrentPackage.FileName}_{archiveIndex:D3}.vpk";

                        //using var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);
                        var data = File.ReadAllBytes(fileName).AsSpan(); // TODO: stream it

                        var match = -1;
                        var offset = 0;
                        var lastEntryId = 0;

                        do
                        {
                            match = data.IndexOf(pattern);

                            if (match > -1)
                            {
                                match += pattern.Length;
                                offset += match;
                                data = data[match..];

                                var archiveEntries = sortedEntriesPerArchive[archiveIndex];

                                for (var entryId = lastEntryId; entryId < archiveEntries.Count; entryId++)
                                {
                                    if (match >= archiveEntries[entryId].Offset)
                                    {
                                        lastEntryId = entryId;
                                        continue;
                                    }

                                    break;
                                }

                                // TODO: Validate file length to avoid deleted files in gaps
                                matches.Add(archiveEntries[lastEntryId]);
                            }
                        }
                        while (match != -1);
                    }
                );
            }

            Console.WriteLine($"Found {matches.Count} matches");

            var results = new List<TreeNode>();

            foreach (var file in matches)
            {
                var fileName = file.GetFileName();

                if (!ExtensionIconList.TryGetValue(file.TypeName, out var ext))
                {
                    ext = "_default";
                }

                var newNode = new TreeNode(fileName)
                {
                    Name = fileName,
                    ImageKey = ext,
                    SelectedImageKey = ext,
                    Tag = VrfTreeViewData.MakeFile(file),
                };
                results.Add(newNode);
            }

            return results;
        }

        public void GenerateIconList(IEnumerable<string> extensions)
        {
            ExtensionIconList = new Dictionary<string, string>();

            foreach (var originalExtension in extensions)
            {
                var extension = originalExtension;

                if (extension.EndsWith("_c", StringComparison.Ordinal))
                {
                    extension = extension[0..^2];
                }

                if (!ImageList.Images.ContainsKey(extension))
                {
                    if (extension.Length > 0 && extension[0] == 'v')
                    {
                        extension = extension[1..];

                        if (!ImageList.Images.ContainsKey(extension))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        continue;
                    }
                }

                ExtensionIconList.Add(originalExtension, extension);
            }
        }

        /// <summary>
        /// Adds a node to the tree based on the passed file information. This is useful when building a directory-based tree.
        /// </summary>
        /// <param name="currentNode">Root node.</param>
        /// <param name="file">File entry.</param>
        /// <param name="skipDeletedRootFolder">If true, ignore root folder for recovered deleted files.</param>
        public void AddFileNode(TreeNode currentNode, PackageEntry file, bool skipDeletedRootFolder = false)
        {
            if (!string.IsNullOrWhiteSpace(file.DirectoryName))
            {
                var subPaths = file.DirectoryName.Split(Package.DirectorySeparatorChar);

                foreach (var subPath in subPaths)
                {
                    if (skipDeletedRootFolder && subPath == Types.Viewers.Package.DELETED_FILES_FOLDER)
                    {
                        continue;
                    }

                    var subNode = currentNode.Nodes[subPath];

                    if (subNode == null)
                    {
                        var toAdd = new TreeNode(subPath)
                        {
                            Name = subPath,
                            ImageKey = "_folder",
                            SelectedImageKey = "_folder",
                            Tag = VrfTreeViewData.MakeFolder(1)
                        };
                        currentNode.Nodes.Add(toAdd);
                        currentNode = toAdd;
                    }
                    else
                    {
                        currentNode = subNode;
                        ((VrfTreeViewData)subNode.Tag).ItemCount++;
                    }
                }
            }

            var fileName = file.GetFileName();

            if (!ExtensionIconList.TryGetValue(file.TypeName, out var ext))
            {
                ext = "_default";
            }

            var newNode = new TreeNode(fileName)
            {
                Name = fileName,
                ImageKey = ext,
                SelectedImageKey = ext,
                Tag = VrfTreeViewData.MakeFile(file) //so we can use it later
            };

            currentNode.Nodes.Add(newNode);
            currentNode = newNode;
        }
    }
}
