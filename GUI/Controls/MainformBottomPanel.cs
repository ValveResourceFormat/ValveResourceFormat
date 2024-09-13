using System.Drawing;
using System.Windows.Forms;

namespace GUI.Controls
{
    internal class MainformBottomPanel : Panel
    {
        public Color SeparatorColor = Color.Gray;
        public int SeparatorWidth = 4;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // Draw separator
            using var pen = new Pen(SeparatorColor);
            pen.Width = SeparatorWidth;
            var p1 = new Point(ClientRectangle.Left, ClientRectangle.Top);
            var p2 = new Point(ClientRectangle.Right, ClientRectangle.Top);
            e.Graphics.DrawLine(pen, p1, p2);
        }
    }
}
