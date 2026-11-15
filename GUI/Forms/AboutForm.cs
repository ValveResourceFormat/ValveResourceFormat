using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.GLViewers;
using GUI.Utils;
using Svg.Skia;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.TextureDecoders;

namespace GUI.Forms
{
    partial class AboutForm : ThemedForm
    {
        public const string DiscordUrl = "https://discord.gg/s9QQ7Wg7r4";
        public const string IssuesUrl = "https://github.com/ValveResourceFormat/ValveResourceFormat/issues";

        public AboutForm()
        {
            InitializeComponent();
            ApplyTranslations();

            Icon = Program.MainForm.Icon;

            {
                using var svg = new SKSvg();
                using var svgResource = Program.Assembly.GetManifestResourceStream(Themer.CurrentThemeColors.ColorMode == SystemColorMode.Classic ? "GUI.Icons.Logo_light.svg" : "GUI.Icons.Logo.svg");
                Debug.Assert(svgResource is not null);
                svg.Load(svgResource);
                icon.Image = Themer.SvgToBitmap(svg, icon.Width, icon.Height);
            }

            // Start the decoder thread so that it fetches the opengl version and is ready for the version copy
            if (GLEnvironment.GpuRendererAndDriver == null && HardwareAcceleratedTextureDecoder.Decoder is GLTextureDecoder decoder)
            {
                decoder.StartThread();
            }

            currentVersionLabel.Text = Program.DisplayVersion;

            checkForUpdatesCheckbox.Checked = Settings.Config.Update.CheckAutomatically;

            updateChannelComboBox.Items.AddRange([Locale.About_ChannelStable, Locale.About_ChannelDev]);
            updateChannelComboBox.SelectedIndex = (int)Settings.Config.Update.Channel;

            if (UpdateInstaller.InstalledVersionText != null)
            {
                OnUpdateChecked();
                return;
            }

            CheckForUpdates(fetchAgain: true);
        }

        private async void CheckForUpdates(bool fetchAgain)
        {
            // The previous result stays visible until the check completes
            if (UpdateChecker.Offer != null)
            {
                OnUpdateChecked();
            }
            else
            {
                newVersionLabel.Text = Locale.About_CheckingForUpdates;
            }

            downloadButton.Text = Locale.About_CheckingForUpdates;
            downloadButton.Enabled = false;

            try
            {
                var check = fetchAgain ? UpdateChecker.RecheckForUpdates() : UpdateChecker.CheckForUpdates();
                await check.ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.Error(nameof(AboutForm), $"Failed to check for updates: {ex.Message}");

                if (!IsDisposed)
                {
                    newVersionLabel.Text = Locale.About_CheckFailed(ex.Message);
                    downloadButton.Text = Locale.About_NotAvailable;
                }

                return;
            }

            if (!IsDisposed)
            {
                OnUpdateChecked();
            }
        }

        private void OnUpdateChecked()
        {
            var installed = UpdateInstaller.InstalledVersionText;
            var offer = UpdateChecker.Offer;
            var build = offer?.Build;
            var newVersion = build?.DisplayText ?? Locale.About_NotAvailable;

            if (UpdateChecker.GetUpdateStatus() == UpdateChecker.UpdateStatus.LocalBuild)
            {
                newVersionLabel.Text = ":)";
                downloadButton.Text = Locale.About_UpToDate;
                downloadButton.Enabled = false;
            }
            else if (installed != null)
            {
                newVersionLabel.Text = GetInstalledText(UpdateInstaller.InstalledBuild);
                downloadButton.Text = Locale.Update_RestartToUpdate;
                downloadButton.Enabled = true;
            }
            else if (offer != null && build != null && offer.IsNewVersionAvailable)
            {
                newVersionLabel.Text = newVersion;

                if (build.Channel == Settings.UpdateChannel.Stable)
                {
                    downloadButton.Text = offer.IsNewer ? Locale.About_DownloadStable(build.Tag) : Locale.About_SwitchToStable(build.Tag);
                }
                else
                {
                    downloadButton.Text = offer.IsNewer ? Locale.About_DownloadDev(build.BuildNumberText) : Locale.About_SwitchToDev(build.BuildNumberText);
                }

                downloadButton.Enabled = true;
            }
            else
            {
                newVersionLabel.Text = newVersion;
                downloadButton.Text = build == null ? Locale.About_NotAvailable : Locale.About_UpToDate;
                downloadButton.Enabled = false;
            }

            if (UpdateInstaller.IsInstalling)
            {
                downloadButton.Enabled = false;
            }

            // Switching channels would not affect an installed update awaiting restart or an install in progress
            updateChannelComboBox.Enabled = installed == null && !UpdateInstaller.IsInstalling;

            if (offer != null && !string.IsNullOrEmpty(offer.ReleaseNotesUrl))
            {
                viewReleaseNotesButton.Text = Locale.About_ViewReleaseNotesFor(offer.ReleaseNotesVersion ?? string.Empty);
            }
        }

        private static string GetInstalledText(UpdateChecker.UpdateBuild? build)
        {
            if (build == null)
            {
                return string.Empty;
            }

            return build.Channel == Settings.UpdateChannel.Stable
                ? Locale.Update_InstalledStable(build.Tag)
                : Locale.Update_InstalledDev(build.BuildNumberText);
        }

        private void ApplyTranslations()
        {
            website.Text = Locale.About_Website;
            github.Text = Locale.About_GitHub;
            label3.Text = Locale.About_Disclaimer;
            discord.Text = Locale.About_Discord;
            licenses.Text = Locale.About_Licenses;
            copyVersion.Text = Locale.About_CopyVersion;
            checkForUpdatesCheckbox.Text = Locale.About_CheckAutomatically;
            updateChannelLabel.Text = Locale.About_UpdateChannel;
            downloadButton.Text = Locale.About_DownloadNewVersion;
            viewReleaseNotesButton.Text = Locale.About_ViewReleaseNotes;
            newVersionLabelText.Text = Locale.About_NewVersion;
            currentVersionLabelText.Text = Locale.About_CurrentVersion;
            groupBox1.Text = Locale.About_Title;
            groupBox2.Text = Locale.About_Version;
        }

        public void OnWebsiteClick(object sender, EventArgs e)
        {
            OpenUrl("https://s2v.app/");
        }

        private void OnGithubClick(object sender, EventArgs e)
        {
            OpenUrl("https://github.com/ValveResourceFormat/ValveResourceFormat");
        }

        private void OnDiscordClick(object sender, EventArgs e)
        {
            OpenUrl(DiscordUrl);
        }

        private void OnLicensesClick(object sender, EventArgs e)
        {
            using var stream = Program.Assembly.GetManifestResourceStream("GUI.Utils.THIRD_PARTY_NOTICES.txt");
            Debug.Assert(stream is not null);
            using var reader = new StreamReader(stream);

            using var form = new ThemedForm
            {
                Text = Locale.About_ThirdPartyLicenses,
                Icon = Icon,
                StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false,
                MinimizeBox = false,
                ClientSize = new Size(800, 600),
            };
            using var textBox = new CodeTextBox(reader.ReadToEnd(), HighlightLanguage.None);
            form.Controls.Add(textBox);
            form.ShowDialog(this);
        }

        private void OnViewReleaseNotesButtonClick(object sender, EventArgs e)
        {
            OpenUrl(UpdateChecker.Offer?.ReleaseNotesUrl ?? "https://s2v.app/release/");
        }

        private async void OnDownloadButtonClick(object sender, EventArgs e)
        {
            if (UpdateInstaller.InstalledVersionText != null)
            {
                Program.Restart(Environment.ProcessPath!);
                return;
            }

            try
            {
                // Returns at its first await, by which time the install is marked as running
                var install = UpdateInstaller.InstallAsync(this);
                OnUpdateChecked();

                await install.ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ErrorReport.ShowError(ex);
            }
            finally
            {
                if (!IsDisposed)
                {
                    OnUpdateChecked();
                }
            }
        }

        private void OnCheckForUpdatesCheckboxChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            Settings.Config.Update.CheckAutomatically = checkForUpdatesCheckbox.Checked;
            Settings.Config.Update.NextCheck = string.Empty;
        }

        private void OnUpdateChannelSelectedIndexChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            Settings.Config.Update.Channel = (Settings.UpdateChannel)updateChannelComboBox.SelectedIndex;

            CheckForUpdates(fetchAgain: false);
        }

        /// <summary>
        /// Opens an https url in the default browser, logging instead of throwing when that fails.
        /// </summary>
        public static void OpenUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                Log.Error(nameof(AboutForm), $"Refusing to open \"{url}\".");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
                {
                    UseShellExecute = true,
                });
            }
            catch (Win32Exception e)
            {
                Log.Error(nameof(AboutForm), $"Failed to open \"{url}\": {e.Message}");
            }
        }

        private void OnCopyVersionClick(object sender, EventArgs e)
        {
            var output = new StringBuilder(192);
            output.Append(Program.DisplayVersion);
            output.Append(CultureInfo.InvariantCulture, $" on {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");

            if (GLEnvironment.GpuRendererAndDriver != null)
            {
                output.Append(CultureInfo.InvariantCulture, $" ({GLEnvironment.GpuRendererAndDriver})");
            }

            AppClipboard.SetText(output.ToString());

            copyVersion.Text = Locale.About_Copied;
        }
    }
}
