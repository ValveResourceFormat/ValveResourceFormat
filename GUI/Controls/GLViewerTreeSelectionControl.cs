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
    class GLViewerTreeSelectionControl : UserControl
    {
        private const char PathSeparator = '/';

        private readonly CompactTreeView treeView;

        /// <summary>Raised with the paths of the checked items after one is checked or unchecked.</summary>
        public event Action<IEnumerable<string>>? CheckedPathsChanged;

        public GLViewerTreeSelectionControl(string name, IEnumerable<string> paths, bool initiallyChecked, bool foldGroups)
        {
            treeView = new CompactTreeView
            {
                AccessibleName = name,
                CheckBoxes = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                ShowLines = false,
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

            // A lone item at the top is never folded, so only several need the arrows that fold them
            treeView.ShowRootLines = treeView.Nodes.Count > 1 && treeView.Nodes.Cast<TreeNode>().Any(static node => node.Nodes.Count > 0);

            treeView.EndUpdate();

            treeView.LabelClicked += node => SetChecked(node, !node.Checked);
            treeView.AfterCheck += (_, e) =>
            {
                // Only a check by the user, rather than the ones made here, carries over to the items below
                if (e is { Action: not TreeViewAction.Unknown, Node: { } node })
                {
                    SetChecked(node, node.Checked);
                }
            };

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
                node.Checked = isChecked;
                nodes = node.Nodes;
            }
        }

        private void SetChecked(TreeNode node, bool isChecked)
        {
            node.Checked = isChecked;

            foreach (var child in AllBelow(node.Nodes))
            {
                child.Checked = isChecked;
            }

            CheckedPathsChanged?.Invoke([.. AllBelow(treeView.Nodes).Where(static node => node.Checked).Select(static node => node.Name)]);
        }

        private static IEnumerable<TreeNode> AllBelow(TreeNodeCollection nodes)
            => nodes.Cast<TreeNode>().SelectMany(static node => AllBelow(node.Nodes).Prepend(node));

        // The rows of the panel's other lists, rather than the taller rows of the file trees. Like the lists, a click
        // on a row checks it, and rows are never selected.
        internal sealed class CompactTreeView : TreeViewDoubleBuffered
        {
            private const int WM_LBUTTONDOWN = 0x0201;
            private const int WM_LBUTTONDBLCLK = 0x0203;
            private const int LogicalStateImageSize = 16;

            private readonly ImageList checkBoxImages;

            /// <summary>Raised when the label of an item is clicked.</summary>
            public event Action<TreeNode>? LabelClicked;

            // The tree draws its own check boxes larger than the lists do, and unlike them in dark mode, so it gets
            // theirs as the images of its check states
            public CompactTreeView()
            {
                using var graphics = Graphics.FromHwnd(IntPtr.Zero);
                var checkBoxSize = CheckBoxRenderer.GetGlyphSize(graphics, CheckBoxState.UncheckedNormal);

                // The tree copies the images into a list that is 16 pixels at the system DPI, which would stretch
                // them, so a check box sits unscaled in the middle of an image of that size
                var imageSize = (int)MathF.Round(LogicalStateImageSize * graphics.DpiX / 96f);
                var size = new Size(imageSize, imageSize);

                using var uncheckedImage = DrawCheckBox(size, checkBoxSize, VisualStyleElement.Button.CheckBox.UncheckedNormal);
                using var checkedImage = DrawCheckBox(size, checkBoxSize, VisualStyleElement.Button.CheckBox.CheckedNormal);

                checkBoxImages = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = size };
                checkBoxImages.Images.Add(uncheckedImage);
                checkBoxImages.Images.Add(checkedImage);

                // The list copies the images once it has a handle, and they are disposed after this
                _ = checkBoxImages.Handle;
                StateImageList = checkBoxImages;
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
                    && HitTest(e.Location).Location is TreeViewHitTestLocations.Label or TreeViewHitTestLocations.RightOfLabel)
                {
                    LabelClicked?.Invoke(node);
                }
            }

            // A quick second click is a click like the first, rather than a double click that folds the item. On a
            // check box, the tree would also check it again without the check being reported.
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
            private static Bitmap DrawCheckBox(Size size, Size checkBoxSize, VisualStyleElement element)
            {
                var image = new Bitmap(size.Width, size.Height);

                using var graphics = Graphics.FromImage(image);

                if (Application.IsDarkModeEnabled)
                {
                    element = VisualStyleElement.CreateElement("DarkMode_Explorer::Button", element.Part, element.State);
                }

                var position = new Point((size.Width - checkBoxSize.Width) / 2, (size.Height - checkBoxSize.Height) / 2);
                new VisualStyleRenderer(element).DrawBackground(graphics, new Rectangle(position, checkBoxSize));

                return image;
            }
        }
    }
}
