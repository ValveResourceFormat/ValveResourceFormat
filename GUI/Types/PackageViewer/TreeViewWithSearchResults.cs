using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Utils;
using SteamDatabase.ValvePak;

namespace GUI.Types.PackageViewer
{
    /// <summary>
    /// Represents a user control in which a TreeView and ListView are used to view a directory/file listing. In addition to a normal TreeView,
    /// this control allows for searching to occur within the TreeView and have the results displayed in a ListView with details about the resulting
    /// items.
    /// </summary>
    partial class TreeViewWithSearchResults : UserControl
    {
        private static readonly string[] Columns = ["Name", "Size", "Type"];

        private static int SplitterWidth;

        public bool DeletedFilesRecovered { get; private set; }
        public PackageViewer Viewer { get; }

        public CancellationTokenSource? PreviewTokenSource { get; private set; }

        public event EventHandler<PackageEntry>? OpenPackageEntry;
        public event EventHandler<PackageContextMenuEventArgs>? OpenContextMenu;
        public event EventHandler<PackageEntry>? PreviewFile;

        /// <summary>
        /// Initializes a new instance of the <see cref="TreeViewWithSearchResults"/> class.
        /// Require a default constructor for the designer.
        /// </summary>
        public TreeViewWithSearchResults(PackageViewer viewer)
        {
            InitializeComponent();

            foreach (Control control in Controls)
            {
                Themer.ThemeControl(control);
            }

            searchTextBox.BackColor = Themer.CurrentThemeColors.AppMiddle;

            if (SplitterWidth > 0)
            {
                mainSplitContainer.SplitterDistance = SplitterWidth;
            }

            Dock = DockStyle.Fill;

            mainListView.MouseDoubleClick += MainListView_MouseDoubleClick;
            mainListView.MouseDown += MainListView_MouseDown;
            mainListView.ColumnClick += MainListView_ColumnClick;
            mainListView.Disposed += MainListView_Disposed;
            mainListView.FullRowSelect = true;
            mainListView.ListViewItemSorter = new ListViewColumnSorter();

            mainTreeView.HideSelection = false;
            mainTreeView.NodeMouseDoubleClick += MainTreeView_NodeMouseDoubleClick;
            mainTreeView.NodeMouseClick += MainTreeView_NodeMouseClick;
            mainTreeView.AfterSelect += MainTreeView_AfterSelect;

            Viewer = viewer;
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            Themer.ThemeControl(this);

            mainTreeView.BackColor = Themer.CurrentThemeColors.AppMiddle;
            mainListView.BackColor = Themer.CurrentThemeColors.AppSoft;
            mainSplitContainer.BackColor = Themer.CurrentThemeColors.AppMiddle;
        }

        private void MainListView_Disposed(object? sender, EventArgs e)
        {
            mainListView.MouseDoubleClick -= MainListView_MouseDoubleClick;
            mainListView.MouseDown -= MainListView_MouseDown;
            mainListView.ColumnClick -= MainListView_ColumnClick;
            mainListView.Disposed -= MainListView_Disposed;

            mainTreeView.NodeMouseDoubleClick -= MainTreeView_NodeMouseDoubleClick;
            mainTreeView.NodeMouseClick -= MainTreeView_NodeMouseClick;
            mainTreeView.AfterSelect -= MainTreeView_AfterSelect;

            mainTreeView.VrfGuiContext?.Dispose();
            mainTreeView.VrfGuiContext = null;
            mainListView.VrfGuiContext = null;

            mainTreeView = null;
            mainListView = null;
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
        }

        private void MainTreeView_AfterSelect(object? sender, TreeViewEventArgs e)
        {
            if (e.Action == TreeViewAction.Unknown || e.Node == null)
            {
                return;
            }

            var realNode = (BetterTreeNode)e.Node;

            // if user selected a folder, show the contents of that folder in the list view
            if (realNode.IsFolder)
            {
                DisplayMainListView();

                if (realNode.PkgNode != null)
                {
                    MainListView_DisplayNodes(realNode.PkgNode);
                }
            }
            else
            {
                PreviewTokenSource?.Cancel();
                PreviewTokenSource = new CancellationTokenSource();

                Task.Run(async () =>
                {
                    var token = PreviewTokenSource.Token;

                    // The default double-click time in windows (500) is too long to wait entirely.
                    var mouseDoubleClickIntervalMs = 200;
                    await Task.Delay(mouseDoubleClickIntervalMs).ConfigureAwait(false);

                    // double-clicked or started previewing a different file
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    if (realNode.PackageEntry != null)
                    {
                        await InvokeAsync(() => PreviewFile?.Invoke(sender, realNode.PackageEntry)).ConfigureAwait(false);
                    }
                });
            }
        }

        private void MainListView_DisplayNodes(VirtualPackageNode pkgNode, bool updatePath = true)
        {
            mainListView.BeginUpdate();
            mainListView.Items.Clear();

            var sorter = mainListView.ListViewItemSorter;
            mainListView.ListViewItemSorter = null;

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

            mainListView.ListViewItemSorter = sorter;
            mainListView.EndUpdate();

            if (updatePath)
            {
                UpdateSearchTextBoxToCurrentPath(pkgNode);
            }
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
            control.ImageList = MainForm.ImageList;
            control.BeforeExpand += Control_BeforeExpand;
            control.ShowRootLines = false;

            if (vrfGuiContext.CurrentPackage?.Entries != null)
            {
                control.GenerateIconList([.. vrfGuiContext.CurrentPackage.Entries.Keys]);
            }

            if (vrfGuiContext.CurrentPackage != null && !vrfGuiContext.CurrentPackage.IsDirVPK)
            {
                // Disable recover deleted files button for non-dir packages
                DeletedFilesRecovered = true;
            }

            var fullFilePath = vrfGuiContext.FileName.AsSpan();
            var fileName = Path.GetFileName(fullFilePath);
            var parentFolder = Path.GetFileName(Path.GetDirectoryName(fullFilePath));
            var name = fullFilePath.Length > 0 ? $"{parentFolder}/{fileName}" : fileName.ToString();
            var vpkImage = MainForm.ExtensionIcons["vpk"];

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

            MainListView_DisplayNodes(rootVirtual);
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
                image = MainForm.GetImageIndexForExtension(file.TypeName.ToLowerInvariant());
            }
            else if (!mainTreeView.ExtensionIconList.TryGetValue(file.TypeName, out image))
            {
                image = MainForm.Icons["File"];
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
                var treeNode = (BetterTreeNode?)parentTreeNode.Nodes[currentNode.Name];

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

            var node = BetterTreeView.AddFileNode(root, file);

            CreateTreeNodes(node, true);

            if (node.CreatedNode != null)
            {
                CreateFileNode(node.CreatedNode, file, true);
            }
        }

        internal void RecoverDeletedFiles()
        {
            DeletedFilesRecovered = true;

            var currentPackage = mainTreeView.VrfGuiContext?.CurrentPackage;
            if (currentPackage == null)
            {
                return;
            }

            using var progressDialog = new GenericProgressForm
            {
                Text = "Scanning for deleted files…"
            };
            progressDialog.OnProcess += (_, __) =>
            {
                progressDialog.SetProgress("Scanning for deleted files, this may take a while…");

                var foundFiles = Types.PackageViewer.PackageViewer.RecoverDeletedFiles(currentPackage, progressDialog.SetProgress);

                Invoke((MethodInvoker)(() =>
                {
                    var deletedImage = MainForm.Icons["Recover"];

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
            };
            progressDialog.ShowDialog();
        }

        internal void VerifyPackageContents()
        {
            var package = mainTreeView.VrfGuiContext?.CurrentPackage;
            if (package?.Entries == null)
            {
                return;
            }

            using var progressDialog = new GenericProgressForm
            {
                Text = "Verifying package…"
            };
            progressDialog.OnProcess += (_, cancellationToken) =>
            {
                try
                {
                    if (!package.IsSignatureValid())
                    {
                        throw new InvalidDataException("The signature in this package is not valid.");
                    }

                    progressDialog.SetProgress("Verifying hashes…");

                    package.VerifyHashes();

                    var processed = 0;

                    // This does not need to be perfect, ValvePak reports a string per file, and success strings.
                    var maximum = package.AccessPackFileHashes.Count + 2;

                    if (package.AccessPackFileHashes.Count == 0)
                    {
                        maximum += package.Entries.Sum(x => x.Value.Count);
                    }

                    progressDialog.Invoke(() =>
                    {
                        progressDialog.SetBarMax(maximum);
                    });

                    var lastUpdate = 0L;
                    var updateInterval = TimeSpan.FromMilliseconds(400);

                    var progressReporter = new Progress<string>(progress =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }

                        var value = Math.Min(++processed, maximum);

                        var currentTime = System.Diagnostics.Stopwatch.GetTimestamp();
                        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(lastUpdate, currentTime);

                        if (elapsed < updateInterval)
                        {
                            return;
                        }

                        lastUpdate = currentTime;

                        progressDialog.Invoke(() =>
                        {
                            progressDialog.SetBarValue(value);
                            progressDialog.SetProgress(progress);
                        });
                    });

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        package.VerifyChunkHashes(progressReporter);
                    }

                    if (!cancellationToken.IsCancellationRequested && package.AccessPackFileHashes.Count == 0)
                    {
                        package.VerifyFileChecksums(progressReporter);
                    }

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        progressDialog.Invoke(() =>
                        {
                            progressDialog.SetBarValue(maximum);
                        });

                        MessageBox.Show(
                            "Successfully verified package contents.",
                            "Verified package contents",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                    }
                }
                catch (Exception e)
                {
                    Log.Error(nameof(Package), $"Failed to verify package contents: {e.Message}");

                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    MessageBox.Show(
                        e.Message,
                        "Failed to verify package contents",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                }
            };
            progressDialog.ShowDialog();
        }

        /// <summary>
        /// Performs a search for the entered text and search types. Before a search is performed, the contents of the ListView (previous search results) are cleared.
        /// Results of whatever search function is used are displayed in the ListView with name, file size, and file type.
        /// </summary>
        /// <param name="searchText">Value to search for in the TreeView. Matching on this value is based on the search type.</param>
        /// <param name="selectedSearchType">Determines the matching of the value. For example, full/partial text search or full path search.</param>
        internal void SearchAndFillResults(string searchText, SearchType selectedSearchType)
        {
            var results = mainTreeView.Search(searchText, selectedSearchType);

            mainListView.BeginUpdate();
            mainListView.Items.Clear();

            var sorter = mainListView.ListViewItemSorter;
            mainListView.ListViewItemSorter = null;

            foreach (var entry in results)
            {
                AddFileToListView(entry);
            }

            mainListView.ListViewItemSorter = sorter;

            DisplayMainListView();
            mainListView.EndUpdate();
        }

        private void MainListView_ColumnClick(object? sender, ColumnClickEventArgs e)
        {
            if (mainListView.ListViewItemSorter is not ListViewColumnSorter sorter)
            {
                return;
            }

            if (e.Column == sorter.SortColumn)
            {
                if (sorter.Order == SortOrder.Ascending)
                {
                    sorter.Order = SortOrder.Descending;
                }
                else
                {
                    sorter.Order = SortOrder.Ascending;
                }
            }
            else
            {
                sorter.SortColumn = e.Column;

                // For size column, prefer descending first
                sorter.Order = e.Column == 1 ? SortOrder.Descending : SortOrder.Ascending;
            }

            mainListView.Sort();
            mainListView.Invalidate(true);
        }

        /// <summary>
        /// When the user clicks in the ListView, check if the user clicks outside of a ListViewItem. If so, de-select any previously selected ListViewItems. In addition,
        /// if the user right clicked an item in the ListView, let our subscribers know what was clicked and where in case a context menu is needed to be shown.
        /// </summary>
        /// <param name="sender">Object which raised event.</param>
        /// <param name="e">Event data.</param>
        private void MainListView_MouseDown(object? sender, MouseEventArgs e)
        {
            var info = mainListView.HitTest(e.X, e.Y);

            // if an item was clicked in the list view
            if (info.Item is not IBetterBaseItem item)
            {
                mainListView.SelectedItems.Clear();
                return;
            }

            // When left or right clicking a folder, select it in the tree view and ensure it is visible
            if (item.PkgNode != null && mainListView.SelectedItems.Count <= 1)
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
                if (mainListView.SelectedItems.Count <= 1)
                {
                    var pkgNode = mainTreeView.Root;

                    if (pkgNode == null)
                    {
                        return;
                    }

                    var packageNodes = new List<VirtualPackageNode>(2)
                    {
                        pkgNode
                    };

                    // Walk up the directories in the file path to collect all the virtual nodes
                    if (!string.IsNullOrWhiteSpace(item.PackageEntry.DirectoryName))
                    {
                        var directoryName = item.PackageEntry.DirectoryName.AsSpan();

                        foreach (var subPathRange in directoryName.Split([Package.DirectorySeparatorChar]))
                        {
                            var subPath = directoryName[subPathRange].ToString();

                            if (!pkgNode.Folders.TryGetValue(subPath, out var subNode))
                            {
                                throw new InvalidDataException("Failed to find sub path");
                            }

                            pkgNode = subNode;
                            packageNodes.Add(subNode);
                        }
                    }

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
                mainListView.SelectedItems.Clear();
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

            mainListView.SmallImageList = MainForm.ImageList;
        }

        private void AddParentNavigationItemToListView(VirtualPackageNode parentNode)
        {
            var image = MainForm.Icons["FolderUp"];
            var name = parentNode.Parent == null ? ".." : $".. {parentNode.Name}";

            var item = new BetterListViewItem(name)
            {
                ImageIndex = image,
                PkgNode = parentNode,
                Tag = BetterListViewItem.ParentNavigationTag,
            };

            item.SubItems.Add(HumanReadableByteSizeFormatter.Format(parentNode.TotalSize));
            item.SubItems.Add(string.Empty);

            mainListView.Items.Add(item);
        }

        private void AddFolderToListView(string name, VirtualPackageNode node)
        {
            var item = new BetterListViewItem(name)
            {
                ImageIndex = mainTreeView.FolderImage,
                PkgNode = node,
            };

            item.SubItems.Add(HumanReadableByteSizeFormatter.Format(node.TotalSize));
            item.SubItems.Add(string.Empty);

            mainListView.Items.Add(item);
        }

        private void AddFileToListView(PackageEntry file)
        {
            if (!mainTreeView.ExtensionIconList.TryGetValue(file.TypeName, out var image))
            {
                image = MainForm.Icons["File"];
            }

            var item = new BetterListViewItem(file.GetFileName())
            {
                ImageIndex = image,
                PackageEntry = file,
            };

            item.SubItems.Add(HumanReadableByteSizeFormatter.Format(file.TotalLength));
            item.SubItems.Add(file.TypeName);

            mainListView.Items.Add(item);
        }

        public void ReplaceListViewWithControl(TabPage tab)
        {
            mainListView.Visible = false;

            var tabs = new ThemedTabControl
            {
                ImageList = MainForm.ImageList,
                Dock = DockStyle.Fill
            };
            tabs.Controls.Add(tab);

            rightPanel.Controls.Add(tabs);

            foreach (Control old in rightPanel.Controls)
            {
                if (old == tabs || old == mainListView) // TODO: dumb
                {
                    continue;
                }

                old.Dispose();
            }
        }

        private void DisplayMainListView()
        {
            foreach (Control old in rightPanel.Controls)
            {
                if (old != mainListView)
                {
                    old.Dispose();
                }
            }

            mainListView.Visible = true;
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
    }
}
