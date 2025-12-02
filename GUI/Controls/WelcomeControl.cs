using System.Windows.Forms;
using GUI.Forms;

namespace GUI.Controls
{
    public partial class WelcomeControl : UserControl
    {
        public WelcomeControl()
        {
            InitializeComponent();

            splitContainer.Panel2.Controls.Add(new ExplorerControl
            {
                Dock = DockStyle.Fill,
            });
        }

        private void fileAssociationButton_Click(object sender, EventArgs e)
        {
            SettingsControl.RegisterFileAssociation();

            fileAssociationButton.Text = "File association has been registered";
        }

        private void updateCheckButton_Click(object sender, EventArgs e)
        {
            AboutForm.ToggleAutomaticUpdateCheck();

            updateCheckButton.Text = "Automatic update checks have been enabled";
        }
    }
}
