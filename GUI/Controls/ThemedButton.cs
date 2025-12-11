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
        public Color ClickedBackColor { get; set; } = Color.Gray;
        public Color HoveredBackColor { get; set; } = Color.Gray;
        public int CornerRadius { get; set; } = 5;

        public ThemedButton() : base()
        {
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
                HoveredBackColor = Themer.CurrentThemeColors.HoverAccent;
                ForeColor = Themer.CurrentThemeColors.Contrast;
                BackColor = Themer.CurrentThemeColors.Border;
            }
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            pevent.Graphics.Clear(Parent?.BackColor ?? BackColor);
            var rect = ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;

            pevent.Graphics.CompositingQuality = CompositingQuality.HighQuality;
            pevent.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var backColor = BackColor;
            var foreColor = ForeColor;

            if (!Enabled)
            {
                backColor = Themer.Brighten(backColor, 0.6f);
                foreColor = Themer.Brighten(foreColor, 0.6f);
            }
            else if (Clicked)
            {
                backColor = ClickedBackColor;
            }
            else if (Hovered)
            {
                backColor = HoveredBackColor;
            }

            using var backBrush = new SolidBrush(backColor);
            using var textBrush = new SolidBrush(foreColor);

            using var roundedRect = Themer.GetRoundedRect(rect, this.AdjustForDPI(CornerRadius));
            pevent.Graphics.FillPath(backBrush, roundedRect);

            using var borderPen = new Pen(Themer.Brighten(backColor, 1.5f), this.AdjustForDPI(1));
            pevent.Graphics.DrawPath(borderPen, roundedRect);

            TextRenderer.DrawText(pevent.Graphics, Text, Font, ClientRectangle, foreColor, LabelFormatFlags);

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

                imageRect.X -= (imageRect.Width - rect.Width) / 2;
                imageRect.Y -= (imageRect.Height - rect.Height) / 2;

                pevent.Graphics.DrawImage(Image, imageRect);
            }
        }
    }
}
