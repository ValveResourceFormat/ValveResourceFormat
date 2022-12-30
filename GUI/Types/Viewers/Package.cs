using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveResourceFormat;

namespace GUI.Types.Viewers
{
    public class Package : IViewer
    {
        internal const string DELETED_FILES_FOLDER = "@@ VRF Deleted Files @@";
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

            vrfGuiContext.CurrentPackage = package;

            // create a TreeView with search capabilities, register its events, and add it to the tab
            var treeViewWithSearch = new TreeViewWithSearchResults(ImageList);
            treeViewWithSearch.InitializeTreeViewFromPackage(vrfGuiContext);
            treeViewWithSearch.TreeNodeMouseDoubleClick += VPK_OpenFile;
            treeViewWithSearch.TreeNodeRightClick += VPK_OnClick;
            treeViewWithSearch.ListViewItemDoubleClick += VPK_OpenFile;
            treeViewWithSearch.ListViewItemRightClick += VPK_OnClick;
            treeViewWithSearch.Disposed += VPK_Disposed;
            tab.Controls.Add(treeViewWithSearch);

            return tab;
        }

        internal static List<PackageEntry> RecoverDeletedFiles(SteamDatabase.ValvePak.Package package)
        {
            var allEntries = package.Entries
                .SelectMany(file => file.Value)
                .OrderBy(file => file.Offset)
                .GroupBy(file => file.ArchiveIndex)
                .OrderBy(x => x.Key)
                .ToDictionary(x => x.Key, x => x.ToList());

            var hiddenIndex = 0;
            var totalSlackSize = 0u;
            var hiddenFiles = new List<PackageEntry>();

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
                            DirectoryName = DELETED_FILES_FOLDER,
                            TypeName = " ",
                            CRC32 = 0,
                            SmallData = Array.Empty<byte>(),
                            ArchiveIndex = archiveIndex,
                            Offset = offset,
                            Length = length,
                        };

                        package.ReadEntry(newEntry, out var bytes, validateCrc: false);
                        var stream = new MemoryStream(bytes);

                        try
                        {
                            var resource = new ValveResourceFormat.Resource();
                            resource.Read(stream, verifyFileSize: false);

                            var fileSize = resource.FullFileSize;

                            if (fileSize != length)
                            {
                                if (fileSize > length)
                                {
                                    throw new InvalidDataException("Resource filesize is bigger than the gap length we found");
                                }

                                newEntry.Length = fileSize;
                                offset += fileSize;
                                scan = true;
                            }

                            if (resource.ResourceType != ResourceType.Unknown)
                            {
                                var type = typeof(ResourceType).GetMember(resource.ResourceType.ToString())[0];
                                newEntry.TypeName = ((ExtensionAttribute)type.GetCustomAttributes(typeof(ExtensionAttribute), false)[0]).Extension;
                                newEntry.TypeName += "_c";
                            }

                            newEntry.DirectoryName += "/" + resource.ResourceType;
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
                        hiddenFiles.Add(newEntry);
                    }
                }

                // TODO: Check nextOffset against archive file size
            }

            Console.WriteLine($"Found {hiddenIndex} deleted files totaling {totalSlackSize.ToFileSizeString()}");

            // TODO: Check for completely unused vpk chunk files

            return hiddenFiles;
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
            var data = (VrfTreeViewData)node.Tag;
            if (!data.IsFolder)
            {
                var parentGuiContext = (VrfGuiContext)node.TreeView.Tag;
                var file = data.PackageEntry;

                var vrfGuiContext = new VrfGuiContext(file.GetFullPath(), parentGuiContext);
                Program.MainForm.OpenFile(vrfGuiContext, file);
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
