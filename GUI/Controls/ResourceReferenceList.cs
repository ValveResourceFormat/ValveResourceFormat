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

        /// <summary>A top level node, whose children are only added once it is first expanded.</summary>
        private sealed class Group
        {
            public required string Key { get; init; }
            public required TreeNode[] Children { get; init; }
            public bool Filled { get; set; }
        }

        /// <summary>A referrer that is only found in a map package, which opens through that package.</summary>
        private sealed record PackagedReference(ResourceReference Reference, string Package);

        private const string UsedByKey = "\0UsedBy";
        private const string NotFoundNote = "\nNot found in the loaded files";

        private readonly VrfGuiContext guiContext;
        private readonly string? selfName;
        private readonly ResourceReferenceIndex? usedByIndex;
        private readonly List<Row> usedByRows = [];
        private int usedByCount;
        private bool usedByReady;
        private readonly TreeViewDoubleBuffered tree;
        private readonly ThemedTextBox filterBox;
        private readonly System.Windows.Forms.Timer filterTimer = new() { Interval = 150 };
        private readonly List<Row> rows = [];
        private readonly List<Category> categories = [];
        private readonly Dictionary<string, bool> expandedByUser = new(StringComparer.Ordinal);
        private string appliedFilter = string.Empty;
        private bool rebuilding;
        private int lookedUpRows = -1;

        public ResourceReferenceList(VrfGuiContext guiContext, IReadOnlyList<ResourceReference> references, string? selfName = null)
        {
            ArgumentNullException.ThrowIfNull(references);

            this.guiContext = guiContext;
            this.selfName = selfName;
            usedByIndex = selfName == null ? null : ResourceReferenceIndex.ForContext(guiContext);
            _ = usedByIndex?.BuildAsync();
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
            tree.BeforeExpand += OnBeforeExpand;
            tree.AfterExpand += OnExpandChanged;
            tree.AfterCollapse += OnExpandChanged;

            filterBox = new ThemedTextBox
            {
                PlaceholderText = "Filter references…",
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Multiline = false,
            };
            filterBox.TextChanged += OnFilterChanged;
            filterTimer.Tick += OnFilterTimerTick;

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

            if (usedByIndex != null)
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
                tree.BeforeExpand -= OnBeforeExpand;
                tree.AfterExpand -= OnExpandChanged;
                tree.AfterCollapse -= OnExpandChanged;
                filterBox.TextChanged -= OnFilterChanged;
                filterTimer.Tick -= OnFilterTimerTick;

                filterTimer.Dispose();
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

        private static RowCategory Categorize(ResourceReference reference)
        {
            const ResourceReferenceKind ContentOnly = ResourceReferenceKind.InputDependency | ResourceReferenceKind.Related;

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

        private static string ToolTip(ResourceReference reference, string typeName, RowCategory category)
        {
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
            filterTimer.Stop();
            appliedFilter = filterBox.Text;

            UpdateTree(() =>
            {
                tree.Nodes.Clear();

                var roots = new List<TreeNode>(categories.Count + 1);
                var usedByNode = CreateUsedByNode();

                if (usedByNode != null)
                {
                    roots.Add(usedByNode);
                }

                foreach (var category in categories)
                {
                    var matching = Matching(category.Rows, appliedFilter);

                    if (matching.Count == 0)
                    {
                        continue;
                    }

                    var expand = appliedFilter.Length > 0 || expandedByUser.GetValueOrDefault(category.Name, !category.StartsCollapsed);
                    roots.Add(CreateGroupNode(category.Name, $"{category.Name} ({matching.Count})", category.Icon, matching, expand));
                }

                tree.Nodes.AddRange([.. roots]);
                ExpandFilled(roots);
            });
        }

        private void UpdateUsedByNode()
        {
            UpdateTree(() =>
            {
                tree.Nodes.RemoveByKey(UsedByKey);

                var usedByNode = CreateUsedByNode();

                if (usedByNode != null)
                {
                    tree.Nodes.Insert(0, usedByNode);
                    ExpandFilled([usedByNode]);
                }
            });
        }

        private void UpdateTree(Action update)
        {
            var selected = tree.SelectedNode;
            var selectedKey = (selected?.Tag as Group)?.Key;
            var top = tree.TopNode;
            var topKey = (top?.Tag as Group)?.Key;

            rebuilding = true;

            try
            {
                tree.BeginUpdate();

                try
                {
                    update();
                }
                finally
                {
                    tree.EndUpdate();
                }

                var newSelected = selectedKey != null ? tree.Nodes[selectedKey] : selected;

                if (newSelected != null && IsShown(newSelected))
                {
                    tree.SelectedNode = newSelected;
                }

                var newTop = topKey != null ? tree.Nodes[topKey] : top;

                if (newTop != null && IsShown(newTop))
                {
                    tree.TopNode = newTop;
                }
            }
            finally
            {
                rebuilding = false;
            }
        }

        private bool IsShown(TreeNode node) => node.TreeView == tree && (node.Parent == null || node.Parent.IsExpanded);

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

        private static TreeNode CreateGroupNode(string key, string text, int icon, List<Row> children, bool expand)
        {
            var group = new Group
            {
                Key = key,
                Children = [.. children.Select(static row => row.Node)],
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

        private static void Fill(TreeNode node, Group group)
        {
            node.Nodes.Clear();

            foreach (var child in group.Children)
            {
                child.Parent?.Nodes.Remove(child);
            }

            node.Nodes.AddRange(group.Children);
            group.Filled = true;
        }

        private TreeNode? CreateUsedByNode()
        {
            if (usedByIndex == null)
            {
                return null;
            }

            var folderIcon = AppIcons.Icons["Folder"];

            if (!usedByReady)
            {
                return new TreeNode("Used by (indexing…)")
                {
                    Name = UsedByKey,
                    ImageIndex = folderIcon,
                    SelectedImageIndex = folderIcon,
                };
            }

            var matching = Matching(usedByRows, appliedFilter);
            var count = appliedFilter.Length == 0 ? usedByCount : matching.Count(static row => row.Category == RowCategory.Reference);

            if (matching.Count == 0)
            {
                return null;
            }

            var expand = appliedFilter.Length > 0 || expandedByUser.GetValueOrDefault(UsedByKey);

            return CreateGroupNode(UsedByKey, $"Used by ({count})", folderIcon, matching, expand);
        }

        private static List<Row> Matching(List<Row> rows, string filter)
        {
            return filter.Length == 0
                ? rows
                : rows.Where(row => row.Search.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private void BuildUsedBy()
        {
            usedByIndex?.BuildAsync().ContinueWith(_ =>
            {
                if (IsDisposed || Disposing || !IsHandleCreated)
                {
                    return;
                }

                try
                {
                    BeginInvoke(FillUsedBy);
                }
                catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException)
                {
                    // the tab was closed while the index was building
                }
            }, TaskScheduler.Default);
        }

        private void FillUsedBy()
        {
            const int MaxReferrers = 1000;

            if (IsDisposed || Disposing || selfName == null || usedByIndex == null)
            {
                return;
            }

            var found = FoldIntoModels(usedByIndex, usedByIndex.Find(selfName));
            var dimmed = Themer.CurrentThemeColors.ContrastSoft;

            foreach (var (referrer, through) in found.Take(MaxReferrers))
            {
                var reference = ResourceReference.Create(referrer.Name, referrer.Kinds);
                var extension = Path.GetExtension(referrer.Name.AsSpan());

                if (extension.Length > 0)
                {
                    extension = extension[1..];
                }

                var icon = AppIcons.GetImageIndexForExtension(extension);
                var node = new TreeNode(referrer.Name)
                {
                    ImageIndex = icon,
                    SelectedImageIndex = icon,
                    Tag = referrer.Package == null ? reference : new PackagedReference(reference, referrer.Package),
                    ToolTipText = (through, referrer.Package) switch
                    {
                        ({ } mesh, _) => $"{referrer.Name}\nUses this file through {mesh}",
                        (_, { } package) => $"{referrer.Name}\nReferences this file, found in its {KindNames(referrer.Kinds)}\nIn map package {package}",
                        _ => $"{referrer.Name}\nReferences this file, found in its {KindNames(referrer.Kinds)}",
                    },
                };

                usedByRows.Add(new Row
                {
                    Reference = reference,
                    Category = RowCategory.Reference,
                    Node = node,
                    Search = referrer.Name,
                });
            }

            if (found.Count == 0)
            {
                var node = new TreeNode($"Not referenced by any file in the {usedByIndex.SearchedCount} indexed packages and map folders")
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

            usedByCount = found.Count;
            usedByReady = true;
            UpdateUsedByNode();
        }

        /// <summary>
        /// Replaces meshes and animation groups, which only their models load, with the models that use them.
        /// An intermediate that no model uses stays as it is.
        /// </summary>
        private static List<(ResourceReferenceIndex.Referrer Referrer, string? Through)> FoldIntoModels(ResourceReferenceIndex index, IReadOnlyList<ResourceReferenceIndex.Referrer> referrers)
        {
            static bool IsIntermediate(string name) => name.EndsWith(".vmesh", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".vagrp", StringComparison.OrdinalIgnoreCase);

            var folded = new List<(ResourceReferenceIndex.Referrer Referrer, string? Through)>(referrers.Count);
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var referrer in referrers)
            {
                if (!IsIntermediate(referrer.Name))
                {
                    listed.Add(referrer.Name);
                    folded.Add((referrer, null));
                }
            }

            foreach (var referrer in referrers)
            {
                if (!IsIntermediate(referrer.Name))
                {
                    continue;
                }

                var models = index.Find(referrer.Name).Where(static model => model.Name.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase)).ToList();

                if (models.Count == 0)
                {
                    folded.Add((referrer, null));
                    continue;
                }

                foreach (var model in models)
                {
                    if (listed.Add(model.Name))
                    {
                        folded.Add((model, referrer.Name));
                    }
                }
            }

            folded.Sort(static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Referrer.Name, b.Referrer.Name));

            return folded;
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

            if (!node.ToolTipText.EndsWith(NotFoundNote, StringComparison.Ordinal))
            {
                node.ToolTipText = string.Concat(node.ToolTipText, NotFoundNote);
            }
        }

        private bool FileExists(string name)
        {
            var compiled = guiContext.FindFile(name + GameFileLoader.CompiledFileSuffix, logNotFound: false);

            if (compiled.PathOnDisk != null || compiled.PackageEntry != null)
            {
                return true;
            }

            var uncompiled = guiContext.FindFile(name, logNotFound: false);

            if (uncompiled.PathOnDisk != null || uncompiled.PackageEntry != null)
            {
                return true;
            }

            return Types.Viewers.Resource.TryFindMapPackage(guiContext, name, out _, out _);
        }

        private void OnFilterChanged(object? sender, EventArgs e)
        {
            filterTimer.Stop();

            if (filterBox.Text != appliedFilter)
            {
                filterTimer.Start();
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

            tree.BeginUpdate();
            Fill(e.Node, group);
            tree.EndUpdate();
        }

        private void OnExpandChanged(object? sender, TreeViewEventArgs e)
        {
            if (rebuilding || appliedFilter.Length > 0 || e.Node is not { Parent: null, Tag: Group group })
            {
                return;
            }

            expandedByUser[group.Key] = e.Node.IsExpanded;
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
            if (node?.Tag is PackagedReference packaged)
            {
                if (!Types.Viewers.Resource.OpenFileInPackage(packaged.Package, packaged.Reference.Name + GameFileLoader.CompiledFileSuffix))
                {
                    MarkNotFound(node, Themer.CurrentThemeColors.Attention);
                }

                return;
            }

            if (node?.Tag is not ResourceReference reference || Categorize(reference) != RowCategory.Reference)
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
