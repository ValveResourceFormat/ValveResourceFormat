//#define SCREENSHOT_MODE // Uncomment to hide version, keep title bar static, set an exact window size

using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Types.Exporter;
using GUI.Types.PackageViewer;
using GUI.Types.Renderer;
using GUI.Utils;
using OpenTK.Windowing.Desktop;
using SteamDatabase.ValvePak;
using ValveResourceFormat.IO;

using ResourceViewMode = GUI.Types.Viewers.ResourceViewMode;

#nullable disable

namespace GUI
{
    partial class MainForm : Form
    {
        // Disposable fields should be disposed
        // for some reason disposing it makes closing GUI very slow
        public static ImageList ImageList { get; }
        public static Dictionary<string, int> ImageListLookup { get; }

        private SearchForm searchForm;

        static MainForm()
        {
            ImageList = new ImageList
            {
                ColorDepth = ColorDepth.Depth32Bit
            };

            var assembly = Assembly.GetExecutingAssembly();
            var names = assembly.GetManifestResourceNames().Where(n => n.StartsWith("GUI.AssetTypes.", StringComparison.Ordinal)).ToList();

            ImageListLookup = new(names.Count);

            foreach (var name in names)
            {
                var extension = name.Split('.')[2];

                using var stream = assembly.GetManifestResourceStream(name);
                ImageList.Images.Add(extension, Image.FromStream(stream));

                // Keep our own lookup because IndexOfKey is slow and not thread safe
                var index = ImageList.Images.IndexOfKey(extension); // O(n)
                ImageListLookup.Add(extension, index);
                Debug.Assert(index >= 0);
            }

            GLFWProvider.CheckForMainThread = false;
            GLFWProvider.EnsureInitialized();
        }

        public MainForm(string[] args)
        {
            InitializeComponent();

            mainTabs.ImageList = ImageList;
            mainTabs.SelectedIndexChanged += OnMainSelectedTabChanged;

            var consoleTab = new ConsoleTab();
            Log.SetConsoleTab(consoleTab);
            var consoleTabPage = consoleTab.CreateTab();
            consoleTabPage.ImageIndex = ImageListLookup["_console"];
            mainTabs.TabPages.Add(consoleTabPage);

            var version = Application.ProductVersion;
            var versionPlus = version.IndexOf('+', StringComparison.InvariantCulture);

            if (versionPlus > 0)
            {
                // If version ends with ".0", display part of the commit hash, otherwise the zero is replaced with CI build number
                if (version[versionPlus - 2] == '.' && version[versionPlus - 1] == '0')
                {
                    versionPlus += 8;
                }

                versionLabel.Text = string.Concat("v", version[..versionPlus]);
            }
            else
            {
                versionLabel.Text = string.Concat("v", version);

#if !CI_RELEASE_BUILD // Set in Directory.Build.props
                versionLabel.Text += "-dev";
#endif
            }

#if DEBUG
            versionLabel.Text += " (DEBUG)";
#endif

            searchForm = new SearchForm();

            Settings.Load();
            consoleTab.InitializeFont();

#pragma warning disable WFO5001
            Application.SetColorMode(Settings.GetSystemColor());

            if (Application.IsDarkModeEnabled)
            {
                Log.Warn(nameof(Application), "Dark mode is EXPERIMENTAL. Some controls may have less than ideal colors which will be improved in a future .NET update.");
            }

            HardwareAcceleratedTextureDecoder.Decoder = new GLTextureDecoder();

#if DEBUG
            var shadersMenuItem = new ToolStripMenuItem("Validate shaders");
            shadersMenuItem.Click += OnValidateShadersToolStripMenuItem_Click;
            fileToolStripMenuItem.DropDownItems.Add(shadersMenuItem);

            if (args.Length > 0 && args[0] == "validate_shaders")
            {
                GUI.Types.Renderer.ShaderLoader.ValidateShaders();
                Environment.Exit(0);
                return;
            }
#endif

            if (Settings.IsFirstStartup)
            {
                OpenWelcome();
            }
            else if (args.Length == 0 && Settings.Config.OpenExplorerOnStart != 0)
            {
                OpenExplorer();
            }

            // Force refresh title due to OpenFile calls above, SelectedIndexChanged is not called in the same tick
            OnMainSelectedTabChanged(null, null);
        }

        public void OpenCommandLineArgFiles(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                var file = args[i];

                // Handle vpk: protocol
                if (file.StartsWith("vpk:", StringComparison.InvariantCulture))
                {
                    file = System.Net.WebUtility.UrlDecode(file[4..]);

                    var innerFilePosition = file.LastIndexOf(".vpk:", StringComparison.InvariantCulture);

                    if (innerFilePosition == -1)
                    {
                        Log.Error(nameof(MainForm), $"For vpk: protocol to work, specify a file path inside of the package, for example: \"vpk:C:/path/pak01_dir.vpk:inner/file.vmdl_c\"");

                        OpenFile(file);
                        continue;
                    }

                    var innerFile = file[(innerFilePosition + 5)..];
                    file = file[..(innerFilePosition + 4)];

                    if (!File.Exists(file))
                    {
                        var dirFile = file[..innerFilePosition] + "_dir.vpk";

                        if (!File.Exists(dirFile))
                        {
                            Log.Error(nameof(MainForm), $"File '{file}' does not exist.");
                            continue;
                        }

                        file = dirFile;
                    }

                    file = Path.GetFullPath(file);
                    Log.Info(nameof(MainForm), $"Opening {file}");

                    var package = new Package();
                    try
                    {
                        package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
                        package.Read(file);

                        var packageFile = package.FindEntry(innerFile);

                        if (packageFile == null)
                        {
                            packageFile = package.FindEntry(innerFile + GameFileLoader.CompiledFileSuffix);

                            if (packageFile == null)
                            {
                                Log.Error(nameof(MainForm), $"File '{packageFile}' does not exist in package '{file}'.");
                                continue;
                            }
                        }

                        innerFile = packageFile.GetFullPath();

                        Log.Info(nameof(MainForm), $"Opening {innerFile}");

                        var vrfGuiContext = new VrfGuiContext(file, null)
                        {
                            CurrentPackage = package
                        };
                        var fileContext = new VrfGuiContext(innerFile, vrfGuiContext);
                        package = null;

                        try
                        {
                            OpenFile(fileContext, packageFile);
                            fileContext = null;
                        }
                        finally
                        {
                            fileContext?.Dispose();
                            vrfGuiContext?.Dispose();
                        }
                    }
                    finally
                    {
                        package?.Dispose();
                    }

                    continue;
                }

                if (!File.Exists(file))
                {
                    Log.Error(nameof(MainForm), $"File '{file}' does not exist.");
                    continue;
                }

                file = Path.GetFullPath(file);
                OpenFile(file);
            }

            OnMainSelectedTabChanged(null, null);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            var savedWindowDimensionsAreValid = IsOnScreen(new Rectangle(
                Settings.Config.WindowLeft,
                Settings.Config.WindowTop,
                Settings.Config.WindowWidth,
                Settings.Config.WindowHeight));

            if (savedWindowDimensionsAreValid)
            {
                SetBounds(
                    Settings.Config.WindowLeft,
                    Settings.Config.WindowTop,
                    Settings.Config.WindowWidth,
                    Settings.Config.WindowHeight
                );

                var newState = (FormWindowState)Settings.Config.WindowState;

                if (newState == FormWindowState.Maximized || newState == FormWindowState.Normal)
                {
                    WindowState = newState;
                }
            }

#if SCREENSHOT_MODE
            checkForUpdatesToolStripMenuItem.Visible = false;
            versionLabel.Visible = false;
            SetBounds(x: 100, y: 100, width: 1800 + 22, height: 1200 + 11); // Tweak size as needed
#endif
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

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
#if !SCREENSHOT_MODE
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
#endif

            Settings.Save();
            base.OnFormClosing(e);
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

            if (keyData == (Keys.Control | Keys.R) || keyData == Keys.F5)
            {
                CloseAndReOpenActiveTab();
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }


        private void OnMainSelectedTabChanged(object sender, EventArgs e)
        {
#if !SCREENSHOT_MODE
            if (string.IsNullOrEmpty(mainTabs.SelectedTab?.ToolTipText))
            {
                Text = "Source 2 Viewer";
            }
            else
            {
                Text = $"Source 2 Viewer - {mainTabs.SelectedTab.ToolTipText}";
            }
#endif

            ShowHideSearch();
        }

        private void ShowHideSearch()
        {
            // enable/disable the search button as necessary
            if (mainTabs.SelectedTab != null && mainTabs.SelectedTab.Controls[nameof(TreeViewWithSearchResults)] is TreeViewWithSearchResults package)
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

        private void CloseAndReOpenActiveTab()
        {
            var tab = mainTabs.SelectedTab;
            if (tab is not null && tab.Tag is ExportData exportData)
            {
                var (newFileContext, packageEntry) = exportData.VrfGuiContext.FileLoader.FindFileWithContext(
                    exportData.PackageEntry?.GetFullPath() ?? exportData.VrfGuiContext.FileName
                );
                OpenFile(newFileContext, packageEntry);
                CloseTab(tab);
            }
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
            Log.Info(nameof(MainForm), $"Closing {tab.Text}");

            if (isClosingCurrentTab && tabIndex > 0)
            {
                mainTabs.SelectedIndex = tabIndex - 1;
            }

            mainTabs.TabPages.Remove(tab);
            tab.Dispose();
        }

        private void CloseAllTabs()
        {
            mainTabs.SelectedIndex = 0;

            //Close all tabs currently open (excluding console)
            var tabCount = mainTabs.TabPages.Count;
            for (var i = 1; i < tabCount; i++)
            {
                CloseTab(mainTabs.TabPages[tabCount - i]);
            }
        }

        private void CloseTabsToLeft(TabPage basePage)
        {
            //Close all tabs to the left of the base (excluding console)
            for (var i = GetTabIndex(basePage) - 1; i > 0; i--)
            {
                CloseTab(mainTabs.TabPages[i]);
            }
        }

        private void CloseTabsToRight(TabPage basePage)
        {
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
                var tabName = thisTab.Text;

                //Can't close tabs to the left/right if there aren't any!
                closeToolStripMenuItemsToLeft.Visible = tabIndex > 1;
                closeToolStripMenuItemsToRight.Visible = tabIndex != mainTabs.TabPages.Count - 1;

                //For UX purposes, hide the option to close the console also (this is disabled later in code too)
                closeToolStripMenuItem.Visible = tabIndex != 0;

                var canExport = tabName != "Console" && tabName != "Explorer";
                exportAsIsToolStripMenuItem.Visible = canExport;
                decompileExportToolStripMenuItem.Visible = canExport;

                clearConsoleToolStripMenuItem.Visible = tabIndex == 0;

                //Show context menu at the mouse position
                tabContextMenuStrip.Tag = e.Location;
                tabContextMenuStrip.Show((Control)sender, e.Location);
            }
        }

        private void OnAboutItemClick(object sender, EventArgs e)
        {
            using var form = new AboutForm();
            form.ShowDialog(this);
        }

        private void OnSettingsItemClick(object sender, EventArgs e)
        {
            using var form = new SettingsForm();
            form.ShowDialog(this);
        }

        private void OpenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using var openDialog = new OpenFileDialog
            {
                InitialDirectory = Settings.Config.OpenDirectory,
                Filter = "Valve Resource Format (*.*_c, *.vpk)|*.*_c;*.vpk;*.vcs|All files (*.*)|*.*",
                Multiselect = true,
                AddToRecent = true,
            };
            var userOK = openDialog.ShowDialog();

            if (userOK != DialogResult.OK)
            {
                return;
            }

            if (openDialog.FileNames.Length > 0)
            {
                Settings.Config.OpenDirectory = Path.GetDirectoryName(openDialog.FileNames[0]);
            }

            foreach (var file in openDialog.FileNames)
            {
                OpenFile(file);
            }
        }

        public void OpenFile(string fileName)
        {
            Log.Info(nameof(MainForm), $"Opening {fileName}");

            if (Regexes.VpkNumberArchive().IsMatch(fileName))
            {
                var fixedPackage = $"{fileName[..^8]}_dir.vpk";

                if (File.Exists(fixedPackage))
                {
                    Log.Warn(nameof(MainForm), $"You opened \"{Path.GetFileName(fileName)}\" but there is \"{Path.GetFileName(fixedPackage)}\"");
                    fileName = fixedPackage;
                }
            }

            var vrfGuiContext = new VrfGuiContext(fileName, null);
            OpenFile(vrfGuiContext, null);

            Settings.TrackRecentFile(fileName);
        }

        public void OpenFile(VrfGuiContext vrfGuiContext, PackageEntry file, TreeViewWithSearchResults packageTreeView = null, bool withoutViewer = false)
        {
            var isPreview = packageTreeView != null;

            var viewMode = (isPreview, withoutViewer) switch
            {
                (true, _) => ResourceViewMode.ViewerOnly,
                (_, true) => ResourceViewMode.ResourceBlocksOnly,
                (_, _) => ResourceViewMode.Default,
            };

            var tabTemp = new TabPage(Path.GetFileName(vrfGuiContext.FileName))
            {
                ToolTipText = vrfGuiContext.FileName,
                Tag = new ExportData
                {
                    PackageEntry = file,
                    VrfGuiContext = vrfGuiContext,
                }
            };
            var tab = tabTemp;
            tab.Disposed += OnTabDisposed;

            void OnTabDisposed(object sender, EventArgs e)
            {
                tab.Disposed -= OnTabDisposed;

                var oldTag = tab.Tag;
                tab.Tag = null;

                if (oldTag is ExportData exportData)
                {
                    exportData.VrfGuiContext.Dispose();
                }
            }

            try
            {
                var parentContext = vrfGuiContext.ParentGuiContext;

                while (parentContext != null)
                {
                    tab.ToolTipText = $"{parentContext.FileName} > {tab.ToolTipText}";

                    parentContext = parentContext.ParentGuiContext;
                }

                var extension = Path.GetExtension(vrfGuiContext.FileName.AsSpan());

                if (MemoryExtensions.Equals(extension, ".vpk", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var game in ExplorerControl.SteamGames)
                    {
                        if (vrfGuiContext.FileName.StartsWith(game.GamePath, StringComparison.OrdinalIgnoreCase))
                        {
                            tab.ImageIndex = ImageList.Images.IndexOfKey($"@app{game.AppID}");

                            break;
                        }
                    }
                }

                if (tab.ImageIndex < 0)
                {
                    if (extension.Length > 0)
                    {
                        extension = extension[1..];
                    }

                    tab.ImageIndex = GetImageIndexForExtension(extension);
                }

                mainTabs.TabPages.Insert(mainTabs.SelectedIndex + 1, tab);

                if (!isPreview)
                {
                    mainTabs.SelectTab(tab);
                }

                tabTemp = null;
            }
            finally
            {
                tabTemp?.Dispose();
            }

            var loadingFile = new LoadingFile();
            tab.Controls.Add(loadingFile);

            var task = Task.Factory.StartNew(() => ProcessFile(vrfGuiContext, file, viewMode));

            task.ContinueWith(
                t =>
                {
                    vrfGuiContext.GLPostLoadAction = null;

                    t.Exception?.Flatten().Handle(ex =>
                    {
                        var control = CodeTextBox.CreateFromException(ex);

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
                    Cursor.Current = Cursors.WaitCursor;

                    tab.SuspendLayout();

                    try
                    {
                        foreach (Control c in t.Result.Controls)
                        {
                            if (tab.IsDisposed || tab.Disposing)
                            {
                                c.Dispose();
                                continue;
                            }

                            tab.Controls.Add(c);
                        }
                    }
                    finally
                    {
                        tab.ResumeLayout();
                    }

                    ShowHideSearch();

                    Cursor.Current = Cursors.Default;
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.FromCurrentSynchronizationContext());

            task.ContinueWith(t =>
                {
                    tab.BeginInvoke(() =>
                    {
                        loadingFile.Dispose();

                        if (isPreview)
                        {
                            packageTreeView.ReplaceListViewWithControl(tab);
                        }
                    });
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.FromCurrentSynchronizationContext());
        }

        private static TabPage ProcessFile(VrfGuiContext vrfGuiContext, PackageEntry entry, ResourceViewMode viewMode)
        {
            Stream stream = null;
            Span<byte> magicData = stackalloc byte[6];

            if (entry != null)
            {
                stream = AdvancedGuiFileLoader.GetPackageEntryStream(vrfGuiContext.ParentGuiContext.CurrentPackage, entry);

                if (stream.Length >= magicData.Length)
                {
                    stream.ReadExactly(magicData);
                    stream.Seek(-magicData.Length, SeekOrigin.Current);
                }
            }
            else
            {
                using var fs = new FileStream(vrfGuiContext.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                if (fs.Length >= magicData.Length)
                {
                    fs.ReadExactly(magicData);
                }
            }

            var magic = BitConverter.ToUInt32(magicData[..4]);
            var magicResourceVersion = BitConverter.ToUInt16(magicData[4..]);

            if (Types.PackageViewer.PackageViewer.IsAccepted(magic))
            {
                var tab = new PackageViewer().Create(vrfGuiContext, stream);

                return tab;
            }
            else if (Types.Viewers.CompiledShader.IsAccepted(magic))
            {
                var viewer = new Types.Viewers.CompiledShader();

                try
                {
                    var tab = viewer.Create(vrfGuiContext, stream);
                    viewer = null;
                    return tab;
                }
                finally
                {
                    viewer?.Dispose();
                }
            }
            else if (Types.Viewers.ClosedCaptions.IsAccepted(magic))
            {
                return new Types.Viewers.ClosedCaptions().Create(vrfGuiContext, stream);
            }
            else if (Types.Viewers.ToolsAssetInfo.IsAccepted(magic))
            {
                return new Types.Viewers.ToolsAssetInfo().Create(vrfGuiContext, stream);
            }
            else if (Types.Viewers.FlexSceneFile.IsAccepted(magic))
            {
                return new Types.Viewers.FlexSceneFile().Create(vrfGuiContext, stream);
            }
            else if (Types.Viewers.NavView.IsAccepted(magic))
            {
                return new Types.Viewers.NavView().Create(vrfGuiContext, stream);
            }
            else if (Types.Viewers.BinaryKeyValues3.IsAccepted(magic))
            {
                return new Types.Viewers.BinaryKeyValues3().Create(vrfGuiContext, stream);
            }
            else if (Types.Viewers.BinaryKeyValues2.IsAccepted(magic, vrfGuiContext.FileName))
            {
                return new Types.Viewers.BinaryKeyValues2().Create(vrfGuiContext, stream);
            }
            else if (Types.Viewers.BinaryKeyValues1.IsAccepted(magic))
            {
                return new Types.Viewers.BinaryKeyValues1().Create(vrfGuiContext, stream);
            }
            else if (Types.Viewers.Resource.IsAccepted(magicResourceVersion))
            {
                return new Types.Viewers.Resource().Create(vrfGuiContext, stream, viewMode, verifyFileSize: entry == null || entry.CRC32 > 0);
            }
            // Raw images and audio files do not really appear in Source 2 projects, but we support viewing them anyway.
            // As some detections rely on the file extension instead of magic bytes,
            // they should be detected at the bottom here, after failing to detect a proper resource file.
            else if (Types.Viewers.Image.IsAccepted(magic))
            {
                return new Types.Viewers.Image().Create(vrfGuiContext, stream);
            }
            else if (Types.Viewers.Image.IsAcceptedVector(vrfGuiContext.FileName))
            {
                return new Types.Viewers.Image().CreateVector(vrfGuiContext, stream);
            }
            else if (Types.Viewers.Audio.IsAccepted(magic, vrfGuiContext.FileName))
            {
                return new Types.Viewers.Audio().Create(vrfGuiContext, stream, viewMode == ResourceViewMode.ViewerOnly);
            }
            else if (Types.Viewers.GridNavFile.IsAccepted(magic))
            {
                return new Types.Viewers.GridNavFile().Create(vrfGuiContext, stream);
            }

            return new Types.Viewers.ByteViewer().Create(vrfGuiContext, stream);
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            // Despite us setting drag effect only on FileDrop this can still be null on drop
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (var fileName in files)
                {
                    OpenFile(fileName);
                }
            }
            else if (e.Data.GetData(DataFormats.UnicodeText) is string text) // Dropping files from web based apps such as VS code
            {
                foreach (var line in text.AsSpan().EnumerateLines())
                {
                    var fileName = line.ToString();

                    if (File.Exists(fileName))
                    {
                        OpenFile(fileName);
                    }
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
                    var treeView = mainTabs.SelectedTab.Controls[nameof(TreeViewWithSearchResults)] as TreeViewWithSearchResults;
                    treeView.SearchAndFillResults(searchText, searchForm.SelectedSearchType);
                }
            }
        }

        private void OpenExplorer_Click(object sender, EventArgs e) => OpenExplorer();

        private void OpenExplorer()
        {
            foreach (TabPage tabPage in mainTabs.TabPages)
            {
                if (tabPage.Text == "Explorer")
                {
                    mainTabs.SelectTab(tabPage);
                    return;
                }
            }

            var explorerTab = new TabPage("Explorer")
            {
                ToolTipText = "Explorer",
                ImageIndex = ImageListLookup["_folder_star"],
            };

            try
            {
                explorerTab.Controls.Add(new ExplorerControl
                {
                    Dock = DockStyle.Fill,
                });
                mainTabs.TabPages.Insert(1, explorerTab);
                mainTabs.SelectTab(explorerTab);
                explorerTab = null;
            }
            finally
            {
                explorerTab?.Dispose();
            }
        }

        private void OpenWelcome()
        {
            var welcomeTab = new TabPage("Welcome")
            {
                ToolTipText = "Welcome",
                ImageIndex = ImageListLookup["_folder_star"],
            };

            try
            {
                welcomeTab.Controls.Add(new WelcomeControl
                {
                    Dock = DockStyle.Fill
                });
                mainTabs.TabPages.Add(welcomeTab);
                mainTabs.SelectTab(welcomeTab);
                welcomeTab = null;
            }
            finally
            {
                welcomeTab?.Dispose();
            }
        }

        public static int GetImageIndexForExtension(ReadOnlySpan<char> extension)
        {
            if (extension.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
            {
                extension = extension[0..^2];
            }

            var lookup = ImageListLookup.GetAlternateLookup<ReadOnlySpan<char>>();

            if (lookup.TryGetValue(extension, out var image))
            {
                return image;
            }

            if (extension.Length > 0 && extension[0] == 'v' && lookup.TryGetValue(extension[1..], out image))
            {
                return image;
            }

            return ImageListLookup["_default"];
        }

        private void ClearConsoleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Log.ClearConsole();
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            if (!Settings.Config.Update.CheckAutomatically)
            {
                return;
            }

            if (Settings.Config.Update.UpdateAvailable)
            {
                checkForUpdatesToolStripMenuItem.Visible = false;
                checkForUpdatesToolStripMenuItem.Enabled = false;
                newVersionAvailableToolStripMenuItem.Text = "New update available";
                newVersionAvailableToolStripMenuItem.Visible = true;
                return;
            }

            var now = DateTime.UtcNow;

            if (DateTime.TryParseExact(Settings.Config.Update.LastCheck, "s", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lastCheck))
            {
                var diff = now.Subtract(lastCheck);

                // Perform auto update check once a day
                if (diff.TotalDays < 1)
                {
                    return;
                }
            }

            Settings.Config.Update.LastCheck = now.ToString("s");

            CheckForUpdatesCore(false);
        }

        private void CheckForUpdatesToolStripMenuItem_Click(object sender, EventArgs e) => CheckForUpdatesCore(true);

        private void CheckForUpdatesCore(bool showForm)
        {
            checkForUpdatesToolStripMenuItem.Enabled = false;

            Task.Run(() => CheckForUpdates(showForm));
        }

        private void NewVersionAvailableToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // This happens when the auto update checker displays the new update label, but there is no actual update data available
            if (!UpdateChecker.IsNewVersionAvailable)
            {
                checkForUpdatesToolStripMenuItem.Visible = true;
                newVersionAvailableToolStripMenuItem.Visible = false;

                Task.Run(() => CheckForUpdates(true));

                return;
            }

            using var form = new UpdateAvailableForm();
            form.ShowDialog(this);
        }

        private async Task CheckForUpdates(bool showForm)
        {
            await UpdateChecker.CheckForUpdates().ConfigureAwait(false);

            await InvokeAsync(() =>
            {
                if (UpdateChecker.IsNewVersionAvailable)
                {
                    checkForUpdatesToolStripMenuItem.Visible = false;
                    newVersionAvailableToolStripMenuItem.Text = $"New {(UpdateChecker.IsNewVersionStableBuild ? "release" : "build")} {UpdateChecker.NewVersion} available";
                    newVersionAvailableToolStripMenuItem.Visible = true;
                }
                else
                {
                    checkForUpdatesToolStripMenuItem.Text = "Up to date";
                    checkForUpdatesToolStripMenuItem.Enabled = true;
                }

                if (showForm)
                {
                    using var form = new UpdateAvailableForm();
                    form.ShowDialog(this);
                }
            }).ConfigureAwait(false);
        }
    }
}
