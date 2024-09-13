using System.ComponentModel.Design;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GUI;
using GUI.Controls;
using GUI.Types.PackageViewer;
using GUI.Utils;
using Microsoft.Win32;
using Windows.Win32;

namespace DarkModeForms
{
    /// <summary>This tries to automatically apply Windows Dark Mode (if enabled) to a Form.
    /// <para>Author: DarkModeForms (DarkModeForms.play@gmail.com)  2024</para></summary>
    public partial class DarkModeCS
    {
        /// <summary>'true' if Dark Mode Color is set in Windows's Settings.</summary>
        public bool IsDarkMode { get; set; }

        /// <summary>Windows Colors. Can be customized.</summary>
        public ThemeColors ThemeColors { get; set; }

        private static bool DebugTheme;

        /// <summary>Constructor.</summary>
        public DarkModeCS(bool debugTheme = false)
        {
            DebugTheme = debugTheme;
            SystemEvents.UserPreferenceChanged += new UserPreferenceChangedEventHandler(OnUserPreferenceChanged);
            IsDarkMode = IsWindowsDarkThemed();
            ThemeColors = GetAppTheme();
        }

        /// <summary>This tries to style and automatically apply Windows Dark Mode (if enabled) to a Form.</summary>
        /// <param name="_Form">The Form to become Dark</param>
        public void Style(Form _Form)
        {
            ApplyTheme(_Form);
            ApplySystemTheme(_Form);
        }

        /// <summary>Recursively apply the Colors from 'ThemeColors' to the Control and all its childs.</summary>
        /// <param name="control">Can be a Form or any Winforms Control.</param>
        public void ThemeControl(Control control)
        {
            var BStyle = BorderStyle.FixedSingle;
            var FStyle = FlatStyle.Flat;

            var borderStyleInfo = control.GetType().GetProperty("BorderStyle");
            if (borderStyleInfo != null)
            {
                var borderStyle = (BorderStyle)borderStyleInfo.GetValue(control);
                if ((BorderStyle)borderStyle != BorderStyle.None)
                {
                    borderStyleInfo.SetValue(control, BStyle);
                }
            }

            if (control is Panel panel)
            {
                if (panel.Name == "controlsPanel")
                {
                    panel.BackColor = ThemeColors.AppSoft;
                }
                else
                {
                    panel.BackColor = panel.Parent.BackColor;
                }
                panel.BorderStyle = BorderStyle.None;

            }
            if (control is MainformBottomPanel mainformBottomPanel)
            {
                mainformBottomPanel.SeparatorColor = ThemeColors.Border;
            }
            if (control is BetterGroupBox group)
            {
                group.BackColor = ThemeColors.AppFirm;
                group.ForeColor = ThemeColors.Contrast;
                group.BorderColor = ThemeColors.Border;
            }
            if (control is TableLayoutPanel table)
            {
                table.BackColor = ThemeColors.AppFirm;
                table.BorderStyle = BorderStyle.None;
            }
            if (control is FlatTabControl fTab)
            {
                if (fTab.Name == "mainTabs")
                {
                    fTab.BackColor = ThemeColors.App;
                }
                else
                {
                    fTab.BackColor = ThemeColors.AppSoft;
                }
                fTab.TabColor = ThemeColors.AppSoft;
                fTab.SelectTabColor = ThemeColors.AppSoft;
                fTab.SelectedForeColor = ThemeColors.Contrast;
                fTab.BorderColor = ThemeColors.Border;
                fTab.ForeColor = ThemeColors.ContrastSoft;
                fTab.LineColor = ThemeColors.Accent;
                fTab.HoverColor = ThemeColors.Accent;
                //fTab.Margin = new Padding(-10, 0, 0, 0);
            }
            if (control is PictureBox pic)
            {
                pic.BorderStyle = BorderStyle.None;
                pic.BackColor = Color.Transparent;
            }
            if (control is Button button)
            {
                // Let this be styled in the designer
                if (control is SysMenuLogoButton)
                {
                    return;
                }

                button.FlatStyle = FStyle;
                button.FlatAppearance.CheckedBackColor = ThemeColors.AppFirm;
                button.BackColor = ThemeColors.BorderSoft;
                button.FlatAppearance.BorderColor = ThemeColors.BorderSoft;
                button.ForeColor = ThemeColors.Contrast;
            }
            if (control is Label label)
            {
                label.BorderStyle = BorderStyle.None;
                label.ForeColor = ThemeColors.Contrast;
                label.BackColor = Color.Transparent;
            }
            if (control is LinkLabel link)
            {
                link.LinkColor = ThemeColors.Accent;
                link.VisitedLinkColor = ThemeColors.Accent;
            }
            if (control is BetterCheckBox chk)
            {
                chk.BackColor = Color.Transparent;
                chk.ForeColor = ThemeColors.Contrast;
            }
            if (control is BetterCheckBox betterChk)
            {
                betterChk.BackColor = Color.Transparent;
                betterChk.ForeColor = ThemeColors.Contrast;
                betterChk.AccentColor = ThemeColors.Accent;
                betterChk.FillColor = ThemeColors.App;
                betterChk.BorderColor = ThemeColors.Border;
            }
            if (control is RadioButton opt)
            {
                opt.BackColor = ThemeColors.AppFirm;
            }
            if (control is ComboBox combo)
            {
                combo.ForeColor = ThemeColors.Contrast;
                combo.BackColor = ThemeColors.AppSoft;
            }
            if (control is TransparentMenuStrip menu)
            {
                menu.RenderMode = ToolStripRenderMode.Professional;
                menu.Renderer = new DarkToolStripRenderer(new CustomColorTable(ThemeColors), false)
                {
                    themeColors = ThemeColors
                };
            }
            if (control is ToolStrip toolBar)
            {
                toolBar.GripStyle = ToolStripGripStyle.Hidden;
                toolBar.RenderMode = ToolStripRenderMode.Professional;
                toolBar.Renderer = new DarkToolStripRenderer(new CustomColorTable(ThemeColors), false) { themeColors = ThemeColors };
            }
            if (control is ContextMenuStrip cMenu)
            {
                cMenu.RenderMode = ToolStripRenderMode.Professional;
                cMenu.Renderer = new DarkToolStripRenderer(new CustomColorTable(ThemeColors), false) { themeColors = ThemeColors };
            }
            if (control is DataGridView grid)
            {
                grid.EnableHeadersVisualStyles = false;
                grid.BorderStyle = BorderStyle.FixedSingle;
                grid.BackgroundColor = ThemeColors.AppFirm;
                grid.GridColor = ThemeColors.BorderSoft;

                grid.DefaultCellStyle.BackColor = ThemeColors.AppSoft;
                grid.DefaultCellStyle.ForeColor = ThemeColors.Contrast;

                grid.ColumnHeadersDefaultCellStyle.BackColor = ThemeColors.AppSoft;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = ThemeColors.Contrast;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = ThemeColors.Accent;
                grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;

                grid.RowHeadersDefaultCellStyle.BackColor = ThemeColors.AppSoft;
                grid.RowHeadersDefaultCellStyle.ForeColor = ThemeColors.Contrast;
                grid.RowHeadersDefaultCellStyle.SelectionBackColor = ThemeColors.Accent;
                grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            }
            if (control is PropertyGrid pGrid)
            {
                pGrid.BackColor = ThemeColors.AppSoft;
                pGrid.ViewBackColor = ThemeColors.AppSoft;
                pGrid.LineColor = ThemeColors.AppSoft;
                pGrid.ViewForeColor = ThemeColors.Contrast;
                pGrid.ViewBorderColor = ThemeColors.Border;
                pGrid.CategoryForeColor = ThemeColors.Contrast;
                pGrid.CategorySplitterColor = ThemeColors.BorderSoft;
            }
            if (control is TreeView tree)
            {
                tree.BorderStyle = BorderStyle.None;
                tree.BackColor = ThemeColors.AppFirm;
            }
            if (control is TrackBar slider)
            {
                slider.BackColor = ThemeColors.AppSoft;
            }
            if (control is CodeTextBox console)
            {
                console.IndentBackColor = ThemeColors.BorderSoft;
                console.ServiceLinesColor = ThemeColors.App;
                console.BackColor = ThemeColors.AppFirm;
                console.FoldingIndicatorColor = ThemeColors.ContrastSoft;
                var col = new FastColoredTextBoxNS.ServiceColors
                {
                    ExpandMarkerBackColor = ThemeColors.ContrastSoft,
                    ExpandMarkerForeColor = ThemeColors.Contrast,
                    CollapseMarkerForeColor = ThemeColors.Contrast,
                    CollapseMarkerBackColor = ThemeColors.App,
                    ExpandMarkerBorderColor = ThemeColors.Border,
                    CollapseMarkerBorderColor = ThemeColors.BorderSoft
                };
                console.ServiceColors = col;
                console.ForeColor = ThemeColors.Contrast;
            }
            if (control is ByteViewer hexViewer)
            {
                //hexViewer.BackColor = ControlPaint.Dark(ThemeColors.Control, -10);
                //hexViewer.ForeColor = ThemeColors.TextActive;
            }
            if (control.ContextMenuStrip != null)
            {
                ThemeControl(control.ContextMenuStrip);
            }
            if (control is GLViewerMultiSelectionControl multiSelection)
            {
                multiSelection.BackColor = ThemeColors.AppSoft;
                multiSelection.ForeColor = ThemeColors.Contrast;
            }
            if (control is ControlPanelView controlPanelView)
            {
                controlPanelView.BackColor = Color.Transparent;
                controlPanelView.Invalidate();
            }
            if (control is BetterListBox listBox)
            {
                listBox.ForeColor = ThemeColors.Contrast;
                listBox.BackColor = ThemeColors.AppSoft;
                listBox.BorderColor = ThemeColors.Border;
            }
            if (control is BetterCheckedListBox checkedListBox)
            {
                checkedListBox.ForeColor = ThemeColors.Contrast;
                checkedListBox.BackColor = ThemeColors.AppSoft;
                checkedListBox.BorderColor = ThemeColors.Border;
            }
            if (control is NumericUpDown numeric)
            {
                numeric.ForeColor = ThemeColors.Contrast;
                numeric.BackColor = numeric.Parent.BackColor;
            }
            if (control is BetterTextBox textBox)
            {
                textBox.ForeColor = ThemeColors.Contrast;
                textBox.BackColor = ThemeColors.AppFirm;
                textBox.BorderColor = ThemeColors.Border;
            }
            if (control is BetterListView listView)
            {
                listView.BackColor = ThemeColors.AppFirm;
                listView.ForeColor = ThemeColors.Contrast;
            }
            if (control is TreeView treeView)
            {
                treeView.BackColor = ThemeColors.AppSoft;
                treeView.ForeColor = ThemeColors.Contrast;
                treeView.LineColor = ThemeColors.BorderSoft;
            }
            if (control is TabPage tabPage)
            {
                tabPage.Padding = new Padding(-10, 0, 0, 0);
                tabPage.BackColor = tabPage.Parent.BackColor;
                tabPage.ForeColor = ThemeColors.Contrast;
            }
            if (control is ProgressBar pgBar)
            {
                pgBar.BackColor = pgBar.Parent.BackColor;
                pgBar.ForeColor = ThemeColors.Accent;
            }
            if (control is ControlsBoxPanel controlsBoxPanel)
            {
                controlsBoxPanel.ControlBoxIconColor = ThemeColors.Contrast;
                controlsBoxPanel.ControlBoxHoverColor = ThemeColors.ControlBoxHighlightCloseButton;
                controlsBoxPanel.ControlBoxHoverCloseColor = ThemeColors.ControlBoxHighlight;
            }
            if (control is SplitContainer splitContainer)
            {
                splitContainer.BackColor = ThemeColors.App;
            }

            ApplySystemTheme(control);

            foreach (Control childControl in control.Controls)
            {
                // Recursively process its children
                ThemeControl(childControl);
            }
        }

        /// <summary>Returns Windows Color Mode for Applications.
        /// <para>true=dark theme, false=light theme</para>
        /// </summary>
        public static bool IsWindowsDarkThemed()
        {
            var theme = (Settings.AppTheme)Settings.Config.Theme;

            if (theme != Settings.AppTheme.System)
            {
                return theme == Settings.AppTheme.Dark;
            }

            var intResult = 1;

            try
            {
                intResult = (int)Registry.GetValue(
                    @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme",
                    -1);
            }
            catch
            {
                intResult = 1;
            }

            return intResult <= 0;
        }

        public ThemeColors GetAppTheme()
        {
            var themeColors = new ThemeColors();

            if (IsDarkMode)
            {
                themeColors.App = Color.FromArgb(22, 25, 32);
                themeColors.AppFirm = Color.FromArgb(27, 31, 41);
                themeColors.AppSoft = Color.FromArgb(34, 39, 51);

                themeColors.Border = Color.FromArgb(41, 47, 64);
                themeColors.BorderSoft = Color.FromArgb(41, 45, 55);

                themeColors.Contrast = Color.White;
                themeColors.ContrastSoft = Color.FromArgb(158, 159, 164);

                themeColors.ControlBoxHighlight = Color.FromArgb(67, 67, 67);
                themeColors.ControlBoxHighlightCloseButton = Color.FromArgb(240, 20, 20);

                themeColors.Accent = Color.FromArgb(99, 161, 255);
            }
            else
            {
                themeColors.App = Color.FromArgb(218, 218, 218);
                themeColors.AppFirm = Color.FromArgb(255, 255, 255);
                themeColors.AppSoft = Color.FromArgb(244, 244, 244);

                themeColors.Border = Color.FromArgb(230, 230, 230);
                themeColors.BorderSoft = Color.FromArgb(245, 245, 245);

                themeColors.Contrast = Color.Black;
                themeColors.ContrastSoft = Color.FromArgb(109, 109, 109);

                themeColors.ControlBoxHighlight = Color.FromArgb(170, 170, 170);
                themeColors.ControlBoxHighlightCloseButton = Color.FromArgb(240, 20, 20);

                themeColors.Accent = Color.FromArgb(99, 161, 255);
            }

            if (DebugTheme)
            {
                themeColors.App = Color.FromArgb(1, 202, 247);
                themeColors.AppFirm = Color.FromArgb(210, 210, 210);
                themeColors.AppSoft = Color.FromArgb(247, 160, 177);

                themeColors.Border = Color.FromArgb(255, 255, 255);
                themeColors.BorderSoft = Color.FromArgb(235, 235, 235);

                themeColors.Contrast = Color.Black;
                themeColors.ContrastSoft = Color.FromArgb(109, 109, 109);

                themeColors.ControlBoxHighlight = Color.FromArgb(0, 180, 242);
                themeColors.ControlBoxHighlightCloseButton = Color.FromArgb(247, 160, 177);

                themeColors.Accent = Color.FromArgb(255, 255, 255);
            }

            return themeColors;
        }

        /// <summary>Recolor image</summary>
        /// <param name="bmp">Image to recolor</param>
        /// <param name="c">Color</param>
        public static Bitmap ChangeToColor(Bitmap bmp, Color c)
        {
            var bmp2 = new Bitmap(bmp.Width, bmp.Height);
            using (var g = Graphics.FromImage(bmp2))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;

                var tR = c.R / 255f;
                var tG = c.G / 255f;
                var tB = c.B / 255f;

                var colorMatrix = new System.Drawing.Imaging.ColorMatrix(
                [
                [1, 0, 0, 0, 0],
                    [0, 1, 0, 0, 0],
                    [0, 0, 1, 0, 0],
                    [0, 0, 0, 1, 0],  //<- not changing alpha
                    [tR, tG, tB, 0, 1]
                ]);

                var attributes = new System.Drawing.Imaging.ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);

                g.DrawImage(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height),
                    0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attributes);

                attributes.Dispose();
            }
            return bmp2;
        }

        public static Image ChangeToColor(Image bmp, Color c) => (Image)ChangeToColor((Bitmap)bmp, c);

        private void ApplyTheme(Form _Form)
        {
            if (ThemeColors != null)
            {
                if (_Form != null && _Form.Controls != null)
                {
                    _Form.BackColor = ThemeColors.App;
                    _Form.ForeColor = ThemeColors.Contrast;

                    foreach (Control _control in _Form.Controls)
                    {
                        ThemeControl(_control);
                    }

                    void ControlAdded(object sender, ControlEventArgs e)
                    {
                        ThemeControl(e.Control);
                    };
                    void ControlDisposed(object sender, EventArgs e)
                    {
                        _Form.ControlAdded -= ControlAdded;
                        _Form.Disposed -= ControlDisposed;
                    };
                    _Form.ControlAdded += ControlAdded;
                    _Form.Disposed += ControlDisposed;
                }
            }
        }

        private void OnUserPreferenceChanged(object sender, EventArgs e) => UpdateTheme();

        public void UpdateTheme()
        {
            var currentTheme = IsWindowsDarkThemed();

            if (IsDarkMode != currentTheme)
            {
                IsDarkMode = currentTheme;

                foreach (Form form in Application.OpenForms)
                {
                    ThemeColors = GetAppTheme();
                    ApplyTheme(form);
                    ApplySystemTheme(form);
                    form.Invalidate();
                }
            }
        }

        /// <summary>Attemps to apply Window's Dark Style to the Control and all its childs.</summary>
        /// <param name="control"></param>
        public void ApplySystemTheme(Control control = null)
        {
            if (control is ByteViewer)
            {
                return;
            }
            /* 			    
				DWMWA_USE_IMMERSIVE_DARK_MODE:   https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
            
				Use with DwmSetWindowAttribute. Allows the window frame for this window to be drawn in dark mode colors when the dark mode system setting is enabled. 
				For compatibility reasons, all windows default to light mode regardless of the system setting. 
				The pvAttribute parameter points to a value of type BOOL. TRUE to honor dark mode for the window, FALSE to always use light mode.
            
				This value is supported starting with Windows 11 Build 22000.
            
				SetWindowTheme:     https://learn.microsoft.com/en-us/windows/win32/api/uxtheme/nf-uxtheme-setwindowtheme
				Causes a window to use a different set of visual style information than its class normally uses.
			*/
            IntPtr DarkModeOn = 0; //<- 1=True, 0=False

            var windowsTheme = "Explorer";
            var windowsThemeCombo = "Explorer";

            if (IsDarkMode)
            {
                windowsTheme = "DarkMode_Explorer";
                windowsThemeCombo = "DarkMode_CFD";
                DarkModeOn = 1;
            }
            else
            {
                DarkModeOn = 0;
            }

            if (control is ComboBox comboBox)
            {
                _ = PInvoke.SetWindowTheme((Windows.Win32.Foundation.HWND)comboBox.Handle, windowsThemeCombo, null);

                // Style the ComboBox drop-down (including its ScrollBar(s)):
                Windows.Win32.UI.Controls.COMBOBOXINFO cInfo = default;
                var result = PInvoke.GetComboBoxInfo((Windows.Win32.Foundation.HWND)comboBox.Handle, ref cInfo);
                _ = PInvoke.SetWindowTheme(cInfo.hwndList, windowsThemeCombo, null);
            }
            else
            {
                _ = PInvoke.SetWindowTheme((Windows.Win32.Foundation.HWND)control.Handle, windowsTheme, null);
            }
            unsafe
            {
                const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

                if (PInvoke.DwmSetWindowAttribute((Windows.Win32.Foundation.HWND)control.Handle, (Windows.Win32.Graphics.Dwm.DWMWINDOWATTRIBUTE)DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, &DarkModeOn, sizeof(int)) != 0)
                {
                    _ = PInvoke.DwmSetWindowAttribute((Windows.Win32.Foundation.HWND)control.Handle, Windows.Win32.Graphics.Dwm.DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, &DarkModeOn, sizeof(int));
                }
            }

            foreach (Control child in control.Controls)
            {
                if (child.Controls.Count != 0)
                {
                    ApplySystemTheme(child);
                }
            }
        }
    }

    public class ThemeColors
    {
        public ThemeColors() { }

        /// <summary>For background elements like the window itself and titlebars.</summary>
        public Color App { get; set; }
        /// <summary>For element which need to stand out from the background but are not the main focus, like a data container.</summary>
        public Color AppFirm { get; set; }
        /// <summary>For most of the main interfact, this should be used by most flat panels.</summary>
        public Color AppSoft { get; set; }

        /// <summary>For borders meant to visually separate parts of the interfact.</summary>
        public Color Border { get; set; }
        /// <summary>For secondary borders or padding which need to not stand out too much from the background.</summary>
        public Color BorderSoft { get; set; }

        /// <summary>For any element which needs contrast from the background, like text</summary>
        public Color Contrast { get; set; }
        /// <summary>For any element which needs contrast but doesn't have to be as visible, like inactive text or scrollbars</summary>
        public Color ContrastSoft { get; set; }

        /// <summary>For hover state of buttons in the control box except close, (_ [] buttons)</summary>
        public Color ControlBoxHighlight { get; set; }
        /// <summary>For hover state of close button in the control box, (X button)</summary>
        public Color ControlBoxHighlightCloseButton { get; set; }


        /// <summary>For anything that needs to be accented like hovering over a tab</summary>
        public Color Accent { get; set; }
    }

    // Custom Renderers for Menus and ToolBars
    public class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public bool ColorizeIcons { get; set; } = true;
        public ThemeColors themeColors { get; set; }

        public DarkToolStripRenderer(ProfessionalColorTable table, bool pColorizeIcons = true) : base(table)
        {
            ColorizeIcons = pColorizeIcons;
        }

        // Background of the whole ToolBar Or MenuBar:
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.ToolStrip.BackColor = themeColors.App;
            base.OnRenderToolStripBackground(e);
            //e.Graphics.FillRectangle(new SolidBrush(themeColors.Window), e.AffectedBounds);
        }

        // For Normal Buttons on a ToolBar:
        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var g = e.Graphics;
            var bounds = new Rectangle(Point.Empty, e.Item.Size);

            var gradientBegin = themeColors.App;
            var gradientEnd = themeColors.App;

            var BordersPencil = new Pen(themeColors.BorderSoft);

            var button = e.Item as ToolStripButton;
            if (button.Pressed || button.Checked)
            {
                gradientBegin = themeColors.AppSoft;
                gradientEnd = themeColors.AppSoft;
            }
            else if (button.Selected)
            {
                gradientBegin = themeColors.Accent;
                gradientEnd = themeColors.Accent;
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

            if (button.Owner.GetItemAt(button.Bounds.X, button.Bounds.Bottom + 1) is not ToolStripButton nextItem)
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
            var gradientBegin = themeColors.App;
            var gradientEnd = themeColors.App;

            using var BordersPencil = new Pen(themeColors.BorderSoft);

            //1. Determine the colors to use:
            if (e.Item.Pressed)
            {
                gradientBegin = themeColors.AppFirm;
                gradientEnd = themeColors.AppFirm;
            }
            else if (e.Item.Selected)
            {
                gradientBegin = themeColors.AppFirm;
                gradientEnd = themeColors.AppFirm;
            }

            //2. Draw the Box around the Control
            using Brush b = new LinearGradientBrush(
                bounds,
                gradientBegin,
                gradientEnd,
                LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(b, bounds);

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
            var gradientBegin = themeColors.App; // Color.FromArgb(203, 225, 252);
            var gradientEnd = themeColors.App;

            //1. Determine the colors to use:
            if (e.Item.Pressed)
            {
                gradientBegin = themeColors.AppSoft;
                gradientEnd = themeColors.AppSoft;
            }
            else if (e.Item.Selected)
            {
                gradientBegin = themeColors.AppSoft;
                gradientEnd = themeColors.AppSoft;
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
            var ChevronPen = new Pen(themeColors.ContrastSoft, 2); //<- Color and Border Width
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
                e.TextColor = themeColors.Contrast;
            }
            else
            {
                e.TextColor = themeColors.ContrastSoft;
            }

            var text = e.Text.Replace("&", "", StringComparison.Ordinal);

            using var textBrush = new SolidBrush(e.TextColor);
            //e.Graphics.DrawString(text, e.TextFont, textBrush, e.TextRectangle);

            base.OnRenderItemText(e);
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

            var gradientBegin = themeColors.App;
            var gradientEnd = themeColors.App;

            var DrawIt = false;
            var _menu = e.Item as ToolStripItem;
            if (_menu.Pressed)
            {
                gradientBegin = themeColors.ControlBoxHighlight;
                gradientEnd = themeColors.ControlBoxHighlight;
                DrawIt = true;
            }
            else if (_menu.Selected)
            {
                gradientBegin = themeColors.ControlBoxHighlight;
                gradientEnd = themeColors.ControlBoxHighlight;
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
        public ThemeColors Colors { get; set; }

        public CustomColorTable(ThemeColors _Colors)
        {
            Colors = _Colors;
            UseSystemColors = false;
        }

        public override Color ImageMarginGradientBegin => Color.Transparent;
        public override Color ImageMarginGradientMiddle => Color.Transparent;
        public override Color ImageMarginGradientEnd => Color.Transparent;
    }
}
