using System.Drawing;
using System.Windows.Forms;
using GUI;
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

    public ThemedComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        Themer.ThemeControl(this);
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();

        DropDownBackColor = Themer.CurrentThemeColors.AppSoft;
        DropDownForeColor = Themer.CurrentThemeColors.Contrast;
        HighlightColor = Themer.CurrentThemeColors.Accent;
        HeaderColor = Themer.CurrentThemeColors.Border;
        BackColor = Themer.CurrentThemeColors.AppSoft;
        ForeColor = Themer.CurrentThemeColors.Contrast;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;

        e.DrawBackground();

        Color backColor = backColor = (e.State & DrawItemState.Selected) == DrawItemState.Selected
           ? HighlightColor
           : DropDownBackColor;

        string? text = string.Empty;

        if (Items[e.Index] is ThemedComboBoxItem themedComboBoxItem)
        {
            text = themedComboBoxItem.Text;

            if(themedComboBoxItem.IsHeader)
            {
                backColor = HeaderColor;
            }
        }
        else
        {
            text = GetItemText(Items[e.Index]);
        }

        Color foreColor = DropDownForeColor;

        using (SolidBrush brush = new SolidBrush(backColor))
        {
            e.Graphics.FillRectangle(brush, e.Bounds);
        }

        using (SolidBrush textBrush = new SolidBrush(foreColor))
        {
            e.Graphics.DrawString(text, e.Font ?? DefaultFont, textBrush, e.Bounds);
        }

        e.DrawFocusRectangle();
    }
}
