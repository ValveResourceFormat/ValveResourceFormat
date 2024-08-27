using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GUI.Theme
{
    public class CustomToolStripRenderer : ToolStripProfessionalRenderer
    {
        public CustomToolStripRenderer(Color menuItemPressedGradientBegin, Color menuItemPressedGradientEnd, Color menuItemBorder, Color menuItemSelectedGradientBegin, Color menuItemSelectedGradientEnd, Color toolStripDropDownBackground, Color imageMarginGradientBegin, Color imageMarginGradientMiddle, Color imageMarginGradientEnd, Color separatorDark, Color menuBorder) : base(new CustomToolStripRendererColorTable(menuItemPressedGradientBegin, menuItemPressedGradientEnd, menuItemBorder, menuItemSelectedGradientBegin, menuItemSelectedGradientEnd, toolStripDropDownBackground, imageMarginGradientBegin, imageMarginGradientMiddle, imageMarginGradientEnd, separatorDark, menuBorder)) { }
    }
}
