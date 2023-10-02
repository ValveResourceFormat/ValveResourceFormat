using System.Drawing;
using System.Windows.Forms;
using FastColoredTextBoxNS;

namespace GUI.Controls
{
    internal class CodeTextBox : FastColoredTextBox
    {
        public CodeTextBox() : base()
        {
            const int FontSize = 9;

            try
            {
                using var font = new FontFamily("Cascadia Mono");
                Font = new Font(font, FontSize);

            }
            catch
            {
                Font = new Font(FontFamily.GenericMonospace, FontSize);
            }

            Dock = DockStyle.Fill;
            BorderStyle = BorderStyle.None;
            ReadOnly = true;
            AllowMacroRecording = false;
            AutoIndent = false;
            TextChanged += OnTextChanged;

            // TODO: Handle OnZoomChanged and save zoom in settings
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            TextChanged -= OnTextChanged;

            ClearUndo();

            e.ChangedRange.SetFoldingMarkers("{", "}");
            e.ChangedRange.SetFoldingMarkers("\\[", "\\]");
        }
    }
}
