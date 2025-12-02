using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using GUI.Types.Renderer;
using GUI.Utils;

namespace GUI.Forms
{
    partial class AboutForm : Form
    {
        public AboutForm()
        {
            InitializeComponent();

            // Start the decoder thread so that it fetches the opengl version and is ready for the version copy
            if (GLEnvironment.GpuRendererAndDriver == null && HardwareAcceleratedTextureDecoder.Decoder is GLTextureDecoder decoder)
            {
                decoder.StartThread();
            }

            currentVersionLabel.Text += Application.ProductVersion[..16].Replace('+', ' ');

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

        private void OnWebsiteClick(object sender, EventArgs e)
        {
            OpenUrl("https://valveresourceformat.github.io/");
        }

        private void OnGithubClick(object sender, EventArgs e)
        {
            OpenUrl("https://github.com/ValveResourceFormat/ValveResourceFormat");
        }

        private void OnReleasesClick(object sender, EventArgs e)
        {
            OpenUrl("https://github.com/ValveResourceFormat/ValveResourceFormat/releases");
        }

        private void OnKeybindsClick(object sender, EventArgs e)
        {
            OpenUrl("https://github.com/ValveResourceFormat/ValveResourceFormat?tab=readme-ov-file#gui-keybinds");
        }

        private void OnViewReleaseNotesButtonClick(object sender, EventArgs e)
        {
            OpenUrl(UpdateChecker.ReleaseNotesUrl);
        }

        private void OnDownloadButtonClick(object sender, EventArgs e)
        {
            OpenUrl("https://valveresourceformat.github.io/");
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

        private static void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo("cmd", $"/c start {url}")
            {
                CreateNoWindow = true,
            });
        }

        private void OnCopyVersionClick(object sender, EventArgs e)
        {
            var output = new StringBuilder(192);
            var version = Application.ProductVersion;
            var versionPlus = version.IndexOf('+', StringComparison.Ordinal);

            if (versionPlus > 0)
            {
                output.Append(version[..versionPlus]);
                output.Append(' ');
                output.Append(version[(versionPlus + 1)..(versionPlus + 10)]);
            }
            else
            {
                output.Append(version);
            }

            output.Append(CultureInfo.InvariantCulture, $" on {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");

            if (GLEnvironment.GpuRendererAndDriver != null)
            {
                output.Append(CultureInfo.InvariantCulture, $" ({GLEnvironment.GpuRendererAndDriver})");
            }

            Clipboard.SetText(output.ToString());
        }
    }
}
