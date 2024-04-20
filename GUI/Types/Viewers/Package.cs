using System.Buffers;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.IO;

namespace GUI.Types.Viewers
{
#pragma warning disable CA1001 // TreeView is not owned by this class, set to null in VPK_Disposed
    class Package : IViewer
#pragma warning restore CA1001
    {
        private VrfGuiContext VrfGuiContext;
        private TreeViewWithSearchResults TreeView;
        private BetterTreeNode LastContextTreeNode;
        private bool IsEditingPackage; // TODO: Allow editing existing vpks (but not chunked ones)

        public static bool IsAccepted(uint magic)
        {
            return magic == SteamDatabase.ValvePak.Package.MAGIC;
        }

        public Control CreateEmpty(VrfGuiContext vrfGuiContext)
        {
            VrfGuiContext = vrfGuiContext;
            IsEditingPackage = true;

            var package = new SteamDatabase.ValvePak.Package();
            package.AddFile("README.txt", []); // TODO: Otherwise package.Entries is null

            vrfGuiContext.CurrentPackage = package;

            CreateTreeViewWithSearchResults();

            return TreeView;
        }

        public TabPage Create(VrfGuiContext vrfGuiContext, Stream stream)
        {
            VrfGuiContext = vrfGuiContext;

            var tab = new TabPage();
            var package = new SteamDatabase.ValvePak.Package();
            package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);

            if (stream != null)
            {
                package.SetFileName(vrfGuiContext.FileName);
                package.Read(stream);
            }
            else
            {
                package.Read(vrfGuiContext.FileName);
            }

            vrfGuiContext.CurrentPackage = package;

            CreateTreeViewWithSearchResults();
            tab.Controls.Add(TreeView);

            return tab;
        }

        private void CreateTreeViewWithSearchResults()
        {
            // create a TreeView with search capabilities, register its events, and add it to the tab
            TreeView = new TreeViewWithSearchResults(this);
            TreeView.InitializeTreeViewFromPackage(VrfGuiContext);
            TreeView.TreeNodeMouseDoubleClick += VPK_OpenFile;
            TreeView.TreeNodeRightClick += VPK_OnContextMenu;
            TreeView.ListViewItemDoubleClick += VPK_OpenFile;
            TreeView.ListViewItemRightClick += VPK_OnContextMenu;
            TreeView.Disposed += VPK_Disposed;
        }

        public void AddFolder(string directory)
        {
            var prefix = GetCurrentPrefix();

            if (prefix.Length > 0)
            {
                directory = Path.Join(prefix, directory);
            }

            directory = directory.Replace('\\', SteamDatabase.ValvePak.Package.DirectorySeparatorChar);

            TreeView.AddFolderNode(directory);
        }

        public void AddFilesFromFolder(string inputDirectory)
        {
            var files = new FileSystemEnumerable<string>(
                inputDirectory,
                (ref FileSystemEntry entry) => entry.ToSpecifiedFullPath(),
                new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                }
            );

            AddFiles(files, inputDirectory);
        }

        public void AddFiles(IEnumerable<string> files, string inputDirectory = null)
        {
            var prefix = GetCurrentPrefix();

            Cursor.Current = Cursors.WaitCursor;

            TreeView.BeginUpdate();

            var resourceEntries = new List<PackageEntry>();

            // TODO: This is not adding to the selected folder, but to root
            foreach (var file in files)
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                var name = inputDirectory == null ? Path.GetFileName(file) : file[(inputDirectory.Length + 1)..];
                var data = File.ReadAllBytes(file);

                if (prefix.Length > 0)
                {
                    name = Path.Join(prefix, name);
                }

                var entry = VrfGuiContext.CurrentPackage.AddFile(name, data);
                TreeView.AddFileNode(entry);

                if (data.Length >= 6)
                {
                    var magicResourceVersion = BitConverter.ToUInt16(data, 4);

                    if (Resource.IsAccepted(magicResourceVersion) && name.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
                    {
                        resourceEntries.Add(entry);
                    }
                }
            }

            TreeView.EndUpdate();

            Cursor.Current = Cursors.Default;

            if (resourceEntries.Count > 0 && MessageBox.Show(
                "Would you like to scan and all dependencies of the compiled file (ending in \"_c\") you just added?",
                "Detected a compiled resource",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes)
            {
                MessageBox.Show("TODO :)");
            }
        }

        public void RemoveCurrentFiles() => RemoveRecursiveFiles(LastContextTreeNode);

        public void RemoveRecursiveFiles(BetterTreeNode node)
        {
            for (var i = node.Nodes.Count - 1; i >= 0; i--)
            {
                RemoveRecursiveFiles((BetterTreeNode)node.Nodes[i]);
            }

            if (node.PackageEntry != null)
            {
                VrfGuiContext.CurrentPackage.RemoveFile(node.PackageEntry);
            }

            if (node.Level > 0)
            {
                node.Remove();
            }
        }

        public void SaveToFile(string fileName)
        {
            VrfGuiContext.CurrentPackage.Write(fileName);

            var fileCount = 0;
            var fileSize = 0u;

            foreach (var fileType in VrfGuiContext.CurrentPackage.Entries)
            {
                foreach (var file in fileType.Value)
                {
                    fileCount++;
                    fileSize += file.TotalLength;
                }
            }

            var result = $"Created {Path.GetFileName(fileName)} with {fileCount} files of size {HumanReadableByteSizeFormatter.Format(fileSize)}.";

            Log.Info(nameof(Package), result);

            MessageBox.Show(
                result,
                "VPK created",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
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
                    Log.Warn(nameof(Package), $"There is probably an unused {previousArchiveIndex:D3}.vpk");
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
                            DirectoryName = "Undetected filenames",
                            TypeName = " ",
                            CRC32 = 0,
                            SmallData = [],
                            ArchiveIndex = archiveIndex,
                            Offset = offset,
                            Length = length,
                        };

                        var bytes = ArrayPool<byte>.Shared.Rent((int)newEntry.TotalLength);

                        try
                        {
                            package.ReadEntry(newEntry, bytes, validateCrc: false);
                            using var stream = new MemoryStream(bytes);
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
                                var resourceTypeExtension = resource.ResourceType.GetExtension();
                                resourceTypeExtensionWithDot = string.Concat(".", resourceTypeExtension);
                                newEntry.TypeName = string.Concat(resourceTypeExtension, GameFileLoader.CompiledFileSuffix);
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
                                    newEntry.TypeName = string.Concat(Path.GetExtension(filepath)[1..], GameFileLoader.CompiledFileSuffix);
                                }
                            }

                            if (filepath != null)
                            {
                                newEntry.DirectoryName = Path.GetDirectoryName(filepath).Replace('\\', SteamDatabase.ValvePak.Package.DirectorySeparatorChar);
                                newEntry.FileName = Path.GetFileNameWithoutExtension(filepath);
                            }
                            else
                            {
                                newEntry.DirectoryName += string.Concat(SteamDatabase.ValvePak.Package.DirectorySeparatorChar, resource.ResourceType);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(nameof(Package), $"File {hiddenIndex} - {ex.Message}");

                            newEntry.FileName += $" ({length} bytes)";

                            var span = bytes.AsSpan();

                            if (span.StartsWith(kv3header))
                            {
                                newEntry.TypeName = "kv3";
                            }
                            else if (!span.Contains((byte)0))
                            {
                                newEntry.TypeName = "txt";
                            }
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(bytes);
                        }

                        if (!package.Entries.TryGetValue(newEntry.TypeName, out var typeEntries))
                        {
                            typeEntries = [];
                            package.Entries.Add(newEntry.TypeName, typeEntries);
                        }

                        typeEntries.Add(newEntry);
                        hiddenFiles.Add(newEntry);

                        if (hiddenFiles.Count % 100 == 0)
                        {
                            setProgress($"Scanning for deleted files, this may take a while… Found {hiddenFiles.Count} files ({HumanReadableByteSizeFormatter.Format(totalSlackSize)}) so far…");
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
                        Log.Debug(nameof(Package), e.Message);
                    }

                    if (archiveFileSize != nextOffset)
                    {
                        FindValidFiles(archiveFileSize, 0);
                    }
                }
            }

            Log.Info(nameof(Package), $"Found {hiddenIndex} deleted files totaling {HumanReadableByteSizeFormatter.Format(totalSlackSize)}");

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
                TreeView = null;
                LastContextTreeNode = null;
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
                throw new ArgumentException("Unexpected tree node type", nameof(e));
            }

            OpenFileFromNode(node);
        }

        private void VPK_OpenFile(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node is not BetterTreeNode node)
            {
                throw new ArgumentException("Unexpected tree node type", nameof(e));
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

        private string GetCurrentPrefix()
        {
            var prefix = string.Empty;
            TreeNode parent = LastContextTreeNode;

            while (parent != null && parent.Level > 0)
            {
                prefix = Path.Join(parent.Name, prefix);
                parent = parent.Parent;
            }

            return prefix;
        }

        private void VPK_OnContextMenu(object sender, TreeNodeMouseClickEventArgs e)
        {
            var isRoot = e.Node is BetterTreeNode node && node.Level == 0 && node.Name == "root";

            if (IsEditingPackage)
            {
                var treeNode = e.Node as BetterTreeNode;

                LastContextTreeNode = treeNode;

                Program.MainForm.ShowVpkEditingContextMenu(e.Node.TreeView, e.Location, isRoot, treeNode.IsFolder);
                return;
            }

            Program.MainForm.ShowVpkContextMenu(e.Node.TreeView, e.Location, isRoot);
        }

        /// <summary>
        /// Opens a context menu where the user right-clicked in the ListView.
        /// </summary>
        /// <param name="sender">Object which raised event.</param>
        /// <param name="e">Event data.</param>
        private void VPK_OnContextMenu(object sender, ListViewItemClickEventArgs e)
        {
            if (IsEditingPackage)
            {
                return;
            }

            if (e.Node is ListViewItem listViewItem && listViewItem.Tag is TreeNode node)
            {
                if (node.TreeView != null)
                {
                    // Select the node in tree view when right clicking.
                    // It can be null when right clicking an item from file contents search
                    node.TreeView.SelectedNode = node;
                }

                Program.MainForm.ShowVpkContextMenu(listViewItem.ListView, e.Location, false);
            }
        }
    }
}
