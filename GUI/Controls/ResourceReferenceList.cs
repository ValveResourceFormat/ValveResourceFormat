using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

        private readonly VrfGuiContext guiContext;
        private readonly string? selfName;
        private readonly List<Row> usedByRows = [];
        private bool usedByReady;
        private readonly TreeViewDoubleBuffered tree;
        private readonly ThemedTextBox filterBox;
        private readonly List<Row> rows = [];
        private readonly List<Category> categories = [];
        private int lookedUpRows = -1;

        public ResourceReferenceList(VrfGuiContext guiContext, IReadOnlyList<ResourceReference> references, string? selfName = null)
        {
            ArgumentNullException.ThrowIfNull(references);

            this.guiContext = guiContext;
            this.selfName = selfName;
            Dock = DockStyle.Fill;

            tree = new TreeViewDoubleBuffered
            {
                Dock = DockStyle.Fill,
                ImageList = AppIcons.ImageList,
                HideSelection = false,
                ShowRootLines = true,
                ShowNodeToolTips = true,
            };

            tree.NodeMouseDoubleClick += OnNodeDoubleClick;
            tree.KeyDown += OnKeyDown;

            filterBox = new ThemedTextBox
            {
                PlaceholderText = "Filter references…",
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Multiline = false,
            };
            filterBox.TextChanged += OnFilterChanged;

            var filterPanel = new Panel
            {
                Padding = new Padding(this.AdjustForDPI(4)),
                Dock = DockStyle.Top,
                Height = filterBox.PreferredHeight + this.AdjustForDPI(8),
            };
            filterPanel.Controls.Add(filterBox);

            BuildRows(references);

            Controls.Add(tree);
            Controls.Add(filterPanel);

            ApplyFilter();

            Themer.ThemeControl(this);
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();

            BackColor = Themer.CurrentThemeColors.AppMiddle;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (lookedUpRows >= 0)
            {
                return;
            }

            lookedUpRows = 0;
            BeginInvoke(LookUpNextRows);

            if (selfName != null)
            {
                BeginInvoke(BuildUsedBy);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tree.NodeMouseDoubleClick -= OnNodeDoubleClick;
                tree.KeyDown -= OnKeyDown;
                filterBox.TextChanged -= OnFilterChanged;

                tree.Dispose();
                filterBox.Dispose();
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
                var extension = Path.GetExtension(reference.Name.AsSpan());

                if (extension.Length > 0)
                {
                    extension = extension[1..];
                }

                var category = Categorize(reference.Kinds);
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
                    categories.Add(group);
                }

                var row = new Row
                {
                    Reference = reference,
                    Category = category,
                    Node = node,
                    Search = string.Join(' ', reference.Name, typeName, node.ToolTipText),
                };

                group.Rows.Add(row);
                rows.Add(row);
            }

            // Files the viewer can open come first, then the content files, then the names that have no file
            categories.Sort(static (a, b) =>
            {
                var rank = SortRank(a).CompareTo(SortRank(b));
                return rank != 0 ? rank : string.CompareOrdinal(a.Name, b.Name);
            });

            foreach (var category in categories)
            {
                category.Rows.Sort(static (a, b) => string.Compare(a.Node.Text, b.Node.Text, StringComparison.OrdinalIgnoreCase));
            }
        }

        private static int SortRank(Category category) => category.Name switch
        {
            ContentCategory => 1,
            SubassetCategory => 2,
            _ => 0,
        };

        private static RowCategory Categorize(ResourceReferenceKind kinds)
        {
            const ResourceReferenceKind ContentOnly = ResourceReferenceKind.InputDependency | ResourceReferenceKind.Related;

            if (kinds == ResourceReferenceKind.Subasset)
            {
                return RowCategory.Subasset;
            }

            if ((kinds & ~ContentOnly) == ResourceReferenceKind.None)
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

        private static string ToolTip(ResourceReference reference, string typeName, RowCategory category)
        {
            var kinds = new List<string>(2);

            if (reference.Kinds.HasFlag(ResourceReferenceKind.External))
            {
                kinds.Add("external reference list");
            }

            if (reference.Kinds.HasFlag(ResourceReferenceKind.Child))
            {
                kinds.Add("child resource");
            }

            if (reference.Kinds.HasFlag(ResourceReferenceKind.Weak))
            {
                kinds.Add("weak reference");
            }

            if (reference.Kinds.HasFlag(ResourceReferenceKind.Related))
            {
                kinds.Add("related file");
            }

            if (reference.Kinds.HasFlag(ResourceReferenceKind.InputDependency))
            {
                kinds.Add("compiled from");
            }

            if (reference.Kinds.HasFlag(ResourceReferenceKind.Subasset))
            {
                kinds.Add("subasset");
            }

            if (reference.Kinds.HasFlag(ResourceReferenceKind.Data))
            {
                kinds.Add("resource data");
            }

            if (reference.Kinds.HasFlag(ResourceReferenceKind.PanoramaImage))
            {
                kinds.Add("image table");
            }

            if (reference.Kinds.HasFlag(ResourceReferenceKind.Manifest))
            {
                kinds.Add("manifest");
            }

            var tip = $"{reference.Name}\n{typeName}, found in: {string.Join(", ", kinds)}";

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

        private void ApplyFilter()
        {
            var filter = filterBox.Text;

            tree.BeginUpdate();
            tree.Nodes.Clear();

            if (selfName != null)
            {
                var matching = Matching(usedByRows, filter);
                var usedByNode = new TreeNode(usedByReady ? $"Used by ({matching.Count})" : "Used by (indexing\u2026)")
                {
                    ImageIndex = AppIcons.Icons["Folder"],
                    SelectedImageIndex = AppIcons.Icons["Folder"],
                };

                foreach (var row in matching)
                {
                    usedByNode.Nodes.Add(row.Node);
                }

                tree.Nodes.Add(usedByNode);

                if (filter.Length > 0 && matching.Count > 0)
                {
                    usedByNode.Expand();
                }
            }

            foreach (var category in categories)
            {
                var matching = Matching(category.Rows, filter);

                if (matching.Count == 0)
                {
                    continue;
                }

                var categoryNode = new TreeNode($"{category.Name} ({matching.Count})")
                {
                    ImageIndex = category.Icon,
                    SelectedImageIndex = category.Icon,
                };

                foreach (var row in matching)
                {
                    categoryNode.Nodes.Add(row.Node);
                }

                tree.Nodes.Add(categoryNode);

                if (filter.Length > 0 || !category.StartsCollapsed)
                {
                    categoryNode.Expand();
                }
            }

            tree.EndUpdate();
        }

        private static List<Row> Matching(List<Row> rows, string filter)
        {
            return filter.Length == 0
                ? rows
                : rows.Where(row => row.Search.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private void BuildUsedBy()
        {
            var index = ResourceReferenceIndex.ForContext(guiContext);

            if (index == null || selfName == null)
            {
                usedByReady = true;
                ApplyFilter();
                return;
            }

            index.BuildAsync().ContinueWith(_ =>
            {
                if (IsDisposed || Disposing || !IsHandleCreated)
                {
                    return;
                }

                try
                {
                    BeginInvoke(() => FillUsedBy(index));
                }
                catch (ObjectDisposedException)
                {
                    // the tab was closed while the index was building
                }
            }, TaskScheduler.Default);
        }

        private void FillUsedBy(ResourceReferenceIndex index)
        {
            const int MaxReferrers = 1000;

            if (IsDisposed || Disposing || selfName == null)
            {
                return;
            }

            var found = index.Find(selfName);
            var dimmed = Themer.CurrentThemeColors.ContrastSoft;

            foreach (var referrer in found.Take(MaxReferrers))
            {
                var reference = ResourceReference.Create(referrer, ResourceReferenceKind.External);
                var extension = Path.GetExtension(referrer.AsSpan());

                if (extension.Length > 0)
                {
                    extension = extension[1..];
                }

                var icon = AppIcons.GetImageIndexForExtension(extension);
                var node = new TreeNode(referrer)
                {
                    ImageIndex = icon,
                    SelectedImageIndex = icon,
                    Tag = reference,
                    ToolTipText = $"{referrer}\nReferences this file in its external reference list",
                };

                usedByRows.Add(new Row
                {
                    Reference = reference,
                    Category = RowCategory.Reference,
                    Node = node,
                    Search = referrer,
                });
            }

            if (found.Count > MaxReferrers)
            {
                var node = new TreeNode($"{found.Count - MaxReferrers} more not listed")
                {
                    ForeColor = dimmed,
                };

                usedByRows.Add(new Row
                {
                    Reference = default,
                    Category = RowCategory.Subasset,
                    Node = node,
                    Search = string.Empty,
                });
            }

            usedByReady = true;
            ApplyFilter();
        }

        // Looking a reference up walks every mounted package, so this runs in chunks between UI messages
        private void LookUpNextRows()
        {
            const int ChunkSize = 200;

            if (IsDisposed || Disposing)
            {
                return;
            }

            var end = Math.Min(rows.Count, lookedUpRows + ChunkSize);
            var attention = Themer.CurrentThemeColors.Attention;

            for (; lookedUpRows < end; lookedUpRows++)
            {
                var row = rows[lookedUpRows];

                if (row.Category != RowCategory.Reference || FileExists(row.Reference.Name))
                {
                    continue;
                }

                MarkNotFound(row.Node, attention);
            }

            if (lookedUpRows < rows.Count)
            {
                BeginInvoke(LookUpNextRows);
            }
        }

        private static void MarkNotFound(TreeNode node, Color attention)
        {
            node.ForeColor = attention;
            node.ToolTipText = string.Concat(node.ToolTipText, "\nNot found in the loaded files");
        }

        private bool FileExists(string name)
        {
            var context = guiContext;

            while (context != null)
            {
                var compiled = context.FindFile(name + GameFileLoader.CompiledFileSuffix, logNotFound: false);

                if (compiled.PathOnDisk != null || compiled.PackageEntry != null)
                {
                    return true;
                }

                var uncompiled = context.FindFile(name, logNotFound: false);

                if (uncompiled.PathOnDisk != null || uncompiled.PackageEntry != null)
                {
                    return true;
                }

                context = context.ParentGuiContext;
            }

            return Types.Viewers.Resource.TryFindMapPackage(guiContext, name, out _, out _);
        }

        private void OnFilterChanged(object? sender, EventArgs e)
        {
            ApplyFilter();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                Open(tree.SelectedNode);
            }
        }

        private void OnNodeDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
        {
            Open(e.Node);
        }

        private void Open(TreeNode? node)
        {
            if (node?.Tag is not ResourceReference reference || Categorize(reference.Kinds) != RowCategory.Reference)
            {
                return;
            }

            if (Types.Viewers.Resource.OpenExternalReference(guiContext, reference.Name))
            {
                return;
            }

            MarkNotFound(node, Themer.CurrentThemeColors.Attention);
            Log.Warn(nameof(ResourceReferenceList), $"{reference.Name} was not found in the loaded files.");
        }
    }
}
