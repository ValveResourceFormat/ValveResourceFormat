using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls
{
    public partial class WelcomeControl : UserControl
    {
        internal WelcomeControl(ExplorerControl explorerControl)
        {
            InitializeComponent();

            splitContainer.Panel2.Controls.Add(explorerControl);
        }

        protected override void CreateHandle()
        {
            base.CreateHandle();

            BackColor = Themer.CurrentThemeColors.AppMiddle;
        }

        private async void fileAssociationButton_Click(object sender, EventArgs e)
        {
            await SettingsControl.RegisterFileAssociationAsync().ConfigureAwait(true);

            fileAssociationButton.Text = "File association has been registered";
        }
    }
}
