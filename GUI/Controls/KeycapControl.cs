using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls
{
    /// <summary>
    /// A custom control that renders a keyboard keycap with a description label.
    /// </summary>
    public class KeycapControl : UserControl
    {
        private string keyText = string.Empty;
        private string description = string.Empty;

        /// <summary>
        /// The key combination text displayed in the keycap (e.g., "WASD", "Ctrl+C", "F11").
        /// </summary>
        public string KeyText
        {
            get => keyText;
            set
            {
                if (keyText != value)
                {
                    keyText = value;
                    UpdateSize();
                    Invalidate();
                }
            }
        }

        /// <summary>
        /// The description text displayed next to the keycap (e.g., "Move camera", "Screenshot").
        /// </summary>
        public string Description
        {
            get => description;
            set
            {
                if (description != value)
                {
                    description = value;
                    UpdateSize();
                    Invalidate();
                }
            }
        }

        private const int KeycapPadding = 2;
        private const int SpaceBetweenKeyAndDesc = 4;
        private const int CornerRadius = 3;

        public KeycapControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            ResizeRedraw = true;
            BackColor = Color.Transparent;
            AutoSize = false;
            Height = this.AdjustForDPI(20);
            Padding = new Padding(0);
            Font = new Font(Font.FontFamily, 9f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (string.IsNullOrEmpty(KeyText) && string.IsNullOrEmpty(Description))
            {
                return;
            }

            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // Create bold font for key text
            using var boldFont = new Font(Font, FontStyle.Bold);

            // Measure text sizes
            var keySize = TextRenderer.MeasureText(KeyText, boldFont);
            var descSize = TextRenderer.MeasureText(Description, Font);

            // Calculate keycap rectangle
            var keycapWidth = keySize.Width + this.AdjustForDPI(KeycapPadding * 2);
            var keycapMargin = this.AdjustForDPI(1);
            var keycapHeight = Height - (keycapMargin * 2);
            var keycapRect = new Rectangle(0, keycapMargin, keycapWidth, keycapHeight);

            // Draw keycap background with rounded corners
            using var path = Themer.GetRoundedRect(keycapRect, this.AdjustForDPI(CornerRadius));
            using var backBrush = new SolidBrush(Themer.CurrentThemeColors.Border);
            graphics.FillPath(backBrush, path);

            // Draw subtle border for 3D effect
            var isLight = Themer.CurrentThemeColors.ColorMode == SystemColorMode.Classic;
            var borderColor = isLight
                ? ControlPaint.Dark(Themer.CurrentThemeColors.Border, 0.2f)
                : ControlPaint.Light(Themer.CurrentThemeColors.Border, 0.2f);
            using var borderPen = new Pen(borderColor, this.AdjustForDPI(1));
            borderPen.Alignment = PenAlignment.Inset;
            graphics.DrawPath(borderPen, path);

            // Draw key text (bold, centered in keycap)
            TextRenderer.DrawText(
                graphics,
                KeyText,
                boldFont,
                keycapRect,
                Themer.CurrentThemeColors.Contrast,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );

            // Draw description text (regular, to the right of keycap)
            if (!string.IsNullOrEmpty(Description))
            {
                var descRect = new Rectangle(
                    keycapWidth + this.AdjustForDPI(SpaceBetweenKeyAndDesc),
                    0,
                    descSize.Width,
                    Height
                );

                TextRenderer.DrawText(
                    graphics,
                    Description,
                    Font,
                    descRect,
                    Themer.CurrentThemeColors.ContrastSoft,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                );
            }
        }

        private void UpdateSize()
        {
            if (string.IsNullOrEmpty(KeyText) && string.IsNullOrEmpty(Description))
            {
                Width = 0;
                return;
            }

            using var boldFont = new Font(Font, FontStyle.Bold);

            var keySize = TextRenderer.MeasureText(KeyText, boldFont);
            var descSize = TextRenderer.MeasureText(Description, Font);

            var keycapWidth = keySize.Width + this.AdjustForDPI(KeycapPadding * 2);
            var totalWidth = keycapWidth;

            if (!string.IsNullOrEmpty(Description))
            {
                totalWidth += this.AdjustForDPI(SpaceBetweenKeyAndDesc) + descSize.Width;
            }

            Width = totalWidth;
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            UpdateSize();
        }
    }
}
