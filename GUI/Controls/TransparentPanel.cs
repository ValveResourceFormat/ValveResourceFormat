using System.Drawing;
using System.Windows.Forms;

namespace GUI.Controls;

public class TransparentPanel : UnstyledPanel
{
    public TransparentPanel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.UserPaint, true);

        BackColor = Color.Transparent;
    }

    protected override void WndProc(ref Message m)
    {
        if (DesignMode)
        {
            base.WndProc(ref m);
            return;
        }

        if (m.Msg == Windows.Win32.PInvoke.WM_NCHITTEST)
        {
            m.Result = Windows.Win32.PInvoke.HTTRANSPARENT;
        }
        else
        {
            base.WndProc(ref m);
        }
    }
}
