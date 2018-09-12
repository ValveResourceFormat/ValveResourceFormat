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
using GUI.Forms;
using GUI.Types;
using GUI.Types.Audio;
using GUI.Types.Renderer;
using GUI.Types.Renderer.Animation;
using GUI.Utils;
using OpenTK;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.KeyValues;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.Animation;
using Model = GUI.Types.Model;
using Texture = ValveResourceFormat.ResourceTypes.Texture;

namespace GUI
{
    public partial class MainForm : Form
    {
        private class ExportData
        {
            public Resource Resource { get; set; }
            public Renderer Renderer { get; set; }
        }

        private readonly SearchForm searchForm;
        private readonly Regex NewLineRegex;
        private ImageList ImageList;

        public MainForm()
        {
            LoadAssetTypes();
            InitializeComponent();

            mainTabs.SelectedIndexChanged += (o, e) =>
            {
                if (mainTabs.SelectedTab != null)
                {
                    var treeView = mainTabs.SelectedTab.Controls["TreeViewWithSearchResults"] as TreeViewWithSearchResults;
                    findToolStripButton.Enabled = treeView != null;
                }
            };

            searchForm = new SearchForm();

            Settings.Load();

            NewLineRegex = new Regex(@"\r\n|\n\r|\n|\r", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string file = args[i];
                if (File.Exists(file) && IsCompatibleFile(file))
                {
                    OpenFile(file);
                }
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // so we can bind keys to actions properly
            KeyPreview = true;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // if the user presses CTRL + F, show the search form
            if (keyData == (Keys.Control | Keys.F))
            {
                findToolStripButton.PerformClick();
            }

            //if the user presses CTRL + W, and there is a tab open, close the active tab
            if (keyData == (Keys.Control | Keys.W) && mainTabs.SelectedTab != null)
            {
                mainTabs.TabPages.Remove(mainTabs.SelectedTab);

                // enable/disable the search button as necessary
                if (mainTabs.TabCount > 0 && mainTabs.SelectedTab != null)
                {
                    var treeView = mainTabs.SelectedTab.Controls["TreeViewWithSearchResults"] as TreeViewWithSearchResults;
                    findToolStripButton.Enabled = treeView != null;
                }
                else
                {
                    findToolStripButton.Enabled = false;
                }
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
                    .First());
            }
            else if (e.Button == MouseButtons.Right)
            {
                contextMenuStrip1.Tag = e.Location;
                contextMenuStrip1.Show((Control)sender, e.Location);
            }
        }

        private void OnAboutItemClick(object sender, EventArgs e)
        {
            var form = new AboutForm();
            form.ShowDialog(this);
        }

        private void OnSettingsItemClick(object sender, EventArgs e)
        {
            var form = new SettingsForm();
            form.ShowDialog(this);
        }

        private bool IsCompatibleFile(string file)
        {
            return file.EndsWith("_c", StringComparison.Ordinal) || file.EndsWith(".vpk", StringComparison.Ordinal) || file.EndsWith(".vcs", StringComparison.Ordinal);
        }

        private void OpenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var openDialog = new OpenFileDialog
            {
                InitialDirectory = Settings.Config.OpenDirectory,
                Filter = "Valve Resource Format (*.*_c, *.vpk)|*.*_c;*.vpk;*.vcs|All files (*.*)|*.*",
                Multiselect = true,
            };
            var userOK = openDialog.ShowDialog();

            if (userOK != DialogResult.OK)
            {
                return;
            }

            if (openDialog.FileNames.Length > 0)
            {
                Settings.Config.OpenDirectory = Path.GetDirectoryName(openDialog.FileNames[0]);
                Settings.Save();
            }

            foreach (var file in openDialog.FileNames)
            {
                if (IsCompatibleFile(file))
                {
                    OpenFile(file);
                }
                else
                {
                    Process.Start(file);
                }
            }
        }

        private void OpenFile(string fileName, byte[] input = null, Package currentPackage = null)
        {
            var tab = new TabPage(Path.GetFileName(fileName));
            tab.Controls.Add(new LoadingFile());

            mainTabs.TabPages.Add(tab);
            mainTabs.SelectTab(tab);

            var task = Task.Factory.StartNew(() => ProcessFile(fileName, input, currentPackage));

            task.ContinueWith(
                t =>
                {
                    t.Exception?.Flatten().Handle(ex =>
                    {
                        mainTabs.TabPages.Remove(tab);

                        Console.WriteLine(ex);

                        Invoke(new Action(() => MessageBox.Show(ex.Message + Environment.NewLine + Environment.NewLine + ex.StackTrace, "Failed to read package", MessageBoxButtons.OK, MessageBoxIcon.Error)));

                        return false;
                    });
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.FromCurrentSynchronizationContext());

            task.ContinueWith(
                t =>
                {
                    tab.Controls.Clear();

                    foreach (Control c in t.Result.Controls)
                    {
                        tab.Controls.Add(c);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.FromCurrentSynchronizationContext());
        }

        private TabPage ProcessFile(string fileName, byte[] input, Package currentPackage)
        {
            var tab = new TabPage();

            if (fileName.EndsWith(".vpk", StringComparison.Ordinal))
            {
                var package = new Package();

                try
                {
                    if (input != null)
                    {
                        package.SetFileName(fileName);
                        package.Read(new MemoryStream(input));
                    }
                    else
                    {
                        package.Read(fileName);
                    }
                }
                catch (InvalidDataException) when (input == null && Regex.IsMatch(fileName, @"_[0-9]{3}\.vpk$"))
                {
                    // TODO: Update tab name
                    fileName = $"{fileName.Substring(0, fileName.Length - 8)}_dir.vpk";

                    package.Read(fileName);
                }

                // create a TreeView with search capabilities, register its events, and add it to the tab
                var treeViewWithSearch = new TreeViewWithSearchResults(ImageList)
                {
                    Dock = DockStyle.Fill,
                };
                treeViewWithSearch.InitializeTreeViewFromPackage("treeViewVpk", package);
                treeViewWithSearch.TreeNodeMouseDoubleClick += VPK_OpenFile;
                treeViewWithSearch.TreeNodeMouseClick += VPK_OnClick;
                treeViewWithSearch.ListViewItemDoubleClick += VPK_OpenFile;
                treeViewWithSearch.ListViewItemRightClick += VPK_OnClick;
                tab.Controls.Add(treeViewWithSearch);

                // since we're in a separate thread, invoke to update the UI
                Invoke((MethodInvoker)(() => findToolStripButton.Enabled = true));
            }
            else if (fileName.EndsWith(".vcs", StringComparison.Ordinal))
            {
                var shader = new CompiledShader();

                var buffer = new StringWriter();
                var oldOut = Console.Out;
                Console.SetOut(buffer);

                if (input != null)
                {
                    shader.Read(fileName, new MemoryStream(input));
                }
                else
                {
                    shader.Read(fileName);
                }

                Console.SetOut(oldOut);

                var control = new TextBox();
                control.Font = new Font(FontFamily.GenericMonospace, control.Font.Size);
                control.Text = NormalizeLineEndings(buffer.ToString());
                control.Dock = DockStyle.Fill;
                control.Multiline = true;
                control.ReadOnly = true;
                control.ScrollBars = ScrollBars.Both;
                tab.Controls.Add(control);
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

                var resTabs = new TabControl
                {
                    Dock = DockStyle.Fill,
                };

                switch (resource.ResourceType)
                {
                    case ResourceType.Texture:
                        var tab2 = new TabPage("TEXTURE")
                        {
                            AutoScroll = true,
                        };

                        try
                        {
                            var tex = (Texture)resource.Blocks[BlockType.DATA];

                            var control = new Forms.Texture
                            {
                                BackColor = Color.Black,
                            };
                            control.SetImage(tex.GenerateBitmap().ToBitmap(), Path.GetFileNameWithoutExtension(fileName), tex.Width, tex.Height);

                            tab2.Controls.Add(control);
                            Invoke(new ExportDel(AddToExport), $"Export {Path.GetFileName(fileName)} as an image", fileName, new ExportData { Resource = resource });
                        }
                        catch (Exception e)
                        {
                            var control = new TextBox
                            {
                                Dock = DockStyle.Fill,
                                Font = new Font(FontFamily.GenericMonospace, 8),
                                Multiline = true,
                                ReadOnly = true,
                                Text = e.ToString(),
                            };

                            tab2.Controls.Add(control);
                        }

                        resTabs.TabPages.Add(tab2);
                        break;
                    case ResourceType.Panorama:
                        if (((Panorama)resource.Blocks[BlockType.DATA]).Names.Count > 0)
                        {
                            var nameTab = new TabPage("PANORAMA NAMES");
                            var nameControl = new DataGridView
                            {
                                Dock = DockStyle.Fill,
                                AutoSize = true,
                                ReadOnly = true,
                                AllowUserToAddRows = false,
                                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                                DataSource = new BindingSource(new BindingList<Panorama.NameEntry>(((Panorama)resource.Blocks[BlockType.DATA]).Names), null),
                            };
                            nameTab.Controls.Add(nameControl);
                            resTabs.TabPages.Add(nameTab);
                        }

                        break;
                    case ResourceType.PanoramaLayout:
                        Invoke(new ExportDel(AddToExport), $"Export {Path.GetFileName(fileName)} as XML", fileName, new ExportData { Resource = resource });
                        break;
                    case ResourceType.PanoramaScript:
                        Invoke(new ExportDel(AddToExport), $"Export {Path.GetFileName(fileName)} as JS", fileName, new ExportData { Resource = resource });
                        break;
                    case ResourceType.PanoramaStyle:
                        Invoke(new ExportDel(AddToExport), $"Export {Path.GetFileName(fileName)} as CSS", fileName, new ExportData { Resource = resource });
                        break;
                    case ResourceType.Sound:
                        var soundTab = new TabPage("SOUND");
                        var ap = new AudioPlayer(resource, soundTab);
                        resTabs.TabPages.Add(soundTab);

                        Invoke(new ExportDel(AddToExport), $"Export {Path.GetFileName(fileName)} as {((Sound)resource.Blocks[BlockType.DATA]).Type}", fileName, new ExportData { Resource = resource });

                        break;
                    case ResourceType.World:
                        var world = new World(resource);
                        var worldmv = new Renderer(mainTabs, fileName, currentPackage, RenderSubject.World);
                        world.AddObjects(worldmv, fileName, currentPackage);

                        var worldmeshTab = new TabPage("MAP");
                        var worldglControl = worldmv.CreateGL();
                        worldmeshTab.Controls.Add(worldglControl);
                        resTabs.TabPages.Add(worldmeshTab);
                        break;
                    case ResourceType.WorldNode:
                        var node = new WorldNode(resource);
                        var nodemv = new Renderer(mainTabs, fileName, currentPackage);
                        node.AddMeshes(nodemv, fileName, currentPackage);

                        var nodemeshTab = new TabPage("MAP");
                        var nodeglControl = nodemv.CreateGL();
                        nodemeshTab.Controls.Add(nodeglControl);
                        resTabs.TabPages.Add(nodemeshTab);
                        break;
                    case ResourceType.Model:
                        // Create model
                        var model = new Model(resource);

                        // Create skeleton
                        var skeleton = new Skeleton(resource);

                        // Create tab
                        var modelmeshTab = new TabPage("MESH");
                        var modelmv = new Renderer(mainTabs, fileName, currentPackage, RenderSubject.Model);
                        model.LoadMeshes(modelmv, fileName, Matrix4.Identity, Vector4.One, currentPackage);

                        // Add skeleton to renderer
                        modelmv.SetSkeleton(skeleton);

                        // Add animations if available
                        var animGroupPaths = model.GetAnimationGroups();
                        foreach (var animGroupPath in animGroupPaths)
                        {
                            var animGroup = FileExtensions.LoadFileByAnyMeansNecessary(animGroupPath + "_c", fileName, currentPackage);

                            modelmv.AddAnimations(AnimationGroupLoader.LoadAnimationGroup(animGroup, fileName));
                        }

                        //Initialise OpenGL
                        var modelglControl = modelmv.CreateGL();
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
                        var mv = new Renderer(mainTabs, fileName, currentPackage, RenderSubject.Model);

                        Invoke(new ExportDel(AddToExport), $"Export {Path.GetFileName(fileName)} as OBJ", fileName, new ExportData { Resource = resource, Renderer = mv });

                        mv.AddMeshObject(new MeshObject { Resource = resource });
                        var glControl = mv.CreateGL();
                        meshTab.Controls.Add(glControl);
                        resTabs.TabPages.Add(meshTab);
                        break;
                }

                foreach (var block in resource.Blocks)
                {
                    if (block.Key == BlockType.RERL)
                    {
                        var externalRefsTab = new TabPage("External Refs");

                        var externalRefs = new DataGridView
                        {
                            Dock = DockStyle.Fill,
                            AutoGenerateColumns = true,
                            AutoSize = true,
                            ReadOnly = true,
                            AllowUserToAddRows = false,
                            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                            DataSource = new BindingSource(new BindingList<ResourceExtRefList.ResourceReferenceInfo>(resource.ExternalReferences.ResourceRefInfoList), null),
                        };

                        externalRefsTab.Controls.Add(externalRefs);

                        resTabs.TabPages.Add(externalRefsTab);

                        continue;
                    }

                    if (block.Key == BlockType.NTRO)
                    {
                        if (((ResourceIntrospectionManifest)block.Value).ReferencedStructs.Count > 0)
                        {
                            var externalRefsTab = new TabPage("Introspection Manifest: Structs");

                            var externalRefs = new DataGridView
                            {
                                Dock = DockStyle.Fill,
                                AutoGenerateColumns = true,
                                AutoSize = true,
                                ReadOnly = true,
                                AllowUserToAddRows = false,
                                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                                DataSource = new BindingSource(new BindingList<ResourceIntrospectionManifest.ResourceDiskStruct>(((ResourceIntrospectionManifest)block.Value).ReferencedStructs), null),
                            };

                            externalRefsTab.Controls.Add(externalRefs);
                            resTabs.TabPages.Add(externalRefsTab);
                        }

                        if (((ResourceIntrospectionManifest)block.Value).ReferencedEnums.Count > 0)
                        {
                            var externalRefsTab = new TabPage("Introspection Manifest: Enums");
                            var externalRefs2 = new DataGridView
                            {
                                Dock = DockStyle.Fill,
                                AutoGenerateColumns = true,
                                AutoSize = true,
                                ReadOnly = true,
                                AllowUserToAddRows = false,
                                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                                DataSource = new BindingSource(new BindingList<ResourceIntrospectionManifest.ResourceDiskEnum>(((ResourceIntrospectionManifest)block.Value).ReferencedEnums), null),
                            };

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
                                    control.Text = NormalizeLineEndings(new KV3File(((BinaryKV3)block.Value).Data).ToString());
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

        private void OpenFileFromNode(TreeNode node)
        {
            //Make sure we aren't a directory!
            if (node.Tag.GetType() == typeof(PackageEntry))
            {
                var package = node.TreeView.Tag as Package;
                var file = node.Tag as PackageEntry;
                package.ReadEntry(file, out var output);

                if (IsCompatibleFile(file.GetFullPath()))
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
        /// <param name="sender">Object which raised event.</param>
        /// <param name="e">Event data.</param>
        private void VPK_OnClick(object sender, ListViewItemClickEventArgs e)
        {
            if (e.Tag is ListViewItem listViewItem && listViewItem.Tag is TreeNode node)
            {
                node.TreeView.SelectedNode = node; //To stop it spassing out
                vpkContextMenu.Show(listViewItem.ListView, e.Location);
            }
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);

            foreach (var fileName in files)
            {
                if (IsCompatibleFile(fileName))
                {
                    OpenFile(fileName);
                }
            }
        }

        private void MainForm_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Move;
            }
        }

        private void CloseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var contextMenu = ((ToolStripMenuItem)sender).Owner;
            var tabControl = ((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl as TabControl;
            var tabs = tabControl.TabPages;

            tabs.Remove(tabs.Cast<TabPage>()
                .Where((t, i) => tabControl.GetTabRect(i).Contains((Point)contextMenu.Tag))
                .First());

            // enable/disable the search button as necessary
            if (mainTabs.TabCount > 0 && mainTabs.SelectedTab != null)
            {
                var treeView = mainTabs.SelectedTab.Controls["TreeViewWithSearchResults"] as TreeViewWithSearchResults;
                findToolStripButton.Enabled = treeView != null;
            }
            else
            {
                findToolStripButton.Enabled = false;
            }
        }

        private void ExtractToolStripMenuItem_Click(object sender, EventArgs e)
        {
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
                // We are a file
                var file = selectedNode.Tag as PackageEntry;

                var dialog = new SaveFileDialog
                {
                    InitialDirectory = Settings.Config.SaveDirectory,
                    Filter = "All files (*.*)|*.*",
                    FileName = file.FileName + "." + file.TypeName,
                };
                var userOK = dialog.ShowDialog();

                if (userOK == DialogResult.OK)
                {
                    Settings.Config.SaveDirectory = Path.GetDirectoryName(dialog.FileName);
                    Settings.Save();

                    using (var stream = dialog.OpenFile())
                    {
                        package.ReadEntry(file, out var output);
                        stream.Write(output, 0, output.Length);
                    }
                }
            }
            else
            {
                //We are a folder
                var dialog = new FolderBrowserDialog();
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    ExtractFolderFromTree(package, selectedNode, dialog.SelectedPath);
                }
            }
        }

        private void ExtractFolderFromTree(Package package, TreeNode root, string path)
        {
            foreach (TreeNode node in root.Nodes)
            {
                if (node.Tag.GetType() == typeof(PackageEntry))
                {
                    var file = node.Tag as PackageEntry;
                    var filePath = Path.Combine(path, file.DirectoryName, file.FileName + "." + file.TypeName);

                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        package.ReadEntry(file, out var output);
                        stream.Write(output, 0, output.Length);
                    }
                }
                else
                {
                    ExtractFolderFromTree(package, node, path);
                }
            }
        }

        /// <summary>
        /// When the user clicks to search from the toolbar, open a dialog with search options. If the user clicks OK in the dialog,
        /// perform a search in the selected tab's TreeView for the entered value and display the results in a ListView.
        /// </summary>
        /// <param name="sender">Object which raised event.</param>
        /// <param name="e">Event data.</param>
        private void FindToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var result = searchForm.ShowDialog();
            if (result == DialogResult.OK)
            {
                // start searching only if the user entered non-empty string, a tab exists, and a tab is selected
                var searchText = searchForm.SearchText;
                if (!string.IsNullOrEmpty(searchText) && mainTabs.TabCount > 0 && mainTabs.SelectedTab != null)
                {
                    var treeView = mainTabs.SelectedTab.Controls["TreeViewWithSearchResults"] as TreeViewWithSearchResults;
                    treeView.SearchAndFillResults(searchText, searchForm.SelectedSearchType);
                }
            }
        }

        private string NormalizeLineEndings(string input)
        {
            return NewLineRegex.Replace(input, Environment.NewLine);
        }

        private delegate void ExportDel(string name, string filename, ExportData data);

        private void AddToExport(string name, string filename, ExportData data)
        {
            exportToolStripButton.Enabled = true;

            var ts = new ToolStripMenuItem
            {
                Size = new Size(150, 20),
                Text = name,
                ToolTipText = filename,
                Tag = data,
            };
            //This is required for the dialog to know the default name and path.
            //This makes it trivial to dump without exploring our nested TabPages.
            ts.Click += ExportToolStripMenuItem_Click;

            exportToolStripButton.DropDownItems.Add(ts);
        }

        private void ExportToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //ToolTipText is the full filename
            var fileName = ((ToolStripMenuItem)sender).ToolTipText;
            var tag = ((ToolStripMenuItem)sender).Tag as ExportData;
            var resource = tag.Resource;

            Console.WriteLine($"Export requested for {fileName}");

            string[] extensions = null;
            switch (resource.ResourceType)
            {
                case ResourceType.Sound:
                    //WAV or MP3
                    extensions = new[] { ((Sound)resource.Blocks[BlockType.DATA]).Type.ToString().ToLower() };
                    break;
                case ResourceType.Texture:
                    extensions = new[] { "png", "jpg", "gif", "bmp" };
                    break;
                case ResourceType.PanoramaLayout:
                    extensions = new[] { "xml", "vxml" };
                    break;
                case ResourceType.PanoramaScript:
                    extensions = new[] { "js", "vjs" };
                    break;
                case ResourceType.PanoramaStyle:
                    extensions = new[] { "css", "vcss" };
                    break;
                case ResourceType.Mesh:
                    extensions = new[] { "obj" };
                    break;
            }

            //Did we find a format we like?
            if (extensions != null)
            {
                var dialog = new SaveFileDialog
                {
                    FileName = Path.GetFileName(Path.ChangeExtension(fileName, extensions[0])),
                    InitialDirectory = Settings.Config.SaveDirectory,
                    DefaultExt = extensions[0],
                };

                var filter = string.Empty;
                foreach (var extension in extensions)
                {
                    filter += $"{extension} files (*.{extension})|*.{extension}|";
                }

                //Remove the last |
                dialog.Filter = filter.Substring(0, filter.Length - 1);

                var result = dialog.ShowDialog();

                if (result == DialogResult.OK)
                {
                    Settings.Config.SaveDirectory = Path.GetDirectoryName(dialog.FileName);
                    Settings.Save();

                    using (var stream = dialog.OpenFile())
                    {
                        switch (resource.ResourceType)
                        {
                            case ResourceType.Sound:
                                var soundData = ((Sound)resource.Blocks[BlockType.DATA]).GetSound();
                                stream.Write(soundData, 0, soundData.Length);
                                break;
                            case ResourceType.Texture:
                                var format = SKEncodedImageFormat.Png;
                                switch (dialog.FilterIndex)
                                {
                                    case 2:
                                        format = SKEncodedImageFormat.Jpeg;
                                        break;

                                    case 3:
                                        format = SKEncodedImageFormat.Gif;
                                        break;
                                    case 4:
                                        format = SKEncodedImageFormat.Bmp;
                                        break;
                                }

                                var image = SKImage.FromBitmap(((Texture)resource.Blocks[BlockType.DATA]).GenerateBitmap());

                                using (var data = image.Encode(format, 100))
                                {
                                    data.SaveTo(stream);
                                }

                                break;
                            case ResourceType.PanoramaLayout:
                            case ResourceType.PanoramaScript:
                            case ResourceType.PanoramaStyle:
                                var panoramaData = ((Panorama)resource.Blocks[BlockType.DATA]).Data;
                                stream.Write(panoramaData, 0, panoramaData.Length);
                                break;
                            case ResourceType.Mesh:
                                using (var objStream = new StreamWriter(stream))
                                using (var mtlStream = new StreamWriter(Path.ChangeExtension(dialog.FileName, "mtl")))
                                {
                                    MeshObject.WriteObject(objStream, mtlStream, Path.GetFileNameWithoutExtension(dialog.FileName), resource);
                                }

                                foreach (var texture in tag.Renderer.MaterialLoader.LoadedTextures)
                                {
                                    Console.WriteLine($"Exporting texture for mesh: {texture}");

                                    var textureResource = FileExtensions.LoadFileByAnyMeansNecessary(texture + "_c", tag.Renderer.CurrentFileName, tag.Renderer.CurrentPackage);
                                    var textureImage = SKImage.FromBitmap(((Texture)textureResource.Blocks[BlockType.DATA]).GenerateBitmap());

                                    using (var texStream = new FileStream(Path.Combine(Path.GetDirectoryName(dialog.FileName), Path.GetFileNameWithoutExtension(texture) + ".png"), FileMode.Create, FileAccess.Write))
                                    using (var data = textureImage.Encode(SKEncodedImageFormat.Png, 100))
                                    {
                                        data.SaveTo(texStream);
                                    }
                                }

                                break;
                        }
                    }
                }
            }

            Console.WriteLine($"Export requested for {fileName} Complete");
        }
    }
}
