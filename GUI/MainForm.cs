//#define SCREENSHOT_MODE // Uncomment to hide version, keep title bar static, set an exact window size

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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
using Svg.Skia;
using ValveResourceFormat.IO;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using ResourceViewMode = GUI.Types.Viewers.ResourceViewMode;

#nullable disable

namespace GUI
{
    partial class MainForm : Form
    {
        // Disposable fields should be disposed
        // for some reason disposing it makes closing GUI very slow
        //
        // Never lookup icons from this list, use Icons and ExtensionIcons properties.
        public static ImageList ImageList { get; private set; }

        /// <summary>
        /// Lookup an UI icon from GUI/Icons/ folder.
        /// </summary>
        public static Dictionary<string, int> Icons { get; private set; } = [];

        /// <summary>
        /// Lookup a file extension icon from GUI/Icons/AssetTypes/ folder.
        /// </summary>
        public static Dictionary<string, int> ExtensionIcons { get; private set; } = [];

        /// <summary>
        /// Lookup a game icon by appid that are loaded by the Explorer control from Steam.
        /// </summary>
        public static Dictionary<int, int> GameIcons { get; private set; } = [];

        private readonly string[] Args;

        private SearchForm searchForm = new();

        static MainForm()
        {
            GLFWProvider.SetErrorCallback((errorCode, description) =>
            {
                throw new OpenTK.Windowing.GraphicsLibraryFramework.GLFWException(description, errorCode);
            });
            GLFWProvider.CheckForMainThread = false;
            GLFWProvider.EnsureInitialized();
        }

        public MainForm(string[] args)
        {
            Args = args;

            Settings.Load();
            Themer.InitializeTheme();
            InitializeComponent();
            LoadIcons();
            Themer.ApplyTheme(this);

            mainTabs.ImageList = ImageList;
            mainTabs.SelectedIndexChanged += OnMainSelectedTabChanged;
            mainTabs.BackColor = Themer.CurrentThemeColors.App;
            mainTabs.SelectTabColor = Themer.CurrentThemeColors.AppMiddle;
            mainTabs.SelectedForeColor = Themer.CurrentThemeColors.Contrast;
            mainTabs.ForeColor = Themer.CurrentThemeColors.ContrastSoft;
            mainTabs.HoverColor = Themer.CurrentThemeColors.HoverAccent;
            mainTabs.AccentColor = Themer.CurrentThemeColors.Accent;
            mainTabs.SelectionLine = false;
            mainTabs.EndEllipsis = true;
            mainTabs.TabTopRadius = 8;

            // Display version
            {
                var version = Application.ProductVersion;
                var versionPlus = version.IndexOf('+', StringComparison.InvariantCulture);
                string versionDisplay;

                if (versionPlus > 0)
                {
                    // If version ends with ".0", display part of the commit hash, otherwise the zero is replaced with CI build number
                    if (version[versionPlus - 2] == '.' && version[versionPlus - 1] == '0')
                    {
                        versionPlus += 8;
                    }

                    versionDisplay = string.Concat("v", version.AsSpan(0, versionPlus));
                }
                else
                {
                    versionDisplay = string.Concat("v", version);

#if !CI_RELEASE_BUILD // Set in Directory.Build.props
                    versionDisplay += "-dev";
#endif
                }

#if DEBUG
                versionDisplay += " (DEBUG)";
#endif

                mainFormBottomPanel.SetVersionText(versionDisplay);
            }

            CheckForUpdatesIfNecessary();

            HardwareAcceleratedTextureDecoder.Decoder = new GLTextureDecoder();
            RenderLoopThread.Initialize(this);

#if DEBUG
            if (args.Length > 0 && args[0] == "validate_shaders")
            {
                GUI.Types.Renderer.ShaderLoader.ValidateShaders();
                Environment.Exit(0);
                return;
            }
#else
            fileToolStripMenuItem.DropDownItems.Remove(validateShadersToolStripMenuItem);
#endif

            // Force refresh title due to OpenFile calls above, SelectedIndexChanged is not called in the same tick
            OnMainSelectedTabChanged(null, null);
        }

        private void LoadIcons()
        {
            ImageList = new ImageList
            {
                ColorDepth = ColorDepth.Depth32Bit,
                ImageSize = new Size(this.AdjustForDPI(24), this.AdjustForDPI(24)),
            };

            var resources = Program.Assembly.GetManifestResourceNames().Where(static r => r.StartsWith("GUI.Icons.", StringComparison.Ordinal));

            if (Themer.CurrentThemeColors.ColorMode == SystemColorMode.Classic)
            {
                // In light mode, sort icons so that _light icons come first
                resources = resources.OrderByDescending(static r => r.Contains("_light", StringComparison.Ordinal));
            }
            else
            {
                // In dark mode, just filter out all _light icons
                resources = resources.Where(static r => !r.Contains("_light", StringComparison.Ordinal));
            }

            const string AssetTypesAliasesFile = "GUI.Icons.AssetTypes.aliases.txt";

            foreach (var fullName in resources)
            {
                if (fullName == AssetTypesAliasesFile)
                {
                    continue;
                }

                var name = fullName.AsSpan("GUI.Icons.".Length);
                var extension = Path.GetExtension(name);
                name = Path.GetFileNameWithoutExtension(name);

                var isAssetType = name.StartsWith("AssetTypes.", StringComparison.Ordinal);
                var isLightIcon = name.EndsWith("_light", StringComparison.Ordinal);

                if (isAssetType)
                {
                    name = name["AssetTypes.".Length..];
                }

                if (isLightIcon)
                {
                    name = name[..^"_light".Length];
                }

                using var stream = Program.Assembly.GetManifestResourceStream(fullName);
                Debug.Assert(stream is not null);

                var iconName = name.ToString();
                var index = ImageList.Images.Count;

                if (isAssetType)
                {
                    if (!ExtensionIcons.TryAdd(iconName, index))
                    {
                        continue;
                    }
                }
                else if (!Icons.TryAdd(iconName, index))
                {
                    continue;
                }

                if (extension.SequenceEqual(".svg"))
                {
#pragma warning disable CA2000 // Dispose objects before losing scope, this is a false positive
                    using var svg = new SKSvg();
                    svg.Load(stream);

                    using var bitmap = Themer.SvgToBitmap(svg, ImageList.ImageSize.Width, ImageList.ImageSize.Height);
                    AddFixedImageToImageList(bitmap, ImageList);
#pragma warning restore CA2000

                    if (iconName == "Logo")
                    {
                        mainLogo.Image = Themer.SvgToBitmap(svg, mainLogo.Width, mainLogo.Height);
                    }
                }
                else
                {
                    Debug.Assert(false, "Use only svg icons");
                }
            }

            {
                using var stream = Program.Assembly.GetManifestResourceStream(AssetTypesAliasesFile);
                using var reader = new StreamReader(stream);

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var space = line.IndexOf(' ', StringComparison.Ordinal);
                    Debug.Assert(ExtensionIcons.TryAdd(line[..space], ExtensionIcons[line[(space + 1)..]]));
                }
            }
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

            var consoleTab = new ConsoleTab();
            Log.SetConsoleTab(consoleTab);
            var consoleTabPage = consoleTab.CreateTab();
            consoleTabPage.ImageIndex = Icons["Log"];
            mainTabs.TabPages.Add(consoleTabPage);
            consoleTab.InitializeFont();

            if (Settings.IsFirstStartup)
            {
                OpenWelcome();
            }
            else if (Args.Length > 0)
            {
                OpenCommandLineArgFiles(Args);
            }
            else if (Settings.Config.OpenExplorerOnStart != 0)
            {
                OpenExplorer();
            }

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

            InitializeSystemMenu();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            //if the user presses CTRL + W, and there is a tab open, close the active tab
            if (keyData == (Keys.Control | Keys.W) && mainTabs.SelectedTab != null)
            {
                mainTabs.CloseTab(mainTabs.SelectedTab);
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
        }

        private void CloseAndReOpenActiveTab()
        {
            var tab = mainTabs.SelectedTab;
            if (tab is not null && tab.Tag is ExportData exportData)
            {
                var (newFileContext, packageEntry) = exportData.VrfGuiContext.FindFileWithContext(
                    exportData.PackageEntry?.GetFullPath() ?? exportData.VrfGuiContext.FileName
                );
                OpenFile(newFileContext, packageEntry);
                mainTabs.CloseTab(tab);
            }
        }


        private void CloseAllTabs()
        {
            mainTabs.SelectedIndex = 0;

            //Close all tabs currently open (excluding console)
            var tabCount = mainTabs.TabPages.Count;
            for (var i = 1; i < tabCount; i++)
            {
                mainTabs.CloseTab(mainTabs.TabPages[tabCount - i]);
            }
        }

        private void CloseTabsToLeft(TabPage basePage)
        {
            //Close all tabs to the left of the base (excluding console)
            for (var i = mainTabs.GetTabIndex(basePage) - 1; i > 0; i--)
            {
                mainTabs.CloseTab(mainTabs.TabPages[i]);
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

                mainTabs.CloseTab(mainTabs.TabPages[tabCount - i]);
            }
        }

        private void OnTabClick(object sender, MouseEventArgs e)
        {
            //Work out what tab we're interacting with
            var tabControl = sender as TabControl;
            var tabs = tabControl.TabPages;

            var tabIndex = 0;
            TabPage thisTab = null;

            for (; tabIndex < tabs.Count; tabIndex++)
            {
                if (tabControl.GetTabRect(tabIndex).Contains(e.Location))
                {
                    thisTab = tabs[tabIndex];
                    break;
                }
            }

            if (thisTab == null)
            {
                return;
            }

            if (e.Button == MouseButtons.Middle)
            {
                mainTabs.CloseTab(thisTab);
            }
            else if (e.Button == MouseButtons.Right)
            {
                var tabName = thisTab.Text;

                //Can't close tabs to the left/right if there aren't any!
                closeToolStripMenuItemsToLeft.Visible = tabIndex > 1;
                closeToolStripMenuItemsToRight.Visible = tabIndex != mainTabs.TabPages.Count - 1;

                //For UX purposes, hide the option to close the console also (this is disabled later in code too)
                closeToolStripMenuItem.Visible = tabIndex != 0;

                var canExport = thisTab.Tag is ExportData exportData;
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
            foreach (TabPage tabPage in mainTabs.TabPages)
            {
                if (tabPage.Text == "Settings")
                {
                    mainTabs.SelectTab(tabPage);
                    return;
                }
            }

            var seettingsTab = new ThemedTabPage("Settings")
            {
                ToolTipText = "Settings",
                ImageIndex = Icons["Settings"],
            };

            try
            {
                seettingsTab.Controls.Add(new SettingsControl
                {
                    Dock = DockStyle.Fill,
                });
                mainTabs.TabPages.Insert(1, seettingsTab);
                mainTabs.SelectTab(seettingsTab);
                seettingsTab = null;
            }
            finally
            {
                seettingsTab?.Dispose();
            }
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

            var tabTemp = new ThemedTabPage(Path.GetFileName(vrfGuiContext.FileName))
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
                    exportData.DisposableContents?.Dispose();
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
                            if (GameIcons.TryGetValue(game.AppID, out var imageIndexGame))
                            {
                                tab.ImageIndex = imageIndexGame;
                            }

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

                if (!isPreview)
                {
                    mainTabs.TabPages.Insert(mainTabs.SelectedIndex + 1, tab);
                    mainTabs.SelectTab(tab);
                }

                tabTemp = null;
            }
            finally
            {
                tabTemp?.Dispose();
            }

            Control loadingFile = null;

            if (!isPreview)
            {
                loadingFile = new LoadingFile();
                tab.Controls.Add(loadingFile);
            }
            else
            {
                Cursor.Current = Cursors.WaitCursor;
            }

            var taskLoad = Task.Run(() => ProcessFile(vrfGuiContext, file, viewMode));

            taskLoad.ContinueWith(t =>
            {
                vrfGuiContext.GLPostLoadAction = null;

                t.Exception?.Flatten().Handle(ex =>
                {
                    BeginInvoke(() =>
                    {
                        if (isPreview)
                        {
                            Cursor.Current = Cursors.Default;
                        }

                        var control = CodeTextBox.CreateFromException(ex, tab.ToolTipText);

                        tab.Controls.Add(control);
                    });

                    return false;
                });
            },
            TaskContinuationOptions.OnlyOnFaulted);

            var task = taskLoad.ContinueWith(t =>
            {
                BeginInvoke(() =>
                {
                    Cursor.Current = Cursors.WaitCursor;

                    try
                    {
                        var viewer = t.Result;

                        if (tab.IsDisposed)
                        {
                            viewer.Dispose();
                            return; // closed tab before it loaded
                        }

                        if (tab.Tag is ExportData exportData)
                        {
                            exportData.DisposableContents = viewer;
                        }
                        else
                        {
                            Debug.Assert(false);
                        }

                        viewer.Create(tab);
                    }
                    finally
                    {
                        Cursor.Current = Cursors.Default;
                    }
                });
            },
            TaskContinuationOptions.OnlyOnRanToCompletion);

            task.ContinueWith(t =>
            {
                vrfGuiContext.GLPostLoadAction = null;

                t.Exception?.Flatten().Handle(ex =>
                {
                    try
                    {
                        BeginInvoke(() =>
                        {
                            var control = CodeTextBox.CreateFromException(ex, tab.ToolTipText);

                            tab.Controls.Add(control);
                        });

                        Log.Error(nameof(MainForm), ex.ToString());

                        return false;
                    }
                    catch (Exception e)
                    {
                        Program.ShowError(e);

                        return true;
                    }
                });
            },
            TaskContinuationOptions.OnlyOnFaulted);

            task.ContinueWith(t =>
            {
                BeginInvoke(() =>
                {
                    loadingFile?.Dispose();

                    if (isPreview)
                    {
                        packageTreeView.ReplaceListViewWithControl(tab);
                    }
                });
            });
        }

        private static async Task<Types.Viewers.IViewer> ProcessFile(VrfGuiContext vrfGuiContext, PackageEntry entry, ResourceViewMode viewMode)
        {
            await Task.Yield();

            Stream stream = null;
            Span<byte> magicData = stackalloc byte[6];

            if (entry != null)
            {
                stream = GameFileLoader.GetPackageEntryStream(vrfGuiContext.ParentGuiContext.CurrentPackage, entry);

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
                var viewer = new PackageViewer(vrfGuiContext);
                await viewer.LoadAsync(stream).ConfigureAwait(false);
                return viewer;
            }
            else if (Types.Viewers.CompiledShader.IsAccepted(magic))
            {
                var viewer = new Types.Viewers.CompiledShader(vrfGuiContext);
                await viewer.LoadAsync(stream).ConfigureAwait(false);
                return viewer;
            }
            else if (Types.Viewers.ClosedCaptions.IsAccepted(magic))
            {
                var viewer = new Types.Viewers.ClosedCaptions(vrfGuiContext);
                await viewer.LoadAsync(stream).ConfigureAwait(false);
                return viewer;
            }
            else if (Types.Viewers.ToolsAssetInfo.IsAccepted(magic))
            {
                var viewer = new Types.Viewers.ToolsAssetInfo(vrfGuiContext);
                await viewer.LoadAsync(stream).ConfigureAwait(false);
                return viewer;
            }
            else if (Types.Viewers.FlexSceneFile.IsAccepted(magic))
            {
                var viewer = new Types.Viewers.FlexSceneFile(vrfGuiContext);
                await viewer.LoadAsync(stream).ConfigureAwait(false);
                return viewer;
            }
            else if (Types.Viewers.NavView.IsAccepted(magic))
            {
                var viewer = new Types.Viewers.NavView(vrfGuiContext);
                await viewer.LoadAsync(stream).ConfigureAwait(false);
                return viewer;
            }
            else if (Types.Viewers.BinaryKeyValues3.IsAccepted(magic))
            {
                var viewer = new Types.Viewers.BinaryKeyValues3(vrfGuiContext);
                await viewer.LoadAsync(stream).ConfigureAwait(false);
                return viewer;
            }
            else if (Types.Viewers.BinaryKeyValues2.IsAccepted(magic, vrfGuiContext.FileName))
            {
                var viewer = new Types.Viewers.BinaryKeyValues2(vrfGuiContext);
                await viewer.LoadAsync(stream).ConfigureAwait(false);
                return viewer;
            }
            else if (Types.Viewers.BinaryKeyValues1.IsAccepted(magic))
            {
                var viewer = new Types.Viewers.BinaryKeyValues1(vrfGuiContext);
                await viewer.LoadAsync(stream).ConfigureAwait(false);
                return viewer;
            }
            else if (Types.Viewers.Resource.IsAccepted(magicResourceVersion))
            {
                var viewer = new Types.Viewers.Resource(vrfGuiContext, viewMode, verifyFileSize: entry == null || entry.CRC32 > 0);
                await viewer.LoadAsync(stream).ConfigureAwait(false);
                return viewer;
            }
            // Raw images and audio files do not really appear in Source 2 projects, but we support viewing them anyway.
            // As some detections rely on the file extension instead of magic bytes,
            // they should be detected at the bottom here, after failing to detect a proper resource file.
            else if (Types.Viewers.Image.IsAccepted(magic))
            {
                var viewer = new Types.Viewers.Image(vrfGuiContext);
                await viewer.LoadAsync(stream).ConfigureAwait(false);
                return viewer;
            }
            else if (Types.Viewers.ImageVector.IsAccepted(vrfGuiContext.FileName))
            {
                var viewer = new Types.Viewers.ImageVector(vrfGuiContext);
                await viewer.LoadAsync(stream).ConfigureAwait(false);
                return viewer;
            }
            else if (Types.Viewers.Audio.IsAccepted(magic, vrfGuiContext.FileName))
            {
                var viewer = new Types.Viewers.Audio(vrfGuiContext, viewMode == ResourceViewMode.ViewerOnly);
                await viewer.LoadAsync(stream).ConfigureAwait(false);
                return viewer;
            }
            else if (Types.Viewers.GridNavFile.IsAccepted(magic))
            {
                var viewer = new Types.Viewers.GridNavFile(vrfGuiContext);
                await viewer.LoadAsync(stream).ConfigureAwait(false);
                return viewer;
            }

            var byteViewer = new Types.Viewers.ByteViewer(vrfGuiContext);
            await byteViewer.LoadAsync(stream).ConfigureAwait(false);
            return byteViewer;
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
        /// Handles find/search functionality for the selected tab.
        /// </summary>
        /// <param name="sender">Object which raised event.</param>
        /// <param name="e">Event data.</param>
        private void FindToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (mainTabs.SelectedTab == null)
            {
                return;
            }

            var codeTextBox = FindCodeTextBoxInControl(mainTabs.SelectedTab);
            if (codeTextBox != null)
            {
                codeTextBox.ShowFindDialog();
                return;
            }

            var package = mainTabs.SelectedTab.Controls.OfType<TreeViewWithSearchResults>().FirstOrDefault();
            if (package != null)
            {
                var result = searchForm.ShowDialog();
                if (result == DialogResult.OK)
                {
                    var searchText = searchForm.SearchText;
                    if (!string.IsNullOrEmpty(searchText))
                    {
                        package.SearchAndFillResults(searchText, searchForm.SelectedSearchType);
                    }
                }
                return;
            }

            mainTabs.SelectedTab.Controls.OfType<ExplorerControl>().FirstOrDefault()?.FocusFilter();
        }

        private static CodeTextBox FindCodeTextBoxInControl(Control container)
        {
            if (container is CodeTextBox codeTextBox)
            {
                return codeTextBox;
            }

            if (container is TabControl tabControl && tabControl.SelectedTab != null)
            {
                return FindCodeTextBoxInControl(tabControl.SelectedTab);
            }

            foreach (Control child in container.Controls)
            {
                var found = FindCodeTextBoxInControl(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
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

            var explorerTab = new ThemedTabPage("Explorer")
            {
                ToolTipText = "Explorer",
                ImageIndex = Icons["Explorer"],
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
            var welcomeTab = new ThemedTabPage("Welcome")
            {
                ToolTipText = "Welcome",
                ImageIndex = Icons["WelcomeScreen"],
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

            var lookup = ExtensionIcons.GetAlternateLookup<ReadOnlySpan<char>>();

            if (lookup.TryGetValue(extension, out var image))
            {
                return image;
            }

            if (extension.Length > 0 && extension[0] == 'v' && lookup.TryGetValue(extension[1..], out image))
            {
                return image;
            }

            return Icons["File"];
        }

        private void ClearConsoleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Log.ClearConsole();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);

            mainFormBottomPanel.Text = Text;
        }

        private void CheckForUpdatesIfNecessary()
        {
            if (!Settings.Config.Update.CheckAutomatically)
            {
                return;
            }

            if (Settings.Config.Update.UpdateAvailable)
            {
                mainFormBottomPanel.SetNewVersionAvailable();
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

            Task.Run(CheckForUpdates);
        }

        private async Task CheckForUpdates()
        {
            await UpdateChecker.CheckForUpdates().ConfigureAwait(false);

            if (UpdateChecker.IsNewVersionAvailable)
            {
                await InvokeAsync(() =>
                {
                    mainFormBottomPanel.SetNewVersionAvailable();
                }).ConfigureAwait(false);
            }
        }

        // Based on https://www.codeproject.com/articles/Adding-and-using-32-bit-alphablended-images-and-ic
        // Fixes adding images with proper transparency without incorrect anti aliasing
        public static unsafe void AddFixedImageToImageList(Bitmap bm, ImageList il)
        {
            Debug.Assert(bm.Size == il.ImageSize);

            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biWidth = bm.Width;
            bmi.bmiHeader.biHeight = bm.Height;

            bm.RotateFlip(RotateFlipType.RotateNoneFlipY);

            using var hBitmap = PInvoke.CreateDIBSection((HDC)IntPtr.Zero, &bmi, DIB_USAGE.DIB_RGB_COLORS, out var ppvBits, null, 0);

            var bitmapData = bm.LockBits(new Rectangle(0, 0, bm.Width, bm.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var byteCount = bm.Height * bitmapData.Stride;
            Buffer.MemoryCopy((void*)bitmapData.Scan0, ppvBits, byteCount, byteCount);
            bm.UnlockBits(bitmapData);

            using var ilHandle = new DeleteObjectSafeHandle(il.Handle, ownsHandle: false);
            PInvoke.ImageList_Add(ilHandle, hBitmap, default);
        }
    }
}
