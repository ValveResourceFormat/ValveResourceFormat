using System.Drawing;
using System.Windows.Forms;

namespace GUI.Forms
{
    partial class LoadingFile : UserControl
    {
        public LoadingFile()
        {
            InitializeComponent();

            Dock = DockStyle.Fill;
        }

        //bit of bullshit in order to hide the border of the loading screen and make it seamless
        private void LoadingFile_Load(object sender, EventArgs e)
        {
            ForeColor = Parent.ForeColor;
            BackColor = Parent.BackColor;
        }
    }
}
