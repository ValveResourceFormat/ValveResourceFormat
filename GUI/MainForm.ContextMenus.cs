using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
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

            var packageViewer = (mainTabs.SelectedTab.Controls["TreeViewWithSearchResults"] as TreeViewWithSearchResults).Viewer;
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

            var packageViewer = (mainTabs.SelectedTab.Controls["TreeViewWithSearchResults"] as TreeViewWithSearchResults).Viewer;
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

            var packageViewer = (mainTabs.SelectedTab.Controls["TreeViewWithSearchResults"] as TreeViewWithSearchResults).Viewer;
            packageViewer.AddFiles(openDialog.FileNames);
        }

        private void OnVpkEditingRemoveThisToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var packageViewer = (mainTabs.SelectedTab.Controls["TreeViewWithSearchResults"] as TreeViewWithSearchResults).Viewer;
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

            var packageViewer = (mainTabs.SelectedTab.Controls["TreeViewWithSearchResults"] as TreeViewWithSearchResults).Viewer;
            packageViewer.SaveToFile(saveDialog.FileName);
        }
    }
}
