using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls
{
    public class ThemedButton : Button
    {
        private bool Hovered;
        private bool Clicked;

        public bool Style { get; set; } = true;

        public TextFormatFlags LabelFormatFlags { get; set; } = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;

        public bool ForceClicked { get; set; }

        private Color adjustedForeColor;
        public Color AdjustedForeColor
        {
            get => adjustedForeColor;
        }

        private Color adjustedBackColor;
        public Color AdjustedBackColor
        {
            get => adjustedBackColor;
        }

        public Color ClickedBackColor { get; set; } = Color.Gray;
        public int CornerRadius { get; set; } = 5;

        public ThemedButton()
        {
            adjustedBackColor = BackColor;
            adjustedForeColor = ForeColor;

            FlatStyle = FlatStyle.Flat;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            Cursor = Cursors.Hand;
            Hovered = true;
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            Cursor = Cursors.Default;
            Hovered = false;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            Clicked = true;
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            Clicked = false;
            base.OnMouseUp(mevent);
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();

            if (Style)
            {
                ClickedBackColor = Themer.CurrentThemeColors.Accent;
                ForeColor = Themer.CurrentThemeColors.Contrast;
                BackColor = Themer.CurrentThemeColors.Border;
            }
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            adjustedBackColor = BackColor;
            adjustedForeColor = ForeColor;

            pevent.Graphics.Clear(Parent?.BackColor ?? BackColor);
            var rect = ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;

            pevent.Graphics.CompositingQuality = CompositingQuality.HighQuality;
            pevent.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var backBrush = new SolidBrush(adjustedBackColor);
            using var textBrush = new SolidBrush(adjustedForeColor);

            if (Hovered)
            {
                adjustedBackColor = Brighten(BackColor, 1.3f);
            }

            if (Clicked || ForceClicked)
            {
                adjustedBackColor = ClickedBackColor;
            }

            if (!Enabled)
            {
                adjustedBackColor = Brighten(adjustedBackColor, 0.6f);
                adjustedForeColor = Brighten(adjustedForeColor, 0.6f);
            }

            backBrush.Color = adjustedBackColor;
            textBrush.Color = adjustedForeColor;

            using var roundedRect = Themer.GetRoundedRect(rect, this.AdjustForDPI(CornerRadius));
            pevent.Graphics.FillPath(backBrush, roundedRect);

            TextRenderer.DrawText(pevent.Graphics, Text, Font, ClientRectangle, ForeColor, LabelFormatFlags);

            if (Image != null)
            {
                var imageRect = rect;
                var imageScale = 0.8f;

                if (imageRect.Width > imageRect.Height)
                {
                    imageRect.Width = imageRect.Height;
                }
                else
                {
                    imageRect.Height = imageRect.Width;
                }

                imageRect.Width = (int)(imageRect.Width * imageScale);
                imageRect.Height = (int)(imageRect.Height * imageScale);

                imageRect.X = imageRect.X - ((imageRect.Width - rect.Width) / 2);
                imageRect.Y = imageRect.Y - ((imageRect.Height - rect.Height) / 2);

                pevent.Graphics.DrawImage(Image, imageRect);
            }
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
}
