using System.Windows.Forms;
using SteamDatabase.ValvePak;

namespace GUI.Types.PackageViewer
{
#pragma warning disable CA2237 // Mark ISerializable types with SerializableAttribute
    public sealed class BetterListViewItem : ListViewItem, IBetterBaseItem
#pragma warning restore CA2237
    {
        /// <summary>
        /// Magic number to identify parent navigation item via Tag property.
        /// </summary>
        public const int ParentNavigationTag = 0x50415245;

        /// <summary>
        /// True if this node represents a directory in the tree view
        /// </summary>
        public bool IsFolder => PackageEntry == null;

        /// <summary>
        /// If this is a file, the <see cref="PackageEntry"/> representing the file. Otherwise null.
        /// </summary>
        public PackageEntry? PackageEntry { get; init; }

        /// <summary>
        /// If this is a folder, the virtual node representing this folder. Otherwise null.
        /// </summary>
        public VirtualPackageNode? PkgNode { get; init; }

        public BetterListViewItem(string text) : base(text)
        {
            //
        }
    }
}
