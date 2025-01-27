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
            CSS,
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

            BackColor = SystemColors.Window;
            ForeColor = SystemColors.WindowText;
            IndentBackColor = SystemColors.InactiveBorder;
            SelectionColor = SystemColors.Highlight;
            ServiceLinesColor = SystemColors.ActiveBorder;
            CurrentLineColor = SystemColors.Highlight;
            LineNumberColor = SystemColors.GrayText;
            CaretColor = SystemColors.WindowText;
            ServiceLinesColor = SystemColors.ScrollBar;
            FoldingIndicatorColor = SystemColors.Highlight;

            ServiceColors.CollapseMarkerForeColor = SystemColors.ControlText;
            ServiceColors.CollapseMarkerBackColor = SystemColors.Control;
            ServiceColors.CollapseMarkerBorderColor = SystemColors.ControlDark;
            ServiceColors.ExpandMarkerForeColor = SystemColors.ControlText;
            ServiceColors.ExpandMarkerBackColor = SystemColors.Control;
            ServiceColors.ExpandMarkerBorderColor = SystemColors.ControlDark;

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
            else if (highlightSyntax == HighlightLanguage.CSS)
            {
                SyntaxHighlighter = new CssSyntaxHighlighter(this);
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

        public static CodeTextBox CreateFromException(Exception exception)
        {
            var text = $"Unhandled exception occured while trying to open this file:\n{exception.Message}\n\nTry using latest dev build to see if the issue persists.\n\n{exception}\n\nSource 2 Viewer Version: {Application.ProductVersion}";

            var control = new CodeTextBox(text, HighlightLanguage.None)
            {
                WordWrap = true,
            };
            return control;
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

                range.ClearStyle(StringStyle, NumberStyle, CommentStyle);

                range.SetStyle(StringStyle, StringRegex());
                range.SetStyle(CommentStyle, CommentRegex());
                range.SetStyle(NumberStyle, NumberRegex());

                range.ClearFoldingMarkers();
                range.SetFoldingMarkers("{", "}");
                range.SetFoldingMarkers(@"\[", @"\]");
            }

            [GeneratedRegex(@"\b([0-9]+[\.]?[0-9]*|0x[0-9A-F]+|true|false|null)\b")]
            private static partial Regex NumberRegex();

            [GeneratedRegex(@"//.*$", RegexOptions.Multiline)]
            private static partial Regex CommentRegex();
        }

        private partial class CssSyntaxHighlighter : SyntaxHighlighter
        {
            public CssSyntaxHighlighter(FastColoredTextBox currentTb) : base(currentTb)
            {
                CommentStyle = GreenStyle;
                StringStyle = BlueStyle;
                NumberStyle = MagentaStyle;
            }

            public override void HighlightSyntax(Language language, FastColoredTextBoxNS.Range range)
            {
                range.tb.LeftBracket = '{';
                range.tb.RightBracket = '}';
                range.tb.LeftBracket2 = '(';
                range.tb.RightBracket2 = ')';

                range.ClearStyle(StringStyle, NumberStyle, KeywordStyle, CommentStyle);

                range.SetStyle(StringStyle, StringRegex());
                range.SetStyle(KeywordStyle, CssPropertyRegex());
                range.SetStyle(CommentStyle, MultilineCommentRegex());

                range.ClearFoldingMarkers();
                range.SetFoldingMarkers("{", "}");
            }

            [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
            private static partial Regex MultilineCommentRegex();

            [GeneratedRegex("([a-z-]+(?:[a-z0-9-]*[a-z0-9]+)?)\\s*:", RegexOptions.IgnoreCase)]
            private static partial Regex CssPropertyRegex();
        }

        [GeneratedRegex(@"""""|"".*?[^\\]""")]
        private static partial Regex StringRegex();
    }
}
