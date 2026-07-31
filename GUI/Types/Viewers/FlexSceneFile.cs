using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GUI.Utils;

namespace GUI.Types.Viewers
{
    class FlexSceneFile(VrfGuiContext vrfGuiContext) : IViewer, IDisposable
    {
        private string? vfeText;

        public static bool IsAccepted(uint magic)
        {
            return magic == ValveResourceFormat.FlexSceneFile.FlexSceneFile.MAGIC;
        }

        public async Task LoadAsync(Stream? stream)
        {
            var vfe = new ValveResourceFormat.FlexSceneFile.FlexSceneFile();

            if (stream != null)
            {
                vfe.Read(stream);
            }
            else
            {
                vfe.Read(vrfGuiContext.FileName);
            }

            vfeText = vfe.ToString();
        }

        public ViewerContent GetContent()
        {
            Debug.Assert(vfeText is not null);

            var content = new ViewerContent.Tabs(
            [
                new("Text", new ViewerContent.Text(vfeText)),
            ]);

            vfeText = null;

            return content;
        }

        public void Dispose()
        {
            //
        }
    }
}
