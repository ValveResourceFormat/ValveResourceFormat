using SteamDatabase.ValvePak;

namespace GUI.Types.PackageViewer
{
    public interface IBetterBaseItem
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
    }
}
