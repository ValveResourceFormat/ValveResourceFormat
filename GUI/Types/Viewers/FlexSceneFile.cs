using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;

namespace GUI.Types.Viewers
{
    class FlexSceneFile(VrfGuiContext vrfGuiContext) : IViewer
    {
        private string? vfeText;

        public static bool IsAccepted(uint magic)
        {
            return magic == ValveResourceFormat.FlexSceneFile.FlexSceneFile.MAGIC;
        }

        public async Task LoadAsync(Stream stream)
        {
            var vfe = new ValveResourceFormat.FlexSceneFile.FlexSceneFile();

            if (stream != null)
            {
                vfe.Read(stream);
            }
            else
            {
                vfe.Read(vrfGuiContext.FileName);
            }

            vfeText = vfe.ToString();
        }

        public void Create(TabPage tabOuterPage)
        {
            Debug.Assert(vfeText is not null);

            var tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
            };
            tabOuterPage.Controls.Add(tabControl);

            var tabPage = new TabPage("Text");
            var textControl = CodeTextBox.Create(vfeText);
            tabPage.Controls.Add(textControl);
            tabControl.Controls.Add(tabPage);

            vfeText = null;
        }
    }
}
