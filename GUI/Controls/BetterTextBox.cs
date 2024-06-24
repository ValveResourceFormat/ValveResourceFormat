using System.Drawing;
using System.Windows.Forms;

namespace GUI.Controls
{
    // Adds a customizable border 
    internal class BetterTextBox : TextBox
    {
        public Color BorderColor = Color.Gray;

        public BetterTextBox()
        {
            BorderStyle = BorderStyle.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            var dc = Windows.Win32.PInvoke.GetWindowDC((Windows.Win32.Foundation.HWND)Handle);
            using var g = Graphics.FromHdc(dc);
            using var borderPen = new Pen(BorderColor);
            g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
        }
    }
}
