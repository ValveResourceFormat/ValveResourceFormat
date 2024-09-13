using System.Drawing;
using System.Windows.Forms;

namespace GUI.Controls
{
    internal class BetterCheckedListBox : CheckedListBox
    {
        public Color BorderColor = Color.Black;

        public BetterCheckedListBox()
        {
            BorderStyle = BorderStyle.None;
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            var dc = Windows.Win32.PInvoke.GetWindowDC((Windows.Win32.Foundation.HWND)Handle);
            using var g = Graphics.FromHdc(dc);
            using var borderPen = new Pen(BorderColor);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            g.DrawRectangle(borderPen, rect);
        }
    }
}
