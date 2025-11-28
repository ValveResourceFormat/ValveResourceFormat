using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Forms;
public class ThemedForm : Form
{
    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        Themer.ApplyTheme(this);
    }
}
