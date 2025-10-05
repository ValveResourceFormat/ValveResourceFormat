using System.IO;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.GLViewers;
using GUI.Utils;
using ValveResourceFormat.NavMesh;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Viewers
{
    class NavView : IViewer
    {
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

            var infoPage = new TabPage("NAV INFO");
            var infoText = navMeshFile.ToString();
            var infoTextControl = CodeTextBox.Create(infoText, CodeTextBox.HighlightLanguage.None);
            infoPage.Controls.Add(infoTextControl);
            tabControl.Controls.Add(infoPage);

            if (navMeshFile.CustomData != null)
            {
                var subVersionPage = new TabPage("NAV CUSTOM DATA");

                var kv = new KV3File(navMeshFile.CustomData);
                var subVersionDataText = kv.ToString();
                var subVersionDataTextControl = CodeTextBox.Create(subVersionDataText, CodeTextBox.HighlightLanguage.None);

                subVersionPage.Controls.Add(subVersionDataTextControl);
                tabControl.Controls.Add(subVersionPage);
            }

            return tabOuterPage;
        }
    }
}
