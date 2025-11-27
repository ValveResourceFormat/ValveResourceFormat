using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Forms;
public class ThemedForm : Form
{
    public ThemedForm()
    {
        Themer.ApplyTheme(this);
    }
}
