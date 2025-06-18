using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using FastColoredTextBoxNS;
using GUI.Utils;

namespace GUI.Controls
{
    internal partial class CodeTextBox : FastColoredTextBox
    {
        internal enum HighlightLanguage
        {
            Default = -1,
            None,
            KeyValues,
            XML,
            JS,
            CSS,
            Shaders,
        }

        private string? LazyText;

        public static Font GetMonospaceFont()
        {
            try
            {
                using var fontFamily = new FontFamily("Cascadia Mono");
                return new Font(fontFamily, Settings.Config.TextViewerFontSize);
            }
            catch
            {
                return new Font(FontFamily.GenericMonospace, Settings.Config.TextViewerFontSize);
            }
        }

        public CodeTextBox(string text, HighlightLanguage highlightSyntax = HighlightLanguage.KeyValues) : base()
        {
            BackColor = SystemColors.Window;
            ForeColor = SystemColors.WindowText;
            IndentBackColor = SystemColors.InactiveBorder;
            SelectionStyle = new SelectionStyle(SystemBrushes.Highlight, SystemBrushes.HighlightText);
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

            // Console tab is created before the settings are loaded (because the settings loader can print logs)
            if (Settings.Config.TextViewerFontSize > 0)
            {
                Font = GetMonospaceFont();
            }

            Dock = DockStyle.Fill;
            BorderStyle = BorderStyle.None;
            ReadOnly = true;
            AllowDrop = false;
            AllowMacroRecording = false;
            AutoIndent = false;
            Disposed += OnDisposed;
            TextChanged += OnTextChanged;

            if (highlightSyntax == HighlightLanguage.KeyValues || highlightSyntax == HighlightLanguage.Default)
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
            else if (highlightSyntax == HighlightLanguage.Shaders)
            {
                SyntaxHighlighter = new ShaderSyntaxHighlighter(this);
            }

#pragma warning disable WFO5001
            // Fix syntax highlighting colors for dark mode
            if (Application.IsDarkModeEnabled)
            {
                SyntaxHighlighter.StringStyle = new TextStyle(Brushes.DeepSkyBlue, null, FontStyle.Regular);
                SyntaxHighlighter.NumberStyle = new TextStyle(Brushes.MediumPurple, null, FontStyle.Regular);
                SyntaxHighlighter.CommentStyle = new TextStyle(Brushes.YellowGreen, null, FontStyle.Italic);
                SyntaxHighlighter.KeywordStyle = new TextStyle(Brushes.CornflowerBlue, null, FontStyle.Regular);

                if (SyntaxHighlighter is ShaderSyntaxHighlighter ssh)
                {
                    ssh.WordStyle = SyntaxHighlighter.StringStyle;
                    ssh.DirectiveStyle = new TextStyle(Brushes.Gold, null, FontStyle.Bold);
                }

                if (Language == Language.XML)
                {
                    SyntaxHighlighter.XmlAttributeStyle = new TextStyle(Brushes.Tomato, null, FontStyle.Regular);
                    SyntaxHighlighter.XmlAttributeValueStyle = SyntaxHighlighter.StringStyle;
                    SyntaxHighlighter.XmlTagBracketStyle = SyntaxHighlighter.StringStyle;
                    SyntaxHighlighter.XmlTagNameStyle = SyntaxHighlighter.NumberStyle;
                    SyntaxHighlighter.XmlEntityStyle = SyntaxHighlighter.XmlAttributeStyle;
                    SyntaxHighlighter.XmlCDataStyle = new TextStyle(Brushes.Cyan, null, FontStyle.Regular);
                }
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

        public static Control Create(string text, HighlightLanguage language = HighlightLanguage.KeyValues)
        {
            const int MaxLengthForCodeBox = 50 * 1024 * 1024;

            // https://github.com/ValveResourceFormat/ValveResourceFormat/issues/840
            if (text.Length > MaxLengthForCodeBox)
            {
                return CreateBasicTextBox(text);
            }

            return new CodeTextBox(text, language);
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

        private void OnDisposed(object? sender, EventArgs e)
        {
            Disposed -= OnDisposed;
            ParentChanged -= OnVisibleChanged;
            VisibleChanged -= OnVisibleChanged;
            TextChanged -= OnTextChanged;
        }

        private void OnVisibleChanged(object? sender, EventArgs e)
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

        private void OnTextChanged(object? sender, TextChangedEventArgs e)
        {
            ClearUndo();

            //e.ChangedRange.SetFoldingMarkers("{", "}");
            //e.ChangedRange.SetFoldingMarkers("\\[", "\\]");
        }

        private static TextBox CreateBasicTextBox(string text)
        {
            var textBox = new TextBox
            {
                Font = GetMonospaceFont(),
                ReadOnly = true,
                Multiline = true,
                WordWrap = false,
                Text = text,
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Both,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Window,
                ForeColor = SystemColors.WindowText,
            };
            return textBox;
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

                range.SetStyle(CommentStyle, CommentRegex());
                range.SetStyle(CommentStyle, XmlCommentRegex());
                range.SetStyle(StringStyle, StringRegex());
                range.SetStyle(NumberStyle, NumberRegex());

                range.ClearFoldingMarkers();
                range.SetFoldingMarkers("{", "}");
                range.SetFoldingMarkers(@"\[", @"\]");
            }

            [GeneratedRegex(@"^<!--.*-->\s*$", RegexOptions.Multiline)]
            private static partial Regex XmlCommentRegex();
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

        public partial class ShaderSyntaxHighlighter : SyntaxHighlighter
        {
            public Style DirectiveStyle { get; set; }
            public Style WordStyle { get; set; }
            public Style UnknownVarStyle { get; set; }

            public ShaderSyntaxHighlighter(FastColoredTextBox currentTb) : base(currentTb)
            {
                CommentStyle = GreenStyle;
                StringStyle = BrownStyle;
                NumberStyle = MagentaStyle;
                KeywordStyle = BlueStyle;
                DirectiveStyle = new TextStyle(Brushes.Goldenrod, null, FontStyle.Bold);
                WordStyle = BlueStyle;
                UnknownVarStyle = GrayStyle;
            }

            public override void HighlightSyntax(Language language, FastColoredTextBoxNS.Range range)
            {
                range.tb.LeftBracket = '(';
                range.tb.RightBracket = ')';
                range.tb.LeftBracket2 = '{';
                range.tb.RightBracket2 = '}';
                range.tb.BracketsHighlightStrategy = BracketsHighlightStrategy.Strategy2;

                range.ClearStyle(StringStyle, CommentStyle, NumberStyle, KeywordStyle, DirectiveStyle, WordStyle, UnknownVarStyle);

                range.SetStyle(CommentStyle, CommentRegex());
                range.SetStyle(StringStyle, StringRegex());
                range.SetStyle(WordStyle, WordRegex());
                range.SetStyle(UnknownVarStyle, UnknownVarRegex());
                range.SetStyle(NumberStyle, NumberRegex());
                range.SetStyle(DirectiveStyle, DirectiveRegex());
                range.SetStyle(KeywordStyle, KeywordRegex());

                range.ClearFoldingMarkers();
                range.SetFoldingMarkers("{", "}");
            }

            [GeneratedRegex(@"(?:#[a-z]+|\b[A-Z][A-Z0-9_]+)\b")]
            private static partial Regex DirectiveRegex();

            [GeneratedRegex(@"\b(?:g|m|gl)_[A-Za-z0-9_]+\b")]
            private static partial Regex WordRegex();

            [GeneratedRegex(@"\b_[0-9]+\b")]
            private static partial Regex UnknownVarRegex();

            // This should be sorted alphabetically.
            [GeneratedRegex(@"\b(?:Allow[0-9]+|BoolAttribute|ByteAddressBuffer|ChildOf1|CreateInputTexture2D|DWORD|DynamicCombo|DynamicComboRule|DynamicComboFromFeature|ExternalDescriptorSet|Feature|FeatureRule|Float2Attribute|Float3Attribute|FloatAttribute|IntAttribute|RenderState|Requires[0-9]+|SamplerComparisonState|SamplerState|StaticCombo|StaticComboRule|StringAttribute|StructuredBuffer|Texture1D|Texture2D|Texture2DArray|Texture3D|TextureAttribute|TextureCube|TextureCubeArray|atomic_uint|attribute|binding|bool|bool2|bool3|bool4|break|buffer|bvec2|bvec3|bvec4|case|cbuffer|centroid|coherent|const|continue|default|discard|do|double|else|false|fixed|fixed2|fixed3|fixed4|flat|float|float1x1|float1x2|float1x3|float1x4|float2|float2x1|float2x2|float2x3|float2x4|float3|float3x1|float3x2|float3x3|float3x4|float4|float4x1|float4x2|float4x3|float4x4|for|half|half2|half3|half4|if|in|inout|int|int2|int3|int4|invariant|ivec2|ivec3|ivec4|layout|mat2|mat3|mat4|matrix|noperspective|out|patch|precise|precision|readonly|restrict|return|sample|sampler|sampler1D|sampler2D|sampler3D|samplerCube|samplerShadow|shared|smooth|static|struct|subroutine|switch|texture|texture2D|texture2DArray|texture3D|textureCube|textureCubeArray|true|uint|uint2|uint3|uint4|uniform|uvec2|uvec3|uvec4|varying|vec2|vec3|vec4|vector|void|volatile|while|writeonly)\b", RegexOptions.ExplicitCapture)]
            private static partial Regex KeywordRegex();
        }

        [GeneratedRegex(@"""""|"".*?[^\\]""")]
        private static partial Regex StringRegex();

        [GeneratedRegex(@"\b(?:[0-9]+[\.]?[0-9]*f?|0x[0-9A-Fa-f]+|true|false|null)\b")]
        private static partial Regex NumberRegex();

        [GeneratedRegex(@"//.*$", RegexOptions.Multiline)]
        private static partial Regex CommentRegex();
    }
}
