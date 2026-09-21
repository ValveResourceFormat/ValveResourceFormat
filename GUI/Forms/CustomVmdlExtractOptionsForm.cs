using System.Drawing;
using System.IO;
using GUI.Utils;

namespace GUI.Forms
{
    /// <summary>
    /// Collects options for a custom VMDL extractor export: whether to auto-build the result with
    /// resourcecompiler.exe afterwards, and if so, where the compiler and target game live.
    /// </summary>
    public partial class CustomVmdlExtractOptionsForm : ThemedForm
    {
        /// <summary>Whether the export should be auto-built with resourcecompiler.exe afterwards.</summary>
        public bool AutoBuild => autoBuildCheckBox.Checked;

        /// <summary>Path to resourcecompiler.exe, when <see cref="AutoBuild"/> is set.</summary>
        public string ResourceCompilerPath => resourceCompilerTextBox.Text;

        /// <summary>The "-game" directory passed to resourcecompiler.exe, when <see cref="AutoBuild"/> is set.</summary>
        public string GameDir => gameDirTextBox.Text;

        /// <summary>Whether to emit detailed per-file diagnostics to the console.</summary>
        public bool Verbose => verboseCheckBox.Checked;

        public CustomVmdlExtractOptionsForm()
        {
            InitializeComponent();

            resourceCompilerTextBox.Text = Settings.Config.CustomVmdlResourceCompilerPath;
            gameDirTextBox.Text = Settings.Config.CustomVmdlGameDir;
            UpdateAutoBuildControlsEnabled();
            LayoutDynamicControls();
        }

        // hintLabel wraps to its actual rendered size (font/DPI dependent), so lay out
        // everything below it, and size the dialog itself, around that real height instead
        // of a fixed guess that can clip the text on other displays or font settings.
        private void LayoutDynamicControls()
        {
            verboseCheckBox.Location = new Point(verboseCheckBox.Left, hintLabel.Bottom + 12);

            var buttonsTop = verboseCheckBox.Bottom + 16;
            cancelButton.Location = new Point(cancelButton.Left, buttonsTop);
            submitButton.Location = new Point(submitButton.Left, buttonsTop);

            ClientSize = new Size(ClientSize.Width, buttonsTop + cancelButton.Height + 16);
            MinimumSize = Size;
        }

        private void AutoBuildCheckBox_CheckedChanged(object sender, EventArgs e) => UpdateAutoBuildControlsEnabled();

        private void UpdateAutoBuildControlsEnabled()
        {
            var enabled = autoBuildCheckBox.Checked;
            resourceCompilerTextBox.Enabled = enabled;
            browseResourceCompilerButton.Enabled = enabled;
            gameDirTextBox.Enabled = enabled;
            browseGameDirButton.Enabled = enabled;
        }

        private void BrowseResourceCompilerButton_Click(object sender, EventArgs e)
        {
            var path = AppFileDialogs.OpenFile("Choose resourcecompiler.exe", "resourcecompiler.exe|resourcecompiler.exe|Executable files|*.exe", AppFileDialogs.RememberIn.None);

            if (path != null)
            {
                resourceCompilerTextBox.Text = path;
            }
        }

        private void BrowseGameDirButton_Click(object sender, EventArgs e)
        {
            var path = AppFileDialogs.PickFolder("Choose the \"-game\" directory (e.g. .../game/csgo)", AppFileDialogs.RememberIn.None);

            if (path != null)
            {
                gameDirTextBox.Text = path;
            }
        }

        private void SubmitButton_Click(object sender, EventArgs e)
        {
            if (AutoBuild)
            {
                if (string.IsNullOrWhiteSpace(ResourceCompilerPath) || !File.Exists(ResourceCompilerPath))
                {
                    _ = AppMessageDialogs.ShowMessageAsync("Choose a valid path to resourcecompiler.exe, or turn off auto-build.", "Invalid resource compiler path", MessageIcon.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(GameDir) || !Directory.Exists(GameDir))
                {
                    _ = AppMessageDialogs.ShowMessageAsync("Choose a valid \"-game\" directory, or turn off auto-build.", "Invalid game directory", MessageIcon.Warning);
                    return;
                }

                Settings.Config.CustomVmdlResourceCompilerPath = ResourceCompilerPath;
                Settings.Config.CustomVmdlGameDir = GameDir;
                Settings.Save();
            }

            DialogResult = System.Windows.Forms.DialogResult.OK;
        }
    }
}
