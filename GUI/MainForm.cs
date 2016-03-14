using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;

namespace GUI
{
    public partial class MainForm : Form
    {
        private ImageList ImageList;
        private Forms.SearchForm searchForm;
        private Regex NewLineRegex;

        public MainForm()
        {
            LoadAssetTypes();
            InitializeComponent();

            mainTabs.SelectedIndexChanged += (o, e) =>
            {
                if (mainTabs.SelectedTab != null)
                {
                    var treeView = mainTabs.SelectedTab.Controls["TreeViewWithSearchResults"] as Controls.TreeViewWithSearchResults;
                    findToolStripButton.Enabled = (treeView != null);
                }
            };
            
            searchForm = new Forms.SearchForm();

            Settings.Load();

            NewLineRegex = new Regex(@"\r\n|\n\r|\n|\r", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // so we can bind keys to actions properly
            this.KeyPreview = true;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // if the user presses CTRL + F, show the search form
            if (keyData == (Keys.Control | Keys.F))
            {
                findToolStripButton.PerformClick();
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void LoadAssetTypes()
        {
            ImageList = new ImageList();

            var assembly = Assembly.GetExecutingAssembly();
            var names = assembly.GetManifestResourceNames().Where(n => n.StartsWith("GUI.AssetTypes.", StringComparison.Ordinal));

            foreach (var name in names)
            {
                var res = name.Split('.');

                using (var stream = assembly.GetManifestResourceStream(name))
                {
                    ImageList.Images.Add(res[2], Image.FromStream(stream));
                }
            }
        }

        private void OnTabClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                var tabControl = sender as TabControl;
                var tabs = tabControl.TabPages;

                tabs.Remove(tabs.Cast<TabPage>()
                    .Where((t, i) => tabControl.GetTabRect(i).Contains(e.Location))
                    .First()
                );
            }
            else if (e.Button == MouseButtons.Right)
            {
                contextMenuStrip1.Tag = e.Location;
                contextMenuStrip1.Show((Control)sender, e.Location);
            }
        }

        private void OnAboutItemClick(object sender, EventArgs e)
        {
            var form = new Forms.AboutForm();
            form.ShowDialog(this);
        }

        private void OnSettingsItemClick(object sender, EventArgs e)
        {
            var form = new Forms.SettingsForm();
            form.ShowDialog(this);
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var openDialog = new OpenFileDialog();
            openDialog.Filter = "Valve Resource Format (*.*_c, *.vpk)|*.*_c;*.vpk|All files (*.*)|*.*";
            openDialog.Multiselect = true;
            var userOK = openDialog.ShowDialog();

            if (userOK == DialogResult.OK)
            {
                foreach (var file in openDialog.FileNames)
                {
                    if (file.EndsWith("_c", StringComparison.Ordinal) || file.EndsWith(".vpk", StringComparison.Ordinal))
                    {
                        OpenFile(file);
                    }
                    else
                    {
                        Process.Start(file);
                    }
                }
            }
        }

        private void OpenFile(string fileName, byte[] input = null, Package currentPackage = null)
        {
            var tab = new TabPage(Path.GetFileName(fileName));
            tab.Controls.Add(new Forms.LoadingFile());

            mainTabs.TabPages.Add(tab);
            mainTabs.SelectTab(tab);

            var task = Task.Factory.StartNew(() => ProcessFile(fileName, input, currentPackage));

            task.ContinueWith(t =>
            {
                t.Exception.Flatten().Handle(ex =>
                {
                    mainTabs.TabPages.Remove(tab);

                    this.Invoke(new Action(() =>
                        MessageBox.Show(ex.Message + Environment.NewLine + Environment.NewLine + ex.StackTrace, "Failed to read package", MessageBoxButtons.OK, MessageBoxIcon.Error))
                    );

                    return true;
                });
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.FromCurrentSynchronizationContext());

            task.ContinueWith(t =>
            {
                tab.Controls.Clear();

                foreach (Control c in t.Result.Controls)
                {
                    tab.Controls.Add(c);
                }
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private TabPage ProcessFile(string fileName, byte[] input, Package currentPackage)
        {
            var tab = new TabPage();

            if (fileName.EndsWith(".vpk", StringComparison.Ordinal))
            {
                var package = new Package();
                if (input != null)
                {
                    package.SetFileName(fileName);
                    package.Read(new MemoryStream(input));
                }
                else
                {
                    package.Read(fileName);
                }

                // create a TreeView with search capabilities, register its events, and add it to the tab
                var treeViewWithSearch = new GUI.Controls.TreeViewWithSearchResults(ImageList);
                treeViewWithSearch.Dock = DockStyle.Fill;
                treeViewWithSearch.InitializeTreeViewFromPackage("treeViewVpk", package);
                treeViewWithSearch.TreeNodeMouseDoubleClick += VPK_OpenFile;
                treeViewWithSearch.TreeNodeMouseClick += VPK_OnClick;
                treeViewWithSearch.ListViewItemDoubleClick += VPK_OpenFile;
                treeViewWithSearch.ListViewItemRightClick += VPK_OnClick;
                tab.Controls.Add(treeViewWithSearch);

                // since we're in a separate thread, invoke to update the UI
                this.Invoke((MethodInvoker)delegate
                {
                    findToolStripButton.Enabled = true;
                });
            }
            else
            {
                var resource = new Resource();
                if (input != null)
                {
                    resource.Read(new MemoryStream(input));
                }
                else
                {
                    resource.Read(fileName);
                }

                var resTabs = new TabControl();
                resTabs.Dock = DockStyle.Fill;

                switch (resource.ResourceType)
                {
                    case ResourceType.Texture:
                        var tab2 = new TabPage("TEXTURE");
                        tab2.AutoScroll = true;

                        try
                        {
                            var tex = (Texture)resource.Blocks[BlockType.DATA];

                            var control = new Forms.Texture();
                            control.BackColor = Color.Black;
                            control.SetImage(tex.GenerateBitmap(), Path.GetFileNameWithoutExtension(fileName), tex.Width, tex.Height);

                            tab2.Controls.Add(control);
                        }
                        catch (Exception e)
                        {
                            var control = new TextBox
                            {
                                Dock = DockStyle.Fill,
                                Font = new Font(FontFamily.GenericMonospace, 8),
                                Multiline = true,
                                ReadOnly = true,
                                Text = e.ToString()
                            };

                            tab2.Controls.Add(control);
                        }

                        resTabs.TabPages.Add(tab2);
                        break;
                    case ResourceType.Panorama:
                        if (((Panorama)resource.Blocks[BlockType.DATA]).Names.Count > 0)
                        {
                            var nameTab = new TabPage("PANORAMA NAMES");
                            var nameControl = new DataGridView();
                            nameControl.Dock = DockStyle.Fill;
                            nameControl.AutoSize = true;
                            nameControl.ReadOnly = true;
                            nameControl.AllowUserToAddRows = false;
                            nameControl.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                            nameControl.DataSource = new BindingSource(new BindingList<Panorama.NameEntry>(((Panorama)resource.Blocks[BlockType.DATA]).Names), null);
                            nameTab.Controls.Add(nameControl);
                            resTabs.TabPages.Add(nameTab);
                        }

                        break;
                    case ResourceType.Sound:
                        var soundTab = new TabPage("SOUND");
                        var ap = new Types.Audio.Player(resource);
                        resTabs.TabPages.Add(soundTab);

                        Invoke((MethodInvoker)delegate
                        {
                            exportToolStripButton.Enabled = true;

                            var ts = new ToolStripMenuItem();
                            ts.Name = "WAV";
                            ts.Size = new Size(150, 20);
                            ts.Text = "WAV";
                            ts.Click += new EventHandler(exportToolStripMenuItem_Click);

                            exportToolStripButton.DropDownItems.Add(ts);
                        });

                        break;
                    case ResourceType.Model:
                        var model = new Types.Model(resource);

                        var animGroupPath = model.GetAnimationGroup();
                        if (!string.IsNullOrEmpty(animGroupPath))
                        {
                            var animGroup = new Resource();
                            animGroup.Read(animGroupPath);

                            var animGroupLoader = new Types.Renderer.AnimationGroupLoader(animGroup, fileName);
                        }

                        var resourceMesh = new Resource();
                        model.GetMesh(resourceMesh, currentPackage);

                        if (!resourceMesh.Blocks.ContainsKey(BlockType.VBIB))
                        {
                            Console.WriteLine("Old style model, no VBIB!");
                            break;
                        }
                        var modelmeshTab = new TabPage("MESH");
                        var modelmv = new Types.Renderer.Renderer(resourceMesh, mainTabs, fileName, currentPackage);
                        var modelglControl = modelmv.createGL();
                        modelmeshTab.Controls.Add(modelglControl);
                        resTabs.TabPages.Add(modelmeshTab);
                        break;
                    case ResourceType.Mesh:
                        if (!resource.Blocks.ContainsKey(BlockType.VBIB))
                        {
                            Console.WriteLine("Old style model, no VBIB!");
                            break;
                        }
                        var meshTab = new TabPage("MESH");
                        var mv = new Types.Renderer.Renderer(resource, mainTabs, fileName, currentPackage);
                        var glControl = mv.createGL();
                        meshTab.Controls.Add(glControl);
                        resTabs.TabPages.Add(meshTab);
                        break;
                }

                foreach (var block in resource.Blocks)
                {
                    if (block.Key == BlockType.RERL)
                    {
                        var externalRefsTab = new TabPage("External Refs");

                        var externalRefs = new DataGridView();
                        externalRefs.Dock = DockStyle.Fill;
                        externalRefs.AutoGenerateColumns = true;
                        externalRefs.AutoSize = true;
                        externalRefs.ReadOnly = true;
                        externalRefs.AllowUserToAddRows = false;
                        externalRefs.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                        externalRefs.DataSource = new BindingSource(new BindingList<ResourceExtRefList.ResourceReferenceInfo>(resource.ExternalReferences.ResourceRefInfoList), null);

                        externalRefsTab.Controls.Add(externalRefs);

                        resTabs.TabPages.Add(externalRefsTab);

                        continue;
                    }

                    if (block.Key == BlockType.NTRO)
                    {
                        if (((ResourceIntrospectionManifest)block.Value).ReferencedStructs.Count > 0)
                        {
                            var externalRefsTab = new TabPage("Introspection Manifest: Structs");

                            var externalRefs = new DataGridView();
                            externalRefs.Dock = DockStyle.Fill;
                            externalRefs.AutoGenerateColumns = true;
                            externalRefs.AutoSize = true;
                            externalRefs.ReadOnly = true;
                            externalRefs.AllowUserToAddRows = false;
                            externalRefs.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                            externalRefs.DataSource = new BindingSource(new BindingList<ResourceIntrospectionManifest.ResourceDiskStruct>(((ResourceIntrospectionManifest)block.Value).ReferencedStructs), null);

                            externalRefsTab.Controls.Add(externalRefs);
                            resTabs.TabPages.Add(externalRefsTab);
                        }

                        if (((ResourceIntrospectionManifest)block.Value).ReferencedEnums.Count > 0)
                        {
                            var externalRefsTab = new TabPage("Introspection Manifest: Enums");
                            var externalRefs2 = new DataGridView();
                            externalRefs2.Dock = DockStyle.Fill;
                            externalRefs2.AutoGenerateColumns = true;
                            externalRefs2.AutoSize = true;
                            externalRefs2.ReadOnly = true;
                            externalRefs2.AllowUserToAddRows = false;
                            externalRefs2.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                            externalRefs2.DataSource = new BindingSource(new BindingList<ResourceIntrospectionManifest.ResourceDiskEnum>(((ResourceIntrospectionManifest)block.Value).ReferencedEnums), null);

                            externalRefsTab.Controls.Add(externalRefs2);
                            resTabs.TabPages.Add(externalRefsTab);
                        }

                        //continue;
                    }

                    var tab2 = new TabPage(block.Key.ToString());
                    var control = new TextBox();
                    control.Font = new Font(FontFamily.GenericMonospace, control.Font.Size);
                    try
                    {
                        if (block.Key == BlockType.DATA)
                        {
                            switch (resource.ResourceType)
                            {
                                case ResourceType.Particle:
                                case ResourceType.Mesh:
                                    //Wrap it around a KV3File object to get the header.
                                    control.Text = NormalizeLineEndings(new ValveResourceFormat.KeyValues.KV3File(((BinaryKV3)block.Value).Data).ToString());
                                    break;
                                default:
                                    control.Text = NormalizeLineEndings(block.Value.ToString());
                                    break;
                            }
                        }
                        else
                        {
                            control.Text = NormalizeLineEndings(block.Value.ToString());
                        }
                        
                    }
                    catch (Exception e)
                    {
                        control.Text = e.ToString();
                    }

                    control.Dock = DockStyle.Fill;
                    control.Multiline = true;
                    control.ReadOnly = true;
                    control.ScrollBars = ScrollBars.Both;
                    tab2.Controls.Add(control);
                    resTabs.TabPages.Add(tab2);
                }

                tab.Controls.Add(resTabs);
            }

            return tab;
        }

        /// <summary>
        /// Opens a file based on a double clicked list view item. Does nothing if the double clicked item contains a non-TreeNode object.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void VPK_OpenFile(object sender, ListViewItemClickEventArgs e)
        {
            var node = e.Tag as TreeNode;
            if (node != null)
            {
                OpenFileFromNode(node);
            }
        }

        private void VPK_OpenFile(object sender, TreeNodeMouseClickEventArgs e)
        {
            var node = e.Node;
            OpenFileFromNode(node);
        }

        private void OpenFileFromNode(TreeNode node)
        {
            //Make sure we aren't a directory!
            if (node.Tag.GetType() == typeof(PackageEntry))
            {
                var package = node.TreeView.Tag as Package;
                var file = node.Tag as PackageEntry;
                byte[] output;
                package.ReadEntry(file, out output);
                if (file.TypeName.EndsWith("_c", StringComparison.Ordinal) || file.TypeName == "vpk")
                {
                    OpenFile(file.FileName + "." + file.TypeName, output, package);
                }
                else
                {
                    var tempPath = Path.GetTempPath() + Path.GetFileName(package.FileName) + " - " + file.FileName + "." + file.TypeName; // ew
                    using (var stream = new FileStream(tempPath, FileMode.Create))
                    {
                        stream.Write(output, 0, output.Length);
                    }
                    Process.Start(tempPath);
                }
            }
        }

        private void VPK_OnClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            e.Node.TreeView.SelectedNode = e.Node; //To stop it spassing out
            if (e.Button == MouseButtons.Right)
            {
                vpkContextMenu.Show(e.Node.TreeView, e.Location);
            }
        }

        /// <summary>
        /// Opens a context menu where the user right-clicked in the ListView.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void VPK_OnClick(object sender, ListViewItemClickEventArgs e)
        {
            var listViewItem = e.Tag as ListViewItem;
            if (listViewItem != null)
            {
                var node = listViewItem.Tag as TreeNode;
                if (node != null)
                {
                    node.TreeView.SelectedNode = node; //To stop it spassing out
                    vpkContextMenu.Show(listViewItem.ListView, e.Location);
                }
            }
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

            foreach (string fileName in files)
            {
                OpenFile(fileName);
            }
        }

        private void MainForm_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Move;
            }
        }

        private void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var contextMenu = ((ToolStripMenuItem)sender).Owner;
            var tabControl = ((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl as TabControl;
            var tabs = tabControl.TabPages;

            tabs.Remove(tabs.Cast<TabPage>()
                .Where((t, i) => tabControl.GetTabRect(i).Contains((Point)contextMenu.Tag))
                .First()
            );

            // enable/disable the search button as necessary
            if (mainTabs.TabCount > 0 && mainTabs.SelectedTab != null)
            {
                var treeView = mainTabs.SelectedTab.Controls["TreeViewWithSearchResults"] as Controls.TreeViewWithSearchResults;
                findToolStripButton.Enabled = (treeView != null);
            }
            else
            {
                findToolStripButton.Enabled = false;
            }
        }

        private void extractToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var contextMenu = ((ToolStripMenuItem)sender).Owner;

            Package package = null;
            TreeNode selectedNode = null;

            // the context menu can come from a TreeView or a ListView depending on where the user clicked to extract
            // each option has a difference in where we can get the values to extract
            if (((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl is TreeView)
            {
                var tree = ((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl as TreeView;
                selectedNode = tree.SelectedNode;
                package = tree.Tag as Package;
            }
            else if (((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl is ListView)
            {
                var listView = ((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl as ListView;
                selectedNode = listView.SelectedItems[0].Tag as TreeNode;
                package = listView.Tag as Package;
            }

            if (selectedNode.Tag.GetType() == typeof(PackageEntry))
            {
                //We are a file
                var file = selectedNode.Tag as PackageEntry;

                var dialog = new SaveFileDialog();
                dialog.Filter = "All files (*.*)|*.*";
                dialog.FileName = file.FileName + "." + file.TypeName;
                var userOK = dialog.ShowDialog();

                if (userOK == DialogResult.OK)
                {
                    using (var stream = dialog.OpenFile())
                    {
                        byte[] output;
                        package.ReadEntry(file, out output);
                        stream.Write(output, 0, output.Length);
                    }
                }
            }
            else
            {
                //We are a folder
                MessageBox.Show("Folder Extraction coming Soon™");
            }
        }

        /// <summary>
        /// When the user clicks to search from the toolbar, open a dialog with search options. If the user clicks OK in the dialog, 
        /// perform a search in the selected tab's TreeView for the entered value and display the results in a ListView.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void findToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DialogResult result = searchForm.ShowDialog();
            if (result == DialogResult.OK)
            {
                // start searching only if the user entered non-empty string, a tab exists, and a tab is selected
                string searchText = searchForm.SearchText;
                if (!String.IsNullOrEmpty(searchText) && mainTabs.TabCount > 0 && mainTabs.SelectedTab != null)
                {
                    var treeView = mainTabs.SelectedTab.Controls["TreeViewWithSearchResults"] as Controls.TreeViewWithSearchResults;
                    treeView.SearchAndFillResults(searchText, searchForm.IsCaseSensitive, searchForm.SelectedSearchType);
                }
            }
        }

        private string NormalizeLineEndings(string input)
        {
            return NewLineRegex.Replace(input, Environment.NewLine);
        }

        private void exportToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // No idea yet what to do from here
            Console.WriteLine("Export requested to " + ((ToolStripMenuItem)sender).Text);
        }
    }
}
