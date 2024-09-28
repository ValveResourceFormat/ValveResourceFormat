using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using FastColoredTextBoxNS;

namespace GUI.Controls
{
    internal partial class CodeTextBox : FastColoredTextBox
    {
        internal enum HighlightLanguage
        {
            None,
            KeyValues,
            XML,
            JS,
            CSS, // TODO
        }

        private string LazyText;

        public CodeTextBox(string text, HighlightLanguage highlightSyntax = HighlightLanguage.KeyValues) : base()
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

            if (highlightSyntax == HighlightLanguage.KeyValues)
            {
                SyntaxHighlighter = new KvSyntaxHighlighter(this);
            }
            else if (highlightSyntax == HighlightLanguage.XML)
            {
                Language = Language.XML;
            }
            else if (highlightSyntax == HighlightLanguage.JS)
            {
                Language = Language.JS;
            }

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
            ClearUndo();

            //e.ChangedRange.SetFoldingMarkers("{", "}");
            //e.ChangedRange.SetFoldingMarkers("\\[", "\\]");
        }

        private partial class KvSyntaxHighlighter : SyntaxHighlighter
        {
            public KvSyntaxHighlighter(FastColoredTextBox currentTb) : base(currentTb)
            {
                CommentStyle = GreenStyle;
                StringStyle = BlueStyle;
                NumberStyle = MagentaStyle;
            }

            public override void HighlightSyntax(Language language, FastColoredTextBoxNS.Range range)
            {
                range.tb.LeftBracket = '[';
                range.tb.RightBracket = ']';
                range.tb.LeftBracket2 = '{';
                range.tb.RightBracket2 = '}';

                range.ClearStyle(StringStyle, NumberStyle, KeywordStyle, CommentStyle);

                range.SetStyle(StringStyle, StringRegex());
                range.SetStyle(CommentStyle, CommentRegex());
                range.SetStyle(NumberStyle, NumberRegex());

                range.ClearFoldingMarkers();
                range.SetFoldingMarkers("{", "}");
                range.SetFoldingMarkers(@"\[", @"\]");
            }

            [GeneratedRegex(@"""""|"".*?[^\\]""")]
            private static partial Regex StringRegex();

            [GeneratedRegex(@"\b([0-9]+[\.]?[0-9]*|true|false|null)\b")]
            private static partial Regex NumberRegex();

            [GeneratedRegex(@"//.*$", RegexOptions.Multiline)]
            private static partial Regex CommentRegex();
        }
    }
}
