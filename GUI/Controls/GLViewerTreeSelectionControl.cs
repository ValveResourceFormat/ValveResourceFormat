using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using GUI.Utils;

namespace GUI.Controls
{
    /// <summary>
    /// Checkable items in a tree, as Hammer lists its auto visgroups: an item of a path such as
    /// <c>Clip/Player Clip</c> is nested under the item of each group before it, which folds it away. Checking or
    /// unchecking an item does the same to every item below it.
    /// </summary>
    /// <remarks>
    /// The check box of an item is its image rather than a native tree check box, which the dark theme draws larger
    /// than the check boxes of the panel's lists, and which a tree loses track of in ways it does not report.
    /// </remarks>
    class GLViewerTreeSelectionControl : UserControl
    {
        private const char PathSeparator = '/';
        private const int UncheckedImage = 0;
        private const int CheckedImage = 1;

        private readonly CompactTreeView treeView;

        /// <summary>Raised with the paths of the checked items after one is checked or unchecked.</summary>
        public event Action<IEnumerable<string>>? CheckedPathsChanged;

        public GLViewerTreeSelectionControl(string name, IEnumerable<string> paths, bool initiallyChecked, bool foldGroups)
        {
            treeView = new CompactTreeView
            {
                AccessibleName = name,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                ShowRootLines = true,
            };

            treeView.BeginUpdate();

            foreach (var path in paths)
            {
                AddPath(path, initiallyChecked);
            }

            if (!foldGroups)
            {
                treeView.ExpandAll();
            }
            else if (treeView.Nodes is [var root])
            {
                // A lone item at the top holds all the others, so it lists its groups, each folded away
                root.Expand();
            }

            treeView.EndUpdate();

            treeView.ItemClicked += node => SetChecked(node, !IsChecked(node));

            var groupBox = new ThemedGroupBox
            {
                BackColor = SystemColors.Control,
                BorderColor = Color.FromArgb(230, 230, 230),
                BorderWidth = 2,
                CornerRadius = 5,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.Black,
                Margin = new Padding(0),
                Padding = new Padding(4, 0, 4, 0),
                Text = name,
            };
            groupBox.Controls.Add(treeView);

            Controls.Add(groupBox);
            Margin = new Padding(0);
            Padding = new Padding(3);
            Size = new Size(220, 154);
        }

        /// <summary>Checks an item and every item below it.</summary>
        /// <param name="path">The path of the item.</param>
        public void Check(string path)
        {
            if (treeView.Nodes.Find(path, searchAllChildren: true) is [var node, ..])
            {
                SetChecked(node, true);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                treeView.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();

            // The tree has its row height by now, as children are created first. It expands the nodes while it is
            // created, before all of them are in it, so its folding is only followed from here on.
            FitToRows();

            treeView.AfterExpand += (_, _) => FitToRows();
            treeView.AfterCollapse += (_, _) => FitToRows();
        }

        // As tall as the rows shown, so the box neither scrolls nor leaves space below them
        private void FitToRows()
        {
            Height += (CountShownRows(treeView.Nodes) * treeView.ItemHeight) - treeView.ClientSize.Height;
        }

        private static int CountShownRows(TreeNodeCollection nodes)
            => nodes.Cast<TreeNode>().Sum(static node => 1 + (node.IsExpanded ? CountShownRows(node.Nodes) : 0));

        // Each group of the path gets the item the paths below it are nested in
        private void AddPath(string path, bool isChecked)
        {
            var nodes = treeView.Nodes;
            var end = 0;

            while (end < path.Length)
            {
                end = path.IndexOf(PathSeparator, end + 1);
                end = end < 0 ? path.Length : end;

                var groupPath = path[..end];
                var node = nodes[groupPath] ?? nodes.Add(groupPath, groupPath[(groupPath.LastIndexOf(PathSeparator) + 1)..]);
                SetImage(node, isChecked);
                nodes = node.Nodes;
            }
        }

        private void SetChecked(TreeNode node, bool isChecked)
        {
            SetImage(node, isChecked);

            foreach (var child in AllBelow(node.Nodes))
            {
                SetImage(child, isChecked);
            }

            CheckedPathsChanged?.Invoke([.. AllBelow(treeView.Nodes).Where(IsChecked).Select(static node => node.Name)]);
        }

        private static bool IsChecked(TreeNode node) => node.ImageIndex == CheckedImage;

        private static void SetImage(TreeNode node, bool isChecked)
            => node.ImageIndex = node.SelectedImageIndex = isChecked ? CheckedImage : UncheckedImage;

        private static IEnumerable<TreeNode> AllBelow(TreeNodeCollection nodes)
            => nodes.Cast<TreeNode>().SelectMany(static node => AllBelow(node.Nodes).Prepend(node));

        // The rows of the panel's other lists, rather than the taller rows of the file trees. Like the lists, a click
        // on a row checks it, and rows are never selected.
        internal sealed class CompactTreeView : TreeViewDoubleBuffered
        {
            private const int WM_LBUTTONDOWN = 0x0201;
            private const int WM_LBUTTONDBLCLK = 0x0203;

            private readonly ImageList checkBoxImages;

            /// <summary>Raised when the check box or the label of an item is clicked.</summary>
            public event Action<TreeNode>? ItemClicked;

            public CompactTreeView()
            {
                using var graphics = Graphics.FromHwnd(IntPtr.Zero);
                var size = CheckBoxRenderer.GetGlyphSize(graphics, CheckBoxState.UncheckedNormal);

                using var uncheckedImage = DrawCheckBox(size, VisualStyleElement.Button.CheckBox.UncheckedNormal);
                using var checkedImage = DrawCheckBox(size, VisualStyleElement.Button.CheckBox.CheckedNormal);

                checkBoxImages = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = size };
                checkBoxImages.Images.Add(uncheckedImage);
                checkBoxImages.Images.Add(checkedImage);

                // The list copies the images once it has a handle, and they are disposed after this
                _ = checkBoxImages.Handle;
                ImageList = checkBoxImages;
            }

            protected override void OnCreateControl()
            {
                base.OnCreateControl();

                ItemHeight = this.AdjustForDPI(18);
            }

            protected override void OnBeforeSelect(TreeViewCancelEventArgs e)
            {
                e.Cancel = true;
                base.OnBeforeSelect(e);
            }

            protected override void OnNodeMouseClick(TreeNodeMouseClickEventArgs e)
            {
                base.OnNodeMouseClick(e);

                if (e is { Button: MouseButtons.Left, Node: { } node }
                    && HitTest(e.Location).Location is TreeViewHitTestLocations.Image or TreeViewHitTestLocations.Label or TreeViewHitTestLocations.RightOfLabel)
                {
                    ItemClicked?.Invoke(node);
                }
            }

            // A quick second click is a click like the first, rather than a double click that folds the item
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_LBUTTONDBLCLK)
                {
                    m.Msg = WM_LBUTTONDOWN;
                }

                base.WndProc(ref m);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    checkBoxImages.Dispose();
                }

                base.Dispose(disposing);
            }

            // In dark mode a check box is drawn by the button class of the dark theme, as the lists draw theirs
            private static Bitmap DrawCheckBox(Size size, VisualStyleElement element)
            {
                var image = new Bitmap(size.Width, size.Height);

                using var graphics = Graphics.FromImage(image);

                if (Application.IsDarkModeEnabled)
                {
                    element = VisualStyleElement.CreateElement("DarkMode_Explorer::Button", element.Part, element.State);
                }

                new VisualStyleRenderer(element).DrawBackground(graphics, new Rectangle(Point.Empty, size));

                return image;
            }
        }
    }
}
