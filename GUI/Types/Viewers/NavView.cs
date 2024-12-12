using System.IO;
using GUI.Controls;
using GUI.Utils;
using System.Windows.Forms;
using ValveResourceFormat.NavMesh;
using System.Text.Json;
using GUI.Types.Renderer;

namespace GUI.Types.Viewers
{
    class NavView : IViewer
    {
        private static JsonSerializerOptions JsonOptions = new JsonSerializerOptions() { WriteIndented = true };
        public static bool IsAccepted(uint magic)
        {
            return magic == NavMeshFile.MAGIC;
        }

        public TabPage Create(VrfGuiContext vrfGuiContext, Stream stream)
        {
            var tabOuterPage = new TabPage();
            var tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
            };
            tabOuterPage.Controls.Add(tabControl);
            var navMeshFile = new NavMeshFile();
            if (stream != null)
            {
                navMeshFile.Read(stream);
            }
            else
            {
                navMeshFile.Read(vrfGuiContext.FileName);
            }

            var navMeshPage = new TabPage("NAV MESH");
            var worldViewer = new GLNavMeshViewer(vrfGuiContext, navMeshFile);
            navMeshPage.Controls.Add(worldViewer);
            tabControl.Controls.Add(navMeshPage);

            var debugPage = new TabPage("NAV debug");
            var text = JsonSerializer.Serialize(navMeshFile, JsonOptions);
            var textControl = new CodeTextBox(text);
            debugPage.Controls.Add(textControl);
            tabControl.Controls.Add(debugPage);

            return tabOuterPage;
        }
    }
}
