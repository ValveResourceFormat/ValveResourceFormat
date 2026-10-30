using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.GLViewers;
using GUI.Utils;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Source1.FileSystem;

namespace GUI.Types.Viewers
{
    class Source1MapViewer(VrfGuiContext vrfGuiContext) : IViewer, IDisposable
    {
        private RendererContext? rendererContext;
        private Source1FileSystem? fileSystem;
        private GLSource1MapViewer? glViewer;

        /// <summary>"VBSP", the magic of a map.</summary>
        public static bool IsAccepted(uint magic) => magic == 0x50534256;

        public async Task LoadAsync(Stream? stream)
        {
            await Task.Yield();

            // Owned by this viewer, so the map can mount its pakfile without leaking it into other files of the game
            fileSystem = Source1Content.Create(vrfGuiContext);

            rendererContext = vrfGuiContext.CreateRendererContext();
            rendererContext.Source1FileSystem = fileSystem;

            glViewer = new GLSource1MapViewer(vrfGuiContext, rendererContext, fileSystem, vrfGuiContext.FileName);
            glViewer.InitializeLoad();
        }

        public void Create(TabPage tab)
        {
            Debug.Assert(glViewer is not null);

            var tabs = new ThemedTabControl
            {
                Dock = DockStyle.Fill,
                Multiline = true,
            };
            tab.Controls.Add(tabs);

            var viewerTab = new ThemedTabPage("Map");
            viewerTab.Controls.Add(glViewer.InitializeUiControls());
            tabs.TabPages.Add(viewerTab);

            var entitiesTab = new ThemedTabPage("Entity List");
            entitiesTab.Controls.Add(new EntityViewer(vrfGuiContext, glViewer.MapEntities!, glViewer.SelectAndFocusEntity));
            tabs.TabPages.Add(entitiesTab);
        }

        public void NotifyVisible() => glViewer?.NotifyVisible();

        public void Dispose()
        {
            glViewer?.Dispose();
            rendererContext?.Dispose();
            fileSystem?.Dispose();
        }
    }
}
