using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using GUI.Utils;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace GUI.Controls
{
    [ToolboxItem(true)]
    [ToolboxBitmap(typeof(TreeView))]
    class TreeViewDoubleBuffered : TreeView
    {
        // Disable horizontal scrollbar
        protected override CreateParams CreateParams
        {
            get
            {
                const int TVS_NOHSCROLL = 0x8000;

                var cp = base.CreateParams;
                cp.Style |= TVS_NOHSCROLL;
                return cp;
            }
        }

        public TreeViewDoubleBuffered() : base()
        {
            DoubleBuffered = true;
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();

            ItemHeight = this.AdjustForDPI(26);
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
