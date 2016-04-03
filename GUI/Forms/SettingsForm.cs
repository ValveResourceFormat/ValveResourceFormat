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
            foreach (var path in Settings.GameSearchPaths)
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

            Settings.GameSearchPaths.Remove((string)gamePaths.SelectedItem);
            Settings.Save();

            gamePaths.Items.RemoveAt(gamePaths.SelectedIndex);
        }

        private void GamePathAdd(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select a folder";
                if (dlg.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                if (Settings.GameSearchPaths.Contains(dlg.SelectedPath))
                {
                    return;
                }

                Settings.GameSearchPaths.Add(dlg.SelectedPath);
                Settings.Save();

                gamePaths.Items.Add(dlg.SelectedPath);
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            var colorPicker = new ColorDialog();
            colorPicker.Color = Settings.BackgroundColor;

            // Update the text box color if the user clicks OK
            if (colorPicker.ShowDialog() == DialogResult.OK)
            {
                Settings.BackgroundColor = colorPicker.Color;
            }
        }
    }
}
