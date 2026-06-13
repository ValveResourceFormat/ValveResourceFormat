using System.Collections;
using System.Runtime.CompilerServices;

namespace GUI.Types.PackageViewer
{
    internal class TreeViewFileSorter : IComparer
    {
        public int Compare(object? x, object? y)
        {
            // Normal C-style casts are actually very slow on something called this often, so we cheat.
            var tx = Unsafe.As<BetterTreeNode>(x)!;
            var ty = Unsafe.As<BetterTreeNode>(y)!;

            var folderx = tx.IsFolder ? -1 : 1;
            var foldery = ty.IsFolder ? -1 : 1;

            if (folderx != foldery)
            {
                return folderx - foldery;
            }

            return string.CompareOrdinal(tx.Text, ty.Text);
        }
    }
}
