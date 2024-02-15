using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GUI.Utils;
using Microsoft.Win32;

namespace GUI.Forms
{
    partial class SettingsForm : Form
    {
        private static readonly int[] AntiAliasingSampleOptions = [0, 2, 4, 8, 16];

#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time - this requires unsafe code
        [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
#pragma warning restore SYSLIB1054

        private const int SHCNE_ASSOCCHANGED = 0x8000000;
        private const int SHCNF_FLUSH = 0x1000;

        public SettingsForm()
        {
            InitializeComponent();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            Settings.Save();
        }

        private void SettingsForm_Load(object sender, EventArgs e)
        {
            foreach (var path in Settings.Config.GameSearchPaths)
            {
                gamePaths.Items.Add(path);
            }

            maxTextureSizeInput.Value = Settings.Config.MaxTextureSize;
            fovInput.Value = Settings.Config.FieldOfView;
            vsyncCheckBox.Checked = Settings.Config.Vsync != 0;
            displayFpsCheckBox.Checked = Settings.Config.DisplayFps != 0;

            var strings = new string[AntiAliasingSampleOptions.Length];
            var selectedSamples = -1;

            for (var i = 0; i < AntiAliasingSampleOptions.Length; i++)
            {
                var samples = AntiAliasingSampleOptions[i];
                strings[i] = $"{samples}x";

                if (Settings.Config.AntiAliasingSamples >= samples)
                {
                    selectedSamples = i;
                }
            }

            antiAliasingComboBox.BeginUpdate();
            antiAliasingComboBox.Items.AddRange(strings);
            antiAliasingComboBox.SelectedIndex = selectedSamples;
            antiAliasingComboBox.EndUpdate();
        }

        private void GamePathRemoveClick(object sender, EventArgs e)
        {
            if (gamePaths.SelectedIndex < 0)
            {
                return;
            }

            Settings.Config.GameSearchPaths.Remove((string)gamePaths.SelectedItem);

            gamePaths.Items.RemoveAt(gamePaths.SelectedIndex);
        }

        private void GamePathAdd(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                InitialDirectory = Settings.Config.OpenDirectory,
                Filter = "Valve Pak (*.vpk) or gameinfo.gi|*.vpk;gameinfo.gi|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            var fileName = dlg.FileName;

            if (Regexes.VpkNumberArchive().IsMatch(fileName))
            {
                fileName = $"{fileName[..^8]}_dir.vpk";
            }

            if (Settings.Config.GameSearchPaths.Contains(fileName))
            {
                return;
            }

            Settings.Config.OpenDirectory = Path.GetDirectoryName(fileName);
            Settings.Config.GameSearchPaths.Add(fileName);

            gamePaths.Items.Add(fileName);
        }

        private void GamePathAddFolder(object sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog
            {
                SelectedPath = Settings.Config.OpenDirectory,
            };
            if (dlg.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            if (Settings.Config.GameSearchPaths.Contains(dlg.SelectedPath))
            {
                return;
            }

            Settings.Config.OpenDirectory = dlg.SelectedPath;
            Settings.Config.GameSearchPaths.Add(dlg.SelectedPath);

            gamePaths.Items.Add(dlg.SelectedPath);
        }

        private void OnMaxTextureSizeValueChanged(object sender, EventArgs e)
        {
            Settings.Config.MaxTextureSize = (int)maxTextureSizeInput.Value;
        }

        private void OnFovValueChanged(object sender, EventArgs e)
        {
            Settings.Config.FieldOfView = (int)fovInput.Value;
        }

        private void OnAntiAliasingValueChanged(object sender, EventArgs e)
        {
            var newValue = AntiAliasingSampleOptions[antiAliasingComboBox.SelectedIndex];
            Settings.Config.AntiAliasingSamples = newValue;
        }

        private void OnVsyncValueChanged(object sender, EventArgs e)
        {
            Settings.Config.Vsync = vsyncCheckBox.Checked ? 1 : 0;
        }

        private void OnDisplayFpsValueChanged(object sender, EventArgs e)
        {
            Settings.Config.DisplayFps = displayFpsCheckBox.Checked ? 1 : 0;
        }

        private void OnRegisterAssociationButtonClick(object sender, EventArgs e) => RegisterFileAssociation();

        public static void RegisterFileAssociation()
        {
            var extension = ".vpk";
            var progId = $"VRF.Source2Viewer{extension}";
            var applicationPath = Application.ExecutablePath;

            using var reg = Registry.CurrentUser.CreateSubKey(@$"Software\Classes\{extension}\OpenWithProgids");
            reg.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);

            using var reg2 = Registry.CurrentUser.CreateSubKey(@$"Software\Classes\{progId}");
            reg2.SetValue(null, "Valve Pak File");

            using var reg3 = reg2.CreateSubKey(@"shell\open\command");
            reg3.SetValue(null, $"\"{applicationPath}\" \"%1\"");

            // Protocol
            using var regProtocol = Registry.CurrentUser.CreateSubKey(@"Software\Classes\vpk");
            regProtocol.SetValue(string.Empty, "URL:Valve Pak protocol");
            regProtocol.SetValue("URL Protocol", string.Empty);

            using var regProtocolOpen = regProtocol.CreateSubKey(@"shell\open\command");
            regProtocolOpen.SetValue(null, $"\"{applicationPath}\" \"%1\"");

            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);

            MessageBox.Show(
                $"Registered .vpk file association as well as \"vpk:\" protocol link handling.{Environment.NewLine}{Environment.NewLine}If you move {Path.GetFileName(applicationPath)}, you will have to register it again.",
                "File association registered",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }
    }
}
