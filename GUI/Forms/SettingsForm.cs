using System;
using System.IO;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Forms
{
    partial class SettingsForm : Form
    {
        public SettingsForm()
        {
            InitializeComponent();
        }

        private void SettingsForm_Load(object sender, EventArgs e)
        {
            foreach (var path in Settings.Config.GameSearchPaths)
            {
                gamePaths.Items.Add(path);
            }

            maxTextureSizeInput.Value = Settings.Config.MaxTextureSize;
            fovInput.Value = Settings.Config.FieldOfView;
        }

        private void GamePathRemoveClick(object sender, EventArgs e)
        {
            if (gamePaths.SelectedIndex < 0)
            {
                return;
            }

            Settings.Config.GameSearchPaths.Remove((string)gamePaths.SelectedItem);
            Settings.Save();

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

            if (Regexes.VpkNumberArchive.IsMatch(fileName))
            {
                fileName = $"{fileName[..^8]}_dir.vpk";
            }

            if (Settings.Config.GameSearchPaths.Contains(fileName))
            {
                return;
            }

            Settings.Config.OpenDirectory = Path.GetDirectoryName(fileName);
            Settings.Config.GameSearchPaths.Add(fileName);
            Settings.Save();

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
            Settings.Save();

            gamePaths.Items.Add(dlg.SelectedPath);
        }

        private void OpenBackgroundColorPicker(object sender, EventArgs e)
        {
            // Run the dialog on a separate thread, otherwise it will not work
            // when opening settings while opentk is in focus
            new System.Threading.Thread(() =>
            {
                var colorPicker = new ColorDialog
                {
                    Color = Settings.BackgroundColor,
                };

                // Update the text box color if the user clicks OK
                if (colorPicker.ShowDialog() == DialogResult.OK)
                {
                    Settings.BackgroundColor = colorPicker.Color;
                    Settings.Save();
                }
            }).Start();
        }

        private void OnMaxTextureSizeValueChanged(object sender, EventArgs e)
        {
            var newValue = (int)maxTextureSizeInput.Value;

            if (newValue == Settings.Config.MaxTextureSize)
            {
                return;
            }

            Settings.Config.MaxTextureSize = newValue;
            Settings.Save();
        }

        private void OnFovValueChanged(object sender, EventArgs e)
        {
            var newValue = (int)fovInput.Value;

            if (newValue == Settings.Config.FieldOfView)
            {
                return;
            }

            Settings.Config.FieldOfView = newValue;
            Settings.Save();
        }
    }
}
