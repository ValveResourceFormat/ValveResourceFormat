using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace GUI.Utils
{
    [ToolboxItem(true)]
    [ToolboxBitmap(typeof(TreeView))]
    class TreeViewDoubleBuffered : TreeView
    {
        public TreeViewDoubleBuffered() : base()
        {
            DoubleBuffered = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (DesignMode || Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                return;
            }

#pragma warning disable WFO5001
            var theme = Application.IsDarkModeEnabled ? "DarkMode_Explorer" : "Explorer";

            PInvoke.SetWindowTheme((HWND)Handle, theme, null);
        }
    }
}
