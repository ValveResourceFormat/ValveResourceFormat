using System.Drawing;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Types.PackageViewer
{
    /// <inheritdoc/>
    sealed class BetterListView : ListView
    {
        public Color BorderColor { get; set; } = Color.White;
        public Color Highlight { get; set; } = Color.White;
        public VrfGuiContext? VrfGuiContext { get; set; }
        private bool isAdjustingColumns;

        public BetterListView() : base()
        {
            OwnerDraw = true;
            BorderStyle = BorderStyle.None;
            DoubleBuffered = true;
            HotTracking = false;
        }

        protected override void OnDrawItem(DrawListViewItemEventArgs e)
        {
            base.OnDrawItem(e);
            e.DrawDefault = true;
        }

        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
        {
            base.OnDrawSubItem(e);
            e.DrawDefault = true;
        }

        protected override void OnColumnWidthChanged(ColumnWidthChangedEventArgs e)
        {
            base.OnColumnWidthChanged(e);
            AdjustColumnWidths();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            AdjustColumnWidths();
        }

        private void AdjustColumnWidths(int flexibleColumnIndex = 0, int fixedColumnWidth = 100)
        {
            if (isAdjustingColumns || Columns.Count == 0 || View != View.Details)
            {
                return;
            }

            isAdjustingColumns = true;

            try
            {
                var scaledFixedWidth = this.AdjustForDPI(fixedColumnWidth);
                var availableWidth = ClientSize.Width;
                var fixedColumnsWidth = 0;

                for (var i = 0; i < Columns.Count; i++)
                {
                    if (i != flexibleColumnIndex)
                    {
                        fixedColumnsWidth += scaledFixedWidth;
                    }
                }

                var flexibleWidth = Math.Max(availableWidth - fixedColumnsWidth, scaledFixedWidth);

                for (var i = 0; i < Columns.Count; i++)
                {
                    var targetWidth = i == flexibleColumnIndex ? flexibleWidth : scaledFixedWidth;

                    if (Columns[i].Width != targetWidth)
                    {
                        Columns[i].Width = targetWidth;
                    }
                }
            }
            finally
            {
                isAdjustingColumns = false;
            }
        }

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            using var borderPen = new Pen(BorderColor);
            borderPen.Width = this.AdjustForDPI(1);

            e.Graphics.DrawLine(borderPen,
                e.Bounds.Left, e.Bounds.Bottom - 1,
                e.Bounds.Right, e.Bounds.Bottom - 1);

            if (e.ColumnIndex < Columns.Count - 1)
            {
                var pos = e.Bounds.Right - 2;
                e.Graphics.DrawLine(borderPen,
                    pos, e.Bounds.Top,
                    pos, e.Bounds.Bottom - 1);
            }

            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", e.Font,
                e.Bounds, ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            if (ListViewItemSorter is ListViewColumnSorter sorter && e.ColumnIndex == sorter.SortColumn && sorter.Order != SortOrder.None)
            {
                var iconName = sorter.Order == SortOrder.Ascending ? "SortUp" : "SortDown";

                var icon = MainForm.ImageList.Images[MainForm.ImageListLookup[iconName]];
                var x = e.Bounds.Right - icon.Width - this.AdjustForDPI(4);
                var y = e.Bounds.Top + (e.Bounds.Height - icon.Height) / 2;

                e.Graphics.DrawImage(icon, x, y);
            }
        }
    }
}
