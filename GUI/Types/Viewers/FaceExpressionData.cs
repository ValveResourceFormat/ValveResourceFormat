using GUI.Controls;
using GUI.Utils;
using System.IO;
using System.Windows.Forms;

namespace GUI.Types.Viewers
{
    class FaceExpressionData : IViewer
    {
        public static bool IsAccepted(uint magic)
        {
            return magic == ValveResourceFormat.FaceExpressionData.FaceExpressionData.MAGIC;
        }

        public TabPage Create(VrfGuiContext vrfGuiContext, Stream stream)
        {
            var tabOuterPage = new TabPage();
            var tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
            };
            tabOuterPage.Controls.Add(tabControl);
            var vfe = new ValveResourceFormat.FaceExpressionData.FaceExpressionData();

            if (stream != null)
            {
                vfe.Read(stream);
            }
            else
            {
                vfe.Read(vrfGuiContext.FileName);
            }

            var tabPage = new TabPage("Text");
            var textControl = new CodeTextBox(vfe.ToString());
            tabPage.Controls.Add(textControl);
            tabControl.Controls.Add(tabPage);

            return tabOuterPage;
        }
    }
}
