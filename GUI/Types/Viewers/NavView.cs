using System.IO;
using GUI.Controls;
using GUI.Utils;
using System.Windows.Forms;
using ValveResourceFormat.NavMesh;
using System.Text.Json;

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

            var tabPage = new TabPage("NAV debug");
            var text = JsonSerializer.Serialize(navMeshFile, JsonOptions);
            var textControl = new CodeTextBox(text);
            tabPage.Controls.Add(textControl);
            tabControl.Controls.Add(tabPage);

            return tabOuterPage;
        }
    }
}
