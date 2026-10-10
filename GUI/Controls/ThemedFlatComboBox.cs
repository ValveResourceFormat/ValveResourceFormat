using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls;

/// <summary>
/// A dropdown list whose closed box is drawn like a flat button: a plain rectangle with the selected item
/// and an arrow. The list it opens is the themed one.
/// </summary>
public class ThemedFlatComboBox : ThemedComboBox
{
    private bool hovered;

    public ThemedFlatComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnDropDown(EventArgs e)
    {
        Invalidate();
        base.OnDropDown(e);
    }

    protected override void OnDropDownClosed(EventArgs e)
    {
        // The list holds the mouse while it is open, so leaving the box then is never reported
        hovered = ClientRectangle.Contains(PointToClient(Cursor.Position));
        Invalidate();
        base.OnDropDownClosed(e);
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        Invalidate();
        base.OnSelectedIndexChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var colors = Themer.CurrentThemeColors;
        var isLight = colors.ColorMode == SystemColorMode.Classic;
        var rect = ClientRectangle;

        var backColor = DroppedDown
            ? colors.Accent
            : hovered ? colors.HoverAccent : colors.Border;

        using var backBrush = new SolidBrush(backColor);
        e.Graphics.FillRectangle(backBrush, rect);

        using var borderPen = new Pen(isLight ? ControlPaint.Dark(backColor, 0.01f) : ControlPaint.Light(backColor, 0.4f));
        e.Graphics.DrawRectangle(borderPen, 0, 0, rect.Width - 1, rect.Height - 1);

        var padding = this.AdjustForDPI(8);
        var arrowAreaWidth = this.AdjustForDPI(20);

        var textRect = new Rectangle(padding, 0, rect.Width - padding - arrowAreaWidth, rect.Height);
        TextRenderer.DrawText(e.Graphics, GetItemText(SelectedItem), Font, textRect, colors.Contrast,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        // The arrow that marks it as a dropdown
        var arrowHalfWidth = this.AdjustForDPI(4);
        var arrowHalfHeight = this.AdjustForDPI(2);
        var arrowX = rect.Right - (arrowAreaWidth / 2) - 1;
        var arrowY = rect.Height / 2;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var arrowPen = new Pen(colors.Contrast, this.AdjustForDPI(2));
        e.Graphics.DrawLines(arrowPen,
        [
            new Point(arrowX - arrowHalfWidth, arrowY - arrowHalfHeight),
            new Point(arrowX, arrowY + arrowHalfHeight),
            new Point(arrowX + arrowHalfWidth, arrowY - arrowHalfHeight),
        ]);
    }
}
