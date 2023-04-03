using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.Blocks;
using System.Text;

namespace GUI.Types.Viewers
{
    public class Package : IViewer
    {
        internal const string DELETED_FILES_FOLDER = "@@ VRF Deleted Files @@";
        public ImageList ImageList { get; set; }
        private VrfGuiContext VrfGuiContext;

        public static bool IsAccepted(uint magic)
        {
            return magic == SteamDatabase.ValvePak.Package.MAGIC;
        }

        public TabPage Create(VrfGuiContext vrfGuiContext, byte[] input)
        {
            VrfGuiContext = vrfGuiContext;

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
            treeViewWithSearch.TreeNodeRightClick += VPK_OnContextMenu;
            treeViewWithSearch.ListViewItemDoubleClick += VPK_OpenFile;
            treeViewWithSearch.ListViewItemRightClick += VPK_OnContextMenu;
            treeViewWithSearch.Disposed += VPK_Disposed;
            tab.Controls.Add(treeViewWithSearch);

            return tab;
        }

        internal static List<PackageEntry> RecoverDeletedFiles(SteamDatabase.ValvePak.Package package, Action<string> setProgress)
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
            var kv3header = Encoding.ASCII.GetBytes("<!-- kv3 ");
            var previousArchiveIndex = 0;

            foreach (var (archiveIndex, entries) in allEntries)
            {
                if (archiveIndex - previousArchiveIndex > 1)
                {
                    Console.WriteLine($"There is probably an unused {previousArchiveIndex:D3}.vpk");
                }

                previousArchiveIndex = archiveIndex;

                var nextOffset = 0u;

                void FindValidFiles(uint entryOffset, uint entryLength)
                {
                    var offset = nextOffset;
                    nextOffset = entryOffset + entryLength;

                    totalSlackSize += entryOffset - offset;

                    var scan = true;

                    while (scan)
                    {
                        scan = false;

                        if (offset == entryOffset)
                        {
                            break;
                        }

                        offset = (offset + 16 - 1) & ~(16u - 1); // TODO: Validate this gap

                        var length = entryOffset - offset;

                        if (length <= 16)
                        {
                            // TODO: Verify what this gap is, seems to be null bytes
                            break;
                        }

                        hiddenIndex++;
                        var newEntry = new PackageEntry
                        {
                            FileName = $"Archive {archiveIndex:D3} File {hiddenIndex}",
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
                            using var resource = new ValveResourceFormat.Resource();
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

                            string resourceTypeExtensionWithDot = null;

                            if (resource.ResourceType != ResourceType.Unknown)
                            {
                                var type = typeof(ResourceType).GetMember(resource.ResourceType.ToString())[0];
                                var resourceTypeExtension = ((ExtensionAttribute)type.GetCustomAttributes(typeof(ExtensionAttribute), false)[0]).Extension;
                                resourceTypeExtensionWithDot = string.Concat(".", resourceTypeExtension);
                                newEntry.TypeName = string.Concat(resourceTypeExtension, "_c");
                            }

                            string filepath = null;

                            // Use input dependency as the file name if there is one
                            if (resource.EditInfo != null)
                            {
                                if (resource.EditInfo.Structs.TryGetValue(ResourceEditInfo.REDIStruct.InputDependencies, out var inputBlock))
                                {
                                    filepath = RecoverDeletedFilesGetPossiblePath((InputDependencies)inputBlock, resourceTypeExtensionWithDot);
                                }

                                if (filepath == null && resource.EditInfo.Structs.TryGetValue(ResourceEditInfo.REDIStruct.AdditionalInputDependencies, out inputBlock))
                                {
                                    filepath = RecoverDeletedFilesGetPossiblePath((InputDependencies)inputBlock, resourceTypeExtensionWithDot);
                                }

                                // Fix panorama extension
                                if (filepath != null && resourceTypeExtensionWithDot == ".vtxt")
                                {
                                    newEntry.TypeName = string.Concat(Path.GetExtension(filepath)[1..], "_c");
                                }
                            }

                            if (filepath != null)
                            {
                                newEntry.DirectoryName = Path.Join(DELETED_FILES_FOLDER, Path.GetDirectoryName(filepath)).Replace('\\', SteamDatabase.ValvePak.Package.DirectorySeparatorChar);
                                newEntry.FileName = Path.GetFileNameWithoutExtension(filepath);
                            }
                            else
                            {
                                newEntry.DirectoryName += string.Concat(SteamDatabase.ValvePak.Package.DirectorySeparatorChar, resource.ResourceType);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"File {hiddenIndex} - {ex.Message}");

                            newEntry.FileName += $" ({length} bytes)";

                            if (bytes.AsSpan().StartsWith(kv3header))
                            {
                                newEntry.TypeName = "kv3";
                            }
                        }

                        if (!package.Entries.TryGetValue(newEntry.TypeName, out var typeEntries))
                        {
                            typeEntries = new List<PackageEntry>();
                            package.Entries.Add(newEntry.TypeName, typeEntries);
                        }

                        typeEntries.Add(newEntry);
                        hiddenFiles.Add(newEntry);

                        if (hiddenFiles.Count % 100 == 0)
                        {
                            setProgress($"Scanning for deleted files, this may take a while... Found {hiddenFiles.Count} files ({totalSlackSize.ToFileSizeString()}) so far...");
                        }
                    }
                }

                // Recover files in gaps between entries
                foreach (var entry in entries)
                {
                    if (entry.Length == 0)
                    {
                        continue;
                    }

                    FindValidFiles(entry.Offset, entry.Length);
                }

                // Recover files in archives after last possible entry for that archive
                if (archiveIndex != short.MaxValue)
                {
                    var archiveFileSize = nextOffset;

                    try
                    {
                        archiveFileSize = (uint)new FileInfo($"{package.FileName}_{archiveIndex:D3}.vpk").Length;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }

                    if (archiveFileSize != nextOffset)
                    {
                        FindValidFiles(archiveFileSize, 0);
                    }
                }
            }

            Console.WriteLine($"Found {hiddenIndex} deleted files totaling {totalSlackSize.ToFileSizeString()}");

            return hiddenFiles;
        }

        private static string RecoverDeletedFilesGetPossiblePath(InputDependencies inputDeps, string resourceTypeExtensionWithDot)
        {
            if (inputDeps.List.Count == 0)
            {
                return null;
            }

            foreach (var inputDependency in inputDeps.List)
            {
                if (Path.GetExtension(inputDependency.ContentRelativeFilename) == resourceTypeExtensionWithDot)
                {
                    return inputDependency.ContentRelativeFilename;
                }
            }

            // We can't detect correct panorama file type from compiler information, so we have to guess
            if (resourceTypeExtensionWithDot == ".vtxt")
            {
                var preferredExtensions = new string[]
                {
                    ".vcss",
                    ".vxml",
                    ".vpdi",
                    ".vjs",
                    ".vts",
                };

                foreach (var inputDependency in inputDeps.List)
                {
                    if (preferredExtensions.Contains(Path.GetExtension(inputDependency.ContentRelativeFilename)))
                    {
                        return inputDependency.ContentRelativeFilename;
                    }
                }
            }

            return inputDeps.List[0].ContentRelativeFilename;
        }

        private void VPK_Disposed(object sender, EventArgs e)
        {
            if (sender is TreeViewWithSearchResults treeViewWithSearch)
            {
                treeViewWithSearch.TreeNodeMouseDoubleClick -= VPK_OpenFile;
                treeViewWithSearch.TreeNodeRightClick -= VPK_OnContextMenu;
                treeViewWithSearch.ListViewItemDoubleClick -= VPK_OpenFile;
                treeViewWithSearch.ListViewItemRightClick -= VPK_OnContextMenu;
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
            if (e.Node is not BetterTreeNode node)
            {
                throw new Exception("Unexpected tree node type");
            }

            OpenFileFromNode(node);
        }

        private void VPK_OpenFile(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node is not BetterTreeNode node)
            {
                throw new Exception("Unexpected tree node type");
            }

            OpenFileFromNode(node);
        }

        private void OpenFileFromNode(BetterTreeNode node)
        {
            //Make sure we aren't a directory!
            if (!node.IsFolder)
            {
                var file = node.PackageEntry;
                var vrfGuiContext = new VrfGuiContext(file.GetFullPath(), VrfGuiContext);
                Program.MainForm.OpenFile(vrfGuiContext, file);
            }
        }

        private void VPK_OnContextMenu(object sender, TreeNodeMouseClickEventArgs e)
        {
            Program.MainForm.VpkContextMenu.Show(e.Node.TreeView, e.Location);
        }

        /// <summary>
        /// Opens a context menu where the user right-clicked in the ListView.
        /// </summary>
        /// <param name="sender">Object which raised event.</param>
        /// <param name="e">Event data.</param>
        private void VPK_OnContextMenu(object sender, ListViewItemClickEventArgs e)
        {
            if (e.Node is ListViewItem listViewItem && listViewItem.Tag is TreeNode node)
            {
                if (node.TreeView != null)
                {
                    // Select the node in tree view when right clicking.
                    // It can be null when right clicking an item from file contents search
                    node.TreeView.SelectedNode = node;
                }

                Program.MainForm.VpkContextMenu.Show(listViewItem.ListView, e.Location);
            }
        }
    }
}
