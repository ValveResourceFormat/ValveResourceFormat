using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Viewers
{
    public class BinaryKeyValues : IViewer
    {
        public TabPage Create(VrfGuiContext vrfGuiContext, byte[] input)
        {
            var tab = new TabPage();
            var kv3 = new BinaryKV3();
            Stream kv3stream;

            if (input != null)
            {
                kv3stream = new MemoryStream(input);
            }
            else
            {
                kv3stream = File.OpenRead(vrfGuiContext.FileName);
            }

            using (var binaryReader = new BinaryReader(kv3stream))
            {
                kv3.Size = (uint)kv3stream.Length;
                kv3.Read(binaryReader, null);
            }

            kv3stream.Close();

            var control = new TextBox();
            control.Font = new Font(FontFamily.GenericMonospace, control.Font.Size);
            control.Text = kv3.ToString();
            control.Dock = DockStyle.Fill;
            control.Multiline = true;
            control.ReadOnly = true;
            control.ScrollBars = ScrollBars.Both;
            tab.Controls.Add(control);

            return tab;
        }
    }
}
