using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Types.Exporter;
using GUI.Types.PackageViewer;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat.IO;

namespace GUI
{
    partial class MainForm
    {
        private static readonly TimeSpan PackageReleaseTimeout = TimeSpan.FromSeconds(5);

        private bool IsSavingPackage;

        private static VrfGuiContext? GetTabContext(TabPage tab)
            => tab.Tag is ExportData exportData ? exportData.VrfGuiContext : PackageViewer.FromTab(tab)?.GuiContext;

        private static bool IsSameFile(string fileName, string path)
            => Path.IsPathRooted(fileName) && Path.GetFullPath(fileName).Equals(path, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Closes the tabs of files opened from the package file at <paramref name="path"/>, and of files opened from
        /// <paramref name="context"/>, without asking to save changes.
        /// </summary>
        /// <param name="path">Full path of the package file.</param>
        /// <param name="context">Context of the package being saved.</param>
        /// <param name="index">Index of a tab, moved left for each closed tab before it.</param>
        /// <returns>Completes once the closed tabs released their files.</returns>
        private Task CloseTabsUsing(string path, VrfGuiContext? context, ref int index)
        {
            var closedContexts = new HashSet<VrfGuiContext>();

            for (var i = mainTabs.TabPages.Count - 1; i > 0; i--)
            {
                var tab = mainTabs.TabPages[i];

                if (GetTabContext(tab) is not { } tabContext)
                {
                    continue;
                }

                var root = tabContext.RootContext;

                if (!IsSameFile(root.FileName, path) && root != context)
                {
                    continue;
                }

                closedContexts.Add(tabContext);
                closedContexts.Add(root);

                if (i < index)
                {
                    index--;
                }

                mainTabs.CloseTab(tab, skipConfirmation: true);
            }

            return Task.WhenAll(closedContexts.Select(static closedContext => closedContext.DisposalCompleted));
        }

        /// <summary>
        /// Creates the context of a package editor, with the game search paths from settings loaded off the UI thread,
        /// and the package file read there into it when <paramref name="readPackage"/> is set.
        /// </summary>
        private static async Task<VrfGuiContext> CreatePackageEditorContextAsync(string fileName, bool readPackage = false)
        {
            var context = new VrfGuiContext(fileName, null, loadSearchPaths: false);

            try
            {
                await Task.Run(() =>
                {
                    context.LoadSearchPaths();

                    if (readPackage)
                    {
                        context.CurrentPackage = Package.Open(fileName);
                    }
                }).ConfigureAwait(true);
            }
            catch
            {
                context.Dispose();
                throw;
            }

            return context;
        }

        /// <summary>
        /// Adds the tab of a package editor, whose title ends in "*" while the package has unsaved changes.
        /// </summary>
        private void AddPackageEditorTab(PackageViewer viewer, Control contents, string toolTip, int index)
        {
            var title = viewer.FileOnDisk is { } path ? $"Editing {Path.GetFileName(path)}" : "New VPK";
#pragma warning disable CA2000 // Disposed if adding it fails, the tab control owns it after
            var tab = new ThemedTabPage(title)
            {
                ToolTipText = toolTip,
                ImageIndex = AppIcons.ExtensionIcons["vpk"],
            };
#pragma warning restore CA2000

            void UpdateTitle(object? sender, EventArgs e) => tab.Text = viewer.HasUnsavedChanges ? $"{title}*" : title;

            try
            {
                tab.Controls.Add(contents);
                mainTabs.TabPages.Insert(Math.Clamp(index, 1, mainTabs.TabPages.Count), tab);
                mainTabs.SelectTab(tab);
            }
            catch
            {
                tab.Dispose();
                throw;
            }

            viewer.UnsavedChangesChanged += UpdateTitle;
            UpdateTitle(viewer, EventArgs.Empty);
        }

        /// <summary>
        /// Opens a package file for editing in its own tab, closing the other tabs of that file.
        /// </summary>
        private async Task OpenPackageEditorAsync(string path, int? index = null)
        {
            path = Path.GetFullPath(path);

            if (PackageFileNames.TryGetArchiveChunkDirectoryFile(path, out var directoryFile))
            {
                path = directoryFile;
            }

            if (FindPackageEditorTab(path) is { } openTab)
            {
                mainTabs.SelectTab(openTab);
                return;
            }

#pragma warning disable CA2000 // The editor tab owns the viewer and its tree view disposes the context
            VrfGuiContext context;

            try
            {
                context = await CreatePackageEditorContextAsync(path, readPackage: true).ConfigureAwait(true);
            }
            catch (Exception e)
            {
                ErrorReport.ShowError(e, VrfGuiContext.GetDisplayPath(path));
                return;
            }

            if (IsSavingPackage)
            {
                context.Dispose();
                await AppMessageDialogs.ShowMessageAsync("A VPK is being saved, open it again once that is done.", "Failed to open VPK", MessageIcon.Warning).ConfigureAwait(true);
                return;
            }

            if (FindPackageEditorTab(path) is { } openedTab)
            {
                context.Dispose();
                mainTabs.SelectTab(openedTab);
                return;
            }

            var viewer = new PackageViewer(context);
#pragma warning restore CA2000
            var contents = viewer.CreateEditor(context.CurrentPackage!);

            if (viewer.IsSigned && !await AppMessageDialogs.ConfirmAsync(
                "This VPK is signed. Saving it writes it without the signature, so games that check signatures will not load it.",
                "Signed VPK",
                MessageIcon.Warning).ConfigureAwait(true))
            {
                contents.Dispose();
                return;
            }

            var tabIndex = index ?? mainTabs.SelectedIndex + 1;
            _ = CloseTabsUsing(path, null, ref tabIndex);

            AddPackageEditorTab(viewer, contents, path, tabIndex);
        }

        private TabPage? FindPackageEditorTab(string path)
        {
            foreach (TabPage tab in mainTabs.TabPages)
            {
                if (PackageViewer.FromTab(tab) is { IsEditingPackage: true, FileOnDisk: { } editedFile } && editedFile.Equals(path, StringComparison.OrdinalIgnoreCase))
                {
                    return tab;
                }
            }

            return null;
        }

        private async void CreateVpkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await CreatePackageEditorAsync(null).ConfigureAwait(true);
        }

        private async void CreateVpkFromFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var inputFolder = AppFileDialogs.PickFolder("Choose which folder to pack into a VPK", AppFileDialogs.RememberIn.OpenDirectory);

            if (inputFolder != null)
            {
                await CreatePackageEditorAsync(inputFolder).ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Opens a new package for editing, with the files of <paramref name="inputFolder"/> added to its root.
        /// </summary>
        private async Task CreatePackageEditorAsync(string? inputFolder)
        {
            var folderName = inputFolder != null ? Path.GetFileName(Path.TrimEndingDirectorySeparator(inputFolder)) : string.Empty;
            var fileName = folderName.Length > 0 ? $"{folderName}.vpk" : "new.vpk";
            var context = await CreatePackageEditorContextAsync(fileName).ConfigureAwait(true);

            try
            {
#pragma warning disable CA2000 // The editor tab owns the viewer and its tree view disposes the context
                var viewer = new PackageViewer(context);
#pragma warning restore CA2000
                var contents = viewer.CreateEmpty();
                context = null;

                AddPackageEditorTab(viewer, contents, inputFolder ?? "New VPK", mainTabs.SelectedIndex + 1);

                if (inputFolder != null)
                {
                    viewer.AddFilesFromFolder(inputFolder);
                }
            }
            finally
            {
                context?.Dispose();
            }
        }

        private async void EditVpkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var path = AppFileDialogs.OpenFile("Choose a VPK to edit", "Valve Pak (*.vpk)|*.vpk");

            if (path != null)
            {
                await OpenPackageEditorAsync(path).ConfigureAwait(true);
            }
        }

        private async void EditThisVpkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (mainTabs.SelectedTab is { } tab && PackageViewer.FromTab(tab) is { CanEdit: true, FileOnDisk: { } path })
            {
                await OpenPackageEditorAsync(path, mainTabs.GetTabIndex(tab)).ConfigureAwait(true);
            }
        }

        private async void OnSaveVPKToDiskToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (mainTabs.SelectedTab is { } tab && PackageViewer.FromTab(tab) is { IsEditingPackage: true } viewer && SavePackage(viewer, tab, saveAs: false, out var index) is { } target)
            {
                await OpenPackageEditorAsync(target, index).ConfigureAwait(true);
            }
        }

        private async void OnSaveVpkAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (mainTabs.SelectedTab is { } tab && PackageViewer.FromTab(tab) is { IsEditingPackage: true } viewer && SavePackage(viewer, tab, saveAs: true, out var index) is { } target)
            {
                await OpenPackageEditorAsync(target, index).ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Closes the tab and the tabs holding the target open, then writes the package once they released it.
        /// If saving fails, the package opens again with its changes.
        /// </summary>
        /// <param name="viewer">The package being edited.</param>
        /// <param name="tab">The tab of the package.</param>
        /// <param name="saveAs">Ask for the file to save to, a new package always asks.</param>
        /// <param name="index">Index of the tab of the package once the tabs holding the target are closed.</param>
        /// <returns>The file the package was saved to, or <see langword="null"/> if it was not saved.</returns>
        private string? SavePackage(PackageViewer viewer, TabPage tab, bool saveAs, out int index)
        {
            var target = viewer.FileOnDisk;
            var splitIntoChunks = viewer.HasChunkFiles;
            var editedPackage = viewer.GuiContext.CurrentPackage!;
            index = mainTabs.GetTabIndex(tab);

            if (saveAs || target == null)
            {
                target = AppFileDialogs.SaveFile("Save VPK package", Path.GetFileName(viewer.GuiContext.FileName), "vpk", "Valve Pak|*.vpk|Valve Pak split into chunk files|*_dir.vpk",
                    out var selectedFilter, filterIndex: splitIntoChunks ? 2 : 1);

                if (target == null)
                {
                    return null;
                }

                target = Path.GetFullPath(target);
                splitIntoChunks = selectedFilter == 2;

                var error = editedPackage.HasChunkFiles && !splitIntoChunks
                    ? "A VPK with chunk files can only be saved split into chunk files."
                    : PackageFileNames.GetPackageFileNameError(target, splitIntoChunks);

                if (error != null)
                {
                    _ = AppMessageDialogs.ShowMessageAsync(error, "Failed to save VPK", MessageIcon.Warning);
                    return null;
                }
            }

            var requiresRepack = editedPackage.RequiresRepack(target);

            if (requiresRepack && !AppMessageDialogs.Confirm("Saving to another file copies all chunk files of this VPK.", "Save VPK", MessageIcon.Warning))
            {
                return null;
            }

            foreach (TabPage otherTab in mainTabs.TabPages)
            {
                if (PackageViewer.FromTab(otherTab) is { HasUnsavedChanges: true } otherViewer && otherViewer != viewer && otherViewer.FileOnDisk is { } otherFile && otherFile.Equals(target, StringComparison.OrdinalIgnoreCase))
                {
                    _ = AppMessageDialogs.ShowMessageAsync($"\"{Path.GetFileName(target)}\" has unsaved changes in another tab, save or close it first.", "Failed to save VPK", MessageIcon.Warning);
                    return null;
                }
            }

            var hasUnsavedChanges = viewer.HasUnsavedChanges;

            if (viewer.GuiContext.ReleasePackage() is not { } package)
            {
                return null;
            }

            var fileName = viewer.GuiContext.FileName;
            var toolTip = tab.ToolTipText;
            var released = CloseTabsUsing(target, viewer.GuiContext, ref index);

            Log.Info(nameof(MainForm), $"Packing to \"{target}\"...");

            IsSavingPackage = true;
            var writeError = WritePackageInProgressForm(package, target, released, repack: requiresRepack || (splitIntoChunks && !package.IsDirVPK && !package.HasChunkFiles));
            IsSavingPackage = false;

            if (writeError != null)
            {
#pragma warning disable CA2025 // The package is only disposed below when it was saved
                _ = RestorePackageEditorAsync(fileName, package, hasUnsavedChanges, toolTip, index);
#pragma warning restore CA2025

                if (writeError is IOException or UnauthorizedAccessException)
                {
                    _ = AppMessageDialogs.ShowMessageAsync($"{writeError.Message}\n\nIt may be open in another tab, be a game search path in Settings, or be used by another program.", "Failed to save VPK", MessageIcon.Error);
                }
                else if (writeError is InvalidOperationException)
                {
                    _ = AppMessageDialogs.ShowMessageAsync(writeError.Message, "Failed to save VPK", MessageIcon.Error);
                }
                else
                {
                    ErrorReport.ShowError(writeError, VrfGuiContext.GetDisplayPath(target));
                }

                return null;
            }

            package.Dispose();

            Log.Info(nameof(MainForm), $"Saved {VrfGuiContext.GetDisplayPath(target)}");

            return target;
        }

        /// <summary>
        /// Opens a package for editing again in a new tab, after saving it failed.
        /// </summary>
        private async Task RestorePackageEditorAsync(string fileName, Package package, bool hasUnsavedChanges, string toolTip, int index)
        {
#pragma warning disable CA2000 // The editor tab owns the viewer and its tree view disposes the context
            var viewer = new PackageViewer(await CreatePackageEditorContextAsync(fileName).ConfigureAwait(true));
#pragma warning restore CA2000
            AddPackageEditorTab(viewer, viewer.CreateEditor(package, hasUnsavedChanges), toolTip, index);
        }

        /// <summary>
        /// Waits for the tabs closed for the save to release their files, then writes the package, on a worker thread behind a progress dialog that can not be cancelled.
        /// </summary>
        /// <returns>The exception saving failed with, or <see langword="null"/> if the package was written.</returns>
        private static Exception? WritePackageInProgressForm(Package package, string target, Task released, bool repack)
        {
            Exception? error = null;
            var written = false;

            using var progressDialog = new GenericProgressForm
            {
                Text = "Saving VPK…",
                CanCancel = false,
            };
            progressDialog.OnProcess = async cancellationToken =>
            {
                progressDialog.SetProgress(repack ? $"Copying the files of the VPK to \"{Path.GetFileName(target)}\"…" : $"Saving \"{Path.GetFileName(target)}\"…");

                try
                {
                    await released.WaitAsync(PackageReleaseTimeout, cancellationToken).ConfigureAwait(false);
                    package.Write(target, repack);
                    written = true;
                }
                catch (Exception e)
                {
                    error = e is TimeoutException ? new InvalidOperationException($"Tabs that were closed to save \"{Path.GetFileName(target)}\" are still reading it, save again in a moment.") : e;
                }
            };
            progressDialog.ShowDialog();

            return written ? null : error ?? new InvalidOperationException($"\"{Path.GetFileName(target)}\" was not saved.");
        }

        /// <summary>
        /// Asks to save the changes of a package being edited before its tab closes.
        /// </summary>
        private bool ConfirmCloseTab(TabPage tab)
        {
            if (PackageViewer.FromTab(tab) is not { HasUnsavedChanges: true } viewer)
            {
                return true;
            }

            mainTabs.SelectTab(tab);

            return AskToSavePackage(viewer) switch
            {
                SaveChangesChoice.DontSave => true,
                SaveChangesChoice.Save => SavePackage(viewer, tab, saveAs: false, out _) != null,
                _ => false,
            };
        }

        private static SaveChangesChoice AskToSavePackage(PackageViewer viewer)
        {
            var name = viewer.FileOnDisk is { } path ? Path.GetFileName(path) : "the new VPK";

            return AppMessageDialogs.AskToSaveChanges($"Save changes to {name}?", "Unsaved changes");
        }

        /// <summary>
        /// Asks to save the changes of every package being edited before the app closes.
        /// </summary>
        /// <returns>Whether the app can close.</returns>
        private bool ConfirmCloseApp()
        {
            foreach (var tab in mainTabs.TabPages.Cast<TabPage>().ToList())
            {
                if (tab.Parent == null || PackageViewer.FromTab(tab) is not { HasUnsavedChanges: true } viewer)
                {
                    continue;
                }

                mainTabs.SelectTab(tab);

                switch (AskToSavePackage(viewer))
                {
                    case SaveChangesChoice.DontSave:
                    case SaveChangesChoice.Save when SavePackage(viewer, tab, saveAs: false, out _) != null:
                        break;

                    default:
                        return false;
                }
            }

            return true;
        }

        public void ShowVpkEditingContextMenu(Control control, Point position, bool isRootNode, bool isFolderNode, bool hasItem)
        {
            vpkEditRemoveThisFileToolStripMenuItem.Visible = hasItem && !isFolderNode;
            vpkEditRemoveThisFolderToolStripMenuItem.Visible = hasItem && isFolderNode && !isRootNode;
            vpkEditAddExistingFilesToolStripMenuItem.Visible = isFolderNode;
            vpkEditAddExistingFolderToolStripMenuItem.Visible = isFolderNode;
            vpkEditCreateFolderToolStripMenuItem.Visible = isFolderNode;
            vpkEditRenameToolStripMenuItem.Visible = hasItem && !isRootNode;
            vpkEditSeparator1.Visible = hasItem;
            vpkEditExtractToolStripMenuItem.Visible = hasItem;
            vpkEditDecompileToolStripMenuItem.Visible = hasItem;

            vpkEditingContextMenu.Show(control, position);
        }

        private void OnVpkCreateFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using var dialog = new PromptForm("New folder name");

            if (dialog.ShowDialog() != DialogResult.OK || dialog.ResultText.Length < 1)
            {
                return;
            }

            if (mainTabs.SelectedTab is { } tab && PackageViewer.FromTab(tab) is { } viewer)
            {
                viewer.AddFolder(dialog.ResultText);
            }
        }

        private void OnVpkAddNewFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var folders = AppFileDialogs.PickFolders("Choose which folders to add to the VPK", AppFileDialogs.RememberIn.OpenDirectory);

            if (folders != null && mainTabs.SelectedTab is { } tab && PackageViewer.FromTab(tab) is { } viewer)
            {
                viewer.AddFolders(folders);
            }
        }

        private void OnVpkAddNewFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var files = AppFileDialogs.OpenFiles("Choose which files to add to the VPK", null);

            if (files != null && mainTabs.SelectedTab is { } tab && PackageViewer.FromTab(tab) is { } viewer)
            {
                viewer.AddFiles(files);
            }
        }

        private async void OnVpkEditingRenameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var (context, selectedItems) = GetSelectedItems(sender);

            if (context == null || selectedItems is not [var item] || item is BetterListViewItem { Tag: BetterListViewItem.ParentNavigationTag }
                || mainTabs.SelectedTab is not { } tab || PackageViewer.FromTab(tab) is not { } viewer)
            {
                return;
            }

            var currentName = item.PackageEntry?.GetFileName() ?? item.PkgNode?.Name ?? string.Empty;
            using var dialog = new PromptForm("New name", currentName);

            if (dialog.ShowDialog() != DialogResult.OK || dialog.ResultText.Length < 1 || dialog.ResultText == currentName)
            {
                return;
            }

            await viewer.RenameItemAsync(item, dialog.ResultText).ConfigureAwait(true);
        }

        private void OnVpkEditingRemoveThisToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var (context, selectedItems) = GetSelectedItems(sender);

            if (context != null && mainTabs.SelectedTab is { } tab && PackageViewer.FromTab(tab) is { } viewer)
            {
                viewer.RemoveItems(selectedItems.Where(static item => item is not BetterListViewItem { Tag: BetterListViewItem.ParentNavigationTag }));
            }
        }
    }
}
