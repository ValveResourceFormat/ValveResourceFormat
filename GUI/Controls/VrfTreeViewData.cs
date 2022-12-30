using SteamDatabase.ValvePak;

namespace GUI.Controls
{
    /// <summary>
    /// Wrapper class to store info about the contents of a TreeViewNode in a way that can be accessed rapidly
    /// </summary>
    internal sealed class VrfTreeViewData
    {
        /// <summary>
        /// True if this node represents a directory in the tree view
        /// </summary>
        internal bool IsFolder { get; private set; }

        /// <summary>
        /// If this is a directory, the number of files in the directory. Otherwise 0.
        /// </summary>
        internal int ItemCount;

        /// <summary>
        /// If this is a file, the <see cref="PackageEntry"/> representing the file. Otherwise null.
        /// </summary>
        internal PackageEntry PackageEntry;

        /// <summary>
        /// Create a new instance of <see cref="VrfTreeViewData"/> for a directory
        /// </summary>
        /// <param name="count">The initial value of <see cref="ItemCount"/></param>
        internal static VrfTreeViewData MakeFolder(int count)
        {
            return new()
            {
                ItemCount = count,
                IsFolder = true,
            };
        }

        /// <summary>
        /// Create a new instance of <see cref="VrfTreeViewData"/> for a file
        /// </summary>
        /// <param name="entry">The <see cref="PackageEntry"/> that contains information on the file</param>
        internal static VrfTreeViewData MakeFile(PackageEntry entry)
        {
            return new()
            {
                PackageEntry = entry,
                IsFolder = false,
            };
        }
    }
}
