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
            ApplyTranslations();

            splitContainer.Panel2.Controls.Add(explorerControl);

            logo.Image = Themer.SvgToBitmap(AppIcons.ExtensionSVGS["Logo"], logo.Width, logo.Height);

            versionLabel.Text = Program.DisplayVersion;

            var installText = AppInstaller.GetInstallButtonText();
            installLayout.Visible = installText != null;
            installButton.Text = installText;
        }

        private void ApplyTranslations()
        {
            explorerHeading.Text = Locale.Welcome_Explorer;
            explorerLabel.Text = Locale.Welcome_Explorer_Text;
            updatesHeading.Text = Locale.Welcome_Updates;
            updatesLabel.Text = Locale.Welcome_Updates_Text;
            installHeading.Text = Locale.Welcome_Install;
            installLabel.Text = Locale.Welcome_Install_Text;
            installButton.Text = Locale.Menu_InstallApp;
            helpHeading.Text = Locale.Welcome_Help;
            helpLabel.Text = Locale.Welcome_Help_Text;
            discordLink.Text = Locale.Welcome_JoinDiscord;
            issuesLink.Text = Locale.Welcome_ReportOnGitHub;
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
            await AppInstaller.InstallFromUiAsync().ConfigureAwait(true);

            installLayout.Visible = false;
        }
    }
}
