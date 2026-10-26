using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.GLViewers;
using GUI.Utils;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;

namespace GUI.Types.Viewers
{
    /// <summary>
    /// Views a source map (.vmap) that is not compiled, drawn the way Hammer shows it.
    /// </summary>
    class MapDocumentViewer(VrfGuiContext guiContext) : IViewer, IDisposable
    {
        private MapDocument? document;
        private GLMapDocumentViewer? glViewer;

        public static bool IsAccepted(uint magic, string fileName)
        {
            return magic == BinaryKeyValues2.MAGIC && fileName.EndsWith(".vmap", StringComparison.OrdinalIgnoreCase);
        }

        public async Task LoadAsync(Stream? stream)
        {
            await Task.Yield();

            if (stream != null)
            {
                try
                {
                    document = MapDocument.Load(stream, guiContext.FileName);
                }
                finally
                {
                    stream.Close();
                }
            }
            else
            {
                document = MapDocument.Load(guiContext.FileName);
            }

            RendererContext? rendererContext = null;

            try
            {
                rendererContext = guiContext.CreateRendererContext();

                glViewer = new GLMapDocumentViewer(guiContext, rendererContext, document);
                glViewer.InitializeLoad();
                rendererContext = null;
            }
            finally
            {
                rendererContext?.Dispose();
            }
        }

        public void Create(TabPage tabOuterPage)
        {
            var tabControl = new ThemedTabControl
            {
                Dock = DockStyle.Fill,
            };
            tabOuterPage.Controls.Add(tabControl);

            var mapPage = new ThemedTabPage("MAP");
            mapPage.Controls.Add(glViewer!.InitializeUiControls());
            tabControl.Controls.Add(mapPage);

            // A map can be hundreds of megabytes, so its text is only produced when asked for
            ViewerContentPresenter.AddContentTab(tabControl, "TEXT", new ViewerContent.LazyText(GetText));
        }

        private string GetText()
        {
            using var ms = new MemoryStream();
            document!.Save(ms, "keyvalues2", 4);
            ms.Seek(0, SeekOrigin.Begin);

            using var reader = new StreamReader(ms);
            return reader.ReadToEnd();
        }

        public void NotifyVisible() => glViewer?.NotifyVisible();

        public void Dispose()
        {
            glViewer?.Dispose();
            document?.Dispose();
        }
    }
}
