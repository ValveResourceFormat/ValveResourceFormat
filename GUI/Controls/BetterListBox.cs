using System.Drawing;
using System.Windows.Forms;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace GUI.Controls
{
    internal class BetterListBox : ListBox
    {
        public Color BorderColor = Color.Black;

        public BetterListBox()
        {
            BorderStyle = BorderStyle.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            using var borderPen = new Pen(Color.White);
            var rect = ClientRectangle;
            rect.Width -= 1;
            e.Graphics.DrawRectangle(borderPen, ClientRectangle);

        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == PInvoke.WM_PAINT)
            {
                base.WndProc(ref m);

                var dc = PInvoke.GetWindowDC((HWND)Handle);
                using var g = Graphics.FromHdc(dc);
                using var borderPen = new Pen(BorderColor);
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }
            else
            {
                base.WndProc(ref m);
            }
        }

    }
}
