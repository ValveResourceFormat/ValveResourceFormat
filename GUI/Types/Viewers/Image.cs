using System.IO;
using System.Windows.Forms;
using GUI.Types.Renderer;
using GUI.Utils;
using SkiaSharp;

namespace GUI.Types.Viewers
{
    class Image : IViewer
    {
        public static bool IsAccepted(uint magic)
        {
            return magic == 0x474E5089 || /* png */
                   magic << 8 == 0xFFD8FF00 || /* jpg */
                   magic << 8 == 0x46494700; /* gif */
        }

        public TabPage Create(VrfGuiContext vrfGuiContext, Stream stream)
        {
            SKBitmap bitmap;

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
                var textureControl = new GLTextureViewer(vrfGuiContext, bitmap);
                var tab = new TabPage("IMAGE");
                tab.Controls.Add(textureControl);
                bitmap = null;

                return tab;
            }
            finally
            {
                bitmap?.Dispose();
            }
        }
    }
}
