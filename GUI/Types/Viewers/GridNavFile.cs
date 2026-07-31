using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GUI.Utils;

namespace GUI.Types.Viewers
{
    class GridNavFile(VrfGuiContext vrfGuiContext) : IViewer, IDisposable
    {
        private string? infoText;

        public static bool IsAccepted(uint magic)
        {
            return magic == ValveResourceFormat.MapFormats.GridNavFile.MAGIC;
        }

        public async Task LoadAsync(Stream? stream)
        {
            var navMeshFile = new ValveResourceFormat.MapFormats.GridNavFile();

            if (stream != null)
            {
                navMeshFile.Read(stream);
            }
            else
            {
                navMeshFile.Read(vrfGuiContext.FileName);
            }

            infoText = navMeshFile.ToString();
        }

        public ViewerContent GetContent()
        {
            Debug.Assert(infoText is not null);

            var content = new ViewerContent.Tabs(
            [
                new("GRID NAV", new ViewerContent.Text(infoText, HighlightLanguage.None)),
            ]);

            infoText = null;

            return content;
        }

        public void Dispose()
        {
            //
        }
    }
}
