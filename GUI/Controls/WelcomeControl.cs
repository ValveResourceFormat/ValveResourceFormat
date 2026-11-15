using System.Windows.Forms;
using GUI.Forms;
using GUI.Utils;

namespace GUI.Controls
{
    public partial class WelcomeControl : UserControl
    {
        internal WelcomeControl(ExplorerControl explorerControl)
        {
            InitializeComponent();

            splitContainer.Panel2.Controls.Add(explorerControl);

            logo.Image = Themer.SvgToBitmap(AppIcons.ExtensionSVGS["Logo"], logo.Width, logo.Height);

            versionLabel.Text = Program.DisplayVersion;

            var installText = SettingsControl.GetInstallText();
            installLayout.Visible = installText != null;
            installButton.Text = installText;
        }

        protected override void CreateHandle()
        {
            base.CreateHandle();

            var colors = Themer.CurrentThemeColors;

            BackColor = colors.AppMiddle;
            versionLabel.ForeColor = colors.ContrastSoft;

            installButton.BackColor = colors.Accent;
            installButton.HoveredBackColor = colors.HoverAccent;
            installButton.ClickedBackColor = colors.HoverAccent;
            installButton.ForeColor = colors.Contrast;

            ThemeLink(discordLink);
            ThemeLink(issuesLink);
        }

        private static void ThemeLink(LinkLabel link)
        {
            var colors = Themer.CurrentThemeColors;

            link.LinkColor = colors.Contrast;
            link.ActiveLinkColor = colors.Accent;
            link.VisitedLinkColor = colors.Contrast;
        }

        private void discordLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            AboutForm.OpenUrl(AboutForm.DiscordUrl);
        }

        private void issuesLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            AboutForm.OpenUrl(AboutForm.IssuesUrl);
        }

        private async void installButton_Click(object sender, EventArgs e)
        {
            await SettingsControl.InstallAsync().ConfigureAwait(true);

            installLayout.Visible = false;
        }
    }
}
