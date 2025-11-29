using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GUI.Utils;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace GUI.Controls
{
    public class ThemedTabControl : TabControl
    {
        [Description("Color for a decorative line"), Category("Appearance")]
        public Color LineColor { get; set; } = SystemColors.Highlight;

        [Description("Color for all Borders"), Category("Appearance")]
        public Color BorderColor { get; set; } = SystemColors.ControlDark;

        [Description("Back color for selected Tab"), Category("Appearance")]
        public Color SelectTabColor { get; set; } = SystemColors.ControlLight;

        [Description("Fore Color for Selected Tab"), Category("Appearance")]
        public Color SelectedForeColor { get; set; } = SystemColors.HighlightText;

        [Description("Back Color for un-selected tabs"), Category("Appearance")]
        public Color TabColor { get; set; } = SystemColors.ControlLight;

        [Description("Background color for the whole control"), Category("Appearance"), Browsable(true)]
        public override Color BackColor { get; set; } = SystemColors.Control;

        [Description("Fore Color for all Texts"), Category("Appearance")]
        public override Color ForeColor { get; set; } = SystemColors.ControlText;

        [Description("Hover Color for the tab"), Category("Appearance")]
        public Color HoverColor { get; set; } = SystemColors.Highlight;

        private int baseTabWidth;
        [Description("Base width"), Category("Appearance")]
        public int BaseTabWidth
        {
            get { return baseTabWidth; }
            set { baseTabWidth = this.AdjustForDPI(value); }
        }

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

        private const TextFormatFlags TextRenderingFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

        public ThemedTabControl() : base()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.OptimizedDoubleBuffer, true);

            DrawMode = TabDrawMode.OwnerDrawFixed;

            BaseTabWidth = 150;
            TabHeight = 25;
            TabTopRadius = 0;

            ItemSize = new Size(BaseTabWidth, TabHeight);
        }

        protected override void OnCreateControl()
        {
            // Necessary to give tabs the correct width
            base.OnCreateControl();
            OnFontChanged(EventArgs.Empty);

            BackColor = Themer.CurrentThemeColors.App;
            TabColor = Themer.CurrentThemeColors.AppSoft;
            SelectTabColor = Themer.CurrentThemeColors.AppSoft;
            SelectedForeColor = Themer.CurrentThemeColors.Contrast;
            BorderColor = Themer.CurrentThemeColors.Border;
            ForeColor = Themer.CurrentThemeColors.ContrastSoft;
            LineColor = Themer.CurrentThemeColors.Accent;
            HoverColor = Themer.CurrentThemeColors.Accent;
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
        }

        // this makes the tab header flush with the body
        public override Rectangle DisplayRectangle
        {
            get
            {
                Rectangle rect = base.DisplayRectangle;

                // extend the client area by 4 pixels, this makes the page inside the tab control flush with the edges
                var offset = 4;
                return new Rectangle(rect.Left - this.AdjustForDPI(offset), rect.Top - this.AdjustForDPI(offset), rect.Width + this.AdjustForDPI(offset * 2), rect.Height + this.AdjustForDPI(offset * 2));
            }
        }

        int HoveredIndex = -1;
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int oldHovered = HoveredIndex;
            HoveredIndex = -1;

            for (int i = 0; i < TabCount; i++)
            {
                Rectangle tabRect = GetTabRect(i);
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
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush bgBrush = new SolidBrush(BackColor))
            {
                g.FillRectangle(bgBrush, ClientRectangle);
            }

            for (int i = 0; i < TabCount; i++)
            {
                DrawTab(g, i);
            }
        }

        private void DrawTab(Graphics g, int index)
        {
            Rectangle tabRect = GetTabRect(index);
            bool isSelected = (SelectedIndex == index);
            bool isHovered = (HoveredIndex == index);

            Color tabColor = BackColor;

            if (isSelected) tabColor = SelectTabColor;
            else if (isHovered) tabColor = HoverColor;
            using var brush = new SolidBrush(tabColor);

            if (TabTopRadius > 0)
            {
                using var roundedRect = Themer.GetRoundedRect(tabRect, TabTopRadius, true);
                g.FillPath(brush, roundedRect);
            }
            else
            {
                g.FillRectangle(brush, tabRect);
            }

            Rectangle textRect = new Rectangle(
                tabRect.X,
                tabRect.Y,
                tabRect.Width,
                tabRect.Height
            );

            var imageScaleFactor = 0.7;
            var imageSize = (int)(tabRect.Height * imageScaleFactor);
            var imagePadding = this.AdjustForDPI(2);

            if (ImageList != null && ImageList.Images.Count > 0)
            {
                //center image by adding half of the difference between the tab height and the image height
                var imageCenteringOffset = (tabRect.Height - imageSize) / 2;
                var imageHorizontalPositioning = (tabRect.X + imageCenteringOffset) + imagePadding;
                var imageVerticalPositioning = (tabRect.Y + imageCenteringOffset);
                var imageRect = new Rectangle(imageHorizontalPositioning, imageVerticalPositioning, imageSize, imageSize);

                var image = ImageList.Images[TabPages[index].ImageIndex];
                g.DrawImage(image, imageRect.Left, imageRect.Top, imageRect.Height, imageRect.Height);

                var oldTextX = textRect.X;
                textRect.X = imageRect.Right + imagePadding;
                textRect.Width = textRect.Width - (textRect.X - oldTextX);
            }

            string tabText = TabPages[index].Text;
            Color textColor = ForeColor;

            if (isSelected || isHovered)
            {
                textColor = SelectedForeColor;
            }

            using (SolidBrush textBrush = new SolidBrush(textColor))
            {
                var formatFlags = SizeMode switch
                {
                    TabSizeMode.Fixed => TextRenderingFlags | TextFormatFlags.Left,
                    _ => TextRenderingFlags | TextFormatFlags.HorizontalCenter,
                };

                TextRenderer.DrawText(g, tabText, Font, textRect, textColor, formatFlags);
            }
        }
    }
}
