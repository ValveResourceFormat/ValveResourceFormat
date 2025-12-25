using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GUI.Types.Renderer;
using GUI.Utils;

namespace GUI.Controls;

internal class MainTabs : ThemedTabControl
{
    private int _closeButtonSize;
    private int CloseButtonSize
    {
        set { _closeButtonSize = value; RightSideTextPadding = _closeButtonSize + this.AdjustForDPI(10); }
        get { return _closeButtonSize; }
    }

    private int tabHeight;
    [Description("Height of tabs"), Category("Appearance")]
    public new int TabHeight
    {
        get { return tabHeight; }
        set { tabHeight = this.AdjustForDPI(value); SetCloseButtonSize(); }
    }

    public MainTabs()
    {
        SetCloseButtonSize();
    }

    private void SetCloseButtonSize()
    {
        CloseButtonSize = TabHeight / 4;
    }

    protected bool IsCloseButtonHovered { get; private set; }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        bool oldIsCloseButtonHovered = IsCloseButtonHovered;
        IsCloseButtonHovered = false;

        for (var i = 0; i < TabCount; i++)
        {
            var tabRect = GetTabRect(i);

            if (tabRect.Contains(e.Location) && i > 0)
            {
                IsCloseButtonHovered = GetCloseButtonRect(tabRect, this.AdjustForDPI(10)).Contains(this.PointToClient(Cursor.Position));
                break;
            }
        }

        if (oldIsCloseButtonHovered != IsCloseButtonHovered)
        {
            Invalidate();
        }
    }

    public int GetTabIndex(TabPage tab)
    {
        //Work out the index of the requested tab
        for (var i = 0; i < TabPages.Count; i++)
        {
            if (TabPages[i] == tab)
            {
                return i;
            }
        }

        return -1;
    }

    public void CloseTab(TabPage tab)
    {
        var tabIndex = GetTabIndex(tab);
        var isClosingCurrentTab = tabIndex == SelectedIndex;

        //The console cannot be closed!
        if (tabIndex == 0)
        {
            return;
        }

        //Close the requested tab
        Log.Info(nameof(MainForm), $"Closing {tab.Text}");

        RenderLoopThread.UnsetIfClosingParentOfCurrentGLControl(tab);

        if (isClosingCurrentTab && tabIndex > 0)
        {
            SelectedIndex = tabIndex - 1;
        }

        TabPages.Remove(tab);
        tab.Dispose();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);

        if (e.Button == MouseButtons.Left && IsCloseButtonHovered)
        {
            CloseTab(TabPages[HoveredIndex]);
        }
    }

    private Rectangle GetCloseButtonRect(Rectangle tabRect, int padding = 0)
    {
        int closeButtonSize = CloseButtonSize + padding;
        int closeButtonCenteringOffset = (tabRect.Top - closeButtonSize + TabHeight) / 2;
        return new Rectangle(tabRect.Right - closeButtonCenteringOffset - closeButtonSize, closeButtonCenteringOffset, closeButtonSize, closeButtonSize);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // skip first tab as it can never be closed
        for (var i = 1; i < TabCount; i++)
        {
            var isSelected = SelectedIndex == i;
            var isHovered = HoveredIndex == i;

            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using Pen closeButtonPen = new Pen(SelectedForeColor);
            closeButtonPen.Width = this.AdjustForDPI(1);

            if (HoveredIndex == i && IsCloseButtonHovered)
            {
                var closeButtonRectCircle = GetCloseButtonRect(GetTabRect(i), this.AdjustForDPI(10));

                Color closeButtonCircleColor = BackColor;

                if (isSelected)
                {
                    closeButtonCircleColor = SelectTabColor;
                }
                else if (isHovered)
                {
                    closeButtonCircleColor = HoverColor;
                }

                closeButtonCircleColor = Themer.CurrentThemeColors.ColorMode == SystemColorMode.Dark ?
                    ControlPaint.Dark(closeButtonCircleColor, 0.2f) :
                    ControlPaint.Light(closeButtonCircleColor, 0.2f);

                using Brush closeButtonCircleBrush = new SolidBrush(closeButtonCircleColor);
                e.Graphics.FillEllipse(closeButtonCircleBrush, closeButtonRectCircle);
            }
            var closeButtonRectX = GetCloseButtonRect(GetTabRect(i));

            e.Graphics.DrawLine(closeButtonPen, closeButtonRectX.X, closeButtonRectX.Y, closeButtonRectX.Right, closeButtonRectX.Bottom);
            e.Graphics.DrawLine(closeButtonPen, closeButtonRectX.X, closeButtonRectX.Bottom, closeButtonRectX.Right, closeButtonRectX.Top);
        }
    }
}


