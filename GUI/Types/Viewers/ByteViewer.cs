using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Types.Viewers
{
    public class ByteViewer : IViewer
    {
        public static bool IsAccepted() => true;

        public TabPage Create(VrfGuiContext vrfGuiContext, byte[] input)
        {
            var tab = new TabPage();
            var resTabs = new TabControl
            {
                Dock = DockStyle.Fill,
            };
            tab.Controls.Add(resTabs);

            var bvTab = new TabPage("Hex");
            var bv = new System.ComponentModel.Design.ByteViewer
            {
                Dock = DockStyle.Fill,
            };
            bvTab.Controls.Add(bv);
            resTabs.TabPages.Add(bvTab);

            input ??= File.ReadAllBytes(vrfGuiContext.FileName);

            if (!input.Contains<byte>(0x00))
            {
                var textTab = new TabPage("Text");
                var text = new TextBox
                {
                    Dock = DockStyle.Fill,
                    ScrollBars = ScrollBars.Vertical,
                    Multiline = true,
                    ReadOnly = true,
                    Text = Utils.Utils.NormalizeLineEndings(System.Text.Encoding.UTF8.GetString(input)),
                };
                textTab.Controls.Add(text);
                resTabs.TabPages.Add(textTab);
                resTabs.SelectedTab = textTab;
            }

            Program.MainForm.Invoke((MethodInvoker)(() =>
            {
                bv.SetBytes(input);
            }));

            return tab;
        }
    }
}
