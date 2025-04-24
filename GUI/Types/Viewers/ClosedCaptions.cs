using System.ComponentModel;
using System.IO;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.ClosedCaptions;

#nullable disable

namespace GUI.Types.Viewers
{
    class ClosedCaptions : IViewer
    {
        public static bool IsAccepted(uint magic)
        {
            return magic == ValveResourceFormat.ClosedCaptions.ClosedCaptions.MAGIC;
        }

        public TabPage Create(VrfGuiContext vrfGuiContext, Stream stream)
        {
            var tabOuterPage = new TabPage();
            var tabControl = new ThemedTabControl
            {
                Dock = DockStyle.Fill,
            };
            tabOuterPage.Controls.Add(tabControl);
            var captions = new ValveResourceFormat.ClosedCaptions.ClosedCaptions();

            if (stream != null)
            {
                captions.Read(vrfGuiContext.FileName, stream);
            }
            else
            {
                captions.Read(vrfGuiContext.FileName);
            }

            var tabPage = new TabPage("Captions");
            var control = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                DataSource = new BindingSource(new BindingList<ClosedCaption>(captions.Captions), null),
                ScrollBars = ScrollBars.Both,
            };
            tabPage.Controls.Add(control);
            tabControl.Controls.Add(tabPage);

            tabPage = new TabPage("Text");
            var textControl = new CodeTextBox(captions.ToString());
            tabPage.Controls.Add(textControl);
            tabControl.Controls.Add(tabPage);

            return tabOuterPage;
        }
    }
}
