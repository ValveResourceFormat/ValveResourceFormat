using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GUI.Controls;
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

        public void ShowVpkEditingContextMenu(Control control, Point position)
        {
            vpkEditingContextMenu.Show(control, position);
        }

        private void OnVpkAddNewFileToolStripMenuItem_Click(object sender, EventArgs e)
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
