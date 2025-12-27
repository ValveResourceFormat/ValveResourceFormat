using System.IO;
using System.Windows.Forms;
using GUI.Utils;
using Microsoft.Win32;

#nullable disable

namespace GUI.Forms
{
    partial class SettingsControl : UserControl
    {
        private static readonly int[] AntiAliasingSampleOptions = [0, 2, 4, 8, 16];

        public SettingsControl()
        {
            InitializeComponent();
        }

        private void SettingsControl_Load(object sender, EventArgs e)
        {
            foreach (var path in Settings.Config.GameSearchPaths)
            {
                gamePaths.Items.Add(path);
            }

            maxTextureSizeInput.Value = Settings.Config.MaxTextureSize;
            shadowResolutionInput.Value = Settings.Config.ShadowResolution;
            fovInput.Value = Settings.Config.FieldOfView;
            vsyncCheckBox.Checked = Settings.Config.Vsync != 0;
            displayFpsCheckBox.Checked = Settings.Config.DisplayFps != 0;
            openExplorerOnStartCheckbox.Checked = Settings.Config.OpenExplorerOnStart != 0;
            textViewerFontSize.Value = Settings.Config.TextViewerFontSize;

            themeComboBox.Items.AddRange(Enum.GetNames<Themer.AppTheme>());
            themeComboBox.SelectedIndex = Settings.Config.Theme;

            var quickPreviewFlags = (Settings.QuickPreviewFlags)Settings.Config.QuickFilePreview;
            quickPreviewCheckbox.Checked = (quickPreviewFlags & Settings.QuickPreviewFlags.Enabled) != 0;
            quickPreviewSoundsCheckbox.Checked = (quickPreviewFlags & Settings.QuickPreviewFlags.AutoPlaySounds) != 0;

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
            if (!IsHandleCreated)
            {
                return;
            }

            Settings.Config.MaxTextureSize = maxTextureSizeInput.Value;
        }

        private void OnFovValueChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            Settings.Config.FieldOfView = (float)fovInput.Value;
        }

        private void OnSetFovTo4by3ButtonClick(object sender, EventArgs e)
        {
            Settings.Config.FieldOfView = 2f * MathF.Atan(3f / 4f) / MathF.PI * 180f;
            fovInput.Value = Settings.Config.FieldOfView;
        }

        private void OnOpenExplorerOnStartValueChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            Settings.Config.OpenExplorerOnStart = openExplorerOnStartCheckbox.Checked ? 1 : 0;
        }

        private void OnAntiAliasingValueChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            var newValue = AntiAliasingSampleOptions[antiAliasingComboBox.SelectedIndex];
            Settings.Config.AntiAliasingSamples = newValue;
        }

        private void OnVsyncValueChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            Settings.Config.Vsync = vsyncCheckBox.Checked ? 1 : 0;
        }

        private void OnDisplayFpsValueChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            Settings.Config.DisplayFps = displayFpsCheckBox.Checked ? 1 : 0;
        }

        private void OnTextViewerFontSizeValueChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            Settings.Config.TextViewerFontSize = (int)textViewerFontSize.Value;
        }

        private void OnQuickPreviewCheckboxChanged(object sender, EventArgs e) => SetQuickPreviewSetting();
        private void OnQuickPreviewSoundsCheckboxChanged(object sender, EventArgs e) => SetQuickPreviewSetting();

        private void SetQuickPreviewSetting()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            Settings.QuickPreviewFlags value = 0;

            if (quickPreviewCheckbox.Checked)
            {
                value |= Settings.QuickPreviewFlags.Enabled;
            }

            if (quickPreviewSoundsCheckbox.Checked)
            {
                value |= Settings.QuickPreviewFlags.AutoPlaySounds;
            }

            Settings.Config.QuickFilePreview = (int)value;
        }

        private void OnThemeSelectedIndexChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            Settings.Config.Theme = themeComboBox.SelectedIndex;

            // TODO: SetColorMode requires restart for it to work properly
            //Application.SetColorMode(Settings.GetSystemColor());
        }

        private void OnRegisterAssociationButtonClick(object sender, EventArgs e) => RegisterFileAssociation();

        public static void RegisterFileAssociation()
        {
            const string extension = ".vpk";
            const string progId = $"VRF.Source2Viewer{extension}";

            var applicationPath = Application.ExecutablePath;

            // copy vpk icon to settings folder
            var vpkIconPath = Path.Join(Settings.SettingsFolder, "vpk.ico");

            if (!File.Exists(vpkIconPath))
            {
                using var iconStream = Program.Assembly.GetManifestResourceStream("GUI.Utils.vpk.ico");
                using var iconDiskStream = File.OpenWrite(vpkIconPath);
                iconStream.CopyTo(iconDiskStream);
            }

            // .vpk file extension
            using var reg = Registry.CurrentUser.CreateSubKey(@$"Software\Classes\{extension}\OpenWithProgids");
            reg.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);

            using var reg2 = Registry.CurrentUser.CreateSubKey(@$"Software\Classes\{progId}");
            reg2.SetValue(null, "Valve Pak File");

            using var reg3 = reg2.CreateSubKey(@"shell\open\command");
            reg3.SetValue(null, $"\"{applicationPath}\" \"%1\"");

            using var regIco = reg2.CreateSubKey("DefaultIcon");
            regIco.SetValue(null, vpkIconPath);

            // Protocol
            using var regProtocol = Registry.CurrentUser.CreateSubKey(@"Software\Classes\vpk");
            regProtocol.SetValue(string.Empty, "URL:Valve Pak protocol");
            regProtocol.SetValue("URL Protocol", string.Empty);

            using var regProtocolOpen = regProtocol.CreateSubKey(@"shell\open\command");
            regProtocolOpen.SetValue(null, $"\"{applicationPath}\" \"%1\"");

            unsafe
            {
                Windows.Win32.PInvoke.SHChangeNotify(Windows.Win32.UI.Shell.SHCNE_ID.SHCNE_ASSOCCHANGED, Windows.Win32.UI.Shell.SHCNF_FLAGS.SHCNF_FLUSH, null, null);
            }

            MessageBox.Show(
                $"Registered .vpk file association as well as \"vpk:\" protocol link handling.{Environment.NewLine}{Environment.NewLine}If you move {Path.GetFileName(applicationPath)}, you will have to register it again.",
                "File association registered",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private void SettingsControl_Leave(object sender, EventArgs e)
        {
            Settings.Save();
        }
    }
}
