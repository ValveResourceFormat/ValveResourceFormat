using System.Buffers;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Types.Viewers;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.IO;

namespace GUI.Types.PackageViewer
{
#pragma warning disable CA1001 // TreeView is not owned by this class, set to null in VPK_Disposed
    class PackageViewer(VrfGuiContext vrfGuiContext) : IViewer, IDisposable
#pragma warning restore CA1001
    {
#pragma warning disable CA2213 // TODO: Can we fix TreeView to be owned by this class?
        private TreeViewWithSearchResults? TreeView;
#pragma warning restore CA2213
        private VirtualPackageNode? VirtualRoot;
        private VirtualPackageNode? ContextFolder;

        /// <summary>
        /// Whether this package is opened for editing, with the editing context menu.
        /// </summary>
        public bool IsEditingPackage { get; private set; }

        /// <summary>
        /// Whether files were added, removed or renamed since the package was opened for editing.
        /// </summary>
        public bool HasUnsavedChanges
        {
            get;
            private set
            {
                if (field != value)
                {
                    field = value;
                    UnsavedChangesChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// Raised when <see cref="HasUnsavedChanges"/> changes.
        /// </summary>
        public event EventHandler? UnsavedChangesChanged;

        public VrfGuiContext GuiContext => vrfGuiContext;

        public static PackageViewer? FromTab(TabPage tab)
            => tab.Controls[nameof(TreeViewWithSearchResults)] is TreeViewWithSearchResults treeView ? treeView.Viewer : null;

        /// <summary>
        /// Full path of the package file, or <see langword="null"/> for a new package that was never saved.
        /// </summary>
        public string? FileOnDisk => Path.IsPathRooted(vrfGuiContext.FileName) ? Path.GetFullPath(vrfGuiContext.FileName) : null;

        /// <summary>
        /// Whether this package can be opened for editing: a package on disk, not one inside of another package.
        /// </summary>
        public bool CanEdit => !IsEditingPackage
            && vrfGuiContext.ParentGuiContext == null
            && FileOnDisk != null
            && vrfGuiContext.CurrentPackage != null;

        /// <summary>
        /// Whether the package is split into chunk files, which added files are written to right away.
        /// A <c>_dir.vpk</c> without chunk files is a single file package, as mods name them to be mounted.
        /// </summary>
        public bool HasChunkFiles => vrfGuiContext.CurrentPackage?.HasChunkFiles == true;

        public bool IsSigned => vrfGuiContext.CurrentPackage?.IsSigned == true;

        public static bool IsAccepted(uint magic)
        {
            return magic == Package.MAGIC;
        }

        public Control CreateEmpty()
        {
            var package = new Package();
            package.OptimizeEntriesForBinarySearch();

            return CreateEditor(package);
        }

        /// <summary>
        /// Opens a package for editing. The context owns the package.
        /// </summary>
        public Control CreateEditor(Package package, bool hasUnsavedChanges = false)
        {
            IsEditingPackage = true;
            HasUnsavedChanges = hasUnsavedChanges;
            vrfGuiContext.CurrentPackage = package;
            BuildVirtualRoot(package);
            CreateTreeViewWithSearchResults();

            return TreeView ?? throw new InvalidOperationException("TreeView was not created");
        }

        /// <summary>
        /// Not supported, packages are read with <see cref="Load"/>.
        /// </summary>
        Task IViewer.LoadAsync(Stream? stream) => throw new NotSupportedException("Packages are read with Load.");

        /// <summary>
        /// Reads the package file of the context, or the package in an entry of the package of the parent context.
        /// </summary>
        public void Load(PackageEntry? entry)
        {
            var package = entry != null
                ? NestedPackage.Open(vrfGuiContext.ParentGuiContext!.CurrentPackage!, entry)
                : Package.Open(vrfGuiContext.FileName);

            vrfGuiContext.CurrentPackage = package;

            BuildVirtualRoot(package);
        }

        private void BuildVirtualRoot(Package package)
        {
            VirtualRoot = new VirtualPackageNode("root", 0, null);

            foreach (var file in package.Entries?.Values.SelectMany(static entries => entries) ?? [])
            {
                BetterTreeView.AddFileNode(VirtualRoot, file);
            }

            foreach (var node in VirtualRoot.Folders)
            {
                VirtualRoot.TotalSize += node.Value.TotalSize;
            }

            foreach (var node in VirtualRoot.Files)
            {
                VirtualRoot.TotalSize += node.TotalLength;
            }
        }

        public void Create(TabPage tab)
        {
            CreateTreeViewWithSearchResults();
            tab.Controls.Add(TreeView);
        }

        private void CreateTreeViewWithSearchResults()
        {
            if (VirtualRoot == null)
            {
                throw new InvalidOperationException("VirtualRoot must be initialized before creating TreeView");
            }

            // create a TreeView with search capabilities, register its events, and add it to the tab
            TreeView = new TreeViewWithSearchResults(this);
            TreeView.InitializeTreeViewFromPackage(vrfGuiContext, VirtualRoot);
            TreeView.OpenPackageEntry += VPK_OpenFile;
            TreeView.OpenContextMenu += VPK_OnContextMenu;
            TreeView.PreviewFile += VPK_PreviewFile;
            TreeView.PreviewCleared += VPK_PreviewCleared;
            TreeView.PreviewFocused += VPK_PreviewFocused;
            TreeView.PreviewBlurred += VPK_PreviewBlurred;
            TreeView.Disposed += VPK_Disposed;
        }

        public void AddFolder(string directory)
        {
            if (TreeView == null)
            {
                return;
            }

            var folderPath = Package.NormalizeDirectoryPath(JoinPath(GetFolderPath(GetContextFolder()), directory));

            if (folderPath.Length == 0)
            {
                _ = AppMessageDialogs.ShowMessageAsync($"\"{directory}\" is not a valid folder name.", "Failed to create folder", MessageIcon.Warning);
                return;
            }

            TreeView.AddFolderNode(folderPath);
            TreeView.RefreshListView();
        }

        /// <summary>
        /// Adds the files in a folder and its subfolders to the folder the context menu was opened on.
        /// </summary>
        public void AddFilesFromFolder(string inputDirectory) => AddFiles(PackageFileNames.GetFilesToPack(inputDirectory, FileOnDisk));

        /// <summary>
        /// Adds folders, with the files and subfolders in them, as subfolders of the folder the context menu was opened on.
        /// </summary>
        public void AddFolders(IEnumerable<string> folders)
            => AddFiles([.. folders.SelectMany(folder => PackageFileNames.GetFilesToPack(folder, FileOnDisk, $"{Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))}/"))]);

        /// <summary>
        /// Adds files to the folder the context menu was opened on.
        /// </summary>
        public void AddFiles(IEnumerable<string> files) => AddFiles([.. files.Select(static file => (file, Path.GetFileName(file)))]);

        private void AddFiles(List<(string PathOnDisk, string EntryPath)> files)
        {
            if (TreeView is not { } treeView || vrfGuiContext.CurrentPackage is not { } package)
            {
                return;
            }

            if (files.Count == 0)
            {
                _ = AppMessageDialogs.ShowMessageAsync("Nothing was added, there are no files to add.", "No files added", MessageIcon.Info);
                return;
            }

            var folder = GetContextFolder();
            var folderPath = GetFolderPath(folder);
            var filesByPath = new Dictionary<string, string>();
            var invalidFiles = new List<string>();

            foreach (var (pathOnDisk, entryPath) in files)
            {
                if (!Package.TryParseEntryPath(JoinPath(folderPath, entryPath), out var normalizedPath))
                {
                    invalidFiles.Add($"{pathOnDisk}: \"{entryPath}\" is not a valid path in a VPK.");
                }
                else if (!filesByPath.TryAdd(normalizedPath, pathOnDisk))
                {
                    invalidFiles.Add($"{pathOnDisk}: \"{normalizedPath}\" is also the path of {filesByPath[normalizedPath]}.");
                }
            }

            if (invalidFiles.Count > 0)
            {
                _ = AppMessageDialogs.ShowMessageAsync($"Nothing was added:\n{ListLines(invalidFiles, 10)}", "Failed to add files", MessageIcon.Warning);
                return;
            }

            var multiChunk = HasChunkFiles;
            var existingFiles = new List<string>();

            foreach (var entryPath in filesByPath.Keys)
            {
                if (package.FindEntry(entryPath) is { } existingEntry)
                {
                    existingFiles.Add(existingEntry.GetFullPath());
                }
            }

            var choice = existingFiles.Count == 0 ? ReplaceFilesChoice.Replace : AppMessageDialogs.AskToReplaceFiles(
                existingFiles.Count == 1 ? "Replace 1 existing file?" : string.Create(CultureInfo.InvariantCulture, $"Replace {existingFiles.Count} existing files?"),
                ListLines(existingFiles, 5),
                "Files already exist");

            if (choice == ReplaceFilesChoice.Cancel)
            {
                return;
            }

            var replace = choice == ReplaceFilesChoice.Replace;

            if (!multiChunk)
            {
                var lengths = filesByPath.Select(static file => (file.Key, new FileInfo(file.Value).Length)).ToList();

                if (!PackageFileNames.FitsIntoSingleFile(lengths, package, replace))
                {
                    var hint = existingFiles.Count > 0 && PackageFileNames.FitsIntoSingleFile(lengths, package, !replace)
                        ? (replace ? "\n\nSkipping the existing files would fit." : "\n\nReplacing the existing files would fit.")
                        : string.Empty;

                    _ = AppMessageDialogs.ShowMessageAsync($"These files do not fit into a VPK that is not split into chunk files, which holds at most 2 GiB. Save it with Save As and the \"Valve Pak split into chunk files\" type first.{hint}", "Failed to add files", MessageIcon.Warning);
                    return;
                }
            }

            var filesToAdd = filesByPath.Where(file => replace || package.FindEntry(file.Key) == null).ToList();
            var addedFiles = new List<(PackageEntry Entry, PackageEntry? ReplacedEntry)>();
            var failedFiles = new List<string>();

            using (var progressDialog = new GenericProgressForm { Text = "Adding files…", WaitForWorkToStop = true })
            {
                progressDialog.OnProcess = cancellationToken =>
                {
                    progressDialog.SetBarMax(filesToAdd.Count);

                    for (var i = 0; i < filesToAdd.Count && !cancellationToken.IsCancellationRequested; i++)
                    {
                        var (entryPath, pathOnDisk) = filesToAdd[i];

                        progressDialog.SetBarValue(i);
                        progressDialog.SetProgress(entryPath);

                        try
                        {
                            var replacedEntry = package.FindEntry(entryPath);
                            using var stream = File.OpenRead(pathOnDisk);
                            var entry = package.AddFile(entryPath, stream, multiChunk);

                            addedFiles.Add((entry, replacedEntry != null && package.RemoveFile(replacedEntry) ? replacedEntry : null));
                        }
                        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
                        {
                            failedFiles.Add($"{pathOnDisk}: {e.Message}");
                        }
                    }

                    return Task.CompletedTask;
                };
                progressDialog.ShowDialog();
            }

            var replacedFiles = new HashSet<PackageEntry>();

            treeView.BeginUpdate();

            foreach (var (entry, replacedEntry) in addedFiles)
            {
                if (replacedEntry != null)
                {
                    treeView.RemoveFileNode(replacedEntry);
                    replacedFiles.Add(replacedEntry);
                }

                treeView.AddFileNode(entry);
            }

            treeView.EndUpdate();
            folder.CreatedNode?.Expand();
            treeView.RefreshListView(replacedFiles);

            if (failedFiles.Count > 0)
            {
                _ = AppMessageDialogs.ShowMessageAsync(string.Create(CultureInfo.InvariantCulture, $"Failed to add {failedFiles.Count} files:\n{ListLines(failedFiles, 10)}"), "Failed to add files", MessageIcon.Error);
            }

            if (addedFiles.Count > 0)
            {
                HasUnsavedChanges = true;
            }
        }

        private static string ListLines(List<string> lines, int maxLines)
            => lines.Count > maxLines
                ? string.Create(CultureInfo.InvariantCulture, $"{string.Join('\n', lines.Take(maxLines))}\nand {lines.Count - maxLines} more")
                : string.Join('\n', lines);

        /// <summary>
        /// Removes files, and folders with all of the files in them, from the package.
        /// </summary>
        public void RemoveItems(IEnumerable<IBetterBaseItem> items)
        {
            if (TreeView == null || vrfGuiContext.CurrentPackage is not { } package)
            {
                return;
            }

            var removedFiles = new HashSet<PackageEntry>();
            var refusedFolders = new List<string>();

            TreeView.BeginUpdate();

            foreach (var item in items)
            {
                if (item.PackageEntry is { } entry)
                {
                    if (package.RemoveFile(entry))
                    {
                        removedFiles.Add(entry);
                        TreeView.RemoveFileNode(entry);
                    }
                }
                else if (item.PkgNode is { Parent: not null } folder)
                {
                    var folderPath = GetFolderPath(folder);

                    if (TreeViewWithSearchResults.EnumerateFiles(folder).Any())
                    {
                        if (!IsNormalizedFolderPath(folderPath))
                        {
                            refusedFolders.Add(folderPath);
                            continue;
                        }

                        removedFiles.UnionWith(package.RemoveDirectory(folderPath));
                    }

                    TreeView.RemoveFolderNode(folder);
                }
            }

            TreeView.EndUpdate();
            TreeView.RefreshListView(removedFiles);

            if (removedFiles.Count > 0)
            {
                HasUnsavedChanges = true;
            }

            if (refusedFolders.Count > 0)
            {
                _ = AppMessageDialogs.ShowMessageAsync(string.Join('\n', refusedFolders.Select(static path => GetNotNormalizedFolderMessage(path, "deleted"))), "Failed to delete", MessageIcon.Warning);
            }
        }

        /// <summary>
        /// Renames a file or a folder, keeping it in the same folder. Refuses names that other files already have, and asks before merging a folder into an existing one.
        /// </summary>
        public async Task RenameItemAsync(IBetterBaseItem item, string newName)
        {
            if (TreeView == null || vrfGuiContext.CurrentPackage is not { } package)
            {
                return;
            }

            if (newName.Length == 0 || newName is "." or ".." || newName.IndexOfAny([Package.DirectorySeparatorChar, '\\']) >= 0)
            {
                ShowRenameFailure($"\"{newName}\" is not a valid name.");
                return;
            }

            if (item.PackageEntry is { } entry)
            {
                RenameFile(TreeView, package, entry, JoinPath(entry.GetDirectoryPath(), newName));
            }
            else if (item.PkgNode is { Parent: not null } folder)
            {
                await RenameFolderAsync(TreeView, package, folder, JoinPath(GetFolderPath(folder.Parent), newName)).ConfigureAwait(true);
            }
        }

        private static IEnumerable<VirtualPackageNode> EnumerateEmptyFolders(VirtualPackageNode folder)
        {
            if (folder.Files.Count == 0 && folder.Folders.Count == 0)
            {
                yield return folder;
            }

            foreach (var subFolder in folder.Folders.Values)
            {
                foreach (var emptyFolder in EnumerateEmptyFolders(subFolder))
                {
                    yield return emptyFolder;
                }
            }
        }

        private void RenameFile(TreeViewWithSearchResults treeView, Package package, PackageEntry entry, string newPath)
        {
            var oldDirectoryPath = entry.GetDirectoryPath();
            var oldPath = entry.GetFullPath();

            if (Package.TryParseEntryPath(newPath, out var normalizedPath))
            {
                if (normalizedPath == oldPath)
                {
                    return;
                }

                if (package.FindEntry(normalizedPath) is { } conflict && conflict != entry)
                {
                    ShowRenameFailure($"\"{conflict.GetFullPath()}\" already exists.");
                    return;
                }
            }

            try
            {
                package.RenameFile(entry, newPath);
            }
            catch (ArgumentException e)
            {
                ShowRenameFailure(e.Message);
                return;
            }

            treeView.BeginUpdate();
            treeView.RemoveFileNode(entry, oldDirectoryPath, oldPath);
            treeView.AddFileNode(entry);
            treeView.EndUpdate();
            treeView.RefreshListView();

            HasUnsavedChanges = true;
        }

        /// <summary>
        /// Moves the files of a folder into another folder, along with the empty folders in it, which only exist in the tree.
        /// </summary>
        private async Task RenameFolderAsync(TreeViewWithSearchResults treeView, Package package, VirtualPackageNode folder, string newFolderPath)
        {
            var oldFolderPath = GetFolderPath(folder);
            var targetFolderPath = Package.NormalizeDirectoryPath(newFolderPath);

            if (targetFolderPath.Length == 0)
            {
                ShowRenameFailure($"\"{newFolderPath}\" is not a valid folder.");
                return;
            }

            if (targetFolderPath == oldFolderPath)
            {
                return;
            }

            var files = TreeViewWithSearchResults.EnumerateFiles(folder).ToHashSet();

            if (files.Count > 0 && !IsNormalizedFolderPath(oldFolderPath))
            {
                ShowRenameFailure(GetNotNormalizedFolderMessage(oldFolderPath, "renamed"));
                return;
            }

            var oldFilePaths = files.Select(static file => file.GetFullPath()).ToList();
            var emptyFolders = EnumerateEmptyFolders(folder).Select(emptyFolder => GetFolderPath(emptyFolder)[oldFolderPath.Length..]).ToList();
            var newPaths = new HashSet<string>(files.Count, Package.PathComparer);

            foreach (var file in files)
            {
                var newPath = string.Create(CultureInfo.InvariantCulture, $"{targetFolderPath}{file.GetDirectoryPath().AsSpan(oldFolderPath.Length)}{Package.DirectorySeparatorChar}{file.FileName}.{file.TypeName}");

                if (!Package.TryParseEntryPath(newPath, out var normalizedPath))
                {
                    continue;
                }

                if (package.FindEntry(normalizedPath) is { } conflict && !files.Contains(conflict))
                {
                    ShowRenameFailure($"\"{conflict.GetFullPath()}\" already exists.");
                    return;
                }

                if (!newPaths.Add(normalizedPath))
                {
                    ShowRenameFailure($"\"{file.GetFullPath()}\" and another file in the folder would both be moved to \"{normalizedPath}\".");
                    return;
                }
            }

            if (treeView.FindFolderNode(targetFolderPath) is { } existingFolder && existingFolder != folder
                && !await AppMessageDialogs.ConfirmAsync($"\"{existingFolder.Name}\" already exists. Merge the folders?", "Rename folder", buttons: ConfirmButtons.YesNo).ConfigureAwait(true))
            {
                return;
            }

            IReadOnlyList<PackageEntry> movedFiles = [];

            if (files.Count > 0)
            {
                try
                {
                    movedFiles = package.RenameDirectory(oldFolderPath, targetFolderPath);
                }
                catch (ArgumentException e)
                {
                    ShowRenameFailure(e.Message);
                    return;
                }
            }

            treeView.BeginUpdate();
            treeView.RemoveFolderNode(folder, oldFilePaths);

            foreach (var file in movedFiles)
            {
                treeView.AddFileNode(file);
            }

            treeView.EndUpdate();

            foreach (var emptyFolder in emptyFolders)
            {
                treeView.AddFolderNode(Package.NormalizeDirectoryPath(string.Concat(targetFolderPath, emptyFolder)));
            }

            treeView.RefreshListView();

            if (movedFiles.Count > 0)
            {
                HasUnsavedChanges = true;
            }
        }

        /// <summary>
        /// Returns whether the folder path is normalized, so that <see cref="Package.RemoveDirectory(string)"/> and <see cref="Package.RenameDirectory(string, string)"/> act on exactly the files in the folder.
        /// </summary>
        private static bool IsNormalizedFolderPath(string folderPath)
            => folderPath.Length > 0 && Package.NormalizeDirectoryPath(folderPath) == folderPath;

        private static string GetNotNormalizedFolderMessage(string folderPath, string action)
            => $"\"{folderPath}\" can not be {action} because its stored name is not normalized, such as having upper case letters.";

        private static void ShowRenameFailure(string message)
            => _ = AppMessageDialogs.ShowMessageAsync(message, "Failed to rename", MessageIcon.Warning);

        /// <summary>
        /// Forgets the changes, so closing the package does not ask to save them.
        /// </summary>
        public void DiscardChanges() => HasUnsavedChanges = false;

        internal static List<PackageEntry> RecoverDeletedFiles(Package package, GenericProgressForm progress)
        {
            var allEntries = (package.Entries?.Values.SelectMany(static entries => entries) ?? [])
                .OrderBy(file => file.Offset)
                .GroupBy(file => file.ArchiveIndex)
                .OrderBy(x => x.Key)
                .ToDictionary(x => x.Key, x => x.ToList());

            // Every known entry has a gap in front of it to scan, so that is the unit of progress
            progress.SetBarMax(allEntries.Values.Sum(static entries => entries.Count));
            var processed = 0;

            var hiddenIndex = 0;
            var totalSlackSize = 0u;
            var hiddenFiles = new List<PackageEntry>();
            var kv3header = Encoding.ASCII.GetBytes("<!-- kv3 ");
            var previousChunkIndex = -1;

            foreach (var (archiveIndex, entries) in allEntries)
            {
                if (archiveIndex != Package.DirectoryArchiveIndex)
                {
                    for (var unusedChunkIndex = previousChunkIndex + 1; unusedChunkIndex < archiveIndex; unusedChunkIndex++)
                    {
                        Log.Warn(nameof(PackageViewer), string.Create(CultureInfo.InvariantCulture, $"There is probably an unused {unusedChunkIndex:D3}.vpk"));
                    }

                    previousChunkIndex = archiveIndex;
                }

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
                            resource.Read(stream, mode: ReadMode.Deferred);

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

                            string? resourceTypeExtensionWithDot = null;

                            if (resource.ResourceType != ResourceType.Unknown)
                            {
                                var resourceTypeExtension = resource.ResourceType.GetExtension();
                                resourceTypeExtensionWithDot = string.Concat(".", resourceTypeExtension);
                                newEntry.TypeName = string.Concat(resourceTypeExtension, GameFileLoader.CompiledFileSuffix);
                            }

                            string? filepath = null;

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
                                var dirName = Path.GetDirectoryName(filepath);
                                if (dirName != null)
                                {
                                    newEntry.DirectoryName = dirName.Length > 0 ? dirName.Replace('\\', Package.DirectorySeparatorChar) : " ";
                                }
                                newEntry.FileName = Path.GetFileNameWithoutExtension(filepath);
                            }
                            else
                            {
                                newEntry.DirectoryName += string.Concat(Package.DirectorySeparatorChar, resource.ResourceType);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(nameof(PackageViewer), $"Failed to parse \"{newEntry.FileName}\": {ex.Message}");

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

                        hiddenFiles.Add(newEntry);

                        progress.SetProgress($"Found {hiddenFiles.Count} files ({HumanReadableByteSizeFormatter.Format(totalSlackSize)}) so far…");
                    }
                }

                // Recover files in gaps between entries
                foreach (var entry in entries)
                {
                    progress.SetBarValue(++processed);

                    if (entry.Length == 0)
                    {
                        continue;
                    }

                    FindValidFiles(entry.Offset, entry.Length);
                }

                // Recover files in archives after last possible entry for that archive
                var archiveSize = nextOffset;

                try
                {
                    if (archiveIndex == Package.DirectoryArchiveIndex)
                    {
                        using var archive = package.OpenArchiveStream(archiveIndex);
                        archiveSize = (uint)archive.Length;
                    }
                    else
                    {
                        archiveSize = (uint)new FileInfo(package.GetChunkFilePath(archiveIndex)).Length;
                    }
                }
                catch (Exception e)
                {
                    Log.Debug(nameof(PackageViewer), string.Create(CultureInfo.InvariantCulture, $"Failed to get size of archive {archiveIndex}: {e.Message}"));
                }

                if (archiveSize > nextOffset)
                {
                    FindValidFiles(archiveSize, 0);
                }
            }

            Log.Info(nameof(PackageViewer), $"Found {hiddenIndex} deleted files totaling {HumanReadableByteSizeFormatter.Format(totalSlackSize)}");

            return hiddenFiles;
        }

        private static string? RecoverDeletedFilesGetPossiblePath(List<InputDependency> inputDeps, string? resourceTypeExtensionWithDot)
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

        private void VPK_Disposed(object? sender, EventArgs e)
        {
            if (sender is TreeViewWithSearchResults treeViewWithSearch)
            {
                treeViewWithSearch.OpenPackageEntry -= VPK_OpenFile;
                treeViewWithSearch.OpenContextMenu -= VPK_OnContextMenu;
                treeViewWithSearch.PreviewFile -= VPK_PreviewFile;
                treeViewWithSearch.PreviewCleared -= VPK_PreviewCleared;
                treeViewWithSearch.PreviewFocused -= VPK_PreviewFocused;
                treeViewWithSearch.PreviewBlurred -= VPK_PreviewBlurred;
                treeViewWithSearch.Disposed -= VPK_Disposed;
                TreeView = null;
                ContextFolder = null;
            }
        }

        /// <summary>
        /// Opens the given package entry in a new tab.
        /// </summary>
        /// <param name="sender">Object which raised event.</param>
        /// <param name="entry">The package entry to open.</param>
        private void VPK_OpenFile(object? sender, PackageEntry entry)
        {
            var newVrfGuiContext = new VrfGuiContext(entry.GetFullPath(), vrfGuiContext);
            Program.MainForm.OpenFile(newVrfGuiContext, entry);
        }

        private void VPK_PreviewCleared(object? sender, EventArgs e)
        {
            // A folder is shown instead of a file preview, so the window title should no longer reflect a file.
            Program.MainForm.ResetPreviewTitle();
        }

        private void VPK_PreviewFocused(object? sender, TabPage previewTab)
        {
            Program.MainForm.ShowPreviewKeybindings(previewTab);
        }

        private void VPK_PreviewBlurred(object? sender, EventArgs e)
        {
            Program.MainForm.ShowSelectedTabKeybindings();
        }

        private void VPK_PreviewFile(object? sender, PackageEntry entry)
        {
            if (TreeView == null)
            {
                return;
            }

            if (!TreeViewWithSearchResults.CanQuickPreviewFile(entry))
            {
                return;
            }

            var newVrfGuiContext = new VrfGuiContext(entry.GetFullPath(), vrfGuiContext);
            Program.MainForm.OpenFile(newVrfGuiContext, entry, TreeView);
        }

        /// <summary>
        /// Folder the context menu was opened on, files are added into it.
        /// </summary>
        private VirtualPackageNode GetContextFolder() => ContextFolder ?? VirtualRoot!;

        private static string GetFolderPath(VirtualPackageNode folder)
        {
            var names = new Stack<string>();

            for (var node = folder; node.Parent != null; node = node.Parent)
            {
                names.Push(node.Name);
            }

            return string.Join(Package.DirectorySeparatorChar, names);
        }

        private static string JoinPath(string folderPath, string path)
            => folderPath.Length > 0 ? string.Join(Package.DirectorySeparatorChar, folderPath, path) : path;

        /// <summary>
        /// Opens a context menu where the user right-clicked in the TreeView or ListView.
        /// </summary>
        /// <param name="sender">Object which raised event.</param>
        /// <param name="e">Event data.</param>
        private void VPK_OnContextMenu(object? sender, PackageContextMenuEventArgs e)
        {
            if (TreeView == null)
            {
                return;
            }

            var isRoot = e.PkgNode == TreeView.mainTreeView.Root;
            var isFolder = e.PackageEntry is null;

            if (e.TreeNode is not null)
            {
                isFolder = e.TreeNode.IsFolder;
            }

            if (sender is not Control control)
            {
                return;
            }

            if (IsEditingPackage)
            {
                ContextFolder = e.PkgNode
                    ?? (e.TreeNode?.Parent as BetterTreeNode)?.PkgNode
                    ?? (e.PackageEntry != null ? TreeView.FindFolderNode(e.PackageEntry.GetDirectoryPath()) : null);

                Program.MainForm.ShowVpkEditingContextMenu(control, e.Location, isRoot, isFolder, hasItem: !e.IsBackground);
                return;
            }

            Program.MainForm.ShowVpkContextMenu(control, e.Location, isRoot, isFolder, TreeView.DeletedFilesRecovered, CanEdit);
        }

        public void Dispose()
        {
            TreeView = null;
        }
    }
}
