using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Types.PackageViewer
{
    /// <inheritdoc/>
    sealed class BetterListView : ListView
    {
        public VrfGuiContext? VrfGuiContext { get; set; }

        public BetterListView() : base()
        {
            DoubleBuffered = true;
        }
    }
}
