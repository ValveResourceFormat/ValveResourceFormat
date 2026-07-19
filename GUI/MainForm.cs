//#define SCREENSHOT_MODE // Uncomment to hide version, keep title bar static, set an exact window size

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Types.Exporter;
using GUI.Types.GLViewers;
using GUI.Types.PackageViewer;
using GUI.Utils;
using OpenTK.Windowing.Desktop;
using SteamDatabase.ValvePak;
using Svg.Skia;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;
using ResourceViewMode = GUI.Types.Viewers.ResourceViewMode;

namespace GUI
{
    partial class MainForm : Form
    {
        private readonly string[] Args;
        internal ExplorerControl? explorerControl;

        private SearchForm? searchForm;
#pragma warning disable CA2213 // Disposed in OnFormClosing
        private Ipc.IpcWindow? ipcWindow;
#pragma warning restore CA2213

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

            // Let the explorer start scanning games before the window even spawns
            if (args.Length == 0 && (Settings.IsFirstStartup || Settings.Config.OpenExplorerOnStart != 0))
            {
                EnsureExplorerControl();
            }

            Themer.ApplyTheme(this);

            if (Settings.Config.WindowWidth > 0 && Settings.Config.WindowHeight > 0)
            {
                StartPosition = FormStartPosition.Manual;

                if ((FormWindowState)Settings.Config.WindowState == FormWindowState.Maximized)
                {
                    WindowState = FormWindowState.Maximized;
                }
            }

            mainTabs.ImageList = AppIcons.ImageList;
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
                var version = Program.ProductVersion;
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

            HardwareAcceleratedTextureDecoder.Decoder = new GLTextureDecoder(VrfGuiContext.Logger);
            RenderLoopThread.Initialize(this);

#if DEBUG
            if (args.Length > 0 && args[0] == "validate_shaders")
            {
                ValidateShaders();
                Environment.Exit(0);
                return;
            }
#else
            fileToolStripMenuItem.DropDownItems.Remove(validateShadersToolStripMenuItem);
#endif

            // Force refresh title due to OpenFile calls above, SelectedIndexChanged is not called in the same tick
            OnMainSelectedTabChanged(null, EventArgs.Empty);
        }

        private void LoadIcons()
        {
            AppIcons.Load(this.AdjustForDPI(24));

            mainLogo.Image = Themer.SvgToBitmap(AppIcons.ExtensionSVGS["Logo"], mainLogo.Width, mainLogo.Height);
        }

        public void OpenCommandLineArgFiles(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                var file = args[i];

                if (VpkProtocol.IsVpkUri(file))
                {
                    OpenVpkUri(file);
                    continue;
                }

                if (!File.Exists(file))
                {
                    Log.Error(nameof(MainForm), $"File '{file}' does not exist.");
                    mainTabs.OpenTab("Console");
                    continue;
                }

                file = Path.GetFullPath(file);
                OpenFile(file);
            }

            OnMainSelectedTabChanged(null, EventArgs.Empty);
        }

        private void OpenVpkUri(string uri)
        {
            var resolved = VpkProtocol.Resolve(uri, out var plainFilePath);

            if (resolved == null)
            {
                if (plainFilePath != null)
                {
                    // No inner file path was specified, open the file itself
                    OpenFile(plainFilePath);
                }
                else
                {
                    mainTabs.OpenTab("Console");
                }

                return;
            }

            var innerFile = resolved.Entry.GetFullPath();

            Log.Info(nameof(MainForm), $"Opening {innerFile}");

            var package = resolved.Package;
            try
            {
                var vrfGuiContext = new VrfGuiContext(resolved.PackagePath, null)
                {
                    CurrentPackage = package
                };
                var fileContext = new VrfGuiContext(innerFile, vrfGuiContext);
                package = null;

                try
                {
                    OpenFile(fileContext, resolved.Entry);
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
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            var consoleTab = new ConsoleTab();
            Log.SetConsoleTab(consoleTab);
            var consoleTabPage = consoleTab.CreateTab();
            consoleTabPage.ImageIndex = AppIcons.Icons["Log"];
            mainTabs.TabPages.Add(consoleTabPage);
            consoleTab.InitializeFont();

#if SCREENSHOT_MODE
            mainFormBottomPanel.Visible = false;
            SetBounds(x: 100, y: 100, width: 480 + 6, height: 480 + 3); // Tweak size as needed
            unsafe
            {
                var preference = Windows.Win32.Graphics.Dwm.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND;
                PInvoke.DwmSetWindowAttribute((Windows.Win32.Foundation.HWND)Handle,
                    Windows.Win32.Graphics.Dwm.DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE,
                    &preference,
                    sizeof(Windows.Win32.Graphics.Dwm.DWM_WINDOW_CORNER_PREFERENCE));
            }
#else
            if (StartPosition == FormStartPosition.Manual)
            {
                var maximized = WindowState == FormWindowState.Maximized;
                var placement = new WINDOWPLACEMENT
                {
                    length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>(),
                    showCmd = maximized ? SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED : SHOW_WINDOW_CMD.SW_SHOWNORMAL,
                    rcNormalPosition = new Windows.Win32.Foundation.RECT
                    {
                        left = Settings.Config.WindowLeft,
                        top = Settings.Config.WindowTop,
                        right = Settings.Config.WindowLeft + Settings.Config.WindowWidth,
                        bottom = Settings.Config.WindowTop + Settings.Config.WindowHeight,
                    },
                };

                PInvoke.SetWindowPlacement((Windows.Win32.Foundation.HWND)Handle, placement);
            }
#endif

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

            ipcWindow = new(args => BeginInvoke(() =>
            {
                OpenCommandLineArgFiles(args);

                if (WindowState == FormWindowState.Minimized)
                {
                    WindowState = FormWindowState.Normal;
                }

                Activate();
            }));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
#if !SCREENSHOT_MODE
            var placement = new WINDOWPLACEMENT
            {
                length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>(),
            };

            if (PInvoke.GetWindowPlacement((Windows.Win32.Foundation.HWND)Handle, ref placement))
            {
                Settings.Config.WindowLeft = placement.rcNormalPosition.left;
                Settings.Config.WindowTop = placement.rcNormalPosition.top;
                Settings.Config.WindowWidth = placement.rcNormalPosition.right - placement.rcNormalPosition.left;
                Settings.Config.WindowHeight = placement.rcNormalPosition.bottom - placement.rcNormalPosition.top;
                Settings.Config.WindowState = (int)(placement.showCmd == SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED ? FormWindowState.Maximized : FormWindowState.Normal);
            }
#endif

            ipcWindow?.Dispose();

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
            if (keyData == (Keys.Control | Keys.E) && mainTabs.SelectedTab != null)
            {
                CloseTabsToRight(mainTabs.SelectedTab);
            }

            if (keyData == (Keys.Control | Keys.R) || keyData == Keys.F5)
            {
                CloseAndReOpenActiveTab();
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }


        private void OnMainSelectedTabChanged(object? sender, EventArgs e)
        {
#if !SCREENSHOT_MODE
            UpdateWindowTitle(mainTabs.SelectedTab?.ToolTipText);
            UpdateBottomPanelKeybindings();
#endif
        }

        private void UpdateWindowTitle(string? toolTipText)
        {
#if !SCREENSHOT_MODE
            Text = string.IsNullOrEmpty(toolTipText)
                ? "Source 2 Viewer"
                : $"Source 2 Viewer - {toolTipText}";
#endif
        }

        /// <summary>
        /// Resets the window title to the selected tab. Called when a package preview is cleared (e.g. a folder is
        /// shown) so the title stops reflecting the file that was being previewed.
        /// </summary>
        public void ResetPreviewTitle() => UpdateWindowTitle(mainTabs.SelectedTab?.ToolTipText);

        private void UpdateBottomPanelKeybindings()
        {
            var viewerType = KeybindingRegistry.GetViewerTypeFromTab(mainTabs.SelectedTab);
            var keybindings = KeybindingRegistry.GetKeybindingsForViewer(viewerType);
            mainFormBottomPanel.UpdateKeybindings(keybindings);
        }

        /// <summary>
        /// Shows the keybindings for a previewed viewer
        /// </summary>
        public void ShowPreviewKeybindings(TabPage previewTab)
        {
            var keybindings = KeybindingRegistry.GetKeybindingsForViewer(KeybindingRegistry.GetViewerTypeFromTab(previewTab));
            mainFormBottomPanel.UpdateKeybindings(keybindings);
        }

        /// <summary>
        /// Shows the keybindings of the selected tab.
        /// </summary>
        public void ShowSelectedTabKeybindings() => UpdateBottomPanelKeybindings();

        private void CloseAndReOpenActiveTab()
        {
            var tab = mainTabs.SelectedTab;
            if (tab is not null && tab.Tag is ExportData exportData)
            {
                var (newFileContext, packageEntry) = exportData.VrfGuiContext.FindFileWithContext(
                    exportData.PackageEntry?.GetFullPath() ?? exportData.VrfGuiContext.FileName
                );

                if (newFileContext != null)
                {
                    OpenFile(newFileContext, packageEntry);
                    mainTabs.CloseTab(tab);
                }
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
            if (sender is not TabControl tabControl)
            {
                return;
            }

            var tabs = tabControl.TabPages;

            var tabIndex = 0;
            TabPage? thisTab = null;

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
                toolStripSeparator5.Visible = canExport || tabIndex == 0;
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
                ImageIndex = AppIcons.Icons["Settings"],
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
            var files = AppFileDialogs.OpenFiles(null, "Valve Resource Format (*.*_c, *.vpk)|*.*_c;*.vpk;*.vcs|All files (*.*)|*.*");

            if (files == null)
            {
                return;
            }

            foreach (var file in files)
            {
                OpenFile(file);
            }
        }

        public void OpenFile(string fileName)
        {
            Log.Info(nameof(MainForm), $"Opening {fileName}");

            fileName = VpkProtocol.ResolveDirVpkPath(fileName);

            var vrfGuiContext = new VrfGuiContext(fileName, null);
            OpenFile(vrfGuiContext, null);

            Settings.TrackRecentFile(fileName);
        }

        public void OpenFile(VrfGuiContext vrfGuiContext, PackageEntry? file, TreeViewWithSearchResults? packageTreeView = null, bool withoutViewer = false)
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

            void OnTabDisposed(object? sender, EventArgs e)
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
                    tab.ToolTipText = $"{tab.ToolTipText} ← {parentContext.FileName}";

                    parentContext = parentContext.ParentGuiContext;
                }

                var extension = Path.GetExtension(vrfGuiContext.FileName.AsSpan());

                if (MemoryExtensions.Equals(extension, ".vpk", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var game in ExplorerControl.SteamGames)
                    {
                        if (vrfGuiContext.FileName.StartsWith(game.GamePath, StringComparison.OrdinalIgnoreCase))
                        {
                            if (AppIcons.GameIcons.TryGetValue(game.AppID, out var imageIndexGame))
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

                    tab.ImageIndex = AppIcons.GetImageIndexForExtension(extension);
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

            if (isPreview)
            {
                // The preview tab is not in mainTabs, so update the window title to the previewed file ourselves.
                UpdateWindowTitle(tab.ToolTipText);
            }

            // For a preview of the same type as the one already shown, keep that view frozen while the new file loads
            // and show no loading panel; the new viewer is swapped in once ready. Otherwise show the loading panel.
            var keepFrozen = isPreview && packageTreeView!.IsSamePreviewType(file?.TypeName);

            LoadingFile? loadingFile = null;

            if (!keepFrozen)
            {
#pragma warning disable CA2000 // Ownership is transferred to the tab, which disposes it
                loadingFile = new LoadingFile(vrfGuiContext.FileName);
#pragma warning restore CA2000
                tab.Controls.Add(loadingFile);

                if (isPreview)
                {
                    // Show the loading panel in the preview area right away (replacing the blank page).
                    Debug.Assert(packageTreeView != null);
                    packageTreeView.ReplaceListViewWithControl(tab, file?.TypeName);
                }
            }

            Types.Viewers.IViewer? createdViewer = null;

            var taskLoad = Task.Run(() => Types.Viewers.ViewerFactory.CreateAndLoadAsync(vrfGuiContext, file, viewMode));

            taskLoad.ContinueWith(t =>
            {
                vrfGuiContext.GLPostLoadAction = null;

                t.Exception?.Flatten().Handle(ex =>
                {
                    BeginInvoke(() =>
                    {
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
                        createdViewer = viewer;

                        if (mainTabs.SelectedTab == tab)
                        {
                            UpdateBottomPanelKeybindings();
                        }
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
                    if (keepFrozen)
                    {
                        // Same-type preview: swap the frozen previous view for the newly loaded viewer.
                        Debug.Assert(packageTreeView != null);
                        packageTreeView.ReplaceListViewWithControl(tab, file?.TypeName);
                    }
                    else
                    {
                        // The tab is already shown; disposing the loading panel reveals the viewer behind it.
                        loadingFile?.Dispose();
                    }

                    // Revealing the viewer does not reliably deliver a paint to the underlying GL control, so tell
                    // the viewer to redraw now that it is visible.
                    createdViewer?.NotifyVisible();
                });
            });
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            // Despite us setting drag effect only on FileDrop this can still be null on drop
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (var fileName in files)
                {
                    OpenFile(fileName);
                }
            }
            else if (e.Data?.GetData(DataFormats.UnicodeText) is string text) // Dropping files from web based apps such as VS code
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
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
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
                searchForm ??= new();
                searchForm.SetSearchableUserDataKeys(package.GetSearchDataKeysAsync());
                var result = searchForm.ShowDialog();
                if (result == DialogResult.OK)
                {
                    var searchText = searchForm.SearchText;
                    var filterKey = searchForm.SelectedFilterKey;
                    if (!string.IsNullOrEmpty(searchText) || filterKey != null)
                    {
                        package.SearchAndFillResults(searchText, searchForm.SelectedSearchType, filterKey, searchForm.SelectedFilterValue);
                    }
                }
                return;
            }

            mainTabs.SelectedTab.Controls.OfType<ExplorerControl>().FirstOrDefault()?.FocusFilter();
        }

        private static CodeTextBox? FindCodeTextBoxInControl(Control container)
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

        private ExplorerControl EnsureExplorerControl()
        {
            explorerControl ??= new ExplorerControl { Dock = DockStyle.Fill };
            return explorerControl;
        }

        private void OpenExplorer()
        {
            if (mainTabs.OpenTab("Explorer"))
            {
                return;
            }

            var explorerTab = new ThemedTabPage("Explorer")
            {
                ToolTipText = "Explorer",
                ImageIndex = AppIcons.Icons["Explorer"],
            };

            try
            {
                explorerTab.Controls.Add(EnsureExplorerControl());
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
                ImageIndex = AppIcons.Icons["WelcomeScreen"],
            };

            try
            {
                welcomeTab.Controls.Add(new WelcomeControl(EnsureExplorerControl())
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

        private void ClearConsoleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Log.ClearConsole();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);

            mainFormBottomPanel.Text = Text;
        }

        private async void CheckForUpdatesIfNecessary()
        {
            if (await UpdateChecker.CheckForUpdatesIfNecessary().ConfigureAwait(true))
            {
                mainFormBottomPanel.SetNewVersionAvailable();
            }
        }

#if DEBUG
        private static void ValidateShaders()
        {
            using var progressDialog = new GenericProgressForm
            {
                Text = "Compiling shaders…"
            };
            progressDialog.OnProcess += (_, __) =>
            {
                using var window = new OpenTK.Windowing.Desktop.NativeWindow(new()
                {
                    APIVersion = ValveResourceFormat.Renderer.GLEnvironment.RequiredVersion,
                    Flags = GLBaseControl.Flags | OpenTK.Windowing.Common.ContextFlags.Offscreen,
                    StartVisible = false,
                    Title = "Source 2 Viewer Shader Validator"
                });

                window.MakeCurrent();

                ValveResourceFormat.Renderer.Shaders.ShaderLoader.ValidateShaders(new Progress<string>(progressDialog.SetProgress), VrfGuiContext.Logger);
            };
            progressDialog.ShowDialog();
        }
#endif

    }
}
