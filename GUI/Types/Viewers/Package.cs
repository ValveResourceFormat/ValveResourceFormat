using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace GUI.Types.Viewers
{
    public class Package : IViewer
    {
        public ImageList ImageList { get; set; }

        public static bool IsAccepted(uint magic)
        {
            return magic == SteamDatabase.ValvePak.Package.MAGIC;
        }

        public TabPage Create(VrfGuiContext vrfGuiContext, byte[] input)
        {
            var tab = new TabPage();
            var package = new SteamDatabase.ValvePak.Package();

            if (input != null)
            {
                package.SetFileName(vrfGuiContext.FileName);
                package.Read(new MemoryStream(input));
            }
            else
            {
                package.Read(vrfGuiContext.FileName);
            }

            FindHiddenFiles(package);

            // create a TreeView with search capabilities, register its events, and add it to the tab
            var treeViewWithSearch = new TreeViewWithSearchResults(ImageList);
            treeViewWithSearch.InitializeTreeViewFromPackage(vrfGuiContext.FileName, new TreeViewWithSearchResults.TreeViewPackageTag
            {
                Package = package,
                ParentFileLoader = vrfGuiContext.FileLoader,
            });
            treeViewWithSearch.TreeNodeMouseDoubleClick += VPK_OpenFile;
            treeViewWithSearch.TreeNodeRightClick += VPK_OnClick;
            treeViewWithSearch.ListViewItemDoubleClick += VPK_OpenFile;
            treeViewWithSearch.ListViewItemRightClick += VPK_OnClick;
            treeViewWithSearch.Disposed += VPK_Disposed;
            tab.Controls.Add(treeViewWithSearch);

            return tab;
        }

        private static void FindHiddenFiles(SteamDatabase.ValvePak.Package package)
        {
            var allEntries = package.Entries
                .SelectMany(file => file.Value)
                .OrderBy(file => file.Offset)
                .GroupBy(file => file.ArchiveIndex)
                .OrderBy(x => x.Key)
                .ToDictionary(x => x.Key, x => x.ToList());

            var hiddenIndex = 0;
            var totalSlackSize = 0u;

            // TODO: Skip non-chunked vpks?
            foreach (var (archiveIndex, entries) in allEntries)
            {
                var nextOffset = 0u;

                foreach (var entry in entries)
                {
                    if (entry.Length == 0)
                    {
                        continue;
                    }

                    var offset = nextOffset;
                    nextOffset = entry.Offset + entry.Length;

                    totalSlackSize += entry.Offset - offset;

                    var scan = true;

                    while (scan)
                    {
                        scan = false;

                        if (offset == entry.Offset)
                        {
                            break;
                        }

                        offset = (offset + 16 - 1) & ~(16u - 1); // TODO: Validate this gap

                        var length = entry.Offset - offset;

                        if (length <= 16)
                        {
                            // TODO: Verify what this gap is, seems to be null bytes
                            break;
                        }

                        hiddenIndex++;
                        var newEntry = new PackageEntry
                        {
                            FileName = $"Archive {archiveIndex} File {hiddenIndex}",
                            DirectoryName = "@@ Deleted files",
                            TypeName = " ",
                            CRC32 = 0,
                            SmallData = Array.Empty<byte>(),
                            ArchiveIndex = archiveIndex,
                            Offset = offset,
                            Length = length,
                        };

                        package.ReadEntry(newEntry, out var bytes, false);
                        var stream = new MemoryStream(bytes);

                        try
                        {
                            var res = new ValveResourceFormat.Resource();
                            res.Read(stream);

                            // TODO: Audio files have data past the length
                            if (res.FileSize != length)
                            {
                                if (res.FileSize > length)
                                {
                                    throw new Exception("Resource filesize is bigger than the gap length we found");
                                }

                                newEntry.Length = length;
                                offset += res.FileSize;
                                scan = true;
                            }

                            if (res.ResourceType != ResourceType.Unknown)
                            {
                                var type = typeof(ResourceType).GetMember(res.ResourceType.ToString())[0];
                                newEntry.TypeName = ((ExtensionAttribute)type.GetCustomAttributes(typeof(ExtensionAttribute), false)[0]).Extension;
                                newEntry.TypeName += "_c";
                            }

                            newEntry.DirectoryName += "/" + res.ResourceType;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"File {hiddenIndex} - {ex.Message}");

                            newEntry.FileName += $" ({length} bytes)";
                        }

                        if (!package.Entries.TryGetValue(newEntry.TypeName, out var typeEntries))
                        {
                            typeEntries = new List<PackageEntry>();
                            package.Entries.Add(newEntry.TypeName, typeEntries);
                        }

                        typeEntries.Add(newEntry);
                    }
                }

                // TODO: Check nextOffset against archive file size
            }

            Console.WriteLine($"Found {hiddenIndex} deleted files totaling {totalSlackSize.ToFileSizeString()}");

            // TODO: Check for completely unused vpk chunk files
        }

        private void VPK_Disposed(object sender, EventArgs e)
        {
            if (sender is TreeViewWithSearchResults treeViewWithSearch)
            {
                treeViewWithSearch.TreeNodeMouseDoubleClick -= VPK_OpenFile;
                treeViewWithSearch.TreeNodeRightClick -= VPK_OnClick;
                treeViewWithSearch.ListViewItemDoubleClick -= VPK_OpenFile;
                treeViewWithSearch.ListViewItemRightClick -= VPK_OnClick;
                treeViewWithSearch.Disposed -= VPK_Disposed;
            }
        }

        /// <summary>
        /// Opens a file based on a double clicked list view item. Does nothing if the double clicked item contains a non-TreeNode object.
        /// </summary>
        /// <param name="sender">Object which raised event.</param>
        /// <param name="e">Event data.</param>
        private void VPK_OpenFile(object sender, ListViewItemClickEventArgs e)
        {
            if (e.Tag is TreeNode node)
            {
                OpenFileFromNode(node);
            }
        }

        private void VPK_OpenFile(object sender, TreeNodeMouseClickEventArgs e)
        {
            var node = e.Node;
            OpenFileFromNode(node);
        }

        private static void OpenFileFromNode(TreeNode node)
        {
            //Make sure we aren't a directory!
            if (node.Tag.GetType() == typeof(PackageEntry))
            {
                var package = node.TreeView.Tag as TreeViewWithSearchResults.TreeViewPackageTag;
                var file = node.Tag as PackageEntry;
                package.Package.ReadEntry(file, out var output, validateCrc: file.CRC32 > 0);

                Program.MainForm.OpenFile(file.GetFileName(), output, package);
            }
        }

        private void VPK_OnClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            Program.MainForm.VpkContextMenu.Show(e.Node.TreeView, e.Location);
        }

        /// <summary>
        /// Opens a context menu where the user right-clicked in the ListView.
        /// </summary>
        /// <param name="sender">Object which raised event.</param>
        /// <param name="e">Event data.</param>
        private void VPK_OnClick(object sender, ListViewItemClickEventArgs e)
        {
            if (e.Tag is ListViewItem listViewItem && listViewItem.Tag is TreeNode node)
            {
                node.TreeView.SelectedNode = node; // To stop it spassing out
                Program.MainForm.VpkContextMenu.Show(listViewItem.ListView, e.Location);
            }
        }
    }
}
