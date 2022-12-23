using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Types.Exporter;
using GUI.Utils;
using SteamDatabase.ValvePak;

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

        public MainForm()
        {
            LoadAssetTypes();
            InitializeComponent();

            mainTabs.SelectedIndexChanged += (tabControl, e) =>
            {
                if (string.IsNullOrEmpty(mainTabs.SelectedTab?.ToolTipText))
                {
                    Text = "VRF";
                }
                else
                {
                    Text = $"VRF - {mainTabs.SelectedTab.ToolTipText}";
                }

                ShowHideSearch();
            };

            var consoleTab = new ConsoleTab();
            mainTabs.TabPages.Add(consoleTab.CreateTab());

            Console.WriteLine($"VRF v{Application.ProductVersion}");

            searchForm = new SearchForm();

            Settings.Load();

            var args = Environment.GetCommandLineArgs();
            for (var i = 1; i < args.Length; i++)
            {
                var file = args[i];
                var innerFilePosition = file.LastIndexOf(".vpk:", StringComparison.InvariantCulture);
                string innerFile = null;

                if (innerFilePosition > 0)
                {
                    innerFile = file[(innerFilePosition + 5)..];
                    file = file[..(innerFilePosition + 4)];
                }

                if (!File.Exists(file))
                {
                    Console.Error.WriteLine($"File '{file}' does not exist.");
                    continue;
                }

                if (innerFile != null)
                {
                    var package = new Package();
                    package.Read(file);

                    var packageFile = package.FindEntry(innerFile);

                    if (packageFile == null)
                    {
                        Console.Error.WriteLine($"File '{packageFile}' does not exist in package '{file}'.");
                    }

                    var vrfGuiContext = new VrfGuiContext(packageFile.GetFullPath(), null)
                    {
                        CurrentPackage = package
                    };
                    OpenFile(vrfGuiContext, packageFile);

                    continue;
                }

                OpenFile(file);
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
            if (mainTabs.SelectedTab != null && mainTabs.SelectedTab.Controls["TreeViewWithSearchResults"] is TreeViewWithSearchResults package)
            {
                findToolStripButton.Enabled = true;
                recoverDeletedToolStripMenuItem.Enabled = !package.DeletedFilesRecovered;
            }
            else
            {
                findToolStripButton.Enabled = false;
                recoverDeletedToolStripMenuItem.Enabled = false;
            }
        }

        private int GetTabIndex(TabPage tab)
        {
            //Work out the index of the requested tab
            for (var i = 0; i < mainTabs.TabPages.Count; i++)
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
            var tabCount = mainTabs.TabPages.Count;
            for (var i = 1; i < tabCount; i++)
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
            for (var i = GetTabIndex(basePage); i > 0; i--)
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
            var tabCount = mainTabs.TabPages.Count;
            for (var i = 1; i < tabCount; i++)
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
            ImageList = new ImageList
            {
                ColorDepth = ColorDepth.Depth32Bit
            };

            var assembly = Assembly.GetExecutingAssembly();
            var names = assembly.GetManifestResourceNames().Where(n => n.StartsWith("GUI.AssetTypes.", StringComparison.Ordinal));

            foreach (var name in names)
            {
                var res = name.Split('.');

                using var stream = assembly.GetManifestResourceStream(name);
                ImageList.Images.Add(res[2], Image.FromStream(stream));
            }
        }

        private void OnTabClick(object sender, MouseEventArgs e)
        {
            //Work out what tab we're interacting with
            var tabControl = sender as TabControl;
            var tabs = tabControl.TabPages;
            var thisTab = tabs.Cast<TabPage>().Where((t, i) => tabControl.GetTabRect(i).Contains(e.Location)).First();

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
                tabContextMenuStrip.Tag = e.Location;
                tabContextMenuStrip.Show((Control)sender, e.Location);
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

        private void OpenFile(string fileName)
        {
            Console.WriteLine($"Opening {fileName}");

            if (Regex.IsMatch(fileName, @"_[0-9]{3}\.vpk$"))
            {
                var fixedPackage = $"{fileName[..^8]}_dir.vpk";

                if (File.Exists(fixedPackage))
                {
                    Console.WriteLine($"You opened \"{Path.GetFileName(fileName)}\" but there is \"{Path.GetFileName(fixedPackage)}\"");
                    fileName = fixedPackage;
                }
            }

            var vrfGuiContext = new VrfGuiContext(fileName, null);
            OpenFile(vrfGuiContext, null);
        }

        public void OpenFile(VrfGuiContext vrfGuiContext, PackageEntry file)
        {
            var tab = new TabPage(Path.GetFileName(vrfGuiContext.FileName))
            {
                ToolTipText = vrfGuiContext.FileName,
                Tag = new ExportData
                {
                    PackageEntry = file,
                    VrfGuiContext = vrfGuiContext,
                }
            };

            var parentContext = vrfGuiContext.ParentGuiContext;

            while (parentContext != null)
            {
                tab.ToolTipText = $"{parentContext.FileName} > {tab.ToolTipText}";

                parentContext = parentContext.ParentGuiContext;
            }

            tab.Controls.Add(new LoadingFile());

            mainTabs.TabPages.Add(tab);
            mainTabs.SelectTab(tab);

            var task = Task.Factory.StartNew(() => ProcessFile(vrfGuiContext, file));

            task.ContinueWith(
                t =>
                {
                    t.Exception?.Flatten().Handle(ex =>
                    {
                        var control = new MonospaceTextBox
                        {
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
                    tab.SuspendLayout();

                    try
                    {
                        tab.Controls.Clear();

                        foreach (Control c in t.Result.Controls)
                        {
                            tab.Controls.Add(c);
                        }
                    }
                    finally
                    {
                        tab.ResumeLayout();
                    }

                    ShowHideSearch();
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.FromCurrentSynchronizationContext());
        }

        private TabPage ProcessFile(VrfGuiContext vrfGuiContext, PackageEntry file)
        {
            uint magic = 0;
            ushort magicResourceVersion = 0;
            byte[] input = null;

            if (file != null)
            {
                vrfGuiContext.CurrentPackage.ReadEntry(file, out input, validateCrc: file.CRC32 > 0);
            }

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

                using (var fs = new FileStream(vrfGuiContext.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Read(magicData, 0, 6);
                }

                magic = BitConverter.ToUInt32(magicData, 0);
                magicResourceVersion = BitConverter.ToUInt16(magicData, 4);
            }

            if (Types.Viewers.Package.IsAccepted(magic))
            {
                var tab = new Types.Viewers.Package
                {
                    ImageList = ImageList, // TODO: Move this directly into Package
                }.Create(vrfGuiContext, input);

                return tab;
            }
            else if (Types.Viewers.CompiledShader.IsAccepted(magic))
            {
                return new Types.Viewers.CompiledShader().Create(vrfGuiContext, input);
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
            else if (Types.Viewers.Audio.IsAccepted(magic, vrfGuiContext.FileName))
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
            var control = ((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl;
            List<TreeNode> selectedNodes;

            if (control is TreeView treeView)
            {
                selectedNodes = new List<TreeNode>
                {
                    treeView.SelectedNode
                };
            }
            else if (control is ListView listView)
            {
                selectedNodes = new List<TreeNode>(listView.SelectedItems.Count);

                foreach (ListViewItem selectedNode in listView.SelectedItems)
                {
                    selectedNodes.Add(selectedNode.Tag as TreeNode);
                }
            }
            else
            {
                throw new InvalidDataException("Unknown state");
            }

            var sb = new StringBuilder();

            foreach (var selectedNode in selectedNodes)
            {
                if (selectedNode.Tag is PackageEntry packageEntry)
                {
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
            List<TreeNode> selectedNodes;

            if (control is TreeView treeView)
            {
                selectedNodes = new List<TreeNode>
                {
                    treeView.SelectedNode
                };
            }
            else if (control is ListView listView)
            {
                selectedNodes = new List<TreeNode>(listView.SelectedItems.Count);

                foreach (ListViewItem selectedNode in listView.SelectedItems)
                {
                    selectedNodes.Add(selectedNode.Tag as TreeNode);
                }
            }
            else
            {
                throw new InvalidDataException("Unknown state");
            }

            foreach (var selectedNode in selectedNodes)
            {
                if (selectedNode.Tag is PackageEntry file)
                {
                    var vrfGuiContext = (VrfGuiContext)selectedNode.TreeView.Tag;
                    vrfGuiContext.CurrentPackage.ReadEntry(file, out var output, validateCrc: file.CRC32 > 0);

                    var tempPath = $"{Path.GetTempPath()}VRF - {Path.GetFileName(vrfGuiContext.CurrentPackage.FileName)} - {file.GetFileName()}";
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
            if (owner.SourceControl is TreeView tree)
            {
                var vrfGuiContext = (VrfGuiContext)tree.Tag;

                ExportFile.ExtractFilesFromTreeNode(tree.SelectedNode, vrfGuiContext, decompile);
            }
            // Clicking context menu item in right side of the package view
            else if (owner.SourceControl is ListView listView)
            {
                var vrfGuiContext = (VrfGuiContext)listView.Tag;

                foreach (ListViewItem selectedNode in listView.SelectedItems)
                {
                    ExportFile.ExtractFilesFromTreeNode(selectedNode.Tag as TreeNode, vrfGuiContext, decompile);
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

                    ExportFile.ExtractFileFromStream(Path.GetFileName(exportData.VrfGuiContext.FileName), fileStream, exportData.VrfGuiContext, decompile);
                }
            }
            else
            {
                throw new InvalidDataException("Unknown state");
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

        private void RecoverDeletedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            recoverDeletedToolStripMenuItem.Enabled = false;

            var treeView = mainTabs.SelectedTab.Controls["TreeViewWithSearchResults"] as TreeViewWithSearchResults;
            treeView.RecoverDeletedFiles();
        }
    }
}
