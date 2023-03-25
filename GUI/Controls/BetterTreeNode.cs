using System;
using System.Windows.Forms;
using SteamDatabase.ValvePak;

namespace GUI.Controls
{
    /// <summary>
    /// Wrapper class to store info about the contents of a <see cref="TreeNode"/> in a way that can be accessed rapidly
    /// </summary>
#pragma warning disable CA2237 // Mark ISerializable types with SerializableAttribute
    public sealed class BetterTreeNode : TreeNode
#pragma warning restore CA2237
    {
        /// <summary>
        /// True if this node represents a directory in the tree view
        /// </summary>
        public bool IsFolder => ItemCount >= 0;

        /// <summary>
        /// If this is a directory, the number of files in the directory. Otherwise 0.
        /// </summary>
        public int ItemCount { get; set; } = -1;

        /// <summary>
        /// If this is a file, the <see cref="PackageEntry"/> representing the file. Otherwise null.
        /// </summary>
        public PackageEntry PackageEntry { get; }

        /// <summary>
        /// Create a new instance of <see cref="VrfTreeViewData"/> for a file
        /// </summary>
        public BetterTreeNode(string text, PackageEntry entry)
            : base(text)
        {
            PackageEntry = entry;
        }

        /// <summary>
        /// Create a new instance of <see cref="VrfTreeViewData"/> for a directory
        /// </summary>
        public BetterTreeNode(string text, int count)
            : base(text)
        {
            ItemCount = count;
        }
    }
}
