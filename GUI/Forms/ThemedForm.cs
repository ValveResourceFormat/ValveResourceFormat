using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Forms;
public class ThemedForm : Form
{
    public ThemedForm()
    {
        Themer.Style(this);
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        Themer.ApplySystemTheme(this);
    }
}
