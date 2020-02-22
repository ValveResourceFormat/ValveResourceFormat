using System;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Forms
{
    public partial class SettingsForm : Form
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
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "Valve Pak (*.vpk)|*.vpk|All files (*.*)|*.*";

                if (dlg.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                if (Settings.Config.GameSearchPaths.Contains(dlg.FileName))
                {
                    return;
                }

                Settings.Config.GameSearchPaths.Add(dlg.FileName);
                Settings.Save();

                gamePaths.Items.Add(dlg.FileName);
            }
        }

        private void GamePathAddFolder(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                if (dlg.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                if (Settings.Config.GameSearchPaths.Contains(dlg.SelectedPath))
                {
                    return;
                }

                Settings.Config.GameSearchPaths.Add(dlg.SelectedPath);
                Settings.Save();

                gamePaths.Items.Add(dlg.SelectedPath);
            }
        }

        private void Button1_Click(object sender, EventArgs e)
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
        }
    }
}
