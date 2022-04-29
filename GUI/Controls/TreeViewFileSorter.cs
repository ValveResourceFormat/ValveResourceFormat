using System.Collections;
using System.Windows.Forms;

namespace GUI.Controls
{
    internal class TreeViewFileSorter : IComparer
    {
        public int Compare(object x, object y)
        {
            var tx = x as TreeNode;
            var ty = y as TreeNode;

            var folderx = tx.Tag is TreeViewFolder;
            var foldery = ty.Tag is TreeViewFolder;

            if (folderx && !foldery)
            {
                return -1;
            }

            if (!folderx && foldery)
            {
                return 1;
            }

            return string.CompareOrdinal(tx.Text, ty.Text);
        }
    }
}
