using System.Linq;
using System.Windows.Forms;
using GUI.Types.PackageViewer;
using ValvePak;

namespace GUI.Types.Exporter
{
    /// <summary>
    /// Reads what the package viewer's context menu was opened on, so type specific export options can be
    /// offered only when the selection holds files they apply to.
    /// </summary>
    static class ContextMenuSelection
    {
        public static List<IBetterBaseItem> GetSelectedItems(Control? owner) => owner switch
        {
            BetterTreeView { SelectedNode: IBetterBaseItem node } => [node],
            BetterListView listView => [.. listView.GetSelectedVirtualItems().OfType<IBetterBaseItem>()],
            _ => [],
        };

        /// <summary>
        /// Whether any selected file, or any file inside a selected folder, has the given compiled type (e.g. "vmat_c").
        /// </summary>
        public static bool ContainsFileType(List<IBetterBaseItem> items, string typeName)
        {
            foreach (var item in items)
            {
                if (item.PackageEntry != null)
                {
                    if (item.PackageEntry.TypeName == typeName)
                    {
                        return true;
                    }
                }
                else if (item.PkgNode != null && ContainsFileType(item.PkgNode, typeName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Collects the selected files of the given compiled type, recursing into selected folders.
        /// </summary>
        public static List<PackageEntry> CollectFiles(List<IBetterBaseItem> items, string typeName)
        {
            var results = new List<PackageEntry>();

            foreach (var item in items)
            {
                if (item.PackageEntry != null)
                {
                    if (item.PackageEntry.TypeName == typeName)
                    {
                        results.Add(item.PackageEntry);
                    }
                }
                else if (item.PkgNode != null)
                {
                    CollectFiles(item.PkgNode, typeName, results);
                }
            }

            return results;
        }

        private static bool ContainsFileType(VirtualPackageNode node, string typeName)
        {
            foreach (var file in node.Files)
            {
                if (file.TypeName == typeName)
                {
                    return true;
                }
            }

            foreach (var folder in node.Folders.Values)
            {
                if (ContainsFileType(folder, typeName))
                {
                    return true;
                }
            }

            return false;
        }

        private static void CollectFiles(VirtualPackageNode node, string typeName, List<PackageEntry> results)
        {
            foreach (var folder in node.Folders.Values)
            {
                CollectFiles(folder, typeName, results);
            }

            foreach (var file in node.Files)
            {
                if (file.TypeName == typeName)
                {
                    results.Add(file);
                }
            }
        }
    }
}
