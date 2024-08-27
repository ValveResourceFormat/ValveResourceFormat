using System.Windows.Forms;

namespace GUI.Forms
{
    partial class LoadingFile : UserControl
    {
        public LoadingFile()
        {
            InitializeComponent();
            MainForm.ThemeManager.RegisterControl(this);
        }
    }
}
