using System.Drawing;
using System.Windows.Forms;
using GUI.Utils;
using Windows.Win32;

namespace GUI.Controls
{
    public class ThemedMenuStrip : MenuStrip
    {
        public new Size ImageScalingSize
        {
            get => base.ImageScalingSize;
            set => base.ImageScalingSize = new Size(this.AdjustForDPI(value.Width), this.AdjustForDPI(value.Height));
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();

            BackColor = Themer.CurrentThemeColors.App;
            RenderMode = ToolStripRenderMode.Professional;
            Renderer = new DarkToolStripRenderer(new CustomColorTable());
        }

        protected override void WndProc(ref Message m)
        {
            if (DesignMode)
            {
                base.WndProc(ref m);
                return;
            }

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
