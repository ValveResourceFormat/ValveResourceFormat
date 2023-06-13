using System.IO;
using System.Windows.Forms;
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

        public TabPage Create(VrfGuiContext vrfGuiContext, byte[] input)
        {
            var tab = new TabPage();
            var toolsAssetInfo = new ValveResourceFormat.ToolsAssetInfo.ToolsAssetInfo();

            if (input != null)
            {
                toolsAssetInfo.Read(new MemoryStream(input));
            }
            else
            {
                toolsAssetInfo.Read(vrfGuiContext.FileName);
            }

            var text = new MonospaceTextBox
            {
                Text = toolsAssetInfo.ToString().ReplaceLineEndings(),
            };
            tab.Controls.Add(text);

            return tab;
        }
    }
}
