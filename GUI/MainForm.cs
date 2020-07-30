using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Types.Audio;
using GUI.Types.Exporter;
using GUI.Types.ParticleRenderer;
using GUI.Types.Renderer;
using GUI.Utils;
using SkiaSharp.Views.Desktop;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ClosedCaptions;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ToolsAssetInfo;
using Texture = ValveResourceFormat.ResourceTypes.Texture;

namespace GUI
{
    public partial class MainForm : Form
    {
        private readonly Regex NewLineRegex;
        private SearchForm searchForm;
#pragma warning disable CA2213
        // Disposable fields should be disposed
        // for some reason disposing it makes closing GUI very slow
        private ImageList ImageList;
#pragma warning restore CA2213

        public MainForm()
        {
            LoadAssetTypes();
            InitializeComponent();

            Text = "VRF - Source 2 Resource Viewer v" + Application.ProductVersion;

            mainTabs.SelectedIndexChanged += (o, e) =>
            {
                if (mainTabs.SelectedTab != null)
                {
                    findToolStripButton.Enabled = mainTabs.SelectedTab.Controls["TreeViewWithSearchResults"] is TreeViewWithSearchResults;
                }
            };

            mainTabs.TabPages.Add(ConsoleTab.CreateTab());

            Console.WriteLine($"VRF v{Application.ProductVersion}");

            searchForm = new SearchForm();

            Settings.Load();

            NewLineRegex = new Regex(@"\r\n|\n\r|\n|\r", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)
            {
                string file = args[i];
                if (File.Exists(file))
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
            //if the user presses CTRL + W, and there is a tab open, close the active tab
            if (keyData == (Keys.Control | Keys.W) && mainTabs.SelectedTab != null)
            {
                CloseTab(mainTabs.SelectedTab);
            }

            //if the user presses CTRL + Q, close all open tabs
            if (keyData == (Keys.Control | Keys.Q))
            {
                CloseAllTabs();
            }

            //if the user presses CTRL + E, close all tabs to the right of the active tab
            if (keyData == (Keys.Control | Keys.E))
            {
                CloseTabsToRight(mainTabs.SelectedTab);
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ShowHideSearch()
        {
            // enable/disable the search button as necessary
            if (mainTabs.TabCount > 0 && mainTabs.SelectedTab != null)
            {
                findToolStripButton.Enabled = mainTabs.SelectedTab.Controls["TreeViewWithSearchResults"] is TreeViewWithSearchResults;
            }
            else
            {
                findToolStripButton.Enabled = false;
            }
        }

        private int GetTabIndex(TabPage tab)
        {
            //Work out the index of the requested tab
            for (int i = 0; i < mainTabs.TabPages.Count; i++)
            {
                if (mainTabs.TabPages[i] == tab)
                {
                    return i;
                }
            }

            return -1;
        }

        private void CloseTab(TabPage tab)
        {
            var tabIndex = GetTabIndex(tab);
            var isClosingCurrentTab = tabIndex == mainTabs.SelectedIndex;

            //The console cannot be closed!
            if (tabIndex == 0)
            {
                return;
            }

            //Close the requested tab
            Console.WriteLine($"Closing {tab.Text}");
            mainTabs.TabPages.Remove(tab);

            if (isClosingCurrentTab && tabIndex > 0)
            {
                mainTabs.SelectedIndex = tabIndex - 1;
            }

            ShowHideSearch();

            tab.Dispose();
        }

        private void CloseAllTabs()
        {
            //Close all tabs currently open (excluding console)
            int tabCount = mainTabs.TabPages.Count;
            for (int i = 1; i < tabCount; i++)
            {
                CloseTab(mainTabs.TabPages[tabCount - i]);
            }

            ShowHideSearch();
        }

        private void CloseTabsToLeft(TabPage basePage)
        {
            if (mainTabs.SelectedTab == null)
            {
                return;
            }

            //Close all tabs to the left of the base (excluding console)
            for (int i = GetTabIndex(basePage); i > 0; i--)
            {
                CloseTab(mainTabs.TabPages[i]);
            }

            ShowHideSearch();
        }

        private void CloseTabsToRight(TabPage basePage)
        {
            if (mainTabs.SelectedTab == null)
            {
                return;
            }

            //Close all tabs to the right of the base one
            int tabCount = mainTabs.TabPages.Count;
            for (int i = 1; i < tabCount; i++)
            {
                if (mainTabs.TabPages[tabCount - i] == basePage)
                {
                    break;
                }

                CloseTab(mainTabs.TabPages[tabCount - i]);
            }

            ShowHideSearch();
        }

        private void LoadAssetTypes()
        {
            ImageList = new ImageList();
            ImageList.ColorDepth = ColorDepth.Depth32Bit;

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
            //Work out what tab we're interacting with
            var tabControl = sender as TabControl;
            var tabs = tabControl.TabPages;
            TabPage thisTab = tabs.Cast<TabPage>().Where((t, i) => tabControl.GetTabRect(i).Contains(e.Location)).First();

            if (e.Button == MouseButtons.Middle)
            {
                CloseTab(thisTab);
            }
            else if (e.Button == MouseButtons.Right)
            {
                var tabIndex = GetTabIndex(thisTab);

                //Can't close tabs to the left/right if there aren't any!
                closeToolStripMenuItemsToLeft.Visible = tabIndex > 1;
                closeToolStripMenuItemsToRight.Visible = tabIndex != mainTabs.TabPages.Count - 1;

                //For UX purposes, hide the option to close the console also (this is disabled later in code too)
                closeToolStripMenuItem.Visible = tabIndex != 0;

                //Show context menu at the mouse position
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
                OpenFile(file);
            }
        }

        private void OpenFile(string fileName, byte[] input = null, TreeViewWithSearchResults.TreeViewPackageTag currentPackage = null)
        {
            Console.WriteLine($"Opening {fileName}");

            var tab = new TabPage(Path.GetFileName(fileName));
            tab.ToolTipText = fileName;
            tab.Controls.Add(new LoadingFile());

            mainTabs.TabPages.Add(tab);
            mainTabs.SelectTab(tab);

            var task = Task.Factory.StartNew(() => ProcessFile(fileName, input, currentPackage));

            task.ContinueWith(
                t =>
                {
                    t.Exception?.Flatten().Handle(ex =>
                    {
                        var control = new TextBox
                        {
                            Dock = DockStyle.Fill,
                            Font = new Font(FontFamily.GenericMonospace, 8),
                            Multiline = true,
                            ReadOnly = true,
                            Text = ex.ToString(),
                        };

                        tab.Controls.Clear();
                        tab.Controls.Add(control);

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

        private TabPage ProcessFile(string fileName, byte[] input, TreeViewWithSearchResults.TreeViewPackageTag currentPackage)
        {
            var tab = new TabPage();

            uint magic = 0;
            ushort magicResourceVersion = 0;

            if (input != null)
            {
                if (input.Length >= 6)
                {
                    magic = BitConverter.ToUInt32(input, 0);
                    magicResourceVersion = BitConverter.ToUInt16(input, 4);
                }
            }
            else
            {
                var magicData = new byte[6];

                using (var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Read(magicData, 0, 6);
                }

                magic = BitConverter.ToUInt32(magicData, 0);
                magicResourceVersion = BitConverter.ToUInt16(magicData, 4);
            }

            if (magic != Package.MAGIC && input == null && Regex.IsMatch(fileName, @"_[0-9]{3}\.vpk$"))
            {
                // TODO: Update tab name
                fileName = $"{fileName.Substring(0, fileName.Length - 8)}_dir.vpk";
                magic = Package.MAGIC;
            }

            var vrfGuiContext = new VrfGuiContext(fileName, currentPackage);

            if (magic == Package.MAGIC)
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
                var treeViewWithSearch = new TreeViewWithSearchResults(ImageList);
                treeViewWithSearch.InitializeTreeViewFromPackage(fileName, new TreeViewWithSearchResults.TreeViewPackageTag
                {
                    Package = package,
                    ParentFileLoader = vrfGuiContext.FileLoader,
                });
                treeViewWithSearch.TreeNodeMouseDoubleClick += VPK_OpenFile;
                treeViewWithSearch.TreeNodeMouseClick += VPK_OnClick;
                treeViewWithSearch.ListViewItemDoubleClick += VPK_OpenFile;
                treeViewWithSearch.ListViewItemRightClick += VPK_OnClick;
                treeViewWithSearch.Disposed += VPK_Disposed;
                tab.Controls.Add(treeViewWithSearch);

                // since we're in a separate thread, invoke to update the UI
                Invoke((MethodInvoker)(() => findToolStripButton.Enabled = true));
            }
            else if (magic == CompiledShader.MAGIC)
            {
                var shader = new CompiledShader();

                var buffer = new StringWriter(CultureInfo.InvariantCulture);
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
            else if (magic == ClosedCaptions.MAGIC)
            {
                var captions = new ClosedCaptions();
                if (input != null)
                {
                    captions.Read(fileName, new MemoryStream(input));
                }
                else
                {
                    captions.Read(fileName);
                }

                var control = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    ReadOnly = true,
                    AllowUserToAddRows = false,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    DataSource = new BindingSource(new BindingList<ClosedCaption>(captions.Captions), null),
                    ScrollBars = ScrollBars.Both,
                };
                tab.Controls.Add(control);
            }
            else if (magic == ToolsAssetInfo.MAGIC)
            {
                var toolsAssetInfo = new ToolsAssetInfo();
                if (input != null)
                {
                    toolsAssetInfo.Read(new MemoryStream(input));
                }
                else
                {
                    toolsAssetInfo.Read(fileName);
                }

                var text = new TextBox
                {
                    Dock = DockStyle.Fill,
                    ScrollBars = ScrollBars.Vertical,
                    Multiline = true,
                    ReadOnly = true,
                    Text = NormalizeLineEndings(toolsAssetInfo.ToString()),
                };
                tab.Controls.Add(text);
            }
            else if (magic == BinaryKV3.MAGIC || magic == BinaryKV3.MAGIC2)
            {
                var kv3 = new BinaryKV3();
                Stream kv3stream;

                if (input != null)
                {
                    kv3stream = new MemoryStream(input);
                }
                else
                {
                    kv3stream = File.OpenRead(fileName);
                }

                using (var binaryReader = new BinaryReader(kv3stream))
                {
                    kv3.Size = (uint)kv3stream.Length;
                    kv3.Read(binaryReader, null);
                }

                kv3stream.Close();

                var control = new TextBox();
                control.Font = new Font(FontFamily.GenericMonospace, control.Font.Size);
                control.Text = kv3.ToString();
                control.Dock = DockStyle.Fill;
                control.Multiline = true;
                control.ReadOnly = true;
                control.ScrollBars = ScrollBars.Both;
                tab.Controls.Add(control);
            }
            else if (magicResourceVersion == Resource.KnownHeaderVersion)
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
                            var tex = (Texture)resource.DataBlock;

                            var control = new Forms.Texture
                            {
                                BackColor = Color.Black,
                            };
                            control.SetImage(tex.GenerateBitmap().ToBitmap(), Path.GetFileNameWithoutExtension(fileName), tex.ActualWidth, tex.ActualHeight);

                            tab2.Controls.Add(control);
                            Invoke(new ExportDel(AddToExport), resTabs, $"Export {Path.GetFileName(fileName)} as an image", fileName, new ExportData { Resource = resource });
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
                        if (((Panorama)resource.DataBlock).Names.Count > 0)
                        {
                            var nameTab = new TabPage("PANORAMA NAMES");
                            var nameControl = new DataGridView
                            {
                                Dock = DockStyle.Fill,
                                AutoSize = true,
                                ReadOnly = true,
                                AllowUserToAddRows = false,
                                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                                DataSource = new BindingSource(new BindingList<Panorama.NameEntry>(((Panorama)resource.DataBlock).Names), null),
                            };
                            nameTab.Controls.Add(nameControl);
                            resTabs.TabPages.Add(nameTab);
                        }

                        break;
                    case ResourceType.PanoramaLayout:
                        Invoke(new ExportDel(AddToExport), resTabs, $"Export {Path.GetFileName(fileName)} as XML", fileName, new ExportData { Resource = resource });
                        break;
                    case ResourceType.PanoramaScript:
                        Invoke(new ExportDel(AddToExport), resTabs, $"Export {Path.GetFileName(fileName)} as JS", fileName, new ExportData { Resource = resource });
                        break;
                    case ResourceType.PanoramaStyle:
                        Invoke(new ExportDel(AddToExport), resTabs, $"Export {Path.GetFileName(fileName)} as CSS", fileName, new ExportData { Resource = resource });
                        break;
                    case ResourceType.Particle:
                        var viewerControl = new GLParticleViewer(vrfGuiContext);
                        viewerControl.Load += (_, __) =>
                        {
                            var particleSystem = (ParticleSystem)resource.DataBlock;
                            var particleRenderer = new ParticleRenderer(particleSystem, vrfGuiContext);

                            viewerControl.AddRenderer(particleRenderer);
                        };

                        var particleRendererTab = new TabPage("PARTICLE");
                        particleRendererTab.Controls.Add(viewerControl.Control);
                        resTabs.TabPages.Add(particleRendererTab);
                        break;
                    case ResourceType.Sound:
                        var soundTab = new TabPage("SOUND");
                        var ap = new AudioPlayer(resource, soundTab);
                        resTabs.TabPages.Add(soundTab);

                        Invoke(new ExportDel(AddToExport), resTabs, $"Export {Path.GetFileName(fileName)} as {((Sound)resource.DataBlock).SoundType}", fileName, new ExportData { Resource = resource });

                        break;
                    case ResourceType.World:
                        var worldmeshTab = new TabPage("MAP");
                        worldmeshTab.Controls.Add(new GLWorldViewer(vrfGuiContext, (World)resource.DataBlock).ViewerControl);
                        resTabs.TabPages.Add(worldmeshTab);
                        break;

                    case ResourceType.WorldNode:
                        var nodemeshTab = new TabPage("WORLD NODE");
                        nodemeshTab.Controls.Add(new GLWorldViewer(vrfGuiContext, (WorldNode)resource.DataBlock).ViewerControl);
                        resTabs.TabPages.Add(nodemeshTab);
                        break;

                    case ResourceType.Model:
                        Invoke(new ExportDel(AddToExport), resTabs, $"Export {Path.GetFileName(fileName)} as glTF", fileName, new ExportData { Resource = resource, VrfGuiContext = vrfGuiContext });

                        var modelRendererTab = new TabPage("MODEL");
                        modelRendererTab.Controls.Add(new GLModelViewer(vrfGuiContext, (Model)resource.DataBlock).ViewerControl);
                        resTabs.TabPages.Add(modelRendererTab);
                        break;

                    case ResourceType.Mesh:
                        if (!resource.ContainsBlockType(BlockType.VBIB))
                        {
                            Console.WriteLine("Old style model, no VBIB!");
                            break;
                        }

                        Invoke(new ExportDel(AddToExport), resTabs, $"Export {Path.GetFileName(fileName)} as glTF", fileName, new ExportData { Resource = resource, VrfGuiContext = vrfGuiContext });

                        var meshRendererTab = new TabPage("MESH");
                        meshRendererTab.Controls.Add(new GLModelViewer(vrfGuiContext, new Mesh(resource)).ViewerControl);
                        resTabs.TabPages.Add(meshRendererTab);
                        break;

                    case ResourceType.Material:
                        var materialViewerControl = new GLMaterialViewer();
                        materialViewerControl.Load += (_, __) =>
                        {
                            var material = vrfGuiContext.MaterialLoader.LoadMaterial(resource);
                            var materialRenderer = new MaterialRenderer(material);

                            materialViewerControl.AddRenderer(materialRenderer);
                        };

                        var materialRendererTab = new TabPage("MATERIAL");
                        materialRendererTab.Controls.Add(materialViewerControl.Control);
                        resTabs.TabPages.Add(materialRendererTab);
                        break;
                }

                foreach (var block in resource.Blocks)
                {
                    if (block.Type == BlockType.RERL)
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

                    if (block.Type == BlockType.NTRO)
                    {
                        if (((ResourceIntrospectionManifest)block).ReferencedStructs.Count > 0)
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
                                DataSource = new BindingSource(new BindingList<ResourceIntrospectionManifest.ResourceDiskStruct>(((ResourceIntrospectionManifest)block).ReferencedStructs), null),
                            };

                            externalRefsTab.Controls.Add(externalRefs);
                            resTabs.TabPages.Add(externalRefsTab);
                        }

                        if (((ResourceIntrospectionManifest)block).ReferencedEnums.Count > 0)
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
                                DataSource = new BindingSource(new BindingList<ResourceIntrospectionManifest.ResourceDiskEnum>(((ResourceIntrospectionManifest)block).ReferencedEnums), null),
                            };

                            externalRefsTab.Controls.Add(externalRefs2);
                            resTabs.TabPages.Add(externalRefsTab);
                        }

                        //continue;
                    }

                    var tab2 = new TabPage(block.Type.ToString());
                    try
                    {
                        var control = new TextBox();
                        control.Font = new Font(FontFamily.GenericMonospace, control.Font.Size);

                        if (block.Type == BlockType.DATA)
                        {
                            switch (resource.ResourceType)
                            {
                                case ResourceType.Sound:
                                    control.Text = NormalizeLineEndings(((Sound)block).ToString());
                                    break;
                                case ResourceType.Particle:
                                case ResourceType.Mesh:
                                    if (block is BinaryKV3 blockKeyvalues)
                                    {
                                        //Wrap it around a KV3File object to get the header.
                                        control.Text = NormalizeLineEndings(blockKeyvalues.GetKV3File().ToString());
                                    }
                                    else if (block is NTRO blockNTRO)
                                    {
                                        control.Text = NormalizeLineEndings(blockNTRO.ToString());
                                    }

                                    break;
                                default:
                                    control.Text = NormalizeLineEndings(block.ToString());
                                    break;
                            }
                        }
                        else
                        {
                            control.Text = NormalizeLineEndings(block.ToString());
                        }

                        control.Dock = DockStyle.Fill;
                        control.Multiline = true;
                        control.ReadOnly = true;
                        control.ScrollBars = ScrollBars.Both;
                        tab2.Controls.Add(control);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);

                        var bv = new ByteViewer();
                        bv.Dock = DockStyle.Fill;
                        tab2.Controls.Add(bv);

                        Invoke((MethodInvoker)(() =>
                        {
                            resource.Reader.BaseStream.Position = block.Offset;
                            bv.SetBytes(resource.Reader.ReadBytes((int)block.Size));
                        }));
                    }

                    resTabs.TabPages.Add(tab2);
                }

                if (resource.ResourceType == ResourceType.PanoramaLayout
                || resource.ResourceType == ResourceType.PanoramaScript
                || resource.ResourceType == ResourceType.PanoramaStyle
                || resource.ResourceType == ResourceType.SoundEventScript
                || resource.ResourceType == ResourceType.SoundStackScript
                || resource.ResourceType == ResourceType.EntityLump)
                {
                    resTabs.SelectTab(resTabs.TabCount - 1);
                }

                tab.Controls.Add(resTabs);
            }
            else
            {
                var resTabs = new TabControl
                {
                    Dock = DockStyle.Fill,
                };
                tab.Controls.Add(resTabs);

                var bvTab = new TabPage("Hex");
                var bv = new ByteViewer
                {
                    Dock = DockStyle.Fill,
                };
                bvTab.Controls.Add(bv);
                resTabs.TabPages.Add(bvTab);

                if (input == null)
                {
                    input = File.ReadAllBytes(fileName);
                }

                if (!input.Contains<byte>(0x00))
                {
                    var textTab = new TabPage("Text");
                    var text = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        ScrollBars = ScrollBars.Vertical,
                        Multiline = true,
                        ReadOnly = true,
                        Text = System.Text.Encoding.UTF8.GetString(input),
                    };
                    textTab.Controls.Add(text);
                    resTabs.TabPages.Add(textTab);
                    resTabs.SelectedTab = textTab;
                }

                Invoke((MethodInvoker)(() =>
                {
                    bv.SetBytes(input);
                }));
            }

            return tab;
        }

        private void VPK_Disposed(object sender, EventArgs e)
        {
            if (sender is TreeViewWithSearchResults treeViewWithSearch)
            {
                treeViewWithSearch.TreeNodeMouseDoubleClick -= VPK_OpenFile;
                treeViewWithSearch.TreeNodeMouseClick -= VPK_OnClick;
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

        private void OpenFileFromNode(TreeNode node)
        {
            //Make sure we aren't a directory!
            if (node.Tag.GetType() == typeof(PackageEntry))
            {
                var package = node.TreeView.Tag as TreeViewWithSearchResults.TreeViewPackageTag;
                var file = node.Tag as PackageEntry;
                package.Package.ReadEntry(file, out var output);

                OpenFile(file.GetFileName(), output, package);
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
            TreeNode selectedNode = null;
            var control = ((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl;

            if (control is TreeView treeView)
            {
                selectedNode = treeView.SelectedNode;
            }
            else if (control is ListView listView)
            {
                selectedNode = listView.SelectedItems[0].Tag as TreeNode;
            }

            if (selectedNode.Tag is PackageEntry packageEntry)
            {
                Clipboard.SetText(packageEntry.GetFullPath());
            }
            else
            {
                Clipboard.SetText(selectedNode.Name);
            }
        }

        private void OpenWithDefaultAppToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode selectedNode = null;
            var control = ((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl;

            if (control is TreeView treeView)
            {
                selectedNode = treeView.SelectedNode;
            }
            else if (control is ListView listView)
            {
                selectedNode = listView.SelectedItems[0].Tag as TreeNode;
            }

            if (selectedNode.Tag is PackageEntry file)
            {
                var package = selectedNode.TreeView.Tag as TreeViewWithSearchResults.TreeViewPackageTag;
                package.Package.ReadEntry(file, out var output);

                var tempPath = $"{Path.GetTempPath()}VRF - {Path.GetFileName(package.Package.FileName)} - {file.GetFileName()}";
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
                    Console.Error.WriteLine(ex.Message);
                }
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
            TreeViewWithSearchResults.TreeViewPackageTag package = null;
            TreeNode selectedNode = null;

            // the context menu can come from a TreeView or a ListView depending on where the user clicked to extract
            // each option has a difference in where we can get the values to extract
            if (((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl is TreeView)
            {
                var tree = ((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl as TreeView;
                selectedNode = tree.SelectedNode;
                package = tree.Tag as TreeViewWithSearchResults.TreeViewPackageTag;
            }
            else if (((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl is ListView)
            {
                var listView = ((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl as ListView;
                selectedNode = listView.SelectedItems[0].Tag as TreeNode;
                package = listView.Tag as TreeViewWithSearchResults.TreeViewPackageTag;
            }

            if (selectedNode.Tag.GetType() == typeof(PackageEntry))
            {
                // We are a file
                var file = selectedNode.Tag as PackageEntry;
                var fileName = file.GetFileName();

                package.Package.ReadEntry(file, out var output);

                if (decompile && fileName.EndsWith("_c", StringComparison.Ordinal))
                {
                    using var resource = new Resource();
                    using var memory = new MemoryStream(output);

                    resource.Read(memory);

                    ExportFile.Export(fileName, new ExportData
                    {
                        Resource = resource,
                        VrfGuiContext = new VrfGuiContext(null, package),
                    });

                    return;
                }

                var dialog = new SaveFileDialog
                {
                    InitialDirectory = Settings.Config.SaveDirectory,
                    Filter = "All files (*.*)|*.*",
                    FileName = fileName,
                };
                var userOK = dialog.ShowDialog();

                if (userOK == DialogResult.OK)
                {
                    Settings.Config.SaveDirectory = Path.GetDirectoryName(dialog.FileName);
                    Settings.Save();

                    using var stream = dialog.OpenFile();
                    stream.Write(output, 0, output.Length);
                }
            }
            else
            {
                //We are a folder
                var dialog = new FolderBrowserDialog();
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    var extractDialog = new ExtractProgressForm(package.Package, selectedNode, dialog.SelectedPath, decompile);
                    extractDialog.ShowDialog();
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

        private delegate void ExportDel(Control control, string name, string filename, ExportData data);

        private void AddToExport(Control control, string name, string filename, ExportData data)
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

            void ControlExposed(object sender, EventArgs e)
            {
                control.Disposed -= ControlExposed;
                ts.Click -= ExportToolStripMenuItem_Click;
                exportToolStripButton.DropDownItems.Remove(ts);

                if (exportToolStripButton.DropDownItems.Count == 0)
                {
                    exportToolStripButton.Enabled = false;
                }
            }

            control.Disposed += ControlExposed;
        }

        private void ExportToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //ToolTipText is the full filename
            var menuItem = (ToolStripMenuItem)sender;
            var fileName = menuItem.ToolTipText;

            ExportFile.Export(fileName, menuItem.Tag as ExportData);
        }
    }
}
