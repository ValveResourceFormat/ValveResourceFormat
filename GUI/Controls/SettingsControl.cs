using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Utils;
using Microsoft.Win32;

namespace GUI.Controls
{
    partial class SettingsControl : UserControl
    {
        private static readonly int[] AntiAliasingSampleOptions = [0, 2, 4, 8, 16];
        private static readonly int[] ShadowQualityResolutions = [512, 1024, 2048, 4096];
        private static readonly string[] ShadowQualityNames = ["Low", "Medium", "High", "Very High"];

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
            fovInput.Value = Settings.Config.FieldOfView;
            viewmodelFovInput.Value = Settings.Config.ViewmodelFieldOfView;
            mouseSensitivitySlider.Value = (int)(Settings.Config.MouseSensitivity * 10f);
            mouseSensitivityValueLabel.Text = Settings.Config.MouseSensitivity.ToString("0.0");

            var volumePercent = Math.Clamp((int)MathF.Round(Settings.Config.Volume * 100f), 0, 100);
            volumeSlider.Value = volumePercent;
            volumeValueLabel.Text = string.Create(CultureInfo.InvariantCulture, $"{volumePercent}%");
            smoothCamCheckbox.Checked = Settings.Config.SmoothCameraEnabled;

            shadowQualityComboBox.Items.AddRange(ShadowQualityNames);
            var currentShadowResolution = Settings.Config.ShadowResolution;
            var shadowQualityIndex = ShadowQualityResolutions.Length - 1;

            for (var i = 0; i < ShadowQualityResolutions.Length; i++)
            {
                if (currentShadowResolution <= ShadowQualityResolutions[i])
                {
                    shadowQualityIndex = i;
                    break;
                }
            }

            shadowQualityComboBox.SelectedIndex = shadowQualityIndex;
            vsyncCheckBox.Checked = Settings.Config.Vsync != 0;
            displayFpsCheckBox.Checked = Settings.Config.DisplayFps != 0;
            openExplorerOnStartCheckbox.Checked = Settings.Config.OpenExplorerOnStart != 0;
            textViewerFontSize.Value = Settings.Config.TextViewerFontSize;

            themeComboBox.Items.AddRange(Enum.GetNames<Themer.AppTheme>());
            themeComboBox.SelectedIndex = Math.Clamp(Settings.Config.Theme, 0, themeComboBox.Items.Count - 1);

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
            if (gamePaths.SelectedIndex < 0 || gamePaths.SelectedItem is not string gameSearchPath)
            {
                return;
            }

            Settings.Config.GameSearchPaths.Remove(gameSearchPath);

            gamePaths.Items.RemoveAt(gamePaths.SelectedIndex);
        }

        private void GamePathAdd(object sender, EventArgs e)
        {
            var fileName = AppFileDialogs.OpenFile(null, "Valve Pak (*.vpk) or gameinfo.gi|*.vpk;gameinfo.gi|All files (*.*)|*.*", updateRemembered: false);

            if (fileName == null)
            {
                return;
            }

            if (Regexes.VpkNumberArchive().IsMatch(fileName))
            {
                fileName = $"{fileName[..^8]}_dir.vpk";
            }

            if (Settings.Config.GameSearchPaths.Contains(fileName))
            {
                return;
            }

            if (Path.GetDirectoryName(fileName) is { Length: > 0 } directory)
            {
                Settings.Config.OpenDirectory = directory;
            }

            Settings.Config.GameSearchPaths.Add(fileName);

            gamePaths.Items.Add(fileName);
        }

        private void GamePathAddFolder(object sender, EventArgs e)
        {
            var selectedPath = AppFileDialogs.PickFolder(null, AppFileDialogs.RememberIn.OpenDirectory, updateRemembered: false);

            if (selectedPath == null)
            {
                return;
            }

            if (Settings.Config.GameSearchPaths.Contains(selectedPath))
            {
                return;
            }

            Settings.Config.OpenDirectory = selectedPath;
            Settings.Config.GameSearchPaths.Add(selectedPath);

            gamePaths.Items.Add(selectedPath);
        }

        private void OnMaxTextureSizeValueChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            Settings.Config.MaxTextureSize = maxTextureSizeInput.Value;
        }

        private void OnShadowQualityChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated || shadowQualityComboBox.SelectedIndex < 0)
            {
                return;
            }

            Settings.Config.ShadowResolution = ShadowQualityResolutions[shadowQualityComboBox.SelectedIndex];
        }

        private void OnFovValueChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            Settings.Config.FieldOfView = (float)fovInput.Value;
        }

        private void OnViewmodelFovValueChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            Settings.Config.ViewmodelFieldOfView = (float)viewmodelFovInput.Value;
        }

        private void OnMouseSensitivitySliderValueChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            var sensitivity = mouseSensitivitySlider.Value / 10f;
            Settings.Config.MouseSensitivity = sensitivity;
            mouseSensitivityValueLabel.Text = sensitivity.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private void OnVolumeSliderValueChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            Settings.Config.Volume = volumeSlider.Value / 100f;
            volumeValueLabel.Text = string.Create(CultureInfo.InvariantCulture, $"{volumeSlider.Value}%");
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
            if (!IsHandleCreated || antiAliasingComboBox.SelectedIndex < 0)
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

            Settings.Config.TextViewerFontSize = textViewerFontSize.Value;
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

        private async void OnRegisterAssociationButtonClick(object sender, EventArgs e) => await RegisterFileAssociationAsync().ConfigureAwait(true);

        public static async Task RegisterFileAssociationAsync()
        {
            const string extension = ".vpk";
            const string progId = $"VRF.Source2Viewer{extension}";

            var applicationPath = Application.ExecutablePath;

            // copy vpk icon to settings folder
            var vpkIconPath = Path.Join(Settings.SettingsFolder, "vpk.ico");

            if (!File.Exists(vpkIconPath))
            {
                using var iconStream = Program.Assembly.GetManifestResourceStream("GUI.Utils.vpk.ico");
                Debug.Assert(iconStream != null);
                using var iconDiskStream = File.Create(vpkIconPath);
                await iconStream.CopyToAsync(iconDiskStream).ConfigureAwait(true);
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

            await AppMessageDialogs.ShowMessageAsync(
                $"Registered .vpk file association as well as \"vpk:\" protocol link handling.{Environment.NewLine}{Environment.NewLine}If you move {Path.GetFileName(applicationPath)}, you will have to register it again.",
                "File association registered"
            ).ConfigureAwait(false);
        }

        private void OnSmoothCameraChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            Settings.Config.SmoothCameraEnabled = smoothCamCheckbox.Checked;
        }

        private void SettingsControl_Leave(object sender, EventArgs e)
        {
            Settings.Save();
        }
    }
}
