using System.Windows.Forms;

namespace GUI.Controls;
public class TransparentPanel : Panel
{
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
