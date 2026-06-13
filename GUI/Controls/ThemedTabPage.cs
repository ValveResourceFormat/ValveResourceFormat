using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls;

internal class ThemedTabPage : TabPage
{
    public ThemedTabPage() : base()
    {
    }

    public ThemedTabPage(string? text) : this()
    {
        Text = text;
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        Themer.ThemeControl(this);
    }
}
