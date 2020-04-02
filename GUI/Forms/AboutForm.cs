using System.Diagnostics;
using System.Windows.Forms;

namespace GUI.Forms
{
    public partial class AboutForm : Form
    {
        public AboutForm()
        {
            InitializeComponent();
        }

        private void LinkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("cmd", "/c start https://github.com/SteamDatabase/ValveResourceFormat")
            {
                CreateNoWindow = true,
            });
        }
    }
}
