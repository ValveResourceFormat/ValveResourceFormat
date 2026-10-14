using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace GUI.Controls
{
    /// <summary>
    /// Lists the files a resource references, in a collapsible category per type of referenced file.
    /// </summary>
    class ResourceReferenceList : UserControl
    {
        private const string ContentCategory = "Source files, not shipped";
        private const string SubassetCategory = "Subassets";
        private const string NotFoundNote = "\nNot found in the loaded files";
        private const int MaxRowsShown = 500;

        private enum RowCategory
        {
            /// <summary>A file the game loads, which can be opened when the loaded files have it.</summary>
            Reference,

            /// <summary>A content file the resource was compiled from, which the game does not ship.</summary>
            Content,

            /// <summary>A name defined inside another resource, which has no file of its own.</summary>
            Subasset,
        }

        private sealed class Row
        {
            public required ResourceReference Reference { get; init; }
            public required RowCategory Category { get; init; }
            public required TreeNode Node { get; init; }
            public required string Search { get; init; }
        }

        private sealed class Category
        {
            public required string Name { get; init; }
            public required int Icon { get; init; }
            public required bool StartsCollapsed { get; init; }
            public List<Row> Rows { get; } = [];
        }

        /// <summary>A top level node, whose children are only added once it is first expanded.</summary>
        private sealed class Group
        {
            public required string Key { get; init; }
            public required TreeNode[] Children { get; init; }
            public bool Filled { get; set; }
        }

        /// <summary>The last row of a folder that lists only its first rows, which lists all of them when opened.</summary>
        private sealed record ShowAllRows(Group Group);

        private readonly VrfGuiContext GuiContext;
        private readonly bool ListsReferrers;
        private readonly TreeViewDoubleBuffered Tree;
        private readonly ThemedTextBox FilterBox;
        private readonly System.Windows.Forms.Timer FilterTimer = new() { Interval = 150 };
        private readonly List<Row> Rows = [];
        private readonly HashSet<TreeNode> MarkedNotFound = [];
        private readonly List<Category> Categories = [];
        private readonly Dictionary<string, bool> ExpandedByUser = new(StringComparer.Ordinal);
        private readonly HashSet<string> ShowingAllRows = new(StringComparer.Ordinal);
        private string AppliedFilter = string.Empty;
        private bool Rebuilding;
        private int LookedUpRows = -1;

        /// <param name="guiContext">The context the references are looked up and opened in.</param>
        /// <param name="references">The files the resource references.</param>
        public ResourceReferenceList(VrfGuiContext guiContext, IReadOnlyList<ResourceReference> references)
            : this(guiContext, references, listsReferrers: false)
        {
        }

        private ResourceReferenceList(VrfGuiContext guiContext, IReadOnlyList<ResourceReference> references, bool listsReferrers)
        {
            ArgumentNullException.ThrowIfNull(references);

            GuiContext = guiContext;
            ListsReferrers = listsReferrers;
            Dock = DockStyle.Fill;

            Tree = new TreeViewDoubleBuffered
            {
                Dock = DockStyle.Fill,
                ImageList = AppIcons.ImageList,
                HideSelection = false,
                ShowRootLines = true,
                ShowNodeToolTips = true,
            };

            Tree.NodeMouseDoubleClick += OnNodeDoubleClick;
            Tree.KeyDown += OnKeyDown;
            Tree.BeforeExpand += OnBeforeExpand;
            Tree.AfterExpand += OnExpandChanged;
            Tree.AfterCollapse += OnExpandChanged;

            FilterBox = new ThemedTextBox
            {
                PlaceholderText = "Filter references…",
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Multiline = false,
            };
            FilterBox.TextChanged += OnFilterChanged;
            FilterTimer.Tick += OnFilterTimerTick;

            var filterPanel = new Panel
            {
                Padding = new Padding(this.AdjustForDPI(4)),
                Dock = DockStyle.Top,
                Height = FilterBox.PreferredHeight + this.AdjustForDPI(8),
            };
            filterPanel.Controls.Add(FilterBox);

            BuildRows(references);

            Controls.Add(Tree);
            Controls.Add(filterPanel);

            ApplyFilter();

            Themer.ThemeControl(this);
        }

        /// <summary>
        /// Lists the files that reference a resource, with the places each one stores the reference in as its kinds.
        /// </summary>
        public static ResourceReferenceList ForReferrers(VrfGuiContext guiContext, IReadOnlyList<ResourceReference> referrers)
            => new(guiContext, referrers, listsReferrers: true);

        protected override void OnCreateControl()
        {
            base.OnCreateControl();

            BackColor = Themer.CurrentThemeColors.AppMiddle;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (LookedUpRows >= 0)
            {
                return;
            }

            LookedUpRows = 0;
            BeginInvoke(LookUpNextRows);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Tree.NodeMouseDoubleClick -= OnNodeDoubleClick;
                Tree.KeyDown -= OnKeyDown;
                Tree.BeforeExpand -= OnBeforeExpand;
                Tree.AfterExpand -= OnExpandChanged;
                Tree.AfterCollapse -= OnExpandChanged;
                FilterBox.TextChanged -= OnFilterChanged;
                FilterTimer.Tick -= OnFilterTimerTick;

                FilterTimer.Dispose();
                Tree.Dispose();
                FilterBox.Dispose();
            }

            base.Dispose(disposing);
        }

        private void BuildRows(IReadOnlyList<ResourceReference> references)
        {
            var byName = new Dictionary<string, Category>(StringComparer.Ordinal);
            var dimmed = Themer.CurrentThemeColors.ContrastSoft;
            var folderIcon = AppIcons.Icons["Folder"];

            foreach (var reference in references)
            {
                var extension = GetExtension(reference.Name);
                var category = Categorize(reference);
                var typeName = TypeName(reference, extension);
                var icon = AppIcons.GetImageIndexForExtension(extension);

                var node = new TreeNode(reference.Name)
                {
                    ImageIndex = icon,
                    SelectedImageIndex = icon,
                    Tag = reference,
                    ToolTipText = ToolTip(reference, typeName, category),
                };

                if (category != RowCategory.Reference)
                {
                    node.ForeColor = dimmed;
                }

                var categoryName = category switch
                {
                    RowCategory.Content => ContentCategory,
                    RowCategory.Subasset => SubassetCategory,
                    _ => typeName,
                };

                if (!byName.TryGetValue(categoryName, out var group))
                {
                    group = new Category
                    {
                        Name = categoryName,
                        Icon = category == RowCategory.Reference ? icon : folderIcon,
                        StartsCollapsed = category != RowCategory.Reference,
                    };

                    byName.Add(categoryName, group);
                    Categories.Add(group);
                }

                var row = new Row
                {
                    Reference = reference,
                    Category = category,
                    Node = node,
                    Search = string.Join(' ', reference.Name, typeName, node.ToolTipText),
                };

                group.Rows.Add(row);
                Rows.Add(row);
            }

            Categories.Sort(static (a, b) =>
            {
                var rank = SortRank(a).CompareTo(SortRank(b));
                return rank != 0 ? rank : string.CompareOrdinal(a.Name, b.Name);
            });

            foreach (var category in Categories)
            {
                category.Rows.Sort(static (a, b) => string.Compare(a.Node.Text, b.Node.Text, StringComparison.OrdinalIgnoreCase));
            }
        }

        private static ReadOnlySpan<char> GetExtension(string name)
        {
            var extension = Path.GetExtension(name.AsSpan());
            return extension.IsEmpty ? extension : extension[1..];
        }

        private static int SortRank(Category category) => category.Name switch
        {
            ContentCategory => 1,
            SubassetCategory => 2,
            _ => 0,
        };

        private RowCategory Categorize(ResourceReference reference)
        {
            const ResourceReferenceKind ContentOnly = ResourceReferenceKind.InputDependency | ResourceReferenceKind.Related;

            if (ListsReferrers)
            {
                return RowCategory.Reference;
            }

            var kinds = reference.Kinds;

            if (kinds == ResourceReferenceKind.Subasset)
            {
                return RowCategory.Subasset;
            }

            if ((kinds & ~ContentOnly) == ResourceReferenceKind.None
                || (reference.Type == ResourceType.Unknown && kinds.HasFlag(ResourceReferenceKind.InputDependency)))
            {
                return RowCategory.Content;
            }

            return RowCategory.Reference;
        }

        private static string TypeName(ResourceReference reference, ReadOnlySpan<char> extension)
        {
            if (reference.Type != ResourceType.Unknown)
            {
                return reference.Type.ToString();
            }

            if (reference.Kinds == ResourceReferenceKind.Subasset)
            {
                return "Subasset";
            }

            return extension.IsEmpty ? "Other" : string.Concat(".", extension);
        }

        private string ToolTip(ResourceReference reference, string typeName, RowCategory category)
        {
            if (ListsReferrers)
            {
                return $"{reference.Name}\n{typeName}, references this file in its {KindNames(reference.Kinds)}";
            }

            var tip = $"{reference.Name}\n{typeName}, found in: {KindNames(reference.Kinds)}";

            if (reference.Source != null)
            {
                tip = string.Concat(tip, " (", reference.Source, ")");
            }

            return category switch
            {
                RowCategory.Content => string.Concat(tip, "\nA content file, the game does not ship it"),
                RowCategory.Subasset => string.Concat(tip, "\nDefined inside another resource, it has no file of its own"),
                _ => tip,
            };
        }

        private static string KindNames(ResourceReferenceKind kinds)
        {
            var names = new List<string>(2);

            if (kinds.HasFlag(ResourceReferenceKind.External))
            {
                names.Add("external reference list");
            }

            if (kinds.HasFlag(ResourceReferenceKind.Child))
            {
                names.Add("child resource");
            }

            if (kinds.HasFlag(ResourceReferenceKind.Weak))
            {
                names.Add("weak reference");
            }

            if (kinds.HasFlag(ResourceReferenceKind.Related))
            {
                names.Add("related file");
            }

            if (kinds.HasFlag(ResourceReferenceKind.InputDependency))
            {
                names.Add("compiled from");
            }

            if (kinds.HasFlag(ResourceReferenceKind.Subasset))
            {
                names.Add("subasset");
            }

            if (kinds.HasFlag(ResourceReferenceKind.Data))
            {
                names.Add("resource data");
            }

            if (kinds.HasFlag(ResourceReferenceKind.PanoramaImage))
            {
                names.Add("image table");
            }

            if (kinds.HasFlag(ResourceReferenceKind.Manifest))
            {
                names.Add("manifest");
            }

            return string.Join(", ", names);
        }

        private void ApplyFilter()
        {
            FilterTimer.Stop();
            AppliedFilter = FilterBox.Text;

            UpdateTree(() =>
            {
                Tree.Nodes.Clear();

                var roots = new List<TreeNode>(Categories.Count);

                foreach (var category in Categories)
                {
                    var matching = Matching(category.Rows, AppliedFilter);

                    if (matching.Count == 0)
                    {
                        continue;
                    }

                    var expand = AppliedFilter.Length > 0 || ExpandedByUser.GetValueOrDefault(category.Name, !category.StartsCollapsed);
                    roots.Add(CreateGroupNode(category.Name, $"{category.Name} ({matching.Count})", category.Icon, [.. matching.Select(static row => row.Node)], expand));
                }

                Tree.Nodes.AddRange([.. roots]);
                ExpandFilled(roots);
            });
        }

        private void UpdateTree(Action update)
        {
            var selected = Tree.SelectedNode;
            var selectedKey = (selected?.Tag as Group)?.Key;
            var top = Tree.TopNode;
            var topKey = (top?.Tag as Group)?.Key;

            Rebuilding = true;

            try
            {
                Tree.BeginUpdate();

                try
                {
                    update();
                }
                finally
                {
                    Tree.EndUpdate();
                }

                var newSelected = selectedKey != null ? Tree.Nodes[selectedKey] : selected;

                if (newSelected != null && IsShown(newSelected))
                {
                    Tree.SelectedNode = newSelected;
                }

                var newTop = topKey != null ? Tree.Nodes[topKey] : top;

                if (newTop != null && IsShown(newTop))
                {
                    Tree.TopNode = newTop;
                }
            }
            finally
            {
                Rebuilding = false;
            }
        }

        private bool IsShown(TreeNode node) => node.TreeView == Tree && (node.Parent == null || node.Parent.IsExpanded);

        private void ShowAll(TreeNode moreNode, Group group)
        {
            var groupNode = moreNode.Parent;

            if (groupNode == null)
            {
                return;
            }

            ShowingAllRows.Add(group.Key);

            Tree.BeginUpdate();
            Fill(groupNode, group);
            Tree.EndUpdate();
        }

        private static void ExpandFilled(List<TreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Tag is Group { Filled: true } && !node.IsExpanded)
                {
                    node.Expand();
                }
            }
        }

        private TreeNode CreateGroupNode(string key, string text, int icon, TreeNode[] children, bool expand)
        {
            var group = new Group
            {
                Key = key,
                Children = children,
            };

            var node = new TreeNode(text)
            {
                Name = key,
                ImageIndex = icon,
                SelectedImageIndex = icon,
                Tag = group,
            };

            if (expand)
            {
                Fill(node, group);
            }
            else
            {
                node.Nodes.Add(new TreeNode());
            }

            return node;
        }

        private void Fill(TreeNode node, Group group)
        {
            node.Nodes.Clear();

            var children = ShowingAllRows.Contains(group.Key) || group.Children.Length <= MaxRowsShown
                ? group.Children
                : group.Children[..MaxRowsShown];

            foreach (var child in children)
            {
                child.Parent?.Nodes.Remove(child);
            }

            node.Nodes.AddRange(children);

            if (children.Length < group.Children.Length)
            {
                node.Nodes.Add(new TreeNode($"{group.Children.Length - children.Length} more, double click to show all")
                {
                    ForeColor = Themer.CurrentThemeColors.ContrastSoft,
                    Tag = new ShowAllRows(group),
                });
            }

            group.Filled = true;
        }

        private static List<Row> Matching(List<Row> rows, string filter)
        {
            return filter.Length == 0
                ? rows
                : rows.Where(row => row.Search.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private void LookUpNextRows()
        {
            const int ChunkSize = 200;

            if (IsDisposed || Disposing)
            {
                return;
            }

            var end = Math.Min(Rows.Count, LookedUpRows + ChunkSize);

            for (; LookedUpRows < end; LookedUpRows++)
            {
                var row = Rows[LookedUpRows];

                if (row.Category != RowCategory.Reference || FileExists(row.Reference.Name))
                {
                    continue;
                }

                MarkNotFound(row.Node);
            }

            if (LookedUpRows < Rows.Count)
            {
                BeginInvoke(LookUpNextRows);
            }
        }

        private void MarkNotFound(TreeNode node)
        {
            if (!MarkedNotFound.Add(node))
            {
                return;
            }

            node.ForeColor = Themer.CurrentThemeColors.Attention;
            node.ToolTipText = string.Concat(node.ToolTipText, NotFoundNote);
        }

        private bool FileExists(string name)
        {
            var compiled = GuiContext.FindFile(name + GameFileLoader.CompiledFileSuffix, logNotFound: false);

            if (compiled.PathOnDisk != null || compiled.PackageEntry != null)
            {
                return true;
            }

            var uncompiled = GuiContext.FindFile(name, logNotFound: false);

            if (uncompiled.PathOnDisk != null || uncompiled.PackageEntry != null)
            {
                return true;
            }

            return false;
        }

        private void OnFilterChanged(object? sender, EventArgs e)
        {
            FilterTimer.Stop();

            if (FilterBox.Text != AppliedFilter)
            {
                FilterTimer.Start();
            }
        }

        private void OnFilterTimerTick(object? sender, EventArgs e)
        {
            ApplyFilter();
        }

        private void OnBeforeExpand(object? sender, TreeViewCancelEventArgs e)
        {
            if (e.Node?.Tag is not Group { Filled: false } group)
            {
                return;
            }

            Tree.BeginUpdate();
            Fill(e.Node, group);
            Tree.EndUpdate();
        }

        private void OnExpandChanged(object? sender, TreeViewEventArgs e)
        {
            if (Rebuilding || AppliedFilter.Length > 0 || e.Node is not { Parent: null, Tag: Group group })
            {
                return;
            }

            ExpandedByUser[group.Key] = e.Node.IsExpanded;
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                Open(Tree.SelectedNode);
            }
        }

        private void OnNodeDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
        {
            Open(e.Node);
        }

        private void Open(TreeNode? node)
        {
            if (node == null)
            {
                return;
            }

            switch (node.Tag)
            {
                case ShowAllRows more:
                    ShowAll(node, more.Group);
                    return;

                case ResourceReference reference when Categorize(reference) == RowCategory.Reference:
                    if (Types.Viewers.Resource.OpenExternalReference(GuiContext, reference.Name))
                    {
                        return;
                    }

                    MarkNotFound(node);
                    Log.Warn(nameof(ResourceReferenceList), $"{reference.Name} was not found in the loaded files.");
                    return;

                default:
                    return;
            }
        }
    }
}
