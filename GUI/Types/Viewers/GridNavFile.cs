using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;

namespace GUI.Types.Viewers
{
    class GridNavFile(VrfGuiContext vrfGuiContext) : IViewer, IDisposable
    {
        private string? infoText;

        public static bool IsAccepted(uint magic)
        {
            return magic == ValveResourceFormat.MapFormats.GridNavFile.MAGIC;
        }

        public async Task LoadAsync(Stream stream)
        {
            var navMeshFile = new ValveResourceFormat.MapFormats.GridNavFile();

            if (stream != null)
            {
                navMeshFile.Read(stream);
            }
            else
            {
                navMeshFile.Read(vrfGuiContext.FileName);
            }

            infoText = navMeshFile.ToString();
        }

        public void Create(TabPage tabOuterPage)
        {
            Debug.Assert(infoText is not null);

            var tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
            };
            tabOuterPage.Controls.Add(tabControl);

            var infoPage = new TabPage("GRID NAV");
            var infoTextControl = CodeTextBox.Create(infoText, CodeTextBox.HighlightLanguage.None);
            infoPage.Controls.Add(infoTextControl);
            tabControl.Controls.Add(infoPage);

            infoText = null;
        }

        public void Dispose()
        {
            //
        }
    }
}
