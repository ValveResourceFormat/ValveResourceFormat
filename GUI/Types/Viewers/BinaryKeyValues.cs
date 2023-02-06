using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Viewers
{
    public class BinaryKeyValues : IViewer
    {
        public static bool IsAccepted(uint magic)
        {
            return magic == BinaryKV3.MAGIC || magic == BinaryKV3.MAGIC2 || magic == BinaryKV3.MAGIC3;
        }

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

            var control = new MonospaceTextBox
            {
                Text = kv3.ToString().ReplaceLineEndings(),
            };
            tab.Controls.Add(control);

            return tab;
        }
    }
}
