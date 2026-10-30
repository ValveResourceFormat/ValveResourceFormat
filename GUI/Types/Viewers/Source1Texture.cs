using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using GUI.Utils;
using SkiaSharp;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Source1.Texture;

namespace GUI.Types.Viewers
{
    class Source1Texture(VrfGuiContext vrfGuiContext) : IViewer, IDisposable
    {
        private GLTextureViewer? glViewer;

        /// <summary>"VTF" and a zero byte.</summary>
        public static bool IsAccepted(uint magic) => magic == 0x00465456;

        public async Task LoadAsync(Stream? stream)
        {
            await Task.Yield();

            var input = Source1Content.OpenFile(vrfGuiContext, stream);

            SKBitmap? bitmap = null;
            RendererContext? renderContext = null;

            try
            {
                var vtf = new VtfFile(input);

                // The first frame and face of the largest mip, the one that has pixel data for any VTF
                bitmap = vtf.DecodeMip(0, 0, 0);
                renderContext = vrfGuiContext.CreateRendererContext();

                glViewer = new GLTextureViewer(vrfGuiContext, renderContext, bitmap);
                glViewer.InitializeLoad();
                bitmap = null;
                renderContext = null;
            }
            finally
            {
                bitmap?.Dispose();
                renderContext?.Dispose();

                if (stream == null)
                {
                    await input.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        public void Create(TabPage tab)
        {
            Debug.Assert(glViewer is not null);

            tab.Controls.Add(glViewer.InitializeUiControls());
        }

        public void NotifyVisible() => glViewer?.NotifyVisible();

        public void Dispose() => glViewer?.Dispose();
    }
}
