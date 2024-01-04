using System.Collections;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace GUI.Controls;

public class ListViewColumnSorter : IComparer
{
    /// <summary>
    /// Gets or sets the number of the column to which to apply the sorting operation.
    /// </summary>
    public int SortColumn { set; get; }

    /// <summary>
    /// Gets or sets the order of sorting to apply.
    /// </summary>
    public SortOrder Order { set; get; }

    public int Compare(object x, object y)
    {
        var compareResult = 0;
        var listviewX = Unsafe.As<ListViewItem>(x);
        var listviewY = Unsafe.As<ListViewItem>(y);

        var tX = Unsafe.As<BetterTreeNode>(listviewX.Tag);
        var tY = Unsafe.As<BetterTreeNode>(listviewY.Tag);

        var folderX = tX.IsFolder ? -1 : 1;
        var folderY = tY.IsFolder ? -1 : 1;

        if (folderX != folderY)
        {
            return folderX - folderY;
        }

        switch (SortColumn)
        {
            case 0:
                compareResult = string.CompareOrdinal(tX.Text, tY.Text);
                break;
            case 1:
                {
                    var sizeX = tX.IsFolder ? tX.TotalSize : tX.PackageEntry.TotalLength;
                    var sizeY = tY.IsFolder ? tY.TotalSize : tY.PackageEntry.TotalLength;

                    if (sizeX != sizeY)
                    {
                        compareResult = sizeX > sizeY ? 1 : -1;
                    }
                    break;
                }
            default:
                compareResult = string.CompareOrdinal(listviewX.SubItems[SortColumn].Text, listviewY.SubItems[SortColumn].Text);
                break;
        }

        if (Order == SortOrder.Ascending)
        {
            return compareResult;
        }
        else if (Order == SortOrder.Descending)
        {
            return -compareResult;
        }
        else
        {
            return 0;
        }
    }
}
