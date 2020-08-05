using System;
using System.IO;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using SteamDatabase.ValvePak;

namespace GUI.Types.Viewers
{
    public class Package : IViewer
    {
        public ImageList ImageList { get; set; }

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

            // create a TreeView with search capabilities, register its events, and add it to the tab
            var treeViewWithSearch = new TreeViewWithSearchResults(ImageList);
            treeViewWithSearch.InitializeTreeViewFromPackage(vrfGuiContext.FileName, new TreeViewWithSearchResults.TreeViewPackageTag
            {
                Package = package,
                ParentFileLoader = vrfGuiContext.FileLoader,
            });
            treeViewWithSearch.TreeNodeMouseDoubleClick += VPK_OpenFile;
            treeViewWithSearch.TreeNodeRightClick += VPK_OnClick;
            treeViewWithSearch.ListViewItemDoubleClick += VPK_OpenFile;
            treeViewWithSearch.ListViewItemRightClick += VPK_OnClick;
            treeViewWithSearch.Disposed += VPK_Disposed;
            tab.Controls.Add(treeViewWithSearch);

            return tab;
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
            if (node.Tag.GetType() == typeof(PackageEntry))
            {
                var package = node.TreeView.Tag as TreeViewWithSearchResults.TreeViewPackageTag;
                var file = node.Tag as PackageEntry;
                package.Package.ReadEntry(file, out var output);

                Program.MainForm.OpenFile(file.GetFileName(), output, package);
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
