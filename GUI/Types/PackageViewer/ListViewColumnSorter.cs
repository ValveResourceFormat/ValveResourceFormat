using System.Collections;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace GUI.Types.PackageViewer;

public class ListViewColumnSorter : IComparer
{
    /// <summary>
    /// Gets or sets the number of the column to which to apply the sorting operation.
    /// </summary>
    public int SortColumn { set; get; }

    /// <summary>
    /// Gets or sets the order of sorting to apply.
    /// </summary>
    public SortOrder Order { set; get; } = SortOrder.Ascending;

    public int Compare(object? x, object? y)
    {
        var compareResult = 0;
        var tX = Unsafe.As<BetterListViewItem>(x)!;
        var tY = Unsafe.As<BetterListViewItem>(y)!;

        // Parent navigation item always stays at the top
        if (tX.Tag is BetterListViewItem.ParentNavigationTag)
        {
            return -1;
        }
        else if (tY.Tag is BetterListViewItem.ParentNavigationTag)
        {
            return 1;
        }

        var folderX = tX.IsFolder ? -1 : 1;
        var folderY = tY.IsFolder ? -1 : 1;

        switch (SortColumn)
        {
            case 0:
                if (folderX != folderY)
                {
                    return folderX - folderY;
                }

                compareResult = string.CompareOrdinal(tX.Text, tY.Text);
                break;
            case 1:
                {
                    var sizeX = tX.IsFolder ? tX.PkgNode!.TotalSize : tX.PackageEntry!.TotalLength;
                    var sizeY = tY.IsFolder ? tY.PkgNode!.TotalSize : tY.PackageEntry!.TotalLength;

                    if (sizeX != sizeY)
                    {
                        compareResult = sizeX > sizeY ? 1 : -1;
                    }
                    break;
                }
            default:
                compareResult = string.CompareOrdinal(tX.SubItems[SortColumn].Text, tY.SubItems[SortColumn].Text);
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
