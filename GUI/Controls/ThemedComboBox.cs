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

    public ThemedComboBox() : base()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        Themer.ThemeControl(this);
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();

        DropDownBackColor = Parent?.BackColor ?? Themer.CurrentThemeColors.AppMiddle;
        DropDownForeColor = Themer.CurrentThemeColors.Contrast;
        HighlightColor = Themer.CurrentThemeColors.Accent;
        HeaderColor = Themer.CurrentThemeColors.Border;
        BackColor = Parent?.BackColor ?? Color.Red;
        ForeColor = Themer.CurrentThemeColors.Contrast;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0)
        {
            return;
        }

        e.DrawBackground();

        Color backColor = backColor = (e.State & DrawItemState.Selected) == DrawItemState.Selected
           ? HighlightColor
           : DropDownBackColor;

        var text = string.Empty;

        var themedComboBoxItem = Items[e.Index] as ThemedComboBoxItem;

        if (themedComboBoxItem == null)
        {
            text = GetItemText(Items[e.Index]);
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

            //adds padding to the left of non header items
            if (themedComboBoxItem != null && !themedComboBoxItem.IsHeader)
            {
                var padding = this.AdjustForDPI(8);
                bounds.X += padding;
                bounds.Width -= padding;
            }

            TextRenderer.DrawText(e.Graphics, text, e.Font, bounds, foreColor, Color.Transparent, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

        }

        e.DrawFocusRectangle();
    }
}
