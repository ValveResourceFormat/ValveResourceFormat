using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GUI.Types.GLViewers;
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

    private readonly ToolTip tabToolTip = new();
    private int lastHoveredTabIndex = -1;
    private readonly Timer tooltipTimer = new();
    private int pendingTooltipTabIndex = -1;

    public MainTabs()
    {
        SetCloseButtonSize();

        // Enable tooltip on the control
        tabToolTip.ShowAlways = true;
        tabToolTip.Active = true;

        // Set up tooltip delay timer
        tooltipTimer.Interval = 1000; // 1 second delay
        tooltipTimer.Tick += TooltipTimer_Tick;
    }

    private void TooltipTimer_Tick(object? sender, EventArgs e)
    {
        tooltipTimer.Stop();

        if (pendingTooltipTabIndex >= 0 && pendingTooltipTabIndex < TabPages.Count)
        {
            var tabText = TabPages[pendingTooltipTabIndex].Text;
            var mousePos = PointToClient(Cursor.Position);
            tabToolTip.Show(tabText, this, mousePos.X, mousePos.Y + 20, 3000);
        }
    }

    private void SetCloseButtonSize()
    {
        CloseButtonSize = TabHeight / 4;
    }

    protected int CloseButtonHoveredIndex { get; private set; } = -1;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var hoveredTabIndex = -1;
        var oldCloseButtonHoveredIndex = CloseButtonHoveredIndex;
        CloseButtonHoveredIndex = -1;

        for (var i = 1; i < TabCount; i++)
        {
            var tabRect = GetTabRect(i);

            if (tabRect.Contains(e.Location))
            {
                hoveredTabIndex = i;

                if (GetCloseButtonRect(tabRect, this.AdjustForDPI(10)).Contains(PointToClient(Cursor.Position)))
                {
                    CloseButtonHoveredIndex = i;
                }

                break;
            }
        }

        // Update tooltip when hovering over a different tab
        if (hoveredTabIndex != lastHoveredTabIndex)
        {
            // Stop any pending tooltip
            tooltipTimer.Stop();
            tabToolTip.Hide(this);

            if (hoveredTabIndex >= 0 && hoveredTabIndex < TabPages.Count)
            {
                // Start timer to show tooltip after delay
                pendingTooltipTabIndex = hoveredTabIndex;
                tooltipTimer.Start();
            }
            else
            {
                pendingTooltipTabIndex = -1;
            }

            lastHoveredTabIndex = hoveredTabIndex;
        }

        if (oldCloseButtonHoveredIndex != CloseButtonHoveredIndex)
        {
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        tooltipTimer.Stop();
        tabToolTip.Hide(this);
        lastHoveredTabIndex = -1;
        pendingTooltipTabIndex = -1;
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

        if (e.Button == MouseButtons.Left && CloseButtonHoveredIndex > -1)
        {
            CloseTab(TabPages[CloseButtonHoveredIndex]);
        }
    }

    private Rectangle GetCloseButtonRect(Rectangle tabRect, int padding = 0)
    {
        var closeButtonSize = CloseButtonSize + padding;
        var closeButtonCenteringOffset = (tabRect.Top - closeButtonSize + TabHeight) / 2;
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

            var textColor = ForeColor;

            if (isSelected || isHovered)
            {
                textColor = SelectedForeColor;
            }

            using var closeButtonPen = new Pen(textColor);
            closeButtonPen.Width = this.AdjustForDPI(1);

            if (CloseButtonHoveredIndex == i)
            {
                var closeButtonRectCircle = GetCloseButtonRect(GetTabRect(i), this.AdjustForDPI(10));

                var closeButtonCircleColor = BackColor;

                if (isSelected)
                {
                    closeButtonCircleColor = SelectTabColor;
                }
                else if (isHovered)
                {
                    closeButtonCircleColor = HoverColor;
                }

                closeButtonCircleColor = Themer.CurrentThemeColors.ColorMode == SystemColorMode.Dark ?
                    ControlPaint.Light(closeButtonCircleColor, 0.4f) :
                    ControlPaint.Dark(closeButtonCircleColor, 0.01f);

                using Brush closeButtonCircleBrush = new SolidBrush(closeButtonCircleColor);
                e.Graphics.FillEllipse(closeButtonCircleBrush, closeButtonRectCircle);
            }
            var closeButtonRectX = GetCloseButtonRect(GetTabRect(i));

            e.Graphics.DrawLine(closeButtonPen, closeButtonRectX.X, closeButtonRectX.Y, closeButtonRectX.Right, closeButtonRectX.Bottom);
            e.Graphics.DrawLine(closeButtonPen, closeButtonRectX.X, closeButtonRectX.Bottom, closeButtonRectX.Right, closeButtonRectX.Top);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tooltipTimer?.Stop();
            tooltipTimer?.Dispose();
            tabToolTip?.Dispose();
        }
        base.Dispose(disposing);
    }
}
