using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GUI.Types.PackageViewer;
using GUI.Utils;

namespace GUI.Controls
{
    partial class LoadingFile : UserControl
    {
        private Bitmap? iconBitmap;

        public LoadingFile(string? fileName = null)
        {
            InitializeComponent();
            Themer.ThemeControl(this);

            // Show the wait cursor for the whole time the file is loading, but only when hovering this panel
            Cursor = Cursors.WaitCursor;

            if (!string.IsNullOrEmpty(fileName))
            {
                label1.Text = fileName;
                AddLargeIcon(fileName);
            }

            // The panel auto-sizes to fit the file name label. Anchor=None only keeps it centered when the
            // parent resizes, not when the panel itself grows, so a long file name would push it off center.
            // Re-center it ourselves whenever its size changes.
            tableLayoutPanel1.SizeChanged += (_, _) => CenterContent();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            CenterContent();
        }

        private void CenterContent()
        {
            var location = new Point(
                Math.Max(0, (ClientSize.Width - tableLayoutPanel1.Width) / 2),
                Math.Max(0, (ClientSize.Height - tableLayoutPanel1.Height) / 2));

            if (tableLayoutPanel1.Location != location)
            {
                tableLayoutPanel1.Location = location;
            }
        }

        // Show the file-type icon large (the same SVG icon the grid view uses), above the file name.
        private void AddLargeIcon(string fileName)
        {
            var typeName = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
            iconBitmap = TreeViewWithSearchResults.GetTypeIconBitmap(typeName, this.AdjustForDPI(72));

            var iconBox = new PictureBox
            {
                Image = iconBitmap,
                SizeMode = PictureBoxSizeMode.AutoSize,
                Anchor = AnchorStyles.None,
                Margin = new Padding(4, 0, 4, 8),
            };

            // Insert a new top row for the icon and push the label/progress bar down into the rows below it.
            tableLayoutPanel1.RowCount = 3;
            tableLayoutPanel1.RowStyles.Insert(0, new RowStyle(SizeType.AutoSize));
            tableLayoutPanel1.SetRow(label1, 1);
            tableLayoutPanel1.SetRow(progressBar1, 2);
            tableLayoutPanel1.Controls.Add(iconBox, 0, 0);
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();


            BackColor = Themer.CurrentThemeColors.AppMiddle;
            ForeColor = Themer.CurrentThemeColors.Contrast;

            label1.BackColor = Themer.CurrentThemeColors.AppMiddle;
            progressBar1.BackColor = Themer.CurrentThemeColors.AppMiddle;
            tableLayoutPanel1.BackColor = Themer.CurrentThemeColors.AppMiddle;

            Dock = DockStyle.Fill;
        }
    }
}
