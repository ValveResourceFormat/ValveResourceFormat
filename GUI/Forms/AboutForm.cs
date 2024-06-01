using System.Diagnostics;
using System.Windows.Forms;
using GUI.Types.Renderer;
using GUI.Utils;
using ValveResourceFormat.Utils;

namespace GUI.Forms
{
    partial class AboutForm : Form
    {
        public AboutForm()
        {
            InitializeComponent();

            // Start the decoder thread so that it fetches the opengl version and is ready for the version copy
            if (Settings.GpuRendererAndDriver == null && HardwareAcceleratedTextureDecoder.Decoder is GLTextureDecoder decoder)
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
            OpenUrl("https://github.com/ValveResourceFormat/ValveResourceFormat/wiki/Source-2-Viewer-Keybinds");
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
            var version = $"{Application.ProductVersion.Replace('+', ' ')} on {Environment.OSVersion}";

            if (Utils.Settings.GpuRendererAndDriver != null)
            {
                version += $" ({Utils.Settings.GpuRendererAndDriver})";
            }

            Clipboard.SetText(version);
        }
    }
}
