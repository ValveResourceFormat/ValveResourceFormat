using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GUI.Theme
{
    public class CustomToolStripRendererColorTable : ProfessionalColorTable
    {
        public CustomToolStripRendererColorTable(Color menuItemPressedGradientBegin, Color menuItemPressedGradientEnd, Color menuItemBorder, Color menuItemSelectedGradientBegin, Color menuItemSelectedGradientEnd, Color toolStripDropDownBackground, Color imageMarginGradientBegin, Color imageMarginGradientMiddle, Color imageMarginGradientEnd, Color separatorDark, Color menuBorder)
        {
            MenuItemPressedGradientBegin = menuItemPressedGradientBegin;
            MenuItemPressedGradientEnd = menuItemPressedGradientEnd;
            MenuItemBorder = menuItemBorder;
            MenuItemSelectedGradientBegin = menuItemSelectedGradientBegin;
            MenuItemSelectedGradientEnd = menuItemSelectedGradientEnd;
            ToolStripDropDownBackground = toolStripDropDownBackground;
            ImageMarginGradientBegin = imageMarginGradientBegin;
            ImageMarginGradientMiddle = imageMarginGradientMiddle;
            ImageMarginGradientEnd = imageMarginGradientEnd;
            SeparatorDark = separatorDark;
            MenuBorder = menuBorder;
        }

        /// <summary>
        /// Gets the starting color of the gradient used when 
        /// a top-level System.Windows.Forms.ToolStripMenuItem is pressed.
        /// </summary>
        public override Color MenuItemPressedGradientBegin { get; }

        /// <summary>
        /// Gets the end color of the gradient used when a top-level 
        /// System.Windows.Forms.ToolStripMenuItem is pressed.
        /// </summary>
        public override Color MenuItemPressedGradientEnd { get; }

        /// <summary>
        /// Gets the border color to use with a 
        /// System.Windows.Forms.ToolStripMenuItem.
        /// </summary>
        public override Color MenuItemBorder { get; }

        /// <summary>
        /// Gets the starting color of the gradient used when the 
        /// System.Windows.Forms.ToolStripMenuItem is selected.
        /// </summary>
        public override Color MenuItemSelectedGradientBegin { get; }

        /// <summary>
        /// Gets the end color of the gradient used when the 
        /// System.Windows.Forms.ToolStripMenuItem is selected.
        /// </summary>
        public override Color MenuItemSelectedGradientEnd { get; }

        /// <summary>
        /// Gets the solid background color of the 
        /// System.Windows.Forms.ToolStripDropDown.
        /// </summary>
        public override Color ToolStripDropDownBackground { get; }

        /// <summary>
        /// Gets the starting color of the gradient used in the image 
        /// margin of a System.Windows.Forms.ToolStripDropDownMenu.
        /// </summary>
        public override Color ImageMarginGradientBegin { get; }

        /// <summary>
        /// Gets the middle color of the gradient used in the image 
        /// margin of a System.Windows.Forms.ToolStripDropDownMenu.
        /// </summary>
        public override Color ImageMarginGradientMiddle { get; }

        /// <summary>
        /// Gets the end color of the gradient used in the image 
        /// margin of a System.Windows.Forms.ToolStripDropDownMenu.
        /// </summary>
        public override Color ImageMarginGradientEnd { get; }

        /// <summary>
        /// Gets the color to use to for shadow effects on 
        /// the System.Windows.Forms.ToolStripSeparator.
        /// </summary>
        public override Color SeparatorDark { get; }

        /// <summary>
        /// Gets the color that is the border color to use 
        /// on a System.Windows.Forms.MenuStrip.
        /// </summary>
        public override Color MenuBorder { get; }
    }
}
