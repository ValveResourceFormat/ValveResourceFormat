using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls;
internal class ThemedTabPage : TabPage
{
    public ThemedTabPage() : base()
    {
        Themer.ThemeControl(this);
    }

    public ThemedTabPage(string? text) : this()
    {
        Text = text;
    }
}
