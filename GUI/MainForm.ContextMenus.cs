using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Types.Exporter;
using GUI.Types.PackageViewer;
using GUI.Utils;
using SteamDatabase.ValvePak;

namespace GUI
{
    partial class MainForm
    {
        public void ShowVpkContextMenu(Control control, Point position, bool isRootNode, bool isFolderNode)
        {
            copyFileNameToolStripMenuItem.Visible = !isRootNode;
            openWithDefaultAppToolStripMenuItem.Visible = !isRootNode && !isFolderNode;
            openWithoutViewerToolStripMenuItem.Visible = !isRootNode && !isFolderNode;
            viewAssetInfoToolStripMenuItem.Visible = !isRootNode && !isFolderNode;
            toolStripSeparator3.Visible = isRootNode || !isFolderNode;

            verifyPackageContentsToolStripMenuItem.Visible = isRootNode;
            recoverDeletedToolStripMenuItem.Visible = isRootNode;

            vpkContextMenu.Show(control, position);
        }

        public void ShowVpkEditingContextMenu(Control control, Point position, bool isRootNode, bool isFolderNode)
        {
            vpkEditSaveToDiskToolStripMenuItem.Visible = isRootNode;
            vpkEditRemoveThisFileToolStripMenuItem.Visible = !isFolderNode;
            vpkEditRemoveThisFolderToolStripMenuItem.Visible = isFolderNode && !isRootNode;
            vpkEditAddExistingFilesToolStripMenuItem.Visible = isFolderNode;
            vpkEditAddExistingFolderToolStripMenuItem.Visible = isFolderNode;
            vpkEditCreateFolderToolStripMenuItem.Visible = isFolderNode;

            vpkEditingContextMenu.Show(control, position);
        }

        private static TabPage FetchToolstripTabContext(object sender)
        {
            if (sender is not ToolStripMenuItem { Owner: ContextMenuStrip { SourceControl: ThemedTabControl tabControl } contextMenu })
            {
                throw new InvalidDataException("Invalid context menu structure");
            }

            var tabs = tabControl.TabPages;

            if (contextMenu.Tag is not Point location)
            {
                throw new InvalidDataException("Context menu tag is not a Point");
            }

            return tabs.Cast<TabPage>().Where((t, i) => tabControl.GetTabRect(i).Contains(location)).First();
        }

        private void CloseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            mainTabs.CloseTab(FetchToolstripTabContext(sender));
        }

        private void CloseToolStripMenuItemsToLeft_Click(object sender, EventArgs e)
        {
            CloseTabsToLeft(FetchToolstripTabContext(sender));
        }

        private void CloseToolStripMenuItemsToRight_Click(object sender, EventArgs e)
        {
            CloseTabsToRight(FetchToolstripTabContext(sender));
        }

        private void CloseToolStripMenuItems_Click(object sender, EventArgs e)
        {
            CloseAllTabs();
        }

        private void CopyFileNameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CopyFileName(sender, wantsFullPath: false);
        }

        private void CopyFileNameOnDiskToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CopyFileName(sender, wantsFullPath: true);
        }

        private static void CopyFileName(object sender, bool wantsFullPath)
        {
            if (sender is not ToolStripMenuItem { Owner: ContextMenuStrip { SourceControl: var control } })
            {
                return;
            }

            VrfGuiContext? context;
            List<IBetterBaseItem> selectedNodes;

            if (control is BetterTreeView treeView)
            {
                context = treeView.VrfGuiContext;

                if (treeView.SelectedNode is not IBetterBaseItem selectedNode)
                {
                    return;
                }

                selectedNodes = [selectedNode];
            }
            else if (control is BetterListView listView)
            {
                context = listView.VrfGuiContext;
#pragma warning disable IDE0028 // Simplify collection initialization - it doesn't work
                selectedNodes = [];

                foreach (var item in listView.SelectedItems)
                {
                    if (item is IBetterBaseItem selectedNode)
                    {
                        selectedNodes.Add(selectedNode);
                    }
                }
#pragma warning restore IDE0028
            }
            else
            {
                throw new InvalidDataException("Unknown state");
            }

            if (context == null)
            {
                return;
            }

            var sb = new StringBuilder();

            foreach (var selectedNode in selectedNodes)
            {
                if (wantsFullPath)
                {
                    sb.Append("vpk:");
                    sb.Append(context.FileName.Replace('\\', '/'));
                }

                if (!selectedNode.IsFolder)
                {
                    if (wantsFullPath)
                    {
                        sb.Append(':');
                    }

                    var packageEntry = selectedNode.PackageEntry;
                    if (packageEntry != null)
                    {
                        sb.Append(packageEntry.GetFullPath());
                    }
                }
                else
                {
                    var stack = new Stack<string>();
                    var node = selectedNode.PkgNode;

                    if (node == null)
                    {
                        continue;
                    }

                    if (wantsFullPath && node.Parent != null)
                    {
                        sb.Append(':');
                    }

                    do
                    {
                        if (node.Parent == null)
                        {
                            break;
                        }

                        stack.Push(node.Name);
                        node = node.Parent;
                    }
                    while (node != null);

                    while (stack.TryPop(out var name))
                    {
                        sb.Append(name);
                        sb.Append(Package.DirectorySeparatorChar);
                    }
                }
            }

            Clipboard.SetText(sb.ToString());
        }

        private void OpenWithoutViewerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var (guiContext, selectedNode) = GetSingleSelectedNode(sender);
            if (guiContext == null || selectedNode?.PackageEntry == null)
            {
                return;
            }

            var newContext = new VrfGuiContext(selectedNode.PackageEntry.GetFullPath(), guiContext);
            OpenFile(newContext, selectedNode.PackageEntry, null, withoutViewer: true);
        }

        private void OpenWithDefaultAppToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (sender is not ToolStripMenuItem { Owner: ContextMenuStrip { SourceControl: var control } })
            {
                return;
            }

            VrfGuiContext? context;
            List<PackageEntry> selectedFiles;

            if (control is BetterTreeView treeView)
            {
                context = treeView.VrfGuiContext;

                if (treeView.SelectedNode is not IBetterBaseItem treeNode)
                {
                    return;
                }

                if (treeNode.IsFolder)
                {
                    return;
                }

                if (treeNode.PackageEntry == null)
                {
                    return;
                }

                selectedFiles = [treeNode.PackageEntry];
            }
            else if (control is BetterListView listView)
            {
                context = listView.VrfGuiContext;
                selectedFiles = [];

                foreach (var item in listView.SelectedItems)
                {
                    if (item is not IBetterBaseItem selectedNode)
                    {
                        continue;
                    }

                    if (selectedNode.IsFolder)
                    {
                        return;
                    }

                    if (selectedNode.PackageEntry != null)
                    {
                        selectedFiles.Add(selectedNode.PackageEntry);
                    }
                }
            }
            else
            {
                throw new InvalidDataException("Unknown state");
            }

            if (context == null)
            {
                return;
            }

            if (selectedFiles.Count > 5 && MessageBox.Show(
                $"You are trying to open {selectedFiles.Count} files in the default app for each of these files, are you sure you want to continue?",
                "Trying to open many files in the default app",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            ) != DialogResult.Yes)
            {
                return;
            }

            foreach (var file in selectedFiles)
            {
                if (context.CurrentPackage == null)
                {
                    continue;
                }

                context.CurrentPackage.ReadEntry(file, out var output, validateCrc: file.CRC32 > 0);

                var tempPath = $"{Path.GetTempPath()}Source 2 Viewer - {Path.GetFileName(context.CurrentPackage.FileName)} - {file.GetFileName()}";
                using (var stream = new FileStream(tempPath, FileMode.Create))
                {
                    stream.Write(output, 0, output.Length);
                }

                try
                {
                    Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log.Error(nameof(MainForm), $"Failed to start process: {ex.Message}");
                }
            }
        }

        private void OnViewAssetInfoToolStripMenuItemClick(object sender, EventArgs e)
        {
            var (guiContext, selectedNode) = GetSingleSelectedNode(sender);
            if (guiContext == null || selectedNode == null || selectedNode.IsFolder || selectedNode.PackageEntry == null)
            {
                return;
            }

            Cursor.Current = Cursors.WaitCursor;

            try
            {
                var tab = Types.Viewers.SingleAssetInfo.Create(guiContext, selectedNode.PackageEntry);

                if (tab != null)
                {
                    tab.ImageIndex = Icons["Info"];
                    mainTabs.TabPages.Add(tab);
                    mainTabs.SelectTab(tab);
                }
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private static (VrfGuiContext?, IBetterBaseItem?) GetSingleSelectedNode(object sender)
        {
            if (sender is not ToolStripMenuItem { Owner: ContextMenuStrip { SourceControl: var control } })
            {
                throw new InvalidDataException("Invalid context menu structure");
            }

            VrfGuiContext? guiContext;
            IBetterBaseItem? selectedNode;

            if (control is BetterTreeView treeView)
            {
                guiContext = treeView.VrfGuiContext;
                selectedNode = treeView.SelectedNode as IBetterBaseItem;
            }
            else if (control is BetterListView listView)
            {
                guiContext = listView.VrfGuiContext;
                selectedNode = listView.SelectedItems.Count > 0 ? listView.SelectedItems[0] as IBetterBaseItem : null;
            }
            else
            {
                throw new InvalidDataException("Unknown state");
            }

            return (guiContext, selectedNode);
        }

        private void DecompileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ExtractFiles(sender, true);
        }

        private void ExtractToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ExtractFiles(sender, false);
        }

        private static void ExtractFiles(object sender, bool decompile)
        {
            if (sender is not ToolStripMenuItem { Owner: ContextMenuStrip { SourceControl: var owner } })
            {
                throw new InvalidDataException("Invalid context menu structure");
            }

            // Clicking context menu item in left side of the package view
            if (owner is BetterTreeView tree)
            {
                if (tree.SelectedNode is IBetterBaseItem treeNode && tree.VrfGuiContext != null)
                {
                    ExportFile.ExtractFilesFromTreeNode(treeNode, tree.VrfGuiContext, decompile);
                }
            }
            // Clicking context menu item in right side of the package view
            else if (owner is BetterListView listView)
            {
                if (listView.VrfGuiContext == null)
                {
                    return;
                }

                if (listView.SelectedItems.Count > 1)
                {
                    // We're selecting multiple files
                    ExportFile.ExtractFilesFromListViewNodes(listView.SelectedItems, listView.VrfGuiContext, decompile);
                }
                else
                {
                    ExportFile.ExtractFilesFromTreeNode((IBetterBaseItem)listView.SelectedItems[0], listView.VrfGuiContext, decompile);
                }
            }
            // Clicking context menu item when right clicking a tab
            else if (owner is TabControl)
            {
                var tabPage = FetchToolstripTabContext(sender);

                if (tabPage.Tag is not ExportData exportData)
                {
                    throw new InvalidDataException("There is no export data for this tab");
                }

                if (exportData.PackageEntry != null)
                {
                    ExportFile.ExtractFileFromPackageEntry(exportData.PackageEntry, exportData.VrfGuiContext, decompile);
                }
                else
                {
                    var fileStream = File.OpenRead(exportData.VrfGuiContext.FileName);

                    try
                    {
                        ExportFile.ExtractFileFromStream(Path.GetFileName(exportData.VrfGuiContext.FileName), fileStream, exportData.VrfGuiContext, decompile);
                        fileStream = null; // ExtractFileFromStream should dispose it when done, not `using` here in case there's some threading
                    }
                    finally
                    {
                        fileStream?.Dispose();
                    }
                }
            }
            else
            {
                throw new InvalidDataException("Unknown state");
            }
        }

        private void RecoverDeletedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            recoverDeletedToolStripMenuItem.Enabled = false;

            if (mainTabs.SelectedTab?.Controls[nameof(TreeViewWithSearchResults)] is TreeViewWithSearchResults treeView)
            {
                treeView.RecoverDeletedFiles();
            }
        }

        private void VerifyPackageContentsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (mainTabs.SelectedTab?.Controls[nameof(TreeViewWithSearchResults)] is TreeViewWithSearchResults treeView)
            {
                treeView.VerifyPackageContents();
            }
        }

        private void CreateVpkFromFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var newVrfGuiContext = new VrfGuiContext("new.vpk", null);

            try
            {
#pragma warning disable CA2000 // If creating an empty TabPage throws, all hope is lost
                var contents = new PackageViewer(newVrfGuiContext).CreateEmpty();
#pragma warning restore CA2000

                var tab = new ThemedTabPage("New VPK")
                {
                    ToolTipText = "New VPK"
                };
                tab.Controls.Add(contents);
                tab.ImageIndex = ExtensionIcons["vpk"];
                mainTabs.TabPages.Add(tab);
                mainTabs.SelectTab(tab);

                newVrfGuiContext = null;
            }
            finally
            {
                newVrfGuiContext?.Dispose();
            }

        }

        private void RegisterVpkFileAssociationToolStripMenuItem_Click(object sender, EventArgs e) => SettingsControl.RegisterFileAssociation();

        private void OnVpkCreateFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using var dialog = new PromptForm("New folder name");

            if (dialog.ShowDialog() != DialogResult.OK || dialog.ResultText.Length < 1)
            {
                return;
            }

            var directory = dialog.ResultText;

            if (directory.IndexOfAny(Path.GetInvalidPathChars()) != -1)
            {
                MessageBox.Show("Entered folder name contains invalid characters.", "Invalid characters");
                return;
            }

            if (mainTabs.SelectedTab?.Controls[nameof(TreeViewWithSearchResults)] is TreeViewWithSearchResults treeViewWithSearchResults)
            {
                treeViewWithSearchResults.Viewer.AddFolder(directory);
            }
        }

        private void OnVpkAddNewFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using var openDialog = new FolderBrowserDialog
            {
                Description = "Choose which folder to pack into a VPK",
                UseDescriptionForTitle = true,
                SelectedPath = Settings.Config.OpenDirectory,
                AddToRecent = true,
            };

            if (openDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            var inputDirectory = openDialog.SelectedPath;
            Settings.Config.OpenDirectory = inputDirectory;

            if (mainTabs.SelectedTab?.Controls[nameof(TreeViewWithSearchResults)] is TreeViewWithSearchResults treeViewWithSearchResults)
            {
                treeViewWithSearchResults.Viewer.AddFilesFromFolder(inputDirectory);
            }
        }

        private void OnVpkAddNewFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using var openDialog = new OpenFileDialog
            {
                Title = "Choose which files to add to the VPK",
                InitialDirectory = Settings.Config.OpenDirectory,
                AddToRecent = true,
                Multiselect = true,
            };

            if (openDialog.ShowDialog() != DialogResult.OK || openDialog.FileNames.Length < 1)
            {
                return;
            }

            var inputDirectory = openDialog.FileNames;
            var directory = Path.GetDirectoryName(openDialog.FileName);
            if (directory != null)
            {
                Settings.Config.OpenDirectory = directory;
            }

            if (mainTabs.SelectedTab?.Controls[nameof(TreeViewWithSearchResults)] is TreeViewWithSearchResults treeViewWithSearchResults)
            {
                treeViewWithSearchResults.Viewer.AddFiles(openDialog.FileNames);
            }
        }

        private void OnVpkEditingRemoveThisToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (mainTabs.SelectedTab?.Controls[nameof(TreeViewWithSearchResults)] is TreeViewWithSearchResults treeViewWithSearchResults)
            {
                treeViewWithSearchResults.Viewer.RemoveCurrentFiles();
            }
        }

        private void OnSaveVPKToDiskToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using var saveDialog = new SaveFileDialog
            {
                InitialDirectory = Settings.Config.SaveDirectory,
                AddToRecent = true,
                Title = "Save VPK package",
                DefaultExt = "vpk",
                Filter = "Valve Pak|*.vpk"
            };

            if (saveDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            var directory = Path.GetDirectoryName(saveDialog.FileName);
            if (directory != null)
            {
                Settings.Config.SaveDirectory = directory;
            }

            Log.Info(nameof(MainForm), $"Packing to '{saveDialog.FileName}'...");

            if (mainTabs.SelectedTab?.Controls[nameof(TreeViewWithSearchResults)] is TreeViewWithSearchResults treeViewWithSearchResults)
            {
                treeViewWithSearchResults.Viewer.SaveToFile(saveDialog.FileName);
            }
        }

        private void OnOpenWelcomeScreenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenWelcome();
        }

        private void OnValidateShadersToolStripMenuItem_Click(object sender, EventArgs e)
        {
#if DEBUG
            ValidateShaders();
#endif
        }
    }
}
