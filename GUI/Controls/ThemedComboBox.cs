using System.Drawing;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls;

public class ThemedComboBoxItem
{
    public string Text { get; set; } = string.Empty;
    public bool IsHeader { get; set; }
}

public class ThemedComboBox : ComboBox
{
    public Color DropDownBackColor { get; set; } = SystemColors.Control;
    public Color DropDownForeColor { get; set; } = SystemColors.ControlText;
    public Color HighlightColor { get; set; } = SystemColors.Highlight;
    public Color HeaderColor { get; set; } = SystemColors.ControlDark;

    /// <summary>
    /// Whether text that does not fit loses its start rather than its end, keeping the distinctive end
    /// of names that share a prefix in view.
    /// </summary>
    public bool TrimStart { get; set; }

    public ThemedComboBox() : base()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        Themer.ThemeControl(this);
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();

        // Theme constants rather than a frozen Parent colour: this runs before the theming
        // walk has settled the parents, and nothing re-themes the dropdown list afterwards.
        DropDownBackColor = Themer.CurrentThemeColors.AppMiddle;
        DropDownForeColor = Themer.CurrentThemeColors.Contrast;
        HighlightColor = Themer.CurrentThemeColors.Accent;
        HeaderColor = Themer.CurrentThemeColors.Border;
        BackColor = Parent?.BackColor ?? Themer.CurrentThemeColors.AppMiddle;
        ForeColor = Themer.CurrentThemeColors.Contrast;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0)
        {
            return;
        }

        e.DrawBackground();

        var backColor = (e.State & DrawItemState.Selected) == DrawItemState.Selected
           ? HighlightColor
           : DropDownBackColor;

        var text = string.Empty;

        var themedComboBoxItem = Items[e.Index] as ThemedComboBoxItem;

        if (themedComboBoxItem == null)
        {
            text = GetItemText(Items[e.Index]) ?? string.Empty;
        }
        else
        {
            text = themedComboBoxItem.Text;

            if (themedComboBoxItem.IsHeader)
            {
                backColor = HeaderColor;
            }
        }

        var foreColor = DropDownForeColor;

        using (var brush = new SolidBrush(backColor))
        {
            e.Graphics.FillRectangle(brush, e.Bounds);
        }

        using (var textBrush = new SolidBrush(foreColor))
        {
            var bounds = e.Bounds;
            var flags = TextFormatFlags.Left | TextFormatFlags.PathEllipsis;

            //adds padding to the left of non header items
            if (themedComboBoxItem != null && !themedComboBoxItem.IsHeader)
            {
                var padding = this.AdjustForDPI(8);
                bounds.X += padding;
                bounds.Width -= padding;

                flags = TextFormatFlags.Left | TextFormatFlags.EndEllipsis;
            }

            if (TrimStart && themedComboBoxItem is not { IsHeader: true })
            {
                text = TrimStartToFit(e.Graphics, text, e.Font ?? Font, bounds.Width, flags);
            }

            TextRenderer.DrawText(e.Graphics, text, e.Font, bounds, foreColor, Color.Transparent, flags);

        }

        e.DrawFocusRectangle();
    }

    private static string TrimStartToFit(Graphics graphics, string text, Font font, int width, TextFormatFlags drawFlags)
    {
        // Measured with the padding the text is drawn with, minus the end ellipsis it would otherwise take
        var measureFlags = (drawFlags & ~(TextFormatFlags.EndEllipsis | TextFormatFlags.PathEllipsis)) | TextFormatFlags.SingleLine;

        if (TextRenderer.MeasureText(graphics, text, font, Size.Empty, measureFlags).Width <= width)
        {
            return text;
        }

        for (var start = 1; start < text.Length; start++)
        {
            var trimmed = string.Concat("...", text.AsSpan(start));

            if (TextRenderer.MeasureText(graphics, trimmed, font, Size.Empty, measureFlags).Width <= width)
            {
                return trimmed;
            }
        }

        return text;
    }
}
