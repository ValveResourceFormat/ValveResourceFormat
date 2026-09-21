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
        public AboutForm()
        {
            InitializeComponent();

            LayoutCreditsGroup();

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

            updateChannelComboBox.Items.AddRange(Enum.GetNames<Settings.UpdateChannel>());
            updateChannelComboBox.SelectedIndex = (int)Settings.Config.Update.Channel;

            CheckForUpdates();
        }

        // label3 grew to fit the extra credit lines, so reflow the button row below it, the
        // group box around both, and the dialog's own height around its actual rendered size
        // instead of the fixed values from the original (shorter) layout.
        private void LayoutCreditsGroup()
        {
            tableLayoutPanel1.Top = label3.Bottom + 12;
            groupBox1.Height = tableLayoutPanel1.Bottom + 16;

            groupBox2.Top = groupBox1.Bottom + 12;

            ClientSize = new Size(ClientSize.Width, groupBox2.Bottom + 16);
            MinimumSize = Size;
        }

        private async void CheckForUpdates()
        {
            newVersionLabel.Text = "Checking for updates…";
            downloadButton.Enabled = false;

            try
            {
                await UpdateChecker.CheckForUpdates().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                var message = $"Failed to check for updates: {ex.Message}";
                Log.Error(nameof(AboutForm), message);

                if (!IsDisposed)
                {
                    newVersionLabel.Text = message;
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
            var newVersion = UpdateChecker.NewVersionText;

            if (installed != null)
            {
                newVersionLabel.Text = $"{installed} (installed)";
                downloadButton.Text = "Restart to update";
                downloadButton.Enabled = true;
            }
            else if (UpdateChecker.IsNewVersionAvailable)
            {
                newVersionLabel.Text = newVersion;
                downloadButton.Text = UpdateChecker.IsNewer
                    ? $"Download {newVersion}"
                    : $"Switch to {(UpdateChecker.IsNewVersionStableBuild ? "stable " : "")}{newVersion}";
                downloadButton.Enabled = true;
            }
            else
            {
                newVersionLabel.Text = newVersion;
                downloadButton.Text = UpdateChecker.NewVersion == null ? "Not available" : "Up to date";
                downloadButton.Enabled = false;
            }

            // Switching channels would not change what the pending restart installs
            updateChannelComboBox.Enabled = installed == null;

            if (!string.IsNullOrEmpty(UpdateChecker.ReleaseNotesUrl))
            {
                viewReleaseNotesButton.Text = $"View release notes for {UpdateChecker.ReleaseNotesVersion}";
            }
        }

        public void OnWebsiteClick(object sender, EventArgs e)
        {
            OpenUrl("https://h6rd.github.io/Dota2PornFxWeb/");
        }

        private void OnGithubClick(object sender, EventArgs e)
        {
            OpenUrl("https://github.com/J0nathan550");
        }

        private void OnDiscordClick(object sender, EventArgs e)
        {
            OpenUrl("https://discord.com/invite/PBvG8D9MxT");
        }

        private void OnLicensesClick(object sender, EventArgs e)
        {
            using var stream = Program.Assembly.GetManifestResourceStream("GUI.Utils.THIRD_PARTY_NOTICES.txt");
            Debug.Assert(stream is not null);
            using var reader = new StreamReader(stream);

            using var form = new ThemedForm
            {
                Text = "Third party licenses",
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
            OpenUrl(UpdateChecker.ReleaseNotesUrl ?? "https://github.com/ValveResourceFormat/ValveResourceFormat/releases");
        }

        private async void OnDownloadButtonClick(object sender, EventArgs e)
        {
            if (UpdateInstaller.InstalledVersionText != null)
            {
                UpdateInstaller.Restart();
                return;
            }

            downloadButton.Enabled = false;

            try
            {
                await UpdateInstaller.InstallAsync(this).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Program.ShowError(ex);
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

            CheckForUpdates();
        }

        private static void OpenUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException($"Refusing to open \"{url}\".", nameof(url));
            }

            Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
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

            copyVersion.Text = "Copied!";
        }
    }
}
