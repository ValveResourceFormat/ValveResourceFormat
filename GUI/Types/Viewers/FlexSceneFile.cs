using System.IO;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;

namespace GUI.Types.Viewers
{
    class FlexSceneFile : IViewer
    {
        public static bool IsAccepted(uint magic)
        {
            return magic == ValveResourceFormat.FlexSceneFile.FlexSceneFile.MAGIC;
        }

        public TabPage Create(VrfGuiContext vrfGuiContext, Stream stream)
        {
            var tabOuterPage = new TabPage();
            var tabControl = new ThemedTabControl
            {
                Dock = DockStyle.Fill,
            };
            tabOuterPage.Controls.Add(tabControl);
            var vfe = new ValveResourceFormat.FlexSceneFile.FlexSceneFile();

            if (stream != null)
            {
                vfe.Read(stream);
            }
            else
            {
                vfe.Read(vrfGuiContext.FileName);
            }

            var tabPage = new TabPage("Text");
            var textControl = CodeTextBox.Create(vfe.ToString());
            tabPage.Controls.Add(textControl);
            tabControl.Controls.Add(tabPage);

            return tabOuterPage;
        }
    }
}
