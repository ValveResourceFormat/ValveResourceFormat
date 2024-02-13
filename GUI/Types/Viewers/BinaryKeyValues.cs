using System.IO;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Viewers
{
    class BinaryKeyValues : IViewer
    {
        public static bool IsAccepted(uint magic) => BinaryKV3.IsBinaryKV3(magic);

        public TabPage Create(VrfGuiContext vrfGuiContext, Stream stream)
        {
            var tab = new TabPage();
            var kv3 = new BinaryKV3();
            Stream kv3stream;

            if (stream != null)
            {
                kv3stream = stream;
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

            var control = new CodeTextBox(kv3.ToString());
            tab.Controls.Add(control);

            return tab;
        }
    }
}
