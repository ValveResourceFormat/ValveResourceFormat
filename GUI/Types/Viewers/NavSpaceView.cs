using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.GLViewers;
using GUI.Utils;
using ValveResourceFormat.NavMesh;
using ValveResourceFormat.Renderer;

namespace GUI.Types.Viewers
{
    class NavSpaceView(VrfGuiContext guiContext) : IViewer, IDisposable
    {
        private readonly NavSpaceFile navSpaceFile = new();
        private GLNavSpaceViewer? glViewer;

        public static bool IsAccepted(uint magic)
        {
            return magic == NavSpaceFile.MAGIC;
        }

        public async Task LoadAsync(Stream? stream)
        {
            if (stream != null)
            {
                navSpaceFile.Read(stream);
            }
            else
            {
                navSpaceFile.Read(guiContext.FileName);
            }

            RendererContext? rendererContext = null;

            try
            {
                rendererContext = guiContext.CreateRendererContext();

                glViewer = new GLNavSpaceViewer(guiContext, rendererContext, navSpaceFile);
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

            var navSpacePage = new ThemedTabPage("NAV SPACE");
            navSpacePage.Controls.Add(glViewer!.InitializeUiControls());
            tabControl.Controls.Add(navSpacePage);

            var infoPage = new ThemedTabPage("NAV SPACE INFO");
            var infoTextControl = CodeTextBox.Create(navSpaceFile.ToString(), HighlightLanguage.None);
            infoPage.Controls.Add(infoTextControl);
            tabControl.Controls.Add(infoPage);
        }

        public void NotifyVisible() => glViewer?.NotifyVisible();

        public void Dispose()
        {
            glViewer?.Dispose();
        }
    }
}
