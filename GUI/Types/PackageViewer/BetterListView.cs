using System.Drawing;
using System.Windows.Forms;
using GUI.Utils;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ListView;

namespace GUI.Types.PackageViewer
{
    /// <inheritdoc/>
    sealed class BetterListView : ListView
    {
        public Color BorderColor { get; set; } = Color.White;
        public Color Highlight { get; set; } = Color.White;

        public BetterListView()
        {
            OwnerDraw = true;
            BorderStyle = BorderStyle.None;
            DoubleBuffered = true;
            HotTracking = false;
        }

        public VrfGuiContext? VrfGuiContext { get; set; }

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

            AdjustLastColumn();
        }

        private void AdjustLastColumn()
        {
            if (Columns.Count == 0 || View != View.Details)
                return;

            int totalWidth = 0;
            for (int i = 0; i < Columns.Count - 1; i++)
            {
                totalWidth += Columns[i].Width;
            }

            int remainingWidth = ClientSize.Width - totalWidth;

            if (remainingWidth < 50)
                remainingWidth = 50;

            if (Columns[Columns.Count - 1].Width != remainingWidth)
            {
                Columns[Columns.Count - 1].Width = remainingWidth;
            }
        }

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            using var backBrush = new SolidBrush(BackColor);
            using var borderPen = new Pen(BorderColor);

            e.Graphics.FillRectangle(backBrush, e.Bounds);

            e.Graphics.DrawLine(borderPen,
                e.Bounds.Left, e.Bounds.Bottom - 1,
                e.Bounds.Right, e.Bounds.Bottom - 1);

            e.Graphics.DrawLine(borderPen,
                e.Bounds.Right - 1, e.Bounds.Top,
                e.Bounds.Right - 1, e.Bounds.Bottom - 1);

            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", e.Font,
                e.Bounds, ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (View != View.Details || Columns.Count == 0)
                return;

            // Calculate where items end
            int itemsBottom = 0;
            if (Items.Count > 0)
            {
                var lastItem = Items[Items.Count - 1];
                itemsBottom = lastItem.Bounds.Bottom;
            }
            else
            {
                itemsBottom = HeaderStyle != ColumnHeaderStyle.None ? 20 : 0;
            }

            if (itemsBottom >= ClientSize.Height)
                return;

            // Use the Graphics from PaintEventArgs - this respects DoubleBuffered
            using var backBrush = new SolidBrush(BackColor);
            var emptyRect = new Rectangle(0, itemsBottom, ClientSize.Width, ClientSize.Height - itemsBottom);
            e.Graphics.FillRectangle(backBrush, emptyRect);

            // Draw column separators
            using var borderPen = new Pen(BorderColor);
            int x = 0;
            for (int i = 0; i < Columns.Count - 1; i++)
            {
                x += Columns[i].Width;
                e.Graphics.DrawLine(borderPen, x - 1, itemsBottom, x - 1, ClientSize.Height);
            }
        }
    }
}
