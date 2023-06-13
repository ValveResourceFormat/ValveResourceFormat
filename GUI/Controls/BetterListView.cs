using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls
{
    /// <inheritdoc/>
    sealed class BetterListView : ListView
    {
        public VrfGuiContext VrfGuiContext { get; set; }
    }
}
