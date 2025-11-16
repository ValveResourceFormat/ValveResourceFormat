using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;

namespace GUI.Types.Viewers
{
    class ToolsAssetInfo(VrfGuiContext vrfGuiContext) : IViewer
    {
        private string? text;

        public static bool IsAccepted(uint magic)
        {
            return magic == ValveResourceFormat.ToolsAssetInfo.ToolsAssetInfo.MAGIC ||
                   magic == ValveResourceFormat.ToolsAssetInfo.ToolsAssetInfo.MAGIC2;
        }

        public async Task LoadAsync(Stream stream)
        {
            var toolsAssetInfo = new ValveResourceFormat.ToolsAssetInfo.ToolsAssetInfo();

            if (stream != null)
            {
                toolsAssetInfo.Read(stream);
            }
            else
            {
                toolsAssetInfo.Read(vrfGuiContext.FileName!);
            }

            text = toolsAssetInfo.ToString();
        }

        public TabPage Create()
        {
            Debug.Assert(text is not null);

            var tab = new TabPage();
            var textBox = CodeTextBox.Create(text);
            tab.Controls.Add(textBox);

            text = null;

            return tab;
        }
    }
}
