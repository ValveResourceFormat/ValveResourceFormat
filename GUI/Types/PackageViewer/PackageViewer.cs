using System.Buffers;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.Viewers;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.IO;

#nullable disable

namespace GUI.Types.PackageViewer
{
#pragma warning disable CA1001 // TreeView is not owned by this class, set to null in VPK_Disposed
    class PackageViewer(VrfGuiContext vrfGuiContext) : IViewer
#pragma warning restore CA1001
    {
        private TreeViewWithSearchResults TreeView;
        private VirtualPackageNode VirtualRoot;
        private BetterTreeNode LastContextTreeNode;
        private bool IsEditingPackage; // TODO: Allow editing existing vpks (but not chunked ones)

        public static bool IsAccepted(uint magic)
        {
            return magic == SteamDatabase.ValvePak.Package.MAGIC;
        }

        public Control CreateEmpty()
        {
            IsEditingPackage = true;

            var package = new Package();
            package.AddFile("README.txt", []); // TODO: Otherwise package.Entries is null

            vrfGuiContext.CurrentPackage = package;

            VirtualRoot = new VirtualPackageNode("root", 0, null);
            CreateTreeViewWithSearchResults();

            return TreeView;
        }

        public async Task LoadAsync(Stream stream)
        {
            var package = new Package();
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

            VirtualRoot = new VirtualPackageNode("root", 0, null);

            foreach (var fileType in vrfGuiContext.CurrentPackage.Entries)
            {
                foreach (var file in fileType.Value)
                {
                    BetterTreeView.AddFileNode(VirtualRoot, file);
                }
            }
        }

        public TabPage Create()
        {
            var tab = new TabPage();

            CreateTreeViewWithSearchResults();
            tab.Controls.Add(TreeView);

            return tab;
        }

        private void CreateTreeViewWithSearchResults()
        {
            // create a TreeView with search capabilities, register its events, and add it to the tab
            TreeView = new TreeViewWithSearchResults(this);
            TreeView.InitializeTreeViewFromPackage(vrfGuiContext, VirtualRoot);
            TreeView.OpenPackageEntry += VPK_OpenFile;
            TreeView.OpenContextMenu += VPK_OnContextMenu;
            TreeView.PreviewFile += VPK_PreviewFile;
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

            var resourceEntries = new Queue<(string PathOnDisk, PackageEntry Entry)>();

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

                var entry = vrfGuiContext.CurrentPackage.AddFile(name, data);
                TreeView.AddFileNode(entry);

                if (data.Length >= 6)
                {
                    var magicResourceVersion = BitConverter.ToUInt16(data, 4);

                    if (Viewers.Resource.IsAccepted(magicResourceVersion) && name.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
                    {
                        resourceEntries.Enqueue((file, entry));
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

#if DEBUG
                while (resourceEntries.TryDequeue(out var entry))
                {
                    vrfGuiContext.CurrentPackage.ReadEntry(entry.Entry, out var output, false);
                    using var entryStream = new MemoryStream(output);

                    using var resource = new ValveResourceFormat.Resource();
                    resource.Read(entryStream);

                    if (resource.ExternalReferences is null)
                    {
                        continue;
                    }

                    // TODO: This doesn't work properly
                    var folderDepth = entry.Entry.DirectoryName.Count(static c => c == Package.DirectorySeparatorChar);
                    var folder = Path.GetDirectoryName(entry.PathOnDisk.AsSpan());

                    while (folderDepth-- > 0)
                    {
                        folder = Path.GetDirectoryName(folder);
                    }

                    foreach (var reference in resource.ExternalReferences.ResourceRefInfoList)
                    {
                        if (reference.Name.StartsWith("_bakeresourcecache", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        // Do not recurse maps (skyboxes)
                        if (reference.Name.EndsWith(".vmap", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var file = Path.Combine(folder.ToString(), reference.Name);

                        if (!File.Exists(file))
                        {
                            Log.Warn(nameof(PackageViewer), $"Faield to find file: {file}");
                            continue;
                        }
                    }


                }
#endif
            }
        }

        public void RemoveCurrentFiles() => RemoveRecursiveFiles(LastContextTreeNode);

        public void RemoveRecursiveFiles(BetterTreeNode node)
        {
            if (node.PkgNode != null)
            {
                ((BetterTreeNode)node.Parent).PkgNode.Folders.Remove(node.PkgNode.Name);
            }

            for (var i = node.Nodes.Count - 1; i >= 0; i--)
            {
                RemoveRecursiveFiles((BetterTreeNode)node.Nodes[i]);
            }

            if (node.PackageEntry != null)
            {
                ((BetterTreeNode)node.Parent).PkgNode.Files.Remove(node.PackageEntry);
                vrfGuiContext.CurrentPackage.RemoveFile(node.PackageEntry);
            }

            if (node.Level > 0)
            {
                node.Remove();
            }
        }

        public void SaveToFile(string fileName)
        {
            vrfGuiContext.CurrentPackage.Write(fileName);

            var fileCount = 0;
            var fileSize = 0u;

            foreach (var fileType in vrfGuiContext.CurrentPackage.Entries)
            {
                foreach (var file in fileType.Value)
                {
                    fileCount++;
                    fileSize += file.TotalLength;
                }
            }

            var result = $"Created {Path.GetFileName(fileName)} with {fileCount} files of size {HumanReadableByteSizeFormatter.Format(fileSize)}.";

            Log.Info(nameof(PackageViewer), result);

            MessageBox.Show(
                result,
                "VPK created",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        internal static List<PackageEntry> RecoverDeletedFiles(Package package, Action<string> setProgress)
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
                    Log.Warn(nameof(PackageViewer), $"There is probably an unused {previousArchiveIndex:D3}.vpk");
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

                        offset = offset + 16 - 1 & ~(16u - 1); // TODO: Validate this gap

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
                            using var stream = new MemoryStream(bytes, 0, (int)newEntry.TotalLength);
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
                                filepath = RecoverDeletedFilesGetPossiblePath(resource.EditInfo.InputDependencies, resourceTypeExtensionWithDot);
                                filepath ??= RecoverDeletedFilesGetPossiblePath(resource.EditInfo.AdditionalInputDependencies, resourceTypeExtensionWithDot);

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
                            Log.Debug(nameof(PackageViewer), $"File {hiddenIndex} - {ex.Message}");

                            newEntry.FileName += $" ({length} bytes)";

                            var span = bytes.AsSpan(0, (int)newEntry.TotalLength);

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
                        Log.Debug(nameof(PackageViewer), e.Message);
                    }

                    if (archiveFileSize != nextOffset)
                    {
                        FindValidFiles(archiveFileSize, 0);
                    }
                }
            }

            Log.Info(nameof(PackageViewer), $"Found {hiddenIndex} deleted files totaling {HumanReadableByteSizeFormatter.Format(totalSlackSize)}");

            return hiddenFiles;
        }

        private static string RecoverDeletedFilesGetPossiblePath(List<InputDependency> inputDeps, string resourceTypeExtensionWithDot)
        {
            if (inputDeps.Count == 0)
            {
                return null;
            }

            foreach (var inputDependency in inputDeps)
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

                foreach (var inputDependency in inputDeps)
                {
                    if (preferredExtensions.Contains(Path.GetExtension(inputDependency.ContentRelativeFilename)))
                    {
                        return inputDependency.ContentRelativeFilename;
                    }
                }
            }

            return inputDeps[0].ContentRelativeFilename;
        }

        private void VPK_Disposed(object sender, EventArgs e)
        {
            if (sender is TreeViewWithSearchResults treeViewWithSearch)
            {
                treeViewWithSearch.OpenPackageEntry -= VPK_OpenFile;
                treeViewWithSearch.OpenContextMenu -= VPK_OnContextMenu;
                treeViewWithSearch.PreviewFile -= VPK_PreviewFile;
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
        private void VPK_OpenFile(object sender, PackageEntry entry)
        {
            var newVrfGuiContext = new VrfGuiContext(entry.GetFullPath(), vrfGuiContext);
            Program.MainForm.OpenFile(newVrfGuiContext, entry);
        }

        private void VPK_PreviewFile(object sender, PackageEntry entry)
        {
            if (((Settings.QuickPreviewFlags)Settings.Config.QuickFilePreview & Settings.QuickPreviewFlags.Enabled) == 0)
            {
                return;
            }

            var extension = entry.TypeName;

            if (extension is "vpk" or "vmap_c")
            {
                // Not ideal to check by file extension, but do not nest vpk previewss
                return;
            }

            var newVrfGuiContext = new VrfGuiContext(entry.GetFullPath(), vrfGuiContext);
            Program.MainForm.OpenFile(newVrfGuiContext, entry, TreeView);
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

        /// <summary>
        /// Opens a context menu where the user right-clicked in the ListView.
        /// </summary>
        /// <param name="sender">Object which raised event.</param>
        /// <param name="e">Event data.</param>
        private void VPK_OnContextMenu(object sender, PackageContextMenuEventArgs e)
        {
            var isRoot = e.PkgNode == TreeView.mainTreeView.Root;
            var isFolder = e.PackageEntry is null;

            if (e.TreeNode is not null)
            {
                isFolder = e.TreeNode.IsFolder;
            }

            if (IsEditingPackage)
            {
                if (e.TreeNode != null)
                {
                    LastContextTreeNode = e.TreeNode;

                    Program.MainForm.ShowVpkEditingContextMenu((Control)sender, e.Location, isRoot, isFolder);
                }

                return;
            }

            Program.MainForm.ShowVpkContextMenu((Control)sender, e.Location, isRoot, isFolder);
        }
    }
}
