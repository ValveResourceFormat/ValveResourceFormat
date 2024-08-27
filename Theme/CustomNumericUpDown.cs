// Originally taken from https://github.com/r-aghaei/FlatNumericUpDownExample/blob/master/FlatNumericUpDownExample/FlatNumericUpDown.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GUI.Theme
{
    public class CustomNumericUpDown : NumericUpDown
    {
        private Color borderColor = Color.Gray;
        [DefaultValue(typeof(Color), "Gray")]
        public Color BorderColor
        {
            get { return borderColor; }
            set
            {
                if (borderColor != value)
                {
                    borderColor = value;
                    Invalidate();
                }
            }
        }

        private Color buttonColor = Color.Black;
        private SolidBrush buttonBrush = new SolidBrush(Color.Black);
        [DefaultValue(typeof(Color), "Black")]
        public Color ButtonColor
        {
            get { return buttonColor; }
            set
            {
                if (buttonColor != value)
                {
                    buttonBrush.Dispose();
                    buttonColor = value;
                    buttonBrush = new SolidBrush(buttonColor);
                    Invalidate();
                }
            }
        }

        private Color buttonHighlightColor = Color.LightGray;
        [DefaultValue(typeof(Color), "LightGray")]
        public Color ButtonHighlightColor
        {
            get { return buttonHighlightColor; }
            set
            {
                if (buttonHighlightColor != value)
                {
                    buttonHighlightColor = value;
                    Invalidate();
                }
            }
        }
        public CustomNumericUpDown() : base()
        {
            var renderer = new UpDownButtonRenderer(this, Controls[0]);
            BorderStyle = BorderStyle.FixedSingle;
        }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;   // WS_EX_COMPOSITED       
                return cp;
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (BorderStyle == BorderStyle.FixedSingle)
            {
                using (var pen = new Pen(BorderColor, 1))
                {
                    e.Graphics.DrawRectangle(pen,
                        ClientRectangle.Left, ClientRectangle.Top,
                        ClientRectangle.Width - 1, ClientRectangle.Height - 1);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            buttonBrush.Dispose();
        }

        private class UpDownButtonRenderer : NativeWindow
        {
            [DllImport("user32.dll", ExactSpelling = true, EntryPoint = "BeginPaint", CharSet = CharSet.Auto)]
#pragma warning disable CA5392 // Use DefaultDllImportSearchPaths attribute for P/Invokes
            private static extern IntPtr IntBeginPaint(IntPtr hWnd, [In, Out] ref PAINTSTRUCT lpPaint);
#pragma warning restore CA5392 // Use DefaultDllImportSearchPaths attribute for P/Invokes

            [StructLayout(LayoutKind.Sequential)]
            public struct PAINTSTRUCT
            {
                public IntPtr hdc;
                public bool fErase;
                public int rcPaint_left;
                public int rcPaint_top;
                public int rcPaint_right;
                public int rcPaint_bottom;
                public bool fRestore;
                public bool fIncUpdate;
                public int reserved1;
                public int reserved2;
                public int reserved3;
                public int reserved4;
                public int reserved5;
                public int reserved6;
                public int reserved7;
                public int reserved8;
            }

            [DllImport("user32.dll", ExactSpelling = true, EntryPoint = "EndPaint", CharSet = CharSet.Auto)]
#pragma warning disable CA5392 // Use DefaultDllImportSearchPaths attribute for P/Invokes
            private static extern bool IntEndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);
#pragma warning restore CA5392 // Use DefaultDllImportSearchPaths attribute for P/Invokes

            Control updown;
            CustomNumericUpDown owner;
            public UpDownButtonRenderer(CustomNumericUpDown owner, Control c)
            {
                this.updown = c;
                this.owner = owner;
                if (updown.IsHandleCreated)
                    this.AssignHandle(updown.Handle);
                else
                    updown.HandleCreated += (s, e) => this.AssignHandle(updown.Handle);
            }
            private static Point[] GetDownArrow(Rectangle r)
            {
                var middle = new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);
                return new Point[]
                {
                new Point(middle.X - 3, middle.Y - 2),
                new Point(middle.X + 4, middle.Y - 2),
                new Point(middle.X, middle.Y + 2)
                };
            }
            private static Point[] GetUpArrow(Rectangle r)
            {
                var middle = new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);
                return new Point[]
                {
                new Point(middle.X - 4, middle.Y + 2),
                new Point(middle.X + 4, middle.Y + 2),
                new Point(middle.X, middle.Y - 3)
                };
            }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0xF /*WM_PAINT*/ && ((CustomNumericUpDown)updown.Parent).BorderStyle == BorderStyle.FixedSingle)
                {
                    var s = new PAINTSTRUCT();
                    IntBeginPaint(updown.Handle, ref s);
                    using (var g = Graphics.FromHdc(s.hdc))
                    {
                        var enabled = updown.Enabled;
                        using (var backBrush = new SolidBrush(enabled ? ((CustomNumericUpDown)updown.Parent).BackColor : SystemColors.Control))
                        {
                            g.FillRectangle(backBrush, updown.ClientRectangle);
                        }
                        var r1 = new Rectangle(0, 0, updown.Width, updown.Height / 2);
                        var r2 = new Rectangle(0, updown.Height / 2, updown.Width, updown.Height / 2 + 1);
                        var p = updown.PointToClient(MousePosition);
                        if (enabled && updown.ClientRectangle.Contains(p))
                        {
                            using (var b = new SolidBrush(((CustomNumericUpDown)updown.Parent).ButtonHighlightColor))
                            {
                                if (r1.Contains(p))
                                    g.FillRectangle(b, r1);
                                else
                                    g.FillRectangle(b, r2);
                            }
                            using (var pen = new Pen(((CustomNumericUpDown)updown.Parent).BorderColor))
                            {
                                g.DrawLines(pen,
                                    new[] { new Point(0, 0), new Point(0, updown.Height),
                                        new Point(0, updown.Height / 2), new Point(updown.Width, updown.Height / 2)
                                    });
                            }
                        }
                        g.FillPolygon(owner.buttonBrush, GetUpArrow(r1));
                        g.FillPolygon(owner.buttonBrush, GetDownArrow(r2));
                    }
                    m.Result = (IntPtr)1;
                    base.WndProc(ref m);
                    IntEndPaint(updown.Handle, ref s);
                }
                else if (m.Msg == 0x0014/*WM_ERASEBKGND*/)
                {
                    using (var g = Graphics.FromHdcInternal(m.WParam))
                        g.FillRectangle(Brushes.White, updown.ClientRectangle);
                    m.Result = (IntPtr)1;
                }
                else
                    base.WndProc(ref m);
            }
        }
    }
}
