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


            BackColor = Themer.CurrentThemeColors.AppSoft;
            ForeColor = Themer.CurrentThemeColors.Contrast;

            label1.BackColor = Themer.CurrentThemeColors.AppSoft;
            progressBar1.BackColor = Themer.CurrentThemeColors.AppSoft;
            tableLayoutPanel1.BackColor = Themer.CurrentThemeColors.AppSoft;

            Dock = DockStyle.Fill;
        }
    }
}
