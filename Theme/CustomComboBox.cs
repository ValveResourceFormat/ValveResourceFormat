// Special Thanks to https://stackoverflow.com/a/34886006
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GUI.Theme
{
    public class CustomComboBox : ComboBox
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

        private Color headerColor = SystemColors.Window;
        public Color HeaderColor
        {
            get { return headerColor; }
            set
            {
                if (headerColor != value)
                {
                    headerColor = value;
                    Invalidate();
                }
            }
        }

        private Color textHoverColor = SystemColors.HighlightText;
        public Color TextHoverColor
        {
            get { return textHoverColor; }
            set
            {
                if (textHoverColor != value)
                {
                    textHoverColor = value;
                    Invalidate();
                }
            }
        }

        private Color buttonColor = Color.LightGray;
        [DefaultValue(typeof(Color), "LightGray")]
        public Color ButtonColor
        {
            get { return buttonColor; }
            set
            {
                if (buttonColor != value)
                {
                    buttonColor = value;
                    Invalidate();
                }
            }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_PAINT && DropDownStyle != ComboBoxStyle.Simple)
            {
                var clientRect = ClientRectangle;
                var dropDownButtonWidth = SystemInformation.HorizontalScrollBarArrowWidth;
                var outerBorder = new Rectangle(clientRect.Location,
                    new Size(clientRect.Width - 1, clientRect.Height - 1));
                var innerBorder = new Rectangle(outerBorder.X + 1, outerBorder.Y + 1,
                    outerBorder.Width - dropDownButtonWidth - 2, outerBorder.Height - 2);
                var innerInnerBorder = new Rectangle(innerBorder.X + 1, innerBorder.Y + 1,
                    innerBorder.Width - 2, innerBorder.Height - 2);
                var dropDownRect = new Rectangle(innerBorder.Right + 1, innerBorder.Y,
                    dropDownButtonWidth, innerBorder.Height + 1);
                if (RightToLeft == RightToLeft.Yes)
                {
                    innerBorder.X = clientRect.Width - innerBorder.Right;
                    innerInnerBorder.X = clientRect.Width - innerInnerBorder.Right;
                    dropDownRect.X = clientRect.Width - dropDownRect.Right;
                    dropDownRect.Width += 1;
                }
                var innerBorderColor = Enabled ? BackColor : SystemColors.Control;
                var outerBorderColor = Enabled ? BorderColor : SystemColors.ControlDark;
                var buttonColor = Enabled ? ButtonColor : SystemColors.Control;
                var middle = new Point(dropDownRect.Left + dropDownRect.Width / 2,
                    dropDownRect.Top + dropDownRect.Height / 2);
                var arrow = new Point[]
                {
                new Point(middle.X - 3, middle.Y - 2),
                new Point(middle.X + 4, middle.Y - 2),
                new Point(middle.X, middle.Y + 2)
                };
                var ps = new PAINTSTRUCT();
                bool shoulEndPaint = false;
                IntPtr dc;
                if (m.WParam == IntPtr.Zero)
                {
                    dc = BeginPaint(Handle, ref ps);
                    m.WParam = dc;
                    shoulEndPaint = true;
                }
                else
                {
                    dc = m.WParam;
                }
                var rgn = CreateRectRgn(innerInnerBorder.Left, innerInnerBorder.Top,
                    innerInnerBorder.Right, innerInnerBorder.Bottom);
#pragma warning disable CA1806 // Do not ignore method results
                SelectClipRgn(dc, rgn);
#pragma warning restore CA1806 // Do not ignore method results
                DefWndProc(ref m);
                DeleteObject(rgn);
                rgn = CreateRectRgn(clientRect.Left, clientRect.Top,
                    clientRect.Right, clientRect.Bottom);
#pragma warning disable CA1806 // Do not ignore method results
                SelectClipRgn(dc, rgn);
#pragma warning restore CA1806 // Do not ignore method results
                using (var g = Graphics.FromHdc(dc))
                {
                    using (var b = new SolidBrush(buttonColor))
                    {
                        g.FillRectangle(b, dropDownRect);
                    }
                    using (var b = new SolidBrush(outerBorderColor))
                    {
                        g.FillPolygon(b, arrow);
                    }
                    using (var p = new Pen(innerBorderColor))
                    {
                        g.DrawRectangle(p, innerBorder);
                        g.DrawRectangle(p, innerInnerBorder);
                    }
                    using (var p = new Pen(outerBorderColor))
                    {
                        g.DrawRectangle(p, outerBorder);
                    }
                }
                if (shoulEndPaint)
                    EndPaint(Handle, ref ps);
                DeleteObject(rgn);
            }
            else
                base.WndProc(ref m);
        }

        private const int WM_PAINT = 0xF;
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int L, T, R, B;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct PAINTSTRUCT
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
        [DllImport("user32.dll")]
#pragma warning disable CA5392 // Use DefaultDllImportSearchPaths attribute for P/Invokes
        private static extern IntPtr BeginPaint(IntPtr hWnd,
#pragma warning restore CA5392 // Use DefaultDllImportSearchPaths attribute for P/Invokes
            [In, Out] ref PAINTSTRUCT lpPaint);

        [DllImport("user32.dll")]
#pragma warning disable CA5392 // Use DefaultDllImportSearchPaths attribute for P/Invokes
        private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);
#pragma warning restore CA5392 // Use DefaultDllImportSearchPaths attribute for P/Invokes

        [DllImport("gdi32.dll")]
#pragma warning disable CA5392 // Use DefaultDllImportSearchPaths attribute for P/Invokes
        private static extern int SelectClipRgn(IntPtr hDC, IntPtr hRgn);
#pragma warning restore CA5392 // Use DefaultDllImportSearchPaths attribute for P/Invokes

        [DllImport("user32.dll")]
#pragma warning disable CA5392 // Use DefaultDllImportSearchPaths attribute for P/Invokes
        private static extern int GetUpdateRgn(IntPtr hwnd, IntPtr hrgn, bool fErase);
#pragma warning restore CA5392 // Use DefaultDllImportSearchPaths attribute for P/Invokes
        private enum RegionFlags
        {
            ERROR = 0,
            NULLREGION = 1,
            SIMPLEREGION = 2,
            COMPLEXREGION = 3,
        }
        [DllImport("gdi32.dll")]
#pragma warning disable CA5392 // Use DefaultDllImportSearchPaths attribute for P/Invokes
        private static extern bool DeleteObject(IntPtr hObject);
#pragma warning restore CA5392 // Use DefaultDllImportSearchPaths attribute for P/Invokes

        [DllImport("gdi32.dll")]
#pragma warning disable CA5392 // Use DefaultDllImportSearchPaths attribute for P/Invokes
        private static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);
#pragma warning restore CA5392 // Use DefaultDllImportSearchPaths attribute for P/Invokes
    }
}
