using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using GUI.Forms;
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

        protected override void OnPaint(PaintEventArgs pe)
        {
            base.OnPaint(pe);
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

            value = value.ToLower();

            if (searchType == SearchType.FileNameExactMatch)
            {
                results = Nodes.Find(value, true).ToList();
            }
            else if (searchType == SearchType.FileNamePartialMatch)
            {
                Func<TreeNode, string, bool> matchFunction = (node, searchText) => node.Text.Contains(searchText);
                results = Search(value, matchFunction);
            }
            else if (searchType == SearchType.FullPath)
            {
                Func<TreeNode, string, bool> matchFunction = (node, searchText) => node.FullPath.Contains(searchText);
                value = value.Replace('\\', Package.DirectorySeparatorChar);
                results = Search(value, matchFunction);
            }

            return results;
        }

        /// <summary>
        /// Performs a breadth-first-search on the TreeView's nodes in search of the passed value. The matching conditions are based the passed function.
        /// </summary>
        /// <param name="value">Value to search for in the TreeView. Matching on this value is based on the function.</param>
        /// <param name="matchFunction">Function which performs matching on the TreeNode and the passed value. Returns true if there's a match.</param>
        /// <returns>Returns matched nodes.</returns>
        private IReadOnlyCollection<TreeNode> Search(string value, Func<TreeNode, string, bool> matchFunction)
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
                if (matchFunction(currentNode, value))
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

        public void GenerateIconList(IEnumerable<string> extensions)
        {
            ExtensionIconList = new Dictionary<string, string>();

            foreach (var originalExtension in extensions)
            {
                var extension = originalExtension;

                if (extension.EndsWith("_c", StringComparison.Ordinal))
                {
                    extension = extension.Substring(0, extension.Length - 2);
                }

                if (!ImageList.Images.ContainsKey(extension))
                {
                    if (extension.Length > 0 && extension[0] == 'v')
                    {
                        extension = extension.Substring(1);

                        if (!ImageList.Images.ContainsKey(extension))
                        {
                            extension = "_default";
                        }
                    }
                    else
                    {
                        extension = "_default";
                    }
                }

                ExtensionIconList.Add(originalExtension, extension);
            }
        }

        /// <summary>
        /// Adds a node to the tree based on the passed file information. This is useful when building a directory-based tree.
        /// </summary>
        /// <param name="file">File entry.</param>
        public void AddFileNode(PackageEntry file)
        {
            TreeNode currentNode = null;

            if (file.DirectoryName != null)
            {
                var subPaths = file.DirectoryName.Split(Package.DirectorySeparatorChar);

                foreach (var subPath in subPaths)
                {
                    if (currentNode == null)
                    {
                        currentNode = Nodes[subPath] ?? Nodes.Add(subPath, subPath, @"_folder", @"_folder");
                    }
                    else
                    {
                        currentNode = currentNode.Nodes[subPath] ?? currentNode.Nodes.Add(subPath, subPath, @"_folder", @"_folder");
                    }

                    currentNode.Tag = new TreeViewFolder(file.DirectoryName, currentNode.Nodes.Count + 1); //is this enough?
                }
            }

            var fileName = file.GetFileName();
            var ext = ExtensionIconList[file.TypeName];

            if (currentNode == null)
            {
                currentNode = Nodes.Add(fileName, fileName, ext, ext);
            }
            else
            {
                currentNode = currentNode.Nodes.Add(fileName, fileName, ext, ext);
            }

            currentNode.Tag = file; //so we can use it later
        }
    }
}
