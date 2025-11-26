using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.GLViewers;
using GUI.Utils;
using SkiaSharp;

namespace GUI.Types.Viewers
{
    class Image(VrfGuiContext vrfGuiContext) : IViewer, IDisposable
    {
        private SKBitmap? bitmap;
        private GLTextureViewer? glViewer;

        public static bool IsAccepted(uint magic)
        {
            return magic == 0x474E5089 || /* png */
                   magic << 8 == 0xFFD8FF00 || /* jpg */
                   magic << 8 == 0x46494700; /* gif */
        }

        public async Task LoadAsync(Stream stream)
        {
            if (stream != null)
            {
                bitmap = SKBitmap.Decode(stream);
            }
            else
            {
                bitmap = SKBitmap.Decode(vrfGuiContext.FileName);
            }

            try
            {
                glViewer = new GLTextureViewer(vrfGuiContext, bitmap);
                glViewer.InitializeLoad();

                bitmap = null;
            }
            finally
            {
                bitmap?.Dispose();
            }
        }

        public TabPage Create()
        {
            Debug.Assert(glViewer is not null);

            var tab = new TabPage("IMAGE");
            tab.Controls.Add(glViewer.InitializeUiControls());

            return tab;
        }

        public void Dispose()
        {
            bitmap?.Dispose();
            glViewer?.Dispose();
        }
    }
}
