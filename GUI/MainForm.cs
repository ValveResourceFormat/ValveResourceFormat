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
using GUI.Types.Exporter;
using GUI.Utils;
using SteamDatabase.ValvePak;
using Resource = ValveResourceFormat.Resource;

namespace GUI
{
    public partial class MainForm : Form
    {
        private SearchForm searchForm;
#pragma warning disable CA2213
        // Disposable fields should be disposed
        // for some reason disposing it makes closing GUI very slow
        private ImageList ImageList;
#pragma warning restore CA2213
        public ContextMenuStrip VpkContextMenu => vpkContextMenu; // TODO
        public ToolStripDropDownButton ExportToolStripButton => exportToolStripButton; // TODO

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

        protected override void OnShown(EventArgs e)
        {
            var savedWindowDimensionsAreValid = IsOnScreen(new Rectangle(
                Settings.Config.WindowLeft,
                Settings.Config.WindowTop,
                Settings.Config.WindowWidth,
                Settings.Config.WindowHeight));

            if (savedWindowDimensionsAreValid)
            {
                Left = Settings.Config.WindowLeft;
                Top = Settings.Config.WindowTop;
                Height = Settings.Config.WindowHeight;
                Width = Settings.Config.WindowWidth;

                var newState = (FormWindowState)Settings.Config.WindowState;

                if (newState == FormWindowState.Maximized || newState == FormWindowState.Normal)
                {
                    WindowState = newState;
                }
            }

            base.OnShown(e);
        }

        // checks if the Rectangle is within bounds of one of the user's screen
        public bool IsOnScreen(Rectangle formRectangle)
        {
            if (formRectangle.Width < MinimumSize.Width || formRectangle.Height < MinimumSize.Height)
            {
                return false;
            }

            return Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(formRectangle));
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // save the application window size, position and state (if maximized)
            (Settings.Config.WindowLeft, Settings.Config.WindowTop, Settings.Config.WindowWidth, Settings.Config.WindowHeight, Settings.Config.WindowState) = WindowState switch
            {
                FormWindowState.Normal => (Left, Top, Width, Height, (int)FormWindowState.Normal),
                // will restore window to maximized
                FormWindowState.Maximized => (RestoreBounds.Left, RestoreBounds.Top, RestoreBounds.Width, RestoreBounds.Height, (int)FormWindowState.Maximized),
                // if minimized restore to Normal instead, using RestoreBound values
                FormWindowState.Minimized => (RestoreBounds.Left, RestoreBounds.Top, RestoreBounds.Width, RestoreBounds.Height, (int)FormWindowState.Normal),
                // the default switch should never happen (FormWindowState only takes the values Normal, Maximized, Minimized)
                _ => (0, 0, 0, 0, (int)FormWindowState.Normal),
            };

            Settings.Save();
            base.OnClosing(e);
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
            using var openDialog = new OpenFileDialog
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

        public void OpenFile(string fileName, byte[] input = null, TreeViewWithSearchResults.TreeViewPackageTag currentPackage = null)
        {
            Console.WriteLine($"Opening {fileName}");

            if (input == null && Regex.IsMatch(fileName, @"_[0-9]{3}\.vpk$"))
            {
                var fixedPackage = $"{fileName.Substring(0, fileName.Length - 8)}_dir.vpk";

                if (File.Exists(fixedPackage))
                {
                    Console.WriteLine($"You opened \"{Path.GetFileName(fileName)}\" but there is \"{Path.GetFileName(fixedPackage)}\"");
                    fileName = fixedPackage;
                }
            }

            var tab = new TabPage(Path.GetFileName(fileName));
            tab.ToolTipText = fileName;
            tab.Controls.Add(new LoadingFile());

            mainTabs.TabPages.Add(tab);
            mainTabs.SelectTab(tab);

            var task = Task.Factory.StartNew(() => ProcessFile(fileName, input, currentPackage, tab));

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

        private TabPage ProcessFile(string fileName, byte[] input, TreeViewWithSearchResults.TreeViewPackageTag currentPackage, TabPage parentTab)
        {
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

            var vrfGuiContext = new VrfGuiContext(fileName, currentPackage);

            if (Types.Viewers.Package.IsAccepted(magic))
            {
                var tab = new Types.Viewers.Package
                {
                    ImageList = ImageList, // TODO: Move this directly into Package
                }.Create(vrfGuiContext, input);

                // since we're in a separate thread, invoke to update the UI
                Invoke((MethodInvoker)(() => findToolStripButton.Enabled = true));

                return tab;
            }
            else if (Types.Viewers.CompiledShader.IsAccepted(magic))
            {
                return Types.Viewers.CompiledShader.Create(vrfGuiContext, input, parentTab);
            }
            else if (Types.Viewers.ClosedCaptions.IsAccepted(magic))
            {
                return new Types.Viewers.ClosedCaptions().Create(vrfGuiContext, input);
            }
            else if (Types.Viewers.ToolsAssetInfo.IsAccepted(magic))
            {
                return new Types.Viewers.ToolsAssetInfo().Create(vrfGuiContext, input);
            }
            else if (Types.Viewers.BinaryKeyValues.IsAccepted(magic))
            {
                return new Types.Viewers.BinaryKeyValues().Create(vrfGuiContext, input);
            }
            else if (Types.Viewers.BinaryKeyValues1.IsAccepted(magic))
            {
                return new Types.Viewers.BinaryKeyValues1().Create(vrfGuiContext, input);
            }
            else if (Types.Viewers.Resource.IsAccepted(magicResourceVersion))
            {
                return new Types.Viewers.Resource().Create(vrfGuiContext, input);
            }
            else if (Types.Viewers.Image.IsAccepted(magic))
            {
                return new Types.Viewers.Image().Create(vrfGuiContext, input);
            }
            else if (Types.Viewers.Audio.IsAccepted(magic, fileName))
            {
                return new Types.Viewers.Audio().Create(vrfGuiContext, input);
            }

            return new Types.Viewers.ByteViewer().Create(vrfGuiContext, input);
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
                    using var resource = new Resource
                    {
                        FileName = fileName,
                    };
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
    }
}
