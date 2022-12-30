using System.Collections;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace GUI.Controls
{
    internal class TreeViewFileSorter : IComparer
    {
        public int Compare(object x, object y)
        {
            var tx = Unsafe.As<TreeNode>(x); //Normal c-style casts are actually very slow on something called this often, so we cheat
            var ty = Unsafe.As<TreeNode>(y);

            var dataX = Unsafe.As<VrfTreeViewData>(tx.Tag); //Again, perf is king here
            var dataY = Unsafe.As<VrfTreeViewData>(ty.Tag);

            var folderx = dataX.IsFolder ? -1 : 1;
            var foldery = dataY.IsFolder ? -1 : 1;

            if (folderx != foldery)
            {
                return folderx - foldery;
            }

            return string.CompareOrdinal(tx.Text, ty.Text);
        }
    }
}
