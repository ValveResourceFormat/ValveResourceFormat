using System.Drawing;
using SteamDatabase.ValvePak;

namespace GUI.Controls
{
    class PackageContextMenuEventArgs : EventArgs
    {
        public VirtualPackageNode PkgNode { get; init; }
        public PackageEntry PackageEntry { get; init; }
        public Point Location { get; init; }
        public BetterTreeNode TreeNode { get; init; }
    }
}
