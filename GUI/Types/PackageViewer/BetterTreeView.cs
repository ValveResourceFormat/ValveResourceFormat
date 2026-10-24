using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValvePak;

namespace GUI.Types.PackageViewer
{
    /// <summary>
    /// Represents a TreeView of the folders in a package.
    /// </summary>
    partial class BetterTreeView : TreeViewDoubleBuffered
    {
        public VirtualPackageNode? Root;
        public Dictionary<string, int> ExtensionIconList = [];
        public int FolderImage;

        public VrfGuiContext? VrfGuiContext { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
        }

        public void GenerateIconList(IEnumerable<string> extensions)
        {
            var defaultImage = AppIcons.Icons["File"];

            foreach (var originalExtension in extensions)
            {
                var image = AppIcons.GetImageIndexForExtension(originalExtension.ToLowerInvariant());

                if (image == defaultImage)
                {
                    continue;
                }

                ExtensionIconList.TryAdd(originalExtension, image);
            }

            FolderImage = AppIcons.Icons["Folder"];
        }

        public static VirtualPackageNode AddFolderNode(VirtualPackageNode currentNode, string directory, uint size)
        {
            var directorySpan = directory.AsSpan();

            foreach (var subPathRange in directorySpan.Split([Package.DirectorySeparatorChar]))
            {
                var subPath = directorySpan[subPathRange];

                if (!currentNode.Folders.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(subPath, out var subNode))
                {
                    var subPathString = subPath.ToString();
                    var toAdd = new VirtualPackageNode(subPathString, size, currentNode);
                    currentNode.Folders.Add(subPathString, toAdd);
                    currentNode = toAdd;
                }
                else
                {
                    currentNode = subNode;
                    currentNode.TotalSize += size;
                }
            }

            return currentNode;
        }

        public static VirtualPackageNode AddFileNode(VirtualPackageNode currentNode, PackageEntry file)
        {
            var directory = file.GetDirectoryPath();

            if (directory.Length > 0)
            {
                currentNode = AddFolderNode(currentNode, directory, file.TotalLength);
            }

            currentNode.Files.Add(file);

            return currentNode;
        }
    }
}
