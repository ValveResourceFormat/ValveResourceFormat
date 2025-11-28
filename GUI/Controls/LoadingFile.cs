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

            Dock = DockStyle.Fill;
        }
    }
}
