using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.ClosedCaptions;

namespace GUI.Types.Viewers
{
    class ClosedCaptions(VrfGuiContext vrfGuiContext) : IViewer
    {
        private ValveResourceFormat.ClosedCaptions.ClosedCaptions? captions;

        public static bool IsAccepted(uint magic)
        {
            return magic == ValveResourceFormat.ClosedCaptions.ClosedCaptions.MAGIC;
        }

        public async Task LoadAsync(Stream stream)
        {
            captions = new ValveResourceFormat.ClosedCaptions.ClosedCaptions();

            if (stream != null)
            {
                captions.Read(vrfGuiContext.FileName, stream);
            }
            else
            {
                captions.Read(vrfGuiContext.FileName);
            }
        }

        public TabPage Create()
        {
            Debug.Assert(captions is not null);

            var tabOuterPage = new TabPage();
            var tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
            };
            tabOuterPage.Controls.Add(tabControl);

            var tabPage = new TabPage("Captions");
            var control = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                DataSource = new BindingSource(new BindingList<ClosedCaption>(captions.Captions), string.Empty),
                ScrollBars = ScrollBars.Both,
            };
            tabPage.Controls.Add(control);
            tabControl.Controls.Add(tabPage);

            tabPage = new TabPage("Text");
            var textControl = CodeTextBox.Create(captions.ToString());
            tabPage.Controls.Add(textControl);
            tabControl.Controls.Add(tabPage);

            return tabOuterPage;
        }
    }
}
