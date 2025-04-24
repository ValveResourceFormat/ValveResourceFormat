using System.Diagnostics;
using SteamDatabase.ValvePak;

namespace GUI.Types.PackageViewer
{
    [DebuggerDisplay("{Name,nq} (created={CreatedNode is not null})")]
    public class VirtualPackageNode(string name, uint size, VirtualPackageNode parent)
    {
        /// <summary>
        /// Virtual node was converted to real nodes.
        /// </summary>
        public BetterTreeNode? CreatedNode { get; set; }

        /// <summary>
        /// Folder name.
        /// </summary>
        public string Name { get; } = name;

        /// <summary>
        /// Summed up size of all the files in this dictionary (recursively).
        /// </summary>
        public long TotalSize { get; set; } = size;

        /// <summary>
        /// Parent folder.
        /// </summary>
        public VirtualPackageNode Parent { get; } = parent;

        /// <summary>
        /// Folders in this folder.
        /// </summary>
        public Dictionary<string, VirtualPackageNode> Folders { get; } = [];

        /// <summary>
        /// Files in this folder.
        /// </summary>
        public List<PackageEntry> Files { get; } = [];
    }
}
