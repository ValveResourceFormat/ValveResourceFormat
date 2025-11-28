using System.ComponentModel.Design;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Types.PackageViewer;
using Microsoft.Win32;

namespace GUI.Utils
{
    public static class Themer
    {
        public class ThemeColors
        {
            /// <summary>For background elements like the window itself and titlebars.</summary>
            public required Color App { get; set; }
            /// <summary>For element which need to sit between App and AppSoft colors.</summary>
            public required Color AppMiddle { get; set; }
            /// <summary>For element which need to stand out from the background.</summary>
            public required Color AppSoft { get; set; }

            /// <summary>For borders meant to visually separate parts of the interfact.</summary>
            public required Color Border { get; set; }
            /// <summary>For secondary borders or padding which need to not stand out too much from the background.</summary>
            public required Color BorderSoft { get; set; }

            /// <summary>For any element which needs contrast from the background, like text</summary>
            public required Color Contrast { get; set; }
            /// <summary>For any element which needs contrast but doesn't have to be as visible, like inactive text or scrollbars</summary>
            public required Color ContrastSoft { get; set; }

            /// <summary>For hover state of buttons in the control box except close, (_ [] buttons)</summary>
            public required Color ControlBoxHighlight { get; set; }
            /// <summary>For hover state of close button in the control box, (X button)</summary>
            public required Color ControlBoxHighlightCloseButton { get; set; }

            /// <summary>For anything that needs to be accented like hovering over a tab</summary>
            public required Color Accent { get; set; }

            /// <summary>Sets special windows flags on forms which changes some otherwise unthemeable portions to dark/light</summary>
            public required SystemColorMode ColorMode { get; set; }
        }

        public enum Themes
        {
            Dark,
            Light
        }

        public static readonly Dictionary<Themes, ThemeColors> ThemesColors = new Dictionary<Themes, ThemeColors>
        {
            { Themes.Dark,

                new ThemeColors {
                    App = Color.FromArgb(22, 25, 32),
                    AppMiddle = Color.FromArgb(28, 31, 38),
                    AppSoft = Color.FromArgb(34, 39, 51),

                    Border = Color.FromArgb(51, 57, 74),
                    BorderSoft = Color.FromArgb(41, 45, 55),

                    Contrast = Color.White,
                    ContrastSoft = Color.FromArgb(158, 159, 164),

                    ControlBoxHighlight = Color.FromArgb(67, 67, 67),
                    ControlBoxHighlightCloseButton = Color.FromArgb(240, 20, 20),

                    Accent = Color.FromArgb(99, 161, 255),

                    ColorMode = SystemColorMode.Dark,
                }
            },

            { Themes.Light,
                new ThemeColors
                {
                    App = Color.FromArgb(218, 218, 218),
                    AppMiddle = Color.FromArgb(231, 236, 236),
                    AppSoft = Color.FromArgb(244, 244, 244),

                    Border = Color.FromArgb(230, 230, 230),
                    BorderSoft = Color.FromArgb(245, 245, 245),

                    Contrast = Color.Black,
                    ContrastSoft = Color.FromArgb(109, 109, 109),

                    ControlBoxHighlight = Color.FromArgb(170, 170, 170),
                    ControlBoxHighlightCloseButton = Color.FromArgb(240, 20, 20),

                    Accent = Color.FromArgb(99, 161, 255),

                    ColorMode = SystemColorMode.Classic,
                }
            },

        };

        public static ThemeColors CurrentThemeColors { get; set; } = ThemesColors[Application.IsDarkModeEnabled ? Themes.Dark : Themes.Light];

        public static void ApplyTheme(Form Form)
        {
            Form.BackColor = CurrentThemeColors.App;
            Form.ForeColor = CurrentThemeColors.Contrast;

            Form.ControlAdded += ControlAdded;

            foreach (Control control in Form.Controls)
            {
                void ControlDisposed(object? sender, EventArgs e)
                {
                    control.ControlAdded -= ControlAdded;
                    control.Disposed -= ControlDisposed;
                }

                control.Disposed += ControlDisposed;

                control.ControlAdded += ControlAdded;

                ThemeControl(control);
            }

            void ControlAdded(object? sender, ControlEventArgs e)
            {
                if (e.Control is not null)
                {
                    ThemeControl(e.Control);
                }
            }

            void FormDisposed(object? sender, EventArgs e)
            {
                Form.ControlAdded -= ControlAdded;
                Form.Disposed -= FormDisposed;
            }

            Form.Disposed += FormDisposed;
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
            var borderStyleInfo = control.GetType().GetProperty("BorderStyle");
            if (borderStyleInfo != null)
            {
                var borderStyle = (BorderStyle?)borderStyleInfo.GetValue(control);
                if (borderStyle != BorderStyle.None)
                {
                    borderStyleInfo.SetValue(control, BorderStyle.FixedSingle);
                }
            }

            if (control is Panel panel)
            {
                if (control is not UnstyledPanel)
                {
                    panel.BackColor = panel.Parent?.BackColor ?? CurrentThemeColors.AppSoft;
                    panel.BorderStyle = BorderStyle.None;
                }
            }

            if (control is TableLayoutPanel table)
            {
                table.BackColor = CurrentThemeColors.App;
                table.ForeColor = CurrentThemeColors.Contrast;
                table.BorderStyle = BorderStyle.None;
            }


            if (control is PictureBox pic)
            {
                pic.BorderStyle = BorderStyle.None;
                pic.BackColor = Color.Transparent;
            }
            if (control is Button button)
            {
                if (control is not ThemedButton)
                {
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.CheckedBackColor = CurrentThemeColors.AppSoft;
                    button.BackColor = CurrentThemeColors.Border;
                    button.FlatAppearance.BorderColor = CurrentThemeColors.Border;
                    button.ForeColor = CurrentThemeColors.Contrast;
                }
            }
            if (control is Label label)
            {
                label.BorderStyle = BorderStyle.None;
                label.ForeColor = CurrentThemeColors.Contrast;
                label.BackColor = Color.Transparent;
            }
            if (control is LinkLabel link)
            {
                link.LinkColor = Color.FromArgb(255, 84, 127, 235);
                link.ActiveLinkColor = Color.FromArgb(255, 56, 76, 140);
                link.VisitedLinkColor = Brighten(CurrentThemeColors.Accent, 1.1f);
            }
            if (control is RadioButton opt)
            {
                opt.BackColor = CurrentThemeColors.AppSoft;
            }
            if (control is ComboBox combo)
            {
                combo.ForeColor = CurrentThemeColors.Contrast;
                combo.BackColor = CurrentThemeColors.AppSoft;
            }
            if (control is ToolStrip toolBar)
            {
                toolBar.GripStyle = ToolStripGripStyle.Hidden;
                toolBar.RenderMode = ToolStripRenderMode.Professional;
                toolBar.Renderer = new DarkToolStripRenderer(new CustomColorTable(), false);
            }
            if (control is ContextMenuStrip cMenu)
            {
                cMenu.RenderMode = ToolStripRenderMode.Professional;
                cMenu.Renderer = new DarkToolStripRenderer(new CustomColorTable(), false);
            }
            if (control is DataGridView grid)
            {
                grid.EnableHeadersVisualStyles = false;
                grid.BorderStyle = BorderStyle.None;
                grid.BackgroundColor = CurrentThemeColors.AppSoft;
                grid.GridColor = CurrentThemeColors.Border;

                grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = CurrentThemeColors.AppMiddle };

                grid.DefaultCellStyle.BackColor = CurrentThemeColors.AppSoft;
                grid.DefaultCellStyle.ForeColor = CurrentThemeColors.Contrast;

                grid.DefaultCellStyle.SelectionBackColor = CurrentThemeColors.AppSoft;   //Window Cell color when not in focus or selected
                grid.DefaultCellStyle.SelectionForeColor = CurrentThemeColors.Contrast;

                grid.ColumnHeadersDefaultCellStyle.BackColor = CurrentThemeColors.AppMiddle;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = CurrentThemeColors.Contrast;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = CurrentThemeColors.Border;
                grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;

                grid.RowHeadersDefaultCellStyle.BackColor = CurrentThemeColors.AppSoft;
                grid.RowHeadersDefaultCellStyle.ForeColor = CurrentThemeColors.Contrast;
                grid.RowHeadersDefaultCellStyle.SelectionBackColor = CurrentThemeColors.Border;
                grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            }
            if (control is SettingsControl settingsControl)
            {
                settingsControl.BackColor = CurrentThemeColors.AppSoft;
                settingsControl.ForeColor = CurrentThemeColors.Contrast;
            }
            if (control is ListBox listBox)
            {
                listBox.BackColor = CurrentThemeColors.AppSoft;
                listBox.ForeColor = CurrentThemeColors.Contrast;
            }
            if (control is CheckedListBox checkedListBox)
            {
                checkedListBox.BackColor = CurrentThemeColors.AppSoft;
                checkedListBox.ForeColor = CurrentThemeColors.Contrast;
            }
            if (control is PropertyGrid pGrid)
            {
                pGrid.BackColor = CurrentThemeColors.AppSoft;
                pGrid.ViewBackColor = CurrentThemeColors.AppSoft;
                pGrid.LineColor = CurrentThemeColors.AppSoft;
                pGrid.ViewForeColor = CurrentThemeColors.Contrast;
                pGrid.ViewBorderColor = CurrentThemeColors.Border;
                pGrid.CategoryForeColor = CurrentThemeColors.Contrast;
                pGrid.CategorySplitterColor = CurrentThemeColors.BorderSoft;
            }
            if (control is Splitter splitter)
            {
                splitter.BorderStyle = BorderStyle.None;
            }
            if (control is TrackBar slider)
            {
                slider.BackColor = CurrentThemeColors.AppSoft;
            }
            if (control is CodeTextBox console)
            {
                console.IndentBackColor = CurrentThemeColors.BorderSoft;
                console.ServiceLinesColor = CurrentThemeColors.App;
                console.BackColor = CurrentThemeColors.AppSoft;
                console.FoldingIndicatorColor = CurrentThemeColors.ContrastSoft;
                var col = new FastColoredTextBoxNS.ServiceColors
                {
                    ExpandMarkerBackColor = CurrentThemeColors.ContrastSoft,
                    ExpandMarkerForeColor = CurrentThemeColors.Contrast,
                    CollapseMarkerForeColor = CurrentThemeColors.Contrast,
                    CollapseMarkerBackColor = CurrentThemeColors.App,
                    ExpandMarkerBorderColor = CurrentThemeColors.Border,
                    CollapseMarkerBorderColor = CurrentThemeColors.BorderSoft
                };
                console.ServiceColors = col;
                console.ForeColor = CurrentThemeColors.Contrast;
            }
            if (control is GLViewerSelectionControl glViewerSelectionControl)
            {
                glViewerSelectionControl.ForeColor = CurrentThemeColors.Contrast;
                glViewerSelectionControl.BackColor = CurrentThemeColors.AppMiddle;
            }
            if (control is ControlsBoxPanel controlsBoxPanel)
            {
                controlsBoxPanel.ControlBoxIconColor = CurrentThemeColors.Contrast;
                controlsBoxPanel.ControlBoxHoverColor = CurrentThemeColors.ControlBoxHighlightCloseButton;
                controlsBoxPanel.ControlBoxHoverCloseColor = CurrentThemeColors.ControlBoxHighlight;
            }
            if (control is ByteViewer hexViewer)
            {
                //hexViewer.BackColor = ControlPaint.Dark(ThemeColors.Control, -10);
                //hexViewer.ForeColor = ThemeColors.TextActive;
            }
            if (control.ContextMenuStrip != null)
            {
                ThemeControlInternal(control.ContextMenuStrip);
            }
            if (control is ListView listView)
            {
                listView.BorderStyle = BorderStyle.None;
                listView.ForeColor = CurrentThemeColors.Contrast;
                listView.BackColor = CurrentThemeColors.AppSoft;
            }
            if (control is NumericUpDown numeric)
            {
                numeric.ForeColor = CurrentThemeColors.Contrast;
                numeric.BackColor = numeric.Parent?.BackColor ?? CurrentThemeColors.AppSoft;
            }
            if (control is TreeView treeView)
            {
                treeView.BorderStyle = BorderStyle.None;
                treeView.BackColor = CurrentThemeColors.AppMiddle;
                treeView.ForeColor = CurrentThemeColors.Contrast;
                treeView.LineColor = CurrentThemeColors.BorderSoft;
            }
            if (control is BetterListView betterListView)
            {
                betterListView.ForeColor = CurrentThemeColors.Contrast;
                betterListView.BackColor = CurrentThemeColors.AppSoft;
                betterListView.BorderColor = CurrentThemeColors.Border;
                betterListView.Highlight = CurrentThemeColors.Accent;
            }
            if (control is TabPage tabPage)
            {
                tabPage.Padding = new Padding(-10, 0, 0, 0);
                tabPage.BackColor = tabPage.Parent?.BackColor ?? CurrentThemeColors.AppSoft;
                tabPage.ForeColor = CurrentThemeColors.Contrast;
            }
            if (control is ProgressBar pgBar)
            {
                pgBar.BackColor = pgBar.Parent?.BackColor ?? CurrentThemeColors.AppSoft;
                pgBar.ForeColor = CurrentThemeColors.Accent;
            }
            if (control is SplitContainer splitContainer)
            {
                splitContainer.BackColor = CurrentThemeColors.App;
                splitContainer.BorderStyle = BorderStyle.None;
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
            int r = (int)(color.R * brightnessFactor);
            int g = (int)(color.G * brightnessFactor);
            int b = (int)(color.B * brightnessFactor);

            // Ensure values don't exceed 255
            r = Math.Min(255, r);
            g = Math.Min(255, g);
            b = Math.Min(255, b);

            // Return the new color
            return Color.FromArgb(color.A, r, g, b);
        }
    }

    // Custom Renderers for Menus and ToolBars
    public class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public bool ColorizeIcons { get; set; } = true;

        public DarkToolStripRenderer(ProfessionalColorTable table, bool pColorizeIcons = true) : base(table)
        {
            ColorizeIcons = pColorizeIcons;
        }

        // Background of the whole ToolBar Or MenuBar:
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip.IsDropDown)
            {
                e.ToolStrip.BackColor = Themer.CurrentThemeColors.AppMiddle;
            }
            else
            {
                e.ToolStrip.BackColor = Themer.CurrentThemeColors.App;
            }
            base.OnRenderToolStripBackground(e);

        }

        // For Normal Buttons on a ToolBar:
        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var button = e.Item as ToolStripButton;

            if (button is null)
            {
                return;
            }

            var g = e.Graphics;
            var bounds = new Rectangle(Point.Empty, e.Item.Size);

            var gradientBegin = Themer.CurrentThemeColors.App;
            var gradientEnd = Themer.CurrentThemeColors.App;

            var BordersPencil = new Pen(Themer.CurrentThemeColors.BorderSoft);

            if (button.Pressed || button.Checked)
            {
                gradientBegin = Themer.CurrentThemeColors.AppSoft;
                gradientEnd = Themer.CurrentThemeColors.AppSoft;
            }
            else if (button.Selected)
            {
                gradientBegin = Themer.CurrentThemeColors.Accent;
                gradientEnd = Themer.CurrentThemeColors.Accent;
            }

            using Brush b = new LinearGradientBrush(
                bounds,
                gradientBegin,
                gradientEnd,
                LinearGradientMode.Vertical);

            g.FillRectangle(b, bounds);

            e.Graphics.DrawRectangle(
                BordersPencil,
                bounds);

            g.DrawLine(
                BordersPencil,
                bounds.X,
                bounds.Y,
                bounds.Width - 1,
                bounds.Y);

            g.DrawLine(
                BordersPencil,
                bounds.X,
                bounds.Y,
                bounds.X,
                bounds.Height - 1);

            var toolStrip = button.Owner;

            if (button.Owner != null && button.Owner.GetItemAt(button.Bounds.X, button.Bounds.Bottom + 1) is not ToolStripButton nextItem)
            {
                g.DrawLine(
                    BordersPencil,
                    bounds.X,
                    bounds.Height - 1,
                    bounds.X + bounds.Width - 1,
                    bounds.Height - 1);
            }

            BordersPencil.Dispose();
        }

        // For DropDown Buttons on a ToolBar:
        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var g = e.Graphics;
            var bounds = new Rectangle(Point.Empty, e.Item.Size);
            var gradientBegin = Themer.CurrentThemeColors.App;
            var gradientEnd = Themer.CurrentThemeColors.App;

            using var BordersPencil = new Pen(Themer.CurrentThemeColors.BorderSoft);

            //1. Determine the colors to use:
            if (e.Item.Pressed)
            {
                gradientBegin = Themer.CurrentThemeColors.AppSoft;
                gradientEnd = Themer.CurrentThemeColors.AppSoft;
            }
            else if (e.Item.Selected)
            {
                gradientBegin = Themer.CurrentThemeColors.AppSoft;
                gradientEnd = Themer.CurrentThemeColors.AppSoft;
            }



            //3. Draws the Chevron:
            //int Padding = 2; //<- From the right side
            //Size cSize = new Size(8, 4); //<- Size of the Chevron: 8x4 px
            //Pen ChevronPen = new Pen(MyColors.TextInactive, 2); //<- Color and Border Width
            //Point P1 = new Point(bounds.Width - (cSize.Width + Padding), (bounds.Height / 2) - (cSize.Height / 2));
            //Point P2 = new Point(bounds.Width - Padding, (bounds.Height / 2) - (cSize.Height / 2));
            //Point P3 = new Point(bounds.Width - (cSize.Width / 2 + Padding), (bounds.Height / 2) + (cSize.Height / 2));

            //e.Graphics.DrawLine(ChevronPen, P1, P3);
            //e.Graphics.DrawLine(ChevronPen, P2, P3);
        }

        // For SplitButtons on a ToolBar:
        protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var bounds = new Rectangle(Point.Empty, e.Item.Size);
            var gradientBegin = Themer.CurrentThemeColors.App; // Color.FromArgb(203, 225, 252);
            var gradientEnd = Themer.CurrentThemeColors.App;

            //1. Determine the colors to use:
            if (e.Item.Pressed)
            {
                gradientBegin = Themer.CurrentThemeColors.AppSoft;
                gradientEnd = Themer.CurrentThemeColors.AppSoft;
            }
            else if (e.Item.Selected)
            {
                gradientBegin = Themer.CurrentThemeColors.AppSoft;
                gradientEnd = Themer.CurrentThemeColors.AppSoft;
            }

            //2. Draw the Box around the Control
            using Brush b = new LinearGradientBrush(
                bounds,
                gradientBegin,
                gradientEnd,
                LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(b, bounds);

            //3. Draws the Chevron:
            var Padding = 2; //<- From the right side
            var cSize = new Size(8, 4); //<- Size of the Chevron: 8x4 px
            var ChevronPen = new Pen(Themer.CurrentThemeColors.ContrastSoft, 2); //<- Color and Border Width
            var P1 = new Point(bounds.Width - (cSize.Width + Padding), (bounds.Height / 2) - (cSize.Height / 2));
            var P2 = new Point(bounds.Width - Padding, (bounds.Height / 2) - (cSize.Height / 2));
            var P3 = new Point(bounds.Width - (cSize.Width / 2 + Padding), (bounds.Height / 2) + (cSize.Height / 2));

            e.Graphics.DrawLine(ChevronPen, P1, P3);
            e.Graphics.DrawLine(ChevronPen, P2, P3);

            ChevronPen.Dispose();
        }

        // For the Text Color of all Items:
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

            var text = (e.Text ?? string.Empty).Replace("&", "", StringComparison.Ordinal);

            using var textBrush = new SolidBrush(e.TextColor);
            //e.Graphics.DrawString(text, e.TextFont, textBrush, e.TextRectangle);

            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Themer.CurrentThemeColors.Contrast;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemBackground(ToolStripItemRenderEventArgs e)
        {
            base.OnRenderItemBackground(e);

            //// Only draw border for ComboBox items
            //if (e.Item is ComboBox)
            //{
            //    Rectangle rect = new Rectangle(Point.Empty, e.Item.Size);
            //    e.Graphics.DrawRectangle(new Pen(MyColors.ControlLight, 1), rect);
            //}
            //base.OnRenderToolStripBackground(e);
            //e.Graphics.FillRectangle(new SolidBrush(themeColors.Window), e.Item.Bounds);
        }

        // For Menu Items BackColor:
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var g = e.Graphics;
            var bounds = new Rectangle(Point.Empty, e.Item.Size);

            var gradientBegin = Themer.CurrentThemeColors.App;
            var gradientEnd = Themer.CurrentThemeColors.App;

            var DrawIt = false;
            var _menu = e.Item as ToolStripItem;
            if (_menu.Pressed)
            {
                gradientBegin = Themer.CurrentThemeColors.AppSoft;
                gradientEnd = Themer.CurrentThemeColors.AppSoft;
                DrawIt = true;
            }
            else if (_menu.Selected)
            {
                gradientBegin = Themer.Brighten(Themer.CurrentThemeColors.AppSoft, 1.3f);
                gradientEnd = Themer.Brighten(Themer.CurrentThemeColors.AppSoft, 1.3f);
                DrawIt = true;
            }

            if (DrawIt)
            {
                using Brush b = new LinearGradientBrush(
                bounds,
                gradientBegin,
                gradientEnd,
                LinearGradientMode.Vertical);
                g.FillRectangle(b, bounds);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
        }
    }

    public class CustomColorTable : ProfessionalColorTable
    {

        public CustomColorTable()
        {
            UseSystemColors = false;
        }

        public override Color ImageMarginGradientBegin => Color.Transparent;
        public override Color ImageMarginGradientMiddle => Color.Transparent;
        public override Color ImageMarginGradientEnd => Color.Transparent;
    }

}
