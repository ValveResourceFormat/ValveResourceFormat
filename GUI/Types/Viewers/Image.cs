using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using GUI.Utils;
using SkiaSharp;

namespace GUI.Types.Viewers
{
    class Image(VrfGuiContext vrfGuiContext) : IViewer, IDisposable
    {
        private SKBitmap? bitmap;

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
        }

        public TabPage Create()
        {
            Debug.Assert(bitmap is not null);

            try
            {
                var glViewer = new GLTextureViewer(vrfGuiContext, bitmap);
                glViewer.InitializeLoad();
                var tab = new TabPage("IMAGE");
                tab.Controls.Add(glViewer.InitializeUiControls());
                bitmap = null;

                return tab;
            }
            finally
            {
                bitmap?.Dispose();
            }
        }

        public void Dispose()
        {
            if (bitmap != null)
            {
                bitmap.Dispose();
                bitmap = null;
            }
        }
    }
}
