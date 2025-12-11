using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Types.PackageViewer;
using SkiaSharp;
using Svg.Skia;

namespace GUI.Utils
{
    public static class Themer
    {
        public class ThemeColors
        {
            /// <summary>For background elements like the window itself and titlebars.</summary>
            public required Color App { get; init; }
            /// <summary>For element which need to stand out from the background.</summary>
            public required Color AppMiddle { get; init; }
            /// <summary>For element which need to sit between App and AppSoft colors.</summary>
            public required Color AppSoft { get; init; }

            /// <summary>For borders meant to visually separate parts of the interface.</summary>
            public required Color Border { get; init; }

            /// <summary>For any element which needs contrast from the background, like text</summary>
            public required Color Contrast { get; init; }
            /// <summary>For any element which needs contrast but doesn't have to be as visible, like inactive text or scrollbars</summary>
            public required Color ContrastSoft { get; init; }

            /// <summary>For hover state of buttons in the control box except close, (_ [] buttons)</summary>
            public required Color ControlBoxHighlight { get; init; }
            /// <summary>For hover state of close button in the control box, (X button)</summary>
            public required Color ControlBoxHighlightCloseButton { get; init; }

            /// <summary>For anything that needs to be accented like hovering over a tab</summary>
            public required Color HoverAccent { get; init; }

            /// <summary>For anything that needs to be accented</summary>
            public required Color Accent { get; init; }

            /// <summary>Sets special windows flags on forms which changes some otherwise unthemeable portions to dark/light</summary>
            public required SystemColorMode ColorMode { get; init; }
        }

        // This enum is used to store the setting of which theme the user has selected, keep consistent.
        public enum AppTheme
        {
            System = 0,
            Light = 1,
            Dark = 2,
        }

        private static readonly ThemeColors DarkTheme = new()
        {
            App = Color.FromArgb(22, 25, 32),
            AppMiddle = Color.FromArgb(34, 39, 51),
            AppSoft = Color.FromArgb(44, 49, 61),

            Border = Color.FromArgb(51, 57, 74),

            Contrast = Color.White,
            ContrastSoft = Color.FromArgb(158, 159, 164),

            ControlBoxHighlight = Color.FromArgb(67, 67, 67),
            ControlBoxHighlightCloseButton = Color.FromArgb(240, 20, 20),

            HoverAccent = Color.FromArgb(0, 66, 151),
            Accent = Color.FromArgb(99, 161, 255),

            ColorMode = SystemColorMode.Dark,
        };

        private static readonly ThemeColors LightTheme = new()
        {
            App = Color.FromArgb(218, 218, 218),
            AppMiddle = Color.FromArgb(236, 236, 236),
            AppSoft = Color.FromArgb(251, 251, 251),

            Border = Color.FromArgb(188, 188, 188),

            Contrast = Color.Black,
            ContrastSoft = Color.FromArgb(80, 80, 80),

            ControlBoxHighlight = Color.FromArgb(170, 170, 170),
            ControlBoxHighlightCloseButton = Color.FromArgb(240, 20, 20),

            HoverAccent = Color.FromArgb(0, 66, 151),
            Accent = Color.FromArgb(99, 161, 255),

            ColorMode = SystemColorMode.Classic,
        };

        public static AppTheme CurrentTheme { get; private set; } = AppTheme.Light;
        public static ThemeColors CurrentThemeColors { get; private set; } = LightTheme;

        public static void InitializeTheme()
        {
            var theme = (AppTheme)Settings.Config.Theme;

            if (theme == AppTheme.System || !Enum.IsDefined<AppTheme>(theme))
            {
                theme = Application.SystemColorMode == SystemColorMode.Dark ? AppTheme.Dark : AppTheme.Light;
            }

            CurrentTheme = theme;
            CurrentThemeColors = theme == AppTheme.Dark ? DarkTheme : LightTheme;

            Application.SetColorMode(CurrentThemeColors.ColorMode);
        }

        public static void ApplyTheme(Form Form)
        {
            Form.BackColor = CurrentThemeColors.App;
            Form.ForeColor = CurrentThemeColors.Contrast;

            ThemeControl(Form);
        }

        /// <summary>Recursively apply the Colors from 'ThemeColors' to the Control and all its childs.</summary>
        public static void ThemeControl(Control control)
        {
            control.SuspendLayout();

            ThemeControlInternal(control);

            control.ResumeLayout(true);
        }

        private static void ThemeControlInternal(Control control)
        {
            if (control is ExplorerControl or TreeViewWithSearchResults)
            {
                return;
            }

            var borderStyleInfo = control.GetType().GetProperty("BorderStyle");
            if (borderStyleInfo != null)
            {
                var borderStyle = (BorderStyle?)borderStyleInfo.GetValue(control);
                if (borderStyle != BorderStyle.None)
                {
                    borderStyleInfo.SetValue(control, BorderStyle.FixedSingle);
                }
            }

            if (control is Panel panel and not UnstyledPanel)
            {
                panel.BackColor = panel.Parent?.BackColor ?? CurrentThemeColors.AppMiddle;
                panel.BorderStyle = BorderStyle.None;
            }

            if (control is TableLayoutPanel table)
            {
                table.BackColor = table.Parent?.BackColor ?? CurrentThemeColors.App;
                table.ForeColor = CurrentThemeColors.Contrast;
                table.BorderStyle = BorderStyle.None;
            }

            if (control is PictureBox pic)
            {
                pic.BorderStyle = BorderStyle.None;
                pic.BackColor = Color.Transparent;
            }
            if (control is Button button and not ThemedButton)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.CheckedBackColor = CurrentThemeColors.AppMiddle;
                button.BackColor = CurrentThemeColors.Border;
                button.FlatAppearance.BorderColor = CurrentThemeColors.Border;
                button.ForeColor = CurrentThemeColors.Contrast;
            }
            if (control is Label label)
            {
                label.BorderStyle = BorderStyle.None;
                label.ForeColor = CurrentThemeColors.Contrast;
                label.BackColor = Color.Transparent;
            }
            if (control is RadioButton opt)
            {
                opt.BackColor = opt.Parent?.BackColor ?? CurrentThemeColors.AppMiddle;
            }
            if (control is ComboBox combo)
            {
                combo.ForeColor = CurrentThemeColors.Contrast;
                combo.BackColor = combo.Parent?.BackColor ?? CurrentThemeColors.AppMiddle;
            }
            if (control is GroupBox groupBox)
            {
                groupBox.ForeColor = CurrentThemeColors.Contrast;
                groupBox.BackColor = groupBox.Parent?.BackColor ?? CurrentThemeColors.AppMiddle;
            }
            if (control is DataGridView grid)
            {
                grid.EnableHeadersVisualStyles = false;
                grid.BorderStyle = BorderStyle.None;
                grid.BackgroundColor = CurrentThemeColors.AppMiddle;
                grid.GridColor = CurrentThemeColors.Border;

                grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = CurrentThemeColors.AppSoft };

                grid.DefaultCellStyle.BackColor = CurrentThemeColors.AppMiddle;
                grid.DefaultCellStyle.ForeColor = CurrentThemeColors.Contrast;

                grid.DefaultCellStyle.SelectionBackColor = CurrentThemeColors.Accent;   //Window Cell color when not in focus or selected
                grid.DefaultCellStyle.SelectionForeColor = CurrentThemeColors.Contrast;

                grid.ColumnHeadersDefaultCellStyle.BackColor = CurrentThemeColors.AppSoft;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = CurrentThemeColors.Contrast;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = CurrentThemeColors.Accent;
                grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;

                grid.RowHeadersDefaultCellStyle.BackColor = CurrentThemeColors.AppMiddle;
                grid.RowHeadersDefaultCellStyle.ForeColor = CurrentThemeColors.Contrast;
                grid.RowHeadersDefaultCellStyle.SelectionBackColor = CurrentThemeColors.Accent;
                grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            }
            if (control is SettingsControl settingsControl)
            {
                settingsControl.BackColor = CurrentThemeColors.AppMiddle;
                settingsControl.ForeColor = CurrentThemeColors.Contrast;
            }
            if (control is ListBox listBox)
            {
                listBox.BackColor = CurrentThemeColors.AppMiddle;
                listBox.ForeColor = CurrentThemeColors.Contrast;
            }
            if (control is CheckedListBox checkedListBox)
            {
                checkedListBox.BackColor = CurrentThemeColors.AppMiddle;
                checkedListBox.ForeColor = CurrentThemeColors.Contrast;
                checkedListBox.BorderStyle = BorderStyle.None;
            }
            if (control is PropertyGrid pGrid)
            {
                pGrid.BackColor = CurrentThemeColors.AppMiddle;
                pGrid.ViewBackColor = CurrentThemeColors.AppMiddle;
                pGrid.LineColor = CurrentThemeColors.AppMiddle;
                pGrid.ViewForeColor = CurrentThemeColors.Contrast;
                pGrid.ViewBorderColor = CurrentThemeColors.Border;
                pGrid.CategoryForeColor = CurrentThemeColors.Contrast;
                pGrid.CategorySplitterColor = CurrentThemeColors.Border;
            }
            if (control is Splitter splitter)
            {
                splitter.BorderStyle = BorderStyle.None;
            }
            if (control is TrackBar slider)
            {
                slider.BackColor = CurrentThemeColors.AppMiddle;
            }
            if (control is TextBox textBox)
            {
                textBox.BackColor = textBox.Parent?.BackColor ?? CurrentThemeColors.AppMiddle;
                textBox.ForeColor = CurrentThemeColors.Contrast;
            }
            if (control is CodeTextBox console)
            {
                console.IndentBackColor = CurrentThemeColors.Border;
                console.ServiceLinesColor = CurrentThemeColors.App;
                console.BackColor = CurrentThemeColors.AppMiddle;
                console.FoldingIndicatorColor = CurrentThemeColors.ContrastSoft;
                var col = new FastColoredTextBoxNS.ServiceColors
                {
                    ExpandMarkerBackColor = CurrentThemeColors.ContrastSoft,
                    ExpandMarkerForeColor = CurrentThemeColors.Contrast,
                    CollapseMarkerForeColor = CurrentThemeColors.Contrast,
                    CollapseMarkerBackColor = CurrentThemeColors.App,
                    ExpandMarkerBorderColor = CurrentThemeColors.Border,
                    CollapseMarkerBorderColor = CurrentThemeColors.Border
                };
                console.ServiceColors = col;
                console.ForeColor = CurrentThemeColors.Contrast;
            }
            if (control is ControlsBoxPanel controlsBoxPanel)
            {
                controlsBoxPanel.ControlBoxIconColor = CurrentThemeColors.Contrast;
                controlsBoxPanel.ControlBoxHoverColor = CurrentThemeColors.ControlBoxHighlight;
                controlsBoxPanel.ControlBoxHoverCloseColor = CurrentThemeColors.ControlBoxHighlightCloseButton;
            }
            if (control.ContextMenuStrip != null)
            {
                ThemeControlInternal(control.ContextMenuStrip);
            }
            if (control is ListView listView)
            {
                listView.BorderStyle = BorderStyle.None;
                listView.ForeColor = CurrentThemeColors.Contrast;
                listView.BackColor = CurrentThemeColors.AppMiddle;
            }
            if (control is NumericUpDown numeric)
            {
                numeric.ForeColor = CurrentThemeColors.Contrast;
                numeric.BackColor = numeric.Parent?.BackColor ?? CurrentThemeColors.AppMiddle;
            }
            if (control is TreeView treeView)
            {
                treeView.BorderStyle = BorderStyle.None;
                treeView.BackColor = CurrentThemeColors.AppSoft;
                treeView.ForeColor = CurrentThemeColors.Contrast;
                treeView.LineColor = CurrentThemeColors.Border;
            }
            if (control is BetterListView betterListView)
            {
                betterListView.ForeColor = CurrentThemeColors.Contrast;
                betterListView.BackColor = CurrentThemeColors.AppMiddle;
                betterListView.BorderColor = CurrentThemeColors.Border;
                betterListView.Highlight = CurrentThemeColors.Accent;
            }
            if (control is TabPage tabPage)
            {
                tabPage.Padding = new Padding(-10, 0, 0, 0);
                tabPage.BackColor = tabPage.Parent?.BackColor ?? CurrentThemeColors.AppMiddle;
                tabPage.ForeColor = CurrentThemeColors.Contrast;
            }
            if (control is ProgressBar pgBar)
            {
                pgBar.BackColor = CurrentThemeColors.AppMiddle;
                pgBar.ForeColor = CurrentThemeColors.Accent;
            }
            if (control is SplitContainer splitContainer)
            {
                splitContainer.BackColor = splitContainer.Parent?.BackColor ?? CurrentThemeColors.App;
                splitContainer.BorderStyle = BorderStyle.None;
            }
            if (control is SplitterPanel splitterPanel)
            {
                splitterPanel.BackColor = splitterPanel.Parent?.BackColor ?? CurrentThemeColors.App;
                splitterPanel.BorderStyle = BorderStyle.None;
            }
            if (control is HSVSlider hSVSlider)
            {
                hSVSlider.KnobColor = CurrentThemeColors.Contrast;
            }
            if (control is RichTextBox richTextBox)
            {
                richTextBox.BackColor = CurrentThemeColors.App;
                richTextBox.ForeColor = CurrentThemeColors.Contrast;
                richTextBox.BorderStyle = BorderStyle.None;
            }

            foreach (Control childControl in control.Controls)
            {
                // Recursively process its children
                ThemeControlInternal(childControl);
            }
        }

        public static int AdjustForDPI(this Control control, float value)
        {
            return (int)(value * control.DeviceDpi / 96f);
        }

        public static Color Brighten(Color color, float brightnessFactor)
        {
            // Ensure brightnessFactor is within valid range (can be extended if necessary)
            brightnessFactor = Math.Max(0, brightnessFactor);

            // Adjust each color channel by multiplying it with the brightness factor
            var r = (int)(color.R * brightnessFactor);
            var g = (int)(color.G * brightnessFactor);
            var b = (int)(color.B * brightnessFactor);

            // Ensure values don't exceed 255
            r = Math.Min(255, r);
            g = Math.Min(255, g);
            b = Math.Min(255, b);

            // Return the new color
            return Color.FromArgb(color.A, r, g, b);
        }

        public static GraphicsPath GetRoundedRect(Rectangle bounds, int radius, bool onlyTop = false)
        {
            var diameter = radius * 2;
            var arc = new Rectangle(bounds.Location.X, bounds.Location.Y, diameter, diameter);
            var path = new GraphicsPath();

            if (radius == 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            // top left arc
            path.AddArc(arc, 180, 90);

            // top right arc
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);

            if (onlyTop)
            {
                // Right edge down to bottom-right inverse curve
                path.AddLine(bounds.Right, bounds.Y + radius, bounds.Right, bounds.Bottom - radius);

                // Bottom-right inverse arc - curves outward to the right
                arc = new Rectangle(bounds.Right, bounds.Bottom - diameter, diameter, diameter);
                path.AddArc(arc, 180, -90);

                // Bottom edge
                path.AddLine(bounds.Right + radius, bounds.Bottom, bounds.Left - radius, bounds.Bottom);

                // Bottom-left inverse arc - curves outward to the left
                arc = new Rectangle(bounds.Left - diameter, bounds.Bottom - diameter, diameter, diameter);
                path.AddArc(arc, 90, -90);

                // Left edge back up to top-left
                path.AddLine(bounds.Left, bounds.Bottom - radius, bounds.Left, bounds.Y + radius);
            }
            else
            {
                // bottom right arc
                arc.Y = bounds.Bottom - diameter;
                path.AddArc(arc, 0, 90);

                // bottom left arc
                arc.X = bounds.Left;
                path.AddArc(arc, 90, 90);
            }

            path.CloseFigure();
            return path;
        }

        public static Bitmap SvgToBitmap(SKSvg svg, int width, int height)
        {
            if (svg.Picture == null)
            {
                throw new InvalidOperationException("SKSVG must contain an image");
            }

            using var skBitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(skBitmap);

            canvas.Clear(SKColors.Transparent);

            var scaleX = width / svg.Picture.CullRect.Width;
            var scaleY = height / svg.Picture.CullRect.Height;
            var scale = Math.Min(scaleX, scaleY);

            canvas.Scale(scale);
            canvas.DrawPicture(svg.Picture);
            canvas.Flush();

            return skBitmap.ToBitmap();
        }
    }

    // Custom Renderers for Menus and ToolBars
    public class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public DarkToolStripRenderer(ProfessionalColorTable table) : base(table)
        {
        }

        // Background of the whole ToolBar Or MenuBar:
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            if (!e.ToolStrip.IsDropDown)
            {
                e.ToolStrip.BackColor = Themer.CurrentThemeColors.App;
            }

            base.OnRenderToolStripBackground(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            if (e.ToolStrip is null)
            {
                return;
            }

            var g = e.Graphics;
            var bounds = new Rectangle(Point.Empty, e.Item.Size);
            using var separatorPen = new Pen(Themer.CurrentThemeColors.Border, e.ToolStrip.AdjustForDPI(2));

            if (e.Vertical)
            {
                var centerX = bounds.Width / 2;

                g.DrawLine(
                    separatorPen,
                    centerX,
                    bounds.Top,
                    centerX,
                    bounds.Bottom);
            }
            else
            {
                var centerY = bounds.Height / 2;

                g.DrawLine(
                    separatorPen,
                    bounds.Left,
                    centerY,
                    bounds.Right,
                    centerY);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item.Enabled)
            {
                e.TextColor = Themer.CurrentThemeColors.Contrast;
            }
            else
            {
                e.TextColor = Themer.CurrentThemeColors.ContrastSoft;
            }

            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Themer.CurrentThemeColors.Contrast;
            base.OnRenderArrow(e);
        }
    }

    public class CustomColorTable : ProfessionalColorTable
    {
        public CustomColorTable()
        {
            UseSystemColors = false;
        }

        public override Color ToolStripDropDownBackground => Themer.CurrentThemeColors.AppMiddle;
        public override Color MenuBorder => Themer.CurrentThemeColors.Border;
        public override Color MenuItemBorder => Themer.CurrentThemeColors.Accent;
        public override Color MenuItemPressedGradientBegin => Themer.CurrentThemeColors.HoverAccent;
        public override Color MenuItemPressedGradientEnd => Themer.CurrentThemeColors.HoverAccent;
        public override Color MenuItemSelectedGradientBegin => Themer.CurrentThemeColors.HoverAccent;
        public override Color MenuItemSelectedGradientEnd => Themer.CurrentThemeColors.HoverAccent;
        public override Color ImageMarginGradientBegin => Color.Transparent;
        public override Color ImageMarginGradientMiddle => Color.Transparent;
        public override Color ImageMarginGradientEnd => Color.Transparent;
    }
}
