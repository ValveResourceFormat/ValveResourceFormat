using System.Drawing;
using System.Windows.Forms;

namespace GUI.Controls
{
    internal class BetterGroupBox : GroupBox
    {
        public Color BorderColor = Color.Black;

        public BetterGroupBox()
        {
            FlatStyle = FlatStyle.Flat;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            //base.OnPaint(e);
            var textPoint = new Point(ClientRectangle.Left, ClientRectangle.Top);
            using var pen = new Pen(BorderColor, 1);
            var rect = ClientRectangle;
            rect.Height -= 1;
            rect.Width -= 1;
            TextRenderer.DrawText(e, Text, Font, textPoint, ForeColor);
            e.Graphics.DrawRectangle(pen, rect);
        }
    }
}
