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

        private const TextFormatFlags TextRenderingFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;

        public ThemedTabControl() : base()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.OptimizedDoubleBuffer, true);

            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Fixed;

            BaseTabWidth = 150;
            TabHeight = 25;

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

        // this makes the tab header flush with the body
        public override Rectangle DisplayRectangle
        {
            get
            {
                Rectangle rect = base.DisplayRectangle;
                return new Rectangle(rect.Left, rect.Top - this.AdjustForDPI(4), rect.Width, rect.Height + this.AdjustForDPI(4));
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
            g.FillRectangle(brush, tabRect);

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

                textRect.X = imageRect.Right + imagePadding;

            }

            string tabText = TabPages[index].Text;
            Color textColor = ForeColor;

            if (isSelected || isHovered)
            {
                textColor = SelectedForeColor;
            }

            using (SolidBrush textBrush = new SolidBrush(textColor))
            {
                TextRenderer.DrawText(g, tabText, Font, textRect, textColor, TextRenderingFlags);
            }
        }
    }
}
