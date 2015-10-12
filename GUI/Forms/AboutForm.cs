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

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            var info = new ProcessStartInfo("https://github.com/SteamDatabase/ValveResourceFormat");
            Process.Start(info);
        }
    }
}
