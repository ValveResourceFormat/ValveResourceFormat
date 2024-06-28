using System.Drawing;
using System.Windows.Forms;
using Windows.Win32;

namespace GUI.Controls
{
    internal class SysMenuLogoButton : Button
    {
        //
    }

    internal class TransparentPanel : Panel
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == PInvoke.WM_NCHITTEST)
            {
                m.Result = PInvoke.HTTRANSPARENT;
            }
            else
            {
                base.WndProc(ref m);
            }
        }
    }

    internal class TransparentMenuStrip : MenuStrip
    {

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == PInvoke.WM_NCHITTEST)
            {
                var point = PointToClient(MainForm.LParamToPoint(m.LParam));

                foreach (ToolStripMenuItem item in Items)
                {
                    if (item.Bounds.Contains(point))
                    {
                        base.WndProc(ref m);
                        return;
                    }
                }

                m.Result = PInvoke.HTTRANSPARENT;
            }
            else
            {
                base.WndProc(ref m);
            }
        }
    }
}
