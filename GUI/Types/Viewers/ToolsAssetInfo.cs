using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GUI.Utils;

namespace GUI.Types.Viewers
{
    class ToolsAssetInfo(VrfGuiContext vrfGuiContext) : IViewer, IDisposable
    {
        private string? text;

        public static bool IsAccepted(uint magic)
        {
            return magic == ValveResourceFormat.ToolsAssetInfo.ToolsAssetInfo.MAGIC ||
                   magic == ValveResourceFormat.ToolsAssetInfo.ToolsAssetInfo.MAGIC2;
        }

        public async Task LoadAsync(Stream? stream)
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

        public ViewerContent GetContent()
        {
            Debug.Assert(text is not null);

            var content = new ViewerContent.Text(text);

            text = null;

            return content;
        }

        public void Dispose()
        {
            //
        }
    }
}
