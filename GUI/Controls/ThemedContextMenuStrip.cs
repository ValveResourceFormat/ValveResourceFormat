using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using GUI.Utils;
using Windows.Win32.Graphics.Dwm;

namespace GUI.Controls;

internal class ThemedContextMenuStrip : ContextMenuStrip
{
    public ThemedContextMenuStrip(IContainer container) : base()
    {
        // this constructor ensures ContextMenuStrip is disposed properly since its not parented to the form.
        ArgumentNullException.ThrowIfNull(container);

        container.Add(this);
    }

    public new Size ImageScalingSize
    {
        get => base.ImageScalingSize;
        set => base.ImageScalingSize = new Size(this.AdjustForDPI(value.Width), this.AdjustForDPI(value.Height));
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();

        BackColor = Themer.CurrentThemeColors.AppMiddle;
        RenderMode = ToolStripRenderMode.Professional;
        Renderer = new DarkToolStripRenderer(new CustomColorTable());
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        unsafe
        {
            var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
            Windows.Win32.PInvoke.DwmSetWindowAttribute(
                (Windows.Win32.Foundation.HWND)Handle,
                DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE,
                &preference,
                sizeof(DWM_WINDOW_CORNER_PREFERENCE));
        }
    }
}
