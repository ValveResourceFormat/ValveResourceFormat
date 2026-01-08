using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.GLViewers;
using GUI.Types.Renderer;
using GUI.Utils;
using ValveResourceFormat.NavMesh;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Viewers
{
    class NavView(VrfGuiContext guiContext) : IViewer, IDisposable
    {
        private NavMeshFile navMeshFile = new();
        private GLNavMeshViewer? glViewer;

        public static bool IsAccepted(uint magic)
        {
            return magic == NavMeshFile.MAGIC;
        }

        public async Task LoadAsync(Stream stream)
        {
            if (stream != null)
            {
                navMeshFile.Read(stream);
            }
            else
            {
                navMeshFile.Read(guiContext.FileName);
            }

            RendererContext? rendererContext = null;

            try
            {
                rendererContext = guiContext.CreateRendererContext();

                glViewer = new GLNavMeshViewer(guiContext, rendererContext, navMeshFile);
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

            var navMeshPage = new ThemedTabPage("NAV MESH");
            navMeshPage.Controls.Add(glViewer!.InitializeUiControls());

            tabControl.Controls.Add(navMeshPage);
            glViewer.InitializeRenderLoop();

            var infoPage = new ThemedTabPage("NAV INFO");
            var infoText = navMeshFile.ToString();
            var infoTextControl = CodeTextBox.Create(infoText, CodeTextBox.HighlightLanguage.None);
            infoPage.Controls.Add(infoTextControl);
            tabControl.Controls.Add(infoPage);

            if (navMeshFile.CustomData != null)
            {
                var page = CreateKVTab("NAV CUSTOM DATA", navMeshFile.CustomData);
                tabControl.Controls.Add(page);
            }

            if (navMeshFile.KV3Unknown1 != null)
            {
                var page = CreateKVTab("NAV UNKNOWN KV3 1", navMeshFile.KV3Unknown1);
                tabControl.Controls.Add(page);
            }

            if (navMeshFile.KV3Unknown2 != null)
            {
                var page = CreateKVTab("NAV UNKNOWN KV3 2", navMeshFile.KV3Unknown2);
                tabControl.Controls.Add(page);
            }
        }

        public void Dispose()
        {
            glViewer?.Dispose();
        }

        private static ThemedTabPage CreateKVTab(string tabName, KVObject kvObject)
        {
            var kvPage = new ThemedTabPage(tabName);

            var kv = new KV3File(kvObject);
            var kvText = kv.ToString();
            var kvTextControl = CodeTextBox.Create(kvText, CodeTextBox.HighlightLanguage.None);

            kvPage.Controls.Add(kvTextControl);
            return kvPage;
        }
    }
}
