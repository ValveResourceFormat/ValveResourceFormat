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
