using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using GUI.Utils;
using Svg.Skia;

namespace GUI.Types.Viewers
{
    class ImageVector(VrfGuiContext vrfGuiContext) : IViewer, IDisposable
    {
        private SKSvg? svg;
        private GLTextureViewer? textureControl;

        public static bool IsAccepted(string fileName)
        {
            return fileName.EndsWith(".svg", StringComparison.InvariantCultureIgnoreCase);
        }

        public async Task LoadAsync(Stream stream)
        {
            svg = new SKSvg();

            if (stream != null)
            {
                svg.Load(stream);
            }
            else
            {
                svg.Load(vrfGuiContext.FileName!);
            }

            try
            {
                textureControl = new GLTextureViewer(vrfGuiContext, svg);
                textureControl.InitializeLoad();
                svg = null;
            }
            finally
            {
                svg?.Dispose();
            }
        }

        public void Create(TabPage tab)
        {
            tab.Controls.Add(textureControl!.InitializeUiControls());
        }

        public void Dispose()
        {
            svg?.Dispose();
            textureControl?.Dispose();
        }
    }
}
