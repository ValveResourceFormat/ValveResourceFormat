using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GUI.Controls
{
    internal class BetterCheckBox : CheckBox
    {
        public Color FillColor = Color.White;
        public Color BorderColor = Color.Black;
        public Color AccentColor = Color.Blue;

        public Size CheckBoxSize = new(12, 12);

        public int BorderWidth = 1;

        private bool Pressed;
        private bool Hovered;

        public BetterCheckBox()
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {

            var checkBoxRect = new Rectangle(ClientRectangle.Left, (ClientRectangle.Height - CheckBoxSize.Height) / 2,
                Program.MainForm.AdjustForDPI(CheckBoxSize.Width), Program.MainForm.AdjustForDPI(CheckBoxSize.Height));

            InvokePaintBackground(this, e);

            using var backBrush = new SolidBrush(FillColor);

            if (Pressed)
            {
                backBrush.Color = BorderColor;
            }

            using var checkPen = new Pen(ForeColor, Program.MainForm.AdjustForDPI(1));

            using var borderPen = new Pen(BorderColor);
            if (Hovered)
            {
                borderPen.Color = AccentColor;
            }
            borderPen.Width = BorderWidth;


            e.Graphics.FillRectangle(backBrush, checkBoxRect);
            e.Graphics.DrawRectangle(borderPen, checkBoxRect);

            if (Checked)
            {
                var checkMiddle = new Point(checkBoxRect.Left + checkBoxRect.Height / 2 - Program.MainForm.AdjustForDPI(1.1f),
                checkBoxRect.Top + checkBoxRect.Width / 2 + Program.MainForm.AdjustForDPI(3));

                var checkLeft = new Point(checkBoxRect.Left + 2,
                    checkBoxRect.Top + checkBoxRect.Width / 2);

                var checkRight = new Point(checkBoxRect.Right - 2,
                    checkBoxRect.Top + Program.MainForm.AdjustForDPI(3));

                e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawLine(checkPen, checkMiddle, checkLeft);
                e.Graphics.DrawLine(checkPen, checkMiddle, checkRight);
            }

            var textSize = TextRenderer.MeasureText(Text, Program.MainForm.Font);
            var textRect = new Rectangle(checkBoxRect.X + checkBoxRect.Width, checkBoxRect.Y + ((checkBoxRect.Height - textSize.Height) / 2), checkBoxRect.Width + textSize.Width, textSize.Height);

            using var foreBrush = new SolidBrush(ForeColor);
            TextRenderer.DrawText(e.Graphics, Text, Program.MainForm.Font, textRect, ForeColor);
        }

        protected override void OnMouseEnter(EventArgs eventargs)
        {
            base.OnMouseEnter(eventargs);

            Hovered = true;
        }

        protected override void OnMouseLeave(EventArgs eventargs)
        {
            base.OnMouseLeave(eventargs);

            Hovered = false;
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);

            Pressed = true;
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);

            Pressed = false;

            Invalidate();
        }
    }
}
