using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Forms;
using ValveResourceFormat;

namespace GUI.Controls
{
    /// <summary>
    /// Represents a TreeView with the ability to have its contents searched.
    /// </summary>
    public partial class BetterTreeView : TreeView
    {
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
        /// <param name="isCaseSensitive">Specify is search is case sensitive.</param>
        /// <param name="searchType">Determines the matching of the value. For example, full/partial text search or full path search.</param>
        /// <returns>A collection of nodes who match the conditions based on the search type.</returns>
        public IReadOnlyCollection<TreeNode> Search(string value, bool isCaseSensitive, SearchType searchType)
        {
            IReadOnlyCollection<TreeNode> results = new List<TreeNode>().AsReadOnly();

            // if the user is not choosing case sensitive, then lower everything so we clear out any case
            if (!isCaseSensitive)
            {
                value = value.ToLower();
            }

            if (searchType == SearchType.FileNameExactMatch)
            {
                results = Nodes.Find(value, true).ToList();
            }
            else if (searchType == SearchType.FileNamePartialMatch)
            {
                Func<TreeNode, string, bool> matchFunction = (node, searchText) =>
                {
                    return node.Text.ToLower().Contains(searchText);
                };
                results = Search(value, matchFunction);
            }
            else if (searchType == SearchType.FullPath)
            {
                Func<TreeNode, string, bool> matchFunction = (node, searchText) =>
                {
                    return node.FullPath.ToLower().Contains(searchText);
                };
                results = Search(value, matchFunction);
            }

            return results;
        }

        /// <summary>
        /// Performs a breadth-first-search on the TreeView's nodes in search of the passed value. The matching conditions are based the passed function.
        /// </summary>
        /// <param name="value">Value to search for in the TreeView. Matching on this value is based on the function.</param>
        /// <param name="matchFunction">Function which performs matching on the TreeNode and the passed value. Returns true if there's a match.</param>
        /// <returns></returns>
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

        /// <summary>
        /// Adds a node to the tree based on the passed file information. This is useful when building a directory-based tree.
        /// </summary>
        /// <param name="file"></param>
        /// <param name="fileType"></param>
        public void AddFileNode(PackageEntry file, KeyValuePair<string, List<PackageEntry>> fileType)
        {
            TreeNode currentNode = null;

            var fileName = string.Format("{0}.{1}", file.FileName, fileType.Key);
            var path = Path.Combine(file.DirectoryName, fileName);
            var subPaths = path.Split(Path.DirectorySeparatorChar);

            foreach (var subPath in subPaths)
            {
                // Root directory
                if (currentNode == null)
                {
                    if (subPath == " ")
                    {
                        continue; //root files
                    }

                    currentNode = Nodes[subPath] ?? Nodes.Add(subPath, subPath);
                }
                else
                {
                    currentNode = currentNode.Nodes[subPath] ?? currentNode.Nodes.Add(subPath, subPath);
                }

                var ext = Path.GetExtension(currentNode.Name);

                if (ext.Length == 0)
                {
                    ext = "_folder";

                    currentNode.Tag = new TreeViewFolder(file.DirectoryName, currentNode.Nodes.Count + 1); //is this enough?
                }
                else
                {
                    currentNode.Tag = file; //so we can use it later

                    ext = ext.Substring(1);

                    if (ext.EndsWith("_c", StringComparison.Ordinal))
                    {
                        ext = ext.Substring(0, ext.Length - 2);
                    }

                    if (!ImageList.Images.ContainsKey(ext))
                    {
                        if (ext[0] == 'v')
                        {
                            ext = ext.Substring(1);

                            if (!ImageList.Images.ContainsKey(ext))
                            {
                                ext = "_default";
                            }
                        }
                        else
                        {
                            ext = "_default";
                        }
                    }
                }

                currentNode.ImageKey = ext;
                currentNode.SelectedImageKey = ext;
            }
        }
    }
}
