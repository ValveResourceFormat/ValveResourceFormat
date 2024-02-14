using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Utils;
using SteamDatabase.ValvePak;

namespace GUI.Controls
{
    /// <summary>
    /// Represents a user control in which a TreeView and ListView are used to view a directory/file listing. In addition to a normal TreeView,
    /// this control allows for searching to occur within the TreeView and have the results displayed in a ListView with details about the resulting
    /// items.
    /// </summary>
    partial class TreeViewWithSearchResults : UserControl
    {
        public bool DeletedFilesRecovered { get; private set; }
        public Types.Viewers.Package Viewer { get; }

        public event TreeNodeMouseClickEventHandler TreeNodeMouseDoubleClick; // when a TreeNode is double clicked
        public event TreeNodeMouseClickEventHandler TreeNodeRightClick; // when a TreeNode is single clicked
        public event EventHandler<ListViewItemClickEventArgs> ListViewItemDoubleClick; // when a ListViewItem is double clicked
        public event EventHandler<ListViewItemClickEventArgs> ListViewItemRightClick; // when a ListViewItem is single clicked

        /// <summary>
        /// Initializes a new instance of the <see cref="TreeViewWithSearchResults"/> class.
        /// Require a default constructor for the designer.
        /// </summary>
        public TreeViewWithSearchResults(Types.Viewers.Package viewer)
        {
            InitializeComponent();

            Dock = DockStyle.Fill;

            mainListView.MouseDoubleClick += MainListView_MouseDoubleClick;
            mainListView.MouseDown += MainListView_MouseDown;
            mainListView.ColumnClick += MainListView_ColumnClick;
            mainListView.Resize += MainListView_Resize;
            mainListView.Disposed += MainListView_Disposed;
            mainListView.FullRowSelect = true;

            mainTreeView.HideSelection = false;
            mainTreeView.NodeMouseDoubleClick += MainTreeView_NodeMouseDoubleClick;
            mainTreeView.NodeMouseClick += MainTreeView_NodeMouseClick;
            mainTreeView.AfterSelect += MainTreeView_AfterSelect;
            Viewer = viewer;
        }

        private void MainListView_Disposed(object sender, EventArgs e)
        {
            mainListView.MouseDoubleClick -= MainListView_MouseDoubleClick;
            mainListView.MouseDown -= MainListView_MouseDown;
            mainListView.ColumnClick -= MainListView_ColumnClick;
            mainListView.Resize -= MainListView_Resize;
            mainListView.Disposed -= MainListView_Disposed;

            mainTreeView.NodeMouseDoubleClick -= MainTreeView_NodeMouseDoubleClick;
            mainTreeView.NodeMouseClick -= MainTreeView_NodeMouseClick;
            mainTreeView.AfterSelect -= MainTreeView_AfterSelect;

            mainTreeView.VrfGuiContext.Dispose();
            mainTreeView.VrfGuiContext = null;
            mainListView.VrfGuiContext = null;

            mainTreeView = null;
            mainListView = null;
        }

        private void MainTreeView_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (mainTreeView.SelectedNode is BetterTreeNode node && node.IsFolder)
            {
                return;
            }

            TreeNodeMouseDoubleClick?.Invoke(sender, e);
        }

        private void MainTreeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            // if user selected a folder, show the contents of that folder in the list view
            if (e.Action != TreeViewAction.Unknown && ((BetterTreeNode)e.Node).IsFolder)
            {
                mainListView.BeginUpdate();
                mainListView.Items.Clear();

                foreach (BetterTreeNode node in e.Node.Nodes)
                {
                    AddNodeToListView(node);
                }

                mainListView.EndUpdate();
            }
        }

        private void MainTreeView_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                mainTreeView.SelectedNode = e.Node;

                TreeNodeRightClick?.Invoke(sender, e);
            }
        }

        private void MainListView_Resize(object sender, EventArgs e)
        {
            mainListView.BeginUpdate();
            ResizeListViewColumns();
            mainListView.EndUpdate();
        }

        private void ResizeListViewColumns()
        {
            foreach (ColumnHeader col in mainListView.Columns)
            {
                if (col.Text == "Name")
                {
                    col.Width = mainListView.ClientSize.Width - (mainListView.Columns.Count - 1) * 100;
                }
                else
                {
                    col.Width = 100;
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
        internal void InitializeTreeViewFromPackage(VrfGuiContext vrfGuiContext)
        {
            mainListView.VrfGuiContext = vrfGuiContext;

            var control = mainTreeView;
            control.BeginUpdate();
            control.PathSeparator = Package.DirectorySeparatorChar.ToString();
            control.Name = "treeViewVpk";
            control.VrfGuiContext = vrfGuiContext;
            control.Dock = DockStyle.Fill;
            control.ImageList = MainForm.ImageList;
            control.ShowRootLines = false;

            control.GenerateIconList([.. vrfGuiContext.CurrentPackage.Entries.Keys]);

            if (!vrfGuiContext.CurrentPackage.IsDirVPK)
            {
                // Disable recover deleted files button for non-dir packages
                DeletedFilesRecovered = true;
            }

            var name = Path.GetFileName(vrfGuiContext.FileName);
            var vpkImage = MainForm.ImageListLookup["vpk"];
            var root = new BetterTreeNode(name, 0u)
            {
                Name = "root",
                ImageIndex = vpkImage,
                SelectedImageIndex = vpkImage,
            };
            control.Nodes.Add(root);
            root.Expand();

            control.TreeViewNodeSorter = new TreeViewFileSorter();

            foreach (var fileType in vrfGuiContext.CurrentPackage.Entries)
            {
                foreach (var file in fileType.Value)
                {
                    control.AddFileNode(root, file);
                }
            }

            // Expand lone folders (common in maps vpks)
            var node = root;

            while (node.Nodes.Count == 1)
            {
                node = (BetterTreeNode)node.Nodes[0];
                node.Expand();
            }

            control.EndUpdate();
        }

        internal void AddFolderNode(string directoryName)
        {
            var root = mainTreeView.Nodes[0] as BetterTreeNode;
            mainTreeView.AddFolderNode(root, directoryName, 0u);
        }

        internal void AddFileNode(PackageEntry file)
        {
            var root = mainTreeView.Nodes[0] as BetterTreeNode;
            mainTreeView.AddFileNode(root, file, manualAdd: true);
        }

        internal void RecoverDeletedFiles()
        {
            DeletedFilesRecovered = true;

            using var progressDialog = new GenericProgressForm
            {
                Text = "Scanning for deleted files…"
            };
            progressDialog.OnProcess += (_, __) =>
            {
                progressDialog.SetProgress("Scanning for deleted files, this may take a while…");

                var foundFiles = Types.Viewers.Package.RecoverDeletedFiles(mainTreeView.VrfGuiContext.CurrentPackage, progressDialog.SetProgress);

                Invoke((MethodInvoker)(() =>
                {
                    var deletedImage = MainForm.ImageListLookup["_deleted"];

                    if (foundFiles.Count == 0)
                    {
                        const string NO_FILES_FOUND = "No deleted files found";
                        mainTreeView.Nodes.Add(new BetterTreeNode(NO_FILES_FOUND, 0)
                        {
                            Name = NO_FILES_FOUND,
                            ImageIndex = deletedImage,
                            SelectedImageIndex = deletedImage,
                        });
                        return;
                    }

                    mainTreeView.BeginUpdate();

                    var name = $"Deleted files ({foundFiles.Count} files found, names are guessed)";
                    var root = new BetterTreeNode(name, 0u)
                    {
                        Name = name,
                        ImageIndex = deletedImage,
                        SelectedImageIndex = deletedImage,
                    };
                    mainTreeView.Nodes.Add(root);

                    var vpkName = Path.GetFileName(mainTreeView.VrfGuiContext.CurrentPackage.FileName);

                    foreach (var file in foundFiles)
                    {
                        mainTreeView.AddFileNode(root, file);
                    }

                    root.Expand();
                    mainTreeView.EndUpdate();
                }));
            };
            progressDialog.ShowDialog();
        }

        internal void VerifyPackageContents()
        {
            using var progressDialog = new GenericProgressForm
            {
                Text = "Verifying package…"
            };
            progressDialog.OnProcess += (_, __) =>
            {
                var package = mainTreeView.VrfGuiContext.CurrentPackage;

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
                    var maximum = package.Entries.Sum(x => x.Value.Count) + package.ArchiveMD5Entries.Count + 2;

                    progressDialog.Invoke(() =>
                    {
                        progressDialog.SetBarMax(maximum);
                    });

                    var lastUpdate = 0L;
                    var updateInterval = TimeSpan.FromMilliseconds(400);

                    var progressReporter = new Progress<string>(progress =>
                    {
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

                    package.VerifyChunkHashes(progressReporter);
                    package.VerifyFileChecksums(progressReporter);

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
                catch (Exception e)
                {
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
            mainListView.BeginUpdate();
            mainListView.Items.Clear();

            var results = mainTreeView.Search(searchText, selectedSearchType);

            foreach (var node in results)
            {
                AddNodeToListView(node);
            }

            ResizeListViewColumns();

            mainListView.EndUpdate();
        }

        private void MainListView_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            mainListView.ListViewItemSorter ??= new ListViewColumnSorter();

            var sorter = (ListViewColumnSorter)mainListView.ListViewItemSorter;

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
                sorter.Order = SortOrder.Ascending;
            }

            mainListView.Sort();
        }

        /// <summary>
        /// When the user clicks in the ListView, check if the user clicks outside of a ListViewItem. If so, de-select any previously selected ListViewItems. In addition,
        /// if the user right clicked an item in the ListView, let our subscribers know what was clicked and where in case a context menu is needed to be shown.
        /// </summary>
        /// <param name="sender">Object which raised event.</param>
        /// <param name="e">Event data.</param>
        private void MainListView_MouseDown(object sender, MouseEventArgs e)
        {
            var info = mainListView.HitTest(e.X, e.Y);

            // if an item was clicked in the list view
            if (info.Item != null)
            {
                // right click should just notify our subscribers
                if (e.Button == MouseButtons.Right)
                {
                    ListViewItemRightClick?.Invoke(sender, new ListViewItemClickEventArgs(info.Item, e.Location));
                }
                else if (e.Button == MouseButtons.Left)
                {
                    // left click should focus the node in its tree view
                    var node = (BetterTreeNode)info.Item.Tag;

                    if (node.IsFolder)
                    {
                        node.EnsureVisible();
                        node.TreeView.SelectedNode = node;
                    }
                }
            }
            else
            {
                mainListView.SelectedItems.Clear();
            }
        }

        /// <summary>
        /// If the user double clicks (with left mouse button) on a ListViewItem, send up an event to subscribers that such an action has occurred. Also send up
        /// whatever object is represented by the ListViewItem.
        /// </summary>
        /// <param name="sender">Object which raised event.</param>
        /// <param name="e">Event data.</param>
        private void MainListView_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                var info = mainListView.HitTest(e.X, e.Y);

                if (info.Item != null)
                {
                    // if user left double clicks a folder, open its contents and display in list view
                    var node = info.Item.Tag as BetterTreeNode;
                    if (node.IsFolder)
                    {
                        node.Expand();
                        mainListView.BeginUpdate();
                        mainListView.Items.Clear();
                        foreach (BetterTreeNode childNode in node.Nodes)
                        {
                            AddNodeToListView(childNode);
                        }
                        mainListView.EndUpdate();
                    }

                    ListViewItemDoubleClick?.Invoke(sender, new ListViewItemClickEventArgs(info.Item.Tag));
                }
                else
                {
                    mainListView.SelectedItems.Clear();
                }
            }
        }

        /// <summary>
        /// When the form loads, create the columns that we want to see such as name, file size, and file type.
        /// </summary>
        /// <param name="sender">Object which raised event.</param>
        /// <param name="e">Event data.</param>
        private void TreeViewWithSearchResults_Load(object sender, EventArgs e)
        {
            mainListView.Columns.Add("Name");
            mainListView.Columns.Add("Size");
            mainListView.Columns.Add("Type");
            mainListView.SmallImageList = MainForm.ImageList;
        }

        private void AddNodeToListView(BetterTreeNode node)
        {
            var item = new ListViewItem(node.Text)
            {
                ImageIndex = node.ImageIndex,
                Tag = node,
            };

            if (!node.IsFolder)
            {
                var file = node.PackageEntry;
                item.SubItems.Add(HumanReadableByteSizeFormatter.Format(file.TotalLength));
                item.SubItems.Add(file.TypeName);
            }
            else
            {
                item.SubItems.Add(HumanReadableByteSizeFormatter.Format(node.TotalSize));
                item.SubItems.Add(string.Empty);
            }

            mainListView.Items.Add(item);
        }
    }
}
