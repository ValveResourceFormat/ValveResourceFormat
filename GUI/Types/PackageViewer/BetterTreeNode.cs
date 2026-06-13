using System.Windows.Forms;
using SteamDatabase.ValvePak;

namespace GUI.Types.PackageViewer
{
    /// <summary>
    /// Wrapper class to store info about the contents of a <see cref="TreeNode"/> in a way that can be accessed rapidly
    /// </summary>
#pragma warning disable CA2237 // Mark ISerializable types with SerializableAttribute
    public sealed class BetterTreeNode : TreeNode, IBetterBaseItem
#pragma warning restore CA2237
    {
        /// <summary>
        /// True if this node represents a directory in the tree view
        /// </summary>
        public bool IsFolder => PackageEntry == null;

        /// <summary>
        /// If this is a file, the <see cref="PackageEntry"/> representing the file. Otherwise null.
        /// </summary>
        public PackageEntry? PackageEntry { get; }

        /// <summary>
        /// If this is a folder, the virtual node representing this folder. Otherwise null.
        /// </summary>
        public VirtualPackageNode? PkgNode { get; }

        public BetterTreeNode(string text, PackageEntry entry)
            : base(text)
        {
            PackageEntry = entry;
        }

        public BetterTreeNode(string text, VirtualPackageNode node)
            : base(text)
        {
            Name = text; // Only set name for folders, it will be used for indexing
            PkgNode = node;
        }
    }
}
