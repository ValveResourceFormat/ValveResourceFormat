using System.Drawing;
using ValvePak;

namespace GUI.Types.PackageViewer
{
    class PackageContextMenuEventArgs : EventArgs
    {
        public VirtualPackageNode? PkgNode { get; init; }
        public PackageEntry? PackageEntry { get; init; }
        public Point Location { get; init; }
        public BetterTreeNode? TreeNode { get; init; }

        /// <summary>
        /// Whether the empty space of the list view was clicked, <see cref="PkgNode"/> is then the folder it shows.
        /// </summary>
        public bool IsBackground { get; init; }
    }
}
