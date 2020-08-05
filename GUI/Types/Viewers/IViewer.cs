using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Types.Viewers
{
    public interface IViewer
    {
        public TabPage Create(VrfGuiContext vrfGuiContext, byte[] input);
    }
}
