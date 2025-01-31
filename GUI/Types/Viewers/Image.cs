using System.IO;
using System.Windows.Forms;
using GUI.Types.Renderer;
using GUI.Utils;
using SkiaSharp;
using Svg.Skia;

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

        public static bool IsAcceptedVector(string fileName)
        {
            return fileName.EndsWith(".svg", StringComparison.InvariantCultureIgnoreCase);
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

        public TabPage CreateVector(VrfGuiContext vrfGuiContext, Stream stream)
        {
            var svg = new SKSvg();

            if (stream != null)
            {
                svg.Load(stream);
            }
            else
            {
                svg.Load(vrfGuiContext.FileName);
            }

            try
            {
                var textureControl = new GLTextureViewer(vrfGuiContext, svg);
                var tab = new TabPage("IMAGE");
                tab.Controls.Add(textureControl);
                svg = null;

                return tab;
            }
            finally
            {
                svg?.Dispose();
            }
        }
    }
}
