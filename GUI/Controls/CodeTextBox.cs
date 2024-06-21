using System.Drawing;
using System.Windows.Forms;
using FastColoredTextBoxNS;

namespace GUI.Controls
{
    internal class CodeTextBox : FastColoredTextBox
    {
        public bool PreserveUndoHistory { get; set; }
        private string LazyText;

        public CodeTextBox(string text) : base()
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
            AllowDrop = false;
            AllowMacroRecording = false;
            AutoIndent = false;
            Disposed += OnDisposed;
            TextChanged += OnTextChanged;

            if (Visible && Parent != null)
            {
                Text = text;
            }
            else if (!string.IsNullOrEmpty(text))
            {
                LazyText = text;
                ParentChanged += OnVisibleChanged;
                VisibleChanged += OnVisibleChanged;
            }

            // TODO: Handle OnZoomChanged and save zoom in settings
        }

        private void OnDisposed(object sender, EventArgs e)
        {
            Disposed -= OnDisposed;
            ParentChanged -= OnVisibleChanged;
            VisibleChanged -= OnVisibleChanged;
            TextChanged -= OnTextChanged;
        }

        private void OnVisibleChanged(object sender, EventArgs e)
        {
            if (!Visible)
            {
                return;
            }

            ParentChanged -= OnVisibleChanged;
            VisibleChanged -= OnVisibleChanged;

            Cursor.Current = Cursors.WaitCursor;

            base.Text = LazyText;
            LazyText = null;

            Cursor.Current = Cursors.Default;
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!PreserveUndoHistory)
            {
                ClearUndo();
            }

            e.ChangedRange.SetFoldingMarkers("{", "}");
            e.ChangedRange.SetFoldingMarkers("\\[", "\\]");
        }
    }
}
