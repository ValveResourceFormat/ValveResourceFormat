using System.Globalization;
using System.IO;
using System.Windows.Forms;
using GUI.Utils;
using ValveResourceFormat.IO;

namespace GUI.Controls
{
    partial class SettingsControl : UserControl
    {
        private static readonly int[] AntiAliasingSampleOptions = [0, 2, 4, 8, 16];
        private static readonly int[] ShadowQualityResolutions = [512, 1024, 2048, 4096];

        private static string[] ShadowQualityNames => [Locale.Settings_ShadowLow, Locale.Settings_ShadowMedium, Locale.Settings_ShadowHigh, Locale.Settings_ShadowVeryHigh];

        public SettingsControl()
        {
            InitializeComponent();
            ApplyTranslations();
        }

        private void ApplyTranslations()
        {
            vsyncCheckBox.Text = Locale.Settings_VerticalSync;
            gamePathsAdd.Text = Locale.Settings_AddGamePath;
            gamePathsRemove.Text = Locale.Settings_Remove;
            gamePathsAddFolder.Text = Locale.Settings_AddFolder;
            maxTextureSizeLabel.Text = Locale.Settings_MaxTextureSize;
            mouseSensitivityLabel.Text = Locale.Settings_MouseSensitivity;
            fovLabel.Text = Locale.Settings_CameraFov;
            viewmodelFovLabel.Text = Locale.Settings_ViewmodelFov;
            antiAliasingLabel.Text = Locale.Settings_AntiAliasing;
            installButton.Text = Locale.Menu_InstallApp;
            displayFpsCheckBox.Text = Locale.Settings_DisplayFps;
            groupBox1.Text = Locale.Settings_GamePaths_Group;
            groupBox2.Text = Locale.Settings_Video_Group;
            smoothCamCheckbox.Text = Locale.Settings_SmoothCamera;
            shadowResolutionLabel.Text = Locale.Settings_ShadowQuality;
            groupBox3.Text = Locale.Settings_Explorer_Group;
            textViewerFontSizeLabel.Text = Locale.Settings_TextViewerFontSize;
            openExplorerOnStartCheckbox.Text = Locale.Settings_OpenExplorerOnStart;
            themeLabel.Text = Locale.Settings_Theme;
            groupBox4.Text = Locale.Settings_QuickPreview_Group;
            quickPreviewCheckbox.Text = Locale.Settings_PreviewAfterSelecting;
            quickPreviewSoundsCheckbox.Text = Locale.Settings_AutoPlaySounds;
            groupBoxAudio.Text = Locale.Settings_Audio_Group;
            volumeLabel.Text = Locale.Settings_Volume;
            footerLabel.Text = Locale.Settings_Footer;
        }

        private static string GetThemeName(Themer.AppTheme theme) => theme switch
        {
            Themer.AppTheme.Light => Locale.Theme_Light,
            Themer.AppTheme.Dark => Locale.Theme_Dark,
            _ => Locale.Theme_System,
        };

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
            mouseSensitivityValueLabel.Text = Settings.Config.MouseSensitivity.ToString("0.0", CultureInfo.InvariantCulture);

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

            foreach (var theme in Enum.GetValues<Themer.AppTheme>())
            {
                themeComboBox.Items.Add(GetThemeName(theme));
            }

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

            using var _ = antiAliasingComboBox.SuspendPainting();
            antiAliasingComboBox.Items.AddRange(strings);
            antiAliasingComboBox.SelectedIndex = selectedSamples;

            var installText = AppInstaller.GetInstallButtonText();
            installButton.Visible = installText != null;
            installButton.Text = installText;
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
            var fileName = AppFileDialogs.OpenFile(null, Locale.Settings_GamePathFileFilter, updateRemembered: false);

            if (fileName == null)
            {
                return;
            }

            if (PackageFileNames.TryGetArchiveChunkDirectoryFile(fileName, out var directoryFile))
            {
                fileName = directoryFile;
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

        private async void OnInstallButtonClick(object sender, EventArgs e) => await AppInstaller.InstallFromUiAsync().ConfigureAwait(true);

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
