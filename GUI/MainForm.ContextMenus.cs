using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Types.Exporter;
using GUI.Utils;

namespace GUI
{
    partial class MainForm
    {
        public void ShowVpkContextMenu(Control control, Point position, bool isRootNode)
        {
            copyFileNameToolStripMenuItem.Visible = !isRootNode;
            openWithDefaultAppToolStripMenuItem.Visible = !isRootNode;
            viewAssetInfoToolStripMenuItem.Visible = !isRootNode;

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
            var contextMenu = ((ToolStripMenuItem)sender).Owner;
            var tabControl = ((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl as TabControl;
            var tabs = tabControl.TabPages;

            return tabs.Cast<TabPage>().Where((t, i) => tabControl.GetTabRect(i).Contains((Point)contextMenu.Tag)).First();
        }

        private void CloseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CloseTab(FetchToolstripTabContext(sender));
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
            var control = ((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl;
            VrfGuiContext context;
            List<TreeNode> selectedNodes;

            if (control is BetterTreeView treeView)
            {
                context = treeView.VrfGuiContext;
                selectedNodes =
                [
                    treeView.SelectedNode
                ];
            }
            else if (control is BetterListView listView)
            {
                context = listView.VrfGuiContext;
                selectedNodes = new List<TreeNode>(listView.SelectedItems.Count);

                foreach (ListViewItem selectedNode in listView.SelectedItems)
                {
                    selectedNodes.Add((BetterTreeNode)selectedNode.Tag);
                }
            }
            else
            {
                throw new InvalidDataException("Unknown state");
            }

            var wantsFullPath = ModifierKeys.HasFlag(Keys.Shift);
            var sb = new StringBuilder();

            foreach (var selectedNode in selectedNodes.Cast<BetterTreeNode>())
            {
                if (wantsFullPath)
                {
                    sb.Append("vpk:");
                    sb.Append(context.FileName);
                    sb.Append(':');
                }

                if (!selectedNode.IsFolder)
                {
                    var packageEntry = selectedNode.PackageEntry;
                    sb.AppendLine(packageEntry.GetFullPath());
                }
                else
                {
                    sb.AppendLine(selectedNode.Name);
                }
            }

            Clipboard.SetText(sb.ToString().TrimEnd());
        }

        private void OpenWithDefaultAppToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var control = ((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl;
            List<BetterTreeNode> selectedNodes;

            if (control is TreeView treeView)
            {
                var treeNode = (BetterTreeNode)treeView.SelectedNode;

                if (treeNode.IsFolder)
                {
                    return;
                }

                selectedNodes = [treeNode];
            }
            else if (control is ListView listView)
            {
                selectedNodes = new List<BetterTreeNode>(listView.SelectedItems.Count);

                foreach (ListViewItem selectedNode in listView.SelectedItems)
                {
                    var treeNode = (BetterTreeNode)selectedNode.Tag;

                    if (treeNode.IsFolder)
                    {
                        return;
                    }

                    selectedNodes.Add(treeNode);
                }
            }
            else
            {
                throw new InvalidDataException("Unknown state");
            }

            if (selectedNodes.Count > 5 && MessageBox.Show(
                $"You are trying to open {selectedNodes.Count} files in the default app for each of these files, are you sure you want to continue?",
                "Trying to open many files in the default app",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            ) != DialogResult.Yes)
            {
                return;
            }

            foreach (var selectedNode in selectedNodes)
            {
                if (selectedNode.TreeView is not BetterTreeView nodeTreeView)
                {
                    throw new InvalidOperationException("Unexpected tree view");
                }

                var file = selectedNode.PackageEntry;
                nodeTreeView.VrfGuiContext.CurrentPackage.ReadEntry(file, out var output, validateCrc: file.CRC32 > 0);

                var tempPath = $"{Path.GetTempPath()}Source 2 Viewer - {Path.GetFileName(nodeTreeView.VrfGuiContext.CurrentPackage.FileName)} - {file.GetFileName()}";
                using (var stream = new FileStream(tempPath, FileMode.Create))
                {
                    stream.Write(output, 0, output.Length);
                }

                try
                {
                    Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true }).Start();
                }
                catch (Exception ex)
                {
                    Log.Error(nameof(MainForm), $"Failed to start process: {ex.Message}");
                }
            }
        }

        private void OnViewAssetInfoToolStripMenuItemClick(object sender, EventArgs e)
        {
            var control = ((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl;
            BetterTreeNode selectedNode;
            VrfGuiContext guiContext;

            if (control is BetterTreeView treeView)
            {
                guiContext = treeView.VrfGuiContext;
                selectedNode = (BetterTreeNode)treeView.SelectedNode;
            }
            else if (control is BetterListView listView)
            {
                guiContext = listView.VrfGuiContext;
                selectedNode = (BetterTreeNode)listView.SelectedItems[0].Tag;
            }
            else
            {
                throw new InvalidDataException("Unknown state");
            }

            if (selectedNode.IsFolder)
            {
                return;
            }

            var tab = Types.Viewers.SingleAssetInfo.Create(guiContext, selectedNode.PackageEntry);

            if (tab != null)
            {
                mainTabs.TabPages.Add(tab);
                mainTabs.SelectTab(tab);
            }
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
            var owner = (ContextMenuStrip)((ToolStripMenuItem)sender).Owner;

            // Clicking context menu item in left side of the package view
            if (owner.SourceControl is BetterTreeView tree)
            {
                ExportFile.ExtractFilesFromTreeNode((BetterTreeNode)tree.SelectedNode, tree.VrfGuiContext, decompile);
            }
            // Clicking context menu item in right side of the package view
            else if (owner.SourceControl is BetterListView listView)
            {
                if (listView.SelectedItems.Count > 1)
                {
                    // We're selecting multiple files
                    ExportFile.ExtractFilesFromListViewNodes(listView.SelectedItems, listView.VrfGuiContext, decompile);
                }
                else
                {
                    ExportFile.ExtractFilesFromTreeNode((BetterTreeNode)listView.SelectedItems[0].Tag, listView.VrfGuiContext, decompile);
                }
            }
            // Clicking context menu item when right clicking a tab
            else if (owner.SourceControl is TabControl)
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

            var treeView = mainTabs.SelectedTab.Controls[nameof(TreeViewWithSearchResults)] as TreeViewWithSearchResults;
            treeView.RecoverDeletedFiles();
        }

        private void VerifyPackageContentsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var treeView = mainTabs.SelectedTab.Controls[nameof(TreeViewWithSearchResults)] as TreeViewWithSearchResults;
            treeView.VerifyPackageContents();
        }

        private void CreateVpkFromFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var contents = new Types.Viewers.Package().CreateEmpty(new VrfGuiContext("new.vpk", null));

            var tab = new TabPage("New VPK")
            {
                ToolTipText = "New VPK"
            };
            tab.Controls.Add(contents);
            tab.ImageIndex = ImageListLookup["vpk"];
            mainTabs.TabPages.Add(tab);
            mainTabs.SelectTab(tab);
        }

        private void RegisterVpkFileAssociationToolStripMenuItem_Click(object sender, EventArgs e) => SettingsForm.RegisterFileAssociation();

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

            var packageViewer = (mainTabs.SelectedTab.Controls[nameof(TreeViewWithSearchResults)] as TreeViewWithSearchResults).Viewer;
            packageViewer.AddFolder(directory);
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

            var packageViewer = (mainTabs.SelectedTab.Controls[nameof(TreeViewWithSearchResults)] as TreeViewWithSearchResults).Viewer;
            packageViewer.AddFilesFromFolder(inputDirectory);
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
            Settings.Config.OpenDirectory = Path.GetDirectoryName(openDialog.FileName);

            var packageViewer = (mainTabs.SelectedTab.Controls[nameof(TreeViewWithSearchResults)] as TreeViewWithSearchResults).Viewer;
            packageViewer.AddFiles(openDialog.FileNames);
        }

        private void OnVpkEditingRemoveThisToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var packageViewer = (mainTabs.SelectedTab.Controls[nameof(TreeViewWithSearchResults)] as TreeViewWithSearchResults).Viewer;
            packageViewer.RemoveCurrentFiles();
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

            Settings.Config.SaveDirectory = Path.GetDirectoryName(saveDialog.FileName);

            Log.Info(nameof(MainForm), $"Packing to '{saveDialog.FileName}'...");

            var packageViewer = (mainTabs.SelectedTab.Controls[nameof(TreeViewWithSearchResults)] as TreeViewWithSearchResults).Viewer;
            packageViewer.SaveToFile(saveDialog.FileName);
        }
    }
}
