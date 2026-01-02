using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GUI.Utils;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace GUI.Controls
{
    public class ThemedTabControl : TabControl
    {
        [Description("Back color for selected Tab"), Category("Appearance")]
        public Color SelectTabColor { get; set; } = SystemColors.ControlLight;

        [Description("Fore Color for Selected Tab"), Category("Appearance")]
        public Color SelectedForeColor { get; set; } = SystemColors.HighlightText;

        [Description("Background color for the whole control"), Category("Appearance"), Browsable(true)]
        public override Color BackColor { get; set; } = SystemColors.Control;

        [Description("Fore Color for all Texts"), Category("Appearance")]
        public override Color ForeColor { get; set; } = SystemColors.ControlText;

        [Description("Hover Color for the tab"), Category("Appearance")]
        public Color HoverColor { get; set; } = SystemColors.Highlight;

        [Description("Accent Color for the tab"), Category("Appearance")]
        public Color AccentColor { get; set; } = SystemColors.ActiveBorder;

        private int baseTabWidth;
        [Description("Base width"), Category("Appearance")]
        public int BaseTabWidth
        {
            get { return baseTabWidth; }
            set
            {
                baseTabWidth = this.AdjustForDPI(value);
                if (IsHandleCreated)
                {
                    CalculateTabWidth();
                }
            }
        }

        private readonly int minTabWidth;
        private int cachedLeftPadding;
        private int cachedGapPerTab;
        private Size cachedItemSize;

        private int tabHeight;
        [Description("Height of tabs"), Category("Appearance")]
        public int TabHeight
        {
            get { return tabHeight; }
            set { tabHeight = this.AdjustForDPI(value); }
        }

        private int tabTopRadius;
        [Description("Roundness of the corners of the top of tabs"), Category("Appearance")]
        public int TabTopRadius
        {
            get { return tabTopRadius; }
            set { tabTopRadius = this.AdjustForDPI(Math.Max(value, 0)); }
        }

        [Description("Roundness of the corners of the top of tabs"), Category("Appearance")]
        public bool SelectionLine { get; set; } = true;

        private bool _endEllipsis;
        public bool EndEllipsis
        {
            get { return _endEllipsis; }
            set
            {
                _endEllipsis = value;

                if (_endEllipsis)
                {
                    TextRenderingFlags |= TextFormatFlags.EndEllipsis;
                }
                else
                {
                    TextRenderingFlags &= ~TextFormatFlags.EndEllipsis;
                }
            }
        }

        private TextFormatFlags TextRenderingFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;

        public ThemedTabControl() : base()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.OptimizedDoubleBuffer, true);

            DrawMode = TabDrawMode.OwnerDrawFixed;

            BaseTabWidth = 200;
            TabHeight = 32;
            TabTopRadius = 0;
            Padding = new Point(12, 8);

            BackColor = Themer.CurrentThemeColors.AppMiddle;
            SelectTabColor = Themer.CurrentThemeColors.AppSoft;
            SelectedForeColor = Themer.CurrentThemeColors.Contrast;
            ForeColor = Themer.CurrentThemeColors.ContrastSoft;
            HoverColor = Themer.CurrentThemeColors.HoverAccent;
            AccentColor = Themer.CurrentThemeColors.Accent;

            // Calculate minimum tab width for icon-only display
            // Uses same padding as image centering for visual consistency
            var iconSize = (int)(TabHeight * 0.5);
            var horizontalPadding = (TabHeight - iconSize) / 2;
            minTabWidth = horizontalPadding + iconSize + horizontalPadding;
        }

        private void UpdateCachedMetrics()
        {
            // Cache padding and gap values by examining actual tab positions
            if (TabPages.Count >= 1)
            {
                var tab0 = GetTabRect(0);
                cachedLeftPadding = tab0.X;

                if (TabPages.Count >= 2)
                {
                    var tab1 = GetTabRect(1);
                    cachedGapPerTab = tab1.X - (tab0.X + tab0.Width);
                }
            }
        }

        private void CalculateTabWidth(bool isResizing = false)
        {
            if (!IsHandleCreated || SizeMode != TabSizeMode.Fixed)
            {
                return;
            }

            var availableWidth = ClientSize.Width;
            if (availableWidth <= 0)
            {
                return;
            }

            var tabCount = TabPages.Count;
            if (tabCount <= 0)
            {
                return;
            }

            var totalPadding = cachedLeftPadding * 2;
            var totalGapWidth = cachedGapPerTab * (tabCount - 1);
            var spaceForTabs = availableWidth - totalPadding - totalGapWidth;
            var idealWidth = spaceForTabs / tabCount;
            int calculatedWidth;

            if (idealWidth >= BaseTabWidth)
            {
                calculatedWidth = BaseTabWidth;
            }
            else if (idealWidth >= minTabWidth)
            {
                calculatedWidth = idealWidth;
            }
            else
            {
                calculatedWidth = minTabWidth;
            }

            var newSize = new Size(calculatedWidth, TabHeight);
            if (cachedItemSize != newSize)
            {
                if (isResizing)
                {
                    // Setting ItemSize causes size invalidation which triggers resize again which is very janky
                    PInvoke.SendMessage((HWND)Handle, PInvoke.TCM_SETITEMSIZE, 0, (newSize.Height << 16) | (newSize.Width & 0xffff));
                }
                else
                {
                    ItemSize = newSize;
                }

                cachedItemSize = newSize;
            }
        }

        protected override void OnCreateControl()
        {
            // Necessary to give tabs the correct width
            base.OnCreateControl();
            OnFontChanged(EventArgs.Empty);
            UpdateCachedMetrics();
            CalculateTabWidth();
        }

        protected override void InitLayout()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.DoubleBuffer, true);
            SetStyle(ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.UserPaint, true);
            base.InitLayout();
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            UpdateCachedMetrics(); // we only need to calculate this once realistically
            CalculateTabWidth();
        }

        protected override void OnControlRemoved(ControlEventArgs e)
        {
            base.OnControlRemoved(e);

            BeginInvoke(() => CalculateTabWidth());
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            CalculateTabWidth(isResizing: true);
        }

        // this makes the tab header flush with the body
        public override Rectangle DisplayRectangle
        {
            get
            {
                var rect = base.DisplayRectangle;

                // extend the client area by 4 pixels, this makes the page inside the tab control flush with the edges
                var offset = 4;
                return new Rectangle(rect.Left - offset, rect.Top - offset, rect.Width + offset * 2, rect.Height + offset * 2);
            }
        }

        protected int HoveredIndex { get; private set; } = -1;

        protected int RightSideTextPadding { get; set; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var oldHovered = HoveredIndex;
            HoveredIndex = -1;

            for (var i = 0; i < TabCount; i++)
            {
                var tabRect = GetTabRect(i);
                if (tabRect.Contains(e.Location))
                {
                    HoveredIndex = i;
                    break;
                }
            }

            if (oldHovered != HoveredIndex)
            {
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (HoveredIndex != -1)
            {
                HoveredIndex = -1;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var bgBrush = new SolidBrush(BackColor))
            {
                g.FillRectangle(bgBrush, ClientRectangle);
            }

            for (var i = 0; i < TabCount; i++)
            {
                DrawTab(g, i);
            }
        }

        private void DrawTab(Graphics g, int index)
        {
            var tabRect = GetTabRect(index);
            var tabColor = BackColor;
            var isSelected = SelectedIndex == index;
            var isHovered = HoveredIndex == index;

            if (isSelected)
            {
                tabColor = SelectTabColor;
            }
            else if (isHovered)
            {
                tabColor = HoverColor;
            }

            using var brush = new SolidBrush(tabColor);

            if ((isHovered && TabTopRadius > 0) || isSelected)
            {
                var borderRect = isSelected ? tabRect : new Rectangle(tabRect.Left, tabRect.Top, tabRect.Width, tabRect.Height - this.AdjustForDPI(2));
                using var roundedRect = Themer.GetRoundedRect(borderRect, TabTopRadius, onlyTop: isSelected);
                g.FillPath(brush, roundedRect);

                if (isHovered && !isSelected)
                {
                    using var borderPen = new Pen(AccentColor, this.AdjustForDPI(1));
                    g.DrawPath(borderPen, roundedRect);
                }
            }
            else if (isHovered)
            {
                g.FillRectangle(brush, tabRect);

                using var borderPen = new Pen(AccentColor, this.AdjustForDPI(1));
                g.DrawRectangle(borderPen, tabRect);
            }

            if (SelectionLine && isSelected)
            {
                g.SmoothingMode = SmoothingMode.None; // Fixes blurry line

                using var pen = new Pen(AccentColor, this.AdjustForDPI(10));

                g.DrawLine(pen, new Point(tabRect.Left, tabRect.Bottom), new Point(tabRect.Right, tabRect.Bottom));

                g.SmoothingMode = SmoothingMode.AntiAlias;
            }

            var textRect = new Rectangle(
                tabRect.X,
                tabRect.Y,
                tabRect.Width - RightSideTextPadding,
                tabRect.Height
            );

            var imageScaleFactor = 0.75;
            var imageSize = (int)(tabRect.Height * imageScaleFactor);
            var imagePadding = this.AdjustForDPI(2);

            if (ImageList != null && ImageList.Images.Count > 0)
            {
                // Center image vertically within tab
                var imageCenteringOffset = (tabRect.Height - imageSize) / 2;

                // Use centering offset as horizontal padding for visual consistency
                var imageHorizontalPadding = imageCenteringOffset;
                var imageHorizontalPositioning = tabRect.X + imageHorizontalPadding;
                var imageVerticalPositioning = tabRect.Y + imageCenteringOffset;
                var imageRect = new Rectangle(imageHorizontalPositioning, imageVerticalPositioning, imageSize, imageSize);

                var imageIndex = TabPages[index].ImageIndex;

                if (imageIndex > -1)
                {
                    ImageList.Draw(g, imageRect.Left, imageRect.Top, imageRect.Height, imageRect.Height, imageIndex);
                }

                var oldTextX = textRect.X;
                textRect.X = imageRect.Right + imagePadding;
                textRect.Width -= textRect.X - oldTextX;
            }

            // Only render text if tab is wider than minimum (icon-only) width
            if (tabRect.Width > minTabWidth)
            {
                var tabText = TabPages[index].Text;
                var textColor = ForeColor;

                if (isSelected || isHovered)
                {
                    textColor = SelectedForeColor;
                }

                var formatFlags = SizeMode switch
                {
                    TabSizeMode.Fixed => TextRenderingFlags | TextFormatFlags.Left,
                    _ => TextRenderingFlags | TextFormatFlags.HorizontalCenter,
                };

                using var textBrush = new SolidBrush(textColor);
                TextRenderer.DrawText(g, tabText, Font, textRect, textColor, formatFlags);
            }
        }
    }
}
