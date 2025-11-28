using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls
{
    public class ThemedGroupBox : GroupBox
    {
        public Color BorderColor { get; set; } = Color.Black;
        public int CornerRadius { get; set; } = 5;
        public int BorderWidth { get; set; } = 2;

        public ThemedGroupBox()
        {
            FlatStyle = FlatStyle.Flat;
            DoubleBuffered = true;
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();

            BorderColor = Themer.CurrentThemeColors.Border;
            ForeColor = Themer.CurrentThemeColors.Contrast;
            BackColor = Themer.CurrentThemeColors.AppSoft;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var textSize = TextRenderer.MeasureText(Text, Font);
            using var pen = new Pen(BorderColor, this.AdjustForDPI(BorderWidth));
            var rect = ClientRectangle;

            if (string.IsNullOrEmpty(Text))
            {
                rect.Height -= this.AdjustForDPI(10);
                rect.Y += this.AdjustForDPI(9);
                rect.Width -= 1;
            }
            else
            {
                rect.Height -= this.AdjustForDPI(6);
                rect.Y += this.AdjustForDPI(5);
                rect.Width -= 1;
            }

            var textPoint = new Point(rect.X + this.AdjustForDPI(10), rect.Y);

            // Create rounded rectangle path
            using var path = GetRoundedRect(rect, this.AdjustForDPI(CornerRadius), textSize, textPoint);

            e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Draw the border
            e.Graphics.DrawPath(pen, path);
            // Draw the text
            if (!string.IsNullOrEmpty(Text))
            {
                TextRenderer.DrawText(e.Graphics, Text, Font, textPoint, ForeColor, TextFormatFlags.VerticalCenter);
            }
        }

        private static GraphicsPath GetRoundedRect(Rectangle rect, int radius, Size textSize, Point textPoint)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;
            var arcRect = new Rectangle(rect.Location, new Size(diameter, diameter));

            // Top left arc
            path.AddArc(arcRect, 180, 90);
            // Top line (left of text)
            path.AddLine(arcRect.Right, rect.Top, textPoint.X, rect.Top);
            // Skip the text area
            path.StartFigure();
            path.AddLine(textPoint.X + textSize.Width, rect.Top, rect.Right - radius, rect.Top);
            // Top right arc
            arcRect.X = rect.Right - diameter;
            path.AddArc(arcRect, 270, 90);
            // Right side
            path.AddLine(rect.Right, arcRect.Bottom, rect.Right, rect.Bottom - radius);
            // Bottom right arc
            arcRect.Y = rect.Bottom - diameter;
            path.AddArc(arcRect, 0, 90);
            // Bottom side
            path.AddLine(arcRect.Left, rect.Bottom, rect.Left + radius, rect.Bottom);
            // Bottom left arc
            arcRect.X = rect.Left;
            path.AddArc(arcRect, 90, 90);
            // Left side
            path.AddLine(rect.Left, arcRect.Top, rect.Left, rect.Top + radius);

            return path;
        }
    }
}
