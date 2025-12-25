using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GUI.Types.Renderer;
using GUI.Utils;

namespace GUI.Controls;

internal class MainTabs : ThemedTabControl
{

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
                IsCloseButtonHovered = GetCloseButtonRect(tabRect).Contains(this.PointToClient(Cursor.Position));
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

    private Rectangle GetCloseButtonRect(Rectangle tabRect)
    {
        int closeButtonSize = TabHeight / 3;
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

            bool isCloseButtonHighlighted = HoveredIndex == i && (IsCloseButtonHovered || isHovered && !isSelected);
            using Pen closeButtonPen = new Pen(isCloseButtonHighlighted ? SelectedForeColor : ForeColor);
            closeButtonPen.Width = this.AdjustForDPI(1);

            var closeButtonRect = GetCloseButtonRect(GetTabRect(i));

            e.Graphics.DrawLine(closeButtonPen, closeButtonRect.X, closeButtonRect.Y, closeButtonRect.Right, closeButtonRect.Bottom);
            e.Graphics.DrawLine(closeButtonPen, closeButtonRect.X, closeButtonRect.Bottom, closeButtonRect.Right, closeButtonRect.Top);
        }
    }
}


