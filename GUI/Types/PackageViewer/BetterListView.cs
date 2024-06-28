using System.Drawing;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Types.PackageViewer
{
    /// <inheritdoc/>
    sealed class BetterListView : ListView
    {
        public BetterListView()
        {
            OwnerDraw = true;
            BorderStyle = BorderStyle.None;
        }

        public VrfGuiContext VrfGuiContext { get; set; }

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

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            using var backBrush = new SolidBrush(MainForm.DarkModeCS.ThemeColors.AppFirm);
            using var borderPen = new Pen(MainForm.DarkModeCS.ThemeColors.Border);

            // Need to do this because for some reason the bottom edge is obscured
            var boundsRect = e.Bounds;
            boundsRect.Height -= Program.MainForm.AdjustForDPI(2);

            using var sf = new StringFormat();
            sf.Alignment = StringAlignment.Center;
            e.Graphics.FillRectangle(backBrush, boundsRect);
            e.Graphics.DrawRectangle(borderPen, boundsRect);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Font, boundsRect, MainForm.DarkModeCS.ThemeColors.Contrast);
        }
    }
}
