using System.Drawing;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls;

public class ThemedMenuStrip : MenuStrip
{
    public new Size ImageScalingSize
    {
        get => base.ImageScalingSize;
        set => base.ImageScalingSize = new Size(this.AdjustForDPI(value.Width), this.AdjustForDPI(value.Height));
    }
}
