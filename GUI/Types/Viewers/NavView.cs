using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.GLViewers;
using GUI.Utils;
using ValveResourceFormat.NavMesh;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Viewers
{
    class NavView(VrfGuiContext vrfGuiContext) : IViewer, IDisposable
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
                navMeshFile.Read(vrfGuiContext.FileName);
            }

            glViewer = new GLNavMeshViewer(vrfGuiContext, navMeshFile);
            glViewer.InitializeLoad();
        }

        public void Create(TabPage tabOuterPage)
        {
            var tabOuterPage = new TabPage();
            var tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
            };
            tabOuterPage.Controls.Add(tabControl);

            var navMeshPage = new TabPage("NAV MESH");
            navMeshPage.Controls.Add(glViewer!.InitializeUiControls());

            tabControl.Controls.Add(navMeshPage);

            var infoPage = new ThemedTabPage("NAV INFO");
            var infoText = navMeshFile.ToString();
            var infoTextControl = CodeTextBox.Create(infoText, CodeTextBox.HighlightLanguage.None);
            infoPage.Controls.Add(infoTextControl);
            tabControl.Controls.Add(infoPage);

            if (navMeshFile.CustomData != null)
            {
                var subVersionPage = new ThemedTabPage("NAV CUSTOM DATA");

                var kv = new KV3File(navMeshFile.CustomData);
                var subVersionDataText = kv.ToString();
                var subVersionDataTextControl = CodeTextBox.Create(subVersionDataText, CodeTextBox.HighlightLanguage.None);

                subVersionPage.Controls.Add(subVersionDataTextControl);
                tabControl.Controls.Add(subVersionPage);
            }
        }

        public void Dispose()
        {
            glViewer?.Dispose();
        }
    }
}
