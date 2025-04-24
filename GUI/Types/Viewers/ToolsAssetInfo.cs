using System.IO;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;

namespace GUI.Types.Viewers
{
    class ToolsAssetInfo : IViewer
    {
        public static bool IsAccepted(uint magic)
        {
            return magic == ValveResourceFormat.ToolsAssetInfo.ToolsAssetInfo.MAGIC ||
                   magic == ValveResourceFormat.ToolsAssetInfo.ToolsAssetInfo.MAGIC2;
        }

        public TabPage Create(VrfGuiContext vrfGuiContext, Stream stream)
        {
            var tab = new TabPage();
            var toolsAssetInfo = new ValveResourceFormat.ToolsAssetInfo.ToolsAssetInfo();

            if (stream != null)
            {
                toolsAssetInfo.Read(stream);
            }
            else
            {
                toolsAssetInfo.Read(vrfGuiContext.FileName!);
            }

            var text = new CodeTextBox(toolsAssetInfo.ToString());
            tab.Controls.Add(text);

            return tab;
        }
    }
}
