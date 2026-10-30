using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.GLViewers;
using GUI.Utils;
using ValveResourceFormat.Renderer;

namespace GUI.Types.Viewers
{
    class Source1Material(VrfGuiContext vrfGuiContext) : IViewer, IDisposable
    {
        private RendererContext? rendererContext;
        private GLSource1MaterialViewer? glViewer;
        private string text = string.Empty;

        public static bool IsAccepted(string fileName)
            => fileName.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase);

        public async Task LoadAsync(Stream? stream)
        {
            await Task.Yield();

            var input = Source1Content.OpenFile(vrfGuiContext, stream);

            try
            {
                using var reader = new StreamReader(input);
                text = await reader.ReadToEndAsync().ConfigureAwait(false);
            }
            finally
            {
                if (stream == null)
                {
                    await input.DisposeAsync().ConfigureAwait(false);
                }
            }

            var fileSystem = Source1Content.GetShared(vrfGuiContext);
            var materialPath = Source1Content.GetGamePath(vrfGuiContext, fileSystem);

            rendererContext = vrfGuiContext.CreateRendererContext();
            rendererContext.Source1FileSystem = fileSystem;

            glViewer = new GLSource1MaterialViewer(vrfGuiContext, rendererContext, materialPath);
            glViewer.InitializeLoad();
        }

        public void Create(TabPage tab)
        {
            Debug.Assert(glViewer is not null);

            var tabs = new ThemedTabControl
            {
                Dock = DockStyle.Fill,
            };
            tab.Controls.Add(tabs);

            var materialTab = new ThemedTabPage("MATERIAL");
            materialTab.Controls.Add(glViewer.InitializeUiControls());
            tabs.TabPages.Add(materialTab);

            ViewerContentPresenter.AddContentTab(tabs, "VMT", new ViewerContent.Text(text, HighlightLanguage.KeyValues));
        }

        public void NotifyVisible() => glViewer?.NotifyVisible();

        public void Dispose()
        {
            glViewer?.Dispose();
            rendererContext?.Dispose();
        }
    }
}
