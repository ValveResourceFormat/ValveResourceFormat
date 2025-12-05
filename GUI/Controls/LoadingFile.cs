using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls
{
    partial class LoadingFile : UserControl
    {
        public LoadingFile()
        {
            InitializeComponent();
            Themer.ThemeControl(this);
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
