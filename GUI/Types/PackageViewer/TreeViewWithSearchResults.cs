using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Types.PackageViewer.ThumbnailRenderers;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat.IO;
using Windows.Win32;

namespace GUI.Types.PackageViewer
{
    /// <summary>
    /// Represents a user control in which a TreeView and ListView are used to view a directory/file listing. In addition to a normal TreeView,
    /// this control allows for searching to occur within the TreeView and have the results displayed in a ListView with details about the resulting
    /// items.
    /// </summary>
    partial class TreeViewWithSearchResults : UserControl
    {
        /// <summary>
        /// Shows the steps of <see cref="Package.Verify"/> in the dialog, without the thread pool hop of <see cref="Progress{T}"/>.
        /// </summary>
        private sealed class VerifyProgress(GenericProgressForm dialog) : IProgress<(int Step, int StepCount, string Message)>
        {
            public void Report((int Step, int StepCount, string Message) value)
            {
                if (value.Step == 1)
                {
                    dialog.SetBarMax(value.StepCount);
                }

                dialog.SetBarValue(value.Step);
                dialog.SetProgress(value.Message);
            }
        }

        ThumbnailSizes CurrentThumbnailSizes { get; set; } = ThumbnailSizes.Medium;

        private readonly List<ListViewItem> ListViewItems = [];

        private ImageList BigIconsImageList;

        // reserved slots in ImageList, first two slots will always be these icons, after that normal cache continues
        private const int ImageIndexFolder = 0;
        private const int ImageIndexFolderUp = 1;

        private readonly Lock ImageListLock = new();

        private record class IconImageCacheEntry(Bitmap image, int index);

        private readonly ConcurrentDictionary<string, IconImageCacheEntry?> BigIconImageCache = new();

        private CancellationTokenSource? ThumbnailRenderTokenSource;

        // Populated in OnLoad, after the listview's font and DPI context settle, since the native
        // control caches label measurements. Earlier, while un-parented, labels are measured against
        // a stale font and stay truncated until the layout is invalidated.
        private VirtualPackageNode? PendingInitialDisplayNode;

        private readonly ConcurrentDictionary<PackageEntry, byte> QueuedOrRenderedThumbnailItems = new();

#pragma warning disable CA2213 // Disposed once the render thread stopped
        private readonly CancellationTokenSource RenderLoopCancelationTokenSource = new();
        private readonly BlockingCollection<Func<Task>> ThumbnailRenderQueue = [];
#pragma warning restore CA2213
        private readonly Thread ThumbnailRenderThread;
        private readonly TaskCompletionSource ThumbnailRenderStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static readonly string[] Columns = ["Name", "Size", "Type"];

        private static int SplitterWidth;

        public bool DeletedFilesRecovered { get; private set; }
        public PackageViewer Viewer { get; }

        public CancellationTokenSource? PreviewTokenSource { get; private set; }

        private Dictionary<string, Dictionary<string, string>>? AssetSearchData;
        private Dictionary<string, SortedSet<string>>? SearchDataKeys;
        private bool SearchDataBuilt;

        public event EventHandler<PackageEntry>? OpenPackageEntry;
        public event EventHandler<PackageContextMenuEventArgs>? OpenContextMenu;
        public event EventHandler<PackageEntry>? PreviewFile;

        // Raised when the file list (a folder) is shown, i.e. no file is being previewed anymore.
        public event EventHandler? PreviewCleared;
        public event EventHandler<TabPage>? PreviewFocused;
        public event EventHandler? PreviewBlurred;

        private readonly NavigationHistory navigationHistory = new();
        private bool suppressHistoryRecording;

        /// <summary>
        /// Folder shown in the list view, or <see langword="null"/> while it shows search results.
        /// </summary>
        private VirtualPackageNode? DisplayedFolder;

        /// <summary>
        /// Root folder of the recovered deleted files.
        /// </summary>
        private VirtualPackageNode? DeletedFilesRoot;

        /// <summary>
        /// Initializes a new instance of the <see cref="TreeViewWithSearchResults"/> class.
        /// </summary>
        public TreeViewWithSearchResults(PackageViewer viewer)
        {
            InitializeComponent();

            foreach (Control control in Controls)
            {
                Themer.ThemeControl(control);
            }

            ThumbnailRenderThread = new Thread(RenderLoop)
            {
                IsBackground = true,
                Name = "Thumbnail GL Thread"
            };

            ThumbnailRenderThread.Start();

            searchTextBox.BackColor = Themer.CurrentThemeColors.AppMiddle;

            backButton.Image = AppIcons.ImageList.Images[AppIcons.Icons["NavigateBack"]];
            forwardButton.Image = AppIcons.ImageList.Images[AppIcons.Icons["NavigateForward"]];

            if (SplitterWidth > 0)
            {
                mainSplitContainer.SplitterDistance = SplitterWidth;
            }

            mainListView.VirtualMode = true;
            mainListView.VirtualItems = ListViewItems;
            mainListView.RetrieveVirtualItem += MainListView_RetrieveVirtualItem;

            Dock = DockStyle.Fill;

            mainListView.MouseDoubleClick += MainListView_MouseDoubleClick;
            mainListView.MouseDown += MainListView_MouseDown;
            mainListView.MouseWheel += MainListView_MouseWheel;
            mainListView.ColumnClick += MainListView_ColumnClick;
            mainListView.Disposed += MainListView_Disposed;
            mainListView.FullRowSelect = true;
            mainListView.ListViewItemSorter = new ListViewColumnSorter();

            mainTreeView.MouseWheel += MainListView_MouseWheel;
            MouseWheel += MainListView_MouseWheel;

            mainTreeView.HideSelection = false;
            mainTreeView.NodeMouseDoubleClick += MainTreeView_NodeMouseDoubleClick;
            mainTreeView.NodeMouseClick += MainTreeView_NodeMouseClick;
            mainTreeView.AfterSelect += MainTreeView_AfterSelect;

            gridSizeSlider.Value = Settings.Config.PackageGridSize;
            CurrentThumbnailSizes = Enum.GetValues<ThumbnailSizes>()[Settings.Config.PackageGridSize];

            if (Settings.Config.PackageGridView == 0)
            {
                listRadioButton.Checked = true;
            }
            else
            {
                mainListView.View = View.LargeIcon;
            }

            BigIconsImageList = InitThumbnailImageList();
            mainListView.LargeImageList = BigIconsImageList;

            mainListView.Resize += MainListView_Resize;
            mainListView.Scroll += MainListView_Scroll;

            Viewer = viewer;
        }

        private void MainListView_Scroll(object? sender, ScrollEventArgs e)
        {
            if (mainListView.View == View.LargeIcon && ThumbnailRenderTokenSource != null)
            {
                _ = UpdateLargeImageListIconsAsync(ThumbnailRenderTokenSource.Token);
            }
        }

        private void MainListView_Resize(object? sender, EventArgs e)
        {
            if (mainListView.View == View.LargeIcon && ThumbnailRenderTokenSource != null)
            {
                _ = UpdateLargeImageListIconsAsync(ThumbnailRenderTokenSource.Token);
            }
        }

        private void MainListView_MouseWheel(object? sender, MouseEventArgs e)
        {
            if (mainListView.View != View.LargeIcon || (Control.ModifierKeys & Keys.Control) != Keys.Control)
            {
                return;
            }

            if (e.Delta > 0 && gridSizeSlider.Value < gridSizeSlider.Maximum)
            {
                gridSizeSlider.Value++;
                gridSizeSlider_Scroll(gridSizeSlider, EventArgs.Empty);
            }
            else if (e.Delta < 0 && gridSizeSlider.Value > gridSizeSlider.Minimum)
            {
                gridSizeSlider.Value--;
                gridSizeSlider_Scroll(gridSizeSlider, EventArgs.Empty);
            }
        }

        private void RenderLoop()
        {
            try
            {
                foreach (var work in ThumbnailRenderQueue.GetConsumingEnumerable(RenderLoopCancelationTokenSource.Token))
                {
                    work().GetAwaiter().GetResult();
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                foreach (var renderer in ThumbnailRenderers.Values)
                {
                    renderer.Dispose();
                }

                ThumbnailRenderStopped.SetResult();
            }
        }

        private void RestartThumbnailRendering()
        {
            ThumbnailRenderTokenSource?.Cancel();
            ThumbnailRenderTokenSource?.Dispose();
            ThumbnailRenderTokenSource = new CancellationTokenSource();
        }

        private void MainListView_RetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
        {
            e.Item = ListViewItems[e.ItemIndex];
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            Themer.ThemeControl(this);

            mainTreeView.BackColor = Themer.CurrentThemeColors.AppMiddle;
            mainListView.BackColor = Themer.CurrentThemeColors.AppSoft;
            mainSplitContainer.BackColor = Themer.CurrentThemeColors.AppMiddle;
            tableLayoutPanel2.BackColor = Themer.CurrentThemeColors.AppSoft;
            gridSizeSlider.BackColor = Themer.CurrentThemeColors.AppSoft;

            Themer.ThemeControl(gridRadioButton);
            Themer.ThemeControl(listRadioButton);
            Themer.ThemeControl(searchSummaryLabel);
        }

        private void MainSplitContainerSplitterMoved(object sender, SplitterEventArgs e)
        {
            SplitterWidth = e.SplitX;
        }

        private void MainTreeView_NodeMouseDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node is not BetterTreeNode node || node.IsFolder)
            {
                return;
            }

            // When expanding a folder by double click, the list will automatically scroll to fit
            // maximum amount of child nodes on screen, but this also triggers a double click on a node
            // that is now in place after the list scrolled, causing it to open an incorrect file
            if (node != mainTreeView.SelectedNode)
            {
                return;
            }

            PreviewTokenSource?.Cancel();

            if (node.PackageEntry != null)
            {
                OpenPackageEntry?.Invoke(sender, node.PackageEntry);
            }

            DisplayMainListView();
        }

        private void MainTreeView_AfterSelect(object? sender, TreeViewEventArgs e)
        {
            if (e.Action == TreeViewAction.Unknown || e.Node == null)
            {
                return;
            }

            if (PreviewTokenSource is not null)
            {
                PreviewTokenSource.Cancel();
                PreviewTokenSource.Dispose();
            }

            PreviewTokenSource = new CancellationTokenSource();
            var token = PreviewTokenSource.Token;

            var realNode = (BetterTreeNode)e.Node;

            // if user selected a folder, show the contents of that folder in the list view
            if (realNode.IsFolder)
            {
                DisplayMainListView();

                if (realNode.PkgNode != null)
                {
                    MainListView_DisplayNodes(realNode.PkgNode);
                }

                return;
            }

            if (realNode.PackageEntry != null)
            {
                // Blank the list view on a mouse click to minimize flashing, unless the next file is the same
                // type as the one shown, which stays as the background.
                if (CanQuickPreviewFile(realNode.PackageEntry) && realNode.PackageEntry.TypeName != currentPreviewType)
                {
                    ShowPreviewPlaceholder();
                }

                Task.Run(async () =>
                {
                    // The default double-click time in windows (500) is too long to wait entirely.
                    await Task.Delay(200, token).ConfigureAwait(false);
                    await InvokeAsync(() => PreviewFile?.Invoke(sender, realNode.PackageEntry), token).ConfigureAwait(false);
                }, token);
            }
        }

        private void MainListView_DisplayNodes(VirtualPackageNode pkgNode, bool updatePath = true)
        {
            RestartThumbnailRendering();
            DrainThumbnailQueue();

            ListViewItems.Clear();

            DisplayedFolder = pkgNode.Parent != null || pkgNode == mainTreeView.Root ? pkgNode : null;

            if (pkgNode.Parent != null)
            {
                AddParentNavigationItemToListView(pkgNode.Parent);
            }

            foreach (var (name, node) in pkgNode.Folders)
            {
                AddFolderToListView(name, node);
            }

            foreach (var file in pkgNode.Files)
            {
                AddFileToListView(file);
            }

            mainListView.BeginUpdate();
            mainListView.VirtualListSize = ListViewItems.Count;

            AssignIcons();

            if (ListViewItems.Count > 0)
            {
                mainListView.EnsureVisible(0);
            }

            mainListView.EndUpdate();

            ShowSearchResultsState(DisplayedFolder == null);

            if (updatePath)
            {
                UpdateSearchTextBoxToCurrentPath(pkgNode);
            }

            RecordNavigation(new FolderNavigationEntry(pkgNode));
        }

        private void ShowSearchResultsState(bool show)
        {
            searchSummaryLabel.Text = null;

            if (show == (mainListView.Columns.Count > Columns.Length))
            {
                return;
            }

            if (show)
            {
                mainListView.Columns.Add("Folder");
            }
            else
            {
                mainListView.Columns.RemoveAt(Columns.Length);

                if (mainListView.ListViewItemSorter is ListViewColumnSorter sorter && sorter.SortColumn >= Columns.Length)
                {
                    sorter.SortColumn = 0;
                }
            }

            mainListView.SecondFlexibleColumn = show ? Columns.Length : -1;
            mainListView.AdjustColumnWidths();
        }

        private void RecordNavigation(NavigationEntry entry)
        {
            // Opening a folder can reach DisplayNodes more than once (e.g. list double click both
            // selects the tree node and calls DisplayNodes), and replaying history must not re-record.
            if (suppressHistoryRecording)
            {
                return;
            }

            navigationHistory.Record(entry);
            UpdateNavigationButtons();
        }

        private void BackButton_Click(object? sender, EventArgs e) => NavigateBack();

        private void ForwardButton_Click(object? sender, EventArgs e) => NavigateForward();

        private const int APPCOMMAND_BROWSER_BACKWARD = 1;
        private const int APPCOMMAND_BROWSER_FORWARD = 2;
        private const int FAPPCOMMAND_MASK = 0xF000;

        /// <summary>
        /// Handles the back/forward mouse buttons and keyboard keys. WM_APPCOMMAND bubbles up from
        /// whichever child control was clicked or focused, so this works anywhere over the package viewer.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == PInvoke.WM_APPCOMMAND)
            {
                var cmd = (int)((ushort)((long)m.LParam >> 16) & ~FAPPCOMMAND_MASK);

                if (cmd is APPCOMMAND_BROWSER_BACKWARD or APPCOMMAND_BROWSER_FORWARD)
                {
                    if (cmd == APPCOMMAND_BROWSER_BACKWARD)
                    {
                        NavigateBack();
                    }
                    else
                    {
                        NavigateForward();
                    }

                    m.Result = 1;
                    return;
                }
            }

            base.WndProc(ref m);
        }

        private void NavigateBack()
        {
            if (navigationHistory.Back() is { } entry)
            {
                NavigateTo(entry);
            }
        }

        private void NavigateForward()
        {
            if (navigationHistory.Forward() is { } entry)
            {
                NavigateTo(entry);
            }
        }

        private void NavigateTo(NavigationEntry entry)
        {
            suppressHistoryRecording = true;

            try
            {
                if (entry is SearchNavigationEntry search)
                {
                    PerformSearch(search.Search);
                }
                else if (entry is FolderNavigationEntry { Node: var node })
                {
                    mainTreeView.BeginUpdate();
                    var treeNode = CreateTreeNodes(node);

                    if (treeNode != null)
                    {
                        treeNode.EnsureVisible();
                        treeNode.Expand();
                        mainTreeView.SelectedNode = treeNode;
                    }

                    mainTreeView.EndUpdate();

                    DisplayMainListView();
                    MainListView_DisplayNodes(node);
                }
            }
            finally
            {
                suppressHistoryRecording = false;
                UpdateNavigationButtons();
            }
        }

        private void UpdateNavigationButtons()
        {
            backButton.Enabled = navigationHistory.CanGoBack;
            forwardButton.Enabled = navigationHistory.CanGoForward;
        }

        /// <summary>
        /// Finds the folder for a directory of a package entry, see <see cref="PackageEntry.GetDirectoryPath"/>.
        /// </summary>
        internal VirtualPackageNode? FindFolderNode(string directoryPath) => mainTreeView.Root is { } root ? FindFolderNode(root, directoryPath) : null;

        /// <summary>
        /// Finds the folder that holds a file, in the package or in the recovered deleted files.
        /// </summary>
        private VirtualPackageNode? FindFolderNodeOfFile(PackageEntry entry)
        {
            foreach (var root in (ReadOnlySpan<VirtualPackageNode?>)[mainTreeView.Root, DeletedFilesRoot])
            {
                if (root != null && FindFolderNode(root, entry.GetDirectoryPath()) is { } folder && folder.Files.Contains(entry))
                {
                    return folder;
                }
            }

            return null;
        }

        private static VirtualPackageNode? FindFolderNode(VirtualPackageNode root, string directoryPath)
        {
            var node = root;

            if (directoryPath.Length == 0)
            {
                return node;
            }

            var directory = directoryPath.AsSpan();

            foreach (var subPathRange in directory.Split([Package.DirectorySeparatorChar]))
            {
                if (!node.Folders.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(directory[subPathRange], out var subNode))
                {
                    return null;
                }

                node = subNode;
            }

            return node;
        }

        /// <summary>
        /// Returns all of the files in a folder and its subfolders.
        /// </summary>
        internal static IEnumerable<PackageEntry> EnumerateFiles(VirtualPackageNode folder)
        {
            foreach (var file in folder.Files)
            {
                yield return file;
            }

            foreach (var subFolder in folder.Folders.Values)
            {
                foreach (var file in EnumerateFiles(subFolder))
                {
                    yield return file;
                }
            }
        }

        /// <summary>
        /// Removes the node of a file that was removed from the package.
        /// </summary>
        internal void RemoveFileNode(PackageEntry entry) => RemoveFileNode(entry, entry.GetDirectoryPath(), entry.GetFullPath());

        /// <summary>
        /// Removes the node of a file that had the given directory and path before it was moved.
        /// </summary>
        internal void RemoveFileNode(PackageEntry entry, string directoryPath, string path)
        {
            var folder = FindFolderNode(directoryPath);

            if (folder == null || !folder.Files.Remove(entry))
            {
                return;
            }

            SubtractFolderSize(folder, entry.TotalLength);
            BigIconImageCache.TryRemove(path, out _);

            if (folder.CreatedNode == null)
            {
                return;
            }

            foreach (BetterTreeNode node in folder.CreatedNode.Nodes)
            {
                if (node.PackageEntry == entry)
                {
                    node.Remove();
                    break;
                }
            }
        }

        /// <summary>
        /// Removes the node of a folder whose files were removed from the package.
        /// </summary>
        internal void RemoveFolderNode(VirtualPackageNode folder) => RemoveFolderNode(folder, EnumerateFiles(folder).Select(static file => file.GetFullPath()));

        /// <summary>
        /// Removes the node of a folder whose files had the given paths before they were moved.
        /// </summary>
        internal void RemoveFolderNode(VirtualPackageNode folder, IEnumerable<string> filePaths)
        {
            if (folder.Parent == null || !folder.Parent.Folders.Remove(folder.Name))
            {
                return;
            }

            SubtractFolderSize(folder.Parent, folder.TotalSize);

            foreach (var path in filePaths)
            {
                BigIconImageCache.TryRemove(path, out _);
            }

            FindFolderTreeNode(folder.Parent.CreatedNode, folder)?.Remove();

            navigationHistory.RemoveSubtree(folder);
            UpdateNavigationButtons();
        }

        private static BetterTreeNode? FindFolderTreeNode(BetterTreeNode? parentTreeNode, VirtualPackageNode folder)
        {
            if (parentTreeNode == null)
            {
                return null;
            }

            foreach (BetterTreeNode treeNode in parentTreeNode.Nodes)
            {
                if (treeNode.PkgNode == folder)
                {
                    return treeNode;
                }
            }

            return null;
        }

        private static bool IsInPackage(VirtualPackageNode folder)
        {
            for (var node = folder; node.Parent != null; node = node.Parent)
            {
                if (!node.Parent.Folders.TryGetValue(node.Name, out var current) || current != node)
                {
                    return false;
                }
            }

            return true;
        }

        private static void SubtractFolderSize(VirtualPackageNode folder, long size)
        {
            for (var node = folder; node != null; node = node.Parent)
            {
                node.TotalSize -= size;
            }
        }

        /// <summary>
        /// Shows the current contents of the folder in the list view after files were added or removed,
        /// or its closest remaining parent when it was removed. Search results lose the removed files and show the current names of the others.
        /// </summary>
        internal void RefreshListView(IReadOnlySet<PackageEntry>? removedFiles = null)
        {
            if (DisplayedFolder == null)
            {
                ListViewItems.RemoveAll(item => item is BetterListViewItem { PackageEntry: { } entry } && removedFiles?.Contains(entry) == true);

                foreach (var item in ListViewItems)
                {
                    if (item is BetterListViewItem { PackageEntry: { } entry })
                    {
                        item.Text = entry.GetFileName();
                        item.SubItems[2].Text = entry.TypeName;
                        item.SubItems[3].Text = entry.GetDirectoryPath();
                    }
                }

                mainListView.VirtualListSize = ListViewItems.Count;
                AssignIcons();
                mainListView.Invalidate();
                return;
            }

            var folder = DisplayedFolder;

            while (folder.Parent != null && !IsInPackage(folder))
            {
                folder = folder.Parent;
            }

            suppressHistoryRecording = true;

            try
            {
                MainListView_DisplayNodes(folder);
            }
            finally
            {
                suppressHistoryRecording = false;
            }
        }

        private void AssignIcons()
        {
            SortListViewItemsForVirtualMode();

            if (mainListView.View == View.LargeIcon)
            {
                AssignBigIconIndicesAndRenderThumbnails();
                return;
            }

            AssignSmallIconIndices();
        }

        private readonly Dictionary<string, ThumbnailRenderer> ThumbnailRenderers = new()
        {
            {"vmdl_c", new ThumbnailModelRenderer() },
            {"vmat_c", new ThumbnailMaterialRenderer() },
            {"vtex_c", new ThumbnailTextureRenderer() },
            {"vsvg_c", new ThumbnailSVGRenderer() },
            {"vpcf_c", new ThumbnailParticleRenderer() },
            {"vsnap_c", new ThumbnailSnapshotRenderer() },
        };

        /// <summary>
        /// Drains pending work from <see cref="ThumbnailRenderQueue"/>, which the render thread consumes,
        /// and clears <see cref="QueuedOrRenderedThumbnailItems"/>, which prevents duplicate queueing.
        /// Clearing only the dictionary would allow re-queueing, while the old lambdas would still be
        /// ahead of new work.
        /// </summary>
        private void DrainThumbnailQueue()
        {
            while (ThumbnailRenderQueue.TryTake(out _))
            {
            }

            QueuedOrRenderedThumbnailItems.Clear();
        }

        private ThumbnailRenderer? GetThumbnailRenderer(string resourceType)
        {
            if (!ThumbnailRenderers.TryGetValue(resourceType, out var renderer))
            {
                return null;
            }

            if (!renderer.Loaded)
            {
                renderer.Load(mainListView.VrfGuiContext!);
            }

            return renderer;
        }

        private async Task UpdateLargeImageListIconsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var (first, last) = GetVisibleItemRange();

                if (first == -1)
                {
                    return;
                }

                DrainThumbnailQueue();

                for (var i = first; i <= last; i++)
                {
                    if (ListViewItems[i] is not BetterListViewItem castItem)
                    {
                        continue;
                    }

                    var entry = castItem.PackageEntry;

                    if (entry == null)
                    {
                        continue;
                    }

                    // Already has a rendered thumbnail cached, no need to queue
                    if (BigIconImageCache.ContainsKey(entry.GetFullPath()))
                    {
                        continue;
                    }

                    if (!QueuedOrRenderedThumbnailItems.TryAdd(entry, 0))
                    {
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        ThumbnailRenderQueue.Add(async () =>
                        {
                            var context = mainListView?.VrfGuiContext;
                            if (context == null)
                            {
                                return;
                            }

                            var renderer = GetThumbnailRenderer(entry.TypeName);

                            if (renderer == null)
                            {
                                return; // unsupported type, keep marked so we don't retry
                            }

                            var entryKey = entry.GetFullPath();

                            BigIconImageCache.TryGetValue(entryKey, out var cachedEntry);

                            var imageIndex = -1;
                            Bitmap? bitmap = null;

                            if (cachedEntry != null)
                            {
                                imageIndex = cachedEntry.index;
                            }
                            else
                            {
                                try
                                {
                                    bitmap = renderer.Render(entry, context, CurrentThumbnailSizes, cancellationToken);
                                }
                                catch (Exception ex) when (ex is not OperationCanceledException)
                                {
                                    Log.Error(nameof(TreeViewWithSearchResults), $"Failed to render thumbnail for \"{entry.GetFullPath()}\" in \"{context.DisplayPath}\": {ex.Message}");
                                    return;
                                }

                                if (bitmap == null)
                                {
                                    return; // unrenderable, keep marked so we don't retry
                                }
                            }

                            var listView = mainListView;
                            if (listView == null || listView.IsDisposed)
                            {
                                return;
                            }

                            try
                            {
                                await listView.InvokeAsync(() =>
                                {
                                    if (listView.IsDisposed || listView.View != View.LargeIcon || !ListViewItems.Contains(castItem))
                                    {
                                        bitmap?.Dispose();
                                        return;
                                    }

                                    if (imageIndex != -1)
                                    {
                                        castItem.ImageIndex = imageIndex;
                                    }
                                    else if (bitmap != null)
                                    {
                                        lock (ImageListLock)
                                        {
                                            // double-checked, another InvokeAsync callback may have stored this entry while we were waiting to be marshalled
                                            if (BigIconImageCache.TryGetValue(entryKey, out var existing) && existing != null)
                                            {
                                                castItem.ImageIndex = existing.index;
                                            }
                                            else
                                            {
                                                castItem.ImageIndex = BigIconsImageList.Images.Count;
                                                BigIconsImageList.Images.Add(bitmap);
                                                BigIconImageCache[entryKey] = new(bitmap, castItem.ImageIndex);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        return;
                                    }

                                    listView.Invalidate();

                                }, cancellationToken).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                bitmap?.Dispose();
                            }
                        }, cancellationToken);

                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Log.Error(nameof(TreeViewWithSearchResults), $"Failed to render thumbnail for {entry.GetFullPath()}: {ex}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // user navigated away, silently discard
            }
        }

        private static string ResolveExtension(string typeName)
        {
            if (AppIcons.ExtensionSVGS.ContainsKey(typeName))
            {
                return typeName;
            }

            var ext = typeName;

            if (ext.EndsWith("_c", StringComparison.Ordinal))
            {
                ext = ext[..^2];
            }

            if (AppIcons.ExtensionSVGS.ContainsKey(ext))
            {
                return ext;
            }

            if (ext.StartsWith('v'))
            {
                ext = ext[1..];
            }

            if (AppIcons.ExtensionSVGS.ContainsKey(ext))
            {
                return ext;
            }

            return "File";
        }

        /// <summary>
        /// Renders the file-type SVG icon at the given <paramref name="size"/>; this is the same icon the grid view falls back to when a
        /// file has no rendered thumbnail.
        /// </summary>
        internal static Bitmap GetTypeIconBitmap(string typeName, int size)
        {
            var svg = AppIcons.ExtensionSVGS.GetValueOrDefault(ResolveExtension(typeName))
                ?? AppIcons.ExtensionSVGS.GetValueOrDefault("File");
            return Themer.SvgToBitmap(svg!, size, size);
        }

        private ImageList InitThumbnailImageList()
        {
            var currentThumbnailSize = (int)CurrentThumbnailSizes;
            var bigIconsImageList = new ImageList
            {
                ImageSize = new Size(currentThumbnailSize, currentThumbnailSize),
                ColorDepth = ColorDepth.Depth32Bit
            };

            AppIcons.ExtensionSVGS.TryGetValue("Folder", out var folderSvgFile);
            AppIcons.ExtensionSVGS.TryGetValue("FolderUp", out var folderUpSvgFile);
            var folderBitmap = Themer.SvgToBitmap(folderSvgFile!, currentThumbnailSize, currentThumbnailSize);
            var folderUpBitmap = Themer.SvgToBitmap(folderUpSvgFile!, currentThumbnailSize, currentThumbnailSize);
            bigIconsImageList.Images.Add(folderBitmap);
            bigIconsImageList.Images.Add(folderUpBitmap);

            return bigIconsImageList;
        }

        private (int first, int last) GetVisibleItemRange()
        {
            var count = mainListView.VirtualListSize;

            if (count == 0)
            {
                return (-1, -1);
            }

            var visible = mainListView.ClientRectangle;

            // Binary search for the first visible item
            var lo = 0;
            var hi = count - 1;
            var first = -1;

            while (lo <= hi)
            {
                var mid = lo + (hi - lo) / 2;
                var rect = mainListView.GetItemRect(mid);

                if (rect.Bottom <= visible.Top)
                {
                    lo = mid + 1;
                }
                else if (rect.Top >= visible.Bottom)
                {
                    hi = mid - 1;
                }
                else
                {
                    first = mid;
                    hi = mid - 1; // keep searching left for the first one
                }
            }

            if (first == -1)
            {
                return (-1, -1);
            }

            // Linear scan forward for the last visible item
            var last = first;

            for (var i = first + 1; i < count; i++)
            {
                var rect = mainListView.GetItemRect(i);

                if (!rect.IntersectsWith(visible))
                {
                    break;
                }

                last = i;
            }

            return (first, last);
        }

        private void MainTreeView_NodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Right && e.Node is BetterTreeNode node)
            {
                mainTreeView.SelectedNode = e.Node;

                if (node.PackageEntry != null)
                {
                    OpenContextMenu?.Invoke(sender, new PackageContextMenuEventArgs
                    {
                        Location = e.Location,
                        PackageEntry = node.PackageEntry,
                        TreeNode = (BetterTreeNode)e.Node
                    });
                }
                else if (node.PkgNode != null)
                {
                    OpenContextMenu?.Invoke(sender, new PackageContextMenuEventArgs
                    {
                        Location = e.Location,
                        PkgNode = node.PkgNode,
                        TreeNode = (BetterTreeNode)e.Node
                    });
                }
            }
        }

        internal void BeginUpdate()
        {
            mainTreeView.BeginUpdate();
        }

        internal void EndUpdate()
        {
            mainTreeView.EndUpdate();
        }

        /// <summary>
        /// Initializes the TreeView in the control with the contents of the passed Package. Contents are sorted and expanded by default.
        /// </summary>
        internal void InitializeTreeViewFromPackage(VrfGuiContext vrfGuiContext, VirtualPackageNode rootVirtual)
        {
            mainListView.VrfGuiContext = vrfGuiContext;
            mainTreeView.Root = rootVirtual;

            var control = mainTreeView;
            control.BeginUpdate();
            control.PathSeparator = Package.DirectorySeparatorChar.ToString();
            control.Name = "treeViewVpk";
            control.VrfGuiContext = vrfGuiContext;
            control.Dock = DockStyle.Fill;
            control.ImageList = AppIcons.ImageList;
            control.BeforeExpand += Control_BeforeExpand;
            control.ShowRootLines = false;

            if (vrfGuiContext.CurrentPackage?.Entries != null)
            {
                control.GenerateIconList([.. vrfGuiContext.CurrentPackage.Entries.Keys]);
            }

            if (!Viewer.HasChunkFiles)
            {
                // Disable recover deleted files button for packages without chunk files
                DeletedFilesRecovered = true;
            }

            var fullFilePath = vrfGuiContext.FileName.AsSpan();
            var fileName = Path.GetFileName(fullFilePath);
            var parentFolder = Path.GetFileName(Path.GetDirectoryName(fullFilePath));
            var name = parentFolder.Length > 0 ? $"{parentFolder}/{fileName}" : fileName.ToString();
            var vpkImage = AppIcons.ExtensionIcons["vpk"];

            var root = new BetterTreeNode(name, rootVirtual)
            {
                Name = "root",
                ImageIndex = vpkImage,
                SelectedImageIndex = vpkImage,
            };
            control.Nodes.Add(root);
            control.SelectedNode = root;

            CreateNodes(root);
            root.Expand();

            control.TreeViewNodeSorter = new TreeViewFileSorter();
            control.EndUpdate();

            // Defer until OnLoad, see comment on PendingInitialDisplayNode.
            PendingInitialDisplayNode = rootVirtual;
        }

        private void Control_BeforeExpand(object? sender, TreeViewCancelEventArgs e)
        {
            if (e.Node == null)
            {
                return;
            }

            mainTreeView.BeginUpdate();

            var node = (BetterTreeNode)e.Node;
            CreateNodes(node);

            // If the folder we just expanded contains a single folder, expand it too.
            if (node.Nodes.Count == 1 && node.FirstNode != null)
            {
                node = (BetterTreeNode)node.FirstNode;

                if (node.PkgNode != null && !node.IsExpanded)
                {
                    node.Expand();
                }
            }

            mainTreeView.EndUpdate();
        }

        private void CreateNodes(BetterTreeNode realNode)
        {
            var currentNode = realNode.PkgNode;

            if (currentNode == null || currentNode.CreatedNode != null)
            {
                return;
            }

            mainTreeView.BeginUpdate();
            realNode.Nodes.Clear();

            foreach (var node in currentNode.Folders)
            {
                var toAdd = new BetterTreeNode(node.Key, node.Value)
                {
                    ImageIndex = mainTreeView.FolderImage,
                    SelectedImageIndex = mainTreeView.FolderImage,
                };

                if (node.Value.Files.Count > 0 || node.Value.Folders.Count > 0)
                {
                    toAdd.Nodes.Add(new BetterTreeNode(string.Empty, node.Value));
                }

                realNode.Nodes.Add(toAdd);
            }

            foreach (var file in currentNode.Files)
            {
                CreateFileNode(realNode, file);
            }

            currentNode.CreatedNode = realNode;
            mainTreeView.EndUpdate();
        }

        private void CreateFileNode(BetterTreeNode realNode, PackageEntry file, bool isCreating = false)
        {
            var fileName = file.GetFileName();
            int image;

            if (isCreating)
            {
                image = AppIcons.GetImageIndexForExtension(file.TypeName.ToLowerInvariant());
            }
            else if (!mainTreeView.ExtensionIconList.TryGetValue(file.TypeName, out image))
            {
                image = AppIcons.Icons["File"];
            }

            var newNode = new BetterTreeNode(fileName, file)
            {
                ImageIndex = image,
                SelectedImageIndex = image,
            };

            realNode.Nodes.Add(newNode);
        }

        private BetterTreeNode? CreateTreeNodes(VirtualPackageNode node, bool isCreating = false)
        {
            var originalNode = node;
            var queue = new Stack<VirtualPackageNode>();

            while (node.Parent != null && node.CreatedNode == null)
            {
                queue.Push(node);
                node = node.Parent;
            }

            while (queue.TryPop(out var currentNode))
            {
                if (currentNode.Parent?.CreatedNode == null)
                {
                    continue;
                }

                var parentTreeNode = currentNode.Parent.CreatedNode;
                var treeNode = FindFolderTreeNode(parentTreeNode, currentNode);

                if (treeNode == null && isCreating)
                {
                    treeNode = new BetterTreeNode(currentNode.Name, currentNode)
                    {
                        ImageIndex = mainTreeView.FolderImage,
                        SelectedImageIndex = mainTreeView.FolderImage,
                    };
                    parentTreeNode.Nodes.Add(treeNode);
                }

                if (treeNode != null)
                {
                    CreateNodes(treeNode);
                }
            }

            return originalNode.CreatedNode;
        }

        internal void AddFolderNode(string directoryName)
        {
            var root = mainTreeView.Root;

            if (root == null)
            {
                return;
            }

            var node = BetterTreeView.AddFolderNode(root, directoryName, 0u);

            var createdNode = CreateTreeNodes(node, true);

            createdNode?.EnsureVisible();
        }

        internal void AddFileNode(PackageEntry file)
        {
            var root = mainTreeView.Root;

            if (root == null)
            {
                return;
            }

            mainTreeView.GenerateIconList([file.TypeName]);

            var node = BetterTreeView.AddFileNode(root, file);
            root.TotalSize += file.TotalLength;

            // Realizing a folder in CreateTreeNodes already creates nodes for all of its files,
            // so only add the file node when the folder was realized before this call.
            var folderWasCreated = node.CreatedNode != null;

            CreateTreeNodes(node, true);

            if (folderWasCreated && node.CreatedNode != null)
            {
                CreateFileNode(node.CreatedNode, file, true);
            }
        }

        internal void RecoverDeletedFiles()
        {
            DeletedFilesRecovered = true;

            if (mainTreeView.VrfGuiContext is not { CurrentPackage: { } currentPackage } vrfGuiContext)
            {
                return;
            }

            using var progressDialog = new GenericProgressForm
            {
                Text = "Scanning for deleted files…"
            };
            progressDialog.OnProcess = _ =>
            {
                progressDialog.SetProgress("Scanning for deleted files, this may take a while…");

                List<PackageEntry> foundFiles;

                try
                {
                    foundFiles = Types.PackageViewer.PackageViewer.RecoverDeletedFiles(currentPackage, progressDialog);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    ErrorReport.ShowError(e, vrfGuiContext.DisplayPath);
                    return Task.CompletedTask;
                }

                Invoke((MethodInvoker)(() =>
                {
                    var deletedImage = AppIcons.Icons["Recover"];

                    if (foundFiles.Count == 0)
                    {
                        var rootVirtualNone = new VirtualPackageNode("No deleted files found", 0, null);
                        mainTreeView.Nodes.Add(new BetterTreeNode(rootVirtualNone.Name, rootVirtualNone)
                        {
                            Name = "root_deleted",
                            ImageIndex = deletedImage,
                            SelectedImageIndex = deletedImage,
                        });
                        return;
                    }

                    mainTreeView.BeginUpdate();

                    var name = $"Deleted files ({foundFiles.Count} files found, names are guessed)";
                    var rootVirtual = new VirtualPackageNode(name, 0, null);
                    DeletedFilesRoot = rootVirtual;

                    foreach (var file in foundFiles)
                    {
                        BetterTreeView.AddFileNode(rootVirtual, file);
                    }

                    var root = new BetterTreeNode(rootVirtual.Name, rootVirtual)
                    {
                        Name = "root_deleted",
                        ImageIndex = deletedImage,
                        SelectedImageIndex = deletedImage,
                    };
                    mainTreeView.Nodes.Add(root);
                    CreateNodes(root);

                    root.Expand();
                    mainTreeView.SelectedNode = root;
                    DisplayMainListView();
                    MainListView_DisplayNodes(rootVirtual);
                    mainTreeView.EndUpdate();
                }));

                return Task.CompletedTask;
            };
            progressDialog.ShowDialog();
        }

        internal void VerifyPackageContents()
        {
            if (mainTreeView.VrfGuiContext is not { CurrentPackage: { Entries: not null } package } vrfGuiContext)
            {
                return;
            }

            using var progressDialog = new GenericProgressForm
            {
                Text = "Verifying package…"
            };
            progressDialog.OnProcess = cancellationToken =>
            {
                try
                {
                    progressDialog.SetProgress("Verifying package…");

                    package.Verify(new VerifyProgress(progressDialog), cancellationToken);

                    var message = package.IsSigned ? "Successfully verified package contents." : "Successfully verified package contents. This package is not signed.";

                    _ = AppMessageDialogs.ShowMessageAsync(message, "Verified package contents");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                catch (Exception e)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Log.Error(nameof(TreeViewWithSearchResults), $"Failed to verify \"{vrfGuiContext.DisplayPath}\": {e}");
                    }
                    else
                    {
                        ErrorReport.ShowError(e, vrfGuiContext.DisplayPath);
                    }
                }

                return Task.CompletedTask;
            };
            progressDialog.ShowDialog();
        }

        /// <summary>
        /// Performs a search and shows the matching files in the ListView, replacing what it showed before.
        /// </summary>
        internal void SearchAndFillResults(SearchRequest request)
        {
            suppressHistoryRecording = true;

            try
            {
                PerformSearch(request);
            }
            finally
            {
                suppressHistoryRecording = false;
            }

            RecordNavigation(new SearchNavigationEntry(request));
        }

        private void PerformSearch(SearchRequest request)
        {
            if (mainTreeView.VrfGuiContext?.CurrentPackage is not { } package || (request.Folder ?? mainTreeView.Root) is not { } folder)
            {
                return;
            }

            var candidates = EnumerateFiles(folder).Where(entry =>
                (request.Extension == null || entry.TypeName == request.Extension)
                && (request.FilterKey == null || AssetSearchData == null || MatchesAssetFilter(entry, request.FilterKey, request.FilterValue)));

            var query = request.Query;
            var (results, notSearched, cancelled) = Find(package, [.. candidates], query);

            var node = new VirtualPackageNode(string.Empty, 0, null);
            node.Files.AddRange(results);

            DisplayMainListView();
            MainListView_DisplayNodes(node, updatePath: false);

            var summary = new StringBuilder(query.Text.Length > 0 ? $"{SearchForm.SearchTypeNames[(int)query.Type]} \"{query.Text}\"" : "All files");

            if (request.Folder != null)
            {
                summary.Append(" in ").Append(PackageViewer.GetFolderPath(request.Folder));
            }

            if (request.Extension != null)
            {
                summary.Append(", ").Append(request.Extension);
            }

            if (request.FilterKey != null)
            {
                summary.Append(", ").Append(request.FilterKey).Append(" = ").Append(request.FilterValue ?? "any");
            }

            summary.Append(CultureInfo.InvariantCulture, $": {results.Count:N0} found");

            if (notSearched > 0)
            {
                summary.Append(CultureInfo.InvariantCulture, $", {notSearched:N0} not searched");
            }

            searchSummaryLabel.Text = cancelled ? summary.Append(", cancelled").ToString() : summary.ToString();
        }

        private static (List<PackageEntry> Matches, int NotSearched, bool Cancelled) Find(Package package, List<PackageEntry> candidates, PackageSearchQuery query)
        {
            if (query is not { Text.Length: > 0, Type: PackageSearchType.FileContents or PackageSearchType.FileContentsHex })
            {
                var (matches, notSearched) = PackageSearch.Find(package, candidates, query);
                return (matches, notSearched, false);
            }

            var result = (Matches: new List<PackageEntry>(), NotSearched: 0, Cancelled: false);

            using var progressDialog = new GenericProgressForm
            {
                Text = "Searching file contents\u2026",
                WaitForWorkToStop = true,
            };
            progressDialog.OnProcess = cancellationToken =>
            {
                progressDialog.SetProgress(string.Create(CultureInfo.InvariantCulture, $"Searching {candidates.Count:N0} files"));
                progressDialog.SetBarMax(candidates.Count);

                var (matches, notSearched) = PackageSearch.Find(package, candidates, query, progressDialog, cancellationToken);
                result = (matches, notSearched, cancellationToken.IsCancellationRequested);

                return Task.CompletedTask;
            };
            progressDialog.ShowDialog();

            return result;
        }

        private void MainListView_ColumnClick(object? sender, ColumnClickEventArgs e)
        {
            if (mainListView.ListViewItemSorter is not ListViewColumnSorter sorter)
            {
                return;
            }

            if (e.Column == sorter.SortColumn)
            {
                sorter.Order = sorter.Order == SortOrder.Ascending
                    ? SortOrder.Descending
                    : SortOrder.Ascending;
            }
            else
            {
                sorter.SortColumn = e.Column;
                sorter.Order = e.Column == 1 ? SortOrder.Descending : SortOrder.Ascending;
            }

            SortListViewItemsForVirtualMode();
        }

        /// <summary>
        /// When the user clicks in the ListView, check if the user clicks outside of a ListViewItem. If so, de-select any previously selected ListViewItems. In addition,
        /// if the user right clicked an item in the ListView, let our subscribers know what was clicked and where in case a context menu is needed to be shown.
        /// </summary>
        /// <param name="sender">Object which raised event.</param>
        /// <param name="e">Event data.</param>
        private void MainListView_MouseDown(object? sender, MouseEventArgs e)
        {
            // Back/forward mouse buttons navigate history (WM_APPCOMMAND); don't let them change selection here.
            if (e.Button is MouseButtons.XButton1 or MouseButtons.XButton2)
            {
                return;
            }

            var info = mainListView.HitTest(e.X, e.Y);

            // if an item was clicked in the list view
            if (info.Item is not IBetterBaseItem item)
            {
                mainListView.SelectedIndices.Clear();

                if (e.Button == MouseButtons.Right && Viewer.IsEditingPackage && DisplayedFolder != null)
                {
                    OpenContextMenu?.Invoke(sender, new PackageContextMenuEventArgs
                    {
                        Location = e.Location,
                        PkgNode = DisplayedFolder,
                        IsBackground = true,
                    });
                }

                return;
            }

            // When left or right clicking a folder, select it in the tree view and ensure it is visible
            if (item.PkgNode != null && mainListView.SelectedIndices.Count <= 1)
            {
                mainTreeView.BeginUpdate();
                var node = CreateTreeNodes(item.PkgNode);

                if (node != null)
                {
                    node.EnsureVisible();
                    mainTreeView.SelectedNode = node;
                }

                mainTreeView.EndUpdate();
            }

            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            if (item.PackageEntry != null)
            {
                // When right clicking a file, select it in the tree view and ensure it is visible
                if (mainListView.SelectedIndices.Count <= 1 && FindFolderNodeOfFile(item.PackageEntry) is { } pkgNode)
                {
                    mainTreeView.BeginUpdate();

                    // Create tree nodes for the deepest folder (this also creates all ancestor nodes)
                    var parentNode = CreateTreeNodes(pkgNode);

                    mainTreeView.EndUpdate();

                    // Select the file node after EndUpdate so EnsureVisible works correctly
                    if (parentNode != null)
                    {
                        foreach (BetterTreeNode node in parentNode.Nodes)
                        {
                            if (node.PackageEntry == item.PackageEntry)
                            {
                                node.EnsureVisible();
                                mainTreeView.SelectedNode = node;
                                break;
                            }
                        }
                    }
                }

                OpenContextMenu?.Invoke(sender, new PackageContextMenuEventArgs
                {
                    Location = e.Location,
                    PackageEntry = item.PackageEntry,
                });
            }
            else if (item.PkgNode != null)
            {
                OpenContextMenu?.Invoke(sender, new PackageContextMenuEventArgs
                {
                    Location = e.Location,
                    PkgNode = item.PkgNode,
                });
            }
        }

        /// <summary>
        /// If the user double clicks (with left mouse button) on a ListViewItem, send up an event to subscribers that such an action has occurred. Also send up
        /// whatever object is represented by the ListViewItem.
        /// </summary>
        /// <param name="sender">Object which raised event.</param>
        /// <param name="e">Event data.</param>
        private void MainListView_MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            var info = mainListView.HitTest(e.X, e.Y);

            if (info.Item is not IBetterBaseItem item)
            {
                mainListView.SelectedIndices.Clear();
                return;
            }

            // if user left double clicks a folder, open its contents and display in list view
            if (item.PkgNode != null)
            {
                mainTreeView.BeginUpdate();
                var node = CreateTreeNodes(item.PkgNode);

                if (node != null)
                {
                    node.Expand();
                    mainTreeView.SelectedNode = node;
                }

                mainTreeView.EndUpdate();

                MainListView_DisplayNodes(item.PkgNode);
            }
            else if (item.PackageEntry != null)
            {
                OpenPackageEntry?.Invoke(sender, item.PackageEntry);
            }
        }

        /// <summary>
        /// When the form loads, create the columns that we want to see such as name, file size, and file type.
        /// </summary>
        /// <param name="sender">Object which raised event.</param>
        /// <param name="e">Event data.</param>
        private void TreeViewWithSearchResults_Load(object sender, EventArgs e)
        {
            for (var i = 0; i < Columns.Length; i++)
            {
                mainListView.Columns.Add(Columns[i]);
            }

            if (PendingInitialDisplayNode != null)
            {
                var node = PendingInitialDisplayNode;
                PendingInitialDisplayNode = null;
                MainListView_DisplayNodes(node);
            }
        }

        private void AddParentNavigationItemToListView(VirtualPackageNode parentNode)
        {
            var name = parentNode.Parent == null ? ".." : $".. {parentNode.Name}";

            var item = new BetterListViewItem(name)
            {
                ImageIndex = GetFolderUpImageIndex(),
                PkgNode = parentNode,
                Tag = BetterListViewItem.ParentNavigationTag,
            };

            item.SubItems.Add(HumanReadableByteSizeFormatter.Format(parentNode.TotalSize));
            item.SubItems.Add(string.Empty);

            ListViewItems.Add(item);
        }

        private void AddFolderToListView(string name, VirtualPackageNode node)
        {
            var item = new BetterListViewItem(name)
            {
                ImageIndex = GetFolderImageIndex(),
                PkgNode = node,
            };

            item.SubItems.Add(HumanReadableByteSizeFormatter.Format(node.TotalSize));
            item.SubItems.Add(string.Empty);

            ListViewItems.Add(item);
        }

        private void AddFileToListView(PackageEntry file)
        {
            if (!mainTreeView.ExtensionIconList.TryGetValue(file.TypeName, out var image))
            {
                image = AppIcons.Icons["File"];
            }

            var item = new BetterListViewItem(file.GetFileName())
            {
                ImageIndex = image,
                PackageEntry = file,
            };

            item.SubItems.Add(HumanReadableByteSizeFormatter.Format(file.TotalLength));
            item.SubItems.Add(file.TypeName);
            item.SubItems.Add(file.GetDirectoryPath());

            ListViewItems.Add(item);
        }

        // The file type currently shown in the preview area, so a same-type preview can keep the previous view frozen
        // instead of flashing to a blank page. Null when the area shows a blank page or the list.
        private string? currentPreviewType;

        /// <returns>False when this view has already been torn down, in which case the tab is disposed.</returns>
        public bool ReplaceListViewWithControl(TabPage tab, string? typeName = null)
        {
            if (mainListView?.Parent is not { } parentControl)
            {
                tab.Dispose();
                return false;
            }

            mainListView.Visible = false;
            SetGridModeToolbarVisible(false);

            // A TabPage can only render inside a TabControl, so host it in one with the tab strip hidden. There is
            // no visible tab header; the file name is shown in the viewer's side control panel instead
            // (see RendererControl.AddPreviewFileName).
            var tabs = new ThemedTabControl
            {
                Dock = DockStyle.Fill,
                HideTabHeader = true,
            };
            tabs.Controls.Add(tab);
            parentControl.Controls.Add(tabs);

            tabs.Enter += PreviewControl_Enter;
            tabs.Leave += PreviewControl_Leave;

            currentPreviewType = typeName;

            foreach (Control old in parentControl.Controls)
            {
                if (old == tabs || old == mainListView)
                {
                    continue;
                }

                old.Dispose();
            }

            return true;
        }

        private void PreviewControl_Enter(object? sender, EventArgs e)
        {
            if (sender is TabControl { SelectedTab: { } tab })
            {
                PreviewFocused?.Invoke(this, tab);
            }
        }

        private void PreviewControl_Leave(object? sender, EventArgs e) => PreviewBlurred?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// Whether the file type is the one shown in the preview area, so its previous view can be kept
        /// while the next file of that type loads.
        /// </summary>
        public bool IsSamePreviewType(string? typeName) => typeName != null && typeName == currentPreviewType;

        /// <summary>
        /// Whether the entry gets a quick preview: quick preview must be enabled, and the file must not be
        /// vpk (to avoid nesting) or vmap_c.
        /// </summary>
        public static bool CanQuickPreviewFile(PackageEntry entry)
        {
            if (((Settings.QuickPreviewFlags)Settings.Config.QuickFilePreview & Settings.QuickPreviewFlags.Enabled) == 0)
            {
                return false;
            }

            // Not ideal to check by file extension, but do not nest vpk previews
            return entry.TypeName is not ("vpk" or "vmap_c");
        }

        /// <summary>
        /// Blanks the list view with an empty themed panel while the double-click debounce elapses, so a click
        /// gives instant feedback. The panel matches the loading panel background so the swap is seamless, and
        /// is disposed by <see cref="ReplaceListViewWithControl"/> or <see cref="DisplayMainListView"/>.
        /// </summary>
        private void ShowPreviewPlaceholder()
        {
            var parentControl = mainListView.Parent;

            if (parentControl == null)
            {
                return;
            }

            mainListView.Visible = false;
            SetGridModeToolbarVisible(false);

            var placeholder = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Themer.CurrentThemeColors.AppMiddle,
            };

            parentControl.Controls.Add(placeholder);

            currentPreviewType = null;

            foreach (Control old in parentControl.Controls)
            {
                if (old == placeholder || old == mainListView)
                {
                    continue;
                }

                old.Dispose();
            }
        }

        private void SetGridModeToolbarVisible(bool visible)
        {
            tableLayoutPanel2.Visible = visible;
            tableLayoutPanel1.RowStyles[0].Height = visible ? 40F : 0F;
        }

        private void DisplayMainListView()
        {
            if (mainListView.Parent == null)
            {
                return;
            }

            currentPreviewType = null;

            foreach (Control old in mainListView.Parent.Controls)
            {
                if (old != mainListView)
                {
                    old.Dispose();
                }
            }

            SetGridModeToolbarVisible(true);
            mainListView.Visible = true;

            PreviewCleared?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateSearchTextBoxToCurrentPath(VirtualPackageNode node)
        {
            var stack = new Stack<string>();

            do
            {
                if (node.Parent == null)
                {
                    break;
                }

                stack.Push(node.Name);

                node = node.Parent;
            }
            while (node != null);

            var sb = new StringBuilder();

            while (stack.TryPop(out var name))
            {
                sb.Append(name);
                sb.Append(Package.DirectorySeparatorChar);
            }

            searchTextBox.Text = sb.ToString();
        }

        private void OnSearchTextBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            var node = mainTreeView.Root;

            if (node == null)
            {
                return;
            }

            PackageEntry? fileToSelect = null;

            var inputPath = searchTextBox.Text
                .Replace(Path.DirectorySeparatorChar, Package.DirectorySeparatorChar)
                .AsSpan()
                .Trim(Package.DirectorySeparatorChar);

            foreach (var segmentRange in inputPath.Split([Package.DirectorySeparatorChar]))
            {
                var name = inputPath[segmentRange].ToString();

                if (node.Folders.TryGetValue(name, out var nextNode))
                {
                    node = nextNode;
                    continue;
                }

                foreach (var file in node.Files)
                {
                    if (file.GetFileName() == name)
                    {
                        fileToSelect = file;
                        break;
                    }
                }

                break;
            }

            mainTreeView.BeginUpdate();
            var treeNode = CreateTreeNodes(node);

            if (treeNode != null)
            {
                treeNode.EnsureVisible();
                treeNode.Expand();

                // If the path ended in a file name, select this file after the nodes are created
                if (fileToSelect != null)
                {
                    foreach (BetterTreeNode childTreeNode in treeNode.Nodes)
                    {
                        if (!childTreeNode.IsFolder && childTreeNode.PackageEntry == fileToSelect)
                        {
                            mainTreeView.SelectedNode = childTreeNode;
                            break;
                        }
                    }
                }
                else
                {
                    mainTreeView.SelectedNode = treeNode;
                }

                DisplayMainListView();
                MainListView_DisplayNodes(node, updatePath: false);
            }

            mainTreeView.EndUpdate();
        }

        private static int GetFolderImageIndex() => ImageIndexFolder;

        private static int GetFolderUpImageIndex() => ImageIndexFolderUp;

        private void SortListViewItemsForVirtualMode()
        {
            if (mainListView.ListViewItemSorter is not ListViewColumnSorter sorter)
            {
                return;
            }

            ListViewItems.Sort(sorter.Compare);

            if (ListViewItems.Count > 0)
            {
                mainListView.RedrawItems(0, ListViewItems.Count - 1, true);
            }
        }

        private void AssignBigIconIndicesAndRenderThumbnails()
        {
            var currentThumbnailSizeInt = (int)CurrentThumbnailSizes;

            for (var i = 0; i < ListViewItems.Count; i++)
            {
                if (ListViewItems[i] is not BetterListViewItem betterListViewItem)
                {
                    continue;
                }

                if (betterListViewItem.IsFolder)
                {
                    betterListViewItem.ImageIndex = betterListViewItem.Tag is BetterListViewItem.ParentNavigationTag
                        ? GetFolderUpImageIndex()
                        : GetFolderImageIndex();
                    continue;
                }

                var entry = betterListViewItem.PackageEntry!;

                // Check if we have a rendered thumbnail cached for this specific file
                if (BigIconImageCache.TryGetValue(entry.GetFullPath(), out var cachedThumbnail) && cachedThumbnail != null)
                {
                    betterListViewItem.ImageIndex = cachedThumbnail.index;
                    continue;
                }

                // Fall back to SVG icon for the file type
                var extension = ResolveExtension(entry.TypeName);

                if (!BigIconImageCache.TryGetValue(extension, out var iconImageCacheEntry) || iconImageCacheEntry == null)
                {
#pragma warning disable CA2000 // Bitmap lifetime is managed by ImageList, when ImageList is disposed it disposes all images too
                    var bitmap = GetTypeIconBitmap(entry.TypeName, currentThumbnailSizeInt);

                    lock (ImageListLock)
                    {
                        // Double-checked: another thread may have inserted while we were rendering.
                        if (!BigIconImageCache.TryGetValue(extension, out iconImageCacheEntry) || iconImageCacheEntry == null)
                        {
                            var index = BigIconsImageList.Images.Count; // authoritative, inside the lock
                            BigIconsImageList.Images.Add(bitmap);
                            iconImageCacheEntry = new IconImageCacheEntry(bitmap, index);
                            BigIconImageCache[extension] = iconImageCacheEntry;
                        }
                        // else: lost the race, discard the bitmap we just created
                    }
                }

                betterListViewItem.ImageIndex = iconImageCacheEntry.index;
            }

            if (mainListView.View == View.LargeIcon)
            {
                _ = UpdateLargeImageListIconsAsync(ThumbnailRenderTokenSource!.Token);
            }
        }

        /// <summary>
        /// Returns a task that resolves to the <see cref="ValveResourceFormat.ToolsAssetInfo.ToolsAssetInfo.File.SearchableUserData"/> filter keys.
        /// If already cached, returns a completed task. Otherwise loads on a background thread.
        /// </summary>
        internal Task<Dictionary<string, SortedSet<string>>?> GetSearchDataKeysAsync()
        {
            if (SearchDataBuilt)
            {
                return Task.FromResult(SearchDataKeys);
            }

            return Task.Run(GetSearchDataKeys);
        }

        /// <summary>
        /// Lazily loads the tools asset info and builds the <see cref="ValveResourceFormat.ToolsAssetInfo.ToolsAssetInfo.File.SearchableUserData"/> index.
        /// Returns the collected filter keys and their unique values, or null if no data.
        /// </summary>
        internal Dictionary<string, SortedSet<string>>? GetSearchDataKeys()
        {
            if (SearchDataBuilt)
            {
                return SearchDataKeys;
            }

            SearchDataBuilt = true;

            var vrfGuiContext = mainTreeView?.VrfGuiContext;
            if (vrfGuiContext == null)
            {
                return null;
            }

            var toolsAssetInfo = vrfGuiContext.GetOrLoadToolsAssetInfo();
            if (toolsAssetInfo == null)
            {
                return null;
            }

            var assetSearchData = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var searchDataKeys = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (filePath, file) in toolsAssetInfo.Files)
            {
                if (file.SearchableUserData.Count == 0)
                {
                    continue;
                }

                var converted = new Dictionary<string, string>(file.SearchableUserData.Count, StringComparer.OrdinalIgnoreCase);

                foreach (var (key, value) in file.SearchableUserData)
                {
                    var stringValue = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                    converted[key] = stringValue;

                    if (!searchDataKeys.TryGetValue(key, out var values))
                    {
                        values = new SortedSet<string>(SearchForm.NumericComparer);
                        searchDataKeys[key] = values;
                    }

                    if (values.Count < 100)
                    {
                        values.Add(stringValue);
                    }
                }

                assetSearchData[filePath] = converted;
            }

            if (searchDataKeys.Count > 0)
            {
                AssetSearchData = assetSearchData;
                SearchDataKeys = searchDataKeys;
            }

            return SearchDataKeys;
        }

        private bool MatchesAssetFilter(PackageEntry entry, string filterKey, string? filterValue)
        {
            Debug.Assert(AssetSearchData is not null);

            var filePath = entry.GetFullPath();

            if (!AssetSearchData.TryGetValue(filePath, out var searchData))
            {
                // Try without the compiled suffix
                if (!filePath.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
                {
                    return false;
                }

                var uncompiledPath = filePath.AsSpan(0, filePath.Length - GameFileLoader.CompiledFileSuffix.Length);

                if (!AssetSearchData.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(uncompiledPath, out searchData))
                {
                    return false;
                }
            }

            if (!searchData.TryGetValue(filterKey, out var value))
            {
                return false;
            }

            return filterValue == null || value == filterValue;
        }

        private void MainListView_Disposed(object? sender, EventArgs e)
        {
            mainListView.MouseDoubleClick -= MainListView_MouseDoubleClick;
            mainListView.MouseDown -= MainListView_MouseDown;
            mainListView.MouseWheel -= MainListView_MouseWheel;
            mainListView.ColumnClick -= MainListView_ColumnClick;
            mainListView.Scroll -= MainListView_Scroll;
            mainListView.Resize -= MainListView_Resize;
            mainListView.Disposed -= MainListView_Disposed;

            mainTreeView.MouseWheel -= MainListView_MouseWheel;
            mainTreeView.NodeMouseDoubleClick -= MainTreeView_NodeMouseDoubleClick;
            mainTreeView.NodeMouseClick -= MainTreeView_NodeMouseClick;
            mainTreeView.AfterSelect -= MainTreeView_AfterSelect;

            MouseWheel -= MainListView_MouseWheel;

            mainTreeView.VrfGuiContext?.Dispose();
            mainTreeView.VrfGuiContext = null;

            mainTreeView = null;
            mainListView = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
                BigIconsImageList.Dispose();
                ThumbnailRenderTokenSource?.Cancel();
                ThumbnailRenderTokenSource?.Dispose();
                RenderLoopCancelationTokenSource.Cancel();
                PreviewTokenSource?.Dispose();

                ThumbnailRenderStopped.Task.ContinueWith(_ =>
                {
                    ThumbnailRenderQueue.Dispose();
                    RenderLoopCancelationTokenSource.Dispose();
                }, TaskScheduler.Default);

                Viewer.GuiContext.DisposeAfter(ThumbnailRenderStopped.Task);
            }

            base.Dispose(disposing);
        }

        private void listRadioButton_CheckedChanged(object sender, EventArgs e)
        {
            if (listRadioButton.Checked)
            {
                Settings.Config.PackageGridView = 0;

                gridSizeSlider.Enabled = false;

                RestartThumbnailRendering();
                DrainThumbnailQueue();

                mainListView.View = View.Details;
                mainListView.SmallImageList = AppIcons.ImageList;

                AssignIcons();

                mainListView.AdjustColumnWidths();
                mainListView.Invalidate();
            }
        }

        private void AssignSmallIconIndices()
        {
            foreach (var item in ListViewItems)
            {
                if (item is not BetterListViewItem betterItem)
                {
                    continue;
                }

                if (betterItem.IsFolder)
                {
                    betterItem.ImageIndex = betterItem.Tag is BetterListViewItem.ParentNavigationTag
                        ? AppIcons.Icons["FolderUp"]
                        : mainTreeView.FolderImage;
                }
                else if (betterItem.PackageEntry != null
                    && mainTreeView.ExtensionIconList.TryGetValue(betterItem.PackageEntry.TypeName, out var image))
                {
                    betterItem.ImageIndex = image;
                }
                else
                {
                    betterItem.ImageIndex = AppIcons.Icons["File"];
                }
            }
        }

        private void gridRadioButton_CheckedChanged(object sender, EventArgs e)
        {
            if (gridRadioButton.Checked)
            {
                Settings.Config.PackageGridView = 1;

                gridSizeSlider.Enabled = true;

                RestartThumbnailRendering();

                mainListView.View = View.LargeIcon;
                AssignIcons();
            }
        }

        private void gridSizeSlider_Scroll(object sender, EventArgs e)
        {
            CurrentThumbnailSizes = Enum.GetValues<ThumbnailSizes>()[gridSizeSlider.Value];

            Settings.Config.PackageGridSize = gridSizeSlider.Value;

            RestartThumbnailRendering();

            // rebuild image list and clear caches
            var oldBigIconsImageList = BigIconsImageList;
            BigIconsImageList = InitThumbnailImageList();
            BigIconImageCache.Clear();
            DrainThumbnailQueue();

            mainListView.LargeImageList = BigIconsImageList;
            oldBigIconsImageList.Dispose();

            AssignIcons();
        }
    }
}
