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
    }
}
