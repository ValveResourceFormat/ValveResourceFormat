using System;
using System.Drawing;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls
{
    partial class LoadingFile : UserControl
    {
        public LoadingFile(string? fileName = null)
        {
            InitializeComponent();
            Themer.ThemeControl(this);

            // Show the wait cursor for the whole time the file is loading, but only when hovering this panel
            Cursor = Cursors.WaitCursor;

            if (!string.IsNullOrEmpty(fileName))
            {
                label1.Text = fileName;
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
