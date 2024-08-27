using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GUI.Theme
{
    // Taken from https://stackoverflow.com/questions/5569284/how-do-i-change-background-colour-of-tab-control-in-winforms
    // Thanks to them! Saved me some time.
    public class CustomTabControl : TabControl
    {
#pragma warning disable CA1822 // Mark members as static
        public new TabDrawMode DrawMode
#pragma warning restore CA1822 // Mark members as static
        {
            get
            {
                return TabDrawMode.OwnerDrawFixed;
            }
            set
            {
                // No you dont.
            }
        }

        private struct TabItemInfo
        {
            public Color BackColor;
            public Rectangle Bounds;
            public Font Font;
            public Color ForeColor;
            public int Index;
            public DrawItemState State;

            public TabItemInfo(DrawItemEventArgs e)
            {
                this.BackColor = e.BackColor;
                this.ForeColor = e.ForeColor;
                this.Bounds = e.Bounds;
                this.Font = e.Font;
                this.Index = e.Index;
                this.State = e.State;
            }
        }

        private Dictionary<int, TabItemInfo> _tabItemStateMap = new Dictionary<int, TabItemInfo>();

        public CustomTabControl()
        {
            base.DrawMode = TabDrawMode.OwnerDrawFixed;

            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

            TextAlign = ContentAlignment.TopCenter;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            //base.OnDrawItem(e);
            if (!_tabItemStateMap.ContainsKey(e.Index))
            {
                _tabItemStateMap.TryAdd(e.Index, new TabItemInfo(e));
            }
            else
            {
                _tabItemStateMap[e.Index] = new TabItemInfo(e);
            }
        }

        private const int WM_PAINT = 0x000F;
        private const int WM_ERASEBKGND = 0x0014;

        // Cache context to avoid repeatedly re-creating the object.
        // WM_PAINT is called frequently so it's better to declare it as a member.
        private BufferedGraphicsContext _bufferContext = BufferedGraphicsManager.Current;

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_PAINT:
                    {
                        // Let system do its thing first.
                        base.WndProc(ref m);

                        // Custom paint Tab items.
                        HandlePaint(ref m);

                        break;
                    }
                case WM_ERASEBKGND:
                    {
                        if (DesignMode)
                        {
                            // Ignore to prevent flickering in DesignMode.
                        }
                        else
                        {
                            base.WndProc(ref m);
                        }
                        break;
                    }
                default:
                    base.WndProc(ref m);
                    break;
            }
        }


        private Color _backColor = Color.FromArgb(38, 38, 38);
        private SolidBrush _backBrush = new SolidBrush(Color.FromArgb(38, 38, 38));
        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Always)]
        public Color TCBackColor
        {
            get
            {
                return _backColor;
            }
            set
            {
                _backColor = value;
                _backBrush.Dispose();
                _backBrush = new SolidBrush(_backColor);
            }
        }

        private Color _foreColor = Color.FromArgb(255, 255, 255);
        private SolidBrush _foreBrush = new SolidBrush(Color.FromArgb(255, 255, 255));
        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Always)]
        public Color TCForeColor
        {
            get
            {
                return _foreColor;
            }
            set
            {
                _foreColor = value;
                _foreBrush.Dispose();
                _foreBrush = new SolidBrush(_foreColor);
            }
        }

        private Color _pageBackColor = Color.FromArgb(54, 54, 54);
        private SolidBrush _pageBackBrush = new SolidBrush(Color.FromArgb(54, 54, 54));
        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Always)]
        public Color PageBackColor
        {
            get
            {
                return _pageBackColor;
            }
            set
            {
                _pageBackColor = value;
                _pageBackBrush.Dispose();
                _pageBackBrush = new SolidBrush(_pageBackColor);
            }
        }

        private StringFormat _tabTextFormat = new StringFormat();

        private void UpdateTextAlign()
        {
            switch (this.TextAlign)
            {
                case ContentAlignment.TopLeft:
                    _tabTextFormat.Alignment = StringAlignment.Near;
                    _tabTextFormat.LineAlignment = StringAlignment.Near;
                    break;
                case ContentAlignment.TopCenter:
                    _tabTextFormat.Alignment = StringAlignment.Center;
                    _tabTextFormat.LineAlignment = StringAlignment.Near;
                    break;
                case ContentAlignment.TopRight:
                    _tabTextFormat.Alignment = StringAlignment.Far;
                    _tabTextFormat.LineAlignment = StringAlignment.Near;
                    break;
                case ContentAlignment.MiddleLeft:
                    _tabTextFormat.Alignment = StringAlignment.Near;
                    _tabTextFormat.LineAlignment = StringAlignment.Center;
                    break;
                case ContentAlignment.MiddleCenter:
                    _tabTextFormat.Alignment = StringAlignment.Center;
                    _tabTextFormat.LineAlignment = StringAlignment.Center;
                    break;
                case ContentAlignment.MiddleRight:
                    _tabTextFormat.Alignment = StringAlignment.Far;
                    _tabTextFormat.LineAlignment = StringAlignment.Center;
                    break;
                case ContentAlignment.BottomLeft:
                    _tabTextFormat.Alignment = StringAlignment.Near;
                    _tabTextFormat.LineAlignment = StringAlignment.Far;
                    break;
                case ContentAlignment.BottomCenter:
                    _tabTextFormat.Alignment = StringAlignment.Center;
                    _tabTextFormat.LineAlignment = StringAlignment.Far;
                    break;
                case ContentAlignment.BottomRight:
                    _tabTextFormat.Alignment = StringAlignment.Far;
                    _tabTextFormat.LineAlignment = StringAlignment.Far;
                    break;
            }
        }


        private ContentAlignment _textAlign = ContentAlignment.TopLeft;
        public ContentAlignment TextAlign
        {
            get
            {
                return _textAlign;
            }
            set
            {
                if (value != _textAlign)
                {
                    _textAlign = value;
                    UpdateTextAlign();
                }
            }
        }

        private void HandlePaint(ref Message m)
        {
            using (var g = Graphics.FromHwnd(m.HWnd))
            {
                Rectangle r = ClientRectangle;
                using (var buffer = _bufferContext.Allocate(g, r))
                {
                    if (Enabled)
                    {
                        buffer.Graphics.FillRectangle(_backBrush, r);
                    }
                    else
                    {
                        buffer.Graphics.FillRectangle(_backBrush, r);
                    }

                    // Paint items
                    foreach (int index in _tabItemStateMap.Keys)
                    {
                        DrawTabItemInternal(buffer.Graphics, _tabItemStateMap[index]);
                    }

                    buffer.Render();
                }
            }
        }

        private void DrawTabItemInternal(Graphics gr, TabItemInfo tabInfo)
        {
            // Fixed: Was causing a crash when you closed a tab/page.
            if (this.TabPages.Count - 1 < tabInfo.Index)
                return;

            if ((tabInfo.State & DrawItemState.Selected) == DrawItemState.Selected)
            {
                gr.FillRectangle(_pageBackBrush, tabInfo.Bounds);
                gr.DrawString(this.TabPages[tabInfo.Index].Text, tabInfo.Font,
                    _foreBrush, tabInfo.Bounds, _tabTextFormat);
            }
            else
            {
                gr.FillRectangle(_backBrush, tabInfo.Bounds);
                gr.DrawString(this.TabPages[tabInfo.Index].Text, tabInfo.Font,
                    _foreBrush, tabInfo.Bounds, _tabTextFormat);
            }

            // Adding image support
            int imgNdx = this.TabPages[tabInfo.Index].ImageIndex;
            if (imgNdx != -1)
            {
                ImageList.Draw(gr, tabInfo.Bounds.X + 1, tabInfo.Bounds.Y + 1, 12, 12, imgNdx);

                // TODO; This is a temporary solution!
                if (TextAlign != ContentAlignment.MiddleCenter)
                    TextAlign = ContentAlignment.MiddleCenter;
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            _bufferContext.Dispose();
            _backBrush.Dispose();
            _foreBrush.Dispose();
            _pageBackBrush.Dispose();
            _tabTextFormat.Dispose();
        }
    }
}
