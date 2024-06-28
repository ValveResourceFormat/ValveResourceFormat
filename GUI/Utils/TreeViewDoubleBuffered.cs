using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Windows.Win32;

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

            _ = PInvoke.SetWindowTheme((Windows.Win32.Foundation.HWND)Handle, "explorer", null);
        }
    }
}
