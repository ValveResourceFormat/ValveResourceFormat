using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace GUI.Controls
{
    [ToolboxItem(true)]
    [ToolboxBitmap(typeof(TreeView))]
    class TreeViewDoubleBuffered : TreeView
    {
        public TreeViewDoubleBuffered() : base()
        {
            DoubleBuffered = true;
            ItemHeight = 26;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (DesignMode || Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                return;
            }

            // Setting the Explorer theme changes plus buttons to expand folders into arrows
            var theme = Application.IsDarkModeEnabled ? "DarkMode_Explorer" : "Explorer";

            PInvoke.SetWindowTheme((HWND)Handle, theme, null);
        }
    }
}
