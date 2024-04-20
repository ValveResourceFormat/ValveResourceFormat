using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace GUI.Utils
{
    [ToolboxItem(true)]
    [ToolboxBitmap(typeof(TreeView))]
    class TreeViewDoubleBuffered : TreeView
    {
        public TreeViewDoubleBuffered()
        {
            DoubleBuffered = true;
        }

        /*
         * TODO: Disabled for now because it's making large trees like Dota 2 vpk load slower
         * See https://github.com/ValveResourceFormat/ValveResourceFormat/issues/776
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (DesignMode || Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                return;
            }

            _ = NativeMethods.SetWindowTheme(Handle, "explorer", null);
        }
        */
    }
}
