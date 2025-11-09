using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using GUI.Types.Renderer;

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

            labelVersion.Text = $"Version: {Application.ProductVersion[..16]}";
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
