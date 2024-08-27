using System.Diagnostics;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Forms
{
    public partial class UpdateAvailableForm : Form
    {
        public UpdateAvailableForm()
        {
            InitializeComponent();

            var version = Application.ProductVersion;
            var versionPlus = version.IndexOf('+', StringComparison.InvariantCulture); // Drop the git commit

            currentVersionLabel.Text += versionPlus > 0 ? version[..versionPlus] : version;

            if (!string.IsNullOrEmpty(UpdateChecker.NewVersion))
            {
                newVersionLabel.Text += UpdateChecker.IsNewVersionStableBuild ? UpdateChecker.NewVersion : $"Dev build {UpdateChecker.NewVersion}";
            }

            if (!UpdateChecker.IsNewVersionAvailable)
            {
                Text = "Up to date";
                newVersionLabel.Enabled = false;
                downloadButton.Enabled = false;
            }

            if (string.IsNullOrEmpty(UpdateChecker.ReleaseNotesUrl))
            {
                viewReleaseNotesButton.Enabled = false;
            }
            else
            {
                viewReleaseNotesButton.Text = $"View release notes for {UpdateChecker.ReleaseNotesVersion}";
            }

            checkForUpdatesCheckbox.Checked = Settings.Config.Update.CheckAutomatically;
        }

        private void OnViewReleaseNotesButtonClick(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo("cmd", $"/c start {UpdateChecker.ReleaseNotesUrl}")
            {
                CreateNoWindow = true,
            });
        }

        private void OnDownloadButtonClick(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo("cmd", "/c start https://valveresourceformat.github.io/")
            {
                CreateNoWindow = true,
            });
        }

        private void OnCheckForUpdatesCheckboxChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            ToggleAutomaticUpdateCheck(checkForUpdatesCheckbox.Checked);
        }

        public static void ToggleAutomaticUpdateCheck(bool enabled = true)
        {
            Settings.Config.Update.CheckAutomatically = enabled;
            Settings.Config.Update.LastCheck = string.Empty;
            Settings.Config.Update.UpdateAvailable = UpdateChecker.IsNewVersionAvailable && Settings.Config.Update.CheckAutomatically;
        }
    }
}
